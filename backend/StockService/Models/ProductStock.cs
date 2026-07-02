namespace StockService.Models;

public class ProductStock
{
    public int Id { get; set; }

    public string Product { get; set; } = string.Empty;

    public int Available { get; set; }
}