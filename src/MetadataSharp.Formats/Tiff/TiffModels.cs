namespace MetadataSharp.Formats.Tiff;

/// <summary>A run of bytes of the source file.</summary>
/// <param name="Offset">The offset of the first byte, from the start of the file.</param>
/// <param name="Length">The number of bytes.</param>
public readonly record struct TiffByteRange(long Offset, long Length);

/// <summary>How the image data of a directory is divided.</summary>
public enum TiffStorageKind
{
    /// <summary>Strips: StripOffsets (273) and StripByteCounts (279).</summary>
    Strips,
    /// <summary>Tiles: TileOffsets (324) and TileByteCounts (325).</summary>
    Tiles
}

/// <summary>The pointer that leads from one directory to another.</summary>
public enum TiffDirectoryLinkKind
{
    /// <summary>The next-IFD offset that ends a directory.</summary>
    NextIfd,
    /// <summary>A target of SubIFDs (330).</summary>
    SubIfd,
    /// <summary>The Exif IFD pointer (34665).</summary>
    Exif,
    /// <summary>The GPS IFD pointer (34853).</summary>
    Gps,
    /// <summary>The Interoperability IFD pointer (40965).</summary>
    Interoperability
}

/// <summary>What an accounted range of the file holds.</summary>
public enum TiffRangeKind
{
    /// <summary>The eight bytes of the TIFF header.</summary>
    Header,
    /// <summary>A directory: its entry count, its entries and its next-IFD offset.</summary>
    Directory,
    /// <summary>A field value stored outside its directory entry.</summary>
    FieldValue,
    /// <summary>One strip of image data.</summary>
    Strip,
    /// <summary>One tile of image data.</summary>
    Tile,
    /// <summary>The JPEG stream named by JPEGInterchangeFormat (513) and its length (514).</summary>
    JpegPreview
}

/// <summary>Why a layout is outside the accounting profile.</summary>
public enum TiffLayoutIssueCode
{
    /// <summary>The compression (259) is not one the profile accounts for.</summary>
    UnsupportedCompression,
    /// <summary>The sample, photometric or tile layout is outside the profile.</summary>
    UnsupportedSampleLayout,
    /// <summary>Data is reached through pointers whose extent the profile does not establish.</summary>
    UnsupportedPointerLayout,
    /// <summary>A tag the profile does not know; it may hold an offset, so its bytes cannot be ruled safe to move.</summary>
    UnknownFieldSemantics,
    /// <summary>No directory of the file describes an image.</summary>
    MissingImageLayout
}

/// <summary>One entry of a directory: its tag, type and count, and where its value is.</summary>
public sealed class TiffFieldInfo
{
    internal TiffFieldInfo(ushort tagId, ushort fieldType, uint count, bool isInline, TiffByteRange valueRange)
    { TagId = tagId; FieldType = fieldType; Count = count; IsInline = isInline; ValueRange = valueRange; }
    /// <summary>The tag ID.</summary>
    public ushort TagId { get; }
    /// <summary>The field type as stored; see <see cref="MetadataSharp.Core.TiffFieldType"/> for the classic values.</summary>
    public ushort FieldType { get; }
    /// <summary>The number of elements of the field type.</summary>
    public uint Count { get; }
    /// <summary>True when the value is the four value bytes of the entry itself.</summary>
    public bool IsInline { get; }
    /// <summary>Where the value is: inside the entry when <see cref="IsInline"/>, otherwise at the offset the entry gives.</summary>
    public TiffByteRange ValueRange { get; }
}

/// <summary>A pointer from one directory to another.</summary>
public sealed class TiffDirectoryLink
{
    internal TiffDirectoryLink(TiffDirectoryLinkKind kind, ushort? tagId, long pointerOffset, uint targetIfdOffset)
    { Kind = kind; TagId = tagId; PointerOffset = pointerOffset; TargetIfdOffset = targetIfdOffset; }
    /// <summary>The kind of pointer.</summary>
    public TiffDirectoryLinkKind Kind { get; }
    /// <summary>The tag that holds the pointer, or null for a next-IFD offset.</summary>
    public ushort? TagId { get; }
    /// <summary>The offset in the file of the four bytes of the pointer.</summary>
    public long PointerOffset { get; }
    /// <summary>The offset of the directory the pointer leads to.</summary>
    public uint TargetIfdOffset { get; }
}

/// <summary>A range of the file together with what holds it.</summary>
public sealed class TiffAccountedRange
{
    internal TiffAccountedRange(TiffRangeKind kind, uint? directoryOffset, ushort? tagId, TiffByteRange range)
    { Kind = kind; DirectoryOffset = directoryOffset; TagId = tagId; Range = range; }
    /// <summary>What the range holds.</summary>
    public TiffRangeKind Kind { get; }
    /// <summary>The offset of the directory the range belongs to, or null for the header.</summary>
    public uint? DirectoryOffset { get; }
    /// <summary>The tag whose value or image data the range is, or null for a header or a directory.</summary>
    public ushort? TagId { get; }
    /// <summary>The bytes.</summary>
    public TiffByteRange Range { get; }
}

/// <summary>One reason a layout is outside the accounting profile.</summary>
public sealed class TiffLayoutIssue
{
    internal TiffLayoutIssue(TiffLayoutIssueCode code, uint directoryOffset, ushort? tagId, string message)
    { Code = code; DirectoryOffset = directoryOffset; TagId = tagId; Message = message; }
    /// <summary>The kind of issue.</summary>
    public TiffLayoutIssueCode Code { get; }
    /// <summary>The offset of the directory the issue was found in.</summary>
    public uint DirectoryOffset { get; }
    /// <summary>The tag the issue is about, or null when it concerns the directory as a whole.</summary>
    public ushort? TagId { get; }
    /// <summary>A description of the issue in English.</summary>
    public string Message { get; }
}

/// <summary>The image a directory describes and where its data is.</summary>
public sealed class TiffImageLayout
{
    internal TiffImageLayout(uint width, uint height, ushort compression, TiffStorageKind storage, IReadOnlyList<TiffByteRange> segments, TiffByteRange? jpegPreview, bool isSupported)
    { Width = width; Height = height; Compression = compression; Storage = storage; Segments = segments; JpegPreview = jpegPreview; IsSupported = isSupported; }
    /// <summary>ImageWidth (256).</summary>
    public uint Width { get; }
    /// <summary>ImageLength (257).</summary>
    public uint Height { get; }
    /// <summary>Compression (259).</summary>
    public ushort Compression { get; }
    /// <summary>Whether the image data is in strips or in tiles.</summary>
    public TiffStorageKind Storage { get; }
    /// <summary>The strips or tiles, in the order of their offset arrays.</summary>
    public IReadOnlyList<TiffByteRange> Segments { get; }
    /// <summary>The JPEG stream of the directory (tags 513 and 514), or null.</summary>
    public TiffByteRange? JpegPreview { get; }
    /// <summary>False when the image is outside the accounting profile; the issues of the layout say why.</summary>
    public bool IsSupported { get; }
}

/// <summary>One directory of the file.</summary>
public sealed class TiffDirectoryInfo
{
    internal TiffDirectoryInfo(uint offset, TiffByteRange directoryRange, IReadOnlyList<TiffFieldInfo> fields, IReadOnlyList<TiffDirectoryLink> links, TiffImageLayout? image)
    { Offset = offset; DirectoryRange = directoryRange; Fields = fields; Links = links; Image = image; }
    /// <summary>The offset of the directory in the file.</summary>
    public uint Offset { get; }
    /// <summary>The bytes of the directory: its entry count, its entries and its next-IFD offset.</summary>
    public TiffByteRange DirectoryRange { get; }
    /// <summary>The entries, in directory order.</summary>
    public IReadOnlyList<TiffFieldInfo> Fields { get; }
    /// <summary>The pointers that leave the directory.</summary>
    public IReadOnlyList<TiffDirectoryLink> Links { get; }
    /// <summary>The image the directory describes, or null when it describes none.</summary>
    public TiffImageLayout? Image { get; }
}
