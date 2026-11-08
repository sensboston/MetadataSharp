namespace MetadataSharp.Formats.Tiff;

public readonly record struct TiffByteRange(long Offset, long Length);
public enum TiffStorageKind { Strips, Tiles }
public enum TiffDirectoryLinkKind { NextIfd, SubIfd, Exif, Gps, Interoperability }
public enum TiffRangeKind { Header, Directory, FieldValue, Strip, Tile, JpegPreview }
public enum TiffLayoutIssueCode
{ UnsupportedCompression, UnsupportedSampleLayout, UnsupportedPointerLayout, UnknownFieldSemantics, MissingImageLayout }

public sealed class TiffFieldInfo
{
    internal TiffFieldInfo(ushort tagId, ushort fieldType, uint count, bool isInline, TiffByteRange valueRange)
    { TagId = tagId; FieldType = fieldType; Count = count; IsInline = isInline; ValueRange = valueRange; }
    public ushort TagId { get; }
    public ushort FieldType { get; }
    public uint Count { get; }
    public bool IsInline { get; }
    public TiffByteRange ValueRange { get; }
}

public sealed class TiffDirectoryLink
{
    internal TiffDirectoryLink(TiffDirectoryLinkKind kind, ushort? tagId, long pointerOffset, uint targetIfdOffset)
    { Kind = kind; TagId = tagId; PointerOffset = pointerOffset; TargetIfdOffset = targetIfdOffset; }
    public TiffDirectoryLinkKind Kind { get; }
    public ushort? TagId { get; }
    public long PointerOffset { get; }
    public uint TargetIfdOffset { get; }
}

public sealed class TiffAccountedRange
{
    internal TiffAccountedRange(TiffRangeKind kind, uint? directoryOffset, ushort? tagId, TiffByteRange range)
    { Kind = kind; DirectoryOffset = directoryOffset; TagId = tagId; Range = range; }
    public TiffRangeKind Kind { get; }
    public uint? DirectoryOffset { get; }
    public ushort? TagId { get; }
    public TiffByteRange Range { get; }
}

public sealed class TiffLayoutIssue
{
    internal TiffLayoutIssue(TiffLayoutIssueCode code, uint directoryOffset, ushort? tagId, string message)
    { Code = code; DirectoryOffset = directoryOffset; TagId = tagId; Message = message; }
    public TiffLayoutIssueCode Code { get; }
    public uint DirectoryOffset { get; }
    public ushort? TagId { get; }
    public string Message { get; }
}

public sealed class TiffImageLayout
{
    internal TiffImageLayout(uint width, uint height, ushort compression, TiffStorageKind storage, IReadOnlyList<TiffByteRange> segments, TiffByteRange? jpegPreview, bool isSupported)
    { Width = width; Height = height; Compression = compression; Storage = storage; Segments = segments; JpegPreview = jpegPreview; IsSupported = isSupported; }
    public uint Width { get; }
    public uint Height { get; }
    public ushort Compression { get; }
    public TiffStorageKind Storage { get; }
    public IReadOnlyList<TiffByteRange> Segments { get; }
    public TiffByteRange? JpegPreview { get; }
    public bool IsSupported { get; }
}

public sealed class TiffDirectoryInfo
{
    internal TiffDirectoryInfo(uint offset, TiffByteRange directoryRange, IReadOnlyList<TiffFieldInfo> fields, IReadOnlyList<TiffDirectoryLink> links, TiffImageLayout? image)
    { Offset = offset; DirectoryRange = directoryRange; Fields = fields; Links = links; Image = image; }
    public uint Offset { get; }
    public TiffByteRange DirectoryRange { get; }
    public IReadOnlyList<TiffFieldInfo> Fields { get; }
    public IReadOnlyList<TiffDirectoryLink> Links { get; }
    public TiffImageLayout? Image { get; }
}
