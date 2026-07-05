using Microsoft.EntityFrameworkCore;
using StockService.Data;
using StockService.Models;
using StockService.Messaging;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<StockDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxMessagePublisher>();
builder.Services.AddHostedService<OrderCreatedConsumer>();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new
{
    Service = "StockService",
    Status = "Healthy",
    Timestamp = DateTimeOffset.UtcNow
}));

var stock = app.MapGroup("/stock");

stock.MapGet("/", async (StockDbContext dbContext) =>
    await dbContext.ProductStocks
        .AsNoTracking()
        .OrderBy(productStock => productStock.Product)
        .ToListAsync());

stock.MapGet("/{id:int}", async (int id, StockDbContext dbContext) =>
{
    var productStock = await dbContext.ProductStocks
        .AsNoTracking()
        .FirstOrDefaultAsync(existingStock => existingStock.Id == id);

    return productStock is null
        ? Results.NotFound()
        : Results.Ok(productStock);
});

stock.MapGet("/product/{product}", async (string product, StockDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(product))
    {
        return Results.BadRequest("Product is required.");
    }

    var productStock = await dbContext.ProductStocks
        .AsNoTracking()
        .FirstOrDefaultAsync(existingStock => existingStock.Product == product.Trim());

    return productStock is null
        ? Results.NotFound()
        : Results.Ok(productStock);
});

stock.MapPost("/", async (CreateProductStockRequest request, StockDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Product) || request.Available < 0)
    {
        return Results.BadRequest(
            "Product is required and available quantity cannot be negative.");
    }

    var productName = request.Product.Trim();

    var productAlreadyExists = await dbContext.ProductStocks
        .AnyAsync(existingStock => existingStock.Product == productName);

    if (productAlreadyExists)
    {
        return Results.Conflict($"Stock already exists for product '{productName}'.");
    }

    var productStock = new ProductStock
    {
        Product = productName,
        Available = request.Available
    };

    dbContext.ProductStocks.Add(productStock);
    await dbContext.SaveChangesAsync();

    return Results.Created($"/stock/{productStock.Id}", productStock);
});

stock.MapPut("/{id:int}", async (int id, UpdateProductStockRequest request, StockDbContext dbContext) =>
{
    if (request.Available < 0)
    {
        return Results.BadRequest("Available quantity cannot be negative.");
    }

    var productStock = await dbContext.ProductStocks.FindAsync(id);

    if (productStock is null)
    {
        return Results.NotFound();
    }

    if (!string.IsNullOrWhiteSpace(request.Product))
    {
        productStock.Product = request.Product.Trim();
    }

    productStock.Available = request.Available;

    await dbContext.SaveChangesAsync();

    return Results.Ok(productStock);
});

stock.MapPost("/{id:int}/reserve", async (int id, StockMovementRequest request, StockDbContext dbContext) =>
{
    if (request.Quantity <= 0)
    {
        return Results.BadRequest("Quantity must be greater than zero.");
    }

    var productStock = await dbContext.ProductStocks.FindAsync(id);

    if (productStock is null)
    {
        return Results.NotFound();
    }

    if (productStock.Available < request.Quantity)
    {
        return Results.Conflict("Insufficient stock available.");
    }

    productStock.Available -= request.Quantity;

    await dbContext.SaveChangesAsync();

    return Results.Ok(productStock);
});

stock.MapPost("/{id:int}/release", async (int id, StockMovementRequest request, StockDbContext dbContext) =>
{
    if (request.Quantity <= 0)
    {
        return Results.BadRequest("Quantity must be greater than zero.");
    }

    var productStock = await dbContext.ProductStocks.FindAsync(id);

    if (productStock is null)
    {
        return Results.NotFound();
    }

    productStock.Available += request.Quantity;

    await dbContext.SaveChangesAsync();

    return Results.Ok(productStock);
});

stock.MapDelete("/{id:int}", async (int id, StockDbContext dbContext) =>
{
    var productStock = await dbContext.ProductStocks.FindAsync(id);

    if (productStock is null)
    {
        return Results.NotFound();
    }

    dbContext.ProductStocks.Remove(productStock);
    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();

public sealed record CreateProductStockRequest(
    string Product,
    int Available);

public sealed record UpdateProductStockRequest(
    string? Product,
    int Available);

public sealed record StockMovementRequest(int Quantity);