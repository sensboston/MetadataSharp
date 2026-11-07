using MetadataSharp.Core.Binary;

namespace MetadataSharp.Core;

/// <summary>An immutable, owned raw TIFF value with explicit source byte order.</summary>
public sealed class ExifValue
{
    private readonly byte[] bytes;

    private ExifValue(TiffFieldType fieldType, uint count, Endianness byteOrder, byte[] bytes)
    {
        FieldType = fieldType;
        Count = count;
        ByteOrder = byteOrder;
        this.bytes = bytes;
    }

    public TiffFieldType FieldType { get; }
    public uint Count { get; }
    public Endianness ByteOrder { get; }
    public int ByteLength => bytes.Length;

    /// <summary>Validates type/count/length and takes an independent copy of the bytes.</summary>
    /// <remarks>Zero counts are representable for reads; editing policies are separate.</remarks>
    public static ExifValue FromRaw(TiffFieldType fieldType, uint count,
        Endianness byteOrder, ReadOnlySpan<byte> bytes)
    {
        if (byteOrder is not Endianness.LittleEndian and not Endianness.BigEndian)
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        int width = GetWidth(fieldType);
        if (!CheckedRange.TryMultiply(count, width, out int length))
            throw new ArgumentOutOfRangeException(nameof(count), "The value length exceeds the supported range.");
        if (bytes.Length != length)
            throw new ArgumentException("The byte length must exactly match the field type and count.", nameof(bytes));
        return new ExifValue(fieldType, count, byteOrder, bytes.ToArray());
    }

    /// <summary>Copies exactly ByteLength bytes, leaving any destination suffix unchanged.</summary>
    public void CopyRawTo(Span<byte> destination)
    {
        if (destination.Length < bytes.Length)
            throw new ArgumentException("The destination is too short for the value.", nameof(destination));
        bytes.AsSpan().CopyTo(destination);
    }

    internal ReadOnlySpan<byte> RawBytes => bytes;

    internal static int GetWidth(TiffFieldType fieldType) => fieldType switch
    {
        TiffFieldType.Byte or TiffFieldType.Ascii or TiffFieldType.SByte or TiffFieldType.Undefined => 1,
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float => 4,
        TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(fieldType), "The TIFF field type is unsupported.")
    };
}
