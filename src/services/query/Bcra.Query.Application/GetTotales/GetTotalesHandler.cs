using Bcra.Query.Domain.Ports;
using Bcra.Query.Domain.ReadModels;
using Bcra.Shared.Domain.Results;

namespace Bcra.Query.Application.GetTotales;

public sealed record GetTotalesQuery;

public sealed record DeudorTotalesResponse(long CantidadDeudores, long CantidadEntidades, decimal SumaTotalPrestamos);

public sealed class GetTotalesHandler(IActiveImportResolver activeImportResolver, IDeudorReadRepository repository)
{
    public async Task<Result<DeudorTotalesResponse>> HandleAsync(GetTotalesQuery query, CancellationToken cancellationToken)
    {
        var activeImport = await activeImportResolver.ResolveActiveImportOrFailAsync(cancellationToken);
        if (activeImport.IsFailure)
        {
            return Result.Fail<DeudorTotalesResponse>(activeImport.Error);
        }

        var totales = await repository.GetTotalesAsync(activeImport.Value, cancellationToken);
        return Result.Ok(ToResponse(totales));
    }

    private static DeudorTotalesResponse ToResponse(DeudorTotales totales) =>
        new(totales.CantidadDeudores, totales.CantidadEntidades, totales.SumaTotalPrestamos);
}
