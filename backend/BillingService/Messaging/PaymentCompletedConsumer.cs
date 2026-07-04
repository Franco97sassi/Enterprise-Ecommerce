using System.Text;
using System.Text.Json;
using BillingService.Data;
using BillingService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BillingService.Messaging;

public sealed class PaymentCompletedConsumer : BackgroundService
{
    private const string QueueName = "billing.payment-completed";
    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentCompletedConsumer> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public PaymentCompletedConsumer(IOptions<RabbitMqOptions> options, IServiceScopeFactory scopeFactory, ILogger<PaymentCompletedConsumer> logger)
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
        await _channel.QueueBindAsync(QueueName, _options.ExchangeName, "payment.completed", cancellationToken: stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;
        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken: stoppingToken);
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            var integrationEvent = JsonSerializer.Deserialize<PaymentCompletedEvent>(Encoding.UTF8.GetString(args.Body.Span));
            if (integrationEvent is null)
            {
                await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var existingBillingRecord = await dbContext.BillingRecords.AnyAsync(record => record.OrderId == integrationEvent.OrderId);
            if (!existingBillingRecord)
            {
                dbContext.BillingRecords.Add(new BillingRecord
                {
                    OrderId = integrationEvent.OrderId,
                    Amount = integrationEvent.Amount,
                    Status = "Paid",
                    IssuedAt = DateTime.UtcNow,
                    PaidAt = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync();
            }

            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment.completed event");
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
