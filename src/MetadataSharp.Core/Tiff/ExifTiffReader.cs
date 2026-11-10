using System.Buffers.Binary;
using MetadataSharp.Core.Binary;

namespace MetadataSharp.Core.Tiff;

/// <summary>Reads the approved classic TIFF/EXIF subset without recursion or image decoding.</summary>
internal ref struct ExifTiffReader
{
    private readonly ReadOnlySpan<byte> data;
    private readonly MetadataLimits limits;
    private readonly long sourceBase;
    private readonly Endianness order;
    private readonly List<ExifEntry> entries;
    private readonly List<ExifDirectoryKind> directories;
    private readonly List<(int Start, int Length)> ranges;
    private readonly Queue<(uint Offset, ExifDirectoryKind Kind, int Depth)> pending;
    private readonly HashSet<uint> offsets;
    private readonly HashSet<ExifDirectoryKind> roles;
    private int totalEntries;
    private long retainedBytes;
    private int makerNoteStart;
    private int makerNoteLength;

    private ExifTiffReader(ReadOnlySpan<byte> data, MetadataLimits limits, long sourceBase)
    {
        this.data = data;
        this.limits = limits;
        this.sourceBase = sourceBase;
        order = data[0] == 0x49 ? Endianness.LittleEndian : Endianness.BigEndian;
        entries = [];
        directories = [];
        ranges = [(0, 8)];
        pending = new();
        offsets = [];
        roles = [];
        totalEntries = 0;
        retainedBytes = data.Length;
        makerNoteStart = 0;
        makerNoteLength = 0;
    }

    internal static TiffReadResult Read(ReadOnlySpan<byte> tiff, MetadataLimits? limits = null,
        long sourceBase = 0)
    {
        limits ??= new();
        limits.Validate();
        if (sourceBase < 0 || sourceBase > long.MaxValue - tiff.Length)
            throw new ArgumentOutOfRangeException(nameof(sourceBase));
        if (tiff.Length > 65_527)
            throw new MetadataException(MetadataErrorCode.ExifTooLarge, "The EXIF TIFF payload exceeds the APP1 limit.", sourceBase);
        if (tiff.Length > limits.MaxInputBytes || tiff.Length > limits.MaxRetainedMetadataBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EXIF payload exceeds the retained metadata budget.", sourceBase);
        if (tiff.Length < 8)
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The TIFF header is truncated.", sourceBase);
        if (!((tiff[0] == 0x49 && tiff[1] == 0x49) || (tiff[0] == 0x4D && tiff[1] == 0x4D)))
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The TIFF byte order mark is invalid.", sourceBase);
        var reader = new ExifTiffReader(tiff, limits, sourceBase);
        return reader.Parse();
    }

    private TiffReadResult Parse()
    {
        ushort magic = U16(2);
        if (magic == 43)
            throw Error(MetadataErrorCode.UnsupportedFeature, "BigTIFF is not supported.", 2);
        if (magic != 42)
            throw Error(MetadataErrorCode.InvalidExif, "The TIFF identifier is invalid.", 2);
        uint first = U32(4);
        if (first == 0)
            throw Error(MetadataErrorCode.InvalidExif, "The primary IFD is missing.", 4);
        Schedule(first, ExifDirectoryKind.Ifd0, 1);
        while (pending.Count != 0)
        {
            var item = pending.Dequeue();
            ReadDirectory(item.Offset, item.Kind, item.Depth);
        }
        byte[]? thumbnail = ReadThumbnail();
        MakerNote? makerNote = ReadMakerNote();
        return new TiffReadResult(new ExifMetadata(order, entries, makerNote),
            directories.AsReadOnly(), thumbnail);
    }

    private MakerNote? ReadMakerNote()
    {
        if (makerNoteLength == 0)
            return null;
        return MakerNotes.MakerNoteReader.TryRead(data, makerNoteStart, makerNoteLength, order, Make(), limits,
            limits.MaxRetainedMetadataBytes - retainedBytes);
    }

    /// <summary>The raw bytes of the 0th IFD ASCII Make tag, or empty.</summary>
    private readonly ReadOnlySpan<byte> Make()
    {
        foreach (ExifEntry entry in entries)
            if (entry.Key == new ExifTagKey(ExifDirectoryKind.Ifd0, 0x010F))
                return entry.Value.FieldType == TiffFieldType.Ascii ? entry.Value.RawBytes : default;
        return default;
    }

    private void Schedule(uint offset, ExifDirectoryKind kind, int depth)
    {
        if (offset == 0)
            return;
        if (!offsets.Add(offset))
            throw Error(MetadataErrorCode.InvalidExif, "An IFD offset is cyclic or shared.", offset);
        if (!roles.Add(kind))
            throw Error(MetadataErrorCode.UnsupportedFeature, "Multiple IFDs have the same directory role.", offset);
        if (offsets.Count > limits.MaxDirectories || depth > limits.MaxDirectoryDepth)
            throw Error(MetadataErrorCode.LimitExceeded, "The directory traversal budget was exceeded.", offset);
        pending.Enqueue((offset, kind, depth));
    }

    private void ReadDirectory(uint offset, ExifDirectoryKind kind, int depth)
    {
        int start = RequireRange(offset, 2);
        if ((offset & 1) != 0)
            throw Error(MetadataErrorCode.InvalidExif, "IFDs must begin on a word boundary.", offset);
        int count = U16(start);
        if (count > limits.MaxEntriesPerDirectory || count > limits.MaxTotalEntries - totalEntries)
            throw Error(MetadataErrorCode.LimitExceeded, "The IFD entry budget was exceeded.", offset);
        totalEntries += count;
        int length = 2 + count * 12 + 4; // Count is a validated ushort, so this cannot overflow.
        RequireRange(offset, length);
        ClaimRange(start, length);
        directories.Add(kind);
        var ids = new HashSet<ushort>();
        ushort previous = 0;
        for (int i = 0; i < count; i++)
        {
            int p = start + 2 + i * 12;
            ushort tag = U16(p);
            if (!ids.Add(tag))
                throw Error(MetadataErrorCode.AmbiguousMetadata, "The IFD contains a duplicate tag ID.", p);
            if (i != 0 && tag < previous)
                throw Error(MetadataErrorCode.InvalidExif, "IFD tags are not in ascending order.", p);
            previous = tag;
            ushort typeCode = U16(p + 2);
            if (typeCode is < 1 or > 12)
                throw Error(MetadataErrorCode.UnsupportedFeature, "The TIFF field type has no supported width.", p + 2);
            var type = (TiffFieldType)typeCode;
            uint valueCount = U32(p + 4);
            if (!CheckedRange.TryMultiply(valueCount, ExifValue.GetWidth(type), out int byteLength))
                throw Error(MetadataErrorCode.InvalidExif, "The TIFF value length is not representable.", p + 4);
            if (byteLength > limits.MaxValueBytes)
                throw Error(MetadataErrorCode.LimitExceeded, "The TIFF value exceeds the value budget.", p + 4);
            int valueStart = p + 8;
            if (byteLength > 4)
            {
                uint valueOffset = U32(p + 8);
                valueStart = RequireRange(valueOffset, byteLength);
                if ((valueOffset & 1) != 0)
                    throw Error(MetadataErrorCode.InvalidExif, "External TIFF values must be word aligned.", p + 8);
                ClaimRange(valueStart, byteLength);
            }
            ReserveBytes(byteLength, p);
            var entry = new ExifEntry(new ExifTagKey(kind, tag),
                ExifValue.FromRaw(type, valueCount, order, data.Slice(valueStart, byteLength)));
            entries.Add(entry);
            if (tag is 0x8769 or 0x8825 or 0xA005)
            {
                ExifDirectoryKind target = tag switch
                {
                    0x8769 => ExifDirectoryKind.Exif,
                    0x8825 => ExifDirectoryKind.Gps,
                    _ => ExifDirectoryKind.Interop
                };
                ExifDirectoryKind owner = tag == 0xA005 ? ExifDirectoryKind.Exif : ExifDirectoryKind.Ifd0;
                if (kind != owner || type != TiffFieldType.Long || valueCount != 1)
                    throw Error(MetadataErrorCode.InvalidExif, "The IFD pointer has an invalid directory, type or count.", p);
                Schedule(U32(p + 8), target, depth + 1);
            }
            if (tag is 0x014A or 0x0111 or 0x0117 or 0x0144 or 0x0145)
                throw Error(MetadataErrorCode.UnsupportedFeature, "SubIFDs, strips and tiles are outside the EXIF subset.", p);
            if (tag == 0x927C && kind == ExifDirectoryKind.Exif)
            {
                if (type != TiffFieldType.Undefined)
                    throw Error(MetadataErrorCode.InvalidExif, "MakerNote must use the Undefined field type.", p);
                makerNoteStart = valueStart;
                makerNoteLength = byteLength;
            }
        }
        uint next = U32(start + 2 + count * 12);
        if (next != 0)
        {
            if (kind != ExifDirectoryKind.Ifd0)
                throw Error(MetadataErrorCode.UnsupportedFeature, "Only the primary IFD may link to a thumbnail IFD.", start + length - 4);
            Schedule(next, ExifDirectoryKind.Thumbnail, depth + 1);
        }
    }

    private byte[]? ReadThumbnail()
    {
        if (!roles.Contains(ExifDirectoryKind.Thumbnail))
            return null;
        ExifEntry? compression = FindThumbnail(0x0103);
        ExifEntry? offset = FindThumbnail(0x0201);
        ExifEntry? length = FindThumbnail(0x0202);
        if (offset is null || length is null)
            throw Error(MetadataErrorCode.UnsupportedFeature, "Only a thumbnail with a JPEG offset/length pair is supported.", 0);
        if (compression is null || compression.Value.FieldType != TiffFieldType.Short ||
            compression.Value.Count != 1 || ReadScalar(compression.Value) != 6)
            throw Error(MetadataErrorCode.UnsupportedFeature, "Only JPEG-compressed thumbnails are supported.", 0);
        if (offset.Value.FieldType != TiffFieldType.Long || offset.Value.Count != 1 ||
            length.Value.FieldType != TiffFieldType.Long || length.Value.Count != 1)
            throw Error(MetadataErrorCode.InvalidExif, "The thumbnail offset and length must be scalar LONG values.", 0);
        uint byteLength = ReadScalar(length.Value);
        uint byteOffset = ReadScalar(offset.Value);
        if (byteLength > limits.MaxThumbnailBytes)
            throw Error(MetadataErrorCode.LimitExceeded, "The thumbnail budget was exceeded.", byteOffset);
        int start = RequireRange(byteOffset, byteLength);
        if ((byteOffset & 1) != 0 || byteLength < 4 || data[start] != 0xFF || data[start + 1] != 0xD8 ||
            data[start + (int)byteLength - 2] != 0xFF || data[start + (int)byteLength - 1] != 0xD9)
            throw Error(MetadataErrorCode.InvalidExif, "The thumbnail JPEG boundaries or alignment are invalid.", byteOffset);
        ClaimRange(start, (int)byteLength);
        ReserveBytes((int)byteLength, byteOffset);
        return data.Slice(start, (int)byteLength).ToArray();
    }

    private ExifEntry? FindThumbnail(ushort tag)
    {
        foreach (ExifEntry entry in entries)
            if (entry.Key == new ExifTagKey(ExifDirectoryKind.Thumbnail, tag))
                return entry;
        return null;
    }

    private uint ReadScalar(ExifValue value)
    {
        Span<byte> bytes = stackalloc byte[4];
        value.CopyRawTo(bytes);
        return value.FieldType == TiffFieldType.Short
            ? (order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes))
            : (order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes));
    }

    private int RequireRange(long offset, long length)
    {
        if (!CheckedRange.TryGetSpanRange(offset, length, data.Length, out int start, out _))
            throw Error(MetadataErrorCode.InvalidExif, "A TIFF range lies outside the EXIF payload.", offset);
        return start;
    }

    private void ClaimRange(int start, int length)
    {
        foreach (var range in ranges)
            if (start < range.Start + range.Length && range.Start < start + length)
                throw Error(MetadataErrorCode.InvalidExif, "TIFF structures or external values overlap.", start);
        ranges.Add((start, length));
    }

    private void ReserveBytes(int count, long at)
    {
        if (count > limits.MaxRetainedMetadataBytes - retainedBytes)
            throw Error(MetadataErrorCode.LimitExceeded, "The retained metadata budget was exceeded.", at);
        retainedBytes += count;
    }

    private ushort U16(int p) => order == Endianness.LittleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p, 2))
        : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(p, 2));

    private uint U32(int p) => order == Endianness.LittleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(p, 4));

    private MetadataException Error(MetadataErrorCode code, string message, long offset) =>
        new(code, message, offset >= 0 && offset <= data.Length ? sourceBase + offset : null);
}
