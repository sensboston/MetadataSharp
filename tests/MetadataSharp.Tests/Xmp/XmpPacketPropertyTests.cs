using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Tests.Png;
using Xunit;

namespace MetadataSharp.Tests.Xmp;

/// <summary>
/// The convenience properties of an XMP packet: Dublin Core title, description, creator, rights and
/// subject, and the XMP rating, read from the flat view so that a packet outside the editable profile
/// answers as well.
/// </summary>
public sealed class XmpPacketPropertyTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static byte[] Xml(string content, string attributes = "") => Encoding.UTF8.GetBytes(
        $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='{Rdf}' xmlns:dc='http://purl.org/dc/elements/1.1/' " +
        "xmlns:xmp='http://ns.adobe.com/xap/1.0/' xmlns:p='urn:owned:test'>" +
        $"<rdf:Description rdf:about='' {attributes}>{content}</rdf:Description></rdf:RDF></x:xmpmeta>");

    private static XmpPacket Packet(string content, string attributes = "") => XmpPacket.Parse(Xml(content, attributes));

    private static string Alt(string name, params (string Language, string Text)[] items) =>
        $"<{name}><rdf:Alt>" + string.Concat(items.Select(i => $"<rdf:li xml:lang='{i.Language}'>{i.Text}</rdf:li>")) + $"</rdf:Alt></{name}>";

    private static string Array(string name, string kind, params string[] items) =>
        $"<{name}><rdf:{kind}>" + string.Concat(items.Select(i => $"<rdf:li>{i}</rdf:li>")) + $"</rdf:{kind}></{name}>";

    private static string Picture =>
        Alt("dc:title", ("fr-FR", "Port \u00E0 l'aube"), ("x-default", "Harbour at dawn")) +
        Alt("dc:description", ("x-default", "Boats leave before sunrise")) +
        Array("dc:creator", "Seq", "Ana Mart\u00EDn", "B. Ortega") +
        Alt("dc:rights", ("x-default", "\u00A9 2024 Ana Mart\u00EDn")) +
        Array("dc:subject", "Bag", "harbour", "\u6E2F", "dawn") +
        "<xmp:Rating>4</xmp:Rating>";

    private static void AssertPicture(XmpPacket packet)
    {
        Assert.Equal("Harbour at dawn", packet.Title);
        Assert.Equal("Boats leave before sunrise", packet.Description);
        Assert.Equal(new[] { "Ana Mart\u00EDn", "B. Ortega" }, packet.Creators);
        Assert.Equal("\u00A9 2024 Ana Mart\u00EDn", packet.Rights);
        Assert.Equal(new[] { "harbour", "\u6E2F", "dawn" }, packet.Subjects);
        Assert.Equal(4.0, packet.Rating);
    }

    [Fact]
    public void APacketAnswersEveryProperty()
    {
        XmpPacket packet = Packet(Picture);
        Assert.True(packet.IsEditable);
        AssertPicture(packet);
    }

    [Fact]
    public void AFileGivesTheSamePropertiesThroughImageMetadata()
    {
        ImageMetadata image = ImageMetadata.Read(PngFixture.Build(PngFixture.Xmp(Xml(Picture))));
        Assert.Empty(image.Errors);
        Assert.NotNull(image.Xmp);
        AssertPicture(image.Xmp!);
    }

    [Fact]
    public void APacketThatIsNotEditableAnswersToo()
    {
        // A node identifier is RDF that is not written back: Properties throws, Entries lists.
        XmpPacket packet = Packet(Picture + "<p:place rdf:parseType='Resource'><p:city rdf:ID='c'>Lisbon</p:city><dc:title>inner</dc:title></p:place>");
        Assert.False(packet.IsEditable);
        Assert.Throws<MetadataException>(() => packet.Properties);
        AssertPicture(packet);
    }

    [Fact]
    public void APacketWithoutThePropertiesAnswersNullAndEmpty()
    {
        // The same local names in another namespace, and the names as fields of a structure, are other properties.
        foreach (XmpPacket packet in new[]
        {
            XmpPacket.CreateEmpty(),
            Packet("<p:title>mine</p:title><p:creator>me</p:creator><p:Rating>5</p:Rating><p:subject>x</p:subject>"),
            Packet("<p:place rdf:parseType='Resource'><dc:title>inner</dc:title><dc:creator>inner</dc:creator><xmp:Rating>5</xmp:Rating></p:place>"),
        })
        {
            Assert.Null(packet.Title);
            Assert.Null(packet.Description);
            Assert.Empty(packet.Creators);
            Assert.Null(packet.Rights);
            Assert.Empty(packet.Subjects);
            Assert.Null(packet.Rating);
        }
    }

    [Fact]
    public void ALanguageAlternativeGivesItsDefaultOtherwiseItsFirstItem()
    {
        Assert.Equal("default", Packet(Alt("dc:title", ("de", "erste"), ("x-default", "default"), ("fr", "troisi\u00E8me"))).Title);
        Assert.Equal("default", Packet(Alt("dc:title", ("x-default", "default"), ("de", "zweite"))).Title);
        Assert.Equal("erste", Packet(Alt("dc:title", ("de", "erste"), ("fr", "deuxi\u00E8me"))).Title);
        Assert.Equal("only", Packet(Alt("dc:rights", ("en-GB", "only"))).Rights);
        Assert.Equal(string.Empty, Packet(Alt("dc:description", ("x-default", string.Empty), ("de", "zweite"))).Description);
    }

    [Fact]
    public void ASimpleValueWhereAnArrayIsDefinedIsStillTheText()
    {
        // Writers that store these as plain text exist; the text is what the packet says.
        XmpPacket packet = Packet("<dc:title>plain title</dc:title><dc:creator>one author</dc:creator>", "dc:rights='plain rights' dc:subject='one subject'");
        Assert.Equal("plain title", packet.Title);
        Assert.Equal("plain rights", packet.Rights);
        Assert.Equal(new[] { "one author" }, packet.Creators);
        Assert.Equal(new[] { "one subject" }, packet.Subjects);
    }

    [Fact]
    public void AnArrayKeepsItsOrderAndItsDuplicates()
    {
        XmpPacket packet = Packet(Array("dc:subject", "Bag", "b", "a", "b", "") + Array("dc:creator", "Seq", "second", "first"));
        Assert.Equal(new[] { "b", "a", "b", "" }, packet.Subjects);
        Assert.Equal(new[] { "second", "first" }, packet.Creators);
        Assert.NotSame(packet.Subjects, packet.Subjects);
    }

    [Fact]
    public void AnArrayWhereTextIsDefinedAndAnEmptyArrayHaveNoText()
    {
        XmpPacket packet = Packet(Array("dc:title", "Bag", "a", "b") + "<dc:creator><rdf:Seq/></dc:creator><dc:rights><rdf:Alt/></dc:rights>");
        Assert.Null(packet.Title);
        Assert.Empty(packet.Creators);
        Assert.Null(packet.Rights);
        // A language alternative is not a list of creators.
        Assert.Empty(Packet(Alt("dc:creator", ("x-default", "a"))).Creators);
    }

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("5", 5.0)]
    [InlineData("-1", -1.0)]
    [InlineData("4.5", 4.5)]
    [InlineData("3.0", 3.0)]
    [InlineData(" 2 ", 2.0)]
    [InlineData("+1", 1.0)]
    [InlineData("99", 99.0)]
    public void ARatingIsTheNumberAsWritten(string stored, double expected)
    {
        Assert.Equal(expected, Packet($"<xmp:Rating>{stored}</xmp:Rating>").Rating);
        Assert.Equal(expected, Packet(string.Empty, $"xmp:Rating='{stored}'").Rating);
    }

    [Theory]
    [InlineData("")]
    [InlineData("five")]
    [InlineData("3,5")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0x3")]
    [InlineData("3 stars")]
    [InlineData("1,000")]
    public void TextThatIsNotAPlainDecimalNumberIsNotARating(string stored)
    {
        Assert.Null(Packet($"<xmp:Rating>{stored}</xmp:Rating>").Rating);
    }

    [Fact]
    public void ARatingThatIsAnArrayIsNotARating()
    {
        Assert.Null(Packet(Array("xmp:Rating", "Seq", "3")).Rating);
        Assert.Null(Packet(Array("xmp:Rating", "Seq", "3", "4")).Rating);
    }

    [Fact]
    public void ARatingReadsTheSameUnderAnyCulture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // A culture that writes the decimal point as a comma.
            var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ",";
            culture.NumberFormat.NumberGroupSeparator = ".";
            System.Globalization.CultureInfo.CurrentCulture = culture;
            Assert.Equal(4.5, Packet("<xmp:Rating>4.5</xmp:Rating>").Rating);
            Assert.Null(Packet("<xmp:Rating>4,5</xmp:Rating>").Rating);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }
}
