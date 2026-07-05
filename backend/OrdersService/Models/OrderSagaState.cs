namespace OrdersService.Models;

public class OrderSagaState
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string CurrentStep { get; set; } = "OrderCreated";

    public string Status { get; set; } = "Started";

    public string? FailureReason { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
