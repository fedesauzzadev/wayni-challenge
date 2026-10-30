namespace Bcra.Notifier.Application.Ports;

/// <summary>Puerto de envío SMTP: permite testear EmailNotifier sin red real.</summary>
public interface ISmtpTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
