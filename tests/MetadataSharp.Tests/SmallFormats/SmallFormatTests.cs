using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Eps;
using MetadataSharp.Formats.Pgf;
using MetadataSharp.Formats.SmallFormats;
using Xunit;

namespace MetadataSharp.Tests.SmallFormats;

/// <summary>
/// The formats of group B that carry no metadata block of their own - GIF, BMP, TARGA - and the two
/// that carry it in a place of their own - PGF, whose header region is one whole image file, and EPS,
/// whose XMP is a packet inside its DSC comments. Every file here is authored byte by byte.
/// </summary>
public sealed class SmallFormatTests
{
    // ---- GIF ----

    /// <summary>A logical screen descriptor: the width and the height as two little-endian words.</summary>
    internal static byte[] Gif(int width, int height, string version = "GIF89a", byte[]? after = null) =>
        [.. Encoding.ASCII.GetBytes(version), .. Le16(width), .. Le16(height), 0x00, 0x00, 0x00, .. after ?? []];

    internal static byte[] Le16(int value) => [(byte)value, (byte)(value >> 8)];

    /// <summary>
    /// A little-endian TIFF: the header, the primary directory at 8, and every value stored after it
    /// at an even offset. The tags ascend, and every value here is longer than four bytes.
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

    internal static byte[] Le32(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void GifReadsTheSizeOfItsLogicalScreenDescriptor(string version)
    {
        GifMetadata gif = GifMetadata.Read(Gif(640, 480, version));
        Assert.Equal(640, gif.Width);
        Assert.Equal(480, gif.Height);
    }

    [Fact]
    public void GifReadsTheWordsAsUnsigned()
    {
        // A logical screen of 4000 by 60,000 is past the range of a signed word.
        GifMetadata gif = GifMetadata.Read(Gif(4000, 60000));
        Assert.Equal(4000, gif.Width);
        Assert.Equal(60000, gif.Height);
    }

    [Fact]
    public void GifWithNoDescriptorStatesNoSize()
    {
        // The reference reads four bytes after the signature and finds none, so it reports no size.
        GifMetadata gif = GifMetadata.Read("GIF89a"u8);
        Assert.Equal(0, gif.Width);
        Assert.Equal(0, gif.Height);
    }

    [Fact]
    public void GifOfAnotherSignatureIsRefused()
    {
        Assert.Equal(MetadataErrorCode.InvalidGif, Assert.Throws<MetadataException>(() => GifMetadata.Read("GIF88a"u8)).Code);
    }

    [Fact]
    public void GifIsToldByEitherVersionAndNeedsItsWholeDescriptor()
    {
        Assert.True(GifMetadata.HasSignature(Gif(1, 1)));
        Assert.False(GifMetadata.HasSignature("GIF88a"u8));
        // The signature alone names the format; the descriptor is what a read needs.
        Assert.True(GifMetadata.HasSignature("GIF87a"u8));
    }

    // ---- BMP ----

    /// <summary>The file header, then a BITMAPINFOHEADER whose width and height are 32-bit words.</summary>
    internal static byte[] BmpInfoHeader(int width, int height)
    {
        byte[] data = new byte[54];
        data[0] = (byte)'B'; data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), 54);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), 24);
        return data;
    }

    /// <summary>The file header, then a BITMAPCOREHEADER whose width and height are 16-bit words.</summary>
    internal static byte[] BmpCoreHeader(int width, int height)
    {
        byte[] data = new byte[26];
        data[0] = (byte)'B'; data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), 26);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 26);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), (ushort)height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(24), 24);
        return data;
    }

    [Fact]
    public void BmpReadsTheSizeOfAnInformationHeader()
    {
        BmpMetadata bmp = BmpMetadata.Read(BmpInfoHeader(1920, 1080));
        Assert.Equal(1920u, bmp.Width);
        Assert.Equal(1080u, bmp.Height);
        Assert.Equal(40, bmp.HeaderBytes);
    }

    [Fact]
    public void BmpReadsTheSizeOfACoreHeader()
    {
        BmpMetadata bmp = BmpMetadata.Read(BmpCoreHeader(48, 32));
        Assert.Equal(48u, bmp.Width);
        Assert.Equal(32u, bmp.Height);
        Assert.Equal(12, bmp.HeaderBytes);
    }

    [Fact]
    public void BmpReadsANegativeHeightAsTheUnsignedWordItIs()
    {
        // A later header stores a top-down picture with a negative height. The reference reads the
        // word as an unsigned number, and so does this reader: the same bytes, the same answer.
        BmpMetadata bmp = BmpMetadata.Read(BmpInfoHeader(16, -8));
        Assert.Equal(16u, bmp.Width);
        Assert.Equal(4294967288u, bmp.Height);
    }

    [Fact]
    public void BmpOfAnotherSignatureIsRefusedAndATruncatedOneStatesNoSize()
    {
        Assert.Equal(MetadataErrorCode.InvalidBmp, Assert.Throws<MetadataException>(() => BmpMetadata.Read("BA"u8)).Code);
        // The reference reads twenty-six bytes at once; a shorter file gives it no size word at all.
        foreach (int length in new[] { 8, 12, 16, 20, 24 })
        {
            BmpMetadata bmp = BmpMetadata.Read(BmpInfoHeader(4, 4).AsSpan(0, length));
            Assert.Equal(0u, bmp.Width);
            Assert.Equal(0u, bmp.Height);
        }
    }

    // ---- TARGA ----

    /// <summary>An 18-byte header and, when asked for, the 26-byte footer of the 2.0 version.</summary>
    internal static byte[] Tga(int width, int height, bool footer = true)
    {
        byte[] data = new byte[18 + (footer ? 26 : 0)];
        data[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), (ushort)height);
        data[16] = 24;
        if (footer)
        {
            Encoding.ASCII.GetBytes("TRUEVISION-XFILE.").CopyTo(data.AsSpan(18 + 8));
            data[^1] = 0;
        }
        return data;
    }

    [Fact]
    public void TgaReadsTheSizeOfItsHeader()
    {
        TgaMetadata tga = TgaMetadata.Read(Tga(320, 240));
        Assert.Equal(320, tga.Width);
        Assert.Equal(240, tga.Height);
    }

    [Fact]
    public void TgaWithoutTheFooterIsNotToldAtAll()
    {
        // The reference tells a version 1 file by its name alone, which the library never has.
        Assert.False(TgaMetadata.HasSignature(Tga(320, 240, footer: false)));
        Assert.Equal(MetadataErrorCode.InvalidTga, Assert.Throws<MetadataException>(() => TgaMetadata.Read(Tga(320, 240, footer: false))).Code);
        // The smallest file that can carry the footer: the header is the twelve bytes before it, and the
        // reference reads the size out of them without looking at what they mean.
        Assert.Equal(0, TgaMetadata.Read([.. new byte[8], .. Encoding.ASCII.GetBytes("TRUEVISION-XFILE"), 0, 0]).Width);
    }

    [Fact]
    public void TgaIsToldByTheFooterAndNotByTheStart()
    {
        byte[] file = Tga(16, 16);
        Assert.True(TgaMetadata.HasSignature(file));
        // The same bytes without the footer are not a TARGA file however many of them there are.
        byte[] head = file.AsSpan(0, 18).ToArray();
        Assert.False(TgaMetadata.HasSignature(head));
    }

    // ---- PGF ----

    /// <summary>The header of a PGF file whose region holds <paramref name="image"/>, then that image.</summary>
    internal static byte[] Pgf(int width, int height, byte[] image, byte version = (byte)'6')
    {
        // The header structure is sixteen bytes, all little endian: width, height, the number of
        // levels, the quality, the bits per pixel, the channels, then two more bytes.
        var structure = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(structure.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(structure.AsSpan(4), (uint)height);
        structure[8] = 3; structure[9] = 24; structure[10] = 8; structure[11] = 3;
        int headerSize = 16 + image.Length;
        return ["PGF"u8[0], "PGF"u8[1], "PGF"u8[2], version, .. Le32(headerSize), .. structure, .. image];
    }

    [Fact]
    public void PgfReportsTheSizeOfItsHeaderAndTheMetadataOfItsImage()
    {
        // The region is a PNG file of this repository's own fixture builder, with an EXIF block.
        byte[] tiff = Tiff(
            (0x010F, 2, Encoding.ASCII.GetBytes("MetadataSharp\0")),
            (0x0110, 2, Encoding.ASCII.GetBytes("PGF carrier\0")));
        byte[] file = Pgf(320, 211, MetadataSharp.Tests.Png.PngFixture.Build(after: MetadataSharp.Tests.Png.PngFixture.Chunk("eXIf", tiff)));
        PgfMetadata pgf = PgfMetadata.Read(file);
        Assert.Equal(320u, pgf.Width);
        Assert.Equal(211u, pgf.Height);
        Assert.NotNull(pgf.Exif);
        Assert.Equal("PGF carrier", pgf.Exif!.Model);
    }

    [Fact]
    public void PgfRefusesAHeaderThatLeavesTheFileAndAnEmptyRegion()
    {
        byte[] file = Pgf(1, 1, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        // A header longer than the file that holds it.
        byte[] longer = (byte[])file.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(longer.AsSpan(4), 4096);
        Assert.Equal(MetadataErrorCode.InvalidPgf, Assert.Throws<MetadataException>(() => PgfMetadata.Read(longer)).Code);
        // A header that holds no image at all: the file has no metadata, and that is not a refusal.
        PgfMetadata empty = PgfMetadata.Read(Pgf(1, 1, []));
        Assert.Null(empty.Exif);
        Assert.Null(empty.Xmp);
        // A region whose container no reader of this library knows.
        Assert.Equal(MetadataErrorCode.InvalidPgf, Assert.Throws<MetadataException>(() => PgfMetadata.Read(Pgf(1, 1, "not an image"u8.ToArray()))).Code);
        // A region that holds a container this library reads, and refuses: the carrier's own refusal
        // travels, because that is what the reference reports of it too.
        byte[] badGif = [.. Encoding.ASCII.GetBytes("GIF88a"), .. new byte[8]];
        Assert.Equal(MetadataErrorCode.InvalidGif,
            Assert.Throws<MetadataException>(() => PgfMetadata.Read(Pgf(1, 1, badGif))).Code);
    }

    [Fact]
    public void PgfOfAnotherSignatureIsRefused()
    {
        Assert.Equal(MetadataErrorCode.InvalidPgf, Assert.Throws<MetadataException>(() => PgfMetadata.Read("PGX6"u8)).Code);
    }

    // ---- EPS ----

    internal static byte[] Eps(string packet, string trailer = "<?xpacket end=\"w\"?>", string firstLine = "%!PS-Adobe-3.0 EPSF-3.0",
        string newline = "\n", string header = "%%BoundingBox: 0 0 100 100", bool announce = true) =>
        Encoding.Latin1.GetBytes(
            firstLine + newline + header + newline +
            (announce ? "%%ADO_ContainsXMP: MainFirst" + newline : "") +
            "%%EndComments" + newline +
            packet + newline + trailer + newline +
            "%%BeginProlog" + newline + "%%EndProlog" + newline +
            "%%PageTrailer" + newline + "%%EOF" + newline);

    public const string PacketText =
        "<?xpacket begin=\"\u00ef\u00bb\u00bf\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
        "<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">EPS title</rdf:li></rdf:Alt></dc:title>" +
        "</rdf:Description></rdf:RDF></x:xmpmeta>";

    [Fact]
    public void EpsReadsThePacketOfItsComments()
    {
        EpsMetadata eps = EpsMetadata.Read(Eps(PacketText));
        Assert.NotNull(eps.Xmp);
        Assert.Equal("EPS title", eps.Xmp!.Title);
        Assert.False(eps.IsDosEps);
        // The PostScript section is the whole file when the DOS header is absent.
        Assert.Equal(0, eps.PostScriptOffset);
        Assert.Equal(Eps(PacketText).Length, eps.PostScriptLength);
    }

    [Fact]
    public void EpsWithNoPacketHasNoXmp()
    {
        EpsMetadata eps = EpsMetadata.Read(Eps("", announce: false, trailer: ""));
        Assert.Null(eps.Xmp);
        Assert.Null(eps.Packet);
    }

    [Fact]
    public void EpsAnnouncementOfNoPacketIsTakenAtItsWord()
    {
        // The header says there is no packet, so a packet below it is not the metadata of the file.
        byte[] file = Encoding.Latin1.GetBytes(
            "%!PS-Adobe-3.0 EPSF-3.0\n%%ADO_ContainsXMP: NoMain\n%%EndComments\n" + PacketText + "\n<?xpacket end=\"w\"?>\n%%EOF\n");
        Assert.Null(EpsMetadata.Read(file).Xmp);
    }

    [Theory]
    [InlineData("<?xpacket end=\"r\"?>")]
    [InlineData("<?xpacket end='r'?>")]
    public void EpsRefusesAReadOnlyTrailer(string trailer)
    {
        MetadataException error = Assert.Throws<MetadataException>(() => EpsMetadata.Read(Eps(PacketText, trailer)));
        Assert.Equal(MetadataErrorCode.InvalidEps, error.Code);
    }

    [Theory]
    [InlineData("<?xpacket end=\"w\"?>")]
    [InlineData("<?xpacket end='w' ?>")]
    public void EpsAcceptsBothWritableTrailers(string trailer)
    {
        Assert.NotNull(EpsMetadata.Read(Eps(PacketText, trailer)).Xmp);
    }

    [Fact]
    public void EpsReadsTheOtherFirstLinesTheReferenceAccepts()
    {
        Assert.NotNull(EpsMetadata.Read(Eps(PacketText, firstLine: "%!PS-Adobe-3.0 EPSF-3.0 ")).Xmp);
        Assert.NotNull(EpsMetadata.Read(Eps(PacketText, firstLine: "%!PS-Adobe-3.1 EPSF-3.0")).Xmp);
    }

    [Fact]
    public void EpsReadsEitherLineEnding()
    {
        byte[] file = Eps(PacketText, newline: "\r\n");
        EpsMetadata eps = EpsMetadata.Read(file);
        Assert.NotNull(eps.Xmp);
        Assert.Equal("EPS title", eps.Xmp!.Title);
    }

    [Fact]
    public void EpsOfAnotherFirstLineIsRefused()
    {
        Assert.Equal(MetadataErrorCode.InvalidEps, Assert.Throws<MetadataException>(() => EpsMetadata.Read(
            Encoding.Latin1.GetBytes("%!PS-Adobe-2.0 EPSF-2.0\n%%BoundingBox: 0 0 1 1\n%%EOF\n"))).Code);
    }

    [Fact]
    public void EpsWithAPacketHeaderAndNoTrailerIsRefused()
    {
        byte[] file = Encoding.Latin1.GetBytes("%!PS-Adobe-3.0 EPSF-3.0\n%%EndComments\n" + PacketText + "\n%%EOF\n");
        Assert.Equal(MetadataErrorCode.InvalidEps, Assert.Throws<MetadataException>(() => EpsMetadata.Read(file)).Code);
    }

    [Fact]
    public void DosEpsIsReadThroughItsHeader()
    {
        // The PostScript section begins at 30, and a TIFF section follows it.
        byte[] postScript = Eps(PacketText);
        byte[] tiff = "II*\0"u8.ToArray();
        var file = new byte[30 + postScript.Length + tiff.Length];
        file[0] = 0xC5; file[1] = 0xD0; file[2] = 0xD3; file[3] = 0xC6;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 30);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(8), postScript.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(12), 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(16), 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(20), 30 + postScript.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(24), tiff.Length);
        postScript.CopyTo(file.AsSpan(30));
        tiff.CopyTo(file.AsSpan(30 + postScript.Length));

        EpsMetadata eps = EpsMetadata.Read(file);
        Assert.True(eps.IsDosEps);
        Assert.Equal(30, eps.PostScriptOffset);
        Assert.Equal(postScript.Length, eps.PostScriptLength);
        Assert.Equal(30 + postScript.Length, eps.TiffOffset);
        Assert.Equal(tiff.Length, eps.TiffLength);
        Assert.Null(eps.WmfOffset);
        Assert.Equal("EPS title", eps.Xmp!.Title);
    }

    [Fact]
    public void DosEpsSectionOutsideTheFileIsRefused()
    {
        var file = new byte[30];
        file[0] = 0xC5; file[1] = 0xD0; file[2] = 0xD3; file[3] = 0xC6;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 30);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(8), 4096);
        Assert.Equal(MetadataErrorCode.InvalidEps, Assert.Throws<MetadataException>(() => EpsMetadata.Read(file)).Code);
    }

    // ---- the entry point ----

    [Fact]
    public void DetectTellsEveryFormatOfTheGroup()
    {
        Assert.Equal(MetadataFormat.Gif, ImageMetadata.Detect(Gif(1, 1)));
        Assert.Equal(MetadataFormat.Bmp, ImageMetadata.Detect(BmpInfoHeader(1, 1)));
        Assert.Equal(MetadataFormat.Tga, ImageMetadata.Detect(Tga(1, 1)));
        Assert.Equal(MetadataFormat.Pgf, ImageMetadata.Detect(Pgf(1, 1, "x"u8.ToArray())));
        Assert.Equal(MetadataFormat.Eps, ImageMetadata.Detect(Eps(PacketText)));
        Assert.Equal(MetadataFormat.Eps, ImageMetadata.Detect([0xC5, 0xD0, 0xD3, 0xC6, 0, 0, 0, 0]));
    }

    [Fact]
    public void FormatsWithoutAMetadataBlockReadWithNoPartsAndNoErrors()
    {
        foreach (byte[] file in new[] { Gif(640, 480), BmpInfoHeader(16, 16), Tga(16, 16) })
        {
            ImageMetadata metadata = ImageMetadata.Read(file);
            Assert.Null(metadata.Exif);
            Assert.Null(metadata.Iptc);
            Assert.Null(metadata.Xmp);
            Assert.Empty(metadata.Errors);
        }
    }

    [Fact]
    public void EntryPointReadsAPgfFilesCarrier()
    {
        byte[] tiff = Tiff((0x010F, 2, Encoding.ASCII.GetBytes("MetadataSharp\0")));
        ImageMetadata metadata = ImageMetadata.Read(Pgf(8, 8, MetadataSharp.Tests.Png.PngFixture.Build(after: MetadataSharp.Tests.Png.PngFixture.Chunk("eXIf", tiff))));
        Assert.Equal(MetadataFormat.Pgf, metadata.Format);
        Assert.NotNull(metadata.Exif);
        Assert.Equal("MetadataSharp", metadata.Exif!.Make);
    }

    [Fact]
    public void EntryPointReadsTheXmpOfAnEpsFile()
    {
        ImageMetadata metadata = ImageMetadata.Read(Eps(PacketText));
        Assert.Equal(MetadataFormat.Eps, metadata.Format);
        Assert.Null(metadata.Exif);
        Assert.Null(metadata.Iptc);
        Assert.Equal("EPS title", metadata.Xmp!.Title);
    }

    [Fact]
    public void EntryPointRefusesAFileItsOwnReaderRefuses()
    {
        // A file the GIF reader refuses is not a GIF file to the detector either, so the entry point
        // has nothing to read; the reader is called directly for the refusal.
        byte[] notGif = [.. Encoding.ASCII.GetBytes("GIF88a"), .. new byte[8]];
        Assert.Equal(MetadataFormat.Unknown, ImageMetadata.Detect(notGif));
        Assert.Equal(MetadataErrorCode.InvalidGif, Assert.Throws<MetadataException>(() => GifMetadata.Read(notGif)).Code);

        // And an EPS file whose packet is refused is refused under the XMP kind alone.
        ImageMetadata eps = ImageMetadata.Read(Eps(PacketText, "<?xpacket end=\"r\"?>"));
        Assert.Equal(MetadataFormat.Eps, eps.Format);
        MetadataReadError error = Assert.Single(eps.Errors);
        Assert.Equal(MetadataFamily.Xmp, error.Family);
        Assert.Equal(MetadataErrorCode.InvalidEps, error.Code);
    }
}
