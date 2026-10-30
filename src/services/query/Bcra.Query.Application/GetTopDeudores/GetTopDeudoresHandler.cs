using Bcra.Query.Domain.Ports;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;
using Microsoft.Extensions.Options;

namespace Bcra.Query.Application.GetTopDeudores;

public sealed record GetTopDeudoresQuery(int? Top, bool OmitirSinNombre);

public sealed class GetTopDeudoresHandler(
    IActiveImportResolver activeImportResolver,
    IDeudorReadRepository repository,
    IOptions<QueryOptions> options)
{
    public async Task<Result<IReadOnlyList<GetDeudorByCuit.DeudorResumenResponse>>> HandleAsync(GetTopDeudoresQuery query, CancellationToken cancellationToken)
    {
        var topMax = Math.Max(1, options.Value.TopMax);
        if (query.Top is < 1)
        {
            return Result.Fail<IReadOnlyList<GetDeudorByCuit.DeudorResumenResponse>>(
                Error.Validation("Query.InvalidTop", $"'{query.Top}' is not a valid top value; it must be 1 or greater."));
        }

        var importId = await activeImportResolver.ResolveActiveImportIdAsync(cancellationToken);
        if (importId is null)
        {
            return Result.Fail<IReadOnlyList<GetDeudorByCuit.DeudorResumenResponse>>(QueryErrors.NoActiveImport);
        }

        var count = Math.Min(query.Top ?? topMax, topMax);
        var deudores = await repository.GetTopDeudoresAsync(importId.Value, count, query.OmitirSinNombre, cancellationToken);
        return Result.Ok<IReadOnlyList<GetDeudorByCuit.DeudorResumenResponse>>(
            deudores.Select(GetDeudorByCuit.GetDeudorByCuitHandler.ToResponse).ToList());
    }
}
