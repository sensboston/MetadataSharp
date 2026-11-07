namespace MetadataSharp.Core;

/// <summary>Identifies a supported EXIF directory role, not a physical offset.</summary>
public enum ExifDirectoryKind
{
    Ifd0,
    Exif,
    Gps,
    Interop,
    Thumbnail
}

/// <summary>Identifies a tag by directory role and numeric ID, including unknown IDs.</summary>
public readonly record struct ExifTagKey(ExifDirectoryKind Directory, ushort TagId);
