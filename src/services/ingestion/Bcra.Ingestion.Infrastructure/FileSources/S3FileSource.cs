using Amazon.S3;
using Amazon.S3.Model;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Infrastructure.FileSources;

public sealed class S3FileSource(IAmazonS3 s3, string bucket, string key) : IFileSource
{
    public async Task<OpenedSource> OpenAsync(long byteOffset, CancellationToken cancellationToken)
    {
        var metadata = await s3.GetObjectMetadataAsync(bucket, key, cancellationToken);
        if (byteOffset < 0 || byteOffset > metadata.ContentLength)
            throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "Byte offset is outside the S3 object.");

        if (byteOffset == metadata.ContentLength)
            return new OpenedSource(new MemoryStream(), metadata.ContentLength, NormalizeEtag(metadata.ETag));

        var request = new GetObjectRequest
        {
            BucketName = bucket,
            Key = key,
            ByteRange = new ByteRange($"bytes={byteOffset}-")
        };
        var response = await s3.GetObjectAsync(request, cancellationToken);
        return new OpenedSource(response.ResponseStream, metadata.ContentLength, NormalizeEtag(metadata.ETag));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string? NormalizeEtag(string? etag)
    {
        var trimmed = etag?.Trim('"');
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

public static class S3SourceUri
{
    public static bool TryParse(string source, out string bucket, out string key)
    {
        bucket = string.Empty;
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "s3", StringComparison.OrdinalIgnoreCase))
            return false;

        var parsedBucket = uri.Host;
        var parsedKey = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');

        if (parsedBucket.Length == 0 || parsedKey.Length == 0)
            return false;

        bucket = parsedBucket;
        key = parsedKey;
        return true;
    }
}

public sealed class RoutingFileSourceFactory(
    LocalFileSourceFactory localFactory,
    IAmazonS3 s3,
    IOptions<IngestionOptions> options) : IFileSourceFactory
{
    public IFileSource Create(string source)
    {
        if (S3SourceUri.TryParse(source, out var bucket, out var key))
        {
            if (!options.Value.AllowedS3Buckets.Contains(bucket, StringComparer.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"S3 bucket '{bucket}' is not in the allowed buckets list.");

            return new S3FileSource(s3, bucket, key);
        }

        return localFactory.Create(source);
    }
}
