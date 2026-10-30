namespace Bcra.Ingestion.Domain.Situations;

public static class Situacion
{
    public const short Normal = 1;
    public const short AsistenciasCubiertasGarantiasA = 11;
    public const short RiesgoBajo = 21;
    public const short TratamientoEspecial = 23;
    public const short RiesgoMedio = 3;
    public const short RiesgoAlto = 4;
    public const short Irrecuperable = 5;

    public static readonly IReadOnlyCollection<short> ValidValues =
    [
        Normal, AsistenciasCubiertasGarantiasA, RiesgoBajo, TratamientoEspecial, RiesgoMedio, RiesgoAlto, Irrecuperable
    ];

    /// <summary>
    /// Única fuente de verdad de severidad: códigos ordenados por rank 0..6. El CASE SQL de
    /// agregación se genera desde esta tabla (paridad código↔rank garantizada).
    /// </summary>
    public static readonly (short Code, int Rank)[] SeverityTable =
    [
        (Normal, 0),
        (RiesgoBajo, 1),
        (TratamientoEspecial, 2),
        (RiesgoMedio, 3),
        (RiesgoAlto, 4),
        (Irrecuperable, 5),
        (AsistenciasCubiertasGarantiasA, 6)
    ];

    public static bool IsValid(short value) => ValidValues.Contains(value);

    public static int SeverityRank(short value)
    {
        foreach (var (code, rank) in SeverityTable)
        {
            if (code == value)
                return rank;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown situacion code.");
    }
}
