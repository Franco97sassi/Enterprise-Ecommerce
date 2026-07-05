using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrdersService.Data;
using RabbitMQ.Client;

namespace OrdersService.Messaging;

public sealed class OutboxMessagePublisher : BackgroundService
{
    private const int MaxRetryCount = 5;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OutboxMessagePublisher> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public OutboxMessagePublisher(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<OutboxMessagePublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PublishPendingMessagesAsync(stoppingToken);
            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task PublishPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var now = DateTime.UtcNow;
        var messages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedAt == null && message.DeadLetteredAt == null && message.NextAttemptAt <= now).OrderBy(message => message.OccurredAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        var channel = await GetChannelAsync(cancellationToken);
        foreach (var message in messages)
        {
            try
            {
                var properties = new BasicProperties
                {
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = message.Id.ToString("N"),
                    CorrelationId = message.CorrelationId,
                    Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredAt).ToUnixTimeSeconds()),
                    Type = message.Type
                };

                await channel.BasicPublishAsync(
                    exchange: _options.ExchangeName,
                    routingKey: message.RoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.Payload),
                    cancellationToken: cancellationToken);

                message.ProcessedAt = DateTime.UtcNow;
                message.Error = null;
                _logger.LogInformation(
                     "Published outbox message {MessageId} with routing key {RoutingKey} and correlation {CorrelationId}",
                     message.Id,
                     message.RoutingKey,
                     message.CorrelationId);
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message;
                if (message.RetryCount >= MaxRetryCount)
                {
                    message.DeadLetteredAt = DateTime.UtcNow;
                    _logger.LogError(ex, "Dead-lettered outbox message {MessageId} after {RetryCount} retries", message.Id, message.RetryCount);
                    continue;
                }

                message.NextAttemptAt = DateTime.UtcNow.Add(CalculateBackoff(message.RetryCount));
                _logger.LogError(ex, "Error publishing outbox message {MessageId}. Retry {RetryCount} scheduled at {NextAttemptAt}", message.Id, message.RetryCount, message.NextAttemptAt);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
    private static TimeSpan CalculateBackoff(int retryCount)
    {
        var seconds = Math.Min(300, Math.Pow(2, retryCount) * 5);
        return TimeSpan.FromSeconds(seconds);
    }
    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            UserName = _options.Username,
            Password = _options.Password
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        return _channel;
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
