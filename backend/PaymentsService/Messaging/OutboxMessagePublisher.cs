using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentsService.Data;
using RabbitMQ.Client;

namespace PaymentsService.Messaging;

public sealed class OutboxMessagePublisher : BackgroundService
{
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
			await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
		}
	}

	private async Task PublishPendingMessagesAsync(CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
		var messages = await dbContext.OutboxMessages
			.Where(message => message.ProcessedAt == null)
			.OrderBy(message => message.OccurredAt)
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
				_logger.LogInformation("Published outbox message {MessageId} with routing key {RoutingKey}", message.Id, message.RoutingKey);
			}
			catch (Exception ex)
			{
				message.RetryCount++;
				message.Error = ex.Message;
				_logger.LogError(ex, "Error publishing outbox message {MessageId}", message.Id);
			}
		}

		await dbContext.SaveChangesAsync(cancellationToken);
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
