using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;

namespace Bcra.Ingestion.Application.GetImportStatus;

public sealed record GetImportStatusQuery(Guid JobId);

public sealed record ImportStatusResponse(
    Guid Id,
    string Source,
    string Status,
    int? Periodo,
    long Size,
    long BytesCommitted,
    long LinesProcessed,
    long LinesRejected,
    double? LoadingProgressPercent,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc)
{
    public static ImportStatusResponse From(ImportJob job) => new(
        job.Id,
        job.Source,
        job.Status.ToString(),
        job.Periodo,
        job.Size,
        job.BytesCommitted,
        job.LinesProcessed,
        job.LinesRejected,
        job.Size > 0 ? Math.Round(job.BytesCommitted * 100.0 / job.Size, 2) : null,
        job.StartedAtUtc,
        job.FinishedAtUtc);
}

public sealed class GetImportStatusHandler(IImportJobStore jobStore)
{
    public async Task<Result<ImportStatusResponse>> HandleAsync(GetImportStatusQuery query, CancellationToken cancellationToken)
    {
        var job = await jobStore.GetByIdAsync(query.JobId, cancellationToken);
        return job is null
            ? Result.Fail<ImportStatusResponse>(Error.NotFound("ImportJob.NotFound", $"Import job '{query.JobId}' was not found."))
            : Result.Ok(ImportStatusResponse.From(job));
    }
}
