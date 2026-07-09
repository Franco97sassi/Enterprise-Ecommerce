using Microsoft.EntityFrameworkCore;
using NotificationsService.Models;

namespace NotificationsService.Data;

public class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.Property(item => item.Recipient)
                .HasMaxLength(256);

            notification.Property(item => item.Subject)
                .HasMaxLength(200);

            notification.Property(item => item.Channel)
                .HasMaxLength(50);

            notification.Property(item => item.Status)
                .HasMaxLength(50);
        });
    }
}
