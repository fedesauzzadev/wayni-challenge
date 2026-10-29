namespace Bcra.Notifier.Infrastructure.Notifiers;

public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    public string WebhookUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}
