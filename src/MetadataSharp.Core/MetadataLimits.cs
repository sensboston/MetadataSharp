namespace MetadataSharp.Core;

/// <summary>Finite resource budgets shared by metadata readers and writers.</summary>
public sealed record MetadataLimits
{
    /// <summary>The longest input accepted, in bytes. Default 1,073,741,824.</summary>
    public long MaxInputBytes { get; init; } = 1_073_741_824;
    /// <summary>The most segments, chunks, boxes or image strips walked in one file. Default 4,096.</summary>
    public int MaxSegments { get; init; } = 4_096;
    /// <summary>The most metadata bytes one read keeps in memory. Default 1,048,576.</summary>
    public int MaxRetainedMetadataBytes { get; init; } = 1_048_576;
    /// <summary>The most directories in one EXIF block. Default 16.</summary>
    public int MaxDirectories { get; init; } = 16;
    /// <summary>The deepest nesting followed: of directory pointers in EXIF, of boxes in a BMFF file. Default 8.</summary>
    public int MaxDirectoryDepth { get; init; } = 8;
    /// <summary>The most entries in one directory. Default 512.</summary>
    public int MaxEntriesPerDirectory { get; init; } = 512;
    /// <summary>The most entries across all directories of one block. Default 2,048.</summary>
    public int MaxTotalEntries { get; init; } = 2_048;
    /// <summary>The longest single value, in bytes. Default 65,527, the most an EXIF block in a JPEG APP1 segment holds.</summary>
    public int MaxValueBytes { get; init; } = 65_527;
    /// <summary>The longest embedded thumbnail, in bytes. Default 61,440.</summary>
    public int MaxThumbnailBytes { get; init; } = 61_440;
    /// <summary>The buffer for stream copies, in bytes, from 4,096 to 1,048,576. Default 65,536.</summary>
    public int CopyBufferBytes { get; init; } = 65_536;

    // Validate the whole budget as one unit; parsers may then rely on positive bounds and a usable copy buffer.
    internal void Validate()
    {
        if (MaxInputBytes <= 0 || MaxSegments <= 0 || MaxRetainedMetadataBytes <= 0 ||
            MaxDirectories <= 0 || MaxDirectoryDepth <= 0 || MaxEntriesPerDirectory <= 0 ||
            MaxTotalEntries <= 0 || MaxValueBytes <= 0 || MaxThumbnailBytes <= 0 ||
            CopyBufferBytes is < 4_096 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(MetadataLimits), "Resource limits must be positive and the copy buffer must be between 4 KiB and 1 MiB.");
    }
}
