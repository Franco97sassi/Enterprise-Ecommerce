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
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingRecord>(billingRecord =>
        {
            billingRecord.Property(record => record.Amount)
                .HasPrecision(18, 2);

            billingRecord.Property(record => record.Status)
                .HasMaxLength(50);
        });
    }

}
