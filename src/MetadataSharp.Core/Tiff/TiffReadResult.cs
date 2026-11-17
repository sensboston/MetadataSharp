namespace MetadataSharp.Core.Tiff;

/// <summary>Reader-owned state for subsequent layout planning, not a public format API.</summary>
internal sealed record TiffReadResult(
    ExifMetadata Metadata,
    IReadOnlyList<ExifDirectoryKind> Directories,
    byte[]? Thumbnail);
