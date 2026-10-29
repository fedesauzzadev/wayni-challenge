using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Infrastructure.FileSources;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Amazon.S3;

namespace Bcra.Ingestion.InfrastructureTests.FileSources;

public sealed class RoutingFileSourceFactoryTests
{
    private static readonly string LocalRoot = Path.GetTempPath();

    private static RoutingFileSourceFactory CreateFactory(IReadOnlyList<string> allowedBuckets, IReadOnlyList<string> allowedDirs)
    {
        var options = Options.Create(new IngestionOptions
        {
            AllowedS3Buckets = allowedBuckets,
            AllowedSourceDirs = allowedDirs
        });
        return new RoutingFileSourceFactory(new LocalFileSourceFactory(options), Substitute.For<IAmazonS3>(), options);
    }

    [Fact]
    public void S3Source_WithAllowedBucket_RoutesToS3FileSource()
    {
        var sut = CreateFactory(["bcra-local"], [LocalRoot]);

        var source = sut.Create("s3://bcra-local/deudores.txt");

        source.Should().BeOfType<S3FileSource>();
    }

    [Fact]
    public void S3Source_WithBucketOutsideAllowList_Throws()
    {
        var sut = CreateFactory(["bcra-local"], [LocalRoot]);

        var act = () => sut.Create("s3://evil-bucket/deudores.txt");

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*evil-bucket*");
    }

    [Fact]
    public void S3Source_WithEmptyAllowList_Throws()
    {
        var sut = CreateFactory([], [LocalRoot]);

        var act = () => sut.Create("s3://bcra-local/deudores.txt");

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void NonS3Source_RoutesToLocalFileSource()
    {
        var sut = CreateFactory(["bcra-local"], [LocalRoot]);

        var source = sut.Create(Path.Combine(LocalRoot, "deudores.txt"));

        source.Should().BeOfType<LocalFileSource>();
    }

    [Fact]
    public void NonS3Source_OutsideAllowedDirs_StillThrows()
    {
        var sut = CreateFactory(["bcra-local"], [LocalRoot]);

        var act = () => sut.Create(Path.Combine(Path.GetPathRoot(Path.GetFullPath("/other"))!, "deudores.txt"));

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("s3://bucket/key.txt", true, "bucket", "key.txt")]
    [InlineData("s3://bucket/path/to/key.txt", true, "bucket", "path/to/key.txt")]
    [InlineData("s3://bucket/key with spaces.txt", true, "bucket", "key with spaces.txt")]
    [InlineData("s3://bucket/key%20with%20spaces.txt", true, "bucket", "key with spaces.txt")]
    [InlineData("s3://bucket", false, "", "")]
    [InlineData("s3://bucket/", false, "", "")]
    [InlineData("/data/deudores.txt", false, "", "")]
    [InlineData("C:\\data\\deudores.txt", false, "", "")]
    [InlineData("", false, "", "")]
    public void TryParse_Behavior(string source, bool expectedParse, string expectedBucket, string expectedKey)
    {
        var parsed = S3SourceUri.TryParse(source, out var bucket, out var key);

        parsed.Should().Be(expectedParse);
        bucket.Should().Be(expectedBucket);
        key.Should().Be(expectedKey);
    }
}
