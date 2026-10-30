using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;

namespace Bcra.Shared.Infrastructure.Aws;

public static class AwsClientFactory
{
    public static IAmazonS3 CreateS3Client(AwsOptions options)
    {
        var config = new AmazonS3Config();
        if (ApplyEndpoint(config, options))
            config.ForcePathStyle = true;

        var credentials = ResolveCredentials(options);
        return credentials is null ? new AmazonS3Client(config) : new AmazonS3Client(credentials, config);
    }

    public static IAmazonSQS CreateSqsClient(AwsOptions options)
    {
        var config = new AmazonSQSConfig();
        ApplyEndpoint(config, options);

        var credentials = ResolveCredentials(options);
        return credentials is null ? new AmazonSQSClient(config) : new AmazonSQSClient(credentials, config);
    }

    private static bool ApplyEndpoint(ClientConfig config, AwsOptions options)
    {
        var hasEndpoint = !string.IsNullOrWhiteSpace(options.EndpointUrl);
        if (hasEndpoint)
            config.ServiceURL = options.EndpointUrl;
        if (!string.IsNullOrWhiteSpace(options.Region))
            config.AuthenticationRegion = options.Region;
        return hasEndpoint;
    }

    private static BasicAWSCredentials? ResolveCredentials(AwsOptions options) =>
        string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey)
            ? null
            : new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
}
