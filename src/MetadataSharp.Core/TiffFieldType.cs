namespace MetadataSharp.Core;

/// <summary>The classic TIFF field types supported by the raw value model.</summary>
public enum TiffFieldType : ushort
{
    /// <summary>
    /// A field type this library does not read as a number: the entry states a code outside the twelve
    /// TIFF field types. Such a value is read one byte per element, as the reference reads it, and its
    /// exact code is <see cref="ExifValue.FieldTypeCode"/>. A block that holds one is not editable.
    /// </summary>
    Unknown = 0,
    /// <summary>An 8-bit unsigned integer.</summary>
    Byte = 1,
    /// <summary>A byte of 7-bit ASCII text; a string ends with a NUL byte.</summary>
    Ascii = 2,
    /// <summary>A 16-bit unsigned integer.</summary>
    Short = 3,
    /// <summary>A 32-bit unsigned integer.</summary>
    Long = 4,
    /// <summary>Two LONG values: a numerator, then a denominator.</summary>
    Rational = 5,
    /// <summary>An 8-bit signed integer.</summary>
    SByte = 6,
    /// <summary>A byte whose meaning the tag defines.</summary>
    Undefined = 7,
    /// <summary>A 16-bit signed integer.</summary>
    SShort = 8,
    /// <summary>A 32-bit signed integer.</summary>
    SLong = 9,
    /// <summary>Two SLONG values: a numerator, then a denominator.</summary>
    SRational = 10,
    /// <summary>A 32-bit IEEE floating-point number.</summary>
    Float = 11,
    /// <summary>A 64-bit IEEE floating-point number.</summary>
    Double = 12
}
