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

    public static string Format(Cuit cuit) =>
        $"{cuit.Value[..2]}-{cuit.Value.Substring(2, 8)}-{cuit.Value.Substring(10, 1)}";
}
