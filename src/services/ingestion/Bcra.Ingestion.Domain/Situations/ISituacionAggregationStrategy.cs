namespace Bcra.Ingestion.Domain.Situations;

public interface ISituacionAggregationStrategy
{
    string Name { get; }

    string BuildAggregationExpression(string column = "situacion");
}
