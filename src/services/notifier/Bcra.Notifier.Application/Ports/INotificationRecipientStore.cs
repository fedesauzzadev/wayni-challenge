namespace Bcra.Notifier.Application.Ports;

/// <summary>
/// Fuente de destinatarios de email de "import completado" (tabla <c>notification_recipient</c>).
/// Si está vacía, el notifier cae al <c>Notifications:Email__To</c> de configuración (retrocompatible).
/// </summary>
public interface INotificationRecipientStore
{
    Task<IReadOnlyList<string>> GetRecipientEmailsAsync(CancellationToken cancellationToken);
}
