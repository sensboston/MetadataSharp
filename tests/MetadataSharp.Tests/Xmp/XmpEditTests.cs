using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using Xunit;

namespace MetadataSharp.Tests.Xmp;

/// <summary>
/// The edit model over paths: every node kind is set, changed and removed, in known and in custom
/// namespaces, and what an edit does not name is listed afterwards as it was before.
/// </summary>
public sealed class XmpEditTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static byte[] Xml(string content, string declarations = "") => Encoding.UTF8.GetBytes(
        $"<rdf:RDF xmlns:rdf='{Rdf}'><rdf:Description rdf:about='' xmlns:p='urn:owned:test' xmlns:q='urn:owned:other' {declarations}>{content}</rdf:Description></rdf:RDF>");

    private static string Text(XmpPacket packet)
    {
        byte[] bytes = new byte[packet.ByteLength];
        packet.CopyRawTo(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string Row(XmpEntry entry) =>
        entry.Key + "=" + entry.Kind + (entry.HasChildEntries ? "*" : string.Empty) + ":" +
        (entry.Kind == XmpEntryKind.LangAlt
            ? string.Join("|", entry.Languages.Select(item => item.Language + "=" + item.Value))
            : string.Join("|", entry.Values));

    private static string[] Rows(XmpPacket packet) => packet.Entries.Select(Row).ToArray();

    private static XmpPacket Apply(XmpPacket packet, Action<XmpEdit> change)
    {
        var edit = new XmpEdit();
        change(edit);
        XmpPacket result = packet.Apply(edit)!;
        // What was written is a packet of its own: it reads the same from its bytes.
        byte[] bytes = new byte[result.ByteLength];
        result.CopyRawTo(bytes);
        Assert.Equal(Rows(result), Rows(XmpPacket.Parse(bytes)));
        Assert.True(result.IsEditable);
        return result;
    }

    private static MetadataErrorCode Refusal(XmpPacket packet, Action<XmpEdit> change)
    {
        var edit = new XmpEdit();
        change(edit);
        return Assert.Throws<MetadataException>(() => packet.Apply(edit)).Code;
    }

    [Fact]
    public void SimplePropertiesAreSetUnderConventionalPrefixes()
    {
        XmpPacket packet = Apply(XmpPacket.CreateEmpty(), edit =>
        {
            edit.Set("Xmp.dc.format", "image/jpeg");
            edit.Set("Xmp.xmp.Rating", "5");
            edit.Set("Xmp.photoshop.City", "Lisboa & Porto <1>");
        });
        Assert.Equal(
            new[] { "Xmp.dc.format=Text:image/jpeg", "Xmp.xmp.Rating=Text:5", "Xmp.photoshop.City=Text:Lisboa & Porto <1>" },
            Rows(packet));
        string xml = Text(packet);
        Assert.Contains("xmlns:dc=\"http://purl.org/dc/elements/1.1/\"", xml, StringComparison.Ordinal);
        Assert.Contains("xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"", xml, StringComparison.Ordinal);
        Assert.Contains("<dc:format>image/jpeg</dc:format>", xml, StringComparison.Ordinal);
        Assert.Equal(5.0, packet.Rating);

        XmpPacket replaced = Apply(packet, edit => { edit.Set("Xmp.xmp.Rating", "3"); edit.RemovePath("Xmp.dc.format"); });
        Assert.Equal(new[] { "Xmp.xmp.Rating=Text:3", "Xmp.photoshop.City=Text:Lisboa & Porto <1>" }, Rows(replaced));
    }

    [Fact]
    public void ArrayItemsAreAppendedInsertedReplacedAndRemoved()
    {
        XmpPacket packet = Apply(XmpPacket.CreateEmpty(), edit =>
        {
            edit.Append("Xmp.dc.subject", XmpValueKind.Bag, "b");
            edit.Append("Xmp.dc.subject", XmpValueKind.Bag, "c");
            edit.Insert("Xmp.dc.subject", 1, "a");
        });
        Assert.Equal(new[] { "Xmp.dc.subject=Bag:a|b|c" }, Rows(packet));
        Assert.Equal(new[] { "a", "b", "c" }, packet.Subjects);

        packet = Apply(packet, edit =>
        {
            edit.Set("Xmp.dc.subject[2]", "B");
            edit.RemovePath("Xmp.dc.subject[1]");
            edit.Insert("Xmp.dc.subject", 3, "d");
            edit.Set("Xmp.dc.subject[last()]", "D");
        });
        Assert.Equal(new[] { "Xmp.dc.subject=Bag:B|c|D" }, Rows(packet));

        packet = Apply(packet, edit =>
        {
            edit.RegisterNamespace("p", "urn:owned:test");
            edit.Append("Xmp.dc.creator", XmpValueKind.Sequence, "Ana");
            edit.Append("Xmp.p.choice", XmpValueKind.Alternative, "first");
            edit.Append("Xmp.p.choice", XmpValueKind.Alternative, "second");
            edit.RemovePath("Xmp.dc.subject[last()]");
        });
        Assert.Equal(new[] { "Xmp.dc.subject=Bag:B|c", "Xmp.dc.creator=Seq:Ana", "Xmp.p.choice=Alt:first|second" }, Rows(packet));
    }

    [Fact]
    public void AnArrayIsReplacedWholeByAValue()
    {
        XmpPacket packet = XmpPacket.Parse(Xml("<p:list><rdf:Seq><rdf:li>1</rdf:li><rdf:li>2</rdf:li></rdf:Seq></p:list>"));
        packet = Apply(packet, edit => edit.SetValue("Xmp.p.list", XmpValue.CreateArray(XmpValueKind.Bag, ["x", "y", "z"])));
        Assert.Equal(new[] { "Xmp.p.list=Bag:x|y|z" }, Rows(packet));
        packet = Apply(packet, edit => edit.SetValue("Xmp.p.list", XmpValue.CreateArray(XmpValueKind.Alternative, ["only"])));
        Assert.Equal(new[] { "Xmp.p.list=Alt:only" }, Rows(packet));
        packet = Apply(packet, edit => edit.SetValue("Xmp.p.list", XmpValue.CreateLanguageAlternative(
            [new KeyValuePair<string, string>("de", "Hallo"), new KeyValuePair<string, string>("x-default", "Hello")])));
        Assert.Equal(new[] { "Xmp.p.list=LangAlt:x-default=Hello|de=Hallo" }, Rows(packet));
        packet = Apply(packet, edit => edit.Set("Xmp.p.list", "plain"));
        Assert.Equal(new[] { "Xmp.p.list=Text:plain" }, Rows(packet));
    }

    [Fact]
    public void ALanguageAlternativeIsEditedPerLanguageWithTheDefaultFirst()
    {
        XmpPacket packet = Apply(XmpPacket.CreateEmpty(), edit =>
        {
            edit.SetLanguage("Xmp.dc.title", "fr", "Bonjour");
            edit.SetLanguage("Xmp.dc.title", "x-default", "Hello");
            edit.SetLanguage("Xmp.dc.title", "de-DE", "Hallo");
        });
        Assert.Equal(new[] { "Xmp.dc.title=LangAlt:x-default=Hello|fr=Bonjour|de-DE=Hallo" }, Rows(packet));
        Assert.Equal("Hello", packet.Title);

        // Languages are compared without case; the item keeps the tag it has.
        packet = Apply(packet, edit => { edit.SetLanguage("Xmp.dc.title", "FR", "Salut"); edit.RemoveLanguage("Xmp.dc.title", "DE-de"); });
        Assert.Equal(new[] { "Xmp.dc.title=LangAlt:x-default=Hello|fr=Salut" }, Rows(packet));

        // No item is derived from another: removing x-default leaves the other language alone.
        packet = Apply(packet, edit => edit.RemoveLanguage("Xmp.dc.title", "x-default"));
        Assert.Equal(new[] { "Xmp.dc.title=LangAlt:fr=Salut" }, Rows(packet));
        Assert.Equal("Salut", packet.Title);

        // The last item takes the alternative with it.
        packet = Apply(packet, edit => edit.RemoveLanguage("Xmp.dc.title", "fr"));
        Assert.Empty(packet.Entries);
    }

    [Fact]
    public void StructureFieldsAreSetAndRemovedAndStructuresAreCreatedOnTheWay()
    {
        XmpPacket packet = Apply(XmpPacket.CreateEmpty(), edit =>
        {
            edit.Set("Xmp.xmpMM.DerivedFrom/stRef:documentID", "xmp.did:1");
            edit.Set("Xmp.xmpMM.DerivedFrom/stRef:instanceID", "xmp.iid:2");
            edit.Set("Xmp.iptc.CreatorContactInfo/Iptc4xmpCore:CiAdrCity", "Boston");
            edit.RegisterNamespace("q", "urn:owned:other");
            edit.Set("Xmp.q.outer/q:inner/q:leaf", "deep");
        });
        Assert.Equal(
            new[]
            {
                "Xmp.xmpMM.DerivedFrom=Struct*:",
                "Xmp.xmpMM.DerivedFrom/stRef:documentID=Text:xmp.did:1",
                "Xmp.xmpMM.DerivedFrom/stRef:instanceID=Text:xmp.iid:2",
                "Xmp.iptc.CreatorContactInfo=Struct*:",
                "Xmp.iptc.CreatorContactInfo/Iptc4xmpCore:CiAdrCity=Text:Boston",
                "Xmp.q.outer=Struct*:",
                "Xmp.q.outer/q:inner=Struct*:",
                "Xmp.q.outer/q:inner/q:leaf=Text:deep"
            },
            Rows(packet));
        string xml = Text(packet);
        Assert.Contains("xmlns:stRef=\"http://ns.adobe.com/xap/1.0/sType/ResourceRef#\"", xml, StringComparison.Ordinal);
        Assert.Contains("xmlns:Iptc4xmpCore=\"http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/\"", xml, StringComparison.Ordinal);

        // The keys the packet lists are the paths an edit takes.
        packet = Apply(packet, edit =>
        {
            edit.RemovePath(packet.Entries[1].Key);
            edit.Set(packet.Entries[4].Key, "Waltham");
            edit.RemovePath("Xmp.q.outer/q:inner");
        });
        Assert.Equal(
            new[]
            {
                "Xmp.xmpMM.DerivedFrom=Struct*:",
                "Xmp.xmpMM.DerivedFrom/stRef:instanceID=Text:xmp.iid:2",
                "Xmp.iptc.CreatorContactInfo=Struct*:",
                "Xmp.iptc.CreatorContactInfo/Iptc4xmpCore:CiAdrCity=Text:Waltham"
            },
            Rows(packet));
    }

    [Fact]
    public void AnArrayOfStructuresIsBuiltItemByItem()
    {
        XmpPacket packet = Apply(XmpPacket.CreateEmpty(), edit =>
        {
            edit.AppendStruct("Xmp.xmpMM.History", XmpValueKind.Sequence);
            edit.Set("Xmp.xmpMM.History[last()]/stEvt:action", "created");
            edit.AppendStruct("Xmp.xmpMM.History", XmpValueKind.Sequence);
            edit.Set("Xmp.xmpMM.History[last()]/stEvt:action", "saved");
            edit.Set("Xmp.xmpMM.History[1]/stEvt:when", "2026-11-12");
            edit.InsertStruct("Xmp.xmpMM.History", 1);
            edit.Set("Xmp.xmpMM.History[1]/stEvt:action", "derived");
        });
        Assert.Equal(
            new[]
            {
                "Xmp.xmpMM.History=Seq*:",
                "Xmp.xmpMM.History[1]=Struct*:",
                "Xmp.xmpMM.History[1]/stEvt:action=Text:derived",
                "Xmp.xmpMM.History[2]=Struct*:",
                "Xmp.xmpMM.History[2]/stEvt:action=Text:created",
                "Xmp.xmpMM.History[2]/stEvt:when=Text:2026-11-12",
                "Xmp.xmpMM.History[3]=Struct*:",
                "Xmp.xmpMM.History[3]/stEvt:action=Text:saved"
            },
            Rows(packet));

        packet = Apply(packet, edit =>
        {
            edit.RemovePath("Xmp.xmpMM.History[2]");
            edit.Set("Xmp.xmpMM.History[2]/stEvt:action", "exported");
            edit.RemovePath("Xmp.xmpMM.History[1]/stEvt:action");
        });
        Assert.Equal(
            new[]
            {
                "Xmp.xmpMM.History=Seq*:",
                "Xmp.xmpMM.History[1]=Struct*:",
                "Xmp.xmpMM.History[1]/stEvt:action=Text:exported"
            },
            Rows(packet));
    }

    [Fact]
    public void WhatAnEditEmptiesIsRemovedWithTheParentsThatBecomeEmptyAndWhatWasEmptyStays()
    {
        XmpPacket packet = XmpPacket.Parse(Xml(
            "<p:list><rdf:Bag><rdf:li>only</rdf:li></rdf:Bag></p:list>" +
            "<p:outer rdf:parseType='Resource'><p:inner rdf:parseType='Resource'><p:leaf>x</p:leaf></p:inner></p:outer>" +
            "<p:jobs><rdf:Seq><rdf:li rdf:parseType='Resource'><p:id>1</p:id></rdf:li></rdf:Seq></p:jobs>" +
            "<p:kept rdf:parseType='Resource'><p:a>1</p:a><p:b rdf:parseType='Resource'><p:c>2</p:c></p:b></p:kept>" +
            "<p:title><rdf:Alt><rdf:li xml:lang='x-default'>t</rdf:li></rdf:Alt></p:title>" +
            "<p:none><rdf:Bag/></p:none><p:hollow rdf:parseType='Resource'/>" +
            "<p:holder rdf:parseType='Resource'><p:void><rdf:Seq/></p:void><p:v>1</p:v></p:holder>"));
        XmpPacket changed = Apply(packet, edit =>
        {
            edit.RemovePath("Xmp.p.list[1]");
            edit.RemovePath("Xmp.p.outer/p:inner/p:leaf");
            edit.RemovePath("Xmp.p.jobs[last()]/p:id");
            edit.RemovePath("Xmp.p.kept/p:b/p:c");
            edit.RemoveLanguage("Xmp.p.title", "x-default");
            // Removing beside an empty array leaves the array, and the structure that still holds it.
            edit.RemovePath("Xmp.p.holder/p:v");
        });
        Assert.Equal(
            new[]
            {
                "Xmp.p.kept=Struct*:",
                "Xmp.p.kept/p:a=Text:1",
                "Xmp.p.none=Bag:",
                "Xmp.p.hollow=Struct*:",
                "Xmp.p.holder=Struct*:",
                "Xmp.p.holder/p:void=Seq:"
            },
            Rows(changed));

        // Nothing is asked of the empty ones, so nothing is removed.
        var none = new XmpEdit();
        none.RemovePath("Xmp.p.none[1]");
        none.RemovePath("Xmp.p.hollow/p:missing");
        Assert.Same(changed, changed.Apply(none));
    }

    [Fact]
    public void QualifiersAreSetOnValuesItemsStructuresAndArrays()
    {
        XmpPacket packet = Apply(XmpPacket.CreateEmpty(), edit =>
        {
            edit.RegisterNamespace("q", "urn:owned:other");
            edit.Append("Xmp.dc.creator", XmpValueKind.Sequence, "Ana");
            edit.Set("Xmp.dc.creator[1]/?q:role", "author");
            edit.Set("Xmp.dc.source", "archive");
            edit.Set("Xmp.dc.source/?xml:lang", "en");
            edit.Set("Xmp.q.place/q:city", "Boston");
            edit.Set("Xmp.q.place/?q:note", "home");
            edit.Append("Xmp.q.list", XmpValueKind.Bag, "one");
            edit.Set("Xmp.q.list/?q:note", "whole");
        });
        Assert.Equal(
            new[]
            {
                "Xmp.dc.creator=Seq*:",
                "Xmp.dc.creator[1]=Text:Ana",
                "Xmp.dc.creator[1]/?q:role=Text:author",
                "Xmp.dc.source=Text:archive",
                "Xmp.dc.source/?xml:lang=Text:en",
                "Xmp.q.place=Struct*:",
                "Xmp.q.place/?q:note=Text:home",
                "Xmp.q.place/q:city=Text:Boston",
                "Xmp.q.list=Bag*:",
                "Xmp.q.list/?q:note=Text:whole",
                "Xmp.q.list[1]=Text:one"
            },
            Rows(packet));

        // A value that is replaced keeps its qualifiers; a qualifier is removed by its path.
        packet = Apply(packet, edit =>
        {
            edit.Set("Xmp.dc.creator[1]", "Ana Mart\u00EDn");
            edit.Set("Xmp.dc.creator[1]/?q:role", "photographer");
            edit.RemovePath("Xmp.dc.source/?xml:lang");
            edit.RemovePath("Xmp.q.place/?q:note");
            edit.RemovePath("Xmp.q.list/?q:note");
        });
        Assert.Equal(
            new[]
            {
                "Xmp.dc.creator=Seq*:",
                "Xmp.dc.creator[1]=Text:Ana Mart\u00EDn",
                "Xmp.dc.creator[1]/?q:role=Text:photographer",
                "Xmp.dc.source=Text:archive",
                "Xmp.q.place=Struct*:",
                "Xmp.q.place/q:city=Text:Boston",
                "Xmp.q.list=Bag:one"
            },
            Rows(packet));
    }

    [Fact]
    public void ACustomNamespaceIsRegisteredAndAnUnknownPrefixOfTheSourceIsKept()
    {
        XmpPacket packet = Apply(XmpPacket.CreateEmpty(), edit =>
        {
            edit.RegisterNamespace("acme", "http://ns.acme.example/1.0/");
            edit.Set("Xmp.acme.Batch", "7");
            edit.Append("Xmp.acme.Steps", XmpValueKind.Sequence, "scan");
        });
        Assert.Equal(new[] { "Xmp.acme.Batch=Text:7", "Xmp.acme.Steps=Seq:scan" }, Rows(packet));
        Assert.Equal("http://ns.acme.example/1.0/", packet.Entries[0].NamespaceUri);
        Assert.Contains("xmlns:acme=\"http://ns.acme.example/1.0/\"", Text(packet), StringComparison.Ordinal);

        // The packet declares the prefix now: a later edit needs no registration.
        packet = Apply(packet, edit => edit.Set("Xmp.acme.Operator", "B. Ortega"));
        Assert.Equal("Xmp.acme.Operator=Text:B. Ortega", Rows(packet)[2]);

        // A prefix nothing declares is refused, and the packet is as it was.
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Set("Xmp.nobody.x", "1")));

        // A source that declares a namespace under a prefix of its own keeps that prefix, whatever an edit registers.
        XmpPacket source = XmpPacket.Parse(Xml("<zz:a>1</zz:a><xap:Rating>2</xap:Rating>", "xmlns:zz='urn:owned:zz' xmlns:xap='http://ns.adobe.com/xap/1.0/'"));
        source = Apply(source, edit =>
        {
            edit.RegisterNamespace("other", "urn:owned:zz");
            edit.Set("Xmp.other.b", "3");
            edit.Set("Xmp.zz.c", "4");
            edit.Set("Xmp.xmp.Label", "red");
        });
        Assert.Equal(new[] { "Xmp.zz.a=Text:1", "Xmp.xmp.Rating=Text:2", "Xmp.zz.b=Text:3", "Xmp.zz.c=Text:4", "Xmp.xmp.Label=Text:red" }, Rows(source));
        string xml = Text(source);
        Assert.Contains("<zz:b>3</zz:b>", xml, StringComparison.Ordinal);
        Assert.Contains("<xap:Label>red</xap:Label>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("xmlns:other", xml, StringComparison.Ordinal);

        // A prefix registered for a namespace other than the one the packet declares under it stands
        // for the registered one in the paths of the edit, and is written under a prefix that is free.
        XmpPacket clash = Apply(XmpPacket.Parse(Xml("<p:a>1</p:a>")), edit =>
        {
            edit.RegisterNamespace("p", "urn:owned:elsewhere");
            edit.Set("Xmp.p.b", "2");
        });
        Assert.Equal(new[] { "urn:owned:test|a|1", "urn:owned:elsewhere|b|2" },
            clash.Entries.Select(entry => entry.NamespaceUri + "|" + entry.Path + "|" + entry.Values[0]).ToArray());
    }

    [Fact]
    public void WhatAnEditDoesNotNameKeepsItsValuesStructureAndPrefixes()
    {
        XmpPacket packet = XmpPacket.Parse(Xml(
            "<p:plain>text  with  spaces </p:plain>" +
            "<p:link rdf:resource='urn:value'/>" +
            "<p:qualified rdf:parseType='Resource'><rdf:value>v</rdf:value><q:kind>k</q:kind></p:qualified>" +
            "<p:place><rdf:Description q:city='Boston'><q:zip>02451</q:zip></rdf:Description></p:place>" +
            "<p:shape><q:Circle q:r='2'/></p:shape>" +
            "<p:history><rdf:Seq><rdf:li rdf:parseType='Resource'><q:action>created</q:action></rdf:li><rdf:li q:action='saved'/></rdf:Seq></p:history>" +
            "<p:nested><rdf:Bag><rdf:li><rdf:Seq><rdf:li>a</rdf:li><rdf:li>b</rdf:li></rdf:Seq></rdf:li></rdf:Bag></p:nested>" +
            "<p:title><rdf:Alt><rdf:li xml:lang='x-default'>T</rdf:li><rdf:li xml:lang='pt'>Titulo</rdf:li></rdf:Alt></p:title>" +
            "<p:mixed><rdf:Alt><rdf:li xml:lang='en'>tagged</rdf:li><rdf:li>bare</rdf:li></rdf:Alt></p:mixed>" +
            "<p:tagged xml:lang='en'>hello</p:tagged>" +
            "<p:empty/><p:none><rdf:Bag/></p:none>",
            "p:attribute='in an attribute'"));
        Assert.True(packet.IsEditable);
        string[] before = Rows(packet);

        XmpPacket changed = Apply(packet, edit => edit.Set("Xmp.p.added", "new"));
        string[] expected = [.. before, "Xmp.p.added=Text:new"];
        Assert.Equal(expected, Rows(changed));
        string xml = Text(changed);
        Assert.Contains("<p:link rdf:resource=\"urn:value\"", xml, StringComparison.Ordinal);
        Assert.Contains("<p:plain>text  with  spaces </p:plain>", xml, StringComparison.Ordinal);

        // A second pass over the canonical form changes nothing but the edit.
        XmpPacket again = Apply(changed, edit => edit.RemovePath("Xmp.p.added"));
        Assert.Equal(before, Rows(again));
    }

    [Fact]
    public void AnEditThatChangesNothingReturnsThePacket()
    {
        XmpPacket packet = XmpPacket.Parse(Xml("<p:a>1</p:a><p:list><rdf:Bag><rdf:li>x</rdf:li></rdf:Bag></p:list>"));
        var edit = new XmpEdit();
        edit.RemovePath("Xmp.p.missing");
        edit.RemovePath("Xmp.p.a/q:field");
        edit.RemovePath("Xmp.p.list[5]");
        edit.RemovePath("Xmp.p.a/?q:qualifier");
        edit.RemovePath("Xmp.p.a/?xml:lang");
        edit.RemoveLanguage("Xmp.p.missing", "en");
        edit.Set("Xmp.p.a", "1");
        edit.Set("Xmp.p.temporary", "x");
        edit.RemovePath("Xmp.p.temporary");
        Assert.False(edit.IsEmpty);
        Assert.Same(packet, packet.Apply(edit));
    }

    [Fact]
    public void AChangeThatDoesNotApplyIsRefusedAndNothingChanges()
    {
        XmpPacket packet = XmpPacket.Parse(Xml(
            "<p:a>1</p:a><p:list><rdf:Bag><rdf:li>x</rdf:li></rdf:Bag></p:list><p:place rdf:parseType='Resource'><q:city>Boston</q:city></p:place>"));
        string[] before = Rows(packet);
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Set("Xmp.p.list[2]", "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Set("Xmp.p.list[last()]/q:f", "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Set("Xmp.p.a/q:field", "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Set("Xmp.p.a[1]", "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Set("Xmp.p.missing[1]", "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Set("Xmp.p.missing/?q:note", "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Set("Xmp.p.missing/?xml:lang", "en")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Append("Xmp.p.list", XmpValueKind.Sequence, "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Append("Xmp.p.a", XmpValueKind.Bag, "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Insert("Xmp.p.list", 3, "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Insert("Xmp.p.missing", 1, "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.InsertStruct("Xmp.p.place", 1)));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.SetLanguage("Xmp.p.list", "en", "y")));
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => edit.Set("Xmp.p.place/rdf:value", "y")));
        // The changes before the one that is refused are not made either.
        Assert.Equal(MetadataErrorCode.InvalidXmpPath, Refusal(packet, edit => { edit.Set("Xmp.p.a", "2"); edit.Set("Xmp.p.list[9]", "y"); }));
        Assert.Equal(before, Rows(packet));
    }

    [Theory]
    [InlineData("dc.title")]
    [InlineData("Xmp.dc")]
    [InlineData("Xmp.dc.")]
    [InlineData("Xmp..title")]
    [InlineData("Xmp.dc.title[0]")]
    [InlineData("Xmp.dc.title[x]")]
    [InlineData("Xmp.dc.title[1")]
    [InlineData("Xmp.dc.title/field")]
    [InlineData("Xmp.dc.title/a:b:c")]
    [InlineData("Xmp.dc.title/xml:lang")]
    [InlineData("Xmp.dc.title/?xml:space")]
    [InlineData("Xmp.dc.title/?xml:lang/a:b")]
    [InlineData("Xmp.dc.title]")]
    [InlineData("Xmp.dc.ti tle")]
    public void APathThatIsNotOneIsRefusedWhenTheChangeIsMade(string path)
    {
        var edit = new XmpEdit();
        Assert.ThrowsAny<ArgumentException>(() => edit.Set(path, "x"));
        Assert.ThrowsAny<ArgumentException>(() => edit.RemovePath(path));
        Assert.True(edit.IsEmpty);
    }

    [Fact]
    public void ArgumentsAreCheckedWhenTheChangeIsMade()
    {
        var edit = new XmpEdit();
        Assert.ThrowsAny<ArgumentException>(() => edit.Append("Xmp.dc.title", XmpValueKind.LanguageAlternative, "x"));
        Assert.ThrowsAny<ArgumentException>(() => edit.AppendStruct("Xmp.dc.title", XmpValueKind.Text));
        Assert.ThrowsAny<ArgumentException>(() => edit.Insert("Xmp.dc.subject", 0, "x"));
        Assert.ThrowsAny<ArgumentException>(() => edit.SetLanguage("Xmp.dc.title", "not a tag", "x"));
        Assert.ThrowsAny<ArgumentException>(() => edit.Set("Xmp.dc.title/?xml:lang", "not a tag"));
        Assert.ThrowsAny<ArgumentException>(() => edit.Set("Xmp.dc.title", "bad \u0001 character"));
        Assert.ThrowsAny<ArgumentException>(() => edit.Append("Xmp.dc.title/?xml:lang", XmpValueKind.Bag, "x"));
        Assert.ThrowsAny<ArgumentException>(() => edit.RegisterNamespace("rdf", "urn:owned:x"));
        Assert.ThrowsAny<ArgumentException>(() => edit.RegisterNamespace("a b", "urn:owned:x"));
        Assert.ThrowsAny<ArgumentException>(() => edit.RegisterNamespace("ok", "not absolute"));
        Assert.ThrowsAny<ArgumentException>(() => edit.RegisterNamespace("ok", Rdf));
        edit.RegisterNamespace("ok", "urn:owned:x");
        edit.RegisterNamespace("ok", "urn:owned:x");
        Assert.ThrowsAny<ArgumentException>(() => edit.RegisterNamespace("ok", "urn:owned:y"));
        // Registering is not a change, and whole removal still cannot be mixed with one.
        Assert.True(edit.IsEmpty);
        edit.RemoveAllXmp();
        Assert.ThrowsAny<ArgumentException>(() => edit.Set("Xmp.dc.format", "x"));
    }

    [Fact]
    public void ASidecarIsWrittenAndReadBack()
    {
        XmpPacket packet = Apply(XmpPacket.CreateEmpty(), edit =>
        {
            edit.SetLanguage("Xmp.dc.title", "x-default", "Harbour");
            edit.Append("Xmp.dc.subject", XmpValueKind.Bag, "dawn");
            edit.AppendStruct("Xmp.xmpMM.History", XmpValueKind.Sequence);
            edit.Set("Xmp.xmpMM.History[1]/stEvt:action", "created");
        });
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-xmp-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "picture.xmp");
            packet.SaveAs(file);
            XmpPacket loaded = XmpPacket.Load(file);
            Assert.Equal(Rows(packet), Rows(loaded));
            XmpPacket edited = Apply(loaded, edit => edit.Set("Xmp.xmpMM.History[1]/stEvt:softwareAgent", "MetadataSharp"));
            Assert.Equal("Xmp.xmpMM.History[1]/stEvt:softwareAgent=Text:MetadataSharp", Rows(edited)[^1]);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void APacketInASlotIsPaddedToTheSlotAndAGrownOneIsGivenRoom()
    {
        var first = new XmpEdit();
        first.Set("Xmp.dc.format", new string('a', 400));
        XmpPacket packet = XmpPacket.CreateEmpty().Apply(first)!;

        var shorter = new XmpEdit();
        shorter.Set("Xmp.dc.format", "b");
        XmpPacket plain = packet.Apply(shorter)!;
        XmpPacket inSlot = packet.Apply(shorter, packet.ByteLength, 2048)!;
        Assert.True(plain.ByteLength < packet.ByteLength);
        Assert.Equal(packet.ByteLength, inSlot.ByteLength);
        Assert.Equal(Rows(plain), Rows(inSlot));
        Assert.EndsWith("\n<?xpacket end=\"w\"?>", Text(inSlot), StringComparison.Ordinal);

        var longer = new XmpEdit();
        longer.Set("Xmp.dc.format", new string('c', 500));
        XmpPacket grown = packet.Apply(longer, packet.ByteLength, 2048)!;
        Assert.Equal(packet.Apply(longer)!.ByteLength + 2048, grown.ByteLength);
        Assert.True(grown.IsEditable);

        // The padding is whitespace before the closing instruction, and the next edit fits in it.
        XmpPacket next = grown.Apply(first, grown.ByteLength, 2048)!;
        Assert.Equal(grown.ByteLength, next.ByteLength);
    }

    [Fact]
    public void TheResultIsHeldToTheBudgetsOfThePacket()
    {
        XmpPacket packet = XmpPacket.CreateEmpty(new() { Limits = new() { MaxDepth = 8 } });
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(packet, edit =>
        {
            edit.RegisterNamespace("q", "urn:owned:other");
            edit.Set("Xmp.q.a/q:b/q:c/q:d/q:e/q:f/q:g/q:h", "too deep");
        }));
    }
}
