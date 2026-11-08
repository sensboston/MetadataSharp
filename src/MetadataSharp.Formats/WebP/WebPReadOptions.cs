using MetadataSharp.Core;

namespace MetadataSharp.Formats.WebP;

/// <summary>Finite static WebP resource limits, captured on open.</summary>
public sealed record WebPReadOptions
{
    public MetadataLimits Limits { get; init; } = new();
    internal WebPReadOptions Snapshot()
    { ArgumentNullException.ThrowIfNull(Limits); Limits.Validate(); return this with { Limits = Limits with { } }; }
}
