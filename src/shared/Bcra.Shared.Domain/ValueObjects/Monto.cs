namespace Bcra.Shared.Domain.ValueObjects;

public readonly record struct Monto
{
    public const int Precision = 15;
    public const int Scale = 1;

    public decimal Value { get; }

    private Monto(decimal value) => Value = value;

    public static Monto FromTrusted(decimal value) => new(value);

    public static bool IsValid(decimal value) =>
        value >= 0 && value < decimal.Parse("9".PadRight(Precision, '9'), System.Globalization.CultureInfo.InvariantCulture);

    public override string ToString() => Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    public static implicit operator decimal(Monto monto) => monto.Value;
}
