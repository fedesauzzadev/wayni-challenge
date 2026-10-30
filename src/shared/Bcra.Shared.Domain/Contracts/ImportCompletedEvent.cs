namespace Bcra.Shared.Domain.Contracts;

public sealed record ImportCompletedEvent(
    Guid JobId,
    string Source,
    int? Periodo,
    long LinesProcessed,
    long LinesRejected,
    long LinesSkippedControl,
    double DurationMs,
    DateTime CompletedAtUtc);
