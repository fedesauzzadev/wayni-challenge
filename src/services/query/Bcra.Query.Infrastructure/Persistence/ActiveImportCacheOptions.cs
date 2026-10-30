using System.ComponentModel.DataAnnotations;

namespace Bcra.Query.Infrastructure.Persistence;

/// <summary>
/// Sección <c>Query</c>: TTL de la caché del import activo. 0 deshabilita la caché
/// (una consulta a <c>active_import</c> por request, comportamiento sin decorator).
/// </summary>
public sealed class ActiveImportCacheOptions
{
    public const string SectionName = "Query";

    [Range(0, 3600)]
    public int ActiveImportCacheTtlSeconds { get; set; } = 5;
}
