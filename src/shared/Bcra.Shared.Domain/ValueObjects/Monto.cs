namespace Bcra.Shared.Domain.ValueObjects;

public readonly record struct Monto
{
    public const int Precision = 15;
    public const int Scale = 1;

    /// <summary>Máximo valor representable: 15 dígitos enteros (10^15 - 1), pre-calculado.</summary>
    private static readonly decimal MaxValue = 999_999_999_999_999m;

    public decimal Value { get; }

    private Monto(decimal value) => Value = value;

    public static Monto FromTrusted(decimal value) => new(value);

    public static bool IsValid(decimal value) => value >= 0 && value < MaxValue;

    public override string ToString() => Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    public static implicit operator decimal(Monto monto) => monto.Value;
}
