namespace MetadataSharp.Formats.Jpeg;

internal sealed record JpegSegment(byte Marker, long Start, long Length, long PayloadOffset, int PayloadLength, bool IsExif, bool IsStandardXmp = false, bool IsExtendedXmp = false, bool IsPhotoshop = false);
internal sealed record JpegScanResult(long Length, IReadOnlyList<JpegSegment> Segments,
    JpegSegment? Exif, long InsertionOffset, long ScanOffset, long EntropyOffset, long EoiOffset,
    int ScanCount, int FrameCount);
