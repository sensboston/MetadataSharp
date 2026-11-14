using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats;
using Xunit;

namespace MetadataSharp.Tests.Containers;

/// <summary>
/// Synthetic files, one per place a preview can be found in, laid out by hand so that every
/// offset an assertion names can be read off the builder.
/// </summary>
public sealed class PreviewTests
{
    private const string Jpeg = "image/jpeg";

    /// <summary>Where <see cref="NoteTiff"/> puts the MakerNote.</summary>
    private const int NoteAt = 72;

    // ---- builders ----

    /// <summary>SOI, one frame header that states the dimensions, optional filler, EOI: 17 bytes without filler.</summary>
    private static byte[] Picture(int width, int height, int filler = 0, byte frame = 0xC0)
    {
        var bytes = new List<byte>
        {
            0xFF, 0xD8, 0xFF, frame, 0x00, 0x0B, 0x08,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x01, 0x01, 0x11, 0x00,
        };
        for (int i = 0; i < filler; i++) bytes.Add(0x55);
        bytes.Add(0xFF);
        bytes.Add(0xD9);
        return bytes.ToArray();
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private static void Put16(List<byte> to, bool little, long value)
    {
        if (little) { to.Add((byte)value); to.Add((byte)(value >> 8)); }
        else { to.Add((byte)(value >> 8)); to.Add((byte)value); }
    }

    private static void Put32(List<byte> to, bool little, long value)
    {
        if (little) for (int i = 0; i < 4; i++) to.Add((byte)(value >> (8 * i)));
        else for (int i = 3; i >= 0; i--) to.Add((byte)(value >> (8 * i)));
    }

    private static byte[] Shorts(bool little, params long[] values)
    {
        var bytes = new List<byte>();
        foreach (long value in values) Put16(bytes, little, value);
        return bytes.ToArray();
    }

    private static byte[] Longs(bool little, params long[] values)
    {
        var bytes = new List<byte>();
        foreach (long value in values) Put32(bytes, little, value);
        return bytes.ToArray();
    }

    /// <summary>A directory field; <c>Value</c> is the value itself for one Short or Long and an offset otherwise.</summary>
    private readonly record struct Field(int Tag, int Type, long Count, long Value);

    private static Field Short(int tag, long value) => new(tag, 3, 1, value);

    private static Field Long(int tag, long value) => new(tag, 4, 1, value);

    /// <summary>A directory of 2 + 12 * fields + 4 bytes.</summary>
    private static byte[] Ifd(bool little, long next, params Field[] fields)
    {
        var bytes = new List<byte>();
        Put16(bytes, little, fields.Length);
        foreach (Field field in fields)
        {
            Put16(bytes, little, field.Tag);
            Put16(bytes, little, field.Type);
            Put32(bytes, little, field.Count);
            if (field.Type == 3 && field.Count == 1)
            {
                Put16(bytes, little, field.Value);
                Put16(bytes, little, 0);
            }
            else
            {
                Put32(bytes, little, field.Value);
            }
        }
        Put32(bytes, little, next);
        return bytes.ToArray();
    }

    private static byte[] Header(bool little) => little
        ? new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00 }
        : new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08 };

    /// <summary>Header, IFD0 at 8 (one field), IFD1 at 26 (three fields, to 68), the JPEG at 68.</summary>
    private static byte[] ThumbnailTiff(bool little, byte[] picture, long offset = 68, long length = -1) => Join(
        Header(little),
        Ifd(little, 26, Short(0x0112, 1)),
        Ifd(little, 0, Short(0x0103, 6), Long(0x0201, offset), Long(0x0202, length < 0 ? picture.Length : length)),
        picture);

    /// <summary>
    /// Header, IFD0 at 8 (Make, Exif pointer; to 38), the make at 38 (16 bytes), the Exif directory
    /// at 54 (the MakerNote field; to 72), the MakerNote at 72.
    /// </summary>
    private static byte[] NoteTiff(bool little, string make, byte[] note)
    {
        var name = new byte[16];
        Encoding.ASCII.GetBytes(make, name);
        return Join(
            Header(little),
            Ifd(little, 0, new Field(0x010F, 2, 16, 38), Long(0x8769, 54)),
            name,
            Ifd(little, 0, new Field(0x927C, 7, note.Length, NoteAt)),
            note);
    }

    private static byte[] Wrap(string container, byte[] tiff, out int origin)
    {
        switch (container)
        {
            case "jpeg":
            case "exv":
            {
                byte[] head = container == "jpeg" ? new byte[] { 0xFF, 0xD8 } : Join(new byte[] { 0xFF, 0x01 }, "Exiv2"u8.ToArray());
                int size = 2 + 6 + tiff.Length;
                origin = head.Length + 4 + 6;
                return Join(head, new byte[] { 0xFF, 0xE1, (byte)(size >> 8), (byte)size }, "Exif\0\0"u8.ToArray(), tiff, new byte[] { 0xFF, 0xD9 });
            }
            case "png":
                origin = 16;
                return Join(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, Be32(tiff.Length), "eXIf"u8.ToArray(), tiff, new byte[4],
                    Be32(0), "IEND"u8.ToArray(), new byte[4]);
            case "webp":
                origin = 20;
                return Join("RIFF"u8.ToArray(), Longs(true, 4 + 8 + tiff.Length + (tiff.Length & 1)), "WEBP"u8.ToArray(),
                    "EXIF"u8.ToArray(), Longs(true, tiff.Length), tiff, new byte[tiff.Length & 1]);
            default:
                origin = 0;
                return tiff;
        }
    }

    private static byte[] Fixture(string directory, string name)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MetadataSharp.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllBytes(Path.Combine(root!.FullName, "tests", "fixtures", directory, name));
    }

    private static byte[] Be16(long value) => new[] { (byte)(value >> 8), (byte)value };

    private static byte[] Be32(long value) => new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

    private static byte[] Box(string type, params byte[][] payload)
    {
        byte[] body = Join(payload);
        return Join(Be32(8 + body.Length), Encoding.ASCII.GetBytes(type), body);
    }

    private static MetadataErrorCode Refusal(byte[] file, PreviewImage preview) =>
        Assert.Throws<MetadataException>(() => Previews.Extract(file, preview)).Code;

    // ---- the EXIF thumbnail ----

    [Theory]
    [InlineData("jpeg", true)]
    [InlineData("jpeg", false)]
    [InlineData("exv", true)]
    [InlineData("tiff", true)]
    [InlineData("tiff", false)]
    [InlineData("png", false)]
    [InlineData("webp", true)]
    public void TheExifThumbnailIsListedAndExtracted(string container, bool little)
    {
        byte[] picture = Picture(160, 120);
        byte[] file = Wrap(container, ThumbnailTiff(little, picture), out int origin);

        PreviewImage preview = Assert.Single(Previews.List(file));

        Assert.Equal(new PreviewImage(Jpeg, 160, 120, origin + 68, picture.Length, "Exif.Thumbnail.JPEGInterchangeFormat"), preview);
        Assert.Equal(picture, Previews.Extract(file, preview));
    }

    /// <summary>
    /// IFD1 at 26 with nine fields (to 140), BitsPerSample at 140, the strip offsets at 146, the
    /// strip lengths at 154, then two strips of six bytes from 162.
    /// </summary>
    private static byte[] StripTiff(bool little, long secondStrip) => Join(
        Header(little),
        Ifd(little, 26, Short(0x0112, 1)),
        Ifd(little, 0, Short(0x0100, 2), Short(0x0101, 2), new Field(0x0102, 3, 3, 140), Short(0x0103, 1), Short(0x0106, 2),
            new Field(0x0111, 4, 2, 146), Short(0x0115, 3), Short(0x0116, 1), new Field(0x0117, 4, 2, 154)),
        Shorts(little, 8, 8, 8),
        Longs(little, 162, secondStrip),
        Longs(little, 6, 6),
        new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUncompressedThumbnailIsListedAsItsSamples(bool little)
    {
        byte[] file = Wrap("jpeg", StripTiff(little, 168), out int origin);

        PreviewImage preview = Assert.Single(Previews.List(file));

        Assert.Equal(new PreviewImage("image/x-raw-rgb", 2, 2, origin + 162, 12, "Exif.Thumbnail.StripOffsets"), preview);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, Previews.Extract(file, preview));
    }

    [Theory]
    [InlineData(165L)] // the second strip starts inside the first
    [InlineData(169L)] // a gap between the strips
    [InlineData(162L)] // both strips are the same bytes
    public void StripsThatOverlapOrLeaveAGapAreNotListed(long secondStrip)
    {
        Assert.Empty(Previews.List(Wrap("jpeg", StripTiff(true, secondStrip), out _)));
    }

    // ---- MakerNotes ----

    [Theory]
    [InlineData("nikon", true)]
    [InlineData("nikon", false)]
    [InlineData("pentax", true)]
    [InlineData("pentax", false)]
    [InlineData("pentax-dng", true)]
    [InlineData("pentax-dng", false)]
    [InlineData("olympus", true)]
    [InlineData("olympus", false)]
    [InlineData("olympus-settings", true)]
    [InlineData("olympus-settings", false)]
    [InlineData("sony", true)]
    [InlineData("sony", false)]
    [InlineData("minolta", true)]
    [InlineData("minolta", false)]
    [InlineData("canon", true)]
    [InlineData("canon", false)]
    public void APreviewBehindAMakerNotePointerIsListedAndExtracted(string vendor, bool little)
    {
        byte[] picture = Picture(640, 480);
        long size = picture.Length;
        byte[] mark = little ? "II"u8.ToArray() : "MM"u8.ToArray();
        string make, source;
        int where;
        byte[] note;
        switch (vendor)
        {
            case "nikon":
                // A TIFF of its own ten bytes in: its directory at 8, the preview directory at 26 (to 56), the JPEG at 56.
                make = "NIKON";
                source = "Exif.NikonPreview.JPEGInterchangeFormat";
                where = NoteAt + 10 + 56;
                note = Join("Nikon\0"u8.ToArray(), new byte[] { 0x02, 0x10, 0x00, 0x00 }, Header(little),
                    Ifd(little, 0, Long(0x0011, 26)), Ifd(little, 0, Long(0x0201, 56), Long(0x0202, size)), picture);
                break;
            case "pentax":
                // The two bytes after the signature say the opposite of the truth: the TIFF's order holds.
                make = "PENTAX";
                source = "Exif.Pentax.PreviewOffset";
                where = NoteAt + 36;
                note = Join("AOC\0"u8.ToArray(), little ? "MM"u8.ToArray() : "II"u8.ToArray(),
                    Ifd(little, 0, Long(0x0003, size), Long(0x0004, where)), picture);
                break;
            case "pentax-dng":
                // Offsets count from the note.
                make = "PENTAX";
                source = "Exif.PentaxDng.PreviewOffset";
                where = NoteAt + 40;
                note = Join("PENTAX \0"u8.ToArray(), mark, Ifd(little, 0, Long(0x0003, size), Long(0x0004, 40)), picture);
                break;
            case "olympus":
                // The thumbnail is the value of a field.
                make = "OLYMPUS";
                source = "Exif.Olympus.ThumbnailImage";
                where = NoteAt + 26;
                note = Join("OLYMP\0"u8.ToArray(), new byte[] { 1, 0 }, Ifd(little, 0, new Field(0x0100, 7, size, where)), picture);
                break;
            case "olympus-settings":
                // The directory at 12 (to 30) points at the camera settings at 30 (to 60); offsets count from the note.
                make = "OLYMPUS";
                source = "Exif.OlympusCs.PreviewImageStart";
                where = NoteAt + 60;
                note = Join("OLYMPUS\0"u8.ToArray(), mark, new byte[] { 3, 0 }, Ifd(little, 0, new Field(0x2020, 13, 1, 30)),
                    Ifd(little, 0, Long(0x0101, 60), Long(0x0102, size)), picture);
                break;
            case "sony":
                // The value of the field is a header of 32 bytes and then the JPEG.
                make = "SONY";
                source = "Exif.Sony1.PreviewImage";
                where = NoteAt + 18 + 32;
                note = Join(Ifd(little, 0, new Field(0x2001, 7, 32 + size, NoteAt + 18)), new byte[32], picture);
                break;
            case "minolta":
                make = "MINOLTA";
                source = "Exif.Minolta.ThumbnailOffset";
                where = NoteAt + 30;
                note = Join(Ifd(little, 0, Long(0x0088, where), Long(0x0089, size)), picture);
                break;
            default:
                // Six numbers at 18 (to 42): the third is the length, the sixth the start.
                make = "Canon";
                source = "Exif.Canon.PreviewImageInfo";
                where = NoteAt + 42;
                note = Join(Ifd(little, 0, new Field(0x00B6, 4, 6, NoteAt + 18)), Longs(little, 24, 2, size, 640, 480, where), picture);
                break;
        }
        byte[] file = NoteTiff(little, make, note);

        PreviewImage preview = Assert.Single(Previews.List(file));

        Assert.Equal(new PreviewImage(Jpeg, 640, 480, where, picture.Length, source), preview);
        Assert.Equal(picture, Previews.Extract(file, preview));
    }

    [Fact]
    public void AMakerNotePointerThatLeavesTheFileIsNotListed()
    {
        byte[] picture = Picture(640, 480);
        byte[] note = Join(Ifd(true, 0, Long(0x0088, 5000), Long(0x0089, picture.Length)), picture);

        Assert.Empty(Previews.List(NoteTiff(true, "MINOLTA", note)));
    }

    // ---- TIFF-based raw files ----

    [Theory]
    [InlineData(1, 7, 0xC0, true)]  // a reduced image in new-style JPEG compression
    [InlineData(0, 6, 0xC0, true)]  // old-style JPEG compression
    [InlineData(0, 7, 0xC0, false)] // the main image is not a preview
    [InlineData(1, 7, 0xC3, false)] // a lossless frame is sensor data
    [InlineData(1, 8, 0xC0, false)] // not JPEG compression at all
    public void AJpegCompressedSubImageIsListedOnlyWhenItIsAPreview(int subfileType, int compression, int frame, bool listed)
    {
        // IFD0 at 8 (one field, to 26) points at a directory at 26 (four fields, to 80); the strip at 80.
        byte[] picture = Picture(1616, 1080, 5, (byte)frame);
        byte[] file = Join(Header(true),
            Ifd(true, 0, Long(0x014A, 26)),
            Ifd(true, 0, Long(0x00FE, subfileType), Short(0x0103, compression), Long(0x0111, 80), Long(0x0117, picture.Length)),
            picture);

        IReadOnlyList<PreviewImage> found = Previews.List(file);

        if (!listed)
        {
            Assert.Empty(found);
            return;
        }
        PreviewImage preview = Assert.Single(found);
        Assert.Equal(new PreviewImage(Jpeg, 1616, 1080, 80, picture.Length, "Exif.SubImage1.StripOffsets"), preview);
        Assert.Equal(picture, Previews.Extract(file, preview));
    }

    [Fact]
    public void PreviewsAreSortedBySizeSmallestFirst()
    {
        // IFD0 at 8 (three fields, to 50) holds a full-size JPEG strip at 80; IFD1 at 50 (two fields, to 80) the thumbnail at 107.
        byte[] large = Picture(1936, 1288, 10), small = Picture(160, 120);
        byte[] file = Join(Header(false),
            Ifd(false, 50, Short(0x0103, 6), Long(0x0111, 80), Long(0x0117, large.Length)),
            Ifd(false, 0, Long(0x0201, 107), Long(0x0202, small.Length)),
            large, small);

        IReadOnlyList<PreviewImage> found = Previews.List(file);

        Assert.Equal(2, found.Count);
        Assert.Equal(new PreviewImage(Jpeg, 160, 120, 107, small.Length, "Exif.Thumbnail.JPEGInterchangeFormat"), found[0]);
        Assert.Equal(new PreviewImage(Jpeg, 1936, 1288, 80, large.Length, "Exif.Image.StripOffsets"), found[1]);
        Assert.Equal(large, Previews.Extract(file, found[1]));
    }

    // ---- BMFF ----

    private static byte[] Heif(long jpegAt, long jpegLength, long codedAt, long codedLength)
    {
        byte[] full = new byte[4];
        byte[] information = Box("iinf", full, Be16(3),
            Box("infe", new byte[] { 2, 0, 0, 0 }, Be16(1), Be16(0), "hvc1"u8.ToArray(), new byte[1]),
            Box("infe", new byte[] { 2, 0, 0, 0 }, Be16(2), Be16(0), "jpeg"u8.ToArray(), new byte[1]),
            Box("infe", new byte[] { 2, 0, 0, 0 }, Be16(3), Be16(0), "hvc1"u8.ToArray(), new byte[1]));
        // Items 2 and 3 are thumbnails of item 1.
        byte[] references = Box("iref", full, Box("thmb", Be16(2), Be16(1), Be16(1)), Box("thmb", Be16(3), Be16(1), Be16(1)));
        // Offsets and lengths of four bytes, no base offset; one extent for each of the two items.
        byte[] locations = Box("iloc", full, new byte[] { 0x44, 0x00 }, Be16(2),
            Be16(2), Be16(0), Be16(1), Be32(jpegAt), Be32(jpegLength),
            Be16(3), Be16(0), Be16(1), Be32(codedAt), Be32(codedLength));
        // One property, 160 by 120, associated with item 3.
        byte[] properties = Box("iprp", Box("ipco", Box("ispe", full, Be32(160), Be32(120))),
            Box("ipma", full, Be32(1), Be16(3), new byte[] { 1, 1 }));
        return Join(Box("ftyp", "heic"u8.ToArray(), new byte[4], "mif1"u8.ToArray()), Box("meta", full, information, references, locations, properties));
    }

    [Fact]
    public void ThumbnailItemsOfABmffFileAreListedAndEncodedItemsAreExtractable()
    {
        byte[] picture = Picture(4, 3), coded = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        int at = Heif(0, 0, 0, 0).Length;
        byte[] file = Join(Heif(at, picture.Length, at + picture.Length, coded.Length), picture, coded);

        IReadOnlyList<PreviewImage> found = Previews.List(file);

        Assert.Equal(2, found.Count);
        Assert.Equal(new PreviewImage(Jpeg, 4, 3, at, picture.Length, "Bmff.thmb"), found[0]);
        Assert.Equal(new PreviewImage("video/H265", 160, 120, at + picture.Length, coded.Length, "Bmff.thmb"), found[1]);
        Assert.Equal(picture, Previews.Extract(file, found[0]));
        Assert.Equal(coded, Previews.Extract(file, found[1]));
    }

    [Fact]
    public void ABmffItemThatLeavesTheFileIsNotListed()
    {
        byte[] picture = Picture(4, 3);
        int at = Heif(0, 0, 0, 0).Length;

        Assert.Empty(Previews.List(Join(Heif(at, picture.Length + 1, at + 4000, 9), picture)));
    }

    // ---- PSD ----

    [Fact]
    public void ThePhotoshopThumbnailResourceIsListedAndExtracted()
    {
        byte[] picture = Picture(150, 91);
        // Signature, ID, an empty name, the size; then the header of 28 bytes: format 1, width, height,
        // row bytes, total size, compressed size, bits per pixel, planes. The payload is padded to an even length.
        byte[] resource = Join("8BIM"u8.ToArray(), Be16(0x040C), new byte[2], Be32(28 + picture.Length),
            Be32(1), Be32(150), Be32(91), Be32(452), Be32(41132), Be32(picture.Length), Be16(24), Be16(1), picture, new byte[1]);
        // The header of 26 bytes, an empty color mode section, the resources, an empty layer section.
        byte[] file = Join("8BPS"u8.ToArray(), Be16(1), new byte[6], Be16(3), Be32(1), Be32(1), Be16(8), Be16(3),
            Be32(0), Be32(resource.Length), resource, Be32(0), Be16(0), new byte[3]);

        PreviewImage preview = Assert.Single(Previews.List(file));

        Assert.Equal(new PreviewImage(Jpeg, 150, 91, 26 + 4 + 4 + 12 + 28, picture.Length, "Photoshop.Thumbnail"), preview);
        Assert.Equal(picture, Previews.Extract(file, preview));
    }

    // ---- native preview loaders ----

    [Fact]
    public void MrwTtwUsesItsBoundedTiffPreviewLoader()
    {
        byte[] picture = Picture(20, 10);
        byte[] tiff = ThumbnailTiff(true, picture);
        byte[] prd = Join(new byte[] { 0 }, "PRD"u8.ToArray(), Be32(24), "27730001"u8.ToArray(), new byte[16]);
        byte[] ttw = Join(new byte[] { 0 }, "TTW"u8.ToArray(), Be32(tiff.Length), tiff);
        byte[] blocks = Join(prd, ttw);
        byte[] file = Join(new byte[] { 0 }, "MRM"u8.ToArray(), Be32(8 + blocks.Length), blocks);
        PreviewImage preview = Assert.Single(Previews.List(file));
        Assert.Equal(new PreviewImage(Jpeg, 20, 10, 8 + prd.Length + 8 + 68, picture.Length,
            "Exif.Thumbnail.JPEGInterchangeFormat"), preview);
        Assert.Equal(picture, Previews.Extract(file, preview));
    }

    [Fact]
    public void RafListsTheEmbeddedJpegAndItsExifThumbnailAtFileOffsets()
    {
        byte[] picture = Picture(64, 48);
        byte[] thumb = Picture(16, 12);
        byte[] tiff = ThumbnailTiff(true, thumb);
        int appLength = 2 + 6 + tiff.Length;
        byte[] jpeg = Join(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, (byte)(appLength >> 8), (byte)appLength },
            "Exif\0\0"u8.ToArray(), tiff, picture[2..]);
        byte[] header = new byte[108];
        "FUJIFILM"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(84), 108);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(88), (uint)jpeg.Length);
        byte[] file = Join(header, jpeg);

        IReadOnlyList<PreviewImage> found = Previews.List(file);
        Assert.Equal(2, found.Count);
        Assert.Equal(new PreviewImage(Jpeg, 16, 12, 108 + 2 + 4 + 6 + 68, thumb.Length,
            "Exif.Thumbnail.JPEGInterchangeFormat"), found[0]);
        Assert.Equal(new PreviewImage(Jpeg, 64, 48, 108, jpeg.Length,
            "Exif.Image2.JPEGInterchangeFormat"), found[1]);
        Assert.Equal(thumb, Previews.Extract(file, found[0]));
        Assert.Equal(jpeg, Previews.Extract(file, found[1]));
    }

    [Fact]
    public void EpsAi7HexThumbnailIsReturnedAsPortableAnymap()
    {
        byte[] file = Encoding.ASCII.GetBytes("%!PS-Adobe-3.0 EPSF-3.0\n%AI7_Thumbnail: 1 1 8\n" +
            "%%BeginData: 6 Hex Bytes\n010203\n%%EndData\n%%EndPreview\n%%EndComments\n%%EOF\n");
        PreviewImage preview = Assert.Single(Previews.List(file));
        Assert.Equal("image/x-portable-anymap", preview.MimeType);
        Assert.Equal(1, preview.Width);
        Assert.Equal(1, preview.Height);
        Assert.Equal("EPS.AI7.Hex", preview.Source);
        Assert.Equal(new byte[] { 0x50, 0x36, 0x0A, 0x31, 0x20, 0x31, 0x0A, 0x32, 0x35, 0x35, 0x0A, 1, 2, 3 },
            Previews.Extract(file, preview));
    }

    [Fact]
    public void EpsPhotoshopIrbHexThumbnailReturnsTheEmbeddedJpeg()
    {
        byte[] jpeg = Picture(9, 7);
        byte[] payload = Join(new byte[28], jpeg);
        byte[] irb = Join("8BIM"u8.ToArray(), Be16(0x0409), new byte[] { 0, 0 }, Be32(payload.Length), payload,
            new byte[payload.Length & 1]);
        string hex = Convert.ToHexString(irb);
        byte[] file = Encoding.ASCII.GetBytes("%!PS-Adobe-3.0 EPSF-3.0\n%%EndComments\n%BeginPhotoshop: 1\n" +
            hex + "\n%EndPhotoshop\n%%EOF\n");
        PreviewImage preview = Assert.Single(Previews.List(file));
        Assert.Equal("EPS.Photoshop.Hex", preview.Source);
        byte[] marker = Encoding.ASCII.GetBytes("%BeginPhotoshop: 1\n");
        Assert.Equal(file.AsSpan().IndexOf(marker) + marker.Length, preview.Offset);
        Assert.Equal(jpeg, Previews.Extract(file, preview));
    }

    [Fact]
    public void XmpThumbnailBase64IsExtractedWithoutChangingTheSidecar()
    {
        byte[] jpeg = Picture(13, 9);
        string encoded = Convert.ToBase64String(jpeg);
        string xml = "<?xpacket begin='﻿' id='W5M0MpCehiHzreSzNTczkc9d'?>" +
            "<x:xmpmeta xmlns:x='adobe:ns:meta/'>" +
            "<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#' " +
            "xmlns:xmp='http://ns.adobe.com/xap/1.0/' xmlns:xmpGImg='http://ns.adobe.com/xap/1.0/g/img/'>" +
            "<rdf:Description rdf:about=''><xmp:Thumbnails><rdf:Seq><rdf:li rdf:parseType='Resource'>" +
            "<xmpGImg:format>JPEG</xmpGImg:format><xmpGImg:width>13</xmpGImg:width>" +
            "<xmpGImg:height>9</xmpGImg:height><xmpGImg:image>" + encoded +
            "</xmpGImg:image></rdf:li></rdf:Seq></xmp:Thumbnails></rdf:Description></rdf:RDF>" +
            "</x:xmpmeta><?xpacket end='w'?>";
        byte[] file = Encoding.UTF8.GetBytes(xml);
        PreviewImage preview = Assert.Single(Previews.List(file));
        Assert.Equal("Xmp.Thumbnail[1]", preview.Source);
        int encodedAt = xml.IndexOf(encoded, StringComparison.Ordinal);
        Assert.Equal((long)Encoding.UTF8.GetByteCount(xml[..encodedAt]), preview.Offset);
        Assert.Equal(jpeg.Length, preview.Length);
        Assert.Equal(jpeg, Previews.Extract(file, preview));
    }

    [Fact]
    public void CrwPreviewAndThumbnailUseTheCiffRanges()
    {
        byte[] file = Fixture("b11", "owned-crw-preview-thumb.crw");
        MetadataSharp.Formats.Crw.CrwMetadata metadata = MetadataSharp.Formats.Crw.CrwMetadata.Read(file);
        IReadOnlyList<PreviewImage> found = Previews.List(file);
        foreach ((long? offset, long length) in new[] { (metadata.PreviewOffset, metadata.PreviewLength), (metadata.ThumbnailOffset, metadata.ThumbnailLength) })
        {
            if (offset is not long at || length <= 0) continue;
            PreviewImage listed = Assert.Single(found, preview => preview.Offset == at && preview.Length == length);
            Assert.Equal(file.AsSpan((int)at, (int)length).ToArray(), Previews.Extract(file, listed));
        }
        Assert.NotEmpty(found);
    }

    [Fact]
    public void Jp2ExifUuidPreviewOffsetsIncludeTheOuterBoxes()
    {
        byte[] source = Fixture("b8", "owned-rgb.jp2");
        byte[] picture = Picture(16, 12);
        byte[] tiff = ThumbnailTiff(true, picture);
        int insertAt = 0;
        while (insertAt < source.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(insertAt, 4)));
            if (source.AsSpan(insertAt + 4, 4).SequenceEqual("jp2c"u8)) break;
            insertAt = checked(insertAt + length);
        }
        byte[] uuid = Join(Be32(24 + tiff.Length), "uuid"u8.ToArray(), "JpgTiffExif->JP2"u8.ToArray(), tiff);
        byte[] file = Join(source[..insertAt], uuid, source[insertAt..]);
        PreviewImage preview = Assert.Single(Previews.List(file));
        Assert.Equal("Exif.Thumbnail.JPEGInterchangeFormat", preview.Source);
        Assert.Equal((long)(insertAt + 24 + 68), preview.Offset);
        Assert.Equal(picture, Previews.Extract(file, preview));
    }

    // ---- refusals ----

    [Theory]
    [InlineData(4000L, -1L)]       // the offset is outside the file
    [InlineData(4294967280L, -1L)] // and far outside it
    [InlineData(68L, 4000L)]       // the length runs past the end
    [InlineData(0L, -1L)]          // the TIFF header is not a JPEG stream
    [InlineData(69L, -1L)]         // nor is the second byte of one
    public void AThumbnailPointerThatDoesNotHoldIsNotListed(long offset, long length)
    {
        byte[] file = Wrap("jpeg", ThumbnailTiff(true, Picture(160, 120), offset, length), out _);

        Assert.Empty(Previews.List(file));
    }

    [Fact]
    public void ATruncatedFileLosesTheThumbnailThatNoLongerFits()
    {
        byte[] tiff = ThumbnailTiff(true, Picture(160, 120));

        Assert.Single(Previews.List(tiff));
        Assert.Empty(Previews.List(tiff.AsSpan(0, tiff.Length - 1)));
        Assert.Empty(Previews.List(tiff.AsSpan(0, 40)));
        Assert.Empty(Previews.List(tiff.AsSpan(0, 9)));
    }

    [Fact]
    public void APreviewThatOverlapsTheDirectoryThatDeclaresItIsNotListed()
    {
        // IFD1 at 26: the value of its first field, at 36, reads as a complete JPEG stream of four bytes.
        static byte[] Build(long offset) => Join(Header(true), Ifd(true, 26, Short(0x0112, 1)),
            Ifd(true, 0, new Field(0xC000, 7, 4, 0xD9FFD8FFL), Long(0x0201, offset), Long(0x0202, 4)),
            new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        Assert.Empty(Previews.List(Build(36)));
        PreviewImage outside = Assert.Single(Previews.List(Build(68)));
        Assert.Equal(new PreviewImage(Jpeg, null, null, 68, 4, "Exif.Thumbnail.JPEGInterchangeFormat"), outside);
    }

    [Fact]
    public void ADirectoryChainThatLoopsEnds()
    {
        byte[] picture = Picture(160, 120);
        byte[] file = Join(Header(true), Ifd(true, 26, Short(0x0112, 1)),
            Ifd(true, 26, Short(0x0103, 6), Long(0x0201, 68), Long(0x0202, picture.Length)), picture);

        Assert.Single(Previews.List(file));
    }

    [Fact]
    public void ExtractRefusesARecordThatDoesNotFitTheFile()
    {
        byte[] file = Wrap("jpeg", ThumbnailTiff(true, Picture(160, 120)), out _);
        PreviewImage good = Assert.Single(Previews.List(file));

        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(file, good with { Offset = file.Length }));
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(file, good with { Offset = -1 }));
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(file, good with { Offset = long.MaxValue }));
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(file, good with { Length = good.Length + 100 }));
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(file, good with { Length = int.MaxValue }));
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(file, good with { Length = 0 }));
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(file, good with { Length = -5 }));
        // Inside the file, and not a JPEG stream: no SOI at the start, no EOI at the end.
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(file, good with { Offset = good.Offset + 1 }));
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(file, good with { Length = good.Length - 1 }));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(file, good with { MimeType = "image/png" }));
        Assert.Equal(file.AsSpan((int)good.Offset, good.Length).ToArray(),
            Previews.Extract(file, good with { MimeType = "image/tiff" }));
        Assert.Equal(MetadataErrorCode.InvalidTiff, Refusal(file, good with { MimeType = "image/x-raw-rgb" }));
        Assert.Throws<ArgumentNullException>(() => Previews.Extract(file, null!));
    }

    [Fact]
    public void ZeroPaddingAfterTheEndOfTheJpegIsKeptAndAnythingElseIsRefused()
    {
        byte[] picture = Picture(160, 120);
        byte[] padded = Join(picture, new byte[3]);
        byte[] file = Wrap("jpeg", ThumbnailTiff(true, padded), out _);
        PreviewImage preview = Assert.Single(Previews.List(file));
        Assert.Equal(padded.Length, preview.Length);
        Assert.Equal(padded, Previews.Extract(file, preview));

        byte[] trailing = Join(picture, new byte[] { 0xFF });
        byte[] other = Wrap("jpeg", ThumbnailTiff(true, trailing), out _);
        PreviewImage listed = Assert.Single(Previews.List(other));
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Refusal(other, listed));
    }

    [Fact]
    public void TheLimitsBoundTheInputAndTheDirectoriesRead()
    {
        byte[] file = Wrap("jpeg", ThumbnailTiff(true, Picture(160, 120)), out _);
        PreviewImage good = Assert.Single(Previews.List(file));
        var tight = new MetadataLimits { MaxInputBytes = 16 };

        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => Previews.List(file, tight)).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => Previews.Extract(file, good, tight)).Code);
        // The thumbnail is in the second directory.
        Assert.Empty(Previews.List(file, new MetadataLimits { MaxDirectories = 1 }));
    }

    [Fact]
    public void AFileWithoutAPreviewHasAnEmptyList()
    {
        Assert.Empty(Previews.List(ReadOnlySpan<byte>.Empty));
        Assert.Empty(Previews.List("not an image at all"u8));
        Assert.Empty(Previews.List(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));
        Assert.Empty(Previews.List(Header(true)));
        Assert.Empty(Previews.List(Wrap("jpeg", Join(Header(false), Ifd(false, 0, Short(0x0112, 1))), out _)));
        Assert.Empty(Previews.List("8BPS"u8));
        Assert.Empty(Previews.List(Box("ftyp", "heic"u8.ToArray(), new byte[4])));
    }
}
