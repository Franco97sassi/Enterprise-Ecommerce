namespace StockService.Models;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string RoutingKey { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public int RetryCount { get; set; }

    public string? Error { get; set; }
}
