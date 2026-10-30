namespace Bcra.Ingestion.Domain.Parsing;

/// <summary>
/// Parser compartido de campos numéricos fixed-width ASCII: recorta espacios, exige solo
/// dígitos y acota por longitud y por valor máximo (mismo contrato que usaban el parser de
/// deudores y el lector de archivos de referencia).
/// </summary>
public static class FixedWidthDigits
{
    private const byte DigitZero = (byte)'0';

    public static bool TryParse(ReadOnlySpan<byte> field, int maxLength, int maxValue, out int value)
    {
        field = field.Trim((byte)' ');
        value = 0;
        if (field.IsEmpty || field.Length > maxLength)
            return false;

        var accumulated = 0L;
        foreach (var digit in field)
        {
            if (!IsAsciiDigit(digit))
                return false;
            accumulated = accumulated * 10 + (digit - DigitZero);
        }

        if (accumulated > maxValue)
            return false;

        value = (int)accumulated;
        return true;
    }

    public static bool IsAsciiDigit(byte value) => (uint)(value - DigitZero) <= 9;
}
