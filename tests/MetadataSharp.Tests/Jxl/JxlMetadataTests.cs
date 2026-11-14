using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Tests.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Jxl;
using Xunit;
using static MetadataSharp.Tests.Cr3.Cr3Fixture;
using static MetadataSharp.Tests.Jxl.JxlFixture;

namespace MetadataSharp.Tests.Jxl;

/// <summary>What <see cref="JxlMetadata.Read"/> returns for the boxes a container stores as they are.</summary>
public sealed class JxlMetadataTests
{
    private const ExifDirectoryKind Primary = ExifDirectoryKind.Ifd0;
    private const ExifDirectoryKind Photo = ExifDirectoryKind.Exif;
    private static readonly XmpPropertyName Title = new("urn:owned:test", "title");

    private static ExifTagKey Key(ExifDirectoryKind directory, ushort tag) => new(directory, tag);

    private static byte[] Bytes(ExifMetadata exif, ExifDirectoryKind directory, ushort tag)
    {
        Assert.True(exif.TryGetEntry(Key(directory, tag), out ExifEntry? entry));
        var bytes = new byte[entry!.Value.ByteLength];
        entry.Value.CopyRawTo(bytes);
        return bytes;
    }

    private static ExifMetadata Exif(byte[] file, MetadataLimits? limits = null)
    {
        ExifMetadata? exif = JxlMetadata.Read(file, limits).Exif;
        Assert.NotNull(exif);
        return exif;
    }

    private static MetadataException Refusal(byte[] file, MetadataLimits? limits = null) =>
        Assert.Throws<MetadataException>(() => JxlMetadata.Read(file, limits));

    [Fact]
    public void ABareCodestreamHasNoMetadata()
    {
        JxlMetadata metadata = JxlMetadata.Read(Codestream());

        Assert.Null(metadata.Exif);
        Assert.Null(metadata.Xmp);
        Assert.False(metadata.HasJumbf);
    }

    [Fact]
    public void AContainerWithoutMetadataBoxesHasNoMetadata()
    {
        JxlMetadata metadata = JxlMetadata.Read(Container(Box("jxll", new byte[] { 5 })));

        Assert.Null(metadata.Exif);
        Assert.Null(metadata.Xmp);
        Assert.False(metadata.HasJumbf);
    }

    [Fact]
    public void IptcNaaInTheExifTiffIsRead()
    {
        byte[] iptc = IptcCodecTests.Raw(IptcPacket.CreateEmpty());
        byte[] tiff = TiffBlock(true, Raw(0x83BB, 7, iptc));

        JxlMetadata metadata = JxlMetadata.Read(Container(ExifBox(tiff)));

        Assert.NotNull(metadata.Iptc);
        Assert.Equal(iptc.Length, metadata.Iptc.ByteLength);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheBlockOfTheExifBoxIsRead(bool little)
    {
        ExifMetadata exif = Exif(Container(ExifBox(Block(little))));

        Assert.Equal(little ? Endianness.LittleEndian : Endianness.BigEndian, exif.ByteOrder);
        Assert.Equal(
            new[]
            {
                Key(Primary, 0x010F), Key(Primary, 0x0110), Key(Primary, 0x0112), Key(Primary, 0x8769),
                Key(Photo, 0x829A), Key(Photo, 0x8827), Key(Photo, 0x9003),
            },
            exif.Entries.Select(entry => entry.Key));
        Assert.Equal("Model XL\0"u8.ToArray(), Bytes(exif, Primary, 0x0110));
        Assert.Equal(little ? new byte[] { 6, 0 } : new byte[] { 0, 6 }, Bytes(exif, Primary, 0x0112));
        Assert.Equal(little ? new byte[] { 100, 0 } : new byte[] { 0, 100 }, Bytes(exif, Photo, 0x8827));
        Assert.Equal(little ? new byte[] { 1, 0, 0, 0, 250, 0, 0, 0 } : new byte[] { 0, 0, 0, 1, 0, 0, 0, 250 }, Bytes(exif, Photo, 0x829A));
        Assert.Equal("2021:01:02 03:04:05\0"u8.ToArray(), Bytes(exif, Photo, 0x9003));
        Assert.Null(exif.MakerNote);
    }

    [Fact]
    public void TheOffsetOfTheBoxSaysWhereTheBlockStarts()
    {
        // Six bytes of an identifier, as an APP1 segment has them, or any other bytes: they are skipped.
        byte[] identified = Container(ExifBox(Block(), "Exif\0\0"u8.ToArray()));
        byte[] padded = Container(ExifBox(Block(), new byte[] { 1, 2, 3 }));

        Assert.Equal(7, Exif(identified).Entries.Count);
        Assert.Equal("Model XL\0"u8.ToArray(), Bytes(Exif(padded), Primary, 0x0110));
    }

    [Fact]
    public void TheBoxIsReadWhereverItStands()
    {
        byte[] exif = ExifBox(Block(model: "Anywhere"));
        byte[] code = CodestreamBox();
        byte[] expected = "Anywhere\0"u8.ToArray();

        Assert.Equal(expected, Bytes(Exif(Boxes(exif, code)), Primary, 0x0110));
        Assert.Equal(expected, Bytes(Exif(Boxes(code, exif)), Primary, 0x0110));
        Assert.Equal(expected, Bytes(Exif(Boxes(Box("jxlp", new byte[8]), exif, Box("jxlp", new byte[8]))), Primary, 0x0110));
        Assert.Equal(expected, Bytes(Exif(Boxes(code, WideBox("Exif", ExifPayload(Block(model: "Anywhere"))))), Primary, 0x0110));
        Assert.Equal(expected, Bytes(Exif(Boxes(code, Join(Be32(0), "Exif"u8.ToArray(), ExifPayload(Block(model: "Anywhere"))))), Primary, 0x0110));
    }

    [Fact]
    public void ABlockOnlyTheReadOnlyRulesAcceptIsNotEditable()
    {
        // Field type 13 is not one of the twelve: the entry is left out, and the block cannot be written back.
        byte[] odd = TiffBlock(true, Shorts(0x0112, 6), Raw(0x0200, 13, [1, 2, 3, 4]));

        ExifMetadata plain = Exif(Container(ExifBox(Block())));
        ExifMetadata read = Exif(Container(ExifBox(odd)));

        Assert.True(plain.IsEditable);
        Assert.False(read.IsEditable);
        Assert.Equal(new[] { Key(Primary, 0x0112) }, read.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void ThePacketOfTheXmlBoxIsRead()
    {
        byte[] packet = Packet("jxl");

        JxlMetadata metadata = JxlMetadata.Read(Container(XmlBox(packet)));

        Assert.Null(metadata.Exif);
        Assert.NotNull(metadata.Xmp);
        Assert.Equal("jxl", metadata.Xmp.Properties[Title].Text);
        // The packet is kept as the box holds it, wrapper and padding included.
        var raw = new byte[metadata.Xmp.ByteLength];
        metadata.Xmp.CopyRawTo(raw);
        Assert.Equal(packet, raw);
    }

    [Fact]
    public void TheBlockThePacketAndTheJumbfBoxAreReportedTogether()
    {
        byte[] file = Boxes(XmlBox(Packet("both")), Box("jumb", new byte[24]), CodestreamBox(), ExifBox(Block()));

        JxlMetadata metadata = JxlMetadata.Read(file);

        Assert.NotNull(metadata.Exif);
        Assert.NotNull(metadata.Xmp);
        Assert.Equal(7, metadata.Exif.Entries.Count);
        Assert.Equal("both", metadata.Xmp.Properties[Title].Text);
        Assert.True(metadata.HasJumbf);
    }

    public static TheoryData<string, byte[], MetadataErrorCode> Malformed()
    {
        byte[] block = Block();
        byte[] order = (byte[])block.Clone();
        order[0] = 0x4A;
        byte[] magic = (byte[])block.Clone();
        magic[2] = 44;
        byte[] big = (byte[])block.Clone();
        big[2] = 43;
        byte[] directory = (byte[])block.Clone();
        Write32(directory.AsSpan(4), (uint)block.Length, true);
        byte[] count = (byte[])block.Clone();
        Write16(count.AsSpan(8), 400, true);
        return new TheoryData<string, byte[], MetadataErrorCode>
        {
            { "an empty Exif box", Box("Exif"), MetadataErrorCode.InvalidBmff },
            { "an Exif box shorter than its offset", Box("Exif", new byte[3]), MetadataErrorCode.InvalidBmff },
            { "an Exif box that is its offset alone", Box("Exif", new byte[4]), MetadataErrorCode.InvalidBmff },
            { "an offset that ends where the box ends", Box("Exif", Be32((uint)block.Length), block), MetadataErrorCode.InvalidBmff },
            { "an offset that leaves the box", Box("Exif", Be32(0x7FFF_FFF0), block), MetadataErrorCode.InvalidBmff },
            { "an offset of every bit", Box("Exif", Be32(0xFFFF_FFFF), block), MetadataErrorCode.InvalidBmff },
            { "an offset that leaves no room for a header", Box("Exif", Be32((uint)block.Length - 7), block), MetadataErrorCode.InvalidExif },
            { "a byte order mark that is neither", ExifBox(order), MetadataErrorCode.InvalidExif },
            { "another TIFF identifier", ExifBox(magic), MetadataErrorCode.InvalidExif },
            { "a BigTIFF identifier", ExifBox(big), MetadataErrorCode.UnsupportedFeature },
            { "a directory outside the block", ExifBox(directory), MetadataErrorCode.InvalidExif },
            { "a directory longer than the block", ExifBox(count), MetadataErrorCode.InvalidExif },
            { "a block cut short", ExifBox(block[..^6]), MetadataErrorCode.InvalidExif },
            { "an empty XMP box", Box("xml "), MetadataErrorCode.InvalidXmp },
            { "an XMP box of zero bytes", XmlBox(new byte[64]), MetadataErrorCode.InvalidXmp },
            { "an XML document that is no packet", XmlBox("<note>text</note>"u8.ToArray()), MetadataErrorCode.InvalidXmp },
            { "a packet cut short", XmlBox(Packet("cut")[..^100]), MetadataErrorCode.InvalidXmp },
        };
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedMetadataBoxIsRefused(string what, byte[] box, MetadataErrorCode code)
    {
        Assert.True(code == Refusal(Container(box)).Code, what);
        Assert.True(code == Refusal(Boxes(CodestreamBox(), Box("jumb", new byte[8]), box)).Code, what);
    }

    [Fact]
    public void ARefusalOfTheBlockSaysWhereInTheFile()
    {
        byte[] order = Block();
        order[1] = 0x4D;

        // The payload starts 8 bytes into the first box; the block 4 bytes later, or 10 with an identifier.
        Assert.Equal(First + 8L, Refusal(Container(Box("Exif", new byte[3]))).SourceOffset);
        Assert.Equal(First + 12L, Refusal(Container(ExifBox(order))).SourceOffset);
        Assert.Equal(First + 18L, Refusal(Container(ExifBox(order, "Exif\0\0"u8.ToArray()))).SourceOffset);
    }

    [Fact]
    public void TheRetainedBudgetBoundsTheBlockAndThePacketTogether()
    {
        byte[] block = Block(), packet = Packet("budget");
        byte[] both = Container(XmlBox(packet), ExifBox(block));
        var limits = new MetadataLimits();
        // The payload of the Exif box counts as its bytes and its values again, the packet as its bytes.
        int values = Exif(both, limits).Entries.Sum(entry => entry.Value.ByteLength);
        int exif = 4 + block.Length + values;

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(both, limits with { MaxRetainedMetadataBytes = exif + packet.Length - 1 }).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(both, limits with { MaxRetainedMetadataBytes = exif }).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(both, limits with { MaxRetainedMetadataBytes = 4 }).Code);
        JxlMetadata tight = JxlMetadata.Read(both, limits with { MaxRetainedMetadataBytes = exif + packet.Length });
        Assert.NotNull(tight.Exif);
        Assert.NotNull(tight.Xmp);

        byte[] alone = Container(ExifBox(block));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(alone, limits with { MaxRetainedMetadataBytes = exif - 1 }).Code);
        Assert.Equal(7, Exif(alone, limits with { MaxRetainedMetadataBytes = exif }).Entries.Count);
        byte[] only = Container(XmlBox(packet));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(only, limits with { MaxRetainedMetadataBytes = packet.Length - 1 }).Code);
        Assert.NotNull(JxlMetadata.Read(only, limits with { MaxRetainedMetadataBytes = packet.Length }).Xmp);
        // The codestream is not metadata: its bytes are never counted.
        byte[] large = Boxes(Box("jxlc", Codestream(4096)), ExifBox(block));
        Assert.Equal(7, Exif(large, limits with { MaxRetainedMetadataBytes = exif }).Entries.Count);
    }

    [Fact]
    public void TheEntryAndValueBudgetsBoundTheBlock()
    {
        byte[] file = Container(ExifBox(Block()));
        var limits = new MetadataLimits();

        // Four entries in the primary directory and three in the Exif directory.
        Assert.Equal(7, Exif(file, limits with { MaxTotalEntries = 7 }).Entries.Count);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxTotalEntries = 6 }).Code);
        Assert.Equal(7, Exif(file, limits with { MaxEntriesPerDirectory = 4 }).Entries.Count);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxEntriesPerDirectory = 3 }).Code);
        // The longest value is the date, twenty bytes.
        Assert.Equal(7, Exif(file, limits with { MaxValueBytes = 20 }).Entries.Count);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxValueBytes = 19 }).Code);
    }

    [Fact]
    public void ABlockLongerThanAJpegSegmentHoldsIsReadWithinTheDefaultBudgets()
    {
        // 70,000 bytes of one value: more than the 65,527 an APP1 segment has room for.
        byte[] large = TiffBlock(true, Text(0x010F, "Maker"), Raw(0x9286, 7, new byte[70_000]));

        ExifMetadata exif = Exif(Container(ExifBox(large)));

        Assert.Equal(70_000, Bytes(exif, Primary, 0x9286).Length);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(Container(ExifBox(large)), new MetadataLimits()).Code);
    }

    [Fact]
    public void ThePacketBudgetOfTheXmpReaderApplies()
    {
        byte[] packet = Packet("budget");
        byte[] file = Container(XmlBox(packet));
        XmpReadOptions Options(int bytes) => new() { Limits = new XmpLimits { MaxPacketBytes = bytes } };

        Assert.NotNull(JxlReader.Read(file, null, Options(packet.Length)).Xmp);
        MetadataException refusal = Assert.Throws<MetadataException>(() => JxlReader.Read(file, null, Options(packet.Length - 1)));
        Assert.Equal(MetadataErrorCode.LimitExceeded, refusal.Code);
    }

    [Fact]
    public void EveryTruncationIsReadOrRefused()
    {
        byte[] file = Boxes(ExifBox(Block()), Box("jumb", new byte[9]), CodestreamBox(), XmlBox(Packet("cut")));
        for (int length = 0; length < file.Length; length++)
        {
            try { JxlMetadata.Read(file.AsSpan(0, length)); }
            catch (MetadataException refusal) { Assert.Equal(MetadataErrorCode.InvalidBmff, refusal.Code); }
        }
    }

    [Fact]
    public void ADamagedByteIsReadOrRefused()
    {
        byte[] file = Boxes(ExifBox(Block()), CodestreamBox(), XmlBox(Packet("damage")));
        int read = 0, refused = 0;
        foreach (byte damage in new byte[] { 0x00, 0x01, 0x7F, 0xFF })
            for (int at = 0; at < file.Length; at++)
            {
                byte[] copy = (byte[])file.Clone();
                copy[at] = damage;
                try
                {
                    JxlMetadata.Read(copy);
                    read++;
                }
                catch (MetadataException)
                {
                    refused++;
                }
            }
        Assert.True(read > 0 && refused > 0);
    }
}
