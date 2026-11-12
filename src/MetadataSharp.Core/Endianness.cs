namespace MetadataSharp.Core;

/// <summary>Specifies the byte order of a binary value.</summary>
public enum Endianness
{
    /// <summary>The least significant byte comes first (TIFF header <c>II</c>).</summary>
    LittleEndian,
    /// <summary>The most significant byte comes first (TIFF header <c>MM</c>).</summary>
    BigEndian
}
