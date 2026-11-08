using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Dng;

/// <summary>Finite read-only DNG accounting budgets, captured before I/O.</summary>
public sealed record DngReadOptions
{
    public TiffReadOptions TiffOptions { get; init; } = new();
    public long MaxRawPixels { get; init; } = 100_000_000;
    public long MaxThumbnailPixels { get; init; } = 1_048_576;
    public int MaxThumbnailBytes { get; init; } = 61_440;
    internal DngReadOptions Snapshot()
    {
        ArgumentNullException.ThrowIfNull(TiffOptions);
        TiffReadOptions tiff = TiffOptions.Snapshot();
        if (MaxRawPixels <= 0 || MaxThumbnailPixels <= 0 || MaxThumbnailBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(DngReadOptions), "Pixel and thumbnail byte ceilings must be positive.");
        return this with { TiffOptions = tiff };
    }
}
