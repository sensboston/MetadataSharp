using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Formats.Jxl;

/// <summary>
/// Read-only metadata of a JPEG XL file. A file in the container form (ISO BMFF boxes after a
/// signature box, major brand <c>jxl </c>) can hold an EXIF block, an XMP packet and JUMBF boxes; a
/// bare codestream holds none of them. The codestream is never decoded and nothing is written.
/// </summary>
public sealed class JxlMetadata
{
    // Internal rather than private: the box walk that produces the parts is a separate type.
    internal JxlMetadata(ExifMetadata? exif, XmpPacket? xmp, bool hasJumbf)
    {
        Exif = exif;
        Xmp = xmp;
        HasJumbf = hasJumbf;
    }

    /// <summary>
    /// The EXIF block of the <c>Exif</c> box, plain or Brotli-compressed in a <c>brob</c> box, or
    /// null when the file has neither. A block that only the read-only rules accept comes back
    /// with <see cref="ExifMetadata.IsEditable"/> false.
    /// </summary>
    public ExifMetadata? Exif { get; }

    /// <summary>
    /// The XMP packet of the <c>xml </c> box, plain or Brotli-compressed in a <c>brob</c> box, or
    /// null when the file has neither.
    /// </summary>
    public XmpPacket? Xmp { get; }

    /// <summary>
    /// Whether the file has a <c>jumb</c> box (JUMBF, the carrier of content credentials among other
    /// things), plain or compressed. The box is only noticed: its content is neither decompressed,
    /// read nor checked.
    /// </summary>
    public bool HasJumbf { get; }

    /// <summary>
    /// Reads one complete JPEG XL file held in memory. A bare codestream (it starts with the bytes
    /// <c>FF 0A</c>) has no metadata and gives an empty result. Of a container only the top-level
    /// boxes are walked; the codestream boxes are stepped over. Without <paramref name="limits"/>
    /// the budgets are those of a whole TIFF-based file, as for <c>BmffMetadata.Read</c>;
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/> bounds the EXIF block and the packet
    /// together, each by its length after decompression: a compressed box is never decoded past
    /// what the budget has room for.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidBmff"/> when the file is neither form, the box structure
    /// does not hold, the major brand is not <c>jxl </c>, a metadata box appears twice or a
    /// compressed metadata box does not hold one whole Brotli stream;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when a budget is exceeded; otherwise the code
    /// the EXIF reader or the XMP reader reports for what it refused.
    /// </exception>
    /// <param name="data">Complete input file bytes, beginning at the container signature.</param>
    /// <param name="limits">Resource budgets for parsing; null selects the default limits.</param>
    /// <returns>The parsed JxlMetadata for the complete input file.</returns>
    public static JxlMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null) =>
        JxlReader.Read(data, limits);
}
