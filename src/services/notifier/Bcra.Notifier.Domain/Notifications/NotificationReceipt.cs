namespace Bcra.Notifier.Domain.Notifications;

public sealed record NotificationReceipt(
    string Strategy,
    bool Delivered,
    string? FailureReason = null);
