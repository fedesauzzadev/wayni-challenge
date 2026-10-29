using Bcra.Ingestion.Domain.Situations;

namespace Bcra.Ingestion.Infrastructure.Situations;

public sealed class MaxLiteralSituacionStrategy : ISituacionAggregationStrategy
{
    public const string StrategyName = "max";

    public string Name => StrategyName;

    public string BuildAggregationExpression(string column = "situacion") => $"MAX({column})";
}
