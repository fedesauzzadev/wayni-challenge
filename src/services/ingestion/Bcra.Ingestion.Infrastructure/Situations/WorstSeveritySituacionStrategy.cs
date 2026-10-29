using Bcra.Ingestion.Domain.Situations;

namespace Bcra.Ingestion.Infrastructure.Situations;

public sealed class WorstSeveritySituacionStrategy : ISituacionAggregationStrategy
{
    public const string StrategyName = "peor_situacion";

    public string Name => StrategyName;

    public string BuildAggregationExpression(string column = "situacion") => $"""
        CASE MAX(CASE {column}
                    WHEN {Situacion.Normal} THEN 0
                    WHEN {Situacion.RiesgoBajo} THEN 1
                    WHEN {Situacion.TratamientoEspecial} THEN 2
                    WHEN {Situacion.RiesgoMedio} THEN 3
                    WHEN {Situacion.RiesgoAlto} THEN 4
                    WHEN {Situacion.Irrecuperable} THEN 5
                    WHEN {Situacion.AsistenciasCubiertasGarantiasA} THEN 6
                 END)
            WHEN 0 THEN {Situacion.Normal}
            WHEN 1 THEN {Situacion.RiesgoBajo}
            WHEN 2 THEN {Situacion.TratamientoEspecial}
            WHEN 3 THEN {Situacion.RiesgoMedio}
            WHEN 4 THEN {Situacion.RiesgoAlto}
            WHEN 5 THEN {Situacion.Irrecuperable}
            WHEN 6 THEN {Situacion.AsistenciasCubiertasGarantiasA}
        END
        """;
}
