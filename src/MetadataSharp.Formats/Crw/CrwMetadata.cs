using MetadataSharp.Core;

namespace MetadataSharp.Formats.Crw;

/// <summary>
/// Read-only metadata of a Canon CRW file (CIFF, the raw format of Canon cameras before CR2). The
/// file has no EXIF block: its heap of records is walked, and the records the reference tool
/// (Exiv2) maps to EXIF tags are given as those tags, the Canon ones as a Canon MakerNote. The raw
/// data is never decoded. <see cref="CrwMetadataDocument"/> writes the records back.
/// </summary>
public sealed class CrwMetadata
{
    // Capture validated inputs and stable state for this CIFF object.
    internal CrwMetadata(ExifMetadata? exif, string? comment, long? previewOffset, long previewLength, long? thumbnailOffset, long thumbnailLength)
    {
        Exif = exif;
        Comment = comment;
        PreviewOffset = previewOffset;
        PreviewLength = previewLength;
        ThumbnailOffset = thumbnailOffset;
        ThumbnailLength = thumbnailLength;
    }

    /// <summary>The records that are tags of the Canon MakerNote, by the tag of the note, as the reader took them.</summary>
    internal IReadOnlyDictionary<ushort, (TiffFieldType Type, byte[] Bytes)> Canon { get; init; } = new Dictionary<ushort, (TiffFieldType Type, byte[] Bytes)>();

    /// <summary>The byte order of the file.</summary>
    internal bool Little { get; init; }

    /// <summary>
    /// The mapped records as EXIF tags, or null when the file has none of them. Make, Model and
    /// Orientation are tags of the primary directory; DateTimeOriginal, the pixel dimensions,
    /// ColorSpace, SubjectDistance, FNumber and ExposureTime are tags of the Exif directory; the
    /// Canon records (firmware, owner, serial and file number, focal length, camera settings, shot
    /// information, custom functions, picture information and others) are the entries of
    /// <see cref="ExifMetadata.MakerNote"/>. A thumbnail is listed as the reference lists it: the
    /// Compression, JPEGInterchangeFormat (zero) and JPEGInterchangeFormatLength tags of the
    /// thumbnail directory. <see cref="ExifMetadata.IsEditable"/> is false: the tags are a view of
    /// the records, not a block that could be written back.
    /// </summary>
    public ExifMetadata? Exif { get; }

    /// <summary>The user comment record of the file without the zero bytes that end it, or null when the file has none.</summary>
    public string? Comment { get; }

    /// <summary>Where the JPEG preview (record 0x2007 of the root directory) starts in the file, or null when the file has none.</summary>
    public long? PreviewOffset { get; }

    /// <summary>The length of the JPEG preview in bytes; zero when the file has none.</summary>
    public long PreviewLength { get; }

    /// <summary>Where the JPEG thumbnail (record 0x2008 of the root directory) starts in the file, or null when the file has none.</summary>
    public long? ThumbnailOffset { get; }

    /// <summary>The length of the JPEG thumbnail in bytes; zero when the file has none.</summary>
    public long ThumbnailLength { get; }

    /// <summary>
    /// Reads one complete CRW file held in memory. Every directory of the heap is walked;
    /// <see cref="MetadataLimits.MaxTotalEntries"/> bounds the records of the file,
    /// <see cref="MetadataLimits.MaxDirectoryDepth"/> how deep its directories nest, and
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/> the mapped values together.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidCrw"/> when the file does not start with the header of a
    /// CRW file, or a directory or a record lies outside the block that holds it or over its own
    /// entry; <see cref="MetadataErrorCode.LimitExceeded"/> when a budget is exceeded.
    /// </exception>
    public static CrwMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null) => CrwReader.Read(data, limits);
}
