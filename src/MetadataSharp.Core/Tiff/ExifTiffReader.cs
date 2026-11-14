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
    private const ushort DcpMagic = 0x4352;

    private readonly ReadOnlySpan<byte> data;
    private readonly MetadataLimits limits;
    private readonly long sourceBase;
    private readonly bool wholeFile;
    private readonly bool readOnly;
    private readonly ExifDirectoryKind firstKind;
    private readonly Endianness order;
    private readonly List<ExifEntry> entries;
    private readonly List<ExifDirectoryKind> directories;
    // Image directories named by tag 0x014A are listed for reading only, never merged into the entries.
    private readonly List<ExifSubImage> subImages;
    private int subIfdImages;
    private int chainImages;
    private readonly Queue<(uint Offset, int Index, bool Chain)> pendingImages;
    private readonly List<(int Start, int Length)> ranges;
    // Breadth-first worklist avoids recursion and carries each directory role and depth into validation.
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
        bool readOnly, ExifDirectoryKind firstKind = ExifDirectoryKind.Ifd0)
    {
        this.data = data;
        this.limits = limits;
        this.sourceBase = sourceBase;
        this.wholeFile = wholeFile;
        this.readOnly = readOnly;
        this.firstKind = firstKind;
        order = data[0] == 0x49 ? Endianness.LittleEndian : Endianness.BigEndian;
        entries = [];
        directories = [];
        subImages = [];
        ranges = [(0, 8)];
        pending = new();
        pendingImages = new();
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

    /// <summary>
    /// The longest EXIF block a JPEG APP1 segment holds: 65,535 bytes less the two of the segment
    /// length and the six of the identifier. It is a rule of that container, and of the containers
    /// whose writers rebuild a block under the same profile; it is not a rule of TIFF.
    /// </summary>
    internal const int SegmentCeiling = 65_527;

    /// <summary>For a block in a container that sets no size of its own: only the budgets bound it.</summary>
    internal const int NoCeiling = int.MaxValue;

    internal static TiffReadResult Read(ReadOnlySpan<byte> tiff, MetadataLimits? limits = null,
        long sourceBase = 0) => ReadBlock(tiff, limits, sourceBase, readOnly: false, SegmentCeiling);

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
        long sourceBase = 0) => ReadBlock(tiff, limits, sourceBase, readOnly: true, SegmentCeiling).Metadata;

    /// <summary>
    /// Whether a refusal of the strict walk is worth a second walk under the read-only rules. Budgets,
    /// the APP1 size cap and duplicate tags are refused by both, so they are not.
    /// </summary>
    internal static bool IsRelaxable(MetadataException failure) =>
        failure.Code is MetadataErrorCode.UnsupportedFeature or MetadataErrorCode.InvalidExif;

    /// <summary>
    /// Reads an EXIF block for reading only: the strict walk, and where it refuses the block, the
    /// read-only rules of <see cref="ReadRelaxed"/>. A block both walks refuse fails with the second
    /// walk's error. A block longer than <paramref name="ceiling"/> is refused as too large for its
    /// container; <see cref="NoCeiling"/> leaves its length to the budgets.
    /// </summary>
    internal static ExifMetadata Inspect(ReadOnlySpan<byte> tiff, MetadataLimits? limits = null,
        long sourceBase = 0, int ceiling = SegmentCeiling)
    {
        try { return ReadBlock(tiff, limits, sourceBase, readOnly: false, ceiling).Metadata; }
        catch (MetadataException failure) when (IsRelaxable(failure))
        {
            return ReadBlock(tiff, limits, sourceBase, readOnly: true, ceiling).Metadata;
        }
    }

    /// <summary>
    /// Reads, for reading only, a block whose header leads to one directory of a known kind instead of
    /// to the primary one: a Canon CR3 file stores the primary, the Exif and the GPS directory each as
    /// a TIFF of its own. The directory is read as <paramref name="first"/> and every rule of
    /// <see cref="Inspect"/> applies from there, so an Exif directory may point at an interoperability
    /// directory and may not point at another Exif or GPS directory, and only a primary directory may
    /// be followed by a second one. The length of the block is left to the budgets.
    /// </summary>
    internal static ExifMetadata InspectDirectory(ReadOnlySpan<byte> tiff, ExifDirectoryKind first,
        MetadataLimits? limits = null, long sourceBase = 0)
    {
        if (first is not (ExifDirectoryKind.Ifd0 or ExifDirectoryKind.Exif or ExifDirectoryKind.Gps))
            throw new ArgumentOutOfRangeException(nameof(first));
        try { return ReadBlock(tiff, limits, sourceBase, readOnly: false, NoCeiling, first).Metadata; }
        catch (MetadataException failure) when (IsRelaxable(failure))
        {
            return ReadBlock(tiff, limits, sourceBase, readOnly: true, NoCeiling, first).Metadata;
        }
    }

    // Validate the external block and its header before constructing the bounded directory walker.
    private static TiffReadResult ReadBlock(ReadOnlySpan<byte> tiff, MetadataLimits? limits, long sourceBase,
        bool readOnly, int ceiling, ExifDirectoryKind first = ExifDirectoryKind.Ifd0)
    {
        limits ??= new();
        limits.Validate();
        if (sourceBase < 0 || sourceBase > long.MaxValue - tiff.Length)
            throw new ArgumentOutOfRangeException(nameof(sourceBase));
        if (tiff.Length > ceiling)
            throw new MetadataException(MetadataErrorCode.ExifTooLarge, "The EXIF TIFF payload exceeds the APP1 limit.", sourceBase);
        if (tiff.Length > limits.MaxInputBytes || tiff.Length > limits.MaxRetainedMetadataBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EXIF payload exceeds the retained metadata budget.", sourceBase);
        if (tiff.Length < 8)
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The TIFF header is truncated.", sourceBase);
        if (!HasByteOrderMark(tiff))
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The TIFF byte order mark is invalid.", sourceBase);
        var reader = new ExifTiffReader(tiff, limits, sourceBase, wholeFile: false, readOnly, first);
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

    // Whole-file reads retain selected metadata only and intentionally ignore image-data directory chains.
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
        return magic is ClassicMagic or BigTiffMagic or OrfMagic or Rw2Magic or DcpMagic;
    }

    private static bool HasByteOrderMark(ReadOnlySpan<byte> tiff) =>
        (tiff[0] == 0x49 && tiff[1] == 0x49) || (tiff[0] == 0x4D && tiff[1] == 0x4D);

    // Validate the TIFF header, drain the nonrecursive directory queue, then collect only a safe thumbnail.
    private TiffReadResult Parse()
    {
        ushort magic = U16(2);
        if (magic == BigTiffMagic)
            throw Error(MetadataErrorCode.UnsupportedFeature, "BigTIFF is not supported.", 2);
        bool vendorMagic = wholeFile && magic is OrfMagic or Rw2Magic or DcpMagic;
        if (magic != ClassicMagic && !vendorMagic)
            throw Error(MetadataErrorCode.InvalidExif, "The TIFF identifier is invalid.", 2);
        uint first = U32(4);
        if (first == 0)
            throw Error(MetadataErrorCode.InvalidExif, "The primary IFD is missing.", 4);
        Schedule(first, firstKind, 1);
        while (pending.Count != 0)
        {
            var item = pending.Dequeue();
            ReadDirectory(item.Offset, item.Kind, item.Depth);
        }
        while (pendingImages.Count != 0)
        {
            var item = pendingImages.Dequeue();
            ReadSubImage(item.Offset, item.Index, item.Chain);
        }
        // A read-only result is never planned for writing, so its picture is not taken out either.
        byte[]? thumbnail = readOnly ? null : ReadThumbnail();
        MakerNote? makerNote = ReadMakerNote();
        return new TiffReadResult(
            new ExifMetadata(order, entries, makerNote, editable: !readOnly, subImages.AsReadOnly()),
            directories.AsReadOnly(), thumbnail);
    }

    // Decode maker-note fields only after the containing value and its protected byte range are established.
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

    // Read a top-level ASCII tag only from its validated, retained entry bytes.
    private readonly ReadOnlySpan<byte> Ifd0Ascii(ushort tag)
    {
        foreach (ExifEntry entry in entries)
            if (entry.Key == new ExifTagKey(ExifDirectoryKind.Ifd0, tag))
                return entry.Value.FieldType == TiffFieldType.Ascii ? entry.Value.RawBytes : default;
        return default;
    }

    // Reject repeated, role-conflicting, or over-budget directory pointers before enqueueing them.
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

    /// <summary>
    /// Queues the image directories a whole file names with tag 0x014A. The tag holds file offsets, one
    /// per picture, in the type its entry declares; an entry of another shape is left as an entry only.
    /// A picture directory the payload does not hold, or one that was already read, is not followed: the
    /// pictures are incidental to the metadata, exactly as the thumbnail is, and the entry stays.
    /// </summary>
    private void ScheduleSubImages(ExifValue value, int depth)
    {
        if (value.FieldType is not (TiffFieldType.Long or TiffFieldType.Short))
            return;
        int width = ExifValue.GetWidth(value.FieldType);
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        for (int i = 0; i < value.Count; i++)
        {
            uint at = width == 4
                ? (order == Endianness.LittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4, 4))
                    : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i * 4, 4)))
                : (order == Endianness.LittleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2))
                    : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i * 2, 2)));
            if (at == 0)
                continue; // A slot the file leaves empty is not a directory.
            if (!CheckedRange.TryGetSpanRange(at, 2, data.Length, out _, out _))
                continue; // The picture directory is not in the payload: there is nothing to list.
            if (!offsets.Add(at))
                continue; // The offset names a directory this read already visited.
            if (offsets.Count > limits.MaxDirectories || depth > limits.MaxDirectoryDepth)
                throw Error(MetadataErrorCode.LimitExceeded, "The directory traversal budget was exceeded.", at);
            pendingImages.Enqueue((at, ++subIfdImages, false));
        }
    }

    /// <summary>
    /// Reads one image directory for listing. It holds pictures, not metadata, so its entries are
    /// collected apart from <see cref="entries"/>, nothing it points at is followed, and an entry of a
    /// field type this reader does not know is left out rather than refused: the rest of the file's
    /// metadata does not depend on it.
    /// </summary>
    private void ReadSubImage(uint offset, int index, bool chain)
    {
        int start = RequireRange(offset, 2);
        int count = U16(start);
        if (count > limits.MaxEntriesPerDirectory || count > limits.MaxTotalEntries - totalEntries)
            throw Error(MetadataErrorCode.LimitExceeded, "The IFD entry budget was exceeded.", offset);
        totalEntries += count;
        int length = 2 + count * 12 + 4;
        RequireRange(offset, length);
        ClaimRange(start, length);
        if (wholeFile)
            ReserveBytes(length, offset);
        var ids = new HashSet<ushort>();
        ushort previous = 0;
        var imageEntries = new List<ExifEntry>(count);
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
                continue;
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
            imageEntries.Add(new ExifEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, tag),
                ExifValue.FromRaw(type, valueCount, order, data.Slice(valueStart, byteLength))));
        }
        subImages.Add(new ExifSubImage(index, imageEntries,
            chain ? ExifSubImageKind.ImageDirectory : ExifSubImageKind.SubIfd));
        // A chain continues: the directory after this one is the file's next picture, not metadata.
        uint following = U32(start + 2 + count * 12);
        if (chain && following != 0 && offsets.Add(following))
            pendingImages.Enqueue((following, ++chainImages + 1, true));
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
                // TIFF 6.0: a reader skips a field whose type it does not expect, and its width is unknown,
                // so the entry cannot be written back. For reading the value is one byte per element, as
                // the reference reads it, and the entry is kept with the type code the file states. A
                // stated count the file or the budget does not hold has no bytes to read, so the entry is
                // left out: the reference prints such an entry with an empty value, and a value that is
                // not in the payload is not made up.
                if (!readOnly)
                    throw Error(MetadataErrorCode.UnsupportedFeature, "The TIFF field type has no supported width.", p + 2);
                uint unknownCount = U32(p + 4);
                int unknownStart = p + 8;
                if (unknownCount > (uint)limits.MaxValueBytes)
                    continue;
                if (unknownCount > 4 &&
                    !CheckedRange.TryGetSpanRange(U32(p + 8), unknownCount, data.Length, out unknownStart, out _))
                    continue;
                ReserveBytes((int)unknownCount, p);
                entries.Add(new ExifEntry(new ExifTagKey(kind, tag),
                    ExifValue.FromRawUnknown(typeCode, unknownCount, order, data.Slice(unknownStart, (int)unknownCount))));
                continue;
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
            // like any other, and only a SubIFD list is followed, as a list of image directories.
            if (!wholeFile && !readOnly && tag is 0x014A or 0x0111 or 0x0117 or 0x0144 or 0x0145)
                throw Error(MetadataErrorCode.UnsupportedFeature, "SubIFDs, strips and tiles are outside the EXIF subset.", p);
            if (wholeFile && tag == 0x014A && kind == ExifDirectoryKind.Ifd0)
                ScheduleSubImages(entry.Value, depth);
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
            // A whole file may chain further image directories after the second one (CR2 puts its raw
            // picture in the second directory and a further reduced one in the third, PEF its preview).
            // They hold pictures, not EXIF: like SubIFDs they are listed and nothing in them is followed.
            if (wholeFile && kind == ExifDirectoryKind.Thumbnail)
            {
                if (offsets.Add(next))
                    pendingImages.Enqueue((next, ++chainImages + 1, true));
                return;
            }
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

    // Thumbnail tags belong to the thumbnail IFD only; similarly numbered primary tags are unrelated.
    private ExifEntry? FindThumbnail(ushort tag)
    {
        foreach (ExifEntry entry in entries)
            if (entry.Key == new ExifTagKey(ExifDirectoryKind.Thumbnail, tag))
                return entry;
        return null;
    }

    // Decode a validated one-element TIFF scalar using the file byte order.
    private uint ReadScalar(ExifValue value)
    {
        Span<byte> bytes = stackalloc byte[4];
        value.CopyRawTo(bytes);
        return value.FieldType == TiffFieldType.Short
            ? (order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes))
            : (order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes));
    }

    // Convert an untrusted TIFF offset to a span index only after checked range validation.
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

    // Charge retained metadata incrementally so whole-file reads do not reserve the entire input.
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

    // Convert in-payload offsets back to absolute source positions; synthetic/out-of-range offsets stay unset.
    private MetadataException Error(MetadataErrorCode code, string message, long offset) =>
        new(code, message, offset >= 0 && offset <= data.Length ? sourceBase + offset : null);
}
