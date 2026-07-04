namespace BillingService.Models;

public class BillingRecord
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public decimal Amount { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PaidAt { get; set; }
}
