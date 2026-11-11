using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Formats.Bmff;

/// <summary>
/// Read-only ISO BMFF (HEIC, HEIF, AVIF) metadata: the major brand of the file type box and the
/// EXIF and XMP items the top-level <c>meta</c> box declares. Image items are never decoded and
/// nothing is written.
/// </summary>
public sealed class BmffMetadata
{
    private BmffMetadata(string majorBrand, ExifMetadata? exif, XmpPacket? xmp)
    {
        MajorBrand = majorBrand;
        Exif = exif;
        Xmp = xmp;
    }

    /// <summary>The four-character major brand of the leading file type box.</summary>
    public string MajorBrand { get; }

    /// <summary>The EXIF item the file declares, or null when there is none.</summary>
    public ExifMetadata? Exif { get; }

    /// <summary>The XMP item of mime type <c>application/rdf+xml</c>, or null when there is none.</summary>
    public XmpPacket? Xmp { get; }

    /// <summary>
    /// Reads one complete BMFF buffer. Boxes other than the file type box and <c>meta</c> are
    /// skipped, and items other than EXIF and XMP are never interpreted.
    /// </summary>
    public static BmffMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null) =>
        BmffReader.Read(data, limits);
}

/// <summary>
/// One bounded pass over the box structure of a BMFF buffer. Every size is validated against the
/// box that encloses it before it is used, every count against <see cref="MetadataLimits"/>, and no
/// range is sliced without a check, so malformed input is a <see cref="MetadataException"/> and
/// never an index, overflow or allocation failure.
/// </summary>
internal ref struct BmffReader
{
    private const uint FileTypeBox = 0x66747970;       // 'ftyp'
    private const uint MetaBox = 0x6d657461;           // 'meta'
    private const uint HandlerBox = 0x68646c72;        // 'hdlr'
    private const uint PrimaryItemBox = 0x7069746d;    // 'pitm'
    private const uint ItemInformationBox = 0x69696e66; // 'iinf'
    private const uint ItemEntryBox = 0x696e6665;      // 'infe'
    private const uint ItemLocationBox = 0x696c6f63;   // 'iloc'
    private const uint ItemDataBox = 0x69646174;       // 'idat'
    private const uint PictureHandler = 0x70696374;    // 'pict'
    private const uint ExifItemType = 0x45786966;      // 'Exif'
    private const uint MimeItemType = 0x6d696d65;      // 'mime'
    private const string XmpContentType = "application/rdf+xml";

    private readonly ReadOnlySpan<byte> data;
    private readonly MetadataLimits limits;
    private readonly List<Item> items;
    private readonly List<Location> locations;
    private int boxes;
    private int structures;
    private int idatStart;
    private int idatLength;
    private long retained;
    private bool hasMeta;
    private bool hasHandler;
    private bool hasPrimary;
    private bool hasInformation;
    private bool hasLocations;
    private bool hasIdat;

    private BmffReader(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        this.data = data;
        this.limits = limits;
        items = [];
        locations = [];
        boxes = 0;
        structures = 0;
        idatStart = 0;
        idatLength = 0;
        retained = 0;
        hasMeta = false;
        hasHandler = false;
        hasPrimary = false;
        hasInformation = false;
        hasLocations = false;
        hasIdat = false;
    }

    internal static BmffMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits)
    {
        limits ??= new MetadataLimits();
        limits.Validate();
        if (data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The BMFF file exceeds the input budget.", 0);
        return new BmffReader(data, limits).Parse();
    }

    private BmffMetadata Parse()
    {
        int position = 0;
        BoxHeader fileType = Next(ref position, data.Length, 1);
        if (fileType.Type != FileTypeBox)
            throw Fail("The BMFF file does not start with a file type box.", 0);
        string brand = Brand(fileType);
        while (position < data.Length)
        {
            BoxHeader box = Next(ref position, data.Length, 1);
            if (box.Type == MetaBox) Meta(box);
        }
        // A file without a meta box carries no item metadata; it is not a malformed file. A .mov or
        // a bare AVIF sequence has a brand and nothing of ours, and that is what the caller gets.
        if (!hasMeta) return new BmffMetadata(brand, null, null);
        if (!hasHandler) throw Fail("The meta box declares no handler box.", 0);
        ExifMetadata? exif = ExifItem();
        XmpPacket? xmp = XmpItem();
        return new BmffMetadata(brand, exif, xmp);
    }

    private string Brand(BoxHeader box)
    {
        int length = box.End - box.Payload;
        if (length < 8 || length % 4 != 0)
            throw Fail("The file type box payload is truncated or unaligned.", box.Payload);
        return Encoding.Latin1.GetString(data.Slice(box.Payload, 4));
    }

    /// <summary>Reads one box header, validating its size against the enclosing range and its cost against the budgets.</summary>
    private BoxHeader Next(ref int position, int end, int depth)
    {
        if (depth > limits.MaxDirectoryDepth)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The box nesting budget was exceeded.", position);
        if (++boxes > limits.MaxSegments)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The box count budget was exceeded.", position);
        int start = position;
        int available = end - start;
        if (available < 8) throw Fail("A box header is truncated.", start);
        uint size32 = U32(start);
        uint type = U32(start + 4);
        int header = 8;
        long size = size32;
        if (size32 == 1)
        {
            if (available < 16) throw Fail("An extended box header is truncated.", start);
            ulong large = U64(start + 8);
            if (large > (ulong)available) throw Fail("A box size exceeds the box that encloses it.", start);
            size = (long)large;
            header = 16;
        }
        else if (size32 == 0)
        {
            size = available; // A zero size runs to the end of the enclosing box.
        }
        if (size < header) throw Fail("A box size is smaller than its own header.", start);
        if (size > available) throw Fail("A box size exceeds the box that encloses it.", start);
        position = start + (int)size;
        return new BoxHeader(type, start, start + header, position);
    }

    private void Meta(BoxHeader box)
    {
        if (hasMeta) throw Fail("The meta box appears more than once.", box.Start);
        hasMeta = true;
        if (box.End - box.Payload < 4) throw Fail("The meta box header is truncated.", box.Payload);
        int position = box.Payload + 4; // version and flags of the FullBox
        while (position < box.End)
        {
            BoxHeader child = Next(ref position, box.End, 2);
            if (child.Type == HandlerBox) Handler(child);
            else if (child.Type == PrimaryItemBox) PrimaryItem(child);
            else if (child.Type == ItemInformationBox) Information(child);
            else if (child.Type == ItemLocationBox) ItemLocations(child);
            else if (child.Type == ItemDataBox) ItemData(child);
        }
    }

    /// <summary>The handler type is the third field of the FullBox payload: version/flags, pre_defined, handler_type.</summary>
    private void Handler(BoxHeader box)
    {
        if (hasHandler) throw Fail("The handler box appears more than once.", box.Start);
        if (box.End - box.Payload < 12) throw Fail("The handler box is truncated.", box.Payload);
        if (U32(box.Payload + 8) != PictureHandler)
            throw Fail("The meta handler is not 'pict'.", box.Payload + 8);
        hasHandler = true;
    }

    private void PrimaryItem(BoxHeader box)
    {
        if (hasPrimary) throw Fail("The primary item box appears more than once.", box.Start);
        hasPrimary = true;
        int length = box.End - box.Payload;
        if (length < 4) throw Fail("The primary item box is truncated.", box.Payload);
        if (data[box.Payload] == 0)
        {
            if (length < 6) throw Fail("The primary item ID is truncated.", box.Payload + 4);
            _ = U16(box.Payload + 4);
        }
        else if (data[box.Payload] == 1)
        {
            if (length < 8) throw Fail("The primary item ID is truncated.", box.Payload + 4);
            _ = U32(box.Payload + 4);
        }
        else
        {
            throw Fail("The primary item version is not defined.", box.Payload);
        }
    }

    private void Information(BoxHeader box)
    {
        if (hasInformation) throw Fail("The item information box appears more than once.", box.Start);
        hasInformation = true;
        int length = box.End - box.Payload;
        if (length < 4) throw Fail("The item information box is truncated.", box.Payload);
        bool narrow = data[box.Payload] == 0;
        uint declared;
        int position;
        if (narrow)
        {
            if (length < 6) throw Fail("The item information entry count is truncated.", box.Payload + 4);
            declared = U16(box.Payload + 4);
            position = box.Payload + 6;
        }
        else
        {
            if (length < 8) throw Fail("The item information entry count is truncated.", box.Payload + 4);
            declared = U32(box.Payload + 4);
            position = box.Payload + 8;
        }
        int before = items.Count;
        while (position < box.End)
        {
            BoxHeader entry = Next(ref position, box.End, 3);
            if (entry.Type == ItemEntryBox) Item(entry);
        }
        if ((uint)(items.Count - before) != declared)
            throw Fail("The item information entry count does not match its entries.", box.Payload + 4);
    }

    /// <summary>An infe entry of version 2 or 3; the protection-only entries of versions 0 and 1 say nothing about a type.</summary>
    private void Item(BoxHeader box)
    {
        int length = box.End - box.Payload;
        if (length < 4) throw Fail("The item entry is truncated.", box.Payload);
        byte version = data[box.Payload];
        if (version is not (2 or 3)) return;
        int at = box.Payload + 4;
        int idSize = version == 2 ? 2 : 4;
        Require(at, idSize, box.End);
        uint id = version == 2 ? U16(at) : U32(at);
        at += idSize;
        Require(at, 6, box.End); // item_protection_index and item_type
        _ = U16(at);
        uint type = U32(at + 2);
        int nameEnd = Terminated(at + 6, box.End);
        string? contentType = null;
        if (type == MimeItemType)
        {
            int contentEnd = Terminated(nameEnd, box.End);
            contentType = Encoding.Latin1.GetString(data.Slice(nameEnd, contentEnd - nameEnd - 1));
        }
        foreach (Item item in items)
            if (item.Id == id) throw Fail("The item ID is declared more than once.", box.Start);
        items.Add(new Item(id, type, contentType));
    }

    private void ItemLocations(BoxHeader box)
    {
        if (hasLocations) throw Fail("The item location box appears more than once.", box.Start);
        hasLocations = true;
        int length = box.End - box.Payload;
        if (length < 8) throw Fail("The item location box is truncated.", box.Payload);
        byte version = data[box.Payload];
        if (version > 2) throw Fail("The item location version is not defined.", box.Payload);
        int at = box.Payload + 4;
        int offsetSize = data[at] >> 4;
        int extentLengthSize = data[at] & 0x0F;
        int baseOffsetSize = data[at + 1] >> 4;
        int indexSize = version == 0 ? 0 : data[at + 1] & 0x0F;
        if (version == 0 && indexSize != 0)
            throw Fail("The reserved item location nibble is not zero.", at + 1);
        if (!FieldSize(offsetSize) || !FieldSize(extentLengthSize) || !FieldSize(baseOffsetSize) || !FieldSize(indexSize))
            throw Fail("An item location field size is not 0, 4 or 8 bytes.", at);
        at += 2;
        int idSize = version < 2 ? 2 : 4;
        Require(at, idSize, box.End);
        uint count = version < 2 ? U16(at) : U32(at);
        at += idSize;
        for (uint index = 0; index < count; index++)
        {
            if (++structures > limits.MaxSegments)
                throw Limit("The item location budget was exceeded.", at);
            Require(at, idSize, box.End);
            uint id = version < 2 ? U16(at) : U32(at);
            at += idSize;
            byte method = 0;
            if (version >= 1)
            {
                Require(at, 2, box.End);
                ushort field = U16(at);
                if ((field & 0xFFF0) != 0) throw Fail("The reserved construction method bits are not zero.", at);
                method = (byte)(field & 0x0F);
                at += 2;
            }
            if (method > 1)
            {
                if (method == 2)
                    throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                        "Item construction method 2 is outside this profile.", at);
                throw Fail("The construction method is not defined.", at);
            }
            Require(at, 2, box.End);
            ushort reference = U16(at);
            at += 2;
            if (reference != 0)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "An item location refers to data outside the file.", at - 2);
            ulong baseOffset = Sized(ref at, baseOffsetSize, box.End);
            Require(at, 2, box.End);
            int extentCount = U16(at);
            at += 2;
            var extents = new List<Extent>(Math.Min(extentCount, 4));
            for (int extent = 0; extent < extentCount; extent++)
            {
                if (++structures > limits.MaxSegments)
                    throw Limit("The item extent budget was exceeded.", at);
                if (indexSize != 0) _ = Sized(ref at, indexSize, box.End); // a group label, not an address
                ulong offset = Sized(ref at, offsetSize, box.End);
                ulong extentLength = Sized(ref at, extentLengthSize, box.End);
                extents.Add(new Extent(offset, extentLength));
            }
            foreach (Location location in locations)
                if (location.Id == id) throw Fail("The item location is declared more than once.", box.Start);
            locations.Add(new Location(id, method, baseOffset, extents));
        }
    }

    private void ItemData(BoxHeader box)
    {
        if (hasIdat) throw Fail("The item data box appears more than once.", box.Start);
        hasIdat = true;
        idatStart = box.Payload;
        idatLength = box.End - box.Payload;
    }

    private ExifMetadata? ExifItem()
    {
        uint id = 0;
        bool found = false;
        foreach (Item item in items)
        {
            if (item.Type != ExifItemType) continue;
            if (found) throw Fail("The file declares more than one EXIF item.", 0);
            found = true;
            id = item.Id;
        }
        if (!found) return null;
        byte[] bytes = Bytes(id, out long source);
        if (bytes.Length < 4) throw Fail("The EXIF item is shorter than its TIFF offset.", source);
        uint skip = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        long start = 4L + skip;
        if (start >= bytes.Length) throw Fail("The EXIF TIFF header lies past its item.", source);
        return ExifTiffReader.Read(bytes.AsSpan((int)start), limits, source + start).Metadata;
    }

    private XmpPacket? XmpItem()
    {
        uint id = 0;
        bool found = false;
        foreach (Item item in items)
        {
            if (item.Type != MimeItemType) continue;
            if (!string.Equals(item.ContentType, XmpContentType, StringComparison.OrdinalIgnoreCase)) continue;
            if (found) throw Fail("The file declares more than one XMP item.", 0);
            found = true;
            id = item.Id;
        }
        return found ? XmpPacket.Parse(Bytes(id, out _)) : null;
    }

    /// <summary>The item data, its extents concatenated in order and every one of them bounds-checked.</summary>
    private byte[] Bytes(uint id, out long source)
    {
        int found = -1;
        for (int i = 0; i < locations.Count; i++)
        {
            if (locations[i].Id != id) continue;
            found = i;
            break;
        }
        if (found < 0) throw Fail("A metadata item has no item location.", 0);
        Location location = locations[found];
        long total = 0;
        foreach (Extent extent in location.Extents)
        {
            if (extent.Length > (ulong)limits.MaxRetainedMetadataBytes ||
                total > limits.MaxRetainedMetadataBytes - (long)extent.Length)
                throw Limit("The item data exceeds the retained metadata budget.", 0);
            total += (long)extent.Length;
        }
        if (total > limits.MaxRetainedMetadataBytes - retained)
            throw Limit("The item data exceeds the retained metadata budget.", 0);
        retained += total;
        var bytes = new byte[(int)total];
        int written = 0;
        source = 0;
        bool first = true;
        foreach (Extent extent in location.Extents)
        {
            int start = ExtentStart(location, extent, out int count);
            if (first)
            {
                source = start;
                first = false;
            }
            data.Slice(start, count).CopyTo(bytes.AsSpan(written));
            written += count;
        }
        return bytes;
    }

    /// <summary>The absolute position of one extent, and how many bytes it addresses.</summary>
    private int ExtentStart(Location location, Extent extent, out int count)
    {
        count = 0;
        if (location.BaseOffset > long.MaxValue || extent.Offset > long.MaxValue || extent.Length > int.MaxValue)
            throw Fail("An item extent is not representable in this file.", 0);
        long baseOffset = (long)location.BaseOffset;
        long offset = (long)extent.Offset;
        if (offset > long.MaxValue - baseOffset) throw Fail("An item extent offset overflows.", 0);
        long relative = baseOffset + offset;
        bool inIdat = location.Method == 1;
        if (inIdat && !hasIdat) throw Fail("An item extent names the idat box the file does not contain.", 0);
        long limit = inIdat ? idatLength : data.Length;
        if (relative > limit || (long)extent.Length > limit - relative)
            throw Fail(inIdat ? "An item extent lies outside the idat box." : "An item extent lies outside the file.",
                inIdat ? idatStart : relative);
        count = (int)extent.Length;
        return inIdat ? idatStart + (int)relative : (int)relative;
    }

    /// <summary>Consumes one unsigned field of 0, 4 or 8 bytes.</summary>
    private ulong Sized(ref int at, int size, int end)
    {
        if (size == 0) return 0;
        Require(at, size, end);
        ulong value = size == 4 ? U32(at) : U64(at);
        at += size;
        return value;
    }

    /// <summary>The index just past a NUL-terminated name that starts at <paramref name="at"/>.</summary>
    private int Terminated(int at, int end)
    {
        if (at > end) throw Fail("An item entry name lies outside its box.", at);
        int stop = data[at..end].IndexOf((byte)0);
        if (stop < 0) throw Fail("An item entry name is not NUL-terminated.", at);
        return at + stop + 1;
    }

    private void Require(int at, int count, int end)
    {
        if (count > end - at) throw Fail("A box field is truncated.", at);
    }

    private static bool FieldSize(int size) => size is 0 or 4 or 8;

    private ushort U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));

    private uint U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));

    private ulong U64(int at) => BinaryPrimitives.ReadUInt64BigEndian(data.Slice(at, 8));

    private MetadataException Fail(string message, long offset) =>
        new(MetadataErrorCode.InvalidBmff, message, offset >= 0 && offset <= data.Length ? offset : null);

    private MetadataException Limit(string message, long offset) =>
        new(MetadataErrorCode.LimitExceeded, message, offset >= 0 && offset <= data.Length ? offset : null);

    private readonly record struct BoxHeader(uint Type, int Start, int Payload, int End);

    private readonly record struct Item(uint Id, uint Type, string? ContentType);

    private readonly record struct Extent(ulong Offset, ulong Length);

    private readonly record struct Location(uint Id, byte Method, ulong BaseOffset, List<Extent> Extents);
}
