using System.Text.Json;

namespace OrdersService.Models;

public class OrderEvent
{
    public long Id { get; set; }

    public int OrderId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    public static OrderEvent From<TPayload>(int orderId, string eventType, TPayload payload, string correlationId)
        where TPayload : notnull
    {
        return new OrderEvent
        {
            OrderId = orderId,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            OccurredAt = DateTime.UtcNow,
            CorrelationId = correlationId
        };
    }
}
