namespace Bcra.Ingestion.Application.Ports;

public sealed record OpenedSource(Stream Stream, long TotalLength, string? Etag);

public interface IFileSource : IAsyncDisposable
{
    Task<OpenedSource> OpenAsync(long byteOffset, CancellationToken cancellationToken);
}

public interface IFileSourceFactory
{
    IFileSource Create(string source);
}
