using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrdersService.Data;
using OrdersService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrdersService.Messaging;

public sealed class OrderSagaOrchestrator : BackgroundService
{
    private const string QueueName = "orders.saga-orchestrator";
    private const string DeadLetterExchangeName = "enterprise.dead-letter";
    private const string DeadLetterQueueName = "orders.saga-orchestrator.dlq";
    private static readonly string[] RoutingKeys = ["stock.reserved", "stock.rejected", "payment.completed"];

    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderSagaOrchestrator> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public OrderSagaOrchestrator(
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderSagaOrchestrator> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            UserName = _options.Username,
            Password = _options.Password
        };

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(DeadLetterExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(DeadLetterQueueName, DeadLetterExchangeName, QueueName, cancellationToken: stoppingToken);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = QueueName
        };

        await _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: queueArguments, cancellationToken: stoppingToken);

        foreach (var routingKey in RoutingKeys)
        {
            await _channel.QueueBindAsync(QueueName, _options.ExchangeName, routingKey, cancellationToken: stoppingToken);
        }

        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;
        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken: stoppingToken);
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            var json = Encoding.UTF8.GetString(args.Body.Span);
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var handled = args.RoutingKey switch
            {
                "stock.reserved" => await MarkStockReservedAsync(dbContext, json),
                "stock.rejected" => await MarkFailedAsync(dbContext, json),
                "payment.completed" => await MarkPaymentCompletedAsync(dbContext, json),
                _ => false
            };

            if (!handled)
            {
                await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error orchestrating order saga from {RoutingKey}", args.RoutingKey);
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private static async Task<OrderSagaState> GetOrCreateSagaAsync(OrdersDbContext dbContext, int orderId)
    {
        var saga = await dbContext.OrderSagaStates.FirstOrDefaultAsync(state => state.OrderId == orderId);
        if (saga.CurrentStep is "StockReserved" or "PaymentCompleted")
        {
            return true;
        }

        if (saga is not null)
        {
            return saga;
        }

        saga = new OrderSagaState { OrderId = orderId };
        dbContext.OrderSagaStates.Add(saga);
        return saga;
    }

    private static async Task<bool> MarkStockReservedAsync(OrdersDbContext dbContext, string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<StockReservedEvent>(json);
        if (integrationEvent is null)
        {
            return false;
        }

        var saga = await GetOrCreateSagaAsync(dbContext, integrationEvent.OrderId);
        if (saga.Status is "Failed" or "Completed")
        {
            return true;
        }

        saga.CurrentStep = "StockReserved";
        saga.Status = "ProcessingPayment";
        saga.UpdatedAt = DateTime.UtcNow;
        await UpdateOrderStatusAsync(dbContext, integrationEvent.OrderId, "StockReserved");
        await dbContext.SaveChangesAsync();
        return true;
    }

    private static async Task<bool> MarkFailedAsync(OrdersDbContext dbContext, string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<StockRejectedEvent>(json);
        if (integrationEvent is null)
        {
            return false;
        }

        var saga = await GetOrCreateSagaAsync(dbContext, integrationEvent.OrderId);
        if (saga.CurrentStep == "PaymentCompleted")
        {
            return true;
        }

        saga.CurrentStep = "StockRejected";
        saga.Status = "Failed";
        saga.FailureReason = integrationEvent.Reason;
        saga.UpdatedAt = DateTime.UtcNow;
        await UpdateOrderStatusAsync(dbContext, integrationEvent.OrderId, "Rejected");
        await dbContext.SaveChangesAsync();
        return true;
    }

    private static async Task<bool> MarkPaymentCompletedAsync(OrdersDbContext dbContext, string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<PaymentCompletedEvent>(json);
        if (integrationEvent is null)
        {
            return false;
        }

        var saga = await GetOrCreateSagaAsync(dbContext, integrationEvent.OrderId);
        saga.CurrentStep = "PaymentCompleted";
        saga.Status = "Completed";
        saga.UpdatedAt = DateTime.UtcNow;
        await UpdateOrderStatusAsync(dbContext, integrationEvent.OrderId, "Completed");
        await dbContext.SaveChangesAsync();
        return true;
    }

    private static async Task UpdateOrderStatusAsync(OrdersDbContext dbContext, int orderId, string status)
    {
        var order = await dbContext.Orders.FindAsync(orderId);
        if (order is not null)
        {
            order.Status = status;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
