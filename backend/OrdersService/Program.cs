using Microsoft.EntityFrameworkCore;
using OrdersService.Data;
using OrdersService.Models;
using OrdersService.Messaging;
using System.Text.Json;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<RabbitMqEventPublisher>();
builder.Services.AddHostedService<OrderSagaOrchestrator>();
builder.Services.AddHostedService<OutboxMessagePublisher>(); 
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
orders.MapGet("/{id:int}/events", async (int id, OrdersDbContext dbContext) =>
    await dbContext.OrderEvents
        .AsNoTracking()
        .Where(orderEvent => orderEvent.OrderId == id)
        .OrderBy(orderEvent => orderEvent.Id)
        .ToListAsync());

orders.MapGet("/{id:int}/projection", async (int id, OrdersDbContext dbContext) =>
{
    var events = await dbContext.OrderEvents
        .AsNoTracking()
        .Where(orderEvent => orderEvent.OrderId == id)
        .OrderBy(orderEvent => orderEvent.Id)
        .ToListAsync();

    if (events.Count == 0)
    {
        return Results.NotFound();
    }

    var projection = OrderProjection.Replay(id, events);
    return Results.Ok(projection);
});
orders.MapPost("/", async (CreateOrderRequest request, OrdersDbContext dbContext, CancellationToken cancellationToken) => {
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
    await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    dbContext.Orders.Add(order);
    await dbContext.SaveChangesAsync(cancellationToken);
    var correlationId = Guid.NewGuid().ToString("N");
    dbContext.OrderSagaStates.Add(new OrderSagaState
    {
        OrderId = order.Id,
        CurrentStep = "OrderCreated",
        Status = "Started",
        StartedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    });
    dbContext.OrderEvents.Add(OrderEvent.From(
      order.Id,
      nameof(OrderCreatedEvent),
      new OrderCreatedSnapshot(order.Id, order.Customer, order.Product, order.Quantity, order.Total, order.Status),
      correlationId));

    var orderCreated = new OrderCreatedEvent(order.Id, order.Customer, order.Product, order.Quantity, order.Total, DateTime.UtcNow);
    dbContext.OutboxMessages.Add(new OutboxMessage
    {
        RoutingKey = "order.created",
        Type = nameof(OrderCreatedEvent),
        Payload = JsonSerializer.Serialize(orderCreated),
        OccurredAt = orderCreated.OccurredAt,
        CorrelationId = correlationId,
    });
    await dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
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
    dbContext.OrderEvents.Add(OrderEvent.From(
       order.Id,
       nameof(OrderStatusChangedEvent),
       new OrderStatusChangedEvent(order.Id, order.Status, DateTime.UtcNow),
       Guid.NewGuid().ToString("N")));
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
    dbContext.OrderEvents.Add(OrderEvent.From(
      order.Id,
      nameof(OrderDeletedEvent),
      new OrderDeletedEvent(order.Id, DateTime.UtcNow),
      Guid.NewGuid().ToString("N")));
    dbContext.Orders.Remove(order);
    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});
var sagas = app.MapGroup("/order-sagas");

sagas.MapGet("/", async (OrdersDbContext dbContext) =>
    await dbContext.OrderSagaStates
        .AsNoTracking()
        .OrderByDescending(saga => saga.UpdatedAt)
        .ToListAsync());

sagas.MapGet("/order/{orderId:int}", async (int orderId, OrdersDbContext dbContext) =>
{
    var saga = await dbContext.OrderSagaStates
        .AsNoTracking()
        .FirstOrDefaultAsync(existingSaga => existingSaga.OrderId == orderId);

    return saga is null
        ? Results.NotFound()
        : Results.Ok(saga);
});
 
 
app.Run();

public sealed record CreateOrderRequest(
    string Customer,
    string Product,
    int Quantity,
    decimal Total,
    string? Status);

public sealed record UpdateOrderStatusRequest(string Status);

public sealed record OrderCreatedSnapshot(int OrderId, string Customer, string Product, int Quantity, decimal Total, string Status);

public sealed record OrderStatusChangedEvent(int OrderId, string Status, DateTime OccurredAt);

public sealed record OrderDeletedEvent(int OrderId, DateTime OccurredAt);

public sealed record OrderProjection(int OrderId, string Customer, string Product, int Quantity, decimal Total, string Status, IReadOnlyList<string> AppliedEvents)
{
    public static OrderProjection Replay(int orderId, IReadOnlyList<OrderEvent> events)
    {
        var projection = new OrderProjection(orderId, string.Empty, string.Empty, 0, 0, "Unknown", []);
        var appliedEvents = new List<string>();

        foreach (var orderEvent in events)
        {
            appliedEvents.Add(orderEvent.EventType);

            if (orderEvent.EventType == nameof(OrderCreatedEvent))
            {
                var snapshot = JsonSerializer.Deserialize<OrderCreatedSnapshot>(orderEvent.Payload);
                if (snapshot is not null)
                {
                    projection = projection with
                    {
                        Customer = snapshot.Customer,
                        Product = snapshot.Product,
                        Quantity = snapshot.Quantity,
                        Total = snapshot.Total,
                        Status = snapshot.Status
                    };
                }
            }
            else if (orderEvent.EventType == nameof(OrderStatusChangedEvent))
            {
                var statusChanged = JsonSerializer.Deserialize<OrderStatusChangedEvent>(orderEvent.Payload);
                if (statusChanged is not null)
                {
                    projection = projection with { Status = statusChanged.Status };
                }
            }
            else if (orderEvent.EventType == nameof(OrderDeletedEvent))
            {
                projection = projection with { Status = "Deleted" };
            }
        }

        return projection with { AppliedEvents = appliedEvents };
    }
}