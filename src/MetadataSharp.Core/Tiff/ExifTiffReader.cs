using System.Buffers.Binary;
using MetadataSharp.Core.Binary;

namespace MetadataSharp.Core.Tiff;

/// <summary>
/// Reads the approved classic TIFF/EXIF subset without recursion or image decoding. Two entry points
/// share one walk: <see cref="Read"/> takes the TIFF payload of an APP1-sized EXIF block, and
/// <see cref="ReadFile"/> takes a whole TIFF-based file, whose image directories are left alone.
/// Both refuse whatever the editable profile cannot write back. <see cref="Inspect"/> and
/// <see cref="InspectFile"/> are for reading only: when the strict walk refuses the input they walk it
/// again under the read-only rules, which accept three structures the TIFF and Exif specifications
/// allow and mark the result as not editable. Malformed input is refused by both walks.
/// </summary>
internal ref struct ExifTiffReader
{
    private const ushort ClassicMagic = 42;
    private const ushort BigTiffMagic = 43;
    private const ushort OrfMagic = 0x4F52; // "RO" after "II", "OR" after "MM"
    private const ushort Rw2Magic = 0x0055;

    private readonly ReadOnlySpan<byte> data;
    private readonly MetadataLimits limits;
    private readonly long sourceBase;
    private readonly bool wholeFile;
    private readonly bool readOnly;
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
    private int dngPrivateStart;
    private int dngPrivateLength;

    private ExifTiffReader(ReadOnlySpan<byte> data, MetadataLimits limits, long sourceBase, bool wholeFile,
        bool readOnly)
    {
        this.data = data;
        this.limits = limits;
        this.sourceBase = sourceBase;
        this.wholeFile = wholeFile;
        this.readOnly = readOnly;
        order = data[0] == 0x49 ? Endianness.LittleEndian : Endianness.BigEndian;
        entries = [];
        directories = [];
        ranges = [(0, 8)];
        pending = new();
        offsets = [];
        roles = [];
        totalEntries = 0;
        // An APP1 payload is retained as a whole. A file is not: only what is read out of it counts.
        retainedBytes = wholeFile ? 0 : data.Length;
        makerNoteStart = 0;
        makerNoteLength = 0;
        dngPrivateStart = 0;
        dngPrivateLength = 0;
    }

    internal static TiffReadResult Read(ReadOnlySpan<byte> tiff, MetadataLimits? limits = null,
        long sourceBase = 0) => ReadBlock(tiff, limits, sourceBase, readOnly: false);

    /// <summary>
    /// Reads an EXIF block under the read-only rules alone. They differ from <see cref="Read"/> in three
    /// places, each a structure the specifications allow and the editable profile cannot write back: an
    /// entry whose field type is not one of the twelve this reader knows is left out, as TIFF 6.0 tells
    /// a reader to do; strip, tile and SubIFD tags are ordinary entries that are never followed (an
    /// uncompressed Exif thumbnail is stored in strips); and values may share bytes with each other or
    /// with a directory. Every other rule stands, so out-of-range offsets, duplicate or unordered tags
    /// and bad pointers are refused exactly as before. No thumbnail is extracted, and the metadata
    /// reports <see cref="ExifMetadata.IsEditable"/> as false.
    /// </summary>
    internal static ExifMetadata ReadRelaxed(ReadOnlySpan<byte> tiff, MetadataLimits? limits = null,
        long sourceBase = 0) => ReadBlock(tiff, limits, sourceBase, readOnly: true).Metadata;

    /// <summary>
    /// Whether a refusal of the strict walk is worth a second walk under the read-only rules. Budgets,
    /// the APP1 size cap and duplicate tags are refused by both, so they are not.
    /// </summary>
    internal static bool IsRelaxable(MetadataException failure) =>
        failure.Code is MetadataErrorCode.UnsupportedFeature or MetadataErrorCode.InvalidExif;

    /// <summary>
    /// Reads an EXIF block for reading only: the strict walk, and where it refuses the block, the
    /// read-only rules of <see cref="ReadRelaxed"/>. A block both walks refuse fails with the second
    /// walk's error.
    /// </summary>
    internal static ExifMetadata Inspect(ReadOnlySpan<byte> tiff, MetadataLimits? limits = null,
        long sourceBase = 0)
    {
        try { return Read(tiff, limits, sourceBase).Metadata; }
        catch (MetadataException failure) when (IsRelaxable(failure)) { return ReadRelaxed(tiff, limits, sourceBase); }
    }

    private static TiffReadResult ReadBlock(ReadOnlySpan<byte> tiff, MetadataLimits? limits, long sourceBase,
        bool readOnly)
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
        if (!HasByteOrderMark(tiff))
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The TIFF byte order mark is invalid.", sourceBase);
        var reader = new ExifTiffReader(tiff, limits, sourceBase, wholeFile: false, readOnly);
        return reader.Parse();
    }

    /// <summary>
    /// Reads the EXIF directories of a whole TIFF-based file. The differences from <see cref="Read"/>:
    /// there is no APP1 size cap, and the retained budget counts the directories and values read rather
    /// than the file; the header may carry the ORF or RW2 identifier; SubIFD, strip and tile tags are
    /// ordinary entries that are never followed; and directories chained after the second one are image
    /// directories that are neither read nor refused. Every other rule is the same.
    /// </summary>
    internal static TiffReadResult ReadFile(ReadOnlySpan<byte> file, MetadataLimits? limits = null) =>
        ReadWhole(file, limits, readOnly: false);

    /// <summary>
    /// Reads the EXIF directories of a whole TIFF-based file for reading only: the strict walk of
    /// <see cref="ReadFile"/>, and where it refuses the file, the read-only rules described at
    /// <see cref="ReadRelaxed"/>. A file both walks refuse fails with the second walk's error.
    /// </summary>
    internal static ExifMetadata InspectFile(ReadOnlySpan<byte> file, MetadataLimits? limits = null)
    {
        try { return ReadFile(file, limits).Metadata; }
        catch (MetadataException failure) when (IsRelaxable(failure)) { return ReadWhole(file, limits, readOnly: true).Metadata; }
    }

    private static TiffReadResult ReadWhole(ReadOnlySpan<byte> file, MetadataLimits? limits, bool readOnly)
    {
        limits ??= new();
        limits.Validate();
        if (file.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The TIFF file exceeds the input budget.", 0);
        if (file.Length < 8)
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The TIFF header is truncated.", 0);
        if (!HasByteOrderMark(file))
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The TIFF byte order mark is invalid.", 0);
        var reader = new ExifTiffReader(file, limits, 0, wholeFile: true, readOnly);
        return reader.Parse();
    }

    /// <summary>
    /// Whether the bytes begin with the header of a TIFF-based file: a byte order mark followed by the
    /// TIFF, BigTIFF, ORF or RW2 identifier. BigTIFF is recognised here and refused by the read.
    /// </summary>
    internal static bool HasFileHeader(ReadOnlySpan<byte> file)
    {
        if (file.Length < 8 || !HasByteOrderMark(file))
            return false;
        ushort magic = file[0] == 0x49
            ? BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(2, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(file.Slice(2, 2));
        return magic is ClassicMagic or BigTiffMagic or OrfMagic or Rw2Magic;
    }

    private static bool HasByteOrderMark(ReadOnlySpan<byte> tiff) =>
        (tiff[0] == 0x49 && tiff[1] == 0x49) || (tiff[0] == 0x4D && tiff[1] == 0x4D);

    private TiffReadResult Parse()
    {
        ushort magic = U16(2);
        if (magic == BigTiffMagic)
            throw Error(MetadataErrorCode.UnsupportedFeature, "BigTIFF is not supported.", 2);
        bool vendorMagic = wholeFile && magic is OrfMagic or Rw2Magic;
        if (magic != ClassicMagic && !vendorMagic)
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
        // A read-only result is never planned for writing, so its picture is not taken out either.
        byte[]? thumbnail = readOnly ? null : ReadThumbnail();
        MakerNote? makerNote = ReadMakerNote();
        return new TiffReadResult(new ExifMetadata(order, entries, makerNote, editable: !readOnly),
            directories.AsReadOnly(), thumbnail);
    }

    private MakerNote? ReadMakerNote()
    {
        long budget = limits.MaxRetainedMetadataBytes - retainedBytes;
        MakerNote? note = null;
        int start = 0;
        if (makerNoteLength != 0)
        {
            start = makerNoteStart;
            note = MakerNotes.MakerNoteReader.TryRead(data, makerNoteStart, makerNoteLength, order, Make(), Model(), limits, budget);
        }
        else if (dngPrivateLength != 0 && data.Slice(dngPrivateStart, dngPrivateLength).StartsWith("PENTAX \0"u8))
        {
            // Pentax DNGs carry their MakerNote in DNGPrivateData instead of the Exif MakerNote tag.
            start = dngPrivateStart;
            note = MakerNotes.MakerNoteReader.TryRead(data, dngPrivateStart, dngPrivateLength, order, Make(), Model(), limits, budget);
        }
        if (note is not null) note.Offset = start;
        return note;
    }

    /// <summary>The raw bytes of the 0th IFD ASCII Make tag, or empty.</summary>
    private readonly ReadOnlySpan<byte> Make() => Ifd0Ascii(0x010F);

    /// <summary>The raw bytes of the 0th IFD ASCII Model tag, or empty.</summary>
    private readonly ReadOnlySpan<byte> Model() => Ifd0Ascii(0x0110);

    private readonly ReadOnlySpan<byte> Ifd0Ascii(ushort tag)
    {
        foreach (ExifEntry entry in entries)
            if (entry.Key == new ExifTagKey(ExifDirectoryKind.Ifd0, tag))
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
        int count = U16(start);
        if (count > limits.MaxEntriesPerDirectory || count > limits.MaxTotalEntries - totalEntries)
            throw Error(MetadataErrorCode.LimitExceeded, "The IFD entry budget was exceeded.", offset);
        totalEntries += count;
        int length = 2 + count * 12 + 4; // Count is a validated ushort, so this cannot overflow.
        RequireRange(offset, length);
        ClaimRange(start, length);
        if (wholeFile)
            ReserveBytes(length, offset);
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
            {
                // TIFF 6.0: a reader skips a field whose type it does not expect. Its width is unknown,
                // so the entry cannot be written back; reading leaves it out and keeps the rest.
                if (readOnly)
                    continue;
                throw Error(MetadataErrorCode.UnsupportedFeature, "The TIFF field type has no supported width.", p + 2);
            }
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
            // In a whole file these describe image data and further image directories: the entry is kept
            // like any other, and nothing it points at is read.
            if (!wholeFile && !readOnly && tag is 0x014A or 0x0111 or 0x0117 or 0x0144 or 0x0145)
                throw Error(MetadataErrorCode.UnsupportedFeature, "SubIFDs, strips and tiles are outside the EXIF subset.", p);
            if (tag == 0x927C && kind == ExifDirectoryKind.Exif)
            {
                if (type != TiffFieldType.Undefined)
                    throw Error(MetadataErrorCode.InvalidExif, "MakerNote must use the Undefined field type.", p);
                makerNoteStart = valueStart;
                makerNoteLength = byteLength;
            }
            if (tag == 0xC634 && kind == ExifDirectoryKind.Ifd0 && type is TiffFieldType.Byte or TiffFieldType.Undefined)
            {
                dngPrivateStart = valueStart;
                dngPrivateLength = byteLength;
            }
        }
        uint next = U32(start + 2 + count * 12);
        if (next != 0)
        {
            // A whole file may chain further image directories after the second one (CR2 and PEF do).
            // Like SubIFDs they hold images, not EXIF: the link is neither followed nor refused.
            if (wholeFile && kind == ExifDirectoryKind.Thumbnail)
                return;
            if (kind != ExifDirectoryKind.Ifd0)
                throw Error(MetadataErrorCode.UnsupportedFeature, "Only the primary IFD may link to a thumbnail IFD.", start + length - 4);
            Schedule(next, ExifDirectoryKind.Thumbnail, depth + 1);
        }
    }

    /// <summary>
    /// Reads the thumbnail if and only if it is a whole, addressable JPEG; every failure here drops the
    /// thumbnail rather than the read, because the picture is incidental to the metadata, which is already
    /// collected. Two refusals stay: thumbnail bytes that overlap other TIFF structures, and a retained
    /// budget the file as a whole has run out of. Both say something false about the payload, not merely
    /// something broken about the picture.
    /// </summary>
    private byte[]? ReadThumbnail()
    {
        if (!roles.Contains(ExifDirectoryKind.Thumbnail))
            return null;
        ExifEntry? compression = FindThumbnail(0x0103);
        ExifEntry? offset = FindThumbnail(0x0201);
        ExifEntry? length = FindThumbnail(0x0202);
        if (offset is null || length is null)
            return null; // no JPEG is addressed; the directory itself remains in the listing
        if (compression is null || compression.Value.FieldType != TiffFieldType.Short ||
            compression.Value.Count != 1 || ReadScalar(compression.Value) != 6)
            return null; // not a JPEG representation; nothing of this file's is handed out
        if (offset.Value.FieldType != TiffFieldType.Long || offset.Value.Count != 1 ||
            length.Value.FieldType != TiffFieldType.Long || length.Value.Count != 1)
            return null; // the pair does not describe one addressable byte range
        uint byteLength = ReadScalar(length.Value);
        uint byteOffset = ReadScalar(offset.Value);
        if (byteLength > limits.MaxThumbnailBytes)
            return null; // over the thumbnail budget: nothing is copied, so nothing is reserved
        if (!CheckedRange.TryGetSpanRange(byteOffset, byteLength, data.Length, out int start, out _))
            return null; // the picture lies outside the payload
        if ((byteOffset & 1) != 0 || byteLength < 4 || data[start] != 0xFF || data[start + 1] != 0xD8 ||
            data[start + (int)byteLength - 2] != 0xFF || data[start + (int)byteLength - 1] != 0xD9)
            return null; // unaligned, truncated or boundary-markless: not a whole JPEG
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
        // Shared bytes are a hazard for relocation only. Each range was already checked to lie inside
        // the payload, and every directory is visited once, so reading them is bounded without this.
        if (readOnly)
            return;
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
