using System.ComponentModel.DataAnnotations;

namespace Bcra.Notifier.Infrastructure.Notifiers;

public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Único origen del default de timeout del webhook HTTP (segundos).</summary>
    public const int DefaultTimeoutSeconds = 10;

    /// <summary>Vacío = sin webhook: solo notificación vía log estructurado.</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
}
