namespace Bcra.Ingestion.Infrastructure.Sqs;

public sealed class SqsOptions
{
    public const string SectionName = "Sqs";

    public string ImportCompletedQueueUrl { get; set; } = string.Empty;
}
