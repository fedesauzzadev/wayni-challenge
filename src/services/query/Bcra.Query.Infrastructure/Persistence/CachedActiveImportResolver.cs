using Bcra.Query.Domain.Ports;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Bcra.Query.Infrastructure.Persistence;

/// <summary>
/// Decorator de <see cref="ActiveImportResolver"/> con caché en memoria de TTL corto
/// (<c>Query:ActiveImportCacheTtlSeconds</c>): ahorra el round-trip a <c>active_import</c> en cada
/// request de query. Trade-off asumido: justo después de rotar el import activo, las lecturas pueden
/// resolver un <c>import_id</c> hasta TTL segundos viejo. TTL 0 deshabilita la caché (bypass directo).
/// </summary>
public sealed class CachedActiveImportResolver(
    IActiveImportResolver inner,
    IMemoryCache cache,
    IOptions<ActiveImportCacheOptions> options) : IActiveImportResolver, IDisposable
{
    private const string CacheKey = "active-import-id";

    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<Guid?> ResolveActiveImportIdAsync(CancellationToken cancellationToken)
    {
        var ttl = TimeSpan.FromSeconds(options.Value.ActiveImportCacheTtlSeconds);
        if (ttl <= TimeSpan.Zero)
            return await inner.ResolveActiveImportIdAsync(cancellationToken);

        if (cache.TryGetValue(CacheKey, out ActiveImportId? cached))
            return cached?.ImportId;

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(CacheKey, out cached))
                return cached?.ImportId;

            var importId = await inner.ResolveActiveImportIdAsync(cancellationToken);
            cache.Set(CacheKey, new ActiveImportId(importId), ttl);
            return importId;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Wrapper para poder cachear también el "sin import activo" (import_id null).</summary>
    private sealed record ActiveImportId(Guid? ImportId);

    public void Dispose() => gate.Dispose();
}
