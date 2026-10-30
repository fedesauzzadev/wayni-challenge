using Bcra.Ingestion.Domain.Situations;
using Bcra.Ingestion.Infrastructure.Situations;
using FluentAssertions;
using Xunit;

namespace Bcra.Ingestion.Infrastructure.Tests.Situations;

public class WorstSeveritySituacionStrategyTests
{
    private readonly WorstSeveritySituacionStrategy _sut = new();

    /// <summary>Golden: el SQL generado desde SeverityTable debe quedar byte-idéntico al literal histórico.</summary>
    [Fact]
    public void BuildAggregationExpression_MatchesGoldenSql_ByteForByte()
    {
        var sql = _sut.BuildAggregationExpression();

        sql.Should().Be("""
            CASE MAX(CASE situacion
                        WHEN 1 THEN 0
                        WHEN 21 THEN 1
                        WHEN 23 THEN 2
                        WHEN 3 THEN 3
                        WHEN 4 THEN 4
                        WHEN 5 THEN 5
                        WHEN 11 THEN 6
                     END)
                WHEN 0 THEN 1
                WHEN 1 THEN 21
                WHEN 2 THEN 23
                WHEN 3 THEN 3
                WHEN 4 THEN 4
                WHEN 5 THEN 5
                WHEN 6 THEN 11
            END
            """);
        sql.Should().NotContain("\r");
    }

    /// <summary>Paridad: cada código válido aparece con su rank en ambos CASE, sin códigos extra.</summary>
    [Fact]
    public void BuildAggregationExpression_IsConsistentWithSeverityTable_ForEveryValidCode()
    {
        var sql = _sut.BuildAggregationExpression();

        foreach (var (code, rank) in Situacion.SeverityTable)
        {
            sql.Should().Contain($"WHEN {code} THEN {rank}", $"el CASE código→rank de {code} debe existir");
            sql.Should().Contain($"WHEN {rank} THEN {code}", $"el CASE rank→código de {code} debe existir");
        }

        var whens = sql.Split('\n').Count(line => line.Contains("WHEN ", StringComparison.Ordinal));
        whens.Should().Be(Situacion.SeverityTable.Length * 2, "un WHEN por entrada de la tabla en cada CASE");
    }

    [Fact]
    public void BuildAggregationExpression_UsesProvidedColumn()
    {
        var sql = _sut.BuildAggregationExpression("s");

        sql.Should().StartWith("CASE MAX(CASE s\n");
    }

    [Fact]
    public void Name_IsWorstSeverity()
    {
        _sut.Name.Should().Be("peor_situacion");
    }
}
