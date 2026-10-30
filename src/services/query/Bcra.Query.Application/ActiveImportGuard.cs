using Bcra.Query.Domain.Ports;
using Bcra.Shared.Domain.Results;

namespace Bcra.Query.Application;

/// <summary>
/// Bloque único resolver→<see cref="QueryErrors.NoActiveImport"/>: devuelve el import activo
/// o el error 503 que comparten los handlers de query.
/// </summary>
public static class ActiveImportGuard
{
    public static async Task<Result<Guid>> ResolveActiveImportOrFailAsync(
        this IActiveImportResolver resolver,
        CancellationToken cancellationToken)
    {
        var importId = await resolver.ResolveActiveImportIdAsync(cancellationToken);
        return importId is { } active
            ? Result.Ok(active)
            : Result.Fail<Guid>(QueryErrors.NoActiveImport);
    }
}
