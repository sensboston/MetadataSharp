using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Bmff;

/// <summary>Finite BMFF resource limits, captured on open.</summary>
public sealed record BmffReadOptions
{
    /// <summary>
    /// The budgets. Unless set, those of a whole TIFF-based file (<see cref="TiffReadOptions.Limits"/>):
    /// <see cref="MetadataLimits.MaxValueBytes"/> 16,777,216 and
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/> 67,108,864. An item of a BMFF file has
    /// no size of its own to keep to, so the 65,527 bytes of a JPEG APP1 segment, which the defaults
    /// of <see cref="MetadataLimits"/> are sized for, do not bound its EXIF block.
    /// </summary>
    public MetadataLimits Limits { get; init; } = DefaultLimits;

    /// <summary>The budgets of a read that names none: the ones of a whole TIFF-based file.</summary>
    internal static MetadataLimits DefaultLimits => TiffReadOptions.FileLimits;
    internal BmffReadOptions Snapshot()
    { ArgumentNullException.ThrowIfNull(Limits); Limits.Validate(); return this with { Limits = Limits with { } }; }
}
