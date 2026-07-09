using Microsoft.EntityFrameworkCore;
using PaymentsService.Models;

namespace PaymentsService.Data;

public class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(payment =>
        {
            payment.Property(item => item.Amount)
                .HasPrecision(18, 2);

            payment.Property(item => item.Status)
                .HasMaxLength(50);
        });

        modelBuilder.Entity<OutboxMessage>(outboxMessage =>
        {
            outboxMessage.Property(message => message.RoutingKey)
                .HasMaxLength(200);

            outboxMessage.Property(message => message.Type)
                .HasMaxLength(200);

            outboxMessage.Property(message => message.CorrelationId)
                .HasMaxLength(64);

            outboxMessage.Property(message => message.Error)
                .HasMaxLength(1000);
        });
    }
}