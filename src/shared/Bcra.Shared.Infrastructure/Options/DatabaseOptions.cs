using System.ComponentModel.DataAnnotations;

namespace Bcra.Shared.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;
    public string? ReadonlyPassword { get; set; }

    [Range(1, 100)]
    public int MigrationRetryCount { get; set; } = 10;

    [Range(1, 300)]
    public int MigrationRetryDelaySeconds { get; set; } = 3;

    /// <summary>
    /// Command timeout de los scripts de migración: los ALTER TABLE que reescriben tablas
    /// grandes (p. ej. smallint→integer sobre staging con millones de filas) superan el
    /// default de 30s de Npgsql y crasheaban el arranque.
    /// </summary>
    [Range(30, 3600)]
    public int MigrationCommandTimeoutSeconds { get; set; } = 900;
}
