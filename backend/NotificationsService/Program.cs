using Microsoft.EntityFrameworkCore;
using NotificationsService.Data;
using NotificationsService.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new
{
    Service = "NotificationsService",
    Status = "Healthy",
    Timestamp = DateTimeOffset.UtcNow
}));

var notifications = app.MapGroup("/notifications");

notifications.MapGet("/", async (NotificationsDbContext dbContext) =>
    await dbContext.Notifications
        .AsNoTracking()
        .OrderByDescending(notification => notification.CreatedAt)
        .ToListAsync());

notifications.MapGet("/{id:int}", async (int id, NotificationsDbContext dbContext) =>
{
    var notification = await dbContext.Notifications
        .AsNoTracking()
        .FirstOrDefaultAsync(existingNotification => existingNotification.Id == id);

    return notification is null
        ? Results.NotFound()
        : Results.Ok(notification);
});

notifications.MapGet("/order/{orderId:int}", async (int orderId, NotificationsDbContext dbContext) =>
    await dbContext.Notifications
        .AsNoTracking()
        .Where(notification => notification.OrderId == orderId)
        .OrderByDescending(notification => notification.CreatedAt)
        .ToListAsync());

notifications.MapPost("/", async (CreateNotificationRequest request, NotificationsDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Recipient) ||
        string.IsNullOrWhiteSpace(request.Subject) ||
        string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest("Recipient, subject and message are required.");
    }

    var notification = new Notification
    {
        OrderId = request.OrderId,
        Recipient = request.Recipient.Trim(),
        Subject = request.Subject.Trim(),
        Message = request.Message.Trim(),
        Channel = string.IsNullOrWhiteSpace(request.Channel)
            ? "Email"
            : request.Channel.Trim(),
        Status = string.IsNullOrWhiteSpace(request.Status)
            ? "Pending"
            : request.Status.Trim(),
        CreatedAt = DateTime.UtcNow,
        SentAt = request.SentAt
    };

    dbContext.Notifications.Add(notification);
    await dbContext.SaveChangesAsync();

    return Results.Created($"/notifications/{notification.Id}", notification);
});

notifications.MapPut("/{id:int}/status", async (int id, UpdateNotificationStatusRequest request, NotificationsDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Status))
    {
        return Results.BadRequest("Status is required.");
    }

    var notification = await dbContext.Notifications.FindAsync(id);

    if (notification is null)
    {
        return Results.NotFound();
    }

    notification.Status = request.Status.Trim();
    notification.SentAt = request.SentAt;

    await dbContext.SaveChangesAsync();

    return Results.Ok(notification);
});

notifications.MapPost("/{id:int}/mark-sent", async (int id, NotificationsDbContext dbContext) =>
{
    var notification = await dbContext.Notifications.FindAsync(id);

    if (notification is null)
    {
        return Results.NotFound();
    }

    notification.Status = "Sent";
    notification.SentAt = DateTime.UtcNow;

    await dbContext.SaveChangesAsync();

    return Results.Ok(notification);
});

notifications.MapDelete("/{id:int}", async (int id, NotificationsDbContext dbContext) =>
{
    var notification = await dbContext.Notifications.FindAsync(id);

    if (notification is null)
    {
        return Results.NotFound();
    }

    dbContext.Notifications.Remove(notification);
    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();

public sealed record CreateNotificationRequest(
    int? OrderId,
    string Recipient,
    string Subject,
    string Message,
    string? Channel,
    string? Status,
    DateTime? SentAt);

public sealed record UpdateNotificationStatusRequest(
    string Status,
    DateTime? SentAt);
