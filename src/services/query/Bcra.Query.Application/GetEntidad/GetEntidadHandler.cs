using Bcra.Query.Domain.Ports;
using Bcra.Query.Domain.ReadModels;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;

namespace Bcra.Query.Application.GetEntidad;

public sealed record GetEntidadQuery(int Codigo);

public sealed record EntidadResumenResponse(int Codigo, string? Nombre, decimal SumaTotalPrestamos);

public sealed class GetEntidadHandler(IActiveImportResolver activeImportResolver, IEntidadReadRepository repository)
{
    public async Task<Result<EntidadResumenResponse>> HandleAsync(GetEntidadQuery query, CancellationToken cancellationToken)
    {
        if (query.Codigo < 1)
        {
            return Result.Fail<EntidadResumenResponse>(
                Error.Validation("Entidad.InvalidCodigo", $"'{query.Codigo}' is not a valid entity code; it must be 1 or greater."));
        }

        var importId = await activeImportResolver.ResolveActiveImportIdAsync(cancellationToken);
        if (importId is null)
        {
            return Result.Fail<EntidadResumenResponse>(QueryErrors.NoActiveImport);
        }

        var entidad = await repository.GetEntidadAsync(importId.Value, query.Codigo, cancellationToken);
        return entidad is null
            ? Result.Fail<EntidadResumenResponse>(
                Error.NotFound("Entidad.NotFound", $"Entity '{query.Codigo}' was not found in the active import."))
            : Result.Ok(ToResponse(entidad));
    }

    private static EntidadResumenResponse ToResponse(EntidadResumen entidad) =>
        new(entidad.Codigo, entidad.Nombre, entidad.SumaTotalPrestamos);
}
