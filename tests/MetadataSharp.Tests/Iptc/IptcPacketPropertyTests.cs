using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Iptc;

/// <summary>
/// The text properties of an IPTC stream: the six datasets GetText decodes, by its rules, with an
/// occurrence it refuses left out instead of thrown for.
/// </summary>
public sealed class IptcPacketPropertyTests
{
    private static readonly byte[] DeclaresUtf8 = IptcCodecTests.Dataset(1, 90, 0x1b, 0x25, 0x47);
    private static readonly byte[] DeclaresLatin1 = IptcCodecTests.Dataset(1, 90, 0x1b, 0x2d, 0x41);
    private static readonly byte[] Version = IptcCodecTests.Dataset(2, 0, 0, 4);

    private static byte[] Text(byte dataset, string text) => IptcCodecTests.Dataset(2, dataset, Encoding.UTF8.GetBytes(text));

    private static IptcPacket Packet(params byte[][] datasets) => IptcPacket.Parse(datasets.SelectMany(d => d).ToArray());

    private static byte[] Picture =>
    [
        .. DeclaresUtf8, .. Version,
        .. Text(5, "Harbour at dawn"),
        .. Text(25, "harbour"), .. Text(25, "\u6E2F"), .. Text(25, "Z\u00FCrich"),
        .. Text(80, "Ana Mart\u00EDn"), .. Text(80, "B. Ortega"),
        .. Text(105, "Boats leave before sunrise"),
        .. Text(116, "\u00A9 2024 Ana Mart\u00EDn"),
        .. Text(120, "Line one\r\nLine two"),
    ];

    [Fact]
    public void AStreamThatDeclaresUtf8AnswersEveryProperty()
    {
        IptcPacket packet = IptcPacket.Parse(Picture);
        Assert.Equal("Harbour at dawn", packet.ObjectName);
        Assert.Equal(new[] { "harbour", "\u6E2F", "Z\u00FCrich" }, packet.Keywords);
        Assert.Equal(new[] { "Ana Mart\u00EDn", "B. Ortega" }, packet.Bylines);
        Assert.Equal("Boats leave before sunrise", packet.Headline);
        Assert.Equal("\u00A9 2024 Ana Mart\u00EDn", packet.Copyright);
        Assert.Equal("Line one\r\nLine two", packet.Caption);
    }

    [Fact]
    public void AFileGivesTheSamePropertiesThroughImageMetadata()
    {
        byte[] file = JpegFixture.Build(JpegFixture.Segment(0xED, IptcCodecTests.Photoshop(IptcCodecTests.Block(0x0404, Picture))));
        ImageMetadata image = ImageMetadata.Read(file);
        Assert.Empty(image.Errors);
        IptcPacket packet = image.Iptc!;
        Assert.NotNull(packet);
        Assert.Equal("Harbour at dawn", packet.ObjectName);
        Assert.Equal(new[] { "harbour", "\u6E2F", "Z\u00FCrich" }, packet.Keywords);
        Assert.Equal(new[] { "Ana Mart\u00EDn", "B. Ortega" }, packet.Bylines);
        Assert.Equal("\u00A9 2024 Ana Mart\u00EDn", packet.Copyright);
        Assert.Equal("Line one\r\nLine two", packet.Caption);
    }

    [Fact]
    public void AStreamWithoutTheDatasetsAnswersNullAndEmpty()
    {
        // Datasets outside the six are not text properties, whatever they hold.
        foreach (IptcPacket packet in new[] { IptcPacket.CreateEmpty(), Packet(DeclaresUtf8, Version, Text(90, "Lisbon"), Text(101, "Portugal"), Text(15, "A")) })
        {
            Assert.Null(packet.ObjectName);
            Assert.Empty(packet.Keywords);
            Assert.Empty(packet.Bylines);
            Assert.Null(packet.Headline);
            Assert.Null(packet.Copyright);
            Assert.Null(packet.Caption);
        }
    }

    [Fact]
    public void WithoutADeclarationUsesOnePacketWideFallbackEncoding()
    {
        // One invalid UTF-8 text occurrence selects ISO-8859-1 for every IPTC text occurrence.
        IptcPacket packet = Packet(Version,
            Text(25, "plain"), IptcCodecTests.Dataset(2, 25, 0x5A, 0xFC, 0x72, 0x69, 0x63, 0x68), Text(25, "Z\u00FCrich"), Text(25, "also plain"),
            Text(80, "B. Ortega"),
            IptcCodecTests.Dataset(2, 120, 0x63, 0x61, 0x66, 0xE9), Text(120, "second caption"),
            Text(116, "(c) 2024"));
        Assert.Equal(IptcCharacterSet.Unspecified, packet.CharacterSet);
        Assert.Equal(new[] { "plain", "Z\u00FCrich", "Z\u00C3\u00BCrich", "also plain" }, packet.Keywords);
        Assert.Equal(new[] { "B. Ortega" }, packet.Bylines);
        Assert.Equal("(c) 2024", packet.Copyright);
        Assert.Equal("caf\u00E9", packet.Caption);
    }

    [Fact]
    public void AnotherDeclaredCharacterSetDoesNotOverrideContentDetection()
    {
        IptcPacket packet = Packet(DeclaresLatin1, Version, Text(5, "title"), Text(25, "plain"), Text(80, "name"),
            Text(105, "headline"), Text(116, "notice"), Text(120, "caption"));
        Assert.Equal(IptcCharacterSet.Unsupported, packet.CharacterSet);
        Assert.Equal(IptcTextEncoding.Ascii, packet.DetectedTextEncoding);
        Assert.Equal("title", packet.ObjectName);
        Assert.Equal(new[] { "plain" }, packet.Keywords);
        Assert.Equal(new[] { "name" }, packet.Bylines);
        Assert.Equal("headline", packet.Headline);
        Assert.Equal("notice", packet.Copyright);
        Assert.Equal("caption", packet.Caption);
    }

    [Fact]
    public void TextThatIsNotValidUtf8IsLeftOut()
    {
        IptcPacket packet = Packet(DeclaresUtf8, Version,
            Text(25, "first"),
            IptcCodecTests.Dataset(2, 25, 0x5A, 0xFC, 0x72),            // ISO 8859-1 under a UTF-8 declaration
            IptcCodecTests.Dataset(2, 25, 0xC3),                        // a sequence cut short
            IptcCodecTests.Dataset(2, 25, 0xED, 0xA0, 0x80),            // a surrogate
            IptcCodecTests.Dataset(2, 25, 0xC0, 0xAF),                  // an overlong form
            Text(25, "last"),
            IptcCodecTests.Dataset(2, 105, 0xFF));
        Assert.Equal(new[] { "first", "last" }, packet.Keywords);
        Assert.Null(packet.Headline);
    }

    [Fact]
    public void ASingleTextIsTheFirstOccurrenceAndAnEmptyOneIsEmpty()
    {
        IptcPacket packet = Packet(DeclaresUtf8, Version, Text(120, "first"), Text(120, "second"), Text(5, string.Empty), Text(25, string.Empty), Text(25, "x"));
        Assert.Equal("first", packet.Caption);
        Assert.Equal(string.Empty, packet.ObjectName);
        Assert.Equal(new[] { string.Empty, "x" }, packet.Keywords);
    }

    [Fact]
    public void EachPropertyAgreesWithGetTextOccurrenceByOccurrence()
    {
        byte[][] streams =
        [
            Picture,
            [.. Version, .. Text(25, "plain"), .. IptcCodecTests.Dataset(2, 25, 0xFC), .. Text(120, "caption"), .. IptcCodecTests.Dataset(2, 5, 0xE9)],
            [.. DeclaresLatin1, .. Version, .. Text(25, "plain"), .. Text(116, "notice")],
            [.. DeclaresUtf8, .. Version, .. IptcCodecTests.Dataset(2, 80, 0xC3), .. Text(80, "name"), .. IptcCodecTests.Dataset(2, 105, 0xED, 0xA0, 0x80)],
        ];
        (IptcDatasetKey Key, Func<IptcPacket, IReadOnlyList<string>> Read, bool Repeats)[] properties =
        [
            (new(2, 5), p => p.ObjectName is { } text ? [text] : [], false),
            (new(2, 25), p => p.Keywords, true),
            (new(2, 80), p => p.Bylines, true),
            (new(2, 105), p => p.Headline is { } text ? [text] : [], false),
            (new(2, 116), p => p.Copyright is { } text ? [text] : [], false),
            (new(2, 120), p => p.Caption is { } text ? [text] : [], false),
        ];
        foreach (byte[] stream in streams)
        {
            IptcPacket packet = IptcPacket.Parse(stream);
            foreach ((IptcDatasetKey key, Func<IptcPacket, IReadOnlyList<string>> read, bool repeats) in properties)
            {
                var expected = new List<string>();
                bool first = true;
                for (int i = 0; i < packet.Datasets.Count; i++)
                {
                    if (packet.Datasets[i].Key != key || (!repeats && !first))
                        continue;
                    first = false;
                    try { expected.Add(packet.GetText(i)); }
                    catch (MetadataException) { }
                }
                Assert.Equal(expected, read(packet));
            }
        }
    }

    [Fact]
    public void AListIsBuiltAnewForEachRead()
    {
        IptcPacket packet = IptcPacket.Parse(Picture);
        Assert.NotSame(packet.Keywords, packet.Keywords);
        Assert.Equal(packet.Keywords, packet.Keywords);
    }

    [Fact]
    public void AStreamReadForReadingOnlyAnswersToo()
    {
        // Zero padding after the last dataset: the strict rules refuse it, the read-only rules list it.
        byte[] stream = [.. Picture, 0, 0, 0, 0];
        byte[] file = JpegFixture.Build(JpegFixture.Segment(0xED, IptcCodecTests.Photoshop(IptcCodecTests.Block(0x0404, stream))));
        IptcPacket packet = ImageMetadata.Read(file).Iptc!;
        Assert.NotNull(packet);
        Assert.False(packet.IsEditable);
        Assert.Equal("Harbour at dawn", packet.ObjectName);
        Assert.Equal(new[] { "harbour", "\u6E2F", "Z\u00FCrich" }, packet.Keywords);
    }
}
