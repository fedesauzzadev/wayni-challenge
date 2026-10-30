using Bcra.Query.Application.Contracts;
using Bcra.Query.Domain.Ports;
using Bcra.Shared.Domain;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;

namespace Bcra.Query.Application.GetDeudorByCuit;

public sealed record GetDeudorByCuitQuery(string Cuit);

public sealed class GetDeudorByCuitHandler(IActiveImportResolver activeImportResolver, IDeudorReadRepository repository)
{
    public async Task<Result<DeudorResumenResponse>> HandleAsync(GetDeudorByCuitQuery query, CancellationToken cancellationToken)
    {
        if (!CuitParser.TryNormalize(query.Cuit, out var cuit))
        {
            return Result.Fail<DeudorResumenResponse>(
                Error.Validation("Deudor.InvalidCuit", $"'{query.Cuit}' is not a valid CUIT."));
        }

        var activeImport = await activeImportResolver.ResolveActiveImportOrFailAsync(cancellationToken);
        if (activeImport.IsFailure)
        {
            return Result.Fail<DeudorResumenResponse>(activeImport.Error);
        }

        var deudor = await repository.GetDeudorAsync(activeImport.Value, cuit, cancellationToken);
        return deudor is null
            ? Result.Fail<DeudorResumenResponse>(
                Error.NotFound("Deudor.NotFound", $"Deudor '{cuit}' was not found in the active import."))
            : Result.Ok(deudor.ToResponse());
    }
}
