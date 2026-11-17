using MetadataSharp.Core.Binary;

namespace MetadataSharp.Core;

/// <summary>An immutable, owned raw TIFF value with explicit source byte order.</summary>
public sealed partial class ExifValue
{
    private readonly byte[] bytes;

    // The private byte array is always freshly owned by this value; callers enter through FromRaw's copy.
    private ExifValue(TiffFieldType fieldType, ushort fieldTypeCode, uint count, Endianness byteOrder, byte[] bytes)
    {
        FieldType = fieldType;
        FieldTypeCode = fieldTypeCode;
        Count = count;
        ByteOrder = byteOrder;
        this.bytes = bytes;
    }

    /// <summary>The TIFF field type of the value.</summary>
    public TiffFieldType FieldType { get; }

    /// <summary>
    /// The field type as the entry states it: the number of the type, which for
    /// <see cref="TiffFieldType.Unknown"/> is the code the file stores and otherwise equals the number of
    /// <see cref="FieldType"/>.
    /// </summary>
    public ushort FieldTypeCode { get; }
    /// <summary>The number of elements of the field type, as the entry states it.</summary>
    public uint Count { get; }
    /// <summary>The byte order of the raw bytes.</summary>
    public Endianness ByteOrder { get; }
    /// <summary>The length of the raw value in bytes: the count times the width of the field type.</summary>
    public int ByteLength => bytes.Length;

    /// <summary>Validates type/count/length and takes an independent copy of the bytes.</summary>
    /// <remarks>Zero counts are representable for reads; editing policies are separate.</remarks>
    /// <param name="fieldType">The TIFF field type of the value.</param>
    /// <param name="count">The number of elements represented by the bytes.</param>
    /// <param name="byteOrder">The byte order used by multibyte elements.</param>
    /// <param name="bytes">The exact encoded value bytes.</param>
    /// <returns>An immutable value that owns a copy of <paramref name="bytes"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The byte order or field type is unsupported, or the computed byte length exceeds the supported range.</exception>
    /// <exception cref="ArgumentException">The byte length does not equal the field width multiplied by <paramref name="count"/>.</exception>
    public static ExifValue FromRaw(TiffFieldType fieldType, uint count,
        Endianness byteOrder, ReadOnlySpan<byte> bytes)
    {
        if (byteOrder is not Endianness.LittleEndian and not Endianness.BigEndian)
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        if (fieldType == TiffFieldType.Unknown)
            throw new ArgumentOutOfRangeException(nameof(fieldType), "An unknown field type is taken through FromRawUnknown.");
        int width = GetWidth(fieldType);
        if (!CheckedRange.TryMultiply(count, width, out int length))
            throw new ArgumentOutOfRangeException(nameof(count), "The value length exceeds the supported range.");
        if (bytes.Length != length)
            throw new ArgumentException("The byte length must exactly match the field type and count.", nameof(bytes));
        return new ExifValue(fieldType, (ushort)fieldType, count, byteOrder, bytes.ToArray());
    }

    /// <summary>
    /// Takes an independent copy of a value whose field type this library does not know. TIFF 6.0 gives a
    /// reader no width for such a type; one byte per element is read, as the reference reads it, and the
    /// entry cannot be written back.
    /// </summary>
    /// <param name="fieldTypeCode">The field type number the entry states.</param>
    /// <param name="count">The number of elements represented by the bytes.</param>
    /// <param name="byteOrder">The byte order of the block.</param>
    /// <param name="bytes">The exact encoded value bytes, one per element.</param>
    /// <returns>An immutable value that owns a copy of <paramref name="bytes"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The field type code is one of the TIFF types, or the byte order is unsupported.</exception>
    /// <exception cref="ArgumentException">The byte length does not equal <paramref name="count"/>.</exception>
    public static ExifValue FromRawUnknown(ushort fieldTypeCode, uint count, Endianness byteOrder, ReadOnlySpan<byte> bytes)
    {
        if (byteOrder is not Endianness.LittleEndian and not Endianness.BigEndian)
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        if (fieldTypeCode is >= 1 and <= 12)
            throw new ArgumentOutOfRangeException(nameof(fieldTypeCode), "The field type is one this library reads.");
        if (bytes.Length != count)
            throw new ArgumentException("An unknown field type is read one byte per element.", nameof(bytes));
        return new ExifValue(TiffFieldType.Unknown, fieldTypeCode, count, byteOrder, bytes.ToArray());
    }

    /// <summary>Copies exactly ByteLength bytes, leaving any destination suffix unchanged.</summary>
    /// <param name="destination">The buffer that receives the raw value bytes.</param>
    /// <exception cref="ArgumentException">The destination is shorter than <see cref="ByteLength"/>.</exception>
    public void CopyRawTo(Span<byte> destination)
    {
        if (destination.Length < bytes.Length)
            throw new ArgumentException("The destination is too short for the value.", nameof(destination));
        bytes.AsSpan().CopyTo(destination);
    }

    internal ReadOnlySpan<byte> RawBytes => bytes;

    // This width table is the single source for FromRaw's checked count-to-byte-length invariant.
    internal static int GetWidth(TiffFieldType fieldType) => fieldType switch
    {
        TiffFieldType.Unknown => 1,
        TiffFieldType.Byte or TiffFieldType.Ascii or TiffFieldType.SByte or TiffFieldType.Undefined => 1,
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float => 4,
        TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(fieldType), "The TIFF field type is unsupported.")
    };
}
