using System.Buffers.Binary;
using System.Text;

namespace MetadataSharp.Tests.Cr3;

/// <summary>
/// Authored Canon CR3 files: the box structure a camera writes, with small directories this project
/// made up in place of a camera's. Nothing here comes from a real file, and nothing is shared with
/// production code: every offset a test asks about is computed while the file is built.
/// </summary>
internal sealed class Cr3Fixture
{
    internal const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    internal static readonly byte[] CanonBox = [0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48];
    internal static readonly byte[] XmpBox = [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];
    internal static readonly byte[] PreviewBox = [0xEA, 0xF4, 0x2B, 0x5E, 0x1C, 0x98, 0x4B, 0x88, 0xB9, 0xFB, 0xB7, 0xDC, 0x40, 0x6E, 0x4D, 0x16];

    /// <summary>One entry of a directory to build; the value is encoded once the byte order is known.</summary>
    internal sealed record Entry(ushort Tag, ushort Type, uint Count, Func<bool, byte[]> Value);

    /// <summary>What a file holds. A null payload leaves the box out.</summary>
    internal sealed class Layout
    {
        internal string Brand { get; set; } = "crx ";
        internal byte[]? Ifd0 { get; set; } = Tiff(true, Text(0x010F, "Canon"), Text(0x0110, "Canon EOS R6"), Shorts(0x0112, 6), Text(0x0132, "2021:01:02 03:04:05"));
        internal byte[]? Exif { get; set; } = Tiff(true, Rational(0x829A, 1, 250), Shorts(0x8827, 100), Raw(0x9000, 7, "0231"u8.ToArray()), Text(0x9003, "2021:01:02 03:04:05"));
        internal byte[]? MakerNote { get; set; } = Tiff(true, Shorts(0x0001, 20, 0, 3, 0, 2, 0, 0, 4, 0, 1), Text(0x0006, "IMG:EOS R6 CR3"), Text(0x0007, "Firmware Version 1.0.0"), Longs(0x0010, 0x80000453));
        internal byte[]? Gps { get; set; } = Tiff(true, Raw(0x0000, 1, [2, 3, 0, 0]), Text(0x0001, "N"), Rational(0x0006, 123, 10));
        internal byte[]? Xmp { get; set; } = Packet("cr3");
        /// <summary>The JPEG of the THMB box, 160 by 120, and of the PRVW box, 1620 by 1080.</summary>
        internal byte[]? Thumbnail { get; set; } = Picture(160, 120, 40);
        internal byte[]? Preview { get; set; } = Picture(1620, 1080, 400);
        /// <summary>The movie box states its size in 64 bits.</summary>
        internal bool WideMovie { get; set; }
        /// <summary>The media data box has size zero: it runs to the end of the file, and nothing follows it.</summary>
        internal bool OpenData { get; set; }
    }

    private Cr3Fixture(byte[] bytes, Dictionary<string, int> at)
    {
        Bytes = bytes;
        At = at;
    }

    internal byte[] Bytes { get; }

    /// <summary>
    /// Where a payload starts in the file, by box name: CMT1 to CMT4 and XMP name the first byte of
    /// the TIFF or of the packet, THMB and PRVW the first byte of the JPEG, moov and canon the box.
    /// </summary>
    internal Dictionary<string, int> At { get; }

    internal static Cr3Fixture Create(Layout? layout = null)
    {
        layout ??= new Layout();
        var at = new Dictionary<string, int>();
        // The Canon box: the boxes a camera writes before the directories, then the directories.
        var canon = new List<byte>(CanonBox);
        canon.AddRange(Box("CNCV", "CanonCR3_001/00.10.00/00.00.00"u8.ToArray()));
        canon.AddRange(Box("CCTP", new byte[12], Box("CCDT", new byte[16])));
        canon.AddRange(Box("CTBO", new byte[4]));
        canon.AddRange(Box("free", new byte[6]));
        var inCanon = new Dictionary<string, int>();
        void Add(string name, byte[]? payload)
        {
            if (payload is null) return;
            inCanon[name] = canon.Count + 8;
            canon.AddRange(Box(name, payload));
        }
        Add("CMT1", layout.Ifd0);
        Add("CMT2", layout.Exif);
        Add("CMT3", layout.MakerNote);
        Add("CMT4", layout.Gps);
        if (layout.Thumbnail is byte[] thumbnail)
        {
            // Version 0: width, height, the length of the JPEG, two more fields, the JPEG.
            inCanon["THMB"] = canon.Count + 8 + 16;
            canon.AddRange(Box("THMB", new byte[4], Be16(160), Be16(120), Be32((uint)thumbnail.Length), Be16(1), Be16(0), thumbnail));
        }
        byte[] fileType = FileType(layout.Brand);
        byte[] rest = Join(Box("mvhd", new byte[100]), Box("trak", Box("tkhd", new byte[84])));
        int movieHeader = layout.WideMovie ? 16 : 8;
        byte[] canonBox = Box("uuid", canon.ToArray());
        byte[] movie = layout.WideMovie ? WideBox("moov", canonBox, rest) : Box("moov", canonBox, rest);
        at["moov"] = fileType.Length;
        at["canon"] = fileType.Length + movieHeader;
        foreach ((string name, int offset) in inCanon)
            at[name] = fileType.Length + movieHeader + 8 + offset;
        var file = new List<byte>(fileType);
        file.AddRange(movie);
        if (layout.Xmp is byte[] xmp)
        {
            at["XMP"] = file.Count + 8 + 16;
            file.AddRange(Uuid(XmpBox, xmp));
        }
        if (layout.Preview is byte[] preview)
        {
            // Eight bytes, then the PRVW box: four bytes, a field, width, height, a field, the length of the JPEG.
            at["PRVW"] = file.Count + 8 + 16 + 8 + 8 + 16;
            file.AddRange(Uuid(PreviewBox, new byte[4], Be32(1),
                Box("PRVW", new byte[4], Be16(1), Be16(1620), Be16(1080), Be16(1), Be32((uint)preview.Length), preview)));
        }
        if (layout.OpenData)
        {
            file.AddRange(Join(Be32(0), "mdat"u8.ToArray(), new byte[32]));
        }
        else
        {
            file.AddRange(Box("mdat", new byte[32]));
            file.AddRange(Box("free", new byte[8]));
        }
        return new Cr3Fixture(file.ToArray(), at);
    }

    // ---- boxes ----

    internal static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    internal static byte[] Be16(int value) => [(byte)(value >> 8), (byte)value];

    internal static byte[] Be32(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    internal static byte[] Box(string type, params byte[][] parts)
    {
        byte[] payload = Join(parts);
        return Join(Be32((uint)(8 + payload.Length)), Encoding.ASCII.GetBytes(type), payload);
    }

    /// <summary>A box whose size field is one and whose size follows the type in 64 bits.</summary>
    internal static byte[] WideBox(string type, params byte[][] parts)
    {
        byte[] payload = Join(parts);
        return Join(Be32(1), Encoding.ASCII.GetBytes(type), Be32(0), Be32((uint)(16 + payload.Length)), payload);
    }

    internal static byte[] Uuid(byte[] identifier, params byte[][] parts) => Box("uuid", identifier, Join(parts));

    internal static byte[] FileType(string brand = "crx ") =>
        Box("ftyp", Encoding.ASCII.GetBytes(brand), Be32(1), "crx "u8.ToArray(), "isom"u8.ToArray());

    // ---- directories ----

    internal static Entry Text(ushort tag, string text) =>
        new(tag, 2, (uint)text.Length + 1, _ => [.. Encoding.ASCII.GetBytes(text), 0]);

    internal static Entry Raw(ushort tag, ushort type, byte[] bytes) => new(tag, type, (uint)bytes.Length, _ => bytes);

    internal static Entry Shorts(ushort tag, params ushort[] values) => new(tag, 3, (uint)values.Length, little =>
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) Write16(bytes.AsSpan(i * 2), values[i], little);
        return bytes;
    });

    internal static Entry Longs(ushort tag, params uint[] values) => new(tag, 4, (uint)values.Length, little =>
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) Write32(bytes.AsSpan(i * 4), values[i], little);
        return bytes;
    });

    internal static Entry Rational(ushort tag, uint numerator, uint denominator) => new(tag, 5, 1, little =>
    {
        var bytes = new byte[8];
        Write32(bytes, numerator, little);
        Write32(bytes.AsSpan(4), denominator, little);
        return bytes;
    });

    /// <summary>A complete TIFF: the header, one directory at offset 8 and the values that do not fit an entry after it.</summary>
    internal static byte[] Tiff(bool little, params Entry[] entries) => Tiff(little, 0, entries);

    /// <summary>The same, with the link to a following directory set to <paramref name="next"/>.</summary>
    internal static byte[] Tiff(bool little, uint next, Entry[] entries)
    {
        var header = new byte[8];
        header[0] = header[1] = little ? (byte)0x49 : (byte)0x4D;
        Write16(header.AsSpan(2), 42, little);
        Write32(header.AsSpan(4), 8, little);
        return Join(header, Ifd(little, 8, next, entries));
    }

    /// <summary>
    /// One directory that lies at <paramref name="at"/> in its TIFF, followed by the values that do
    /// not fit an entry; the result has an even length.
    /// </summary>
    internal static byte[] Ifd(bool little, int at, uint next, params Entry[] entries)
    {
        var head = new byte[2 + entries.Length * 12 + 4];
        var tail = new List<byte>();
        Write16(head, (ushort)entries.Length, little);
        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            Span<byte> slot = head.AsSpan(2 + i * 12, 12);
            Write16(slot, entry.Tag, little);
            Write16(slot[2..], entry.Type, little);
            Write32(slot[4..], entry.Count, little);
            byte[] value = entry.Value(little);
            if (value.Length <= 4)
            {
                value.CopyTo(slot[8..]);
                continue;
            }
            Write32(slot[8..], (uint)(at + head.Length + tail.Count), little);
            tail.AddRange(value);
            if ((tail.Count & 1) != 0) tail.Add(0);
        }
        Write32(head.AsSpan(2 + entries.Length * 12), next, little);
        return [.. head, .. tail];
    }

    internal static void Write16(Span<byte> at, ushort value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(at, value);
        else BinaryPrimitives.WriteUInt16BigEndian(at, value);
    }

    internal static void Write32(Span<byte> at, uint value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(at, value);
        else BinaryPrimitives.WriteUInt32BigEndian(at, value);
    }

    // ---- payloads ----

    /// <summary>An XMP packet with one property, <c>urn:owned:test</c> title, in the wrapper a camera writes.</summary>
    internal static byte[] Packet(string title) => Encoding.UTF8.GetBytes(
        "<?xpacket begin='\uFEFF' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
        $"<rdf:RDF xmlns:rdf=\"{Rdf}\"><rdf:Description rdf:about=\"\" xmlns:t=\"urn:owned:test\">" +
        $"<t:title>{title}</t:title></rdf:Description></rdf:RDF></x:xmpmeta>" + new string(' ', 64) + "\n<?xpacket end='w'?>");

    /// <summary>A JPEG stream that is a frame header and filler: enough to state its dimensions.</summary>
    internal static byte[] Picture(int width, int height, int filler = 0)
    {
        var bytes = new List<byte>
        {
            0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x01, 0x01, 0x11, 0x00,
        };
        for (int i = 0; i < filler; i++) bytes.Add(0x55);
        bytes.Add(0xFF);
        bytes.Add(0xD9);
        return bytes.ToArray();
    }
}
