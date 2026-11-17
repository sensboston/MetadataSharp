namespace MetadataSharp.Core;

/// <summary>Identifies a supported EXIF directory role, not a physical offset.</summary>
public enum ExifDirectoryKind
{
    /// <summary>The primary image directory (IFD0).</summary>
    Ifd0,
    /// <summary>The Exif directory, reached from tag 0x8769.</summary>
    Exif,
    /// <summary>The GPS directory, reached from tag 0x8825.</summary>
    Gps,
    /// <summary>The Interoperability directory, reached from tag 0xA005.</summary>
    Interop,
    /// <summary>The thumbnail directory (IFD1).</summary>
    Thumbnail
}

/// <summary>Identifies a tag by directory role and numeric ID, including unknown IDs.</summary>
/// <param name="Directory">The directory role containing the tag.</param>
/// <param name="TagId">The numeric tag identifier.</param>
public readonly record struct ExifTagKey(ExifDirectoryKind Directory, ushort TagId);
