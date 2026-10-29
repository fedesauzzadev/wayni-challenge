using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;

namespace Bcra.Ingestion.Application.UploadImport;

public sealed record RequestImportCommand(string Source, string? Etag, long Size, int Periodo);

public sealed record ImportRequestResponse(Guid Id, string Status, bool AlreadyExisted);

public sealed class RequestImportHandler(IImportJobRepository repository, TimeProvider clock)
{
    public async Task<Result<ImportRequestResponse>> HandleAsync(RequestImportCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Source))
            return Result.Fail<ImportRequestResponse>(Error.Validation("ImportJob.SourceRequired", "Source cannot be empty."));
        if (command.Periodo < 190001 || command.Periodo > 299912)
            return Result.Fail<ImportRequestResponse>(Error.Validation("ImportJob.PeriodoInvalid", "Periodo must be a valid yyyyMM value."));
        if (command.Size < 0)
            return Result.Fail<ImportRequestResponse>(Error.Validation("ImportJob.SizeInvalid", "Size cannot be negative."));

        var candidate = ImportJob.Create(command.Source, clock.GetUtcNow());
        candidate.SetSourceMetadata(command.Size, command.Etag);
        candidate.SetPeriodo(command.Periodo);

        var (job, alreadyExisted) = await repository.AddOrGetExistingAsync(candidate, cancellationToken);
        return Result.Ok(new ImportRequestResponse(job.Id, job.Status.ToString(), alreadyExisted));
    }
}
