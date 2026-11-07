namespace MetadataSharp.Core.Tiff.Serialization;

internal sealed record PlannedEntry(ushort TagId, TiffFieldType FieldType, uint Count,
    ExifValue? Value, int? DataOffset, uint? ScalarOverride);
internal sealed record PlannedDirectory(ExifDirectoryKind Kind, int Offset,
    uint NextIfdOffset, IReadOnlyList<PlannedEntry> Entries);

/// <summary>An owned, bounded layout description; no TIFF payload is emitted by planning.</summary>
internal sealed class ExifWritePlan
{
    private readonly byte[] thumbnail;

    internal ExifWritePlan(Endianness order, int length, IReadOnlyList<PlannedDirectory> directories,
        int? thumbnailOffset, ReadOnlySpan<byte> thumbnail)
    {
        ByteOrder = order;
        Length = length;
        Directories = directories;
        ThumbnailOffset = thumbnailOffset;
        this.thumbnail = thumbnail.ToArray();
    }

    internal Endianness ByteOrder { get; }
    internal int Length { get; }
    internal IReadOnlyList<PlannedDirectory> Directories { get; }
    internal int? ThumbnailOffset { get; }
    internal ReadOnlySpan<byte> Thumbnail => thumbnail;
}
