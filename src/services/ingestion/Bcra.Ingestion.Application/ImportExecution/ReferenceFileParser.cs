using System.Runtime.CompilerServices;
using System.Text;
using Bcra.Ingestion.Domain.Parsing;

namespace Bcra.Ingestion.Application.ImportExecution;

public sealed record EntidadReference(short Codigo, string Nombre);

public sealed record DeudorNombreReference(string Cuit, string Nombre);

/// <summary>
/// Parser fixed-width de los archivos de referencia: Maeent.txt (código 5 bytes + nombre) y
/// Nomdeu.txt (CUIT 11 bytes + nombre). Descarta líneas cortas y códigos no numéricos;
/// decodifica Latin1 y recorta espacios del nombre.
/// </summary>
public static class ReferenceFileParser
{
    public static async IAsyncEnumerable<EntidadReference> ParseEntidadesAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = new OffsetLineReader(stream);
        while (await reader.ReadNextAsync(cancellationToken) is { } line)
        {
            var span = line.Line.Span;
            if (span.Length < 6)
                continue;
            if (!FixedWidthDigits.TryParse(span[..5], maxLength: 5, short.MaxValue, out var codigo))
                continue;

            yield return new EntidadReference((short)codigo, Encoding.Latin1.GetString(span[5..]).Trim());
        }
    }

    public static async IAsyncEnumerable<DeudorNombreReference> ParseDeudoresNombresAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = new OffsetLineReader(stream);
        while (await reader.ReadNextAsync(cancellationToken) is { } line)
        {
            var span = line.Line.Span;
            if (span.Length < 12)
                continue;

            var cuit = Encoding.Latin1.GetString(span[..11]).Trim();
            if (cuit.Length == 0 || cuit.Length > 11)
                continue;

            yield return new DeudorNombreReference(cuit, Encoding.Latin1.GetString(span[11..]).Trim());
        }
    }
}
