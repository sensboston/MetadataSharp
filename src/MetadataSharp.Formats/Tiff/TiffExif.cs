using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// Read-only EXIF access to a whole TIFF-based file: TIFF, DNG and the TIFF-based raw formats
/// (CR2, NEF, NRW, ARW, SR2, PEF, ORF, RW2). The primary directory, the EXIF, GPS and
/// interoperability directories and the directory chained after the primary one are read, and a
/// recognised MakerNote is decoded. Image data is never read: strip, tile and SubIFD tags are returned
/// as entries and nothing they point at is visited. A file the strict rules refuse for an unknown
/// field type or for values that share bytes is read with <see cref="ExifMetadata.IsEditable"/> false,
/// the entry of unknown type left out. This API does not write files.
/// </summary>
public static class TiffExif
{
    /// <summary>
    /// Reads the EXIF metadata of a complete TIFF-based file held in memory.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidTiff"/> when the data does not begin with a TIFF, ORF or RW2
    /// header; <see cref="MetadataErrorCode.LimitExceeded"/> when a budget of <paramref name="limits"/>
    /// is exceeded; otherwise the code the EXIF directory reader reports for the structure it refused.
    /// </exception>
    public static ExifMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        if (data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The TIFF file exceeds the input budget.", 0);
        if (!ExifTiffReader.HasFileHeader(data))
            throw new MetadataException(MetadataErrorCode.InvalidTiff, "The data does not begin with a TIFF header.", 0);
        return ExifTiffReader.InspectFile(data, limits);
    }
}
