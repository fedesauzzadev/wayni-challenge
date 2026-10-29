using System.Text;
using Bcra.Ingestion.Domain.Parsing;
using FluentAssertions;

namespace Bcra.Ingestion.Domain.Tests.Parsing;

public sealed class DeudoresRecordParserTests
{
        private static byte[] Line(
        string entidad = "00007",
        string periodo = "202311",
        string tipoIdentificacion = "11",
        string cuit = "20003905528",
        string situacion = "1 ",
        string monto = "1,0         ") =>
        Encoding.Latin1.GetBytes(
            entidad.PadRight(5) + periodo.PadRight(6) + tipoIdentificacion.PadRight(2) + cuit.PadRight(11)
            + "000" + situacion.PadRight(2) + monto.PadRight(12) + new string(' ', 130));

    [Fact]
    public void RealSampleLine_ParsesByOffsets()
    {
        var parsed = DeudoresRecordParser.TryParse(Line(), out var record);

        parsed.Should().BeTrue();
        record!.Entidad.Should().Be(7);
        record.Periodo.Should().Be(202311);
        record.Cuit.Should().Be("20003905528");
        record.Situacion.Should().Be(1);
        record.Monto.Should().Be(1.0m);
    }

    [Fact]
    public void LeftAlignedMonto_ParsesWithoutLeadingZeros()
    {
        var parsed = DeudoresRecordParser.TryParse(Line(cuit: "20005894582", monto: "2406,0      "), out var record);

        parsed.Should().BeTrue();
        record!.Monto.Should().Be(2406.0m);
    }

    [Fact]
    public void MontoWithLeadingSpace_Parses()
    {
        var parsed = DeudoresRecordParser.TryParse(Line(monto: " 2406,0     "), out var record);

        parsed.Should().BeTrue();
        record!.Monto.Should().Be(2406.0m);
    }

    [Fact]
    public void MontoWithEmptyIntegerPart_ParsesAsZero()
    {
        var parsed = DeudoresRecordParser.TryParse(Line(monto: ",0          "), out var record);

        parsed.Should().BeTrue();
        record!.Monto.Should().Be(0.0m);
    }

    [Theory]
    [InlineData("1 ", 1)]
    [InlineData("21", 21)]
    [InlineData("23", 23)]
    [InlineData("11", 11)]
    public void Situacion_WithOrWithoutPadding_Parses(string rawSituacion, short expected)
    {
        var parsed = DeudoresRecordParser.TryParse(Line(situacion: rawSituacion), out var record);

        parsed.Should().BeTrue();
        record!.Situacion.Should().Be(expected);
    }

    [Fact]
    public void LineWithWrongLength_IsRejected()
    {
        var shortLine = Encoding.Latin1.GetBytes(new string('7', 170));
        var longLine = Encoding.Latin1.GetBytes(new string('7', 172));

        DeudoresRecordParser.TryParse(shortLine, out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(longLine, out _).Should().BeFalse();
    }

    [Fact]
    public void TipoIdentificacionOtherThanCuit_IsRejected()
    {
        DeudoresRecordParser.TryParse(Line(tipoIdentificacion: "96"), out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(Line(tipoIdentificacion: "1 "), out _).Should().BeFalse();
    }

    [Fact]
    public void NonNumericCuit_IsRejected()
    {
        DeudoresRecordParser.TryParse(Line(cuit: "20003X05528"), out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(Line(cuit: "200 3905528"), out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(Line(cuit: "           "), out _).Should().BeFalse();
    }

    [Fact]
    public void NonNumericSituacion_IsRejected()
    {
        DeudoresRecordParser.TryParse(Line(situacion: "X "), out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(Line(situacion: "  "), out _).Should().BeFalse();
    }

    [Fact]
    public void NonNumericMonto_IsRejected()
    {
        DeudoresRecordParser.TryParse(Line(monto: "AB,0        "), out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(Line(monto: "1.0         "), out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(Line(monto: "1,x         "), out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(Line(monto: "            "), out _).Should().BeFalse();
    }

    [Fact]
    public void NonNumericEntidadOrPeriodo_IsRejected()
    {
        DeudoresRecordParser.TryParse(Line(entidad: "     "), out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(Line(entidad: "AA007"), out _).Should().BeFalse();
        DeudoresRecordParser.TryParse(Line(periodo: "20231A"), out _).Should().BeFalse();
    }

    [Fact]
    public void LargeMonto_WithinFieldWidth_Parses()
    {
        var parsed = DeudoresRecordParser.TryParse(Line(monto: "9999999999,0"), out var record);

        parsed.Should().BeTrue();
        record!.Monto.Should().Be(9_999_999_999.0m);
    }
}
