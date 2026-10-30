using Bcra.Ingestion.Application.Ports;

namespace Bcra.Ingestion.Infrastructure.Situations;

public sealed class MaxLiteralSituacionStrategy : ISituacionAggregationStrategy
{
    public const string StrategyName = "max";

    public string Name => StrategyName;

    public string BuildAggregationExpression(string column = "situacion") => $"MAX({column})";
}
