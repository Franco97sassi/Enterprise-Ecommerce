using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
namespace StockService.Messaging;
public sealed class RabbitMqPublisher : IAsyncDisposable
{
    private readonly RabbitMqOptions _options; private readonly ILogger<RabbitMqPublisher> _logger; private IConnection? _connection; private IChannel? _channel;
    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger) { _options = options.Value; _logger = logger; }
    public async Task PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken = default) { var channel = await GetChannelAsync(cancellationToken); var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)); var props = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent, MessageId = Guid.NewGuid().ToString("N"), Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()), Type = typeof(T).Name }; await channel.BasicPublishAsync(_options.ExchangeName, routingKey, false, props, body, cancellationToken); _logger.LogInformation("Published {EventType} with routing key {RoutingKey}", typeof(T).Name, routingKey); }
    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken) { if (_channel is { IsOpen: true }) return _channel; var factory = new ConnectionFactory { HostName = _options.Host, UserName = _options.Username, Password = _options.Password }; _connection = await factory.CreateConnectionAsync(cancellationToken); _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken); await _channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken); return _channel; }
    public async ValueTask DisposeAsync() { if (_channel is not null) await _channel.DisposeAsync(); if (_connection is not null) await _connection.DisposeAsync(); }
}
