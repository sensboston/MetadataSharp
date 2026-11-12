using MetadataSharp.Core;

namespace MetadataSharp.Formats.Internal;

/// <summary>
/// Where a reader of several kinds of metadata leaves what the reader of one kind refused, so that
/// it can go on to the next kind. Only <see cref="ImageMetadata"/> hands one in. A reader called
/// without one fails as a whole on the first refusal, which is what its public entry point documents.
/// </summary>
internal sealed class KindRefusals
{
    /// <summary>What the EXIF reader refused, or null.</summary>
    internal MetadataException? Exif;

    /// <summary>What the IPTC reader refused, or null.</summary>
    internal MetadataException? Iptc;

    /// <summary>What the XMP reader refused, or null.</summary>
    internal MetadataException? Xmp;
}
