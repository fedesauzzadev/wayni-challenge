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
    IOptions<ActiveImportCacheOptions> options) : IActiveImportResolver
{
    private const string CacheKey = "active-import-id";

    public async Task<Guid?> ResolveActiveImportIdAsync(CancellationToken cancellationToken)
    {
        var ttl = TimeSpan.FromSeconds(options.Value.ActiveImportCacheTtlSeconds);
        if (ttl <= TimeSpan.Zero)
            return await inner.ResolveActiveImportIdAsync(cancellationToken);

        var entry = await cache.GetOrCreateAsync(CacheKey, cacheEntry =>
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = ttl;
            return ResolveAsync(cancellationToken);
        });

        return entry?.ImportId;

        async Task<ActiveImportId> ResolveAsync(CancellationToken ct) =>
            new(await inner.ResolveActiveImportIdAsync(ct));
    }

    /// <summary>Wrapper para poder cachear también el "sin import activo" (import_id null).</summary>
    private sealed record ActiveImportId(Guid? ImportId);
}
