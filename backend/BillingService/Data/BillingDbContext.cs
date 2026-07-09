using BillingService.Models;
using Microsoft.EntityFrameworkCore;

namespace BillingService.Data;

public class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options)
        : base(options)
    {
    }

    public DbSet<BillingRecord> BillingRecords => Set<BillingRecord>();
}
