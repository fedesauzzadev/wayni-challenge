using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;

namespace Bcra.Ingestion.Application.GetLatestImport;

public sealed record GetLatestImportQuery;

public sealed record LatestImportResponse(
    Guid Id,
    string Source,
    string Status,
    int? Periodo,
    long Size,
    long BytesCommitted,
    long LinesProcessed,
    long LinesRejected,
    double? LoadingProgressPercent,
    double? DurationMs,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc)
{
    public static LatestImportResponse From(ImportJob job) => new(
        job.Id,
        job.Source,
        job.Status.ToString(),
        job.Periodo,
        job.Size,
        job.BytesCommitted,
        job.LinesProcessed,
        job.LinesRejected,
        job.Size > 0 ? Math.Round(job.BytesCommitted * 100.0 / job.Size, 2) : null,
        job.StartedAtUtc is { } started && job.FinishedAtUtc is { } finished
            ? Math.Round((finished - started).TotalMilliseconds, 0)
            : null,
        job.CreatedAtUtc,
        job.StartedAtUtc,
        job.FinishedAtUtc);
}

public sealed class GetLatestImportHandler(IImportJobStore jobStore)
{
    public async Task<Result<LatestImportResponse>> HandleAsync(GetLatestImportQuery query, CancellationToken cancellationToken)
    {
        var job = await jobStore.GetLatestAsync(cancellationToken);
        return job is null
            ? Result.Fail<LatestImportResponse>(Error.NotFound("ImportJob.NoImports", "No import job has been created yet."))
            : Result.Ok(LatestImportResponse.From(job));
    }
}
