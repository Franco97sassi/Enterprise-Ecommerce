namespace BillingService.Messaging;

public sealed record PaymentCompletedEvent(int OrderId, decimal Amount, DateTime OccurredAt);
