namespace OrdersService.Models;

public class Order
{
    public int Id { get; set; }

    public string Customer { get; set; } = string.Empty;

    public string Product { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal Total { get; set; }

    public string Status { get; set; } = "Pending";
}