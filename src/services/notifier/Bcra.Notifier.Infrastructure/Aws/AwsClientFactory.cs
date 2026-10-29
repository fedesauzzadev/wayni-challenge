using Amazon.Runtime;
using Amazon.SQS;

namespace Bcra.Notifier.Infrastructure.Aws;

public static class AwsClientFactory
{
    public static IAmazonSQS CreateSqsClient(AwsOptions options)
    {
        var config = new AmazonSQSConfig();
        if (!string.IsNullOrWhiteSpace(options.EndpointUrl))
            config.ServiceURL = options.EndpointUrl;
        if (!string.IsNullOrWhiteSpace(options.Region))
            config.AuthenticationRegion = options.Region;

        var credentials = ResolveCredentials(options);
        return credentials is null ? new AmazonSQSClient(config) : new AmazonSQSClient(credentials, config);
    }

    private static BasicAWSCredentials? ResolveCredentials(AwsOptions options) =>
        string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey)
            ? null
            : new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
}
