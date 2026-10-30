using System.Globalization;
using System.Text;
using Bcra.Ingestion.Application.ImportExecution;
using FluentAssertions;
using Xunit;

namespace Bcra.Ingestion.Application.Tests;

public class ReferenceFileParserTests
{
    private static MemoryStream Latin1Stream(params string[] lines)
    {
        var content = string.Join("\n", lines) + "\n";
        return new MemoryStream(Encoding.Latin1.GetBytes(content));
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source) =>
        await source.ToListAsync(CancellationToken.None);

    public class ParseEntidadesAsync : ReferenceFileParserTests
    {
        [Fact]
        public async Task ParsesFixedWidthCodigoAndNombre_WithTrim()
        {
            await using var stream = Latin1Stream("00007ENTIDAD 7          ", "00015BANCO X");

            var entidades = await ToListAsync(ReferenceFileParser.ParseEntidadesAsync(stream, CancellationToken.None));

            entidades.Should().BeEquivalentTo(
            [
                new EntidadReference(7, "ENTIDAD 7"),
                new EntidadReference(15, "BANCO X")
            ]);
        }

        [Fact]
        public async Task SkipsLinesShorterThanSixBytes()
        {
            await using var stream = Latin1Stream("00007", "0", "");

            var entidades = await ToListAsync(ReferenceFileParser.ParseEntidadesAsync(stream, CancellationToken.None));

            entidades.Should().BeEmpty();
        }

        [Theory]
        [InlineData("AB123NOMBRE")]              // código no numérico
        [InlineData("00  7NOMBRE")]              // el parser fixed-width no acepta espacios intercalados
        public async Task SkipsLinesWithNonNumericCodigo(string line)
        {
            await using var stream = Latin1Stream(line);

            var entidades = await ToListAsync(ReferenceFileParser.ParseEntidadesAsync(stream, CancellationToken.None));

            entidades.Should().BeEmpty();
        }

        [Theory]
        [InlineData("45030NARANJA DIGITAL")]
        [InlineData("99999ENTIDAD TOPE")]
        public async Task FiveDigitCodigo_Parses(string line)
        {
            await using var stream = Latin1Stream(line);

            var entidades = await ToListAsync(ReferenceFileParser.ParseEntidadesAsync(stream, CancellationToken.None));

            entidades.Should().ContainSingle().Which.Codigo.Should().Be(int.Parse(line[..5], CultureInfo.InvariantCulture));
        }

        [Fact]
        public async Task DecodesLatin1AccentedNames()
        {
            await using var stream = Latin1Stream("00007ENTIDAD ÁÉÍÓÚÑ");

            var entidades = await ToListAsync(ReferenceFileParser.ParseEntidadesAsync(stream, CancellationToken.None));

            entidades.Should().ContainSingle().Which.Nombre.Should().Be("ENTIDAD ÁÉÍÓÚÑ");
        }

        [Fact]
        public async Task HandlesCrLfLineEndings()
        {
            var content = "00007ENTIDAD A\r\n00011ENTIDAD B\r\n";
            await using var stream = new MemoryStream(Encoding.Latin1.GetBytes(content));

            var entidades = await ToListAsync(ReferenceFileParser.ParseEntidadesAsync(stream, CancellationToken.None));

            entidades.Should().BeEquivalentTo(
            [
                new EntidadReference(7, "ENTIDAD A"),
                new EntidadReference(11, "ENTIDAD B")
            ]);
        }

        [Fact]
        public async Task HandlesFinalLineWithoutNewline()
        {
            var content = "00007ENTIDAD A\n00011ENTIDAD B";
            await using var stream = new MemoryStream(Encoding.Latin1.GetBytes(content));

            var entidades = await ToListAsync(ReferenceFileParser.ParseEntidadesAsync(stream, CancellationToken.None));

            entidades.Should().HaveCount(2);
        }
    }

    public class ParseDeudoresNombresAsync : ReferenceFileParserTests
    {
        [Fact]
        public async Task ParsesFixedWidthCuitAndNombre_WithTrim()
        {
            await using var stream = Latin1Stream("20111111111JUAN PEREZ      ", "27222222222EMPRESA SA");

            var nombres = await ToListAsync(ReferenceFileParser.ParseDeudoresNombresAsync(stream, CancellationToken.None));

            nombres.Should().BeEquivalentTo(
            [
                new DeudorNombreReference("20111111111", "JUAN PEREZ"),
                new DeudorNombreReference("27222222222", "EMPRESA SA")
            ]);
        }

        [Fact]
        public async Task SkipsLinesShorterThanTwelveBytes()
        {
            await using var stream = Latin1Stream("2011111111", "ABC");

            var nombres = await ToListAsync(ReferenceFileParser.ParseDeudoresNombresAsync(stream, CancellationToken.None));

            nombres.Should().BeEmpty();
        }

        [Fact]
        public async Task SkipsLinesWithBlankCuit()
        {
            await using var stream = Latin1Stream("           SIN CUIT");

            var nombres = await ToListAsync(ReferenceFileParser.ParseDeudoresNombresAsync(stream, CancellationToken.None));

            nombres.Should().BeEmpty();
        }

        [Fact]
        public async Task DecodesLatin1AccentedNames()
        {
            await using var stream = Latin1Stream("20111111111MARÍA MUÑOZ");

            var nombres = await ToListAsync(ReferenceFileParser.ParseDeudoresNombresAsync(stream, CancellationToken.None));

            nombres.Should().ContainSingle().Which.Nombre.Should().Be("MARÍA MUÑOZ");
        }
    }
}
