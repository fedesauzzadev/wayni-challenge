namespace Bcra.Ingestion.Domain.ImportJobs;

public enum ImportJobStatus
{
    Pending = 1,
    Loading = 2,
    Aggregating = 3,
    Completed = 4,
    Failed = 5
}
