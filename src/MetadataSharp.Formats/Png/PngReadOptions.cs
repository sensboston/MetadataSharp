using MetadataSharp.Core;

namespace MetadataSharp.Formats.Png;

/// <summary>Finite PNG and compressed-XMP resource limits, captured on open.</summary>
public sealed record PngReadOptions
{
    /// <summary>The budgets of the document.</summary>
    public MetadataLimits Limits { get; init; } = new();
    /// <summary>The longest compressed XMP text accepted, in bytes. Default 1,048,576.</summary>
    public int MaxCompressedXmpBytes { get; init; } = 1_048_576;
    /// <summary>The longest XMP packet after decompression, and the longest packet written, in bytes. Default 1,048,576.</summary>
    public int MaxDecompressedXmpBytes { get; init; } = 1_048_576;
    internal PngReadOptions Snapshot()
    {
        ArgumentNullException.ThrowIfNull(Limits); Limits.Validate();
        if (MaxCompressedXmpBytes <= 0 || MaxDecompressedXmpBytes <= 0) throw new ArgumentOutOfRangeException(nameof(PngReadOptions));
        return this with { Limits = Limits with { } };
    }
}
