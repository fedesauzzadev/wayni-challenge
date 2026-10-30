using Bcra.Shared.Domain.ValueObjects;

namespace Bcra.Shared.Domain;

public static class CuitParser
{
    public static bool TryParse(ReadOnlySpan<char> raw, out Cuit cuit)
    {
        cuit = default;
        var trimmed = raw.Trim();
        if (trimmed.Length != Cuit.Length)
        {
            return false;
        }

        foreach (var ch in trimmed)
        {
            if (!char.IsDigit(ch)) return false;
        }

        cuit = Cuit.FromTrusted(new string(trimmed));
        return true;
    }

    /// <summary>
    /// Normaliza una entrada cruda conservando solo los dígitos y valida que resulten
    /// exactamente <see cref="Cuit.Length"/> (equivalente a filtrar dígitos y luego
    /// <see cref="Cuit.IsValid"/>, sin LINQ ni alocaciones intermedias).
    /// </summary>
    public static bool TryNormalize(ReadOnlySpan<char> raw, out Cuit cuit)
    {
        cuit = default;
        Span<char> digits = stackalloc char[Cuit.Length];
        var count = 0;
        foreach (var ch in raw)
        {
            if (!char.IsDigit(ch)) continue;
            if (count == Cuit.Length) return false;
            digits[count++] = ch;
        }

        if (count != Cuit.Length) return false;
        cuit = Cuit.FromTrusted(new string(digits));
        return true;
    }

    public static string Format(Cuit cuit) =>
        $"{cuit.Value[..2]}-{cuit.Value.Substring(2, 8)}-{cuit.Value.Substring(10, 1)}";
}
