namespace Bcra.Ingestion.Application.Options;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public int BatchSize { get; set; } = 200_000;
    public string SituacionStrategy { get; set; } = "max";
    public IReadOnlyList<string> AllowedSourceDirs { get; set; } = [];
    public IReadOnlyList<string> AllowedS3Buckets { get; set; } = [];
    public int LeaseTimeoutSeconds { get; set; } = 60;
}
