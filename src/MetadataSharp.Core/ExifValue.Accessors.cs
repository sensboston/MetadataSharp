using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MetadataSharp.Core;

// Typed reading of a raw value. Every accessor is a question about the value as it stands: it answers
// false, and never throws, when the field type or the count is not one it reads. Nothing here knows a
// tag: what a number means is ExifTags.Interpret's business, and the raw bytes stay available.
public sealed partial class ExifValue
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private bool Little => ByteOrder == Endianness.LittleEndian;

    // ---- Arrays ----------------------------------------------------------------------------------------

    /// <summary>The elements of a BYTE or UNDEFINED value, in a new array.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is neither BYTE nor UNDEFINED.</returns>
    public bool TryGetByteArray([NotNullWhen(true)] out byte[]? values)
    {
        values = FieldType is TiffFieldType.Byte or TiffFieldType.Undefined ? bytes.AsSpan().ToArray() : null;
        return values is not null;
    }

    /// <summary>The elements of an SBYTE value, in a new array.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is not SBYTE.</returns>
    public bool TryGetSByteArray([NotNullWhen(true)] out sbyte[]? values)
    {
        values = null;
        if (FieldType != TiffFieldType.SByte)
            return false;
        values = new sbyte[bytes.Length];
        for (int i = 0; i < values.Length; i++)
            values[i] = unchecked((sbyte)bytes[i]);
        return true;
    }

    /// <summary>The elements of a SHORT value, or of a BYTE value widened, in a new array.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is neither SHORT nor BYTE.</returns>
    public bool TryGetUInt16Array([NotNullWhen(true)] out ushort[]? values)
    {
        values = null;
        if (FieldType is not (TiffFieldType.Short or TiffFieldType.Byte))
            return false;
        values = new ushort[(int)Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = (ushort)UnsignedAt(i);
        return true;
    }

    /// <summary>The elements of a LONG value, or of a SHORT or BYTE value widened, in a new array.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is not LONG, SHORT or BYTE.</returns>
    public bool TryGetUInt32Array([NotNullWhen(true)] out uint[]? values)
    {
        values = null;
        if (FieldType is not (TiffFieldType.Long or TiffFieldType.Short or TiffFieldType.Byte))
            return false;
        values = new uint[(int)Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = UnsignedAt(i);
        return true;
    }

    /// <summary>The elements of an SSHORT value, or of an SBYTE value widened, in a new array.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is neither SSHORT nor SBYTE.</returns>
    public bool TryGetInt16Array([NotNullWhen(true)] out short[]? values)
    {
        values = null;
        if (FieldType is not (TiffFieldType.SShort or TiffFieldType.SByte))
            return false;
        values = new short[(int)Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = (short)SignedAt(i);
        return true;
    }

    /// <summary>The elements of an SLONG value, or of an SSHORT or SBYTE value widened, in a new array.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is not SLONG, SSHORT or SBYTE.</returns>
    public bool TryGetInt32Array([NotNullWhen(true)] out int[]? values)
    {
        values = null;
        if (FieldType is not (TiffFieldType.SLong or TiffFieldType.SShort or TiffFieldType.SByte))
            return false;
        values = new int[(int)Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = SignedAt(i);
        return true;
    }

    /// <summary>
    /// The elements of a value of any integer type, signed or unsigned, in a new array: the one form
    /// that holds BYTE, SHORT, LONG, SBYTE, SSHORT and SLONG alike without loss.
    /// </summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is not one of the six integer types. UNDEFINED is not one.</returns>
    public bool TryGetInt64Array([NotNullWhen(true)] out long[]? values)
    {
        values = null;
        bool unsigned = FieldType is TiffFieldType.Long or TiffFieldType.Short or TiffFieldType.Byte;
        if (!unsigned && FieldType is not (TiffFieldType.SLong or TiffFieldType.SShort or TiffFieldType.SByte))
            return false;
        values = new long[(int)Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = unsigned ? UnsignedAt(i) : SignedAt(i);
        return true;
    }

    /// <summary>The elements of a RATIONAL value, in a new array. No pair is normalized or divided.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is not RATIONAL.</returns>
    public bool TryGetRationalArray([NotNullWhen(true)] out UnsignedRational[]? values)
    {
        values = null;
        if (FieldType != TiffFieldType.Rational)
            return false;
        values = new UnsignedRational[(int)Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = new UnsignedRational(UInt32At(i * 8), UInt32At((i * 8) + 4));
        return true;
    }

    /// <summary>The elements of an SRATIONAL value, in a new array. No pair is normalized or divided.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is not SRATIONAL.</returns>
    public bool TryGetSignedRationalArray([NotNullWhen(true)] out SignedRational[]? values)
    {
        values = null;
        if (FieldType != TiffFieldType.SRational)
            return false;
        values = new SignedRational[(int)Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = new SignedRational(unchecked((int)UInt32At(i * 8)), unchecked((int)UInt32At((i * 8) + 4)));
        return true;
    }

    /// <summary>The elements of a FLOAT value, in a new array, bit for bit.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is not FLOAT.</returns>
    public bool TryGetSingleArray([NotNullWhen(true)] out float[]? values)
    {
        values = null;
        if (FieldType != TiffFieldType.Float)
            return false;
        values = new float[(int)Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = SingleAt(i);
        return true;
    }

    /// <summary>The elements of a DOUBLE value, or of a FLOAT value widened, in a new array.</summary>
    /// <param name="values">The elements, empty for a count of zero; null when the method returns false.</param>
    /// <returns>
    /// False when the field type is neither DOUBLE nor FLOAT. A rational is not divided here: read it
    /// with <see cref="TryGetRationalArray" /> and decide what a zero denominator means.
    /// </returns>
    public bool TryGetDoubleArray([NotNullWhen(true)] out double[]? values)
    {
        values = null;
        if (FieldType is not (TiffFieldType.Double or TiffFieldType.Float))
            return false;
        values = new double[(int)Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = FieldType == TiffFieldType.Float ? SingleAt(i) : DoubleAt(i);
        return true;
    }

    // ---- Text ------------------------------------------------------------------------------------------

    /// <summary>The text of an ASCII value: its first string, without the terminator.</summary>
    /// <remarks>
    /// The text ends at the first NUL byte, or at the end of a value that has none, so padding and
    /// anything written after the terminator are left out; <see cref="TryGetStringArray" /> returns
    /// every string of a value that holds several. Bytes above 0x7F are read as UTF-8 when the text
    /// is valid UTF-8 and as ISO 8859-1 otherwise, so a string is always returned.
    /// </remarks>
    /// <param name="value">The text, empty for a count of zero; null when the method returns false.</param>
    /// <returns>False when the field type is not ASCII.</returns>
    public bool TryGetString([NotNullWhen(true)] out string? value)
    {
        value = null;
        if (FieldType != TiffFieldType.Ascii)
            return false;
        ReadOnlySpan<byte> text = bytes;
        int end = text.IndexOf((byte)0);
        value = Decode(end < 0 ? text : text.Slice(0, end));
        return true;
    }

    /// <summary>Every string of an ASCII value, which TIFF lets hold several, each ended by a NUL byte.</summary>
    /// <remarks>
    /// Trailing NUL bytes end the last string and add no empty ones; an empty string between two
    /// others is kept, because its position can carry meaning. Each string is decoded as
    /// <see cref="TryGetString" /> decodes the first.
    /// </remarks>
    /// <param name="values">
    /// The strings in order, in a new array, empty when the value holds nothing but NUL bytes; null
    /// when the method returns false.
    /// </param>
    /// <returns>False when the field type is not ASCII.</returns>
    public bool TryGetStringArray([NotNullWhen(true)] out string[]? values)
    {
        values = null;
        if (FieldType != TiffFieldType.Ascii)
            return false;
        ReadOnlySpan<byte> text = bytes.AsSpan().TrimEnd((byte)0);
        var strings = new List<string>();
        while (!text.IsEmpty)
        {
            int end = text.IndexOf((byte)0);
            if (end < 0)
            {
                strings.Add(Decode(text));
                break;
            }
            strings.Add(Decode(text.Slice(0, end)));
            text = text.Slice(end + 1);
        }
        values = strings.ToArray();
        return true;
    }

    // ---- Single values ---------------------------------------------------------------------------------

    /// <summary>The one element of a LONG, SHORT or BYTE value.</summary>
    /// <param name="value">Receives the value on success, or zero on failure.</param>
    /// <returns>False when the count is not 1 or the field type is not LONG, SHORT or BYTE.</returns>
    public bool TryGetUInt32(out uint value)
    {
        value = 0;
        if (Count != 1 || FieldType is not (TiffFieldType.Long or TiffFieldType.Short or TiffFieldType.Byte))
            return false;
        value = UnsignedAt(0);
        return true;
    }

    /// <summary>The one element of an SLONG, SSHORT or SBYTE value.</summary>
    /// <param name="value">Receives the value on success, or zero on failure.</param>
    /// <returns>False when the count is not 1 or the field type is not SLONG, SSHORT or SBYTE.</returns>
    public bool TryGetInt32(out int value)
    {
        value = 0;
        if (Count != 1 || FieldType is not (TiffFieldType.SLong or TiffFieldType.SShort or TiffFieldType.SByte))
            return false;
        value = SignedAt(0);
        return true;
    }

    /// <summary>The one element of a value of any integer type, signed or unsigned.</summary>
    /// <param name="value">Receives the value on success, or zero on failure.</param>
    /// <returns>False when the count is not 1 or the field type is not one of the six integer types.</returns>
    public bool TryGetInt64(out long value)
    {
        value = 0;
        if (Count != 1)
            return false;
        if (FieldType is TiffFieldType.Long or TiffFieldType.Short or TiffFieldType.Byte)
            value = UnsignedAt(0);
        else if (FieldType is TiffFieldType.SLong or TiffFieldType.SShort or TiffFieldType.SByte)
            value = SignedAt(0);
        else
            return false;
        return true;
    }

    /// <summary>The one element of a RATIONAL value, as stored.</summary>
    /// <param name="value">Receives the numerator and denominator on success, or the default pair on failure.</param>
    /// <returns>False when the count is not 1 or the field type is not RATIONAL.</returns>
    public bool TryGetRational(out UnsignedRational value)
    {
        value = default;
        if (Count != 1 || FieldType != TiffFieldType.Rational)
            return false;
        value = new UnsignedRational(UInt32At(0), UInt32At(4));
        return true;
    }

    /// <summary>The one element of an SRATIONAL value, as stored.</summary>
    /// <param name="value">Receives the numerator and denominator on success, or the default pair on failure.</param>
    /// <returns>False when the count is not 1 or the field type is not SRATIONAL.</returns>
    public bool TryGetSignedRational(out SignedRational value)
    {
        value = default;
        if (Count != 1 || FieldType != TiffFieldType.SRational)
            return false;
        value = new SignedRational(unchecked((int)UInt32At(0)), unchecked((int)UInt32At(4)));
        return true;
    }

    /// <summary>The one element of a DOUBLE value, or of a FLOAT value widened.</summary>
    /// <param name="value">Receives the floating-point value on success, or zero on failure.</param>
    /// <returns>False when the count is not 1 or the field type is neither DOUBLE nor FLOAT.</returns>
    public bool TryGetDouble(out double value)
    {
        value = 0;
        if (Count != 1 || FieldType is not (TiffFieldType.Double or TiffFieldType.Float))
            return false;
        value = FieldType == TiffFieldType.Float ? SingleAt(0) : DoubleAt(0);
        return true;
    }

    // ---- Shared pieces ---------------------------------------------------------------------------------
    // FromRaw is the only way in and it fixes the length at count times width, so an index below Count
    // is inside the bytes for the field type the callers have already checked.

    private uint UnsignedAt(int index) => FieldType switch
    {
        TiffFieldType.Byte => bytes[index],
        TiffFieldType.Short => Little
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index * 2, 2)),
        _ => UInt32At(index * 4),
    };

    private int SignedAt(int index) => FieldType switch
    {
        TiffFieldType.SByte => unchecked((sbyte)bytes[index]),
        TiffFieldType.SShort => Little
            ? BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(index * 2, 2))
            : BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(index * 2, 2)),
        _ => unchecked((int)UInt32At(index * 4)),
    };

    private uint UInt32At(int offset) => Little
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));

    private float SingleAt(int index) => Little
        ? BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * 4, 4))
        : BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(index * 4, 4));

    private double DoubleAt(int index) => Little
        ? BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(index * 8, 8))
        : BinaryPrimitives.ReadDoubleBigEndian(bytes.AsSpan(index * 8, 8));

    private static string Decode(ReadOnlySpan<byte> text)
    {
        try { return StrictUtf8.GetString(text); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(text); }
    }
}
