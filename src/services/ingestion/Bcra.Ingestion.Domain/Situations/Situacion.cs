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

    public static bool IsValid(short value) => ValidValues.Contains(value);

    public static int SeverityRank(short value) => value switch
    {
        Normal => 0,
        RiesgoBajo => 1,
        TratamientoEspecial => 2,
        RiesgoMedio => 3,
        RiesgoAlto => 4,
        Irrecuperable => 5,
        AsistenciasCubiertasGarantiasA => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown situacion code.")
    };
}
