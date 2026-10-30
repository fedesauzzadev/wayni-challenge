using Bcra.Query.Application.Contracts;
using Bcra.Query.Domain.Ports;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;
using Microsoft.Extensions.Options;

namespace Bcra.Query.Application.GetTopDeudores;

public sealed record GetTopDeudoresQuery(int? Offset, int? Limit, int? Top, bool OmitirSinNombre);

public sealed record DeudoresPageResponse(
    long Total,
    int Offset,
    int Limit,
    IReadOnlyList<DeudorResumenResponse> Items);

public sealed class GetTopDeudoresHandler(
    IActiveImportResolver activeImportResolver,
    IDeudorReadRepository repository,
    IOptions<QueryOptions> options)
{
    public async Task<Result<DeudoresPageResponse>> HandleAsync(GetTopDeudoresQuery query, CancellationToken cancellationToken)
    {
        var topMax = Math.Max(1, options.Value.TopMax);
        if (query.Offset is < 0)
        {
            return Result.Fail<DeudoresPageResponse>(
                Error.Validation("Query.InvalidOffset", $"'{query.Offset}' is not a valid offset value; it must be 0 or greater."));
        }

        if (query.Limit is < 1)
        {
            return Result.Fail<DeudoresPageResponse>(
                Error.Validation("Query.InvalidLimit", $"'{query.Limit}' is not a valid limit value; it must be 1 or greater."));
        }

        if (query.Limit is null && query.Top is < 1)
        {
            return Result.Fail<DeudoresPageResponse>(
                Error.Validation("Query.InvalidTop", $"'{query.Top}' is not a valid top value; it must be 1 or greater."));
        }

        var activeImport = await activeImportResolver.ResolveActiveImportOrFailAsync(cancellationToken);
        if (activeImport.IsFailure)
        {
            return Result.Fail<DeudoresPageResponse>(activeImport.Error);
        }

        var offset = query.Offset ?? 0;
        var limit = Math.Min(query.Limit ?? query.Top ?? topMax, topMax);
        var page = await repository.GetDeudoresPageAsync(activeImport.Value, offset, limit, query.OmitirSinNombre, cancellationToken);
        return Result.Ok(new DeudoresPageResponse(
            page.Total,
            offset,
            limit,
            page.Items.Select(deudor => deudor.ToResponse()).ToList()));
    }
}
