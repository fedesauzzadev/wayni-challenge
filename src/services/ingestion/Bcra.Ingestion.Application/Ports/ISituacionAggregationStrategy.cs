namespace Bcra.Ingestion.Application.Ports;

public interface ISituacionAggregationStrategy
{
    string Name { get; }

    string BuildAggregationExpression(string column = "situacion");
}
