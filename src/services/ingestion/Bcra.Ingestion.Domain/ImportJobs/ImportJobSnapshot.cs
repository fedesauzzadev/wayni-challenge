namespace Bcra.Ingestion.Domain.ImportJobs;

/// <summary>Estado persistido de un <see cref="ImportJob"/> tal como sale del storage.</summary>
public sealed record ImportJobSnapshot(
    Guid Id,
    string Source,
    string? Etag,
    long Size,
    int? Periodo,
    ImportJobStatus Status,
    long BytesCommitted,
    long LinesProcessed,
    long LinesRejected,
    string? LeaseOwner,
    DateTimeOffset? HeartbeatAt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? SourceEtag = null);
