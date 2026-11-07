using MetadataSharp.Core;

namespace MetadataSharp.Formats.Jpeg;

/// <summary>Reader options with finite resource limits.</summary>
public sealed record JpegReadOptions
{
    public MetadataLimits Limits { get; init; } = new();
}
