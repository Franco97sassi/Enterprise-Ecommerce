using Microsoft.EntityFrameworkCore;
using StockService.Models;

namespace StockService.Data;

public class StockDbContext : DbContext
{
    public StockDbContext(DbContextOptions<StockDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
}