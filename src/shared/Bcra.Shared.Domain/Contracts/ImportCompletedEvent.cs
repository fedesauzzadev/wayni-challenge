namespace Bcra.Shared.Domain.Contracts;

public sealed record ImportCompletedEvent(
    Guid JobId,
    string Source,
    int? Periodo,
    long LinesProcessed,
    long LinesRejected,
    double DurationMs,
    DateTime CompletedAtUtc);
