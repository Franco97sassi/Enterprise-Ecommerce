using Microsoft.EntityFrameworkCore;
using PaymentsService.Data;
using PaymentsService.Models;
using PaymentsService.Messaging;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<StockReservedConsumer>();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new
{
    Service = "PaymentsService",
    Status = "Healthy",
    Timestamp = DateTimeOffset.UtcNow
}));

var payments = app.MapGroup("/payments");

payments.MapGet("/", async (PaymentsDbContext dbContext) =>
    await dbContext.Payments
        .AsNoTracking()
        .OrderByDescending(payment => payment.CreatedAt)
        .ToListAsync());

payments.MapGet("/{id:int}", async (int id, PaymentsDbContext dbContext) =>
{
    var payment = await dbContext.Payments
        .AsNoTracking()
        .FirstOrDefaultAsync(existingPayment => existingPayment.Id == id);

    return payment is null
        ? Results.NotFound()
        : Results.Ok(payment);
});

payments.MapGet("/order/{orderId:int}", async (int orderId, PaymentsDbContext dbContext) =>
    await dbContext.Payments
        .AsNoTracking()
        .Where(payment => payment.OrderId == orderId)
        .OrderByDescending(payment => payment.CreatedAt)
        .ToListAsync());

payments.MapPost("/", async (CreatePaymentRequest request, PaymentsDbContext dbContext) =>
{
    if (request.OrderId <= 0 || request.Amount <= 0)
    {
        return Results.BadRequest(
            "OrderId and amount are required. Amount must be greater than zero.");
    }

    var payment = new Payment
    {
        OrderId = request.OrderId,
        Amount = request.Amount,
        Status = string.IsNullOrWhiteSpace(request.Status)
            ? "Pending"
            : request.Status.Trim(),
        CreatedAt = DateTime.UtcNow
    };

    dbContext.Payments.Add(payment);
    await dbContext.SaveChangesAsync();

    return Results.Created($"/payments/{payment.Id}", payment);
});

payments.MapPut("/{id:int}/status", async (int id, UpdatePaymentStatusRequest request, PaymentsDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Status))
    {
        return Results.BadRequest("Status is required.");
    }

    var payment = await dbContext.Payments.FindAsync(id);

    if (payment is null)
    {
        return Results.NotFound();
    }

    payment.Status = request.Status.Trim();

    await dbContext.SaveChangesAsync();

    return Results.Ok(payment);
});

payments.MapDelete("/{id:int}", async (int id, PaymentsDbContext dbContext) =>
{
    var payment = await dbContext.Payments.FindAsync(id);

    if (payment is null)
    {
        return Results.NotFound();
    }

    dbContext.Payments.Remove(payment);
    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();

public sealed record CreatePaymentRequest(
    int OrderId,
    decimal Amount,
    string? Status);

public sealed record UpdatePaymentStatusRequest(string Status);