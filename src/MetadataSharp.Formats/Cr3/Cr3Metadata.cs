using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Formats.Cr3;

/// <summary>
/// Read-only metadata of a Canon CR3 file (ISO BMFF, major brand <c>crx </c>): the EXIF directories
/// of the CMT boxes and the XMP packet. The image tracks are never read and nothing is written. The
/// previews of the file are listed and extracted by <see cref="Previews"/>.
/// </summary>
public sealed class Cr3Metadata
{
    // Internal rather than private: the box walk that produces the parts is a separate type.
    internal Cr3Metadata(ExifMetadata? exif, XmpPacket? xmp)
    {
        Exif = exif;
        Xmp = xmp;
    }

    /// <summary>
    /// The directories of the Canon box of the movie box as one EXIF block, or null when the file has
    /// none of them. Each directory is stored as a TIFF of its own: CMT1 is the primary directory,
    /// CMT2 the Exif directory and CMT4 the GPS directory, and CMT3 is the Canon MakerNote, decoded
    /// as <see cref="ExifMetadata.MakerNote"/>. No tag points from one directory to the next, so the
    /// block has no ExifTag, GPSTag or MakerNote entry. <see cref="ExifMetadata.IsEditable"/> is false.
    /// </summary>
    public ExifMetadata? Exif { get; }

    /// <summary>The XMP packet of the XMP box at the top level of the file, or null when there is none.</summary>
    public XmpPacket? Xmp { get; }

    /// <summary>
    /// Reads one complete CR3 file held in memory. Only the top level, the movie box and the Canon
    /// box inside it are opened; every other box is stepped over. Without <paramref name="limits"/>
    /// the budgets are those of a whole TIFF-based file, as for <c>BmffMetadata.Read</c>;
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/> bounds the four directories and the
    /// packet together.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidBmff"/> when the box structure does not hold, a metadata box
    /// appears twice or the major brand is not <c>crx </c>;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when a budget is exceeded;
    /// <see cref="MetadataErrorCode.AmbiguousMetadata"/> when two boxes declare the same directory;
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the directories differ in byte order;
    /// otherwise the code the EXIF directory reader or the XMP reader reports for what it refused.
    /// </exception>
    public static Cr3Metadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null) =>
        Cr3Reader.Read(data, limits);
}
