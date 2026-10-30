using System.Globalization;
using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Application.Ports;
using Bcra.Notifier.Domain.Notifications;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bcra.Notifier.Infrastructure.Notifiers;

public sealed partial class EmailNotifier(
    ISmtpTransport transport,
    INotificationRecipientStore recipientStore,
    IOptions<EmailOptions> emailOptions,
    IOptions<NotificationsOptions> notificationsOptions,
    ILogger<EmailNotifier> logger) : INotifier
{
    public string Strategy => "email";

    public async Task<Result<NotificationReceipt>> NotifyAsync(ImportCompletedEvent importCompleted, CancellationToken cancellationToken)
    {
        var email = emailOptions.Value;
        var recipients = await ResolveRecipientsAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            LogNoRecipients(importCompleted.JobId);
            return Result.Ok(new NotificationReceipt(Strategy, Delivered: true));
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(notificationsOptions.Value.TimeoutSeconds));

            await transport.SendAsync(
                new EmailMessage(email.From, recipients, BuildSubject(importCompleted), BuildBody(importCompleted)),
                timeout.Token);

            var toDisplay = string.Join(", ", recipients);
            LogEmailSent(importCompleted.JobId, toDisplay);
            return Result.Ok(new NotificationReceipt(Strategy, true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Fail<NotificationReceipt>(Error.Unavailable(
                "Email.Failed",
                $"Email notification failed: {ex.Message}"));
        }
    }

    /// <summary>Destinatarios de la tabla <c>notification_recipient</c>; vacía → fallback a
    /// <c>Notifications:Email__To</c> (retrocompatible con la config pre-admin-UI).</summary>
    private async Task<IReadOnlyList<string>> ResolveRecipientsAsync(CancellationToken cancellationToken)
    {
        var fromStore = await recipientStore.GetRecipientEmailsAsync(cancellationToken);
        if (fromStore.Count > 0)
            return fromStore;

        return string.IsNullOrWhiteSpace(emailOptions.Value.To) ? [] : [emailOptions.Value.To];
    }

    private static string BuildSubject(ImportCompletedEvent importCompleted) =>
        $"Import completado {importCompleted.JobId} ({importCompleted.Periodo?.ToString(CultureInfo.InvariantCulture) ?? "sin periodo"})";

    private static string BuildBody(ImportCompletedEvent importCompleted) =>
        $"""
        Import completado.

        JobId: {importCompleted.JobId}
        Source: {importCompleted.Source}
        Periodo: {importCompleted.Periodo}
        Lineas procesadas: {importCompleted.LinesProcessed}
        Lineas rechazadas: {importCompleted.LinesRejected}
        Registros de control descartados (marcador 98/99): {importCompleted.LinesSkippedControl}
        Duracion: {importCompleted.DurationMs.ToString(CultureInfo.InvariantCulture)} ms
        Completado (UTC): {importCompleted.CompletedAtUtc:O}
        """;

    [LoggerMessage(Level = LogLevel.Information, Message = "Email notification sent. JobId={JobId} To={To}")]
    private partial void LogEmailSent(Guid jobId, string to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email notification skipped: no recipients configured (table notification_recipient empty and Notifications:Email__To blank). JobId={JobId}")]
    private partial void LogNoRecipients(Guid jobId);
}
