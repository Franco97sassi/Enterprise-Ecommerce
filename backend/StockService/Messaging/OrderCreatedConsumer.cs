using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StockService.Data;
using StockService.Models;

namespace StockService.Messaging;

public sealed class OrderCreatedConsumer : BackgroundService
{
    private const string QueueName = "stock.order-created";
    private const string DeadLetterExchangeName = "enterprise.dead-letter";
    private const string DeadLetterQueueName = "stock.order-created.dlq";

    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public OrderCreatedConsumer(
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderCreatedConsumer> logger)
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

        await _channel.ExchangeDeclareAsync(
            _options.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync(
            DeadLetterExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            DeadLetterQueueName,
            DeadLetterExchangeName,
            QueueName,
            cancellationToken: stoppingToken);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = QueueName
        };

        await _channel.QueueDeclareAsync(
            QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            QueueName,
            _options.ExchangeName,
            "order.created",
            cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(
            0,
            1,
            false,
            stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;

        await _channel.BasicConsumeAsync(
            QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            var json = Encoding.UTF8.GetString(args.Body.Span);
            var order = JsonSerializer.Deserialize<OrderCreatedEvent>(json);
            var correlationId = args.BasicProperties?.CorrelationId ?? Guid.NewGuid().ToString("N");

            if (order is null)
            {
                await _channel!.BasicNackAsync(args.DeliveryTag, false, false);
                return;
            }

            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<StockDbContext>();

            if (await db.OutboxMessages.AnyAsync(message => message.CorrelationId == correlationId))
            {
                await _channel!.BasicAckAsync(args.DeliveryTag, false);
                return;
            }

            var stock = await db.ProductStocks
                .FirstOrDefaultAsync(x => x.Product == order.Product);

            if (stock is null || stock.Available < order.Quantity)
            {
                var stockRejected = new StockRejectedEvent(
                    order.OrderId,
                    order.Product,
                    order.Quantity,
                    "Insufficient stock",
                    DateTime.UtcNow);

                db.OutboxMessages.Add(new OutboxMessage
                {
                    RoutingKey = "stock.rejected",
                    Type = nameof(StockRejectedEvent),
                    Payload = JsonSerializer.Serialize(stockRejected),
                    CorrelationId = correlationId,
                    OccurredAt = stockRejected.OccurredAt
                });

                await db.SaveChangesAsync();
            }
            else
            {
                stock.Available -= order.Quantity;

                var stockReserved = new StockReservedEvent(
                    order.OrderId,
                    order.Product,
                    order.Quantity,
                    order.Total,
                    DateTime.UtcNow);

                db.OutboxMessages.Add(new OutboxMessage
                {
                    RoutingKey = "stock.reserved",
                    Type = nameof(StockReservedEvent),
                    Payload = JsonSerializer.Serialize(stockReserved),
                    CorrelationId = correlationId,
                    OccurredAt = stockReserved.OccurredAt
                });

                await db.SaveChangesAsync();
            }

            await _channel!.BasicAckAsync(args.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order.created");

            await _channel!.BasicNackAsync(
                args.DeliveryTag,
                false,
                true);
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