using MetadataSharp.Core;

namespace MetadataSharp.Formats.SmallFormats;

/// <summary>
/// Read-only metadata of a Truevision TARGA file. The format carries no EXIF, IPTC or XMP block; the
/// reference tool (Exiv2) reports the size the header states and writes nothing. This reader does the
/// same.
/// </summary>
/// <remarks>
/// <para>
/// A TARGA file has no signature that is always there. The reference tells one by its name (a path
/// that ends in <c>.tga</c>, in either case, anywhere in it) or by the footer the 2.0 version adds:
/// the last 26 bytes, in which the text <c>TRUEVISION-XFILE.</c> begins at the ninth. The library is
/// told the format by the bytes alone, so it takes the footer and never the name; a file of the
/// earlier version that has no footer is not recognised as a TARGA file, as the reference does not
/// recognise it either when it is read by its bytes.
/// </para>
/// <para>
/// The header is 18 bytes: the length of the image identification field, the colour map type, the
/// image type, five bytes of colour map specification, the two origins, the width and the height as
/// two little-endian words at 12 and 14, the pixel depth and the descriptor.
/// </para>
/// </remarks>
public sealed class TgaMetadata
{
    private TgaMetadata(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>The width the header states, in pixels.</summary>
    public int Width { get; }

    /// <summary>The height the header states, in pixels.</summary>
    public int Height { get; }

    /// <summary>The type the reference tool gives a TARGA file.</summary>
    public const string MimeType = "image/targa";

    /// <summary>
    /// The bytes of the footer the 2.0 version adds, from its ninth byte. The reference compares
    /// sixteen of them and does not look at the period that follows, so neither does this reader.
    /// </summary>
    private static ReadOnlySpan<byte> Footer => "TRUEVISION-XFILE"u8;

    /// <summary>The length of the footer, which ends the file.</summary>
    private const int FooterBytes = 26;

    /// <summary>
    /// Whether the bytes end with the signature of the 2.0 version, which is where a TARGA file is told
    /// from any other file. The signature is not at the start, so detection needs the whole file.
    /// </summary>
    internal static bool HasSignature(ReadOnlySpan<byte> file) =>
        file.Length >= FooterBytes && file[^FooterBytes..][8..].StartsWith(Footer);

    /// <summary>Reads the size a TARGA file declares.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidTga"/> when the file does not end with the signature of the
    /// 2.0 version.
    /// </exception>
    public static TgaMetadata Read(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data)) throw new MetadataException(MetadataErrorCode.InvalidTga, "The file does not end with the signature of a TARGA 2.0 file.", Math.Max(0, data.Length - FooterBytes));
        // A file that ends with the footer is at least twenty-six bytes long, so its eighteen-byte
        // header is always there; the reference takes the size from those bytes without checking.
        return new TgaMetadata(data[12] | (data[13] << 8), data[14] | (data[15] << 8));
    }
}
