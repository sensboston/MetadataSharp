using System.Buffers.Binary;
using System.Text;

namespace MetadataSharp.Tests.Bmff;

/// <summary>
/// Authored synthetic HEIC, HEIF and AVIF files for the write tests, and a reader of boxes and item
/// locations that shares nothing with production code. Every offset is computed here, so a test can
/// say where an item was and look for its bytes there, or through the item location box, afterwards.
/// </summary>
internal sealed class BmffFixture
{
    internal const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal const string XmpContentType = "application/rdf+xml";
    internal const uint ImageId = 1, ExifId = 2, XmpId = 3, GridId = 4;
    /// <summary>Offsets inside the EXIF block: the Make text, DateTimeOriginal and the MakerNote.</summary>
    internal const int MakeAt = 80, MakeLength = 12, DateAt = 92, DateLength = 20, NoteAt = 112, NoteLength = 16;
    /// <summary>The length of the EXIF block, and the offset of its Exif directory.</summary>
    internal const int TiffLength = 128, ExifDirectoryAt = 50;
    /// <summary>
    /// The MakerNote of the layout whose EXIF item is longer than a JPEG APP1 segment can hold
    /// (65,527 bytes of TIFF): the note alone is more than that.
    /// </summary>
    internal const int LargeNoteLength = 70_000;

    /// <summary>One item of a file to build.</summary>
    internal sealed record Item(uint Id, string Type, byte[] Data)
    {
        internal string? ContentType { get; init; }
        /// <summary>Stored in the item data box and addressed from its start (construction method 1).</summary>
        internal bool InIdat { get; init; }
        /// <summary>Stored as two extents, the second one first in the file; zero for one extent.</summary>
        internal int Cut { get; init; }
        /// <summary>Addresses the bytes of another item instead of having its own.</summary>
        internal uint SameAs { get; init; }
        /// <summary>The length the item location box states, when it is not the real one.</summary>
        internal long StatedLength { get; init; } = -1;
        internal bool Hidden { get; init; }
    }

    /// <summary>How a file is laid out.</summary>
    internal sealed class Layout
    {
        internal string Brand { get; set; } = "heic";
        /// <summary>The meta box follows the media data box instead of preceding it.</summary>
        internal bool MetaLast { get; set; }
        /// <summary>The version of the item location box: 0, 1 or 2.</summary>
        internal int Version { get; set; }
        internal int OffsetSize { get; set; } = 4;
        internal int LengthSize { get; set; } = 4;
        internal int BaseSize { get; set; }
        internal int IndexSize { get; set; }
        /// <summary>32-bit item IDs: item entries of version 3, primary item and references of version 1.</summary>
        internal bool WideIds { get; set; }
        internal bool Primary { get; set; } = true;
        /// <summary>The media data box has size zero: it runs to the end of the file.</summary>
        internal bool OpenData { get; set; }
        internal List<Item> Items { get; } = [];
        /// <summary>Top-level boxes between the file type box and the rest, and after everything.</summary>
        internal byte[] Before { get; set; } = [];
        internal byte[] After { get; set; } = [];
        /// <summary>Whether the pinned tool reads the metadata items of such a file once it has them.</summary>
        internal bool ToolReads { get; set; }
        /// <summary>How many bytes precede the TIFF header in the EXIF item: 4, or 10 with an "Exif\0\0" marker.</summary>
        internal int ExifPrefix { get; set; } = 4;
        /// <summary>The item the primary item box names.</summary>
        internal uint PrimaryId { get; set; } = ImageId;
        /// <summary>The boxes of the property container, in order: the extents of the image alone, unless a test says more.</summary>
        internal List<byte[]> Properties { get; } = [Box("ispe", U32(0), U32(64), U32(48))];
        /// <summary>
        /// What the association box gives each item, in order: the numbers of its properties, from
        /// one, and whether a reader has to understand each.
        /// </summary>
        internal List<(uint Item, (bool Essential, int Index)[] Properties)> Associations { get; } =
            new() { (ImageId, new[] { (true, 1) }) };
        /// <summary>The association box numbers the properties in 15 bits instead of 7: bit 0 of its flags.</summary>
        internal bool WideProperties { get; set; }
        /// <summary>The association box names the items in 32 bits: its version 1.</summary>
        internal bool WideAssociations { get; set; }
        /// <summary>Boxes of the item properties box after the association box.</summary>
        internal byte[] AfterAssociations { get; set; } = [];
        /// <summary>The meta box has no item properties box.</summary>
        internal bool NoProperties { get; set; }
    }

    private BmffFixture(string name, bool big, Layout layout, byte[] bytes, Dictionary<uint, (int Start, int Length)[]> positions)
    {
        Name = name;
        Big = big;
        Plan = layout;
        Bytes = bytes;
        Positions = positions;
    }

    internal string Name { get; }
    internal bool Big { get; }
    internal Layout Plan { get; }
    internal byte[] Bytes { get; }
    /// <summary>Where the bytes of each item are in the file, extent by extent in declared order.</summary>
    internal Dictionary<uint, (int Start, int Length)[]> Positions { get; }
    internal bool HasExif => Positions.ContainsKey(ExifId);
    internal bool HasXmp => Positions.ContainsKey(XmpId);
    /// <summary>Where the TIFF header of the EXIF item is, for an item of one extent.</summary>
    internal int Tiff => Positions[ExifId][0].Start + Plan.ExifPrefix;

    internal static readonly string[] Names =
        ["heic", "heic-last", "avif", "avif-idat", "heif-wide", "heic-extents", "heic-large", "heic-bare", "heic-bare-last"];

    /// <summary>The layouts that have an EXIF and an XMP item.</summary>
    internal static readonly string[] WithMetadata = ["heic", "heic-last", "avif", "avif-idat", "heif-wide", "heic-extents", "heic-large"];

    /// <summary>The layouts that have an image and nothing else.</summary>
    internal static readonly string[] Bare = ["heic-bare", "heic-bare-last"];

    internal static BmffFixture Create(string name, bool big) => Build(name, big, LayoutOf(name, big));

    /// <summary>The layout of a named fixture, for a test to change before it builds the file.</summary>
    internal static Layout LayoutOf(string name, bool big)
    {
        var layout = new Layout();
        bool metadata = true, idat = false;
        int note = NoteLength;
        string image = "hvc1";
        switch (name)
        {
            case "heic":
                // The usual shape: meta before the data, 32-bit offsets and lengths, no base offset.
                layout.ToolReads = true;
                break;
            case "heic-last":
                // The meta box after the data, items addressed by a base offset, the EXIF item with
                // the marker some writers put before the TIFF header.
                layout.MetaLast = true;
                layout.BaseSize = 4;
                layout.ExifPrefix = 10;
                layout.ToolReads = true;
                break;
            case "avif":
                layout.Brand = "avif";
                layout.Version = 1;
                layout.ToolReads = true;
                image = "av01";
                break;
            case "avif-idat":
                // EXIF and XMP in the item data box, with a derived image beside them.
                layout.Brand = "avif";
                layout.Version = 1;
                image = "av01";
                idat = true;
                break;
            case "heif-wide":
                // The brand of a HEIF file that is not HEVC-coded in particular.
                layout.Brand = "mif1";
                layout.MetaLast = true;
                layout.Version = 2;
                layout.OffsetSize = 8;
                layout.LengthSize = 8;
                layout.BaseSize = 8;
                layout.IndexSize = 4;
                layout.WideIds = true;
                break;
            case "heic-extents":
                break;
            case "heic-large":
                // The usual shape, with an EXIF item no JPEG segment could hold.
                layout.ToolReads = true;
                note = LargeNoteLength;
                break;
            case "heic-bare":
                layout.ToolReads = true;
                metadata = false;
                break;
            case "heic-bare-last":
                layout.MetaLast = true;
                layout.Version = 1;
                layout.ToolReads = true;
                metadata = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }
        layout.Items.Add(new Item(ImageId, image, Pattern(384, 7)));
        if (metadata)
        {
            layout.Items.Add(new Item(ExifId, "Exif", ExifItem(big, layout.ExifPrefix, note))
            {
                InIdat = idat, Hidden = true, Cut = name == "heic-extents" ? 40 : 0
            });
            layout.Items.Add(new Item(XmpId, "mime", Xmp("bmff")) { ContentType = XmpContentType, InIdat = idat, Hidden = true });
        }
        if (idat) layout.Items.Add(new Item(GridId, "grid", [0, 0, 0, 0, 0, 64, 0, 48]) { InIdat = true });
        return layout;
    }

    internal static BmffFixture Build(string name, bool big, Layout layout)
    {
        // The boxes do not change their length with the offsets written into them, so the file is
        // assembled twice: once to learn where the data lies, once with those addresses.
        var none = new Dictionary<uint, (int Start, int Length)[]>();
        Assemble(layout, none, out int data, out int idat);
        var positions = new Dictionary<uint, (int Start, int Length)[]>();
        int inData = data, inIdat = idat;
        foreach (Item item in layout.Items)
        {
            if (item.SameAs != 0) continue;
            int at = item.InIdat ? inIdat : inData;
            positions[item.Id] = item.Cut == 0
                ? new[] { (at, item.Data.Length) }
                : new[] { (at + item.Data.Length - item.Cut, item.Cut), (at, item.Data.Length - item.Cut) };
            if (item.InIdat) inIdat += item.Data.Length;
            else inData += item.Data.Length;
        }
        foreach (Item item in layout.Items)
            if (item.SameAs != 0) positions[item.Id] = positions[item.SameAs];
        byte[] bytes = Assemble(layout, positions, out _, out _);
        return new BmffFixture(name, big, layout, bytes, positions);
    }

    private static byte[] Assemble(Layout layout, Dictionary<uint, (int Start, int Length)[]> positions, out int data, out int idat)
    {
        var inData = new List<byte[]>();
        var inIdat = new List<byte[]>();
        foreach (Item item in layout.Items)
        {
            if (item.SameAs != 0) continue;
            // An item of two extents is stored with its second part first.
            byte[] stored = item.Cut == 0 ? item.Data : Concat(item.Data[item.Cut..], item.Data[..item.Cut]);
            (item.InIdat ? inIdat : inData).Add(stored);
        }
        byte[] fileType = Box("ftyp", Ascii(layout.Brand), U32(0), Ascii("mif1"), Ascii(layout.Brand));
        byte[] media = layout.OpenData
            ? Concat(U32(0), Ascii("mdat"), Concat(inData.ToArray()))
            : Box("mdat", Concat(inData.ToArray()));

        // The start of the item data box inside the meta box is needed for the addresses of its
        // items in the file; the items themselves are addressed from its start.
        int metaStart = fileType.Length + layout.Before.Length + (layout.MetaLast ? media.Length : 0);
        byte[] MetaBox(int idatPayload)
        {
            var parts = new List<byte[]>
            {
                Box("hdlr", U32(0), U32(0), Ascii("pict"), new byte[13])
            };
            if (layout.Primary) parts.Add(layout.WideIds ? Box("pitm", U32(0x01000000), U32(layout.PrimaryId)) : Box("pitm", U32(0), U16(layout.PrimaryId)));
            parts.Add(Locations(layout, positions, idatPayload));
            parts.Add(Box("iinf", U32(0), U16((uint)layout.Items.Count), Concat(layout.Items.Select(item => Entry(layout, item)).ToArray())));
            byte[][] references = layout.Items.Where(item => item.Type is "Exif" or "mime")
                .Select(item => layout.WideIds
                    ? Box("cdsc", U32(item.Id), U16(1), U32(ImageId))
                    : Box("cdsc", U16(item.Id), U16(1), U16(ImageId)))
                .ToArray();
            if (references.Length != 0) parts.Add(Box("iref", U32(layout.WideIds ? 0x01000000u : 0), Concat(references)));
            if (!layout.NoProperties) parts.Add(ItemProperties(layout));
            if (inIdat.Count != 0) parts.Add(Box("idat", Concat(inIdat.ToArray())));
            return Box("meta", U32(0), Concat(parts.ToArray()));
        }
        byte[] draft = MetaBox(0);
        int idatLength = inIdat.Sum(part => part.Length);
        idat = inIdat.Count == 0 ? 0 : metaStart + draft.Length - idatLength;
        byte[] meta = MetaBox(idat);
        data = fileType.Length + layout.Before.Length + (layout.MetaLast ? 0 : meta.Length) + 8;
        return layout.MetaLast
            ? Concat(fileType, layout.Before, media, meta, layout.After)
            : Concat(fileType, layout.Before, meta, media, layout.After);
    }

    /// <summary>The item properties box: the property container, the association box, and what a test puts after them.</summary>
    private static byte[] ItemProperties(Layout layout)
    {
        var associations = new List<byte[]>
        {
            U32((layout.WideAssociations ? 0x01000000u : 0u) | (layout.WideProperties ? 1u : 0u)),
            U32((uint)layout.Associations.Count)
        };
        foreach ((uint item, (bool Essential, int Index)[] properties) in layout.Associations)
        {
            associations.Add(layout.WideAssociations ? U32(item) : U16(item));
            associations.Add(new[] { (byte)properties.Length });
            foreach ((bool essential, int index) in properties)
            {
                associations.Add(layout.WideProperties
                    ? U16((uint)((essential ? 0x8000 : 0) | index))
                    : new[] { (byte)((essential ? 0x80 : 0) | index) });
            }
        }
        return Box("iprp", Box("ipco", layout.Properties.ToArray()), Box("ipma", associations.ToArray()), layout.AfterAssociations);
    }

    /// <summary>A colr property: the kind of colour information, then the information.</summary>
    internal static byte[] Colour(string kind, byte[] body) => Box("colr", Ascii(kind), body);

    private static byte[] Entry(Layout layout, Item item)
    {
        uint versionAndFlags = (layout.WideIds ? 0x03000000u : 0x02000000u) | (item.Hidden ? 1u : 0u);
        var parts = new List<byte[]>
        {
            U32(versionAndFlags), layout.WideIds ? U32(item.Id) : U16(item.Id), U16(0), Ascii(item.Type), new byte[] { 0 }
        };
        if (item.ContentType is not null) parts.Add(Concat(Ascii(item.ContentType), new byte[] { 0 }));
        return Box("infe", parts.ToArray());
    }

    private static byte[] Locations(Layout layout, Dictionary<uint, (int Start, int Length)[]> positions, int idatPayload)
    {
        byte[] Sized(int size, long value) => size switch
        {
            0 => Array.Empty<byte>(),
            4 => U32((uint)value),
            _ => U64((ulong)value)
        };
        var parts = new List<byte[]>
        {
            U32((uint)layout.Version << 24),
            new[] { (byte)((layout.OffsetSize << 4) | layout.LengthSize), (byte)((layout.BaseSize << 4) | layout.IndexSize) },
            layout.Version < 2 ? U16((uint)layout.Items.Count) : U32((uint)layout.Items.Count)
        };
        foreach (Item item in layout.Items)
        {
            Item stored = item.SameAs == 0 ? item : layout.Items.First(other => other.Id == item.SameAs);
            (int Start, int Length)[] extents = positions.TryGetValue(item.Id, out (int Start, int Length)[]? known)
                ? known
                : stored.Cut == 0
                    ? new[] { (0, stored.Data.Length) }
                    : new[] { (0, stored.Cut), (0, stored.Data.Length - stored.Cut) };
            long origin = stored.InIdat ? idatPayload : 0;
            long lowest = extents.Min(extent => extent.Start) - origin;
            long first = layout.BaseSize == 0 ? 0 : lowest;
            parts.Add(layout.Version < 2 ? U16(item.Id) : U32(item.Id));
            if (layout.Version >= 1) parts.Add(U16(stored.InIdat ? 1u : 0u));
            parts.Add(U16(0));
            parts.Add(Sized(layout.BaseSize, first));
            parts.Add(U16((uint)extents.Length));
            foreach ((int start, int length) in extents)
            {
                parts.Add(Sized(layout.IndexSize, 0));
                parts.Add(Sized(layout.OffsetSize, start - origin - first));
                parts.Add(Sized(layout.LengthSize, item.StatedLength >= 0 ? item.StatedLength : length));
            }
        }
        return Box("iloc", parts.ToArray());
    }

    /// <summary>
    /// The EXIF item: the offset of the TIFF header, then the TIFF. IFD0 at 8 (Make at 80,
    /// Orientation, ExifIFD) and an Exif IFD at 50 (DateTimeOriginal at 92, a MakerNote of sixteen
    /// bytes at 112); 128 bytes in all, with no byte between its parts. A longer MakerNote makes
    /// the block longer and changes nothing else.
    /// </summary>
    internal static byte[] ExifItem(bool big, int prefix = 4, int note = NoteLength)
    {
        byte[] tiff = new byte[TiffLength - NoteLength + note];
        void Put16(int at, int value)
        {
            if (big) BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), (ushort)value);
            else BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at), (ushort)value);
        }
        void Put32(int at, long value)
        {
            if (big) BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at), (uint)value);
            else BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(at), (uint)value);
        }
        void Field(int at, int tag, int type, long count, long value, bool inlineShort = false)
        {
            Put16(at, tag);
            Put16(at + 2, type);
            Put32(at + 4, count);
            if (inlineShort) Put16(at + 8, (int)value);
            else Put32(at + 8, value);
        }
        tiff[0] = tiff[1] = big ? (byte)0x4D : (byte)0x49;
        Put16(2, 42);
        Put32(4, 8);
        Put16(8, 3);
        Field(10, 0x010F, 2, MakeLength, MakeAt);
        Field(22, 0x0112, 3, 1, 1, inlineShort: true);
        Field(34, 0x8769, 4, 1, ExifDirectoryAt);
        Put16(ExifDirectoryAt, 2);
        Field(ExifDirectoryAt + 2, 0x9003, 2, DateLength, DateAt);
        Field(ExifDirectoryAt + 14, 0x927C, 7, note, NoteAt);
        Ascii("TestCompany\0").CopyTo(tiff, MakeAt);
        Ascii("2020:01:02 03:04:05\0").CopyTo(tiff, DateAt);
        for (int i = 0; i < note; i++) tiff[NoteAt + i] = (byte)(0xA0 + i);
        return prefix == 4
            ? Concat(U32(0), tiff)
            : Concat(U32(6), Ascii("Exif"), new byte[] { 0, 0 }, tiff);
    }

    internal static byte[] Xmp(string label) => Encoding.UTF8.GetBytes(
        $"<rdf:RDF xmlns:rdf='{Rdf}' xmlns:xmp='http://ns.adobe.com/xap/1.0/'><rdf:Description rdf:about=''>" +
        $"<xmp:Label>{label}</xmp:Label></rdf:Description></rdf:RDF>");

    internal static byte[] Pattern(int count, int seed)
    {
        byte[] bytes = new byte[count];
        // No two neighbours are equal, so the pattern holds no byte order mark of a TIFF.
        for (int i = 0; i < count; i++) bytes[i] = (byte)(0x80 | ((i * 5 + seed) & 0x7F));
        return bytes;
    }

    // ---- an independent reading of a file: boxes and item locations ----

    internal sealed record BoxInfo(string Type, int Start, int Header, int Length)
    {
        internal int Payload => Start + Header;
        internal int End => Start + Length;
    }

    internal static List<BoxInfo> Boxes(byte[] file, int start, int end)
    {
        var boxes = new List<BoxInfo>();
        int at = start;
        while (at < end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            int header = 8;
            if (size == 1)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(at + 8));
                header = 16;
            }
            else if (size == 0)
            {
                size = end - at;
            }
            if (size < header || size > end - at) throw new InvalidDataException("A box leaves its parent.");
            boxes.Add(new(Encoding.Latin1.GetString(file, at + 4, 4), at, header, (int)size));
            at += (int)size;
        }
        return boxes;
    }

    internal static List<BoxInfo> TopLevel(byte[] file) => Boxes(file, 0, file.Length);

    internal static string Types(List<BoxInfo> boxes) => string.Join(' ', boxes.Select(box => box.Type));

    internal static BoxInfo Meta(byte[] file) => TopLevel(file).Single(box => box.Type == "meta");

    internal static List<BoxInfo> MetaChildren(byte[] file)
    {
        BoxInfo meta = Meta(file);
        return Boxes(file, meta.Payload + 4, meta.End);
    }

    /// <summary>The children of the item properties box of the file, in order.</summary>
    internal static List<BoxInfo> PropertyChildren(byte[] file)
    {
        BoxInfo properties = MetaChildren(file).Single(box => box.Type == "iprp");
        return Boxes(file, properties.Payload, properties.End);
    }

    /// <summary>The boxes of the property container of the file, in order: an association names one by its place, from one.</summary>
    internal static List<BoxInfo> PropertyBoxes(byte[] file)
    {
        BoxInfo container = PropertyChildren(file).Single(box => box.Type == "ipco");
        return Boxes(file, container.Payload, container.End);
    }

    /// <summary>The types of the boxes of the property container; colour information with its kind, as in "colr:prof".</summary>
    internal static string PropertyTypes(byte[] file) => string.Join(' ', PropertyBoxes(file).Select(box =>
        box.Type == "colr" ? "colr:" + Encoding.Latin1.GetString(file, box.Payload, 4) : box.Type));

    /// <summary>
    /// What the association box of the file gives each item, as "1:[!1,2] 5:[!1]": the item, then
    /// the numbers of its properties in order, with an exclamation mark before an essential one.
    /// The text starts with "wide " when the numbers have 15 bits, and with "v1 " when the items
    /// have 32.
    /// </summary>
    internal static string Associations(byte[] file)
    {
        BoxInfo box = PropertyChildren(file).Single(child => child.Type == "ipma");
        int at = box.Payload;
        bool wideItems = file[at] != 0, wide = (file[at + 3] & 1) != 0;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 4));
        at += 8;
        var entries = new List<string>();
        for (uint entry = 0; entry < count; entry++)
        {
            uint item = wideItems ? BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at));
            at += wideItems ? 4 : 2;
            int properties = file[at++];
            var numbers = new List<string>();
            for (int index = 0; index < properties; index++)
            {
                int value = wide ? BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at)) : file[at];
                at += wide ? 2 : 1;
                bool essential = (value & (wide ? 0x8000 : 0x80)) != 0;
                numbers.Add((essential ? "!" : "") + (value & (wide ? 0x7FFF : 0x7F)));
            }
            entries.Add($"{item}:[{string.Join(',', numbers)}]");
        }
        if (at != box.End) throw new InvalidDataException("The association box does not end with its last entry.");
        return (wideItems ? "v1 " : "") + (wide ? "wide " : "") + string.Join(' ', entries);
    }

    /// <summary>What the item location box of the file says about an item: its method and its extents in the file.</summary>
    internal static (int Method, (int Start, int Length)[] Extents) Locate(byte[] file, uint id)
    {
        List<BoxInfo> children = MetaChildren(file);
        BoxInfo iloc = children.Single(box => box.Type == "iloc");
        BoxInfo? idat = children.SingleOrDefault(box => box.Type == "idat");
        int at = iloc.Payload;
        int version = file[at];
        int offsetSize = file[at + 4] >> 4, lengthSize = file[at + 4] & 15, baseSize = file[at + 5] >> 4;
        int indexSize = version == 0 ? 0 : file[at + 5] & 15;
        at += 6;
        long Read(int size)
        {
            long value = size switch
            {
                0 => 0,
                2 => BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at)),
                4 => BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at)),
                _ => (long)BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(at))
            };
            at += size;
            return value;
        }
        int idSize = version < 2 ? 2 : 4;
        long count = Read(idSize);
        for (long i = 0; i < count; i++)
        {
            long item = Read(idSize);
            int method = version >= 1 ? (int)Read(2) & 15 : 0;
            Read(2);
            long first = Read(baseSize);
            int extentCount = (int)Read(2);
            var extents = new (int Start, int Length)[extentCount];
            for (int extent = 0; extent < extentCount; extent++)
            {
                Read(indexSize);
                long offset = Read(offsetSize);
                long length = Read(lengthSize);
                long origin = method == 1 ? idat!.Payload : 0;
                extents[extent] = ((int)(origin + first + offset), (int)length);
            }
            if (item == id) return (method, extents);
        }
        if (at > iloc.End) throw new InvalidDataException("The item location box is truncated.");
        throw new InvalidDataException($"Item {id} has no location.");
    }

    /// <summary>The bytes of an item, read through the item location box of the file.</summary>
    internal static byte[] ItemBytes(byte[] file, uint id) =>
        Concat(Locate(file, id).Extents.Select(extent => file.AsSpan(extent.Start, extent.Length).ToArray()).ToArray());

    /// <summary>The item IDs the item information box declares, with their types, in order.</summary>
    internal static List<(uint Id, string Type)> Declared(byte[] file)
    {
        BoxInfo iinf = MetaChildren(file).Single(box => box.Type == "iinf");
        int countSize = file[iinf.Payload] == 0 ? 2 : 4;
        var items = new List<(uint Id, string Type)>();
        foreach (BoxInfo entry in Boxes(file, iinf.Payload + 4 + countSize, iinf.End))
        {
            int version = file[entry.Payload];
            int at = entry.Payload + 4;
            uint id = version == 3 ? BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at));
            at += (version == 3 ? 4 : 2) + 2;
            items.Add((id, Encoding.Latin1.GetString(file, at, 4)));
        }
        return items;
    }

    // ---- bytes ----

    internal static byte[] Box(string type, params byte[][] payload)
    {
        byte[] body = Concat(payload);
        return Concat(U32((uint)(8 + body.Length)), Ascii(type), body);
    }

    internal static byte[] Concat(params byte[][] parts)
    {
        int length = 0;
        foreach (byte[] part in parts) length += part.Length;
        var bytes = new byte[length];
        int at = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(bytes, at);
            at += part.Length;
        }
        return bytes;
    }

    internal static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    internal static byte[] U16(uint value) => [(byte)(value >> 8), (byte)value];

    internal static byte[] U32(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    internal static byte[] U64(ulong value) => [.. U32((uint)(value >> 32)), .. U32((uint)value)];
}
