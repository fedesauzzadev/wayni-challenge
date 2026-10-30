using Bcra.Notifier.Application.Ports;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Bcra.Notifier.Infrastructure.Notifiers;

public sealed class MailKitSmtpTransport(IOptions<EmailOptions> options) : ISmtpTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var email = options.Value;

        using var client = new SmtpClient();
        await client.ConnectAsync(
            email.Host,
            email.Port,
            email.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
            cancellationToken);

        if (!string.IsNullOrEmpty(email.Username))
        {
            await client.AuthenticateAsync(email.Username, email.Password, cancellationToken);
        }

        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(message.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart(TextFormat.Plain) { Text = message.Body };

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
