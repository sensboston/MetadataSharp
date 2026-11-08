using MetadataSharp.Core;

namespace MetadataSharp.Formats.Png;

/// <summary>Finite PNG and compressed-XMP resource limits, captured on open.</summary>
public sealed record PngReadOptions
{
    public MetadataLimits Limits { get; init; } = new();
    public int MaxCompressedXmpBytes { get; init; } = 1_048_576;
    public int MaxDecompressedXmpBytes { get; init; } = 1_048_576;
    internal PngReadOptions Snapshot()
    {
        ArgumentNullException.ThrowIfNull(Limits); Limits.Validate();
        if (MaxCompressedXmpBytes <= 0 || MaxDecompressedXmpBytes <= 0) throw new ArgumentOutOfRangeException(nameof(PngReadOptions));
        return this with { Limits = Limits with { } };
    }
}
