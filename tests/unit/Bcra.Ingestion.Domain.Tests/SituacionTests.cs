using Bcra.Ingestion.Domain.Situations;
using FluentAssertions;
using Xunit;

namespace Bcra.Ingestion.Domain.Tests;

public class SituacionTests
{
    [Theory]
    [InlineData(Situacion.Normal)]
    [InlineData(Situacion.RiesgoBajo)]
    [InlineData(Situacion.TratamientoEspecial)]
    [InlineData(Situacion.RiesgoMedio)]
    [InlineData(Situacion.RiesgoAlto)]
    [InlineData(Situacion.Irrecuperable)]
    [InlineData(Situacion.AsistenciasCubiertasGarantiasA)]
    public void ValidCodes_AreRecognized(short code)
    {
        Situacion.IsValid(code).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void InvalidCodes_AreRejected(short code)
    {
        Situacion.IsValid(code).Should().BeFalse();
    }

    [Fact]
    public void SeverityRank_IsMonotonicallyIncreasing()
    {
        var ranks = new[]
        {
            Situacion.SeverityRank(Situacion.Normal),
            Situacion.SeverityRank(Situacion.RiesgoBajo),
            Situacion.SeverityRank(Situacion.TratamientoEspecial),
            Situacion.SeverityRank(Situacion.RiesgoMedio),
            Situacion.SeverityRank(Situacion.RiesgoAlto),
            Situacion.SeverityRank(Situacion.Irrecuperable),
            Situacion.SeverityRank(Situacion.AsistenciasCubiertasGarantiasA)
        };

        ranks.Should().BeInAscendingOrder();
    }

    [Fact]
    public void SeverityTable_IsTheSingleSourceOfTruthForSeverityRank()
    {
        foreach (var (code, rank) in Situacion.SeverityTable)
            Situacion.SeverityRank(code).Should().Be(rank, $"el rank de {code} debe salir de la tabla");

        Situacion.SeverityTable.Select(e => e.Rank).Should().BeInAscendingOrder();
        Situacion.SeverityTable.Select(e => e.Rank).Should().BeEquivalentTo(
            Enumerable.Range(0, Situacion.SeverityTable.Length), "los ranks son contiguos desde 0");
        Situacion.SeverityTable.Select(e => e.Code).Should().BeEquivalentTo(Situacion.ValidValues);
    }

    [Fact]
    public void SeverityRank_UnknownCode_Throws()
    {
        var act = () => Situacion.SeverityRank(99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
