using Bcra.Ingestion.Domain.ImportJobs;

namespace Bcra.Ingestion.Application.ImportExecution;

/// <summary>
/// La fuente cambió (ETag o size) respecto del import en curso: reanudarla duplicaría o
/// corrompería datos, así que el job debe terminar <see cref="ImportJobStatus.Failed"/>.
/// </summary>
public sealed class SourceChangedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
