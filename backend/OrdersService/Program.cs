using Microsoft.EntityFrameworkCore;
using OrdersService.Data;
using OrdersService.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<OrdersDbContext>(options =>
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
    Service = "OrdersService",
    Status = "Healthy",
    Timestamp = DateTimeOffset.UtcNow
}));

var orders = app.MapGroup("/orders");

orders.MapGet("/", async (OrdersDbContext dbContext) =>
    await dbContext.Orders
        .AsNoTracking()
        .OrderByDescending(order => order.Id)
        .ToListAsync());

orders.MapGet("/{id:int}", async (int id, OrdersDbContext dbContext) =>
{
    var order = await dbContext.Orders
        .AsNoTracking()
        .FirstOrDefaultAsync(existingOrder => existingOrder.Id == id);

    return order is null
        ? Results.NotFound()
        : Results.Ok(order);
});

orders.MapPost("/", async (CreateOrderRequest request, OrdersDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Customer) ||
        string.IsNullOrWhiteSpace(request.Product) ||
        request.Quantity <= 0 ||
        request.Total < 0)
    {
        return Results.BadRequest(
            "Customer, product, quantity and total are required. Quantity must be greater than zero and total cannot be negative.");
    }

    var order = new Order
    {
        Customer = request.Customer.Trim(),
        Product = request.Product.Trim(),
        Quantity = request.Quantity,
        Total = request.Total,
        Status = string.IsNullOrWhiteSpace(request.Status)
            ? "Pending"
            : request.Status.Trim()
    };

    dbContext.Orders.Add(order);
    await dbContext.SaveChangesAsync();

    return Results.Created($"/orders/{order.Id}", order);
});

orders.MapPut("/{id:int}/status", async (int id, UpdateOrderStatusRequest request, OrdersDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Status))
    {
        return Results.BadRequest("Status is required.");
    }

    var order = await dbContext.Orders.FindAsync(id);

    if (order is null)
    {
        return Results.NotFound();
    }

    order.Status = request.Status.Trim();

    await dbContext.SaveChangesAsync();

    return Results.Ok(order);
});

orders.MapDelete("/{id:int}", async (int id, OrdersDbContext dbContext) =>
{
    var order = await dbContext.Orders.FindAsync(id);

    if (order is null)
    {
        return Results.NotFound();
    }

    dbContext.Orders.Remove(order);
    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();

public sealed record CreateOrderRequest(
    string Customer,
    string Product,
    int Quantity,
    decimal Total,
    string? Status);

public sealed record UpdateOrderStatusRequest(string Status);