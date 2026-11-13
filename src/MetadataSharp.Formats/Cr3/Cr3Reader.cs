using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Cr3;

/// <summary>The payload of one metadata box of a CR3 file: where it starts in the file and how long it is.</summary>
internal readonly record struct Cr3Block(int Start, int Length)
{
    /// <summary>False for a box the file does not have. A payload never starts at zero, where the file type box is.</summary>
    internal bool Present => Start != 0;
}

/// <summary>
/// Where the metadata of a CR3 file is. The four CMT boxes of the Canon box inside the movie box
/// each hold one directory as a TIFF of its own: CMT1 the primary directory, CMT2 the Exif
/// directory, CMT3 the Canon MakerNote and CMT4 the GPS directory. The XMP packet is the payload of
/// a box of its own at the top level.
/// </summary>
internal readonly record struct Cr3Layout(Cr3Block Ifd0, Cr3Block Exif, Cr3Block MakerNote, Cr3Block Gps, Cr3Block Xmp);

/// <summary>
/// One bounded pass over the boxes of a Canon CR3 file. Three levels are opened and nothing else:
/// the top level, the movie box and the Canon box inside it. Every other box, the tracks and the
/// media data among them, is stepped over by its size. The directories are then read by the EXIF
/// directory reader and the Canon MakerNote reader that read them in every other container.
/// </summary>
internal ref struct Cr3Reader
{
    private const uint FileTypeBox = 0x66747970; // 'ftyp'
    private const uint MovieBox = 0x6D6F6F76;    // 'moov'
    private const uint UuidBox = 0x75756964;     // 'uuid'
    private const uint Brand = 0x63727820;       // 'crx '
    private const uint Cmt1 = 0x434D5431, Cmt2 = 0x434D5432, Cmt3 = 0x434D5433, Cmt4 = 0x434D5434;

    /// <summary>85c0b687-820f-11e0-8111-f4ce462b6a48: the Canon box of the movie box.</summary>
    internal static ReadOnlySpan<byte> CanonBox => new byte[] { 0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48 };

    /// <summary>be7acfcb-97a9-42e8-9c71-999491e3afac: the XMP packet.</summary>
    internal static ReadOnlySpan<byte> XmpBox => new byte[] { 0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC };

    private readonly ReadOnlySpan<byte> data;
    private readonly MetadataLimits limits;
    private BmffBoxWalk walk;
    private long retained;
    private Cr3Block ifd0;
    private Cr3Block exif;
    private Cr3Block makerNote;
    private Cr3Block gps;
    private Cr3Block xmp;
    private bool hasMovie;
    private bool hasCanon;

    // Capture validated inputs and stable state for this BMFF object.
    private Cr3Reader(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        this.data = data;
        this.limits = limits;
        walk = new BmffBoxWalk(data, limits);
        retained = 0;
        ifd0 = default;
        exif = default;
        makerNote = default;
        gps = default;
        xmp = default;
        hasMovie = false;
        hasCanon = false;
    }

    /// <summary>
    /// Finds the metadata boxes of a complete CR3 file. A file whose major brand is not <c>crx </c>
    /// is refused; a CR3 file without a movie box, a Canon box or an XMP box is not malformed, it
    /// has nothing to find.
    /// </summary>
    internal static Cr3Layout Locate(ReadOnlySpan<byte> data, MetadataLimits? limits = null) =>
        Open(data, limits).Walk();

    /// <summary>
    /// Reads the EXIF directories and the XMP packet of a complete CR3 file. With
    /// <paramref name="refusals"/> what the reader of one of the two refuses is noted there and the
    /// other is still read; a file whose box structure does not hold is refused whole either way.
    /// </summary>
    internal static Cr3Metadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits, XmpReadOptions? xmpOptions = null,
        KindRefusals? refusals = null)
    {
        Cr3Reader reader = Open(data, limits);
        Cr3Layout layout = reader.Walk();
        return reader.Metadata(layout, xmpOptions, refusals);
    }

    // Open the source with the selected options, keeping stream ownership and cancellation explicit.
    private static Cr3Reader Open(ReadOnlySpan<byte> data, MetadataLimits? limits)
    {
        limits ??= BmffReadOptions.DefaultLimits;
        limits.Validate();
        if (data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The CR3 file exceeds the input budget.", 0);
        return new Cr3Reader(data, limits);
    }

    // Traverse BMFF while enforcing nesting depth and visited-range limits.
    private Cr3Layout Walk()
    {
        int position = 0;
        BoxSpan fileType = walk.Next(ref position, data.Length, 1);
        if (fileType.Type != FileTypeBox)
            throw walk.Fail("The file does not start with a file type box.", 0);
        if (fileType.PayloadLength < 8 || fileType.PayloadLength % 4 != 0)
            throw walk.Fail("The file type box payload is truncated or unaligned.", fileType.Payload);
        if (walk.U32(fileType.Payload) != Brand)
            throw walk.Fail("The major brand is not 'crx ': the file is not a Canon CR3.", fileType.Payload);
        while (position < data.Length)
        {
            BoxSpan box = walk.Next(ref position, data.Length, 1);
            if (box.Type == MovieBox)
            {
                Movie(box);
            }
            else if (box.Type == UuidBox && Identified(box, XmpBox))
            {
                if (xmp.Present) throw walk.Fail("The XMP box appears more than once.", box.Start);
                xmp = new Cr3Block(box.Payload + 16, box.PayloadLength - 16);
            }
        }
        return new Cr3Layout(ifd0, exif, makerNote, gps, xmp);
    }

    // Keep Movie focused on BMFF and its format-specific validation contract.
    private void Movie(BoxSpan movie)
    {
        if (hasMovie) throw walk.Fail("The movie box appears more than once.", movie.Start);
        hasMovie = true;
        int position = movie.Payload;
        while (position < movie.End)
        {
            BoxSpan box = walk.Next(ref position, movie.End, 2);
            if (box.Type != UuidBox || !Identified(box, CanonBox)) continue;
            if (hasCanon) throw walk.Fail("The Canon box appears more than once.", box.Start);
            hasCanon = true;
            Canon(box);
        }
    }

    // Keep Canon focused on BMFF and its format-specific validation contract.
    private void Canon(BoxSpan canon)
    {
        int position = canon.Payload + 16;
        while (position < canon.End)
        {
            BoxSpan box = walk.Next(ref position, canon.End, 3);
            if (box.Type == Cmt1) Claim(ref ifd0, box);
            else if (box.Type == Cmt2) Claim(ref exif, box);
            else if (box.Type == Cmt3) Claim(ref makerNote, box);
            else if (box.Type == Cmt4) Claim(ref gps, box);
        }
    }

    // Reject invalid BMFF ranges, overlaps, or field shapes before accepting the data.
    private readonly void Claim(ref Cr3Block block, BoxSpan box)
    {
        if (block.Present) throw walk.Fail("A CMT box appears more than once.", box.Start);
        block = new Cr3Block(box.Payload, box.PayloadLength);
    }

    /// <summary>Whether a uuid box carries the given identifier. A box too short to carry any is malformed.</summary>
    private readonly bool Identified(BoxSpan box, ReadOnlySpan<byte> identifier)
    {
        if (box.PayloadLength < 16) throw walk.Fail("A uuid box is shorter than its identifier.", box.Start);
        return data.Slice(box.Payload, 16).SequenceEqual(identifier);
    }

    /// <summary>
    /// The directories of the CMT boxes as one EXIF block, and the packet of the XMP box. A directory
    /// is read once: a CMT1 box that leads to an Exif or GPS directory of its own beside a CMT2 or
    /// CMT4 box says the same thing twice, and is refused rather than merged.
    /// </summary>
    private Cr3Metadata Metadata(Cr3Layout layout, XmpReadOptions? xmpOptions, KindRefusals? refusals)
    {
        var entries = new List<ExifEntry>();
        var kinds = new HashSet<ExifDirectoryKind>();
        Endianness? order = null;
        int noteDirectory = 0;
        Endianness noteOrder = Endianness.LittleEndian;
        try
        {
            Directory(layout.Ifd0, ExifDirectoryKind.Ifd0, entries, kinds, ref order);
            Directory(layout.Exif, ExifDirectoryKind.Exif, entries, kinds, ref order);
            Directory(layout.Gps, ExifDirectoryKind.Gps, entries, kinds, ref order);
            // The bytes of the MakerNote box and of the packet are counted before the note is decoded.
            // Decoding is the one step that gives up instead of failing, so it takes what the rest left.
            noteDirectory = NoteHeader(layout.MakerNote, out noteOrder);
        }
        catch (MetadataException refusal) when (refusals is not null) { refusals.Exif = refusal; }
        bool packetFits = layout.Xmp.Present;
        try
        {
            if (layout.Xmp.Present)
            {
                if (layout.Xmp.Length > limits.MaxRetainedMetadataBytes - retained)
                    throw walk.Limit("The XMP box exceeds the retained metadata budget.", layout.Xmp.Start);
                retained += layout.Xmp.Length;
            }
        }
        catch (MetadataException refusal) when (refusals is not null) { refusals.Xmp = refusal; packetFits = false; }
        ExifMetadata? metadata = null;
        // Directories one of which was refused are not half of an EXIF block: the block is absent.
        if (refusals?.Exif is null)
        {
            MakerNote? note = noteDirectory == 0 ? null : Note(layout.MakerNote, noteDirectory, noteOrder, entries);
            bool any = layout.Ifd0.Present || layout.Exif.Present || layout.MakerNote.Present || layout.Gps.Present;
            if (any) metadata = new ExifMetadata(order ?? note?.ByteOrder ?? Endianness.LittleEndian, entries, note, editable: false);
        }
        XmpPacket? packet = null;
        try { packet = packetFits ? XmpPacket.Parse(data.Slice(layout.Xmp.Start, layout.Xmp.Length), xmpOptions) : null; }
        catch (MetadataException refusal) when (refusals is not null) { refusals.Xmp = refusal; }
        return new Cr3Metadata(metadata, packet);
    }

    private void Directory(Cr3Block block, ExifDirectoryKind kind, List<ExifEntry> entries,
        HashSet<ExifDirectoryKind> kinds, ref Endianness? order)
    {
        if (!block.Present) return;
        MetadataLimits left = Remaining(block, entries.Count);
        ExifMetadata part = ExifTiffReader.InspectDirectory(data.Slice(block.Start, block.Length), kind, left, block.Start);
        if (order is Endianness known && known != part.ByteOrder)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "The CMT boxes do not share one byte order.", block.Start);
        order = part.ByteOrder;
        // What the directory reader counted against the budget it was given: the block and every value.
        retained += block.Length;
        var here = new HashSet<ExifDirectoryKind>();
        foreach (ExifEntry entry in part.Entries)
        {
            retained += entry.Value.ByteLength;
            here.Add(entry.Key.Directory);
        }
        foreach (ExifDirectoryKind found in here)
        {
            if (!kinds.Add(found))
                throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Two CMT boxes declare the same directory.", block.Start);
        }
        entries.AddRange(part.Entries);
    }

    /// <summary>
    /// The header of the CMT3 box: a TIFF header that leads to the Canon MakerNote directory. It is
    /// held to the rules of the header of any other CMT box. Returns where the directory starts in
    /// the box, or zero when the file has no CMT3 box.
    /// </summary>
    private int NoteHeader(Cr3Block block, out Endianness order)
    {
        order = Endianness.LittleEndian;
        if (!block.Present) return 0;
        if (block.Length < 8)
            throw new MetadataException(MetadataErrorCode.InvalidExif, "A CMT box is too short for a TIFF header.", block.Start);
        if (block.Length > limits.MaxRetainedMetadataBytes - retained)
            throw walk.Limit("The CMT boxes exceed the retained metadata budget.", block.Start);
        ReadOnlySpan<byte> tiff = data.Slice(block.Start, block.Length);
        bool little = tiff[0] == 0x49 && tiff[1] == 0x49;
        if (!little && !(tiff[0] == 0x4D && tiff[1] == 0x4D))
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The TIFF byte order mark is invalid.", block.Start);
        ushort magic = little ? BinaryPrimitives.ReadUInt16LittleEndian(tiff.Slice(2, 2)) : BinaryPrimitives.ReadUInt16BigEndian(tiff.Slice(2, 2));
        if (magic != 42)
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The TIFF identifier is invalid.", block.Start + 2);
        uint first = little ? BinaryPrimitives.ReadUInt32LittleEndian(tiff.Slice(4, 4)) : BinaryPrimitives.ReadUInt32BigEndian(tiff.Slice(4, 4));
        if (first < 8 || first > (uint)(block.Length - 2))
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The MakerNote directory lies outside the CMT3 box.", block.Start + 4);
        retained += block.Length;
        order = little ? Endianness.LittleEndian : Endianness.BigEndian;
        return (int)first;
    }

    /// <summary>
    /// The Canon MakerNote directory of the CMT3 box. Its value offsets count from the TIFF header of
    /// the box, as they count from the TIFF header in every Canon file. A directory the MakerNote
    /// reader cannot follow, or one the budget has no room left for, leaves the note undecoded, as
    /// it does in every other container.
    /// </summary>
    private MakerNote? Note(Cr3Block block, int directory, Endianness order, List<ExifEntry> entries)
    {
        ReadOnlySpan<byte> model = default;
        foreach (ExifEntry entry in entries)
        {
            if (entry.Key == new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0110) && entry.Value.FieldType == TiffFieldType.Ascii)
                model = entry.Value.RawBytes;
        }
        // The box is Canon's by definition, whatever the Make tag of the primary directory says.
        MakerNote? note = MakerNoteReader.TryRead(data.Slice(block.Start, block.Length), directory, block.Length - directory,
            order, "Canon"u8, model, limits, limits.MaxRetainedMetadataBytes - retained);
        if (note is null) return null;
        note.Offset = directory;
        foreach (MakerNoteEntry entry in note.Entries)
            retained += 2L * entry.Value.ByteLength;
        return note;
    }

    /// <summary>
    /// The budgets one CMT box is read under: those of the file, less the retained bytes and the
    /// entries the boxes before it took. A box too short for a TIFF header is refused here, so the
    /// byte budget handed on is never empty.
    /// </summary>
    private readonly MetadataLimits Remaining(Cr3Block block, int entries)
    {
        if (block.Length < 8)
            throw new MetadataException(MetadataErrorCode.InvalidExif, "A CMT box is too short for a TIFF header.", block.Start);
        long left = limits.MaxRetainedMetadataBytes - retained;
        if (block.Length > left)
            throw walk.Limit("The CMT boxes exceed the retained metadata budget.", block.Start);
        if (entries >= limits.MaxTotalEntries)
            throw walk.Limit("The CMT boxes exceed the entry budget.", block.Start);
        return limits with { MaxRetainedMetadataBytes = (int)left, MaxTotalEntries = limits.MaxTotalEntries - entries };
    }
}
