namespace MetadataSharp.Core.Tiff.Serialization;

internal sealed record PlannedEntry(ushort TagId, TiffFieldType FieldType, uint Count,
    ExifValue? Value, int? DataOffset, uint? ScalarOverride);
/// <summary>One sorted IFD and its exact output position and next-directory link.</summary>
internal sealed record PlannedDirectory(ExifDirectoryKind Kind, int Offset,
    uint NextIfdOffset, IReadOnlyList<PlannedEntry> Entries);

/// <summary>An owned, bounded layout description; no TIFF payload is emitted by planning.</summary>
internal sealed class ExifWritePlan
{
    private readonly byte[] thumbnail;

    // Copy directory and entry collections plus thumbnail bytes so later caller mutation cannot invalidate the plan.
    internal ExifWritePlan(Endianness order, int length, IReadOnlyList<PlannedDirectory> directories,
        int? thumbnailOffset, ReadOnlySpan<byte> thumbnail, int ceiling = ExifTiffReader.SegmentCeiling)
    {
        ByteOrder = order;
        Length = length;
        Ceiling = ceiling;
        ArgumentNullException.ThrowIfNull(directories);
        Directories = Array.AsReadOnly(directories.Select(d => d with
        {
            Entries = Array.AsReadOnly(d.Entries.ToArray())
        }).ToArray());
        ThumbnailOffset = thumbnailOffset;
        this.thumbnail = thumbnail.ToArray();
    }

    /// <summary>The byte order used for the TIFF header, directories, and values.</summary>
    internal Endianness ByteOrder { get; }
    internal int Length { get; }

    /// <summary>The longest block the container of the plan holds; the serializer refuses a longer plan.</summary>
    internal int Ceiling { get; }
    internal IReadOnlyList<PlannedDirectory> Directories { get; }
    internal int? ThumbnailOffset { get; }
    internal ReadOnlySpan<byte> Thumbnail => thumbnail;
}
