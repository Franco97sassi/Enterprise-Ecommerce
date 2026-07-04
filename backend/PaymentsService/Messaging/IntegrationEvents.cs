namespace PaymentsService.Messaging;

public sealed record StockReservedEvent(
    int OrderId,
    string Product,
    int Quantity,
    decimal Total,
    DateTime OccurredAt);

public sealed record PaymentCompletedEvent(
    int OrderId,
    decimal Amount,
    DateTime OccurredAt);
