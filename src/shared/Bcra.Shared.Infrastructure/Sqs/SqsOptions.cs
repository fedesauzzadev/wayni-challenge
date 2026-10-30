namespace Bcra.Shared.Infrastructure.Sqs;

public sealed class SqsOptions
{
    public const string SectionName = "Sqs";

    public string ImportCompletedQueueUrl { get; set; } = string.Empty;
    public int WaitTimeSeconds { get; set; } = 20;
    public int MaxMessages { get; set; } = 10;
    public int PollErrorDelaySeconds { get; set; } = 5;
}
