using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Bmff;

/// <summary>A box as it lies in the file. <see cref="Length"/> includes the header.</summary>
internal sealed record BmffBox(uint Type, int Start, int HeaderLength, int Length, bool OpenEnded)
{
    internal int Payload => Start + HeaderLength;
    internal int End => Start + Length;
}

/// <summary>One extent of an item location, as the item location box states it.</summary>
internal readonly record struct BmffExtent(ulong Index, ulong Offset, ulong Length);

/// <summary>
/// One entry of the item location box. Method 0 counts offsets from the start of the file, method 1
/// from the start of the payload of the item data box.
/// </summary>
internal sealed record BmffLocation(uint Id, int Method, ulong BaseOffset, IReadOnlyList<BmffExtent> Extents);

/// <summary>
/// One entry of the item information box. Entries of versions 0 and 1 have no item type, and
/// <see cref="Type"/> is zero for them.
/// </summary>
internal sealed record BmffItem(uint Id, int Version, uint Type, string? ContentType);

/// <summary>Bytes of the file that an extent addresses.</summary>
internal readonly record struct BmffSegment(int Start, int Length)
{
    internal int End => Start + Length;
}

/// <summary>
/// The box structure of a BMFF file, as far as the writer has to know it: the top-level boxes, the
/// children of the <c>meta</c> box, every item the item information box declares and every extent
/// the item location box addresses. Nothing is decoded and no item is copied.
/// </summary>
/// <remarks>
/// The walk is separate from <see cref="BmffReader"/> on purpose: an edited file is read back by
/// both, and they have to agree. A structure the walk cannot account for is a refusal, not a guess:
/// a box that leaves its parent, a second <c>meta</c>, item location or item information box, an
/// item whose data is in another file or is built from other items, a budget of
/// <see cref="MetadataLimits"/>. An extent that leaves the file or the item data box is recorded as
/// it is: such an item cannot be resolved, and that matters only to an edit that appends.
/// </remarks>
internal sealed class BmffFileMap
{
    internal const uint FileType = 0x66747970;        // 'ftyp'
    internal const uint Meta = 0x6d657461;            // 'meta'
    internal const uint MediaData = 0x6d646174;       // 'mdat'
    internal const uint Free = 0x66726565;            // 'free'
    internal const uint Handler = 0x68646c72;         // 'hdlr'
    internal const uint PrimaryItemBox = 0x7069746d;  // 'pitm'
    internal const uint ItemInformation = 0x69696e66; // 'iinf'
    internal const uint ItemEntry = 0x696e6665;       // 'infe'
    internal const uint ItemLocation = 0x696c6f63;    // 'iloc'
    internal const uint ItemReference = 0x69726566;   // 'iref'
    internal const uint ItemData = 0x69646174;        // 'idat'
    internal const uint ExifItem = 0x45786966;        // 'Exif'
    internal const uint MimeItem = 0x6d696d65;        // 'mime'
    internal const string XmpContentType = "application/rdf+xml";

    private readonly byte[] file;
    private readonly MetadataLimits limits;
    private readonly List<BmffBox> topLevel = [];
    private readonly List<BmffBox> children = [];
    private readonly List<BmffItem> items = [];
    private readonly List<BmffLocation> locations = [];
    private int boxes;
    private int structures;

    private BmffFileMap(byte[] file, MetadataLimits limits)
    {
        this.file = file;
        this.limits = limits;
    }

    internal int SourceLength => file.Length;
    internal IReadOnlyList<BmffBox> TopLevel => topLevel;
    /// <summary>The top-level <c>meta</c> box, or null.</summary>
    internal BmffBox? MetaBox { get; private set; }
    /// <summary>The children of the <c>meta</c> box, in file order.</summary>
    internal IReadOnlyList<BmffBox> Children => children;
    internal BmffBox? Locations { get; private set; }
    internal BmffBox? Information { get; private set; }
    /// <summary>The first item reference box of the <c>meta</c> box.</summary>
    internal BmffBox? References { get; private set; }
    internal int ReferenceBoxes { get; private set; }
    internal BmffBox? Data { get; private set; }
    internal uint? PrimaryItem { get; private set; }

    internal int LocationVersion { get; private set; }
    internal int OffsetSize { get; private set; }
    internal int LengthSize { get; private set; }
    internal int BaseOffsetSize { get; private set; }
    internal int IndexSize { get; private set; }
    /// <summary>The entries of the item location box, in file order.</summary>
    internal IReadOnlyList<BmffLocation> Entries => locations;

    internal int InformationVersion { get; private set; }
    /// <summary>The entries of the item information box whose item ID is known, in file order.</summary>
    internal IReadOnlyList<BmffItem> Items => items;
    /// <summary>How many <c>infe</c> boxes the item information box holds.</summary>
    internal int DeclaredItems { get; private set; }
    /// <summary>An item entry of a version this walk does not know: its item ID is not known either.</summary>
    internal bool HasUnknownItemEntry { get; private set; }

    internal static BmffFileMap Read(byte[] file, MetadataLimits limits)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (file.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The BMFF file exceeds the input budget.", 0);
        var map = new BmffFileMap(file, limits);
        map.Walk();
        return map;
    }

    /// <summary>The one item of a type; for <c>mime</c>, of that content type. Null when there is none.</summary>
    internal BmffItem? Find(uint type, string? contentType)
    {
        BmffItem? found = null;
        foreach (BmffItem item in items)
        {
            if (item.Type != type) continue;
            if (contentType is not null && !string.Equals(item.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
                continue;
            if (found is not null) throw Fail("The file declares more than one item of the kind to edit.", 0);
            found = item;
        }
        return found;
    }

    internal BmffLocation? Location(uint id)
    {
        foreach (BmffLocation location in locations)
            if (location.Id == id) return location;
        return null;
    }

    /// <summary>
    /// The bytes an item addresses, extent by extent. With <paramref name="wide"/> an extent of
    /// length zero stands for everything from its offset to the end of the file or of the item data
    /// box, which is the widest meaning the format gives it: that is the reading to protect an item
    /// by. Without it the extent is empty, which is how <see cref="BmffReader"/> reads an item.
    /// Returns false when an extent leaves the file or the item data box, or there is no such box.
    /// </summary>
    internal bool TryResolve(BmffLocation location, bool wide, List<BmffSegment> segments)
    {
        segments.Clear();
        long origin = 0, limit = file.Length;
        if (location.Method == 1)
        {
            if (Data is null) return false;
            origin = Data.Payload;
            limit = Data.End - Data.Payload;
        }
        bool complete = true;
        foreach (BmffExtent extent in location.Extents)
        {
            if (location.BaseOffset > (ulong)limit || extent.Offset > (ulong)limit)
            {
                complete = false;
                continue;
            }
            long start = (long)location.BaseOffset + (long)extent.Offset;
            if (start > limit)
            {
                complete = false;
                continue;
            }
            long length;
            if (extent.Length == 0)
            {
                length = wide ? limit - start : 0;
            }
            else if (extent.Length > (ulong)(limit - start))
            {
                // What is inside the file is still addressed, and still to be protected.
                complete = false;
                length = limit - start;
            }
            else
            {
                length = (long)extent.Length;
            }
            if (length != 0) segments.Add(new((int)(origin + start), (int)length));
        }
        return complete;
    }

    /// <summary>Whether an item has an extent of length zero: one that runs to the end of its source.</summary>
    internal static bool IsOpenEnded(BmffLocation location)
    {
        foreach (BmffExtent extent in location.Extents)
            if (extent.Length == 0) return true;
        return false;
    }

    private void Walk()
    {
        int position = 0;
        BmffBox first = Next(ref position, file.Length, 1);
        if (first.Type != FileType)
            throw Fail("The BMFF file does not start with a file type box.", 0);
        int brands = first.End - first.Payload;
        if (brands < 8 || brands % 4 != 0)
            throw Fail("The file type box payload is truncated or unaligned.", first.Payload);
        topLevel.Add(first);
        while (position < file.Length)
        {
            BmffBox box = Next(ref position, file.Length, 1);
            topLevel.Add(box);
            if (box.Type != Meta) continue;
            if (MetaBox is not null) throw Fail("The meta box appears more than once.", box.Start);
            MetaBox = box;
        }
        if (MetaBox is not null) ReadMeta(MetaBox);
    }

    private void ReadMeta(BmffBox meta)
    {
        if (meta.End - meta.Payload < 4) throw Fail("The meta box header is truncated.", meta.Payload);
        int position = meta.Payload + 4; // version and flags of the FullBox
        bool handler = false;
        while (position < meta.End)
        {
            BmffBox child = Next(ref position, meta.End, 2);
            children.Add(child);
            switch (child.Type)
            {
                case Handler:
                    if (handler) throw Fail("The handler box appears more than once.", child.Start);
                    handler = true;
                    break;
                case PrimaryItemBox:
                    if (PrimaryItem is not null) throw Fail("The primary item box appears more than once.", child.Start);
                    PrimaryItem = ReadPrimaryItem(child);
                    break;
                case ItemInformation:
                    if (Information is not null) throw Fail("The item information box appears more than once.", child.Start);
                    Information = child;
                    ReadInformation(child);
                    break;
                case ItemLocation:
                    if (Locations is not null) throw Fail("The item location box appears more than once.", child.Start);
                    Locations = child;
                    ReadLocations(child);
                    break;
                case ItemData:
                    if (Data is not null) throw Fail("The item data box appears more than once.", child.Start);
                    Data = child;
                    break;
                case ItemReference:
                    References ??= child;
                    ReferenceBoxes++;
                    break;
            }
        }
    }

    private uint ReadPrimaryItem(BmffBox box)
    {
        int length = box.End - box.Payload;
        if (length < 4) throw Fail("The primary item box is truncated.", box.Payload);
        byte version = file[box.Payload];
        if (version == 0)
        {
            if (length < 6) throw Fail("The primary item ID is truncated.", box.Payload + 4);
            return U16(box.Payload + 4);
        }
        if (version == 1)
        {
            if (length < 8) throw Fail("The primary item ID is truncated.", box.Payload + 4);
            return U32(box.Payload + 4);
        }
        throw Fail("The primary item version is not defined.", box.Payload);
    }

    private void ReadInformation(BmffBox box)
    {
        int length = box.End - box.Payload;
        if (length < 4) throw Fail("The item information box is truncated.", box.Payload);
        InformationVersion = file[box.Payload];
        int countSize = InformationVersion == 0 ? 2 : 4;
        if (length < 4 + countSize) throw Fail("The item information entry count is truncated.", box.Payload + 4);
        uint declared = countSize == 2 ? U16(box.Payload + 4) : U32(box.Payload + 4);
        int position = box.Payload + 4 + countSize;
        while (position < box.End)
        {
            BmffBox entry = Next(ref position, box.End, 3);
            if (entry.Type != ItemEntry) continue;
            DeclaredItems++;
            ReadItemEntry(entry);
        }
        if ((uint)DeclaredItems != declared)
            throw Fail("The item information entry count does not match its entries.", box.Payload + 4);
    }

    private void ReadItemEntry(BmffBox box)
    {
        int length = box.End - box.Payload;
        if (length < 4) throw Fail("The item entry is truncated.", box.Payload);
        byte version = file[box.Payload];
        int at = box.Payload + 4;
        if (version > 3)
        {
            HasUnknownItemEntry = true;
            return;
        }
        int idSize = version == 3 ? 4 : 2;
        if (version < 2)
        {
            // No item type: only the ID matters, so that a new item does not take it.
            if (idSize > box.End - at)
            {
                HasUnknownItemEntry = true;
                return;
            }
            AddItem(new(U16(at), version, 0, null), box);
            return;
        }
        Require(at, idSize, box.End);
        uint id = idSize == 2 ? U16(at) : U32(at);
        at += idSize;
        Require(at, 6, box.End); // item_protection_index and item_type
        uint type = U32(at + 2);
        int nameEnd = Terminated(at + 6, box.End);
        string? contentType = null;
        if (type == MimeItem)
        {
            int contentEnd = Terminated(nameEnd, box.End);
            contentType = Encoding.Latin1.GetString(file, nameEnd, contentEnd - nameEnd - 1);
        }
        AddItem(new(id, version, type, contentType), box);
    }

    private void AddItem(BmffItem item, BmffBox box)
    {
        // Entries without a type are outside what the reader looks at; among the typed ones an ID
        // is declared once.
        if (item.Version >= 2)
            foreach (BmffItem other in items)
                if (other.Version >= 2 && other.Id == item.Id)
                    throw Fail("The item ID is declared more than once.", box.Start);
        items.Add(item);
    }

    private void ReadLocations(BmffBox box)
    {
        int length = box.End - box.Payload;
        if (length < 8) throw Fail("The item location box is truncated.", box.Payload);
        LocationVersion = file[box.Payload];
        if (LocationVersion > 2) throw Fail("The item location version is not defined.", box.Payload);
        int at = box.Payload + 4;
        OffsetSize = file[at] >> 4;
        LengthSize = file[at] & 0x0F;
        BaseOffsetSize = file[at + 1] >> 4;
        IndexSize = file[at + 1] & 0x0F;
        if (LocationVersion == 0 && IndexSize != 0)
            throw Fail("The reserved item location nibble is not zero.", at + 1);
        if (!FieldSize(OffsetSize) || !FieldSize(LengthSize) || !FieldSize(BaseOffsetSize) || !FieldSize(IndexSize))
            throw Fail("An item location field size is not 0, 4 or 8 bytes.", at);
        at += 2;
        int idSize = LocationVersion < 2 ? 2 : 4;
        Require(at, idSize, box.End);
        uint count = idSize == 2 ? U16(at) : U32(at);
        at += idSize;
        for (uint index = 0; index < count; index++)
        {
            if (++structures > limits.MaxSegments)
                throw Limit("The item location budget was exceeded.", at);
            Require(at, idSize, box.End);
            uint id = idSize == 2 ? U16(at) : U32(at);
            at += idSize;
            int method = 0;
            if (LocationVersion >= 1)
            {
                Require(at, 2, box.End);
                ushort field = U16(at);
                if ((field & 0xFFF0) != 0) throw Fail("The reserved construction method bits are not zero.", at);
                method = field & 0x0F;
                at += 2;
            }
            if (method == 2)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "Item construction method 2 is outside this profile.", at);
            if (method > 2) throw Fail("The construction method is not defined.", at);
            Require(at, 2, box.End);
            if (U16(at) != 0)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "An item location refers to data outside the file.", at);
            at += 2;
            ulong baseOffset = Sized(ref at, BaseOffsetSize, box.End);
            Require(at, 2, box.End);
            int extentCount = U16(at);
            at += 2;
            var extents = new List<BmffExtent>(Math.Min(extentCount, 4));
            for (int extent = 0; extent < extentCount; extent++)
            {
                if (++structures > limits.MaxSegments)
                    throw Limit("The item extent budget was exceeded.", at);
                ulong label = Sized(ref at, IndexSize, box.End);
                ulong offset = Sized(ref at, OffsetSize, box.End);
                ulong extentLength = Sized(ref at, LengthSize, box.End);
                extents.Add(new(label, offset, extentLength));
            }
            foreach (BmffLocation location in locations)
                if (location.Id == id) throw Fail("The item location is declared more than once.", box.Start);
            locations.Add(new(id, method, baseOffset, extents));
        }
    }

    /// <summary>Reads one box header and validates its size against the range that encloses it.</summary>
    private BmffBox Next(ref int position, int end, int depth)
    {
        if (depth > limits.MaxDirectoryDepth)
            throw Limit("The box nesting budget was exceeded.", position);
        if (++boxes > limits.MaxSegments)
            throw Limit("The box count budget was exceeded.", position);
        int start = position;
        int available = end - start;
        if (available < 8) throw Fail("A box header is truncated.", start);
        uint size32 = U32(start);
        uint type = U32(start + 4);
        int header = 8;
        long size = size32;
        bool open = false;
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
            size = available; // a zero size runs to the end of the enclosing box
            open = true;
        }
        if (size < header) throw Fail("A box size is smaller than its own header.", start);
        if (size > available) throw Fail("A box size exceeds the box that encloses it.", start);
        position = start + (int)size;
        return new BmffBox(type, start, header, (int)size, open);
    }

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
        int stop = file.AsSpan(at, end - at).IndexOf((byte)0);
        if (stop < 0) throw Fail("An item entry name is not NUL-terminated.", at);
        return at + stop + 1;
    }

    private void Require(int at, int count, int end)
    {
        if (count > end - at) throw Fail("A box field is truncated.", at);
    }

    private static bool FieldSize(int size) => size is 0 or 4 or 8;

    private ushort U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at, 2));

    private uint U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at, 4));

    private ulong U64(int at) => BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(at, 8));

    private MetadataException Fail(string message, long offset) =>
        new(MetadataErrorCode.InvalidBmff, message, offset >= 0 && offset <= file.Length ? offset : null);

    private MetadataException Limit(string message, long offset) =>
        new(MetadataErrorCode.LimitExceeded, message, offset >= 0 && offset <= file.Length ? offset : null);
}
