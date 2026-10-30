using System.ComponentModel.DataAnnotations;

namespace Bcra.Notifier.Infrastructure.Notifiers;

public sealed class EmailOptions
{
    public const string SectionName = "Notifications:Email";

    /// <summary>Host SMTP. Default Mailpit local del compose; para Gmail: smtp.gmail.com.</summary>
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    /// <summary>STARTTLS (ej. Gmail puerto 587). Mailpit no lo usa.</summary>
    public bool EnableSsl { get; set; }

    /// <summary>Vacío = sin autenticación SMTP (Mailpit). Gmail: tu cuenta de Gmail.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>App-password SMTP (Gmail: 16 caracteres). Solo por variable de entorno, nunca commiteado.</summary>
    public string Password { get; set; } = string.Empty;

    [EmailAddress]
    public string From { get; set; } = "bcra-notifier@localhost";

    /// <summary>Vacío = sin email: solo notificación vía log estructurado (y webhook si está configurado).</summary>
    [EmailAddress]
    public string To { get; set; } = string.Empty;
}
