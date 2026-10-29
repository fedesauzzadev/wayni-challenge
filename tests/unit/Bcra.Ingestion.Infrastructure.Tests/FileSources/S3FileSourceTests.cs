using Amazon.S3;
using Amazon.S3.Model;
using Bcra.Ingestion.Infrastructure.FileSources;
using FluentAssertions;
using NSubstitute;

namespace Bcra.Ingestion.InfrastructureTests.FileSources;

public sealed class S3FileSourceTests
{
    private const string Bucket = "bcra-local";
    private const string Key = "deudores.txt";

    private static (S3FileSource Source, IAmazonS3 S3) CreateSource(long contentLength, string etag)
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetObjectMetadataAsync(Bucket, Key, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GetObjectMetadataResponse { ContentLength = contentLength, ETag = etag }));
        return (new S3FileSource(s3, Bucket, Key), s3);
    }

    [Fact]
    public async Task OpenAsync_ReturnsRealEtag_WithoutQuotes()
    {
        var (source, s3) = CreateSource(100, "\"abc123\"");
        s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(new byte[100]) }));

        var opened = await source.OpenAsync(0, CancellationToken.None);

        opened.Etag.Should().Be("abc123");
        opened.TotalLength.Should().Be(100);
        opened.Stream.Should().NotBeNull();
        await opened.Stream.DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_WithByteOffset_UsesRangeRequest_AndKeepsTotalLength()
    {
        var (source, s3) = CreateSource(100, "\"abc123\"");
        var remaining = new byte[50];
        var response = new GetObjectResponse { ResponseStream = new MemoryStream(remaining) };
        s3.GetObjectAsync(
                Arg.Is<GetObjectRequest>(request => request.BucketName == Bucket && request.Key == Key),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var opened = await source.OpenAsync(50, CancellationToken.None);

        opened.TotalLength.Should().Be(100, "el largo total del objeto, no el fragmento restante");
        await s3.Received(1).GetObjectAsync(
            Arg.Is<GetObjectRequest>(request => request.ByteRange.FormattedByteRange == "bytes=50-"),
            Arg.Any<CancellationToken>());
        await opened.Stream.DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_WithOffsetAtEnd_ReturnsEmptyStream_WithoutFetchingObject()
    {
        var (source, s3) = CreateSource(100, "\"abc123\"");

        var opened = await source.OpenAsync(100, CancellationToken.None);

        opened.TotalLength.Should().Be(100);
        opened.Etag.Should().Be("abc123");
        opened.Stream.Length.Should().Be(0);
        await s3.DidNotReceiveWithAnyArgs().GetObjectAsync(default!, default);
        await opened.Stream.DisposeAsync();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task OpenAsync_WithOffsetOutsideObject_Throws(long byteOffset)
    {
        var (source, _) = CreateSource(100, "\"abc123\"");

        var act = () => source.OpenAsync(byteOffset, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
