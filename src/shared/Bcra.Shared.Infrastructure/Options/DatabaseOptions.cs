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
}
