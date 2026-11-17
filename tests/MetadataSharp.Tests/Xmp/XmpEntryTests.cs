using System.Text;
using Xunit;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Tests.Xmp;

public sealed class XmpEntryTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>One description whose namespaces are declared on the RDF root: p and q.</summary>
    private static byte[] Xml(string content, string attributes = "") => Encoding.UTF8.GetBytes(
        $"<rdf:RDF xmlns:rdf='{Rdf}' xmlns:p='urn:owned:test' xmlns:q='urn:owned:other'><rdf:Description rdf:about='' {attributes}>{content}</rdf:Description></rdf:RDF>");

    /// <summary>
    /// One line per entry: <c>prefix.path=Kind:values</c>, with a star after the kind when the entry
    /// has child entries, and <c>language=text</c> items for a language alternative.
    /// </summary>
    private static string[] Rows(XmpPacket packet) => packet.Entries.Select(entry =>
        entry.Prefix + "." + entry.Path + "=" + entry.Kind + (entry.HasChildEntries ? "*" : "") + ":" +
        (entry.Kind == XmpEntryKind.LangAlt
            ? string.Join("|", entry.Languages.Select(item => item.Language + "=" + item.Value))
            : string.Join("|", entry.Values))).ToArray();

    private static string[] Rows(string content, string attributes = "") => Rows(XmpPacket.Parse(Xml(content, attributes)));

    private static byte[] Raw(XmpPacket packet) { byte[] b = new byte[packet.ByteLength]; packet.CopyRawTo(b); return b; }

    [Fact]
    public void ElementAndAttributeFormsGiveTheSameEntries()
    {
        string[] expected = ["p.title=Text:one", "p.count=Text:2"];
        Assert.Equal(expected, Rows("<p:title>one</p:title><p:count>2</p:count>"));
        Assert.Equal(expected, Rows("", "p:title='one' p:count='2'"));
        Assert.Equal(expected, Rows("<p:count>2</p:count>", "p:title='one'"));

        XmpEntry entry = XmpPacket.Parse(Xml("", "p:title='Привет &amp; 世界'")).Entries.Single();
        Assert.Equal("title", entry.Path);
        Assert.Equal("urn:owned:test", entry.NamespaceUri);
        Assert.Equal("p", entry.Prefix);
        Assert.Equal(XmpEntryKind.Text, entry.Kind);
        Assert.False(entry.HasChildEntries);
        Assert.Equal(new[] { "Привет & 世界" }, entry.Values);
        Assert.Empty(entry.Languages);
        Assert.Throws<NotSupportedException>(() => { ((IList<string>)entry.Values)[0] = "bad"; });
    }

    [Theory]
    [InlineData("Bag", XmpEntryKind.Bag)]
    [InlineData("Seq", XmpEntryKind.Seq)]
    [InlineData("Alt", XmpEntryKind.Alt)]
    public void AnArrayOfPlainTextIsOneEntryWithItsItemsInOrder(string form, XmpEntryKind kind)
    {
        XmpPacket packet = XmpPacket.Parse(Xml(
            $"<p:list><rdf:{form}><rdf:li>z</rdf:li><rdf:li>a</rdf:li><rdf:li>a</rdf:li><rdf:li/></rdf:{form}></p:list><p:none><rdf:{form}/></p:none>"));
        Assert.Equal(new[] { $"p.list={form}:z|a|a|", $"p.none={form}:" }, Rows(packet));
        Assert.Equal(kind, packet.Entries[0].Kind);
        Assert.Equal(new[] { "z", "a", "a", "" }, packet.Entries[0].Values);
        Assert.False(packet.Entries[1].HasChildEntries);
        Assert.Empty(packet.Entries[1].Values);
    }

    [Fact]
    public void ALanguageAlternativeKeepsItsLanguagesInPacketOrder()
    {
        XmpPacket packet = XmpPacket.Parse(Xml(
            "<p:title><rdf:Alt><rdf:li xml:lang='fr-FR'>Bonjour</rdf:li><rdf:li xml:lang='x-default'>Hello</rdf:li></rdf:Alt></p:title>"));
        Assert.Equal(new[] { "p.title=LangAlt:fr-FR=Bonjour|x-default=Hello" }, Rows(packet));
        XmpEntry entry = packet.Entries.Single();
        Assert.Equal(new[] { "Bonjour", "Hello" }, entry.Values);
        Assert.Equal(2, entry.Languages.Count);
        Assert.Equal("fr-FR", entry.Languages[0].Language);
        Assert.Equal("Hello", entry.Languages[1].Value);
        // The same property through the editable view, which this view does not replace.
        Assert.True(packet.IsEditable);
        Assert.Equal("Bonjour", packet.Properties[new("urn:owned:test", "title")].Languages["fr-FR"]);
    }

    [Fact]
    public void AnAlternativeWithAnUntaggedItemReportsItsItemsSeparately() =>
        Assert.Equal(
            new[] { "p.title=Alt*:", "p.title[1]=Text:a", "p.title[1]/?xml:lang=Text:en", "p.title[2]=Text:b" },
            Rows("<p:title><rdf:Alt><rdf:li xml:lang='en'>a</rdf:li><rdf:li>b</rdf:li></rdf:Alt></p:title>"));

    [Fact]
    public void ANestedStructureIsAnEntryPerFieldUnderItsPath()
    {
        XmpPacket packet = XmpPacket.Parse(Xml(
            "<p:place rdf:parseType='Resource'><q:city>Boston</q:city><q:geo><rdf:Description><q:lat>42</q:lat>" +
            "<q:tags><rdf:Bag><rdf:li>a</rdf:li><rdf:li>b</rdf:li></rdf:Bag></q:tags></rdf:Description></q:geo></p:place>"));
        Assert.Equal(
            new[]
            {
                "p.place=Struct*:", "p.place/q:city=Text:Boston", "p.place/q:geo=Struct*:",
                "p.place/q:geo/q:lat=Text:42", "p.place/q:geo/q:tags=Bag:a|b"
            },
            Rows(packet));
        Assert.All(packet.Entries, entry => { Assert.Equal("urn:owned:test", entry.NamespaceUri); Assert.Equal("p", entry.Prefix); });
        Assert.True(packet.Entries[0].HasChildEntries);
        Assert.Empty(packet.Entries[0].Values);
    }

    [Theory]
    [InlineData("<p:place rdf:parseType='Resource'><q:city>Boston</q:city><q:zip>02451</q:zip></p:place>")]
    [InlineData("<p:place><rdf:Description><q:city>Boston</q:city><q:zip>02451</q:zip></rdf:Description></p:place>")]
    [InlineData("<p:place><rdf:Description q:city='Boston' q:zip='02451'/></p:place>")]
    [InlineData("<p:place><rdf:Description q:city='Boston'><q:zip>02451</q:zip></rdf:Description></p:place>")]
    [InlineData("<p:place q:city='Boston' q:zip='02451'/>")]
    public void EverySyntaxOfAStructureGivesTheSameEntries(string content) =>
        Assert.Equal(new[] { "p.place=Struct*:", "p.place/q:city=Text:Boston", "p.place/q:zip=Text:02451" }, Rows(content));

    [Fact]
    public void ATypedNodeIsAStructureQualifiedByItsType() =>
        Assert.Equal(
            new[] { "p.shape=Struct*:", "p.shape/?rdf:type=Text:urn:owned:otherCircle", "p.shape/q:r=Text:2" },
            Rows("<p:shape><q:Circle q:r='2'/></p:shape>"));

    [Fact]
    public void AnArrayOfStructuresNumbersItsItemsFromOne()
    {
        XmpPacket packet = XmpPacket.Parse(Xml(
            "<p:history><rdf:Seq><rdf:li rdf:parseType='Resource'><q:action>created</q:action><q:when>2026</q:when></rdf:li>" +
            "<rdf:li q:action='saved' q:when='2027'/></rdf:Seq></p:history>"));
        Assert.Equal(
            new[]
            {
                "p.history=Seq*:",
                "p.history[1]=Struct*:", "p.history[1]/q:action=Text:created", "p.history[1]/q:when=Text:2026",
                "p.history[2]=Struct*:", "p.history[2]/q:action=Text:saved", "p.history[2]/q:when=Text:2027"
            },
            Rows(packet));
        Assert.Equal(XmpEntryKind.Seq, packet.Entries[0].Kind);
        Assert.Empty(packet.Entries[0].Values);
        Assert.True(packet.IsEditable);
    }

    [Fact]
    public void AnArrayOfArraysReportsEachInnerArrayAsAnItem() =>
        Assert.Equal(
            new[] { "p.grid=Seq*:", "p.grid[1]=Bag:a|b", "p.grid[2]=Bag:" },
            Rows("<p:grid><rdf:Seq><rdf:li><rdf:Bag><rdf:li>a</rdf:li><rdf:li>b</rdf:li></rdf:Bag></rdf:li><rdf:li><rdf:Bag/></rdf:li></rdf:Seq></p:grid>"));

    [Fact]
    public void QualifiersAreEntriesUnderAQuestionMarkStep()
    {
        Assert.Equal(
            new[] { "p.title=Text:Main", "p.title/?q:role=Text:heading" },
            Rows("<p:title rdf:parseType='Resource'><rdf:value>Main</rdf:value><q:role>heading</q:role></p:title>"));
        Assert.Equal(
            new[] { "p.title=Text:Main", "p.title/?q:role=Text:heading" },
            Rows("<p:title><rdf:Description><q:role>heading</q:role><rdf:value>Main</rdf:value></rdf:Description></p:title>"));
        Assert.Equal(
            new[] { "p.creator=Seq*:", "p.creator[1]=Text:first", "p.creator[2]=Text:second", "p.creator[2]/?q:role=Text:programmer" },
            Rows("<p:creator><rdf:Seq><rdf:li>first</rdf:li><rdf:li rdf:parseType='Resource'><rdf:value>second</rdf:value>" +
                "<q:role>programmer</q:role></rdf:li></rdf:Seq></p:creator>"));
        Assert.Equal(new[] { "p.note=Text:hi", "p.note/?xml:lang=Text:en" }, Rows("<p:note xml:lang='en'>hi</p:note>"));
    }

    [Fact]
    public void AResourceReferenceIsATextValueAndItsOtherAttributesQualifyIt()
    {
        Assert.Equal(new[] { "p.link=Text:urn:value" }, Rows("<p:link rdf:resource='urn:value'/>"));
        Assert.Equal(
            new[] { "p.link=Text:urn:value", "p.link/?q:kind=Text:home" },
            Rows("<p:link rdf:resource='urn:value' q:kind='home'/>"));
        Assert.Equal(
            new[] { "p.link=Text:text", "p.link/?rdf:resource=Text:urn:value", "p.link/?q:kind=Text:home" },
            Rows("<p:link rdf:value='text' rdf:resource='urn:value' q:kind='home'/>"));
        Assert.Equal(
            new[] { "p.links=Bag:urn:a|urn:b" },
            Rows("<p:links><rdf:Bag><rdf:li rdf:resource='urn:a'/><rdf:li rdf:resource='urn:b'/></rdf:Bag></p:links>"));
    }

    [Fact]
    public void APacketOutsideTheEditableProfileStillHasEntries()
    {
        byte[] input = Encoding.UTF8.GetBytes(
            $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='{Rdf}'>" +
            "<rdf:Description rdf:about='urn:subject' xmlns:p='urn:owned:test' p:title='first'>" +
            "<p:list><rdf:Seq><rdf:li>1</rdf:li><rdf:li>2</rdf:li></rdf:Seq></p:list></rdf:Description>" +
            "<rdf:Description rdf:about='urn:subject' xmlns:q='urn:owned:other' xmlns:p='urn:owned:test'>" +
            "<q:place rdf:parseType='Resource'><q:city>Boston</q:city></q:place><p:title>second</p:title></rdf:Description>" +
            "</rdf:RDF></x:xmpmeta>");
        XmpPacket packet = XmpPacket.Parse(input);
        Assert.False(packet.IsEditable);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => packet.Properties).Code);
        // The property repeated in the second description is reported once, as it first appears.
        Assert.Equal(
            new[] { "p.title=Text:first", "p.list=Seq:1|2", "q.place=Struct*:", "q.place/q:city=Text:Boston" },
            Rows(packet));
        Assert.Equal(input, Raw(packet));
    }

    [Fact]
    public void RdfThatIsNotAnXmpFormHasNoEntriesAndDoesNotRefuseThePacket()
    {
        XmpPacket packet = XmpPacket.Parse(Xml(
            "<p:literal rdf:parseType='Literal'><b>x</b></p:literal>" +
            "<p:two><rdf:Bag/><rdf:Seq/></p:two>" +
            "<p:mixed>text<rdf:Bag/></p:mixed>" +
            "<p:item><rdf:Bag><q:li>x</q:li></rdf:Bag></p:item>" +
            "<p:repeated rdf:parseType='Resource'><q:a>1</q:a><q:a>2</q:a></p:repeated>" +
            "<p:good>ok</p:good>"));
        Assert.False(packet.IsEditable);
        Assert.Equal(new[] { "p.good=Text:ok" }, Rows(packet));
    }

    [Theory]
    [InlineData("<rdf:Description rdf:about=''/><rdf:Description rdf:about=''>")]
    [InlineData("<rdf:Description rdf:about=''><p:a><rdf:Bag><rdf:li>x</rdf:Bag></p:a></rdf:Description><rdf:Description/>")]
    [InlineData("<rdf:Description rdf:about='' p:a='1' p:a='2'/><rdf:Description/>")]
    public void InvalidXmlIsStillRefused(string body) =>
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => XmpPacket.Parse(
            Encoding.UTF8.GetBytes($"<rdf:RDF xmlns:rdf='{Rdf}' xmlns:p='urn:owned:test'>{body}</rdf:RDF>"))).Code);

    [Fact]
    public void WellKnownNamespacesUseTheirConventionalPrefixAndStepsKeepThePacketPrefix()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(
            $"<rdf:RDF xmlns:rdf='{Rdf}' xmlns:xap='http://ns.adobe.com/xap/1.0/' xmlns:xapMM='http://ns.adobe.com/xap/1.0/mm/' " +
            "xmlns:Iptc4xmpCore='http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/' xmlns:stRef='http://ns.adobe.com/xap/1.0/sType/ResourceRef#' " +
            "xmlns:own='urn:owned:test'><rdf:Description rdf:about='' xap:Rating='4' own:note='n'>" +
            "<xapMM:DerivedFrom rdf:parseType='Resource'><stRef:documentID>d</stRef:documentID></xapMM:DerivedFrom>" +
            "<Iptc4xmpCore:CreatorContactInfo Iptc4xmpCore:CiAdrCity='Boston'/></rdf:Description></rdf:RDF>"));
        Assert.Equal(
            new[]
            {
                "xmp.Rating=Text:4", "own.note=Text:n",
                "xmpMM.DerivedFrom=Struct*:", "xmpMM.DerivedFrom/stRef:documentID=Text:d",
                "iptc.CreatorContactInfo=Struct*:", "iptc.CreatorContactInfo/Iptc4xmpCore:CiAdrCity=Text:Boston"
            },
            Rows(packet));
        Assert.Equal("http://ns.adobe.com/xap/1.0/", packet.Entries[0].NamespaceUri);
        Assert.Equal("http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/", packet.Entries[5].NamespaceUri);
    }

    [Fact]
    public void APacketThisLibraryWroteReadsBackUnderConventionalPrefixes()
    {
        var edit = new XmpEdit();
        edit.Set(new("http://purl.org/dc/elements/1.1/", "subject"), XmpValue.CreateArray(XmpValueKind.Bag, ["alpha", "beta"]));
        edit.Set(new("http://purl.org/dc/elements/1.1/", "title"), XmpValue.CreateLanguageAlternative(
            new Dictionary<string, string> { ["x-default"] = "Hello", ["fr-FR"] = "Bonjour" }));
        edit.Set(new("urn:owned:test", "note"), XmpValue.CreateText("n"));
        XmpPacket packet = XmpPacket.CreateEmpty().Apply(edit)!;
        Assert.True(packet.IsEditable);
        Assert.Equal(
            new[] { "dc.subject=Bag:alpha|beta", "dc.title=LangAlt:x-default=Hello|fr-FR=Bonjour", "ns1.note=Text:n" },
            Rows(packet));
        Assert.Equal(packet.Properties.Count, packet.Entries.Count);
        Assert.Empty(XmpPacket.CreateEmpty().Entries);
    }

    [Fact]
    public void NulPaddingAfterThePacketIsNotPartOfTheDocument()
    {
        byte[] plain = Encoding.UTF8.GetBytes(
            "<?xpacket begin='' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x='adobe:ns:meta/'>" +
            Encoding.UTF8.GetString(Xml("<p:title>x</p:title><p:list><rdf:Bag><rdf:li>a</rdf:li></rdf:Bag></p:list>")) +
            "</x:xmpmeta><?xpacket end='w'?>");
        byte[] padded = [.. plain, 0, 0, 0];
        XmpPacket packet = XmpPacket.Parse(padded);
        Assert.Equal(new[] { "p.title=Text:x", "p.list=Bag:a" }, Rows(packet));
        Assert.Equal(Rows(XmpPacket.Parse(plain)), Rows(packet));
        Assert.True(packet.IsEditable);
        Assert.Equal("x", packet.Properties[new("urn:owned:test", "title")].Text);
        // The bytes are kept as read, padding included.
        Assert.Equal(padded.Length, packet.ByteLength);
        Assert.Equal(padded, Raw(packet));
        Assert.Equal(padded, Raw(packet.Apply(new())!));
    }

    [Fact]
    public void NulsAnywhereElseAreStillInvalid()
    {
        byte[] plain = Xml("<p:title>x</p:title>");
        byte[] inside = (byte[])plain.Clone();
        inside[Array.LastIndexOf(plain, (byte)'x')] = 0; // the text of p:title
        byte[] before = [0, .. plain];
        byte[] between = [.. plain, 0, (byte)'\n'];
        foreach (byte[] input in new[] { inside, before, between, new byte[] { 0, 0, 0 } })
            Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => XmpPacket.Parse(input)).Code);
    }

    [Fact]
    public void TheTextOfThePathsIsBudgeted()
    {
        // Fifty short fields under one long name: each path repeats the long name, so the paths add
        // up to several times the size of the document that produced them.
        string name = new('s', 200);
        var fields = new StringBuilder();
        for (int i = 0; i < 50; i++) fields.Append("<p:a").Append(i).Append("/>");
        byte[] input = Xml($"<p:{name} rdf:parseType='Resource'>{fields}</p:{name}>");
        XmpPacket packet = XmpPacket.Parse(input);
        Assert.Equal(51, packet.Entries.Count);
        Assert.Equal(name + "/p:a49", packet.Entries[50].Path);
        Assert.True(packet.Entries.Sum(entry => entry.Path.Length) > input.Length);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => XmpPacket.Parse(
            input, new() { Limits = new() { MaxXmlCharacters = input.Length } })).Code);
    }
}
