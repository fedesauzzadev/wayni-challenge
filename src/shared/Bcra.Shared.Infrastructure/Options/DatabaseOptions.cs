namespace Bcra.Shared.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;
    public string? ReadonlyPassword { get; set; }
    public int MigrationRetryCount { get; set; } = 10;
    public int MigrationRetryDelaySeconds { get; set; } = 3;
}
