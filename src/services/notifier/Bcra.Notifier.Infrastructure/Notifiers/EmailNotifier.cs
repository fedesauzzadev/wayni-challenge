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
    IOptions<EmailOptions> emailOptions,
    IOptions<NotificationsOptions> notificationsOptions,
    ILogger<EmailNotifier> logger) : INotifier
{
    public string Strategy => "email";

    public async Task<Result<NotificationReceipt>> NotifyAsync(ImportCompletedEvent importCompleted, CancellationToken cancellationToken)
    {
        var email = emailOptions.Value;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(notificationsOptions.Value.TimeoutSeconds));

            await transport.SendAsync(
                new EmailMessage(email.From, email.To, BuildSubject(importCompleted), BuildBody(importCompleted)),
                timeout.Token);

            LogEmailSent(importCompleted.JobId, email.To);
            return Result.Ok(new NotificationReceipt(Strategy, true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Fail<NotificationReceipt>(Error.Unavailable(
                "Email.Failed",
                $"Email notification failed: {ex.Message}"));
        }
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
        Total deudores: {importCompleted.TotalDeudores}
        Total entidades: {importCompleted.TotalEntidades}
        Suma total prestamos: {importCompleted.SumaTotalPrestamos.ToString(CultureInfo.InvariantCulture)}
        Duracion: {importCompleted.DurationMs.ToString(CultureInfo.InvariantCulture)} ms
        Completado (UTC): {importCompleted.CompletedAtUtc:O}
        """;

    [LoggerMessage(Level = LogLevel.Information, Message = "Email notification sent. JobId={JobId} To={To}")]
    private partial void LogEmailSent(Guid jobId, string to);
}
