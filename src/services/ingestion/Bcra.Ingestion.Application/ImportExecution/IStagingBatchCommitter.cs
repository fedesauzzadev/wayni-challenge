using Bcra.Ingestion.Domain.Parsing;

namespace Bcra.Ingestion.Application.ImportExecution;

/// <summary>
/// Checkpoint atómico de un lote: el COPY a staging y el avance de <c>bytes_committed</c>
/// (junto con el size y el source_etag observados y el heartbeat del lease) se confirman en
/// una única transacción READ COMMITTED.
/// </summary>
public sealed record StagingBatchCheckpoint(
    long BytesCommitted,
    long LinesProcessed,
    long LinesRejected,
    string LeaseOwner,
    DateTimeOffset HeartbeatAt,
    string? SourceEtag,
    long Size,
    long LinesSkippedControl = 0);

public interface IStagingBatchCommitter
{
    Task CommitBatchAsync(
        Guid importId,
        IReadOnlyList<ParsedDebtRecord> records,
        StagingBatchCheckpoint checkpoint,
        CancellationToken cancellationToken);
}
