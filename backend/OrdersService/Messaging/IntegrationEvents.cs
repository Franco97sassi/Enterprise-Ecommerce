namespace OrdersService.Messaging;

public sealed record OrderCreatedEvent(
	int OrderId,
	string Customer,
	string Product,
	int Quantity,
	decimal Total,
	DateTime OccurredAt);
