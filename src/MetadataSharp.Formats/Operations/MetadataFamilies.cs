namespace MetadataSharp.Formats.Operations;

/// <summary>The kinds of metadata an operation of <see cref="MetadataOperations"/> works on: the targets of <c>exiv2 -d</c>, <c>-e</c> and <c>-i</c>.</summary>
[Flags]
public enum MetadataFamilies
{
    /// <summary>Nothing.</summary>
    None = 0,
    /// <summary>The EXIF block, its thumbnail with it (target <c>e</c>).</summary>
    Exif = 1,
    /// <summary>The IPTC stream (target <c>i</c>).</summary>
    Iptc = 2,
    /// <summary>The XMP packet (target <c>x</c>).</summary>
    Xmp = 4,
    /// <summary>The comment of a JPEG or PNG file (target <c>c</c>).</summary>
    Comment = 8,
    /// <summary>The ICC profile (target <c>C</c>).</summary>
    IccProfile = 16,
    /// <summary>Only the thumbnail of the EXIF block (target <c>t</c>).</summary>
    Thumbnail = 32,
    /// <summary>Every kind (target <c>a</c>).</summary>
    All = Exif | Iptc | Xmp | Comment | IccProfile,
}
