using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Dng;

/// <summary>Finite read-only DNG accounting budgets, captured before I/O.</summary>
public sealed record DngReadOptions
{
    /// <summary>The budgets of the TIFF structure of the file.</summary>
    public TiffReadOptions TiffOptions { get; init; } = new();
    /// <summary>The most pixels (width times height) of the raw image. Default 100,000,000.</summary>
    public long MaxRawPixels { get; init; } = 100_000_000;
    /// <summary>The most pixels of the reduced RGB image. Default 1,048,576.</summary>
    public long MaxThumbnailPixels { get; init; } = 1_048_576;
    /// <summary>The most bytes of image data of the reduced RGB image. Default 61,440.</summary>
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
