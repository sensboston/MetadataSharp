namespace MetadataSharp.Core;

/// <summary>A stable classification of a metadata operation failure.</summary>
public enum MetadataErrorCode
{
    /// <summary>The JPEG structure is malformed or truncated.</summary>
    InvalidJpeg,
    /// <summary>The EXIF block is malformed or truncated.</summary>
    InvalidExif,
    /// <summary>The input is valid and uses something this release does not read or does not edit.</summary>
    UnsupportedFeature,
    /// <summary>A budget of the limits in effect was exceeded.</summary>
    LimitExceeded,
    /// <summary>The file holds the same metadata more than once, and no rule says which is meant.</summary>
    AmbiguousMetadata,
    /// <summary>The edit would have to move or rebuild bytes that cannot be shown safe to move, or the planned output failed its verification; nothing was written.</summary>
    UnsafeRelocation,
    /// <summary>The EXIF block is longer than the container can hold.</summary>
    ExifTooLarge,
    /// <summary>The source is no longer the data that was opened.</summary>
    SourceChanged,
    /// <summary>The XMP packet is not UTF-8, is not well-formed XML or has no RDF root.</summary>
    InvalidXmp,
    /// <summary>The XMP packet is longer than the container can hold.</summary>
    XmpTooLarge,
    /// <summary>The IPTC stream is malformed or truncated.</summary>
    InvalidIptc,
    /// <summary>The Photoshop image resources are malformed or truncated.</summary>
    InvalidPhotoshopResources,
    /// <summary>The IPTC stream is longer than the container can hold.</summary>
    IptcTooLarge,
    /// <summary>The PNG structure is malformed or truncated.</summary>
    InvalidPng,
    /// <summary>The WebP structure is malformed or truncated.</summary>
    InvalidWebP,
    /// <summary>The TIFF structure is malformed or truncated.</summary>
    InvalidTiff,
    /// <summary>The DNG structure is malformed or truncated.</summary>
    InvalidDng,
    /// <summary>The PSD structure is malformed or truncated.</summary>
    InvalidPsd,
    /// <summary>The JPEG 2000 structure is malformed or truncated.</summary>
    InvalidJp2,
    /// <summary>The EPS structure is malformed or truncated.</summary>
    InvalidEps,
    /// <summary>The EXV structure is malformed or truncated.</summary>
    InvalidExv,
    /// <summary>The ISO base media file structure (HEIF, AVIF, CR3, JPEG XL) is malformed or truncated.</summary>
    InvalidBmff,
    /// <summary>The ICC profile is malformed: too short, a wrong size field or no <c>acsp</c> signature.</summary>
    InvalidIccProfile,
    /// <summary>The ICC profile is valid and longer than the container can hold.</summary>
    IccProfileTooLarge,
    /// <summary>The comment is longer than the container can hold.</summary>
    CommentTooLarge
}

/// <summary>A data or safety error with an optional absolute offset in the input.</summary>
public sealed class MetadataException : Exception
{
    internal MetadataException(MetadataErrorCode code, string message, long? sourceOffset = null)
        : base(message)
    {
        Code = code;
        SourceOffset = sourceOffset;
    }

    /// <summary>The classification of the failure.</summary>
    public MetadataErrorCode Code { get; }
    /// <summary>The offset in the input the failure was found at, or null when none applies.</summary>
    public long? SourceOffset { get; }
}
