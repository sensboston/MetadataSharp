using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.Operations;

/// <summary>
/// A value of an EXIF tag from text, as the modify commands of Exiv2 give it: ASCII text as it is,
/// numbers separated by blanks, rationals as <c>1/3</c>.
/// </summary>
internal static class ExifValueText
{
    private static readonly char[] Blank = [' ', '\t'];

    /// <summary>The value, little-endian.</summary>
    /// <exception cref="ArgumentException">Text that is not ASCII for an ASCII value, no value, or a type text does not give.</exception>
    /// <exception cref="FormatException">A number that does not read as one of the type.</exception>
    /// <exception cref="OverflowException">A number outside the range of the type.</exception>
    internal static ExifValue Parse(TiffFieldType field, string value)
    {
        byte[] bytes;
        uint count;
        if (field == TiffFieldType.Ascii)
        {
            if (value.Any(c => c is '\0' or > '\u007F')) throw new ArgumentException("An Ascii value holds seven-bit characters and no NUL.");
            bytes = new byte[value.Length + 1];
            Encoding.ASCII.GetBytes(value, bytes);
            count = (uint)bytes.Length;
        }
        else
        {
            string[] items = value.Split(Blank, StringSplitOptions.RemoveEmptyEntries);
            if (items.Length == 0) throw new ArgumentException("The value is empty.");
            int width = field switch
            {
                TiffFieldType.Byte or TiffFieldType.SByte or TiffFieldType.Undefined => 1,
                TiffFieldType.Short or TiffFieldType.SShort => 2,
                TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float => 4,
                TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double => 8,
                _ => throw new ArgumentException($"The tag is defined with the type {field}, which a script cannot write; name a type."),
            };
            bytes = new byte[items.Length * width];
            count = (uint)items.Length;
            CultureInfo c = CultureInfo.InvariantCulture;
            for (int i = 0; i < items.Length; i++)
            {
                Span<byte> at = bytes.AsSpan(i * width, width);
                string item = items[i];
                switch (field)
                {
                    case TiffFieldType.Byte or TiffFieldType.Undefined: at[0] = byte.Parse(item, NumberStyles.None, c); break;
                    case TiffFieldType.SByte: at[0] = (byte)sbyte.Parse(item, NumberStyles.AllowLeadingSign, c); break;
                    case TiffFieldType.Short: BinaryPrimitives.WriteUInt16LittleEndian(at, ushort.Parse(item, NumberStyles.None, c)); break;
                    case TiffFieldType.SShort: BinaryPrimitives.WriteInt16LittleEndian(at, short.Parse(item, NumberStyles.AllowLeadingSign, c)); break;
                    case TiffFieldType.Long: BinaryPrimitives.WriteUInt32LittleEndian(at, uint.Parse(item, NumberStyles.None, c)); break;
                    case TiffFieldType.SLong: BinaryPrimitives.WriteInt32LittleEndian(at, int.Parse(item, NumberStyles.AllowLeadingSign, c)); break;
                    case TiffFieldType.Float: BinaryPrimitives.WriteSingleLittleEndian(at, float.Parse(item, NumberStyles.Float, c)); break;
                    case TiffFieldType.Double: BinaryPrimitives.WriteDoubleLittleEndian(at, double.Parse(item, NumberStyles.Float, c)); break;
                    default:
                    {
                        int slash = item.IndexOf('/');
                        string top = slash < 0 ? item : item[..slash], bottom = slash < 0 ? "1" : item[(slash + 1)..];
                        if (field == TiffFieldType.Rational)
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(at, uint.Parse(top, NumberStyles.None, c));
                            BinaryPrimitives.WriteUInt32LittleEndian(at[4..], uint.Parse(bottom, NumberStyles.None, c));
                        }
                        else
                        {
                            BinaryPrimitives.WriteInt32LittleEndian(at, int.Parse(top, NumberStyles.AllowLeadingSign, c));
                            BinaryPrimitives.WriteInt32LittleEndian(at[4..], int.Parse(bottom, NumberStyles.AllowLeadingSign, c));
                        }
                        break;
                    }
                }
            }
        }
        return ExifValue.FromRaw(field, count, Endianness.LittleEndian, bytes);
    }
}
