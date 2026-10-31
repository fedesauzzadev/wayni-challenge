namespace Bcra.Shared.Domain.Contracts;

/// <summary>
/// Evento publicado al completar un import. Los totales (<see cref="TotalDeudores"/>,
/// <see cref="TotalEntidades"/>, <see cref="SumaTotalPrestamos"/>) son opcionales con default 0:
/// se leen precomputados de <c>import_job</c> (una fila) y, si esa lectura falla, el evento
/// sale igual con totales en cero — perder la notificación es peor que notificar sin totales.
/// </summary>
public sealed record ImportCompletedEvent(
    Guid JobId,
    string Source,
    int? Periodo,
    long LinesProcessed,
    long LinesRejected,
    long LinesSkippedControl,
    double DurationMs,
    DateTime CompletedAtUtc,
    long TotalDeudores = 0,
    long TotalEntidades = 0,
    decimal SumaTotalPrestamos = 0);
