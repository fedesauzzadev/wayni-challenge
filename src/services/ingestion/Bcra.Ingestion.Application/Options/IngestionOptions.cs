namespace Bcra.Ingestion.Application.Options;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public const string DefaultEntidadesReferenceFile = "Maeent.txt";
    public const string DefaultNombresReferenceFile = "Nomdeu.txt";

    public int BatchSize { get; set; } = 200_000;
    public string SituacionStrategy { get; set; } = "max";
    public IReadOnlyList<string> AllowedSourceDirs { get; set; } = [];
    public IReadOnlyList<string> AllowedS3Buckets { get; set; } = [];
    public int LeaseTimeoutSeconds { get; set; } = 60;
    public int PollDelaySeconds { get; set; } = 5;
    public string? EntidadesReferenceOverride { get; set; }
    public string? NombresReferenceOverride { get; set; }
}
