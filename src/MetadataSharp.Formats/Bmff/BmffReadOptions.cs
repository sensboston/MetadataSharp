using MetadataSharp.Core;

namespace MetadataSharp.Formats.Bmff;

/// <summary>Finite BMFF resource limits, captured on open.</summary>
public sealed record BmffReadOptions
{
    public MetadataLimits Limits { get; init; } = new();
    internal BmffReadOptions Snapshot()
    { ArgumentNullException.ThrowIfNull(Limits); Limits.Validate(); return this with { Limits = Limits with { } }; }
}
