namespace MetadataSharp.Core;

/// <summary>A stable classification of a metadata operation failure.</summary>
public enum MetadataErrorCode
{
    InvalidJpeg,
    InvalidExif,
    UnsupportedFeature,
    LimitExceeded,
    AmbiguousMetadata,
    UnsafeRelocation,
    ExifTooLarge,
    SourceChanged,
    InvalidXmp,
    XmpTooLarge,
    InvalidIptc,
    InvalidPhotoshopResources,
    IptcTooLarge,
    InvalidPng,
    InvalidWebP,
    InvalidTiff,
    InvalidDng,
    InvalidPsd,
    InvalidJp2,
    InvalidEps,
    InvalidExv
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

    public MetadataErrorCode Code { get; }
    public long? SourceOffset { get; }
}
