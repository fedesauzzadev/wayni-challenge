namespace Bcra.Shared.Domain.ValueObjects;

public readonly record struct Cuit
{
    public const int Length = 11;

    public string Value { get; }

    private Cuit(string value) => Value = value;

    public static Cuit FromTrusted(string value) => new(value);

    public static bool IsValid(string value) =>
        value is not null
        && value.Length == Length
        && value.All(char.IsDigit);

    public override string ToString() => Value;

    public static implicit operator string(Cuit cuit) => cuit.Value;
}
