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
        var batchSkippedControlLines = 0L;
        var reader = new OffsetLineReader(stream, job.BytesCommitted);

        while (await reader.ReadNextAsync(cancellationToken) is { } line)
        {
            switch (DeudoresRecordParser.Classify(line.Line.Span, out var record))
            {
                case DeudoresLineKind.Record:
                    batch.Add(record!);
                    batchValidLines++;
                    break;
                case DeudoresLineKind.ControlRecord:
                    batchSkippedControlLines++;
                    break;
                default:
                    batchRejectedLines++;
                    break;
            }

            if (batch.Count >= batchSize)
            {
                await FlushAsync(job, leaseOwner, batch, line.NextOffset, batchValidLines, batchRejectedLines, batchSkippedControlLines, cancellationToken);
                batchValidLines = 0;
                batchRejectedLines = 0;
                batchSkippedControlLines = 0;
            }
        }

        if (batch.Count > 0 || batchValidLines + batchRejectedLines + batchSkippedControlLines > 0)
            await FlushAsync(job, leaseOwner, batch, reader.Position, batchValidLines, batchRejectedLines, batchSkippedControlLines, cancellationToken);
    }

    private async Task FlushAsync(
        ImportJob job,
        string leaseOwner,
        List<ParsedDebtRecord> batch,
        long nextOffset,
        long validLines,
        long rejectedLines,
        long skippedControlLines,
        CancellationToken cancellationToken)
    {
        job.CommitBatch(nextOffset, validLines, rejectedLines, skippedControlLines);

        await batchCommitter.CommitBatchAsync(
            job.Id,
            batch,
            new StagingBatchCheckpoint(
                job.BytesCommitted, job.LinesProcessed, job.LinesRejected,
                leaseOwner, clock.GetUtcNow(), job.SourceEtag, job.Size, job.LinesSkippedControl),
            cancellationToken);
        batch.Clear();

        pipelineMeter.AddBatchCommitted();
        pipelineMeter.AddLinesCopied(validLines);
        pipelineMeter.AddLinesRejected(rejectedLines);
    }
}
