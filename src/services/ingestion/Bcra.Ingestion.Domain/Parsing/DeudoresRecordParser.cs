using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Bcra.Ingestion.Domain.Parsing;

public sealed record ParsedDebtRecord(int Entidad, int Periodo, string Cuit, short Situacion, decimal Monto);

/// <summary>Clasificación de una línea del archivo de deudores.</summary>
public enum DeudoresLineKind
{
    /// <summary>Registro de deudor con CUIT (marcador "11") y todos los campos válidos.</summary>
    Record,

    /// <summary>
    /// Línea de ancho correcto que NO es un registro de deudor con CUIT (marcador ≠ "11":
    /// registros de control 98/99, encabezados/cierres de lote del BCRA, otros tipos de
    /// identificación). Se descarta deliberadamente: no es un error del archivo.
    /// </summary>
    ControlRecord,

    /// <summary>Línea malformada (largo incorrecto o campos inválidos): rechazo genuino.</summary>
    Invalid,
}

public sealed class DeudoresRecordParser
{
    public const int LineLength = 171;

    private const byte Space = (byte)' ';
    private const byte Comma = (byte)',';
    private const byte DigitZero = (byte)'0';
    private const long MaxMontoIntegerPart = 9_999_999_999_999;

    /// <summary>Offset del marcador fijo "11" que sigue al periodo (posiciones 11-12 del registro).</summary>
    private const int RecordMarkerOffset = 11;
    private const byte RecordMarkerDigit = DigitZero + 1;

    /// <summary>Código de entidad BCRA: campo de 5 dígitos (Maeent.txt real llega hasta 72.622).</summary>
    private const int MaxEntidad = 99_999;

    public static bool TryParse(ReadOnlySpan<byte> line, [NotNullWhen(true)] out ParsedDebtRecord? record) =>
        Classify(line, out record) == DeudoresLineKind.Record;

    /// <summary>
    /// Distingue registro de deudor válido (<see cref="DeudoresLineKind.Record"/>) de registro
    /// de control descartado a propósito (<see cref="DeudoresLineKind.ControlRecord"/>) y de
    /// línea malformada (<see cref="DeudoresLineKind.Invalid"/>).
    /// </summary>
    public static DeudoresLineKind Classify(ReadOnlySpan<byte> line, [NotNullWhen(true)] out ParsedDebtRecord? record)
    {
        record = null;
        if (line.Length != LineLength)
            return DeudoresLineKind.Invalid;
        if (line[RecordMarkerOffset] != RecordMarkerDigit || line[RecordMarkerOffset + 1] != RecordMarkerDigit)
            return DeudoresLineKind.ControlRecord;
        if (!TryParseBounded(line[..5], MaxEntidad, out var entidad))
            return DeudoresLineKind.Invalid;
        if (!TryParseBounded(line[5..11], 9_999_999, out var periodo))
            return DeudoresLineKind.Invalid;

        var cuit = line[13..24].Trim(Space);
        if (cuit.IsEmpty || cuit.Length > 11 || !AllDigits(cuit))
            return DeudoresLineKind.Invalid;

        if (!TryParseBounded(line[27..29], short.MaxValue, out var situacion))
            return DeudoresLineKind.Invalid;
        if (!TryParseMonto(line[29..41], out var monto))
            return DeudoresLineKind.Invalid;

        record = new ParsedDebtRecord(entidad, periodo, Encoding.Latin1.GetString(cuit), (short)situacion, monto);
        return DeudoresLineKind.Record;
    }

    private static bool AllDigits(ReadOnlySpan<byte> field)
    {
        foreach (var value in field)
        {
            if (!FixedWidthDigits.IsAsciiDigit(value))
                return false;
        }
        return true;
    }

    private static bool TryParseBounded(ReadOnlySpan<byte> field, int maxValue, out int value) =>
        FixedWidthDigits.TryParse(field, maxLength: 10, maxValue, out value);

    private static bool TryParseMonto(ReadOnlySpan<byte> field, out decimal monto)
    {
        field = field.Trim(Space);
        monto = 0m;
        if (field.IsEmpty)
            return false;

        var index = 0;
        var integerPart = 0L;
        var hasIntegerDigits = false;
        while (index < field.Length && FixedWidthDigits.IsAsciiDigit(field[index]))
        {
            integerPart = integerPart * 10 + (field[index] - DigitZero);
            if (integerPart > MaxMontoIntegerPart)
                return false;
            hasIntegerDigits = true;
            index++;
        }

        var value = (decimal)integerPart;
        if (index == field.Length)
        {
            if (!hasIntegerDigits)
                return false;
            monto = value;
            return true;
        }

        if (field[index] != Comma)
            return false;
        index++;

        var hasFractionDigits = false;
        var scale = 0.1m;
        while (index < field.Length && FixedWidthDigits.IsAsciiDigit(field[index]))
        {
            value += (field[index] - DigitZero) * scale;
            scale /= 10m;
            hasFractionDigits = true;
            index++;
        }

        if (index != field.Length)
            return false;
        if (!hasIntegerDigits && !hasFractionDigits)
            return false;

        monto = value;
        return true;
    }
}
