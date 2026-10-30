using Bcra.Ingestion.Application.ImportExecution;

namespace Bcra.Ingestion.Application.Ports;

/// <summary>
/// Puerto estrecho de carga de tablas de referencia: recibe los records ya parseados
/// (sin <c>Stream</c> en la firma); el batching de upserts es detalle del storage.
/// </summary>
public interface IReferenceTableStorage
{
    Task LoadReferenceTablesAsync(
        IAsyncEnumerable<EntidadReference> entidades,
        IAsyncEnumerable<DeudorNombreReference> nombres,
        CancellationToken cancellationToken);
}
