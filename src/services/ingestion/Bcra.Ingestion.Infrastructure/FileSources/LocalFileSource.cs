using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Infrastructure.FileSources;

public sealed class LocalFileSource(string fullPath) : IFileSource
{
    public async Task<OpenedSource> OpenAsync(long byteOffset, CancellationToken cancellationToken)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("Source file not found.", fullPath);
        if (byteOffset < 0 || byteOffset > info.Length)
            throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "Byte offset is outside the source file.");

        cancellationToken.ThrowIfCancellationRequested();
        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1 << 16,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Seek(byteOffset, SeekOrigin.Begin);

        var etag = $"local:size-{info.Length}:mtime-{info.LastWriteTimeUtc.Ticks:x16}";
        return new OpenedSource(stream, info.Length, etag);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class LocalFileSourceFactory(IOptions<IngestionOptions> options) : IFileSourceFactory
{
    public IFileSource Create(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source cannot be empty.", nameof(source));

        var allowedDirs = options.Value.AllowedSourceDirs;
        if (allowedDirs.Count == 0)
            throw new UnauthorizedAccessException("No allowed source directories configured.");

        var fullPath = Path.GetFullPath(source);
        foreach (var allowedDir in allowedDirs)
        {
            if (IsUnderRoot(fullPath, allowedDir))
                return new LocalFileSource(fullPath);
        }

        throw new UnauthorizedAccessException($"Source '{source}' is outside the allowed source directories.");
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison)
            || string.Equals(normalizedPath, normalizedRoot, comparison);
    }
}
