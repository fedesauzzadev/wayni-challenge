using Bcra.Query.Domain.Ports;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;
using Bcra.Shared.Domain.ValueObjects;

namespace Bcra.Query.Application.GetDeudorByCuit;

public sealed record GetDeudorByCuitQuery(string Cuit);

public sealed record DeudorResumenResponse(string Cuit, int SituacionMaxima, decimal SumaTotalPrestamos, string? Nombre);

public sealed class GetDeudorByCuitHandler(IActiveImportResolver activeImportResolver, IDeudorReadRepository repository)
{
    public async Task<Result<DeudorResumenResponse>> HandleAsync(GetDeudorByCuitQuery query, CancellationToken cancellationToken)
    {
        var digits = new string(query.Cuit.Where(char.IsDigit).ToArray());
        if (!Cuit.IsValid(digits))
        {
            return Result.Fail<DeudorResumenResponse>(
                Error.Validation("Deudor.InvalidCuit", $"'{query.Cuit}' is not a valid CUIT."));
        }

        var importId = await activeImportResolver.ResolveActiveImportIdAsync(cancellationToken);
        if (importId is null)
        {
            return Result.Fail<DeudorResumenResponse>(QueryErrors.NoActiveImport);
        }

        var deudor = await repository.GetDeudorAsync(importId.Value, Cuit.FromTrusted(digits), cancellationToken);
        return deudor is null
            ? Result.Fail<DeudorResumenResponse>(
                Error.NotFound("Deudor.NotFound", $"Deudor '{digits}' was not found in the active import."))
            : Result.Ok(ToResponse(deudor));
    }

    internal static DeudorResumenResponse ToResponse(Domain.ReadModels.DeudorResumen deudor) =>
        new(deudor.Cuit.Value, deudor.SituacionMaxima, deudor.SumaTotalPrestamos, deudor.Nombre);
}
