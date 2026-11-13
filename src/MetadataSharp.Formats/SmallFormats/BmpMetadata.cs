using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.SmallFormats;

/// <summary>
/// Read-only metadata of a Windows bitmap file. The format carries no EXIF, IPTC or XMP block, and
/// the reference tool (Exiv2) reads none: it reports the size the information header states and
/// writes nothing. This reader does the same, for both header layouts the reference reads.
/// </summary>
/// <remarks>
/// <para>
/// After the two-byte <c>BM</c> signature the file header holds the size, two reserved words and the
/// offset of the pixel data, and at offset 14 a header whose size word tells its kind: 12 is the
/// OS/2 1.x and Windows 2.x <c>BITMAPCOREHEADER</c>, whose width and height are unsigned 16-bit
/// words at 18 and 20; any other size is a <c>BITMAPINFOHEADER</c> or a later one, whose width and
/// height are unsigned 32-bit words at 18 and 22.
/// </para>
/// <para>
/// The words are read as unsigned, as the reference reads them: a height of the later layout that
/// states rows stored top first is written as a negative number in the file, and the number read is
/// then the two's complement of it, which is what the reference reports.
/// </para>
/// </remarks>
public sealed class BmpMetadata
{
    // Capture validated inputs and stable state for this metadata object.
    private BmpMetadata(uint width, uint height, int headerBytes)
    {
        Width = width;
        Height = height;
        HeaderBytes = headerBytes;
    }

    /// <summary>The width the information header states, in pixels; zero when the file states none.</summary>
    public uint Width { get; }

    /// <summary>The height the information header states, in pixels, read as the unsigned word it is; zero when the file states none.</summary>
    public uint Height { get; }

    /// <summary>The size word of the information header: 12 for the core header, otherwise its declared size; zero when the file states none.</summary>
    public int HeaderBytes { get; }

    /// <summary>The type the reference tool gives a bitmap file.</summary>
    public const string MimeType = "image/x-ms-bmp";

    /// <summary>How many bytes of the file the reference reads before it takes the size from them.</summary>
    private const int ProbeBytes = 26;

    /// <summary>Whether the bytes begin as a bitmap file does.</summary>
    internal static bool HasSignature(ReadOnlySpan<byte> header) =>
        header.Length >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M';

    /// <summary>Reads the size a bitmap file declares.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidBmp"/> when the file does not begin as a bitmap file. A
    /// header that does not lie inside the file is not a refusal: the size is then the two zeroes the
    /// reference reports of a file it cannot take a size from.
    /// </exception>
    public static BmpMetadata Read(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data)) throw new MetadataException(MetadataErrorCode.InvalidBmp, "The file does not begin with the signature of a Windows bitmap.", 0);
        // The reference reads twenty-six bytes at once and takes the size word at 14 out of them; a
        // file shorter than that states no size at all to it.
        if (data.Length < ProbeBytes) return new BmpMetadata(0, 0, 0);
        int headerBytes = BinaryPrimitives.ReadInt32LittleEndian(data[14..]);
        if (headerBytes == 12)
            return new BmpMetadata(BinaryPrimitives.ReadUInt16LittleEndian(data[18..]), BinaryPrimitives.ReadUInt16LittleEndian(data[20..]), headerBytes);
        return new BmpMetadata(BinaryPrimitives.ReadUInt32LittleEndian(data[18..]), BinaryPrimitives.ReadUInt32LittleEndian(data[22..]), headerBytes);
    }
}
