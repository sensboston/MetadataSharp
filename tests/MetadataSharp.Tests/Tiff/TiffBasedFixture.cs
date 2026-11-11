using System.Buffers.Binary;
using System.Text;

namespace MetadataSharp.Tests.Tiff;

/// <summary>
/// Authored synthetic TIFF-based files for the write tests. Every offset below is written by the
/// test and none comes from production code, so a test can say where a strip, a MakerNote or a
/// value was and look for it there afterwards.
/// </summary>
internal sealed class TiffBasedFixture
{
    private TiffBasedFixture(string name, bool big, byte[] bytes)
    {
        Name = name;
        Big = big;
        Bytes = bytes;
    }

    internal string Name { get; }
    internal bool Big { get; }
    internal byte[] Bytes { get; }

    /// <summary>Where the primary directory is.</summary>
    internal int Primary { get; private init; } = 8;
    /// <summary>Where the Exif directory is.</summary>
    internal int Exif { get; private init; }
    /// <summary>Where the GPS directory is, or zero.</summary>
    internal int Gps { get; private init; }
    /// <summary>Strips and JPEG streams: the image data no edit may touch.</summary>
    internal (int Start, int Length)[] Image { get; private init; } = [];
    /// <summary>The MakerNote or DNGPrivateData value.</summary>
    internal (int Start, int Length) Note { get; private init; }
    /// <summary>The twenty bytes of DateTimeOriginal (Exif 0x9003).</summary>
    internal int Date { get; private init; }
    /// <summary>A text tag of the primary directory that every edit of a text uses, and its value.</summary>
    internal int TextTag { get; private init; }
    internal (int Start, int Length) Text { get; private init; }

    internal static readonly string[] Names = ["tiff", "dng", "nef", "cr2"];

    internal static TiffBasedFixture Create(string name, bool big) => name switch
    {
        "tiff" => Tiff(big),
        "dng" => Dng(big),
        "nef" => Nef(big),
        "cr2" => Cr2(big),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    /// <summary>
    /// A baseline TIFF. IFD0 at 8 (width, length, compression, Make at 130, one strip at 300 of 64
    /// bytes, Orientation, DateTime at 142, ExifIFD) and an Exif IFD at 170 (ExposureTime at 200,
    /// DateTimeOriginal at 208).
    /// </summary>
    internal static TiffBasedFixture Tiff(bool big, long stripLength = 64, int extraType = 0)
    {
        var layout = new Layout(364, big).Header()
            .Directory(8, 0,
                (0x0100, 3, 1, 4), (0x0101, 3, 1, 4), (0x0103, 3, 1, 1), (0x010F, 2, 12, 130),
                (0x0111, 4, 1, 300), (0x0112, 3, 1, 1), (0x0117, 4, 1, stripLength), (0x0132, 2, 20, 142),
                (0x8769, 4, 1, 170))
            .Ascii(130, "TestCompany\0")
            .Ascii(142, "2020:01:02 03:04:05\0")
            .Directory(170, 0, (0x829A, 5, 1, 200), (0x9003, 2, 20, 208))
            .U32(200, 1).U32(204, 250)
            .Ascii(208, "2020:01:02 03:04:05\0")
            .Pattern(300, 64, 7);
        // Orientation becomes an entry of a type no reader knows the width of.
        if (extraType != 0) layout.U16(8 + 2 + 5 * 12 + 2, extraType);
        return new("tiff", big, layout.Bytes)
        {
            Exif = 170, Image = [(300, 64)], Date = 208, TextTag = 0x010F, Text = (130, 12)
        };
    }

    /// <summary>
    /// A DNG. IFD0 at 8 is the thumbnail (strip at 400 of 16 bytes) with Make at 160, SubIFDs,
    /// ExifIFD, DNGVersion and DNGPrivateData at 320 (24 bytes); the Exif IFD at 190 has
    /// DateTimeOriginal at 210; the SubIFD at 240 is the raw image (strip at 420 of 80 bytes).
    /// With <paramref name="sharedExif"/> the SubIFD points at the Exif IFD as well.
    /// </summary>
    internal static TiffBasedFixture Dng(bool big, bool sharedExif = false)
    {
        var layout = new Layout(500, big).Header()
            .Directory(8, 0,
                (0x00FE, 4, 1, 1), (0x0100, 3, 1, 4), (0x0101, 3, 1, 4), (0x010F, 2, 12, 160),
                (0x0111, 4, 1, 400), (0x0117, 4, 1, 16), (0x014A, 4, 1, 240), (0x8769, 4, 1, 190),
                (0xC612, 1, 4, 0), (0xC634, 1, 24, 320))
            .Raw(8 + 2 + 8 * 12 + 8, 1, 4, 0, 0)
            .Ascii(160, "TestCompany\0")
            .Directory(190, 0, (0x9003, 2, 20, 210))
            .Ascii(210, "2020:01:02 03:04:05\0")
            .Pattern(320, 24, 3)
            .Pattern(400, 16, 5)
            .Pattern(420, 80, 11);
        if (sharedExif)
            layout.Directory(240, 0, (0x00FE, 4, 1, 0), (0x0100, 3, 1, 8), (0x0101, 3, 1, 8), (0x0111, 4, 1, 420),
                (0x0117, 4, 1, 80), (0x8769, 4, 1, 190));
        else
            layout.Directory(240, 0, (0x00FE, 4, 1, 0), (0x0100, 3, 1, 8), (0x0101, 3, 1, 8), (0x0111, 4, 1, 420),
                (0x0117, 4, 1, 80));
        return new("dng", big, layout.Bytes)
        {
            Exif = 190, Image = [(400, 16), (420, 80)], Note = (320, 24), Date = 210, TextTag = 0x010F, Text = (160, 12)
        };
    }

    /// <summary>
    /// A NEF-like layout. IFD0 at 8 (Make at 120, Model at 138, a thumbnail strip at 600 of 32
    /// bytes, two SubIFDs listed at 150, ExifIFD, GPS); SubIFD 160 addresses a JPEG at 640 of 20
    /// bytes, SubIFD 230 the raw strip at 660 of 100 bytes; the Exif IFD at 300 has ExposureTime at
    /// 350, DateTimeOriginal at 358 and a Nikon type 3 MakerNote at 380 of 56 bytes, whose own
    /// offsets count from the header embedded in it; the GPS IFD at 520 has GPSVersionID.
    /// </summary>
    internal static TiffBasedFixture Nef(bool big)
    {
        var layout = new Layout(760, big).Header()
            .Directory(8, 0,
                (0x00FE, 4, 1, 1), (0x010F, 2, 18, 120), (0x0110, 2, 10, 138), (0x0111, 4, 1, 600),
                (0x0117, 4, 1, 32), (0x014A, 4, 2, 150), (0x8769, 4, 1, 300), (0x8825, 4, 1, 520))
            .Ascii(120, "NIKON CORPORATION\0")
            .Ascii(138, "NIKON D70\0")
            .U32(150, 160).U32(154, 230)
            .Directory(160, 0, (0x00FE, 4, 1, 1), (0x0103, 3, 1, 6), (0x0201, 4, 1, 640), (0x0202, 4, 1, 20))
            .Directory(230, 0, (0x00FE, 4, 1, 0), (0x0103, 3, 1, 1), (0x0111, 4, 1, 660), (0x0117, 4, 1, 100))
            .Directory(300, 0, (0x829A, 5, 1, 350), (0x9003, 2, 20, 358), (0x927C, 7, 56, 380))
            .U32(350, 1).U32(354, 125)
            .Ascii(358, "2020:01:02 03:04:05\0")
            .Directory(520, 0, (0x0000, 1, 4, 0))
            .Raw(520 + 2 + 8, 2, 3, 0, 0)
            .Pattern(600, 32, 5)
            .Jpeg(640, 20)
            .Pattern(660, 100, 13);
        // "Nikon\0" 02 10 00 00, a TIFF header, and a directory at 8 from that header: ISOSpeed
        // inline and Quality as text at 38 from the header.
        int note = 380, inner = note + 10;
        layout.Ascii(note, "Nikon\0").Raw(note + 6, 0x02, 0x10, 0, 0)
            .Raw(inner, big ? (byte)0x4D : (byte)0x49, big ? (byte)0x4D : (byte)0x49)
            .U16(inner + 2, 42).U32(inner + 4, 8)
            .U16(inner + 8, 2)
            .U16(inner + 10, 0x0002).U16(inner + 12, 3).U32(inner + 14, 2).U16(inner + 18, 0).U16(inner + 20, 200)
            .U16(inner + 22, 0x0004).U16(inner + 24, 2).U32(inner + 26, 8).U32(inner + 30, 38)
            .U32(inner + 34, 0)
            .Ascii(inner + 38, "FINE   \0");
        return new("nef", big, layout.Bytes)
        {
            Exif = 300, Gps = 520, Image = [(600, 32), (640, 20), (660, 100)], Note = (380, 56), Date = 358,
            TextTag = 0x0110, Text = (138, 10)
        };
    }

    /// <summary>
    /// A CR2-like layout. A sixteen-byte header ("CR" 2 0 and the address of the raw directory after
    /// the TIFF header); IFD0 at 16 (Make at 100, Model at 106, a JPEG in a strip at 500 of 24 bytes,
    /// Orientation, ExifIFD), chained to IFD1 at 270 (a JPEG at 530 of 20 bytes), chained to the raw
    /// directory at 330 (a strip at 560 of 120 bytes); the Exif IFD at 130 has DateTimeOriginal at
    /// 160 and a Canon MakerNote at 180 of 78 bytes, whose offsets count from the start of the file.
    /// </summary>
    internal static TiffBasedFixture Cr2(bool big)
    {
        const int note = 180;
        var layout = new Layout(680, big).Header(42, 16)
            .Ascii(8, "CR").Raw(10, 2, 0).U32(12, 330)
            .Directory(16, 270,
                (0x010F, 2, 6, 100), (0x0110, 2, 14, 106), (0x0111, 4, 1, 500), (0x0112, 3, 1, 1),
                (0x0117, 4, 1, 24), (0x8769, 4, 1, 130))
            .Ascii(100, "Canon\0")
            .Ascii(106, "Canon EOS 20D\0")
            .Directory(130, 0, (0x9003, 2, 20, 160), (0x927C, 7, 78, note))
            .Ascii(160, "2020:01:02 03:04:05\0")
            .Directory(note, 0, (0x0002, 3, 4, note + 62), (0x0006, 2, 8, note + 54),
                (0x0010, 4, 1, 0x80000175), (0x7777, 3, 1, 9))
            .Ascii(note + 54, "EOS 20D\0")
            .U16(note + 62, 2).U16(note + 64, 95).U16(note + 66, 907).U16(note + 68, 605)
            .Raw(note + 70, big ? (byte)0x4D : (byte)0x49, big ? (byte)0x4D : (byte)0x49)
            .U16(note + 72, 42).U32(note + 74, note)
            .Directory(270, 330, (0x0103, 3, 1, 6), (0x0201, 4, 1, 530), (0x0202, 4, 1, 20))
            .Directory(330, 0, (0x0103, 3, 1, 6), (0x0111, 4, 1, 560), (0x0117, 4, 1, 120))
            .Jpeg(500, 24)
            .Jpeg(530, 20)
            .Pattern(560, 120, 17);
        return new("cr2", big, layout.Bytes)
        {
            Primary = 16, Exif = 130, Image = [(500, 24), (530, 20), (560, 120)], Note = (note, 78), Date = 160,
            TextTag = 0x0110, Text = (106, 14)
        };
    }

    /// <summary>
    /// An RW2 of the later kind: the "IIU\0" header, and a primary directory at 8 with Make at 80,
    /// RawDataOffset (0x0118) = 200 and no length, and an Exif IFD at 100 with DateTimeOriginal at 120.
    /// </summary>
    internal static TiffBasedFixture Rw2()
    {
        var layout = new Layout(300, false).Header(0x55, 8)
            .Directory(8, 0, (0x010F, 2, 10, 80), (0x0118, 4, 1, 200), (0x8769, 4, 1, 100))
            .Ascii(80, "Panasonic\0")
            .Directory(100, 0, (0x9003, 2, 20, 120))
            .Ascii(120, "2020:01:02 03:04:05\0")
            .Pattern(200, 100, 19);
        return new("rw2", false, layout.Bytes)
        {
            Exif = 100, Image = [(200, 100)], Date = 120, TextTag = 0x010F, Text = (80, 10)
        };
    }

    /// <summary>
    /// A file of one directory at 8 and nothing else: every value is stored after the directory at
    /// an even offset, whatever its length, and the tags are given in ascending order.
    /// </summary>
    internal static byte[] Flat(bool big, params (int Tag, int Type, byte[] Value)[] fields)
    {
        int at = 8 + 2 + fields.Length * 12 + 4;
        var offsets = new int[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            offsets[i] = at;
            at += fields[i].Value.Length + (fields[i].Value.Length & 1);
        }
        var layout = new Layout(at, big).Header();
        layout.U16(8, fields.Length);
        for (int i = 0; i < fields.Length; i++)
        {
            int p = 10 + i * 12;
            int width = fields[i].Type switch { 3 => 2, 4 => 4, 5 => 8, _ => 1 };
            layout.U16(p, fields[i].Tag).U16(p + 2, fields[i].Type).U32(p + 4, fields[i].Value.Length / width)
                .U32(p + 8, offsets[i]).Raw(offsets[i], fields[i].Value);
        }
        return layout.Bytes;
    }

    /// <summary>The offset of the entry of a tag in the directory at <paramref name="directory"/>, or -1.</summary>
    internal static int EntryAt(byte[] file, bool big, long directory, int tag)
    {
        int count = U16(file, big, (int)directory);
        for (int i = 0; i < count; i++)
        {
            int p = (int)directory + 2 + 12 * i;
            if (U16(file, big, p) == tag) return p;
        }
        return -1;
    }

    internal static int U16(byte[] file, bool big, int at) => big
        ? BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at))
        : BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at));

    internal static long U32(byte[] file, bool big, int at) => big
        ? BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at))
        : BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));

    /// <summary>
    /// A SHORT with one value is stored in the value field itself; everything else is stored as the
    /// given 32-bit number, which is an inline LONG or the offset of an external value.
    /// </summary>
    private sealed class Layout(int length, bool big)
    {
        internal byte[] Bytes { get; } = new byte[length];

        internal Layout Header(int magic = 42, long first = 8)
        {
            Bytes[0] = Bytes[1] = big ? (byte)0x4D : (byte)0x49;
            U16(2, magic);
            U32(4, first);
            return this;
        }

        internal Layout Directory(int at, long next, params (int Tag, int Type, long Count, long Value)[] entries)
        {
            U16(at, entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                int p = at + 2 + i * 12;
                U16(p, entries[i].Tag);
                U16(p + 2, entries[i].Type);
                U32(p + 4, entries[i].Count);
                if (entries[i].Type == 3 && entries[i].Count == 1)
                    U16(p + 8, (int)entries[i].Value);
                else
                    U32(p + 8, entries[i].Value);
            }
            U32(at + 2 + entries.Length * 12, next);
            return this;
        }

        internal Layout Ascii(int at, string text)
        {
            Encoding.ASCII.GetBytes(text).CopyTo(Bytes, at);
            return this;
        }

        internal Layout Raw(int at, params byte[] bytes)
        {
            bytes.CopyTo(Bytes, at);
            return this;
        }

        /// <summary>Bytes that differ from one another and from zero, so a moved or cleared byte shows.</summary>
        internal Layout Pattern(int at, int count, int seed)
        {
            for (int i = 0; i < count; i++) Bytes[at + i] = (byte)(1 + (i * seed + seed) % 250);
            return this;
        }

        /// <summary>SOI, filler, EOI: what a reader accepts as a whole JPEG stream.</summary>
        internal Layout Jpeg(int at, int count)
        {
            Pattern(at, count, 29);
            Bytes[at] = 0xFF; Bytes[at + 1] = 0xD8;
            Bytes[at + count - 2] = 0xFF; Bytes[at + count - 1] = 0xD9;
            return this;
        }

        internal Layout U16(int at, int value)
        {
            if (big) BinaryPrimitives.WriteUInt16BigEndian(Bytes.AsSpan(at), (ushort)value);
            else BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(at), (ushort)value);
            return this;
        }

        internal Layout U32(int at, long value)
        {
            if (big) BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(at), (uint)value);
            else BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(at), (uint)value);
            return this;
        }
    }
}
