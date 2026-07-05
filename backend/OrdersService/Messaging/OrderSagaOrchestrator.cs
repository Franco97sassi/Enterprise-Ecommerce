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

    private const string StockReservedRoutingKey = "stock.reserved";
    private const string StockRejectedRoutingKey = "stock.rejected";
    private const string PaymentCompletedRoutingKey = "payment.completed";

    private const string StockReservedStep = "StockReserved";
    private const string StockRejectedStep = "StockRejected";
    private const string PaymentCompletedStep = "PaymentCompleted";

    private const string StartedStatus = "Started";
    private const string ProcessingPaymentStatus = "ProcessingPayment";
    private const string FailedStatus = "Failed";
    private const string CompletedStatus = "Completed";

    private const string StockReservedOrderStatus = "StockReserved";
    private const string RejectedOrderStatus = "Rejected";
    private const string CompletedOrderStatus = "Completed";

    private const string OrderStatusChangedEventType = "OrderStatusChangedEvent";

    private static readonly string[] RoutingKeys =
    [
        StockReservedRoutingKey,
        StockRejectedRoutingKey,
        PaymentCompletedRoutingKey
    ];

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

        await DeclareTopologyAsync(_channel, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;

        await _channel.BasicConsumeAsync(
            QueueName,
            autoAck: false,
            consumer,
            cancellationToken: stoppingToken);
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        if (_channel is null)
        {
            _logger.LogWarning("Order saga message received before RabbitMQ channel was initialized");
            return;
        }

        try
        {
            var json = Encoding.UTF8.GetString(args.Body.Span);
            var correlationId = args.BasicProperties?.CorrelationId ?? Guid.NewGuid().ToString("N");

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var handled = args.RoutingKey switch
            {
                StockReservedRoutingKey => await MarkStockReservedAsync(dbContext, json, correlationId),
                StockRejectedRoutingKey => await MarkFailedAsync(dbContext, json, correlationId),
                PaymentCompletedRoutingKey => await MarkPaymentCompletedAsync(dbContext, json, correlationId),
                _ => false
            };

            if (handled)
            {
                await _channel.BasicAckAsync(args.DeliveryTag, multiple: false);
                return;
            }

            await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error orchestrating order saga from {RoutingKey}", args.RoutingKey);
            await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            _options.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            DeadLetterExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            DeadLetterQueueName,
            DeadLetterExchangeName,
            QueueName,
            cancellationToken: cancellationToken);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = QueueName
        };

        await channel.QueueDeclareAsync(
            QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: cancellationToken);

        foreach (var routingKey in RoutingKeys)
        {
            await channel.QueueBindAsync(
                QueueName,
                _options.ExchangeName,
                routingKey,
                cancellationToken: cancellationToken);
        }

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: cancellationToken);
    }

    private static async Task<OrderSagaState> GetOrCreateSagaAsync(OrdersDbContext dbContext, int orderId)
    {
        var saga = await dbContext.OrderSagaStates
            .FirstOrDefaultAsync(state => state.OrderId == orderId);

        if (saga is not null)
        {
            return saga;
        }

        saga = new OrderSagaState
        {
            OrderId = orderId,
            CurrentStep = "OrderCreated",
            Status = StartedStatus,
            StartedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.OrderSagaStates.Add(saga);
        return saga;
    }

    private static async Task<bool> MarkStockReservedAsync(
        OrdersDbContext dbContext,
        string json,
        string correlationId)
    {
        var integrationEvent = JsonSerializer.Deserialize<StockReservedEvent>(json);
        if (integrationEvent is null)
        {
            return false;
        }

        var saga = await GetOrCreateSagaAsync(dbContext, integrationEvent.OrderId);
        if (IsTerminal(saga))
        {
            return true;
        }

        saga.CurrentStep = StockReservedStep;
        saga.Status = ProcessingPaymentStatus;
        saga.FailureReason = null;
        saga.UpdatedAt = DateTime.UtcNow;

        await UpdateOrderStatusAsync(
            dbContext,
            integrationEvent.OrderId,
            StockReservedOrderStatus,
            nameof(StockReservedEvent),
            correlationId);

        await dbContext.SaveChangesAsync();
        return true;
    }

    private static async Task<bool> MarkFailedAsync(
        OrdersDbContext dbContext,
        string json,
        string correlationId)
    {
        var integrationEvent = JsonSerializer.Deserialize<StockRejectedEvent>(json);
        if (integrationEvent is null)
        {
            return false;
        }

        var saga = await GetOrCreateSagaAsync(dbContext, integrationEvent.OrderId);
        if (saga.Status == CompletedStatus)
        {
            return true;
        }

        saga.CurrentStep = StockRejectedStep;
        saga.Status = FailedStatus;
        saga.FailureReason = integrationEvent.Reason;
        saga.UpdatedAt = DateTime.UtcNow;

        await UpdateOrderStatusAsync(
            dbContext,
            integrationEvent.OrderId,
            RejectedOrderStatus,
            nameof(StockRejectedEvent),
            correlationId);

        await dbContext.SaveChangesAsync();
        return true;
    }

    private static async Task<bool> MarkPaymentCompletedAsync(
        OrdersDbContext dbContext,
        string json,
        string correlationId)
    {
        var integrationEvent = JsonSerializer.Deserialize<PaymentCompletedEvent>(json);
        if (integrationEvent is null)
        {
            return false;
        }

        var saga = await GetOrCreateSagaAsync(dbContext, integrationEvent.OrderId);
        if (saga.Status == FailedStatus)
        {
            return true;
        }

        saga.CurrentStep = PaymentCompletedStep;
        saga.Status = CompletedStatus;
        saga.FailureReason = null;
        saga.UpdatedAt = DateTime.UtcNow;

        await UpdateOrderStatusAsync(
            dbContext,
            integrationEvent.OrderId,
            CompletedOrderStatus,
            nameof(PaymentCompletedEvent),
            correlationId);

        await dbContext.SaveChangesAsync();
        return true;
    }

    private static bool IsTerminal(OrderSagaState saga) =>
        saga.Status is FailedStatus or CompletedStatus;

    private static async Task UpdateOrderStatusAsync(
        OrdersDbContext dbContext,
        int orderId,
        string status,
        string sourceEventType,
        string correlationId)
    {
        var order = await dbContext.Orders.FindAsync(orderId);
        if (order is null)
        {
            return;
        }

        order.Status = status;
        dbContext.OrderEvents.Add(OrderEvent.From(
            orderId,
            OrderStatusChangedEventType,
            new
            {
                OrderId = orderId,
                Status = status,
                SourceEventType = sourceEventType,
                OccurredAt = DateTime.UtcNow
            },
            correlationId));
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