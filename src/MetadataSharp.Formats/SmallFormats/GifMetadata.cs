using MetadataSharp.Core;

namespace MetadataSharp.Formats.SmallFormats;

/// <summary>
/// Read-only metadata of a CompuServe GIF file. The format carries no EXIF, IPTC or XMP block, and
/// the reference tool (Exiv2) reads none: it reports the size the logical screen descriptor states
/// and nothing else, and writes nothing at all. This reader does the same. A file whose descriptor is
/// shorter than the file needs is not refused: the reference reports no size for it, and so does
/// this reader, as the two zeroes that say so.
/// </summary>
/// <remarks>
/// The logical screen descriptor is the seven bytes after the six-byte signature: the width and the
/// height as two little-endian unsigned words, then a packed field, the background colour and the
/// pixel aspect ratio. The width and height are what the reference reports; the rest of the header is
/// not read, and a colour table, an image or a comment extension is not walked.
/// </remarks>
public sealed class GifMetadata
{
    private GifMetadata(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>The width of the logical screen in pixels, as <c>exiv2 -ps</c> reports it; zero when the file states none.</summary>
    public int Width { get; }

    /// <summary>The height of the logical screen in pixels, as <c>exiv2 -ps</c> reports it; zero when the file states none.</summary>
    public int Height { get; }

    /// <summary>The type the reference tool gives a GIF file.</summary>
    public const string MimeType = "image/gif";

    /// <summary>Whether the bytes begin as a GIF file does, in either of its two versions.</summary>
    internal static bool HasSignature(ReadOnlySpan<byte> header) =>
        header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8));

    /// <summary>Reads the size a GIF file declares.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidGif"/> when the file does not begin as a GIF file. A
    /// descriptor that does not lie inside the file is not a refusal: the size is then the two zeroes
    /// the reference reports of a file it cannot take a size from.
    /// </exception>
    public static GifMetadata Read(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data)) throw new MetadataException(MetadataErrorCode.InvalidGif, "The file does not begin with the signature of a GIF file.", 0);
        // The four bytes the reference reads: the width then the height of the logical screen.
        if (data.Length < 10) return new GifMetadata(0, 0);
        return new GifMetadata(data[6] | (data[7] << 8), data[8] | (data[9] << 8));
    }
}
