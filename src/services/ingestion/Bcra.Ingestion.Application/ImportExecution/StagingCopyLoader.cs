using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Domain.Parsing;
using Bcra.Shared.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Application.ImportExecution;

/// <summary>
/// Copia el archivo de deudores a staging por lotes atómicos (lote + checkpoint en la misma
/// transacción), reanudando desde el último <see cref="ImportJob.BytesCommitted"/> confirmado.
/// </summary>
public sealed class StagingCopyLoader(
    IStagingBatchCommitter batchCommitter,
    TimeProvider clock,
    IOptions<IngestionOptions> options,
    ImportPipelineMeter pipelineMeter)
{
    /// <summary>Tope de capacidad inicial del buffer de lote: evita sobre-alocar para BatchSize gigantes.</summary>
    private const int MaxCopyBatchSize = 8192;

    public async Task LoadAsync(ImportJob job, IFileSource source, string leaseOwner, CancellationToken cancellationToken)
    {
        var opened = await source.OpenAsync(job.BytesCommitted, cancellationToken);
        await using var stream = opened.Stream;
        try
        {
            job.ObserveSource(opened.TotalLength, opened.Etag);
        }
        catch (InvalidOperationException ex)
        {
            throw new SourceChangedException(ex.Message, ex);
        }

        var batchSize = Math.Max(1, options.Value.BatchSize);
        var batch = new List<ParsedDebtRecord>(Math.Min(batchSize, MaxCopyBatchSize));
        var batchValidLines = 0L;
        var batchRejectedLines = 0L;
        var reader = new OffsetLineReader(stream, job.BytesCommitted);

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
        job.CommitBatch(nextOffset, validLines, rejectedLines);

        await batchCommitter.CommitBatchAsync(
            job.Id,
            batch,
            new StagingBatchCheckpoint(
                job.BytesCommitted, job.LinesProcessed, job.LinesRejected,
                leaseOwner, clock.GetUtcNow(), job.SourceEtag, job.Size),
            cancellationToken);
        batch.Clear();

        pipelineMeter.AddBatchCommitted();
        pipelineMeter.AddLinesCopied(validLines);
        pipelineMeter.AddLinesRejected(rejectedLines);
    }
}
