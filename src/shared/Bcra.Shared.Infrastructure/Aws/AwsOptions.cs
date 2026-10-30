namespace Bcra.Shared.Infrastructure.Aws;

public sealed class AwsOptions
{
    public const string SectionName = "AWS";

    public string EndpointUrl { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
}
