using System.Text.RegularExpressions;
using Bcra.Query.Application.GetEntidad;
using Bcra.Query.Domain.Ports;
using Bcra.Query.Domain.ReadModels;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;

namespace Bcra.Query.Application.GetEntidades;

public sealed record GetEntidadesQuery(string? Codigo);

public sealed record EntidadesPageResponse(long Total, IReadOnlyList<EntidadResumenResponse> Items);

public sealed partial class GetEntidadesHandler(IActiveImportResolver activeImportResolver, IEntidadReadRepository repository)
{
    /// <summary>El filtro de código es un prefijo numérico (parcial) o el código exacto.</summary>
    [GeneratedRegex(@"^\d{1,5}$")]
    private static partial Regex CodigoFilterRegex();

    public async Task<Result<EntidadesPageResponse>> HandleAsync(GetEntidadesQuery query, CancellationToken cancellationToken)
    {
        var codigo = query.Codigo?.Trim();
        if (!string.IsNullOrEmpty(codigo) && !CodigoFilterRegex().IsMatch(codigo))
        {
            return Result.Fail<EntidadesPageResponse>(
                Error.Validation("Query.InvalidEntidadCodigo", $"'{query.Codigo}' is not a valid entity code filter; it must be a numeric prefix."));
        }

        var activeImport = await activeImportResolver.ResolveActiveImportOrFailAsync(cancellationToken);
        if (activeImport.IsFailure)
        {
            return Result.Fail<EntidadesPageResponse>(activeImport.Error);
        }

        var page = await repository.GetEntidadesPageAsync(activeImport.Value, codigo, cancellationToken);
        return Result.Ok(new EntidadesPageResponse(
            page.Total,
            page.Items.Select(entidad => new EntidadResumenResponse(entidad.Codigo, entidad.Nombre, entidad.SumaTotalPrestamos)).ToList()));
    }
}
