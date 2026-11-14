using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Cr3;
using MetadataSharp.Formats.Exv;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Jxl;
using MetadataSharp.Formats.Psd;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Tests.Bmff;
using MetadataSharp.Tests.Cr3;
using MetadataSharp.Tests.Iptc;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Jxl;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.WebP;
using Xunit;

namespace MetadataSharp.Tests.Containers;

/// <summary>
/// The one entry point for reading: a file of each format the signature names, with good and with
/// refused parts. Every file is synthetic and built here from the fixtures of the format, or is an
/// authored fixture of the repository.
/// </summary>
public sealed class ImageMetadataTests
{
    // ---- builders ----

    /// <summary>A block neither walk of the EXIF reader takes: it has no byte order mark.</summary>
    private static readonly byte[] BadTiff = [(byte)'X', (byte)'X', 42, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>A document that opens as XML does and is not well formed, so it is no packet.</summary>
    private static readonly byte[] NoPacket = "<?xml version='1.0'?><malformed"u8.ToArray();

    private static readonly byte[] XmpHeader = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();

    private static byte[] GoodPacket => Cr3Fixture.Packet("snapshot");

    /// <summary>The IPTC stream the library starts from, the character set and the record version, and a caption (2:120).</summary>
    private static byte[] IptcStream => [.. IptcCodecTests.Raw(IptcPacket.CreateEmpty()), .. IptcCodecTests.Dataset(2, 120, "hello"u8.ToArray())];

    private static byte[] Fixture(string name)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MetadataSharp.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "tests", "fixtures", "b8", name));
    }

    /// <summary>
    /// A little-endian TIFF: the header, the primary directory at 8, and every value stored after
    /// it at an even offset. Each value is longer than four bytes and the tags ascend.
    /// </summary>
    private static byte[] Tiff(params (int Tag, int Type, byte[] Value)[] fields)
    {
        int at = 8 + 2 + fields.Length * 12 + 4;
        var offsets = new int[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            offsets[i] = at;
            at += fields[i].Value.Length + (fields[i].Value.Length & 1);
        }
        byte[] file = new byte[at];
        "II*\0"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(8), (ushort)fields.Length);
        for (int i = 0; i < fields.Length; i++)
        {
            int p = 10 + i * 12;
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(p), (ushort)fields[i].Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(p + 2), (ushort)fields[i].Type);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(p + 4), (uint)fields[i].Value.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(p + 8), (uint)offsets[i]);
            fields[i].Value.CopyTo(file, offsets[i]);
        }
        return file;
    }

    /// <summary>A box of a JPEG 2000 file: the length, the type, the payload.</summary>
    private static byte[] Box(string type, byte[] payload)
    {
        byte[] box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        System.Text.Encoding.ASCII.GetBytes(type, box.AsSpan(4));
        payload.CopyTo(box, 8);
        return box;
    }

    /// <summary>Writes zeros over the first place the file holds these bytes.</summary>
    private static byte[] Blank(byte[] file, byte[] part)
    {
        int at = file.AsSpan().IndexOf((ReadOnlySpan<byte>)part);
        Assert.True(at >= 0 && part.Length != 0);
        byte[] copy = (byte[])file.Clone();
        copy.AsSpan(at, part.Length).Clear();
        return copy;
    }

    /// <summary>
    /// One file of the named format with EXIF, IPTC and XMP wherever the format carries them. A
    /// refused part is stored exactly where the good one would be.
    /// </summary>
    private static byte[] Sample(string name, bool badExif = false, bool badXmp = false)
    {
        byte[] tiff = badExif ? BadTiff : JpegFixture.OrientationTiff;
        byte[] packet = badXmp ? NoPacket : GoodPacket;
        switch (name)
        {
            case "jpeg":
                return JpegFixture.Build([
                    .. JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)),
                    .. JpegFixture.Segment(0xED, IptcCodecTests.Photoshop(IptcCodecTests.Block(0x0404, IptcStream))),
                    .. JpegFixture.Segment(0xE1, [.. XmpHeader, .. packet])]);
            case "png":
                return PngFixture.Build([.. PngFixture.Chunk("eXIf", tiff), .. PngFixture.Xmp(packet)]);
            case "webp":
                return WebPFixture.Extended(0x0C, WebPFixture.Image(0)[12..], WebPFixture.Chunk("EXIF", tiff), WebPFixture.Chunk("XMP ", packet));
            case "tiff":
                // ImageDescription, XMLPacket, IPTC-NAA: the directories themselves are never the refused part.
                return Tiff((0x010E, 2, "snapshot\0"u8.ToArray()), (0x02BC, 1, packet), (0x83BB, 7, IptcStream));
            case "psd":
            {
                byte[] file = Fixture("owned-metadata-rgb.psd");
                PsdMetadata resources = PsdMetadata.Read(file);
                if (badExif) file = Blank(file, resources.GetResource(0x0422)!);
                if (badXmp) file = Blank(file, resources.GetResource(0x0424)!);
                return file;
            }
            case "exv":
                return [
                    0xFF, 0x01, .. "Exiv2"u8,
                    .. JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)),
                    .. JpegFixture.Segment(0xED, IptcCodecTests.Photoshop(IptcCodecTests.Block(0x0404, IptcStream))),
                    .. JpegFixture.Segment(0xE1, [.. XmpHeader, .. packet]),
                    0xFF, 0xD9];
            case "jp2":
                return [.. Fixture("owned-rgb.jp2"), .. Box("xml ", packet)];
            case "sidecar":
                return packet;
            case "cr3":
            {
                var layout = new Cr3Fixture.Layout { Xmp = packet };
                if (badExif) layout.Exif = BadTiff;
                return Cr3Fixture.Create(layout).Bytes;
            }
            case "heic":
            {
                BmffFixture fixture = BmffFixture.Create("heic", false);
                byte[] file = (byte[])fixture.Bytes.Clone();
                if (badExif) file[fixture.Tiff] = file[fixture.Tiff + 1] = (byte)'X';
                if (badXmp)
                {
                    (int start, int length) = fixture.Positions[BmffFixture.XmpId][0];
                    file.AsSpan(start, length).Clear();
                }
                return file;
            }
            case "jxl":
                return JxlFixture.Container(JxlFixture.ExifBox(tiff), JxlFixture.XmlBox(packet));
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }
    }

    /// <summary>The name of a sample, its format, and whether the format carries EXIF, IPTC and XMP.</summary>
    public static TheoryData<string, MetadataFormat, bool, bool> Formats => new()
    {
        { "jpeg", MetadataFormat.Jpeg, true, true },
        { "png", MetadataFormat.Png, true, false },
        { "webp", MetadataFormat.WebP, true, false },
        { "tiff", MetadataFormat.Tiff, true, true },
        { "psd", MetadataFormat.Psd, true, true },
        { "exv", MetadataFormat.Exv, true, true },
        { "jp2", MetadataFormat.Jp2, false, false },
        { "sidecar", MetadataFormat.XmpSidecar, false, false },
        { "cr3", MetadataFormat.Cr3, true, false },
        { "heic", MetadataFormat.Bmff, true, false },
        { "jxl", MetadataFormat.Jxl, true, false },
    };

    /// <summary>The formats whose EXIF block is a part of its own, which can be refused alone.</summary>
    public static TheoryData<string> WithExifBlock => new() { "jpeg", "png", "webp", "psd", "exv", "cr3", "heic", "jxl" };

    /// <summary>A stream that only reads forward, a few bytes at a time.</summary>
    private sealed class ForwardOnly(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 7));
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static MetadataReadError Error(ImageMetadata metadata, MetadataFamily family) =>
        Assert.Single(metadata.Errors, error => error.Family == family);

    /// <summary>
    /// The errors of the three kinds a container reader gives. The readers of the profile, the
    /// comments and the previews judge a broken file on their own, and may list theirs after these.
    /// </summary>
    private static MetadataReadError[] OfTheContainer(ImageMetadata metadata) =>
        metadata.Errors.Where(error => error.Family is MetadataFamily.Exif or MetadataFamily.IptcIim or MetadataFamily.Xmp).ToArray();

    // ---- the signature ----

    [Theory]
    [InlineData("FFD8FF", MetadataFormat.Jpeg)]
    [InlineData("89504E470D0A1A0A", MetadataFormat.Png)]
    [InlineData("524946462400000057454250", MetadataFormat.WebP)]
    [InlineData("4949524308000000", MetadataFormat.Dcp)]
    [InlineData("4D4D435200000008", MetadataFormat.Dcp)]
    [InlineData("49492A0008000000", MetadataFormat.Tiff)]
    [InlineData("4D4D002A00000008", MetadataFormat.Tiff)]
    [InlineData("4949524F08000000", MetadataFormat.Tiff)]
    [InlineData("4D4D4F5200000008", MetadataFormat.Tiff)]
    [InlineData("4949550018000000", MetadataFormat.Tiff)]
    [InlineData("384250530001", MetadataFormat.Psd)]
    [InlineData("FF014578697632", MetadataFormat.Exv)]
    [InlineData("0000000C6A5020200D0A870A", MetadataFormat.Jp2)]
    [InlineData("3C3F787061636B6574", MetadataFormat.XmpSidecar)]
    [InlineData("3C3F786D6C", MetadataFormat.XmpSidecar)]
    [InlineData("3C783A786D706D657461", MetadataFormat.XmpSidecar)]
    [InlineData("3C7264663A524446", MetadataFormat.XmpSidecar)]
    [InlineData("EFBBBF3C3F787061636B6574", MetadataFormat.XmpSidecar)]
    [InlineData("EFBBBF3C783A786D706D657461", MetadataFormat.XmpSidecar)]
    [InlineData("000000186674797063727820", MetadataFormat.Cr3)]
    [InlineData("000000186674797068656963", MetadataFormat.Bmff)]
    [InlineData("000000186674797061766966", MetadataFormat.Bmff)]
    [InlineData("0000001466747970", MetadataFormat.Bmff)]
    [InlineData("0000000C4A584C200D0A870A", MetadataFormat.Jxl)]
    [InlineData("FF0A", MetadataFormat.Jxl)]
    // Nothing the metadata readers take: no bytes, GIF, Matroska, PostScript, ORF under "IIRS", a
    // RIFF that is not WebP, a byte order mark alone, another processing instruction, another
    // element, BMP, and signatures cut short.
    [InlineData("", MetadataFormat.Unknown)]
    [InlineData("1A45DFA3", MetadataFormat.Matroska)]
    [InlineData("4949525308000000", MetadataFormat.Unknown)]
    [InlineData("524946462400000057415645", MetadataFormat.Riff)]
    [InlineData("EFBBBF", MetadataFormat.Unknown)]
    [InlineData("3C3F666F6F", MetadataFormat.Unknown)]
    [InlineData("3C737667", MetadataFormat.Unknown)]
    // The signatures of the small group, and the near misses either side of them.
    [InlineData("474946383961", MetadataFormat.Gif)]
    [InlineData("474946383761", MetadataFormat.Gif)]
    [InlineData("4749463838", MetadataFormat.Unknown)]
    [InlineData("424D", MetadataFormat.Bmp)]
    [InlineData("504746", MetadataFormat.Pgf)]
    [InlineData("50474636", MetadataFormat.Pgf)]
    [InlineData("504647", MetadataFormat.Unknown)]
    [InlineData("252150532D41646F62652D332E3020455053462D332E30", MetadataFormat.Eps)]
    [InlineData("C5D0D3C6", MetadataFormat.Eps)]
    [InlineData("252150532D41646F62652D322E30", MetadataFormat.Unknown)]
    [InlineData("252150532D41646F6265", MetadataFormat.Unknown)]
    [InlineData("FF", MetadataFormat.Unknown)]
    [InlineData("FFD9", MetadataFormat.Unknown)]
    [InlineData("0000000C", MetadataFormat.Unknown)]
    [InlineData("52494646", MetadataFormat.Riff)]
    [InlineData("4949", MetadataFormat.Unknown)]
    public void TheSignatureTellsTheFormat(string hex, MetadataFormat expected)
    {
        byte[] header = Convert.FromHexString(hex);
        Assert.Equal(expected, ImageMetadata.Detect(header));
        // What follows a signature that was found changes nothing.
        if (expected != MetadataFormat.Unknown)
            Assert.Equal(expected, ImageMetadata.Detect([.. header, .. Enumerable.Repeat((byte)0x55, 64)]));
        // A header cut anywhere is answered, never refused.
        for (int length = 0; length < header.Length; length++)
            _ = ImageMetadata.Detect(header.AsSpan(0, length));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void TheHeaderLengthIsEnoughForEveryFormat(string name, MetadataFormat format, bool hasExif, bool hasIptc)
    {
        _ = hasExif;
        _ = hasIptc;
        byte[] file = Sample(name);
        Assert.True(file.Length >= ImageMetadata.HeaderLength);
        Assert.Equal(format, ImageMetadata.Detect(file));
        Assert.Equal(format, ImageMetadata.Detect(file.AsSpan(0, ImageMetadata.HeaderLength)));
    }

    [Fact]
    public void ANameDoesNotOverrideARecognizedSignature()
    {
        string path = Path.Combine(Path.GetTempPath(), "image-metadata-" + Guid.NewGuid().ToString("N") + ".tga");
        File.WriteAllBytes(path, Sample("jpeg"));
        try
        {
            ImageMetadata metadata = ImageMetadata.Read(path);
            Assert.Equal(MetadataFormat.Jpeg, metadata.Format);
            Assert.NotNull(metadata.Exif);
            Assert.Empty(metadata.Errors);
        }
        // The file was closed: on Windows an open file cannot be deleted.
        finally { File.Delete(path); }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ANameTellsTargaWhenNoSignatureDoes()
    {
        byte[] file = new byte[18];
        file[12] = 37;
        file[14] = 19;
        string path = Path.Combine(Path.GetTempPath(), "image-metadata-" + Guid.NewGuid().ToString("N") + ".TGA");
        File.WriteAllBytes(path, file);
        try
        {
            Assert.Equal(MetadataFormat.Unknown, ImageMetadata.Detect(file));
            Assert.Equal(MetadataFormat.Tga, ImageMetadata.Detect(path));
            Assert.Equal(MetadataFormat.Tga, ImageMetadata.Read(path).Format);
            Assert.Equal(MetadataFormat.Tga, (await ImageMetadata.ReadAsync(path)).Format);
        }
        finally { File.Delete(path); }
    }

    // ---- every format ----

    [Theory]
    [MemberData(nameof(Formats))]
    public void EveryKindAFormatCarriesIsRead(string name, MetadataFormat format, bool hasExif, bool hasIptc)
    {
        ImageMetadata metadata = ImageMetadata.Read(Sample(name));
        Assert.Equal(format, metadata.Format);
        Assert.Empty(metadata.Errors);
        Assert.Equal(hasExif, metadata.Exif is not null);
        if (hasExif) Assert.NotEmpty(metadata.Exif!.Entries);
        Assert.Equal(hasIptc, metadata.Iptc is not null);
        if (hasIptc) Assert.NotEmpty(metadata.Iptc!.Datasets);
        Assert.NotNull(metadata.Xmp);
        Assert.NotEmpty(metadata.Xmp.Entries);
    }

    [Fact]
    public void DcpProfileUsesTheClassicTiffMetadataReader()
    {
        byte[] profile = Tiff((0x010F, 2, "Adobe\0"u8.ToArray()), (0x02BC, 1, GoodPacket), (0x83BB, 7, IptcStream));
        profile[2] = 0x52;
        profile[3] = 0x43;

        ImageMetadata metadata = ImageMetadata.Read(profile);

        Assert.Equal(MetadataFormat.Dcp, metadata.Format);
        Assert.Equal(new[] { 0x010F, 0x02BC, 0x83BB }, metadata.Exif!.Entries.Select(entry => (int)entry.Key.Tag));
        Assert.NotNull(metadata.Iptc);
        Assert.Equal(IptcStream.Length, metadata.Iptc.ByteLength);
        Assert.Equal(GoodPacket.Length, metadata.Xmp!.ByteLength);
        Assert.Empty(metadata.Errors);
    }

    [Fact]
    public void ThePartsAreWhatTheReaderOfTheContainerReturns()
    {
        byte[] cr3 = Sample("cr3");
        Cr3Metadata direct = Cr3Metadata.Read(cr3);
        ImageMetadata metadata = ImageMetadata.Read(cr3);
        Assert.Equal(direct.Exif!.Entries.Select(entry => entry.Key), metadata.Exif!.Entries.Select(entry => entry.Key));
        // The MakerNote comes with the EXIF, decoded.
        Assert.NotNull(metadata.Exif.MakerNote);
        Assert.Equal(direct.Exif.MakerNote!.Entries.Count, metadata.Exif.MakerNote.Entries.Count);
        Assert.Equal(direct.Xmp!.ByteLength, metadata.Xmp!.ByteLength);
        // The previews of the file are listed, in the order of the preview reader.
        Assert.NotEmpty(metadata.Previews);
        Assert.Equal(Previews.List(cr3), metadata.Previews);

        byte[] tiff = Sample("tiff");
        metadata = ImageMetadata.Read(tiff);
        Assert.Equal(TiffExif.Read(tiff).Entries.Count, metadata.Exif!.Entries.Count);
        Assert.Equal(TiffIptc.Read(tiff)!.ByteLength, metadata.Iptc!.ByteLength);
        // The packet of a TIFF is the value of tag 700 of the primary directory.
        Assert.Equal(GoodPacket.Length, metadata.Xmp!.ByteLength);

        byte[] heic = Sample("heic");
        BmffMetadata items = BmffMetadata.Read(heic);
        metadata = ImageMetadata.Read(heic);
        Assert.Equal(items.Exif!.Entries.Count, metadata.Exif!.Entries.Count);
        Assert.Equal(items.Xmp!.ByteLength, metadata.Xmp!.ByteLength);
    }

    [Fact]
    public void TheProfileAndTheCommentsComeWithTheRest()
    {
        byte[] profile = IccProfileTests.Profile(200);
        byte[] file = JpegFixture.Build([
            .. JpegFixture.Segment(0xE1, JpegFixture.Exif()),
            .. JpegFixture.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. profile]),
            .. JpegFixture.Segment(0xFE, "a comment"u8.ToArray())]);
        ImageMetadata metadata = ImageMetadata.Read(file);
        Assert.Empty(metadata.Errors);
        Assert.Equal(profile, metadata.IccProfile.ToArray());
        Assert.Equal(IccProfiles.Read(file), metadata.IccProfile.ToArray());
        Assert.Equal(new[] { "a comment" }, metadata.Comments);
        Assert.Equal(Comments.Read(file), metadata.Comments);
        Assert.Null(metadata.Iptc);
        Assert.Null(metadata.Xmp);
        Assert.Empty(metadata.Previews);

        // A file without a profile has an empty one, not an error.
        metadata = ImageMetadata.Read(Sample("png"));
        Assert.True(metadata.IccProfile.IsEmpty);
        Assert.Empty(metadata.Comments);
    }

    [Fact]
    public void AFileWithoutMetadataGivesAnEmptySnapshotOfItsFormat()
    {
        foreach ((byte[] file, MetadataFormat format) in new[]
        {
            (JpegFixture.Build(), MetadataFormat.Jpeg),
            (PngFixture.Build(), MetadataFormat.Png),
            (WebPFixture.Image(0), MetadataFormat.WebP),
            (Fixture("owned-flat-rgb.psd"), MetadataFormat.Psd),
            (Fixture("owned-rgb.jp2"), MetadataFormat.Jp2),
            (new byte[] { 0xFF, 0x01, (byte)'E', (byte)'x', (byte)'i', (byte)'v', (byte)'2', 0xFF, 0xD9 }, MetadataFormat.Exv),
            (BmffFixture.Create("heic-bare", false).Bytes, MetadataFormat.Bmff),
            // A JPEG XL file that is the codestream alone has no boxes to carry metadata.
            (JxlFixture.Codestream(), MetadataFormat.Jxl),
        })
        {
            ImageMetadata metadata = ImageMetadata.Read(file);
            Assert.Equal(format, metadata.Format);
            Assert.Null(metadata.Exif);
            Assert.Null(metadata.Iptc);
            Assert.Null(metadata.Xmp);
            Assert.True(metadata.IccProfile.IsEmpty);
            Assert.Empty(metadata.Comments);
            Assert.Empty(metadata.Errors);
        }
    }

    // A GIF that stops inside its signature is not a GIF file: it has no format and no metadata, and
    // reading it is not a refusal either.
    [Theory]
    [InlineData("")]
    [InlineData("4749463838")]
    public void AnUnknownFormatGivesAnEmptySnapshot(string hex)
    {
        ImageMetadata metadata = ImageMetadata.Read(Convert.FromHexString(hex));
        Assert.Equal(MetadataFormat.Unknown, metadata.Format);
        Assert.Null(metadata.Exif);
        Assert.Null(metadata.Iptc);
        Assert.Null(metadata.Xmp);
        Assert.True(metadata.IccProfile.IsEmpty);
        Assert.Empty(metadata.Comments);
        Assert.Empty(metadata.Previews);
        Assert.Empty(metadata.Errors);
    }

    // ---- what one kind's reader refuses ----

    [Theory]
    [MemberData(nameof(Formats))]
    public void ARefusedPacketDoesNotHideTheOtherKinds(string name, MetadataFormat format, bool hasExif, bool hasIptc)
    {
        ImageMetadata metadata = ImageMetadata.Read(Sample(name, badXmp: true));
        Assert.Equal(format, metadata.Format);
        Assert.Null(metadata.Xmp);
        MetadataReadError error = Assert.Single(metadata.Errors);
        Assert.Equal(MetadataFamily.Xmp, error.Family);
        Assert.Equal(MetadataErrorCode.InvalidXmp, error.Code);
        Assert.Equal(MetadataErrorCode.InvalidXmp, error.Exception.Code);
        Assert.StartsWith("Xmp: InvalidXmp: ", error.ToString());
        Assert.Equal(hasExif, metadata.Exif is not null);
        Assert.Equal(hasIptc, metadata.Iptc is not null);
    }

    [Theory]
    [MemberData(nameof(WithExifBlock))]
    public void ARefusedExifBlockDoesNotHideTheOtherKinds(string name)
    {
        ImageMetadata good = ImageMetadata.Read(Sample(name));
        ImageMetadata metadata = ImageMetadata.Read(Sample(name, badExif: true));
        Assert.Equal(good.Format, metadata.Format);
        Assert.Null(metadata.Exif);
        Assert.Equal(MetadataFamily.Exif, Assert.Single(metadata.Errors).Family);
        Assert.NotNull(metadata.Xmp);
        Assert.Equal(good.Xmp!.ByteLength, metadata.Xmp.ByteLength);
        Assert.Equal(good.Iptc is not null, metadata.Iptc is not null);
        // A kind that was refused is not known to be absent: nothing is reconciled without it.
        Assert.Same(metadata.Errors[0].Exception, Assert.Throws<MetadataException>(() => metadata.Reconcile()));
    }

    [Fact]
    public void BothPartsRefusedAreBothListed()
    {
        foreach (string name in new[] { "jpeg", "png", "webp", "exv", "cr3", "heic", "jxl" })
        {
            ImageMetadata metadata = ImageMetadata.Read(Sample(name, badExif: true, badXmp: true));
            Assert.Null(metadata.Exif);
            Assert.Null(metadata.Xmp);
            Assert.Equal(new[] { MetadataFamily.Exif, MetadataFamily.Xmp }, metadata.Errors.Select(error => error.Family));
            Assert.NotSame(metadata.Errors[0].Exception, metadata.Errors[1].Exception);
        }
    }

    [Fact]
    public void TheReadersOfTheContainersStillFailWhole()
    {
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => ExvMetadata.Read(Sample("exv", badXmp: true))).Code);
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => BmffMetadata.Read(Sample("heic", badXmp: true))).Code);
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => Cr3Metadata.Read(Sample("cr3", badXmp: true))).Code);
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => JxlMetadata.Read(Sample("jxl", badXmp: true))).Code);
        Assert.Throws<MetadataException>(() => ExvMetadata.Read(Sample("exv", badExif: true)));
        Assert.Throws<MetadataException>(() => BmffMetadata.Read(Sample("heic", badExif: true)));
        Assert.Throws<MetadataException>(() => Cr3Metadata.Read(Sample("cr3", badExif: true)));
        Assert.Throws<MetadataException>(() => JxlMetadata.Read(Sample("jxl", badExif: true)));
        // A JPEG document does not open over an EXIF block its reader refuses.
        Assert.Throws<MetadataException>(() => JpegMetadataDocument.Open(new MemoryStream(Sample("jpeg", badExif: true))));
    }

    [Fact]
    public void ALaterSegmentIsNotTakenInPlaceOfARefusedOne()
    {
        byte[] file = [
            0xFF, 0x01, .. "Exiv2"u8,
            .. JpegFixture.Segment(0xE1, JpegFixture.Exif(BadTiff)),
            .. JpegFixture.Segment(0xE1, JpegFixture.Exif()),
            0xFF, 0xD9];
        ImageMetadata metadata = ImageMetadata.Read(file);
        Assert.Null(metadata.Exif);
        Assert.Equal(MetadataFamily.Exif, Assert.Single(metadata.Errors).Family);
    }

    [Fact]
    public void ARefusedProfileIsListedAfterTheKindsBeforeIt()
    {
        // The first of two chunks of a profile, and no second.
        byte[] file = JpegFixture.Build([
            .. JpegFixture.Segment(0xE1, JpegFixture.Exif()),
            .. JpegFixture.Segment(0xE1, [.. XmpHeader, .. NoPacket]),
            .. JpegFixture.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 2, .. IccProfileTests.Profile(400)[..200]])]);
        ImageMetadata metadata = ImageMetadata.Read(file);
        Assert.NotNull(metadata.Exif);
        Assert.Null(metadata.Xmp);
        Assert.True(metadata.IccProfile.IsEmpty);
        Assert.Equal(new[] { MetadataFamily.Xmp, MetadataFamily.Icc }, metadata.Errors.Select(error => error.Family));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Error(metadata, MetadataFamily.Icc).Code);
    }

    [Fact]
    public void ARefusedContainerIsListedUnderEachKindItsReaderGives()
    {
        // A JPEG with a segment the scanner refuses: the document does not open.
        ImageMetadata jpeg = ImageMetadata.Read(JpegFixture.Build(prefix: JpegFixture.Segment(0xDC, 0, 9)));
        Assert.Equal(MetadataFormat.Jpeg, jpeg.Format);
        MetadataReadError[] errors = OfTheContainer(jpeg);
        Assert.Equal(new[] { MetadataFamily.Exif, MetadataFamily.IptcIim, MetadataFamily.Xmp }, errors.Select(error => error.Family));
        Assert.All(errors, error => Assert.Equal(MetadataErrorCode.InvalidJpeg, error.Code));
        Assert.Same(errors[0].Exception, errors[1].Exception);
        Assert.Same(errors[0].Exception, errors[2].Exception);
        // The errors keep the order of the kinds, whatever else is listed.
        Assert.Equal(jpeg.Errors.OrderBy(error => error.Family), jpeg.Errors);

        // A PNG that ends after its signature.
        ImageMetadata png = ImageMetadata.Read(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13 });
        Assert.Equal(MetadataFormat.Png, png.Format);
        errors = OfTheContainer(png);
        Assert.Equal(new[] { MetadataFamily.Exif, MetadataFamily.IptcIim, MetadataFamily.Xmp }, errors.Select(error => error.Family));
        Assert.All(errors, error => Assert.Equal(MetadataErrorCode.InvalidPng, error.Code));
        Assert.Same(errors[0].Exception, errors[2].Exception);

        // A BMFF file whose second box is longer than the file. The format carries no IPTC, so none is listed.
        byte[] cut = [.. BmffFixture.Box("ftyp", BmffFixture.Ascii("heic"), new byte[4], BmffFixture.Ascii("mif1heic")), 0, 0, 1, 0, .. "meta"u8];
        ImageMetadata heic = ImageMetadata.Read(cut);
        Assert.Equal(MetadataFormat.Bmff, heic.Format);
        errors = OfTheContainer(heic);
        Assert.Equal(new[] { MetadataFamily.Exif, MetadataFamily.Xmp }, errors.Select(error => error.Family));
        Assert.All(errors, error => Assert.Equal(MetadataErrorCode.InvalidBmff, error.Code));
        Assert.Same(errors[0].Exception, errors[1].Exception);

        // A TIFF whose first directory lies past its end: no directories, so no packet to look for.
        ImageMetadata tiff = ImageMetadata.Read(new byte[] { 0x49, 0x49, 42, 0, 0xF0, 0xFF, 0xFF, 0x7F });
        Assert.Equal(MetadataFormat.Tiff, tiff.Format);
        Assert.Null(tiff.Exif);
        Assert.Null(tiff.Xmp);
        Assert.Same(Error(tiff, MetadataFamily.Exif).Exception, Error(tiff, MetadataFamily.Xmp).Exception);

        // The snapshot of such a file is still a snapshot: nothing is half read.
        Assert.Null(jpeg.Exif);
        Assert.Null(jpeg.Iptc);
        Assert.Null(jpeg.Xmp);
    }

    // ---- limits ----

    [Fact]
    public void GivenLimitsReachTheReaders()
    {
        byte[] file = Sample("tiff");
        Assert.NotNull(ImageMetadata.Read(file).Exif);
        // Three entries in a directory that may hold one.
        ImageMetadata metadata = ImageMetadata.Read(file, new MetadataLimits { MaxEntriesPerDirectory = 1 });
        Assert.Null(metadata.Exif);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(metadata, MetadataFamily.Exif).Code);
    }

    [Fact]
    public async Task AFileLongerThanTheInputBudgetIsRefusedByEveryOverload()
    {
        byte[] file = Sample("jpeg");
        var fits = new MetadataLimits { MaxInputBytes = file.Length };
        var tooSmall = new MetadataLimits { MaxInputBytes = file.Length - 1 };
        Assert.Empty(ImageMetadata.Read(file, fits).Errors);
        Assert.Empty(ImageMetadata.Read(new ForwardOnly(file), fits).Errors);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => ImageMetadata.Read(file, tooSmall)).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => ImageMetadata.Read(new MemoryStream(file), tooSmall)).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => ImageMetadata.Read(new ForwardOnly(file), tooSmall)).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            (await Assert.ThrowsAsync<MetadataException>(() => ImageMetadata.ReadAsync(new MemoryStream(file), tooSmall))).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            (await Assert.ThrowsAsync<MetadataException>(() => ImageMetadata.ReadAsync(new ForwardOnly(file), tooSmall))).Code);

        string path = Path.Combine(Path.GetTempPath(), "image-metadata-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, file);
        try
        {
            Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => ImageMetadata.Read(path, tooSmall)).Code);
            Assert.Equal(MetadataErrorCode.LimitExceeded,
                (await Assert.ThrowsAsync<MetadataException>(() => ImageMetadata.ReadAsync(path, tooSmall))).Code);
            Assert.Empty((await ImageMetadata.ReadAsync(path, fits)).Errors);
        }
        // The file was closed after the refusal too: on Windows an open file cannot be deleted.
        finally { File.Delete(path); }
    }

    [Fact]
    public void WrongArgumentsFailAtTheCall()
    {
        var invalid = new MetadataLimits { MaxSegments = 0 };
        byte[] file = Sample("png");
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageMetadata.Read(file, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageMetadata.Read(new MemoryStream(file), invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageMetadata.Read("unused", invalid));
        // The asynchronous read checks its arguments before it starts: these are thrown at the call, not held in the task.
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = ImageMetadata.ReadAsync(new MemoryStream(file), invalid); });
        Assert.Throws<ArgumentNullException>(() => ImageMetadata.Read((Stream)null!));
        Assert.Throws<ArgumentNullException>(() => { _ = ImageMetadata.ReadAsync((Stream)null!); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = ImageMetadata.ReadAsync("unused", invalid); });
        Assert.Throws<ArgumentNullException>(() => { _ = ImageMetadata.ReadAsync((string)null!); });
        Assert.Throws<ArgumentException>(() => { _ = ImageMetadata.ReadAsync(string.Empty); });
        Assert.Throws<ArgumentNullException>(() => ImageMetadata.Read((string)null!));
        Assert.Throws<ArgumentException>(() => ImageMetadata.Read(string.Empty));
        var closed = new MemoryStream(file);
        closed.Dispose();
        Assert.Throws<ArgumentException>(() => ImageMetadata.Read(closed));
    }

    // ---- stream, async, path ----

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task EveryOverloadGivesTheSameSnapshot(string name, MetadataFormat format, bool hasExif, bool hasIptc)
    {
        byte[] file = Sample(name);
        ImageMetadata expected = ImageMetadata.Read(file);

        void Same(ImageMetadata actual)
        {
            Assert.Equal(format, actual.Format);
            Assert.Empty(actual.Errors);
            Assert.Equal(hasExif, actual.Exif is not null);
            Assert.Equal(expected.Exif?.Entries.Count, actual.Exif?.Entries.Count);
            Assert.Equal(hasIptc, actual.Iptc is not null);
            Assert.Equal(expected.Iptc?.ByteLength, actual.Iptc?.ByteLength);
            Assert.Equal(expected.Xmp!.ByteLength, actual.Xmp!.ByteLength);
            Assert.Equal(expected.IccProfile.ToArray(), actual.IccProfile.ToArray());
            Assert.Equal(expected.Comments, actual.Comments);
            Assert.Equal(expected.Previews, actual.Previews);
        }

        using var seekable = new MemoryStream(file);
        Same(ImageMetadata.Read(seekable));
        // The stream is read to its end and left open.
        Assert.Equal(file.Length, seekable.Position);
        Assert.True(seekable.CanRead);

        // A stream is read from where it stands, not from its start.
        using var offset = new MemoryStream([1, 2, 3, 4, 5, .. file]);
        offset.Position = 5;
        Same(ImageMetadata.Read(offset));
        offset.Position = 5;
        Same(await ImageMetadata.ReadAsync(offset));

        Same(ImageMetadata.Read(new ForwardOnly(file)));
        Same(await ImageMetadata.ReadAsync(new ForwardOnly(file)));

        string path = Path.Combine(Path.GetTempPath(), "image-metadata-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, file);
        try
        {
            Same(ImageMetadata.Read(path));
            Same(await ImageMetadata.ReadAsync(path));
        }
        // Both reads closed the file: on Windows an open file cannot be deleted.
        finally { File.Delete(path); }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ACancelledReadEndsWithoutASnapshot()
    {
        using var source = new MemoryStream(Sample("jpeg"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageMetadata.ReadAsync(source, null, new CancellationToken(true)));
        Assert.Equal(0, source.Position);

        string path = Path.Combine(Path.GetTempPath(), "image-metadata-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, Sample("jpeg"));
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageMetadata.ReadAsync(path, null, new CancellationToken(true))); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AFileThatIsNotThereFailsTheTaskOfTheAsynchronousRead()
    {
        string missing = Path.Combine(Path.GetTempPath(), "image-metadata-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<FileNotFoundException>(() => ImageMetadata.Read(missing));
        // The call returns: the arguments are in order, and the file is opened in the task.
        Task<ImageMetadata> read = ImageMetadata.ReadAsync(missing);
        await Assert.ThrowsAsync<FileNotFoundException>(() => read);
    }

    // ---- the snapshot does not change ----

    [Fact]
    public void TheSnapshotKeepsNothingOfTheCallersBuffer()
    {
        byte[] profile = IccProfileTests.Profile(200);
        byte[] file = JpegFixture.Build([
            .. JpegFixture.Segment(0xE1, JpegFixture.Exif()),
            .. JpegFixture.Segment(0xE1, [.. XmpHeader, .. GoodPacket]),
            .. JpegFixture.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. profile]),
            .. JpegFixture.Segment(0xFE, "kept"u8.ToArray())]);
        ImageMetadata metadata = ImageMetadata.Read(file);
        Array.Clear(file);
        Assert.Equal(profile, metadata.IccProfile.ToArray());
        Assert.Equal(new[] { "kept" }, metadata.Comments);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), Assert.Single(metadata.Exif!.Entries).Key);
        Assert.Equal(GoodPacket.Length, metadata.Xmp!.ByteLength);
        // The lists are read-only views, not the lists of the readers.
        Assert.True(((ICollection<string>)metadata.Comments).IsReadOnly);
        Assert.True(((ICollection<PreviewImage>)metadata.Previews).IsReadOnly);
        Assert.True(((ICollection<MetadataReadError>)metadata.Errors).IsReadOnly);
    }
}
