using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Jxl;
using Xunit;
using static MetadataSharp.Tests.Cr3.Cr3Fixture;
using static MetadataSharp.Tests.Jxl.JxlFixture;

namespace MetadataSharp.Tests.Jxl;

/// <summary>The <c>brob</c> box: a metadata box whose payload is stored as one Brotli stream.</summary>
public sealed class JxlCompressedTests
{
    private static readonly XmpPropertyName Title = new("urn:owned:test", "title");
    private static readonly byte[] NoStream = Enumerable.Repeat((byte)0xFF, 16).ToArray();

    private static byte[] Pack(bool stored, byte[] payload) => stored ? Stored(payload) : Squeezed(payload);

    private static byte[] Bytes(ExifValue value)
    {
        var bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return bytes;
    }

    private static MetadataException Refusal(byte[] file, MetadataLimits? limits = null) =>
        Assert.Throws<MetadataException>(() => JxlMetadata.Read(file, limits));

    [Fact]
    public void TheHandWrittenStreamIsWhatItsDescriptionSays()
    {
        // One byte: the window bit, a header of three bytes for a length of one, the byte, the end.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x10, 0x41, 0x03 }, Stored([0x41]));
        Assert.Equal(new byte[] { 0x06 }, Stored([]));
        Assert.Equal(70_000 + 3 + 3 + 1, Stored(new byte[70_000]).Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACompressedExifBoxIsReadAsThePlainOne(bool stored)
    {
        byte[] payload = ExifPayload(Block(), "Exif\0\0"u8.ToArray());
        ExifMetadata plain = JxlMetadata.Read(Container(Box("Exif", payload))).Exif!;

        ExifMetadata? read = JxlMetadata.Read(Container(Brob("Exif", Pack(stored, payload)))).Exif;

        Assert.NotNull(read);
        Assert.Equal(plain.ByteOrder, read.ByteOrder);
        Assert.Equal(plain.IsEditable, read.IsEditable);
        Assert.Equal(7, read.Entries.Count);
        Assert.Equal(plain.Entries.Select(entry => entry.Key), read.Entries.Select(entry => entry.Key));
        Assert.Equal(plain.Entries.Select(entry => Bytes(entry.Value)), read.Entries.Select(entry => Bytes(entry.Value)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACompressedXmlBoxIsReadAsThePlainOne(bool stored)
    {
        byte[] packet = Packet("squeezed");

        JxlMetadata metadata = JxlMetadata.Read(Container(Brob("xml ", Pack(stored, packet))));

        Assert.Null(metadata.Exif);
        Assert.NotNull(metadata.Xmp);
        Assert.Equal("squeezed", metadata.Xmp.Properties[Title].Text);
        // The packet is the bytes the stream decodes to, not the bytes of the file.
        var raw = new byte[metadata.Xmp.ByteLength];
        metadata.Xmp.CopyRawTo(raw);
        Assert.Equal(packet, raw);
    }

    [Fact]
    public void CompressedAndPlainBoxesMayStandSideBySide()
    {
        byte[] exif = ExifPayload(Block()), packet = Packet("mixed");

        JxlMetadata first = JxlMetadata.Read(Boxes(Brob("Exif", Stored(exif)), CodestreamBox(), XmlBox(packet)));
        JxlMetadata second = JxlMetadata.Read(Boxes(Brob("xml ", Squeezed(packet)), Box("Exif", exif), Brob("jumb", NoStream), CodestreamBox()));

        Assert.Equal(7, first.Exif!.Entries.Count);
        Assert.Equal("mixed", first.Xmp!.Properties[Title].Text);
        Assert.False(first.HasJumbf);
        Assert.Equal(7, second.Exif!.Entries.Count);
        Assert.Equal("mixed", second.Xmp!.Properties[Title].Text);
        Assert.True(second.HasJumbf);
    }

    [Fact]
    public void TheStreamOfACompressedBoxStartsAfterTheTypeItStandsFor()
    {
        byte[] stream = Stored(ExifPayload(Block())), packet = Squeezed(Packet("where"));

        JxlLayout found = JxlReader.Locate(Container(Brob("Exif", stream), Brob("xml ", packet)));

        Assert.Equal(new JxlBlock(First + 8 + 4, stream.Length, true), found.Exif);
        Assert.Equal(new JxlBlock(First + 8 + 4 + stream.Length + 8 + 4, packet.Length, true), found.Xmp);
        // A compressed box that ends with its type is found, and holds an empty stream.
        Assert.Equal(new JxlBlock(First + 8 + 4, 0, true), JxlReader.Locate(Boxes(Brob("xml "))).Xmp);
    }

    [Fact]
    public void ACompressedJumbfBoxIsNoticedWithoutBeingDecoded()
    {
        // No stream would decode from these bytes; nobody tries.
        Assert.True(JxlMetadata.Read(Container(Brob("jumb", NoStream))).HasJumbf);
        Assert.True(JxlMetadata.Read(Container(Brob("jumb"))).HasJumbf);
        Assert.True(JxlReader.Locate(Container(Brob("jumb", NoStream), Box("jumb", new byte[4]))).HasJumbf);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("jxlc")]
    [InlineData("jxll")]
    [InlineData("brob")]
    [InlineData("exif")]
    [InlineData("XML ")]
    [InlineData("uuid")]
    public void ACompressedBoxOfAnotherTypeIsSteppedOverAndNeverDecoded(string type)
    {
        JxlMetadata metadata = JxlMetadata.Read(Container(Brob(type, NoStream), Brob(type)));

        Assert.Null(metadata.Exif);
        Assert.Null(metadata.Xmp);
        Assert.False(metadata.HasJumbf);
    }

    [Fact]
    public void ACompressedBoxAndAPlainOneOfTheSameTypeAreTheSameBoxTwice()
    {
        byte[] exif = ExifPayload(Block()), packet = Packet("twice");
        byte[][] files =
        [
            Container(Box("Exif", exif), Brob("Exif", Stored(exif))),
            Container(Brob("Exif", Stored(exif)), Box("Exif", exif)),
            Container(Brob("Exif", Stored(exif)), Brob("Exif", Squeezed(exif))),
            Container(XmlBox(packet), Brob("xml ", Stored(packet))),
            Boxes(Brob("xml ", Stored(packet)), CodestreamBox(), Brob("xml ", NoStream)),
        ];

        foreach (byte[] file in files)
        {
            Assert.Equal(MetadataErrorCode.InvalidBmff, Refusal(file).Code);
            Assert.Equal(MetadataErrorCode.InvalidBmff, Assert.Throws<MetadataException>(() => JxlReader.Locate(file)).Code);
        }
        // The second box is refused where it starts.
        byte[] plain = Box("Exif", exif);
        Assert.Equal((long)First + plain.Length, Refusal(files[0]).SourceOffset);
    }

    public static TheoryData<string, byte[], MetadataErrorCode> Malformed()
    {
        byte[] exif = ExifPayload(Block()), packet = Packet("damage");
        byte[] stored = Stored(exif), squeezed = Squeezed(packet);
        byte[] order = Block();
        order[0] = 0x4A;
        return new TheoryData<string, byte[], MetadataErrorCode>
        {
            { "a compressed box without a type", Box("brob"), MetadataErrorCode.InvalidBmff },
            { "a compressed box with three bytes of a type", Box("brob", "Exi"u8.ToArray()), MetadataErrorCode.InvalidBmff },
            { "an Exif stream of no bytes", Brob("Exif"), MetadataErrorCode.InvalidBmff },
            { "an XMP stream of no bytes", Brob("xml "), MetadataErrorCode.InvalidBmff },
            { "bytes that are no stream", Brob("Exif", NoStream), MetadataErrorCode.InvalidBmff },
            { "a stream without its last byte", Brob("Exif", stored[..^1]), MetadataErrorCode.InvalidBmff },
            { "a stream cut in its payload", Brob("Exif", stored[..^20]), MetadataErrorCode.InvalidBmff },
            { "a stream cut after its header", Brob("Exif", stored[..3]), MetadataErrorCode.InvalidBmff },
            { "a compressed stream cut short", Brob("xml ", squeezed[..^4]), MetadataErrorCode.InvalidBmff },
            { "a byte after the stream", Brob("Exif", stored, new byte[1]), MetadataErrorCode.InvalidBmff },
            { "a second stream after the stream", Brob("xml ", squeezed, squeezed), MetadataErrorCode.InvalidBmff },
            { "a stream of nothing for the Exif box", Brob("Exif", Stored([])), MetadataErrorCode.InvalidBmff },
            { "a stream of three bytes for the Exif box", Brob("Exif", Stored(new byte[3])), MetadataErrorCode.InvalidBmff },
            { "an offset that leaves the decoded payload", Brob("Exif", Stored(Join(Be32(4096), Block()))), MetadataErrorCode.InvalidBmff },
            { "a decoded block with another byte order mark", Brob("Exif", Squeezed(ExifPayload(order))), MetadataErrorCode.InvalidExif },
            { "a decoded block cut short", Brob("Exif", Stored(exif[..^6])), MetadataErrorCode.InvalidExif },
            { "a stream of nothing for the XMP box", Brob("xml ", Stored([])), MetadataErrorCode.InvalidXmp },
            { "a decoded document that is no packet", Brob("xml ", Squeezed("<note>text</note>"u8.ToArray())), MetadataErrorCode.InvalidXmp },
            { "a decoded packet cut short", Brob("xml ", Stored(packet[..^100])), MetadataErrorCode.InvalidXmp },
        };
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedCompressedBoxIsRefused(string what, byte[] box, MetadataErrorCode code)
    {
        Assert.True(code == Refusal(Container(box)).Code, what);
        Assert.True(code == Refusal(Boxes(CodestreamBox(), Box("jumb", new byte[8]), box)).Code, what);
    }

    [Fact]
    public void ARefusalOfACompressedBoxPointsAtTheStream()
    {
        byte[] order = Block();
        order[0] = 0x4A;
        byte[] stored = Stored(ExifPayload(Block()));

        // The stream starts 8 bytes of box header and 4 bytes of type into the first box. A place in
        // the decoded bytes is not a place in the file, so a refusal of the block names the stream.
        Assert.Equal(First + 12L, Refusal(Container(Brob("Exif", Stored(ExifPayload(order))))).SourceOffset);
        Assert.Equal(First + 12L, Refusal(Container(Brob("Exif", NoStream))).SourceOffset);
        Assert.Equal(First + 12L, Refusal(Container(Brob("Exif", Stored(new byte[2])))).SourceOffset);
        // The byte after the stream is in the file, and is named.
        Assert.Equal(First + 12L + stored.Length, Refusal(Container(Brob("Exif", stored, new byte[5]))).SourceOffset);
        Assert.Equal((long)First, Refusal(Container(Box("brob", new byte[2]))).SourceOffset);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheRetainedBudgetCountsWhatAStreamDecodesTo(bool stored)
    {
        byte[] exif = ExifPayload(Block()), packet = Packet("budget");
        byte[] both = Container(Brob("xml ", Pack(stored, packet)), Brob("Exif", Pack(stored, exif)));
        var limits = new MetadataLimits();
        int values = JxlMetadata.Read(both, limits).Exif!.Entries.Sum(entry => entry.Value.ByteLength);
        // As for plain boxes: the payload of the Exif box and its values again, then the packet.
        int block = exif.Length + values;

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(both, limits with { MaxRetainedMetadataBytes = block + packet.Length - 1 }).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(both, limits with { MaxRetainedMetadataBytes = block }).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(both, limits with { MaxRetainedMetadataBytes = exif.Length - 1 }).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(both, limits with { MaxRetainedMetadataBytes = 1 }).Code);
        JxlMetadata tight = JxlMetadata.Read(both, limits with { MaxRetainedMetadataBytes = block + packet.Length });
        Assert.Equal(7, tight.Exif!.Entries.Count);
        Assert.NotNull(tight.Xmp);

        byte[] alone = Container(Brob("Exif", Pack(stored, exif)));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(alone, limits with { MaxRetainedMetadataBytes = block - 1 }).Code);
        Assert.Equal(7, JxlMetadata.Read(alone, limits with { MaxRetainedMetadataBytes = block }).Exif!.Entries.Count);
        byte[] only = Container(Brob("xml ", Pack(stored, packet)));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(only, limits with { MaxRetainedMetadataBytes = packet.Length - 1 }).Code);
        Assert.NotNull(JxlMetadata.Read(only, limits with { MaxRetainedMetadataBytes = packet.Length }).Xmp);
    }

    [Fact]
    public void ASmallStreamThatDecodesToALargePayloadIsHeldToTheBudgetOfThePayload()
    {
        // Two million zero bytes as one value: a few dozen bytes once compressed.
        byte[] large = ExifPayload(TiffBlock(true, Text(0x010F, "Maker"), Raw(0x9286, 7, new byte[2_000_000])));
        byte[] stream = Squeezed(large);
        byte[] file = Container(Brob("Exif", stream));
        Assert.True(stream.Length < 4096);

        // The default budgets are those of a whole file, and have room for it.
        Assert.Equal(2, JxlMetadata.Read(file).Exif!.Entries.Count);
        // One mebibyte retained: the stream is given up when it has produced that much.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits()).Code);
        var roomy = new MetadataLimits { MaxRetainedMetadataBytes = 8_000_000 };
        // Room for the payload, and the value budget of an EXIF block in a JPEG segment: the value is refused.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, roomy).Code);
        Assert.Equal(2, JxlMetadata.Read(file, roomy with { MaxValueBytes = 2_000_000 }).Exif!.Entries.Count);
    }

    [Fact]
    public void AStreamOfZeroBytesWithoutEndIsGivenUpAtTheBudget()
    {
        // Eight mebibytes that are no metadata at all, under the type of either box.
        byte[] stream = Squeezed(new byte[8 * 1024 * 1024]);
        var limits = new MetadataLimits();

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(Container(Brob("Exif", stream)), limits).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(Container(Brob("xml ", stream)), limits).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(Container(Brob("xml ", stream)), limits with { MaxRetainedMetadataBytes = 16 }).Code);
        // With room for all of it the bytes are read, and are not what the box says they are.
        var roomy = limits with { MaxRetainedMetadataBytes = 16 * 1024 * 1024 };
        Assert.Equal(MetadataErrorCode.InvalidExif, Refusal(Container(Brob("Exif", stream)), roomy).Code);
    }

    [Fact]
    public void EveryTruncationIsReadOrRefused()
    {
        byte[] file = Boxes(Brob("Exif", Stored(ExifPayload(Block()))), CodestreamBox(), Brob("xml ", Squeezed(Packet("cut"))));
        for (int length = 0; length < file.Length; length++)
        {
            try { JxlMetadata.Read(file.AsSpan(0, length)); }
            catch (MetadataException refusal) { Assert.Equal(MetadataErrorCode.InvalidBmff, refusal.Code); }
        }
    }

    [Fact]
    public void ADamagedByteIsReadOrRefused()
    {
        byte[] file = Boxes(Brob("Exif", Stored(ExifPayload(Block()))), CodestreamBox(), Brob("xml ", Squeezed(Packet("damage"))));
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
