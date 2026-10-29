namespace Bcra.Shared.Domain.Contracts;

public sealed record ImportCompletedEvent(
    Guid JobId,
    string Source,
    int? Periodo,
    long LinesProcessed,
    long LinesRejected,
    long TotalDeudores,
    long TotalEntidades,
    decimal SumaTotalPrestamos,
    double DurationMs,
    DateTime CompletedAtUtc);
