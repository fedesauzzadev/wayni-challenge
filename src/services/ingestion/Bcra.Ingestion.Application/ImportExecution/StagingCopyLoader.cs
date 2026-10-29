using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Domain.Parsing;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Application.ImportExecution;

public interface IStagingWriter
{
    Task WriteBatchAsync(Guid importId, IReadOnlyList<ParsedDebtRecord> records, CancellationToken cancellationToken);
}

public sealed class StagingCopyLoader(
    IStagingWriter stagingWriter,
    IImportJobRepository repository,
    TimeProvider clock,
    IOptions<IngestionOptions> options)
{
    public async Task LoadAsync(ImportJob job, IFileSource source, string leaseOwner, CancellationToken cancellationToken)
    {
        var opened = await source.OpenAsync(job.BytesCommitted, cancellationToken);
        await using var stream = opened.Stream;
        job.SetSourceMetadata(opened.TotalLength, job.Etag ?? opened.Etag);

        var batchSize = Math.Max(1, options.Value.BatchSize);
        var batch = new List<ParsedDebtRecord>(Math.Min(batchSize, 8192));
        var batchValidLines = 0L;
        var batchRejectedLines = 0L;
        var reader = new OffsetLineReader(stream);

        while (await reader.ReadNextAsync(cancellationToken) is { } line)
        {
            if (DeudoresRecordParser.TryParse(line.Line.Span, out var record))
            {
                batch.Add(record);
                batchValidLines++;
            }
            else
            {
                batchRejectedLines++;
            }

            if (batch.Count >= batchSize)
            {
                await FlushAsync(job, leaseOwner, batch, line.NextOffset, batchValidLines, batchRejectedLines, cancellationToken);
                batchValidLines = 0;
                batchRejectedLines = 0;
            }
        }

        if (batch.Count > 0 || batchValidLines + batchRejectedLines > 0)
            await FlushAsync(job, leaseOwner, batch, reader.Position, batchValidLines, batchRejectedLines, cancellationToken);
    }

    private async Task FlushAsync(
        ImportJob job,
        string leaseOwner,
        List<ParsedDebtRecord> batch,
        long nextOffset,
        long validLines,
        long rejectedLines,
        CancellationToken cancellationToken)
    {
        if (batch.Count > 0)
        {
            await stagingWriter.WriteBatchAsync(job.Id, batch, cancellationToken);
            batch.Clear();
        }

        job.CommitBatch(nextOffset, validLines, rejectedLines);
        await repository.SaveAsync(job, cancellationToken);
        await repository.RenewLeaseAsync(job.Id, leaseOwner, clock.GetUtcNow(), cancellationToken);
    }
}
