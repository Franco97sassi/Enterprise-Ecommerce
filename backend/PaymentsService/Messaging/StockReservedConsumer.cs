using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentsService.Data;
using PaymentsService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaymentsService.Messaging;

public sealed class StockReservedConsumer : BackgroundService
{
    private const string QueueName = "payments.stock-reserved";
    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StockReservedConsumer> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public StockReservedConsumer(IOptions<RabbitMqOptions> options, IServiceScopeFactory scopeFactory, ILogger<StockReservedConsumer> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { HostName = _options.Host, UserName = _options.Username, Password = _options.Password };
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(QueueName, _options.ExchangeName, "stock.reserved", cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;
        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken: stoppingToken);
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            var integrationEvent = JsonSerializer.Deserialize<StockReservedEvent>(Encoding.UTF8.GetString(args.Body.Span));
            if (integrationEvent is null)
            {
                await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<RabbitMqPublisher>();

            var payment = await dbContext.Payments.FirstOrDefaultAsync(existingPayment => existingPayment.OrderId == integrationEvent.OrderId);
            if (payment is null)
            {
                payment = new Payment
                {
                    OrderId = integrationEvent.OrderId,
                    Amount = integrationEvent.Total,
                    Status = "Completed",
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Payments.Add(payment);
            }
            else
            {
                payment.Amount = integrationEvent.Total;
                payment.Status = "Completed";
            }

            await dbContext.SaveChangesAsync();
            await publisher.PublishAsync(
                "payment.completed",
                new PaymentCompletedEvent(integrationEvent.OrderId, integrationEvent.Total, DateTime.UtcNow));

            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stock.reserved event");
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
