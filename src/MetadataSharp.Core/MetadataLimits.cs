namespace MetadataSharp.Core;

/// <summary>Finite resource budgets shared by metadata readers and writers.</summary>
public sealed record MetadataLimits
{
    public long MaxInputBytes { get; init; } = 1_073_741_824;
    public int MaxSegments { get; init; } = 4_096;
    public int MaxRetainedMetadataBytes { get; init; } = 1_048_576;
    public int MaxDirectories { get; init; } = 16;
    public int MaxDirectoryDepth { get; init; } = 8;
    public int MaxEntriesPerDirectory { get; init; } = 512;
    public int MaxTotalEntries { get; init; } = 2_048;
    public int MaxValueBytes { get; init; } = 65_527;
    public int MaxThumbnailBytes { get; init; } = 61_440;
    public int CopyBufferBytes { get; init; } = 65_536;

    internal void Validate()
    {
        if (MaxInputBytes <= 0 || MaxSegments <= 0 || MaxRetainedMetadataBytes <= 0 ||
            MaxDirectories <= 0 || MaxDirectoryDepth <= 0 || MaxEntriesPerDirectory <= 0 ||
            MaxTotalEntries <= 0 || MaxValueBytes <= 0 || MaxThumbnailBytes <= 0 ||
            CopyBufferBytes is < 4_096 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(MetadataLimits), "Resource limits must be positive and the copy buffer must be between 4 KiB and 1 MiB.");
    }
}
