using BillingService.Data;
using BillingService.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<BillingDbContext>(options =>
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
    Service = "BillingService",
    Status = "Healthy",
    Timestamp = DateTimeOffset.UtcNow
}));

var billing = app.MapGroup("/billing");

billing.MapGet("/", async (BillingDbContext dbContext) =>
    await dbContext.BillingRecords
        .AsNoTracking()
        .OrderByDescending(billingRecord => billingRecord.IssuedAt)
        .ToListAsync());

billing.MapGet("/{id:int}", async (int id, BillingDbContext dbContext) =>
{
    var billingRecord = await dbContext.BillingRecords
        .AsNoTracking()
        .FirstOrDefaultAsync(existingBillingRecord => existingBillingRecord.Id == id);

    return billingRecord is null
        ? Results.NotFound()
        : Results.Ok(billingRecord);
});

billing.MapGet("/order/{orderId:int}", async (int orderId, BillingDbContext dbContext) =>
    await dbContext.BillingRecords
        .AsNoTracking()
        .Where(billingRecord => billingRecord.OrderId == orderId)
        .OrderByDescending(billingRecord => billingRecord.IssuedAt)
        .ToListAsync());

billing.MapPost("/", async (CreateBillingRecordRequest request, BillingDbContext dbContext) =>
{
    if (request.OrderId <= 0 || request.Amount <= 0)
    {
        return Results.BadRequest(
            "OrderId and amount are required. Amount must be greater than zero.");
    }

    var billingRecord = new BillingRecord
    {
        OrderId = request.OrderId,
        Amount = request.Amount,
        Status = string.IsNullOrWhiteSpace(request.Status)
            ? "Pending"
            : request.Status.Trim(),
        IssuedAt = DateTime.UtcNow,
        PaidAt = request.PaidAt
    };

    dbContext.BillingRecords.Add(billingRecord);
    await dbContext.SaveChangesAsync();

    return Results.Created($"/billing/{billingRecord.Id}", billingRecord);
});

billing.MapPut("/{id:int}/status", async (int id, UpdateBillingStatusRequest request, BillingDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Status))
    {
        return Results.BadRequest("Status is required.");
    }

    var billingRecord = await dbContext.BillingRecords.FindAsync(id);

    if (billingRecord is null)
    {
        return Results.NotFound();
    }

    billingRecord.Status = request.Status.Trim();
    billingRecord.PaidAt = request.PaidAt;

    await dbContext.SaveChangesAsync();

    return Results.Ok(billingRecord);
});

billing.MapPost("/{id:int}/mark-paid", async (int id, BillingDbContext dbContext) =>
{
    var billingRecord = await dbContext.BillingRecords.FindAsync(id);

    if (billingRecord is null)
    {
        return Results.NotFound();
    }

    billingRecord.Status = "Paid";
    billingRecord.PaidAt = DateTime.UtcNow;

    await dbContext.SaveChangesAsync();

    return Results.Ok(billingRecord);
});

billing.MapDelete("/{id:int}", async (int id, BillingDbContext dbContext) =>
{
    var billingRecord = await dbContext.BillingRecords.FindAsync(id);

    if (billingRecord is null)
    {
        return Results.NotFound();
    }

    dbContext.BillingRecords.Remove(billingRecord);
    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();

public sealed record CreateBillingRecordRequest(
    int OrderId,
    decimal Amount,
    string? Status,
    DateTime? PaidAt);

public sealed record UpdateBillingStatusRequest(
    string Status,
    DateTime? PaidAt);