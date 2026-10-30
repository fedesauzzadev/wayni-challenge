using System.Text;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.Situations;

namespace Bcra.Ingestion.Infrastructure.Situations;

/// <summary>
/// CASE anidado generado desde <see cref="Situacion.SeverityTable"/> (única fuente de verdad
/// código↔rank): MAX del rank del grupo y vuelta al código del peor rank.
/// </summary>
public sealed class WorstSeveritySituacionStrategy : ISituacionAggregationStrategy
{
    public const string StrategyName = "peor_situacion";

    private const string CodeToRankIndent = "            ";
    private const string EndIndent = "         ";
    private const string RankToCodeIndent = "    ";

    public string Name => StrategyName;

    public string BuildAggregationExpression(string column = "situacion")
    {
        var builder = new StringBuilder("CASE MAX(CASE ").Append(column);
        foreach (var (code, rank) in Situacion.SeverityTable)
            builder.Append('\n').Append(CodeToRankIndent).Append("WHEN ").Append(code).Append(" THEN ").Append(rank);
        builder.Append('\n').Append(EndIndent).Append("END)");
        foreach (var (code, rank) in Situacion.SeverityTable)
            builder.Append('\n').Append(RankToCodeIndent).Append("WHEN ").Append(rank).Append(" THEN ").Append(code);
        builder.Append('\n').Append("END");
        return builder.ToString();
    }
}
