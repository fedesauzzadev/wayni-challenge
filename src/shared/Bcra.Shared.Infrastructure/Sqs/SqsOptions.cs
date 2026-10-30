using System.ComponentModel.DataAnnotations;

namespace Bcra.Shared.Infrastructure.Sqs;

public sealed class SqsOptions
{
    public const string SectionName = "Sqs";

    [Required]
    public string ImportCompletedQueueUrl { get; set; } = string.Empty;

    [Range(0, 20)]
    public int WaitTimeSeconds { get; set; } = 20;

    [Range(1, 10)]
    public int MaxMessages { get; set; } = 10;

    [Range(1, 300)]
    public int PollErrorDelaySeconds { get; set; } = 5;
}
