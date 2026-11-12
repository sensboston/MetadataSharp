using MetadataSharp.Core;

namespace MetadataSharp.Formats.Jpeg;

/// <summary>Reader options with finite resource limits.</summary>
public sealed record JpegReadOptions
{
    /// <summary>The budgets of the document.</summary>
    public MetadataLimits Limits { get; init; } = new();
}
