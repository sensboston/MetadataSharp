using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Jpeg;

public sealed class JpegExtendedXmpTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string StandardSignature = "http://ns.adobe.com/xap/1.0/\0";
    private const string ExtensionSignature = "http://ns.adobe.com/xmp/extension/\0";
    private static readonly XmpPropertyName Tool = new("http://ns.adobe.com/xap/1.0/", "CreatorTool");
    private static readonly XmpPropertyName Big = new("urn:owned:test", "big");
    private static readonly XmpPropertyName Other = new("urn:owned:test", "other");

    private static byte[] Save(byte[] image, XmpEdit edit)
    {
        using var source = new MemoryStream(image);
        using var document = JpegMetadataDocument.Open(source);
        using var output = new MemoryStream();
        document.SaveXmp(output, edit);
        return output.ToArray();
    }

    private static XmpEdit Edit(Action<XmpEdit> change)
    {
        var edit = new XmpEdit();
        change(edit);
        return edit;
    }

    private static XmpPacket Read(byte[] image)
    {
        using var document = JpegMetadataDocument.Open(new MemoryStream(image));
        return document.ReadXmp()!;
    }

    private static MetadataErrorCode ReadRefusal(byte[] image)
    {
        using var document = JpegMetadataDocument.Open(new MemoryStream(image));
        return Assert.Throws<MetadataException>(() => document.ReadXmp()).Code;
    }

    private static byte[] Payload(byte[] image, JpegSegment segment) =>
        image.AsSpan(checked((int)segment.PayloadOffset), segment.PayloadLength).ToArray();

    /// <summary>The standard packet and the extension of a file, the extension put together from its segments as the format says.</summary>
    private static (string Standard, byte[] Extension, int Parts) Divided(byte[] image)
    {
        JpegScanResult scan = JpegScanner.Scan(new MemoryStream(image), new());
        byte[] standard = Payload(image, Assert.Single(scan.Segments, s => s.IsStandardXmp));
        Assert.True(standard.Length - StandardSignature.Length <= 65_504);
        string packet = Encoding.UTF8.GetString(standard.AsSpan(StandardSignature.Length));
        JpegSegment[] parts = scan.Segments.Where(s => s.IsExtendedXmp).ToArray();
        if (parts.Length == 0) return (packet, [], 0);

        // The extension segments follow the standard one directly, in order.
        int first = scan.Segments.ToList().FindIndex(s => s.IsStandardXmp);
        for (int i = 0; i < parts.Length; i++) Assert.Same(parts[i], scan.Segments[first + 1 + i]);

        byte[]? extension = null;
        string? digest = null;
        int expected = 0;
        foreach (JpegSegment part in parts)
        {
            byte[] bytes = Payload(image, part);
            Assert.True(bytes.Length <= 65_533);
            string name = Encoding.ASCII.GetString(bytes, ExtensionSignature.Length, 32);
            int total = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(ExtensionSignature.Length + 32)));
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(ExtensionSignature.Length + 36)));
            extension ??= new byte[total];
            digest ??= name;
            Assert.Equal(digest, name);
            Assert.Equal(extension.Length, total);
            Assert.Equal(expected, offset);
            bytes.AsSpan(ExtensionSignature.Length + 40).CopyTo(extension.AsSpan(offset));
            expected += bytes.Length - ExtensionSignature.Length - 40;
        }
        Assert.Equal(extension!.Length, expected);
        Assert.Equal(Convert.ToHexString(MD5.HashData(extension)), digest);
        Assert.Contains("HasExtendedXMP=\"" + digest + "\"", packet.Replace("<xmpNote:HasExtendedXMP>", "HasExtendedXMP=\"", StringComparison.Ordinal)
            .Replace("</xmpNote:HasExtendedXMP>", "\"", StringComparison.Ordinal), StringComparison.Ordinal);
        return (packet, extension, parts.Length);
    }

    private static string Text(int length, char start)
    {
        var text = new StringBuilder(length);
        for (int i = 0; i < length; i++) text.Append((char)(start + i % 23));
        return text.ToString();
    }

    [Theory]
    [InlineData(65_300)]
    [InlineData(65_600)]
    [InlineData(100_000)]
    [InlineData(400_000)]
    public void APacketLongerThanOneSegmentIsWrittenAsExtendedXmpAndReadWhole(int length)
    {
        string big = Text(length, 'a');
        byte[] original = JpegFixture.Build([.. JpegFixture.Segment(0xE1, JpegFixture.Exif()), .. JpegFixture.Segment(0xED, [4, 5, 6])]);
        byte[] image = Save(original, Edit(edit =>
        {
            edit.Set(Tool, XmpValue.CreateText("owned"));
            edit.Set(Big, XmpValue.CreateText(big));
            edit.Set("Xmp.dc.subject", "x");
            edit.RemovePath("Xmp.dc.subject");
            edit.Append("Xmp.dc.subject", XmpValueKind.Bag, "harbour");
        }));

        (string standard, byte[] extension, int count) = Divided(image);
        Assert.Equal((extension.Length + 65_457) / 65_458, count);
        Assert.True(extension.Length > length);
        Assert.StartsWith("<?xpacket begin=", standard, StringComparison.Ordinal);
        Assert.DoesNotContain(big, standard, StringComparison.Ordinal);
        string second = Encoding.UTF8.GetString(extension);
        Assert.StartsWith("<x:xmpmeta", second, StringComparison.Ordinal);
        Assert.DoesNotContain("xpacket", second, StringComparison.Ordinal);
        Assert.Contains(big, second, StringComparison.Ordinal);

        using var document = JpegMetadataDocument.Open(new MemoryStream(image));
        Assert.True(document.HasExtendedXmp);
        Assert.Single(document.Exif!.Entries);
        XmpPacket packet = document.ReadXmp()!;
        Assert.True(packet.IsEditable);
        Assert.Equal("owned", packet.Properties[Tool].Text);
        Assert.Equal(big, packet.Properties[Big].Text);
        Assert.Equal(new[] { "harbour" }, packet.Subjects);
        Assert.Equal(3, packet.Entries.Count);
        Assert.DoesNotContain(packet.Entries, entry => entry.Path.Contains("HasExtendedXMP", StringComparison.Ordinal));

        // The same edit again changes nothing, and removing the XMP gives the file back.
        Assert.Equal(image, Save(image, Edit(edit => edit.Set(Big, XmpValue.CreateText(big)))));
        Assert.Equal(original, Save(image, Edit(edit => edit.RemoveAllXmp())));
    }

    [Fact]
    public void ExtendedXmpIsEditedAndBecomesOneSegmentAgainWhenItFits()
    {
        string big = Text(150_000, 'A');
        byte[] original = JpegFixture.Build();
        byte[] image = Save(original, Edit(edit =>
        {
            edit.Set(Big, XmpValue.CreateText(big));
            edit.Set(Other, XmpValue.CreateText(Text(70_000, 'B')));
            edit.Set(Tool, XmpValue.CreateText("first"));
            edit.AppendStruct("Xmp.xmpMM.History", XmpValueKind.Sequence);
            edit.Set("Xmp.xmpMM.History[last()]/stEvt:action", "created");
        }));
        Assert.True(Divided(image).Parts >= 4);

        byte[] edited = Save(image, Edit(edit =>
        {
            edit.Set(Tool, XmpValue.CreateText("second"));
            edit.Set("Xmp.xmpMM.History[1]/stEvt:action", "saved");
            edit.Remove(Other);
        }));
        (_, byte[] extension, int parts) = Divided(edited);
        Assert.Equal(3, parts);
        Assert.Contains(big, Encoding.UTF8.GetString(extension), StringComparison.Ordinal);
        XmpPacket packet = Read(edited);
        Assert.Equal("second", packet.Properties[Tool].Text);
        Assert.Equal(big, packet.Properties[Big].Text);
        Assert.False(packet.Properties.ContainsKey(Other));
        Assert.Contains(packet.Entries, entry => entry.Key == "Xmp.xmpMM.History[1]/stEvt:action" && entry.Values[0] == "saved");

        byte[] small = Save(edited, Edit(edit => edit.Remove(Big)));
        (string standard, _, int none) = Divided(small);
        Assert.Equal(0, none);
        Assert.DoesNotContain("HasExtendedXMP", standard, StringComparison.Ordinal);
        using (var document = JpegMetadataDocument.Open(new MemoryStream(small)))
        {
            Assert.False(document.HasExtendedXmp);
            Assert.Equal("second", document.ReadXmp()!.Properties[Tool].Text);
        }
        Assert.Equal(original, Save(small, Edit(edit => edit.RemoveAllXmp())));
    }

    [Fact]
    public void ManySmallPropertiesAreDividedBetweenTheParts()
    {
        byte[] image = Save(JpegFixture.Build(), Edit(edit =>
        {
            edit.RegisterNamespace("own", "urn:owned:test");
            for (int i = 0; i < 400; i++) edit.Set("Xmp.own.p" + i, Text(300 + i, 'a'));
        }));
        (string standard, byte[] extension, int parts) = Divided(image);
        Assert.True(parts >= 1);
        Assert.True(extension.Length > 0);
        Assert.True(standard.Length > 30_000, "The standard packet keeps what fits.");
        XmpPacket packet = Read(image);
        Assert.Equal(400, packet.Entries.Count);
        for (int i = 0; i < 400; i++)
            Assert.Equal(Text(300 + i, 'a'), packet.Properties[new("urn:owned:test", "p" + i)].Text);
    }

    private static byte[] StandardSegment(string digest, string content = "<p:small>s</p:small>") => JpegFixture.Segment(0xE1,
        Encoding.UTF8.GetBytes(StandardSignature + $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='{Rdf}'>" +
            $"<rdf:Description rdf:about='' xmlns:p='urn:owned:test' xmlns:xmpNote='http://ns.adobe.com/xmp/note/' xmpNote:HasExtendedXMP='{digest}'>" +
            content + "</rdf:Description></rdf:RDF></x:xmpmeta>"));

    private static byte[] ExtensionXml(string content) => Encoding.UTF8.GetBytes(
        $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='{Rdf}'><rdf:Description rdf:about='' xmlns:p='urn:owned:test'>" +
        content + "</rdf:Description></rdf:RDF></x:xmpmeta>");

    private static byte[] ExtensionSegment(string digest, int total, int offset, ReadOnlySpan<byte> part)
    {
        byte[] payload = new byte[ExtensionSignature.Length + 40 + part.Length];
        Encoding.ASCII.GetBytes(ExtensionSignature + digest, payload);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(ExtensionSignature.Length + 32), (uint)total);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(ExtensionSignature.Length + 36), (uint)offset);
        part.CopyTo(payload.AsSpan(ExtensionSignature.Length + 40));
        return JpegFixture.Segment(0xE1, payload);
    }

    [Fact]
    public void ExtendedXmpOfAnotherWriterIsReadInAnyPartOrderAndRewritten()
    {
        byte[] extension = ExtensionXml("<p:large>" + Text(500, 'a') + "</p:large><p:list><rdf:Seq><rdf:li>1</rdf:li><rdf:li>2</rdf:li></rdf:Seq></p:list>");
        string digest = Convert.ToHexString(MD5.HashData(extension));
        int half = extension.Length / 2;
        byte[] image = JpegFixture.Build(
        [
            .. ExtensionSegment(digest, extension.Length, half, extension.AsSpan(half)),
            .. JpegFixture.Segment(0xED, [1, 2, 3]),
            .. StandardSegment(digest),
            .. ExtensionSegment(digest, extension.Length, 0, extension.AsSpan(0, half))
        ]);
        XmpPacket packet = Read(image);
        Assert.True(packet.IsEditable);
        Assert.Equal(new[] { "Xmp.p.small", "Xmp.p.large", "Xmp.p.list" }, packet.Entries.Select(entry => entry.Key).ToArray());
        Assert.Equal(Text(500, 'a'), packet.Properties[new("urn:owned:test", "large")].Text);

        // The result fits one segment: both old extension segments go, the other segment stays.
        byte[] edited = Save(image, Edit(edit => edit.Append("Xmp.p.list", XmpValueKind.Sequence, "3")));
        JpegScanResult scan = JpegScanner.Scan(new MemoryStream(edited), new());
        Assert.DoesNotContain(scan.Segments, s => s.IsExtendedXmp);
        Assert.Single(scan.Segments, s => s.IsStandardXmp);
        Assert.Single(scan.Segments, s => s.Marker == 0xED);
        XmpPacket after = Read(edited);
        Assert.Equal(new[] { "1", "2", "3" }, after.Properties[new("urn:owned:test", "list")].Items);
        Assert.Equal("s", after.Properties[new("urn:owned:test", "small")].Text);
        Assert.Equal(JpegFixture.Build(JpegFixture.Segment(0xED, [1, 2, 3])), Save(image, Edit(edit => edit.RemoveAllXmp())));
    }

    [Theory]
    [InlineData("other digest")]
    [InlineData("missing part")]
    [InlineData("part twice")]
    [InlineData("length differs")]
    [InlineData("beyond the end")]
    [InlineData("no standard packet")]
    public void AnExtensionThatIsNotCompleteUnderTheNamedDigestIsRefused(string fault)
    {
        byte[] extension = ExtensionXml("<p:large>" + Text(300, 'a') + "</p:large>");
        string digest = Convert.ToHexString(MD5.HashData(extension));
        int half = extension.Length / 2;
        byte[] first = ExtensionSegment(digest, extension.Length, 0, extension.AsSpan(0, half));
        byte[] second = ExtensionSegment(digest, extension.Length, half, extension.AsSpan(half));
        byte[] standard = StandardSegment(digest);
        byte[] segments = fault switch
        {
            "other digest" => [.. standard, .. first, .. ExtensionSegment(new string('0', 32), extension.Length, half, extension.AsSpan(half))],
            "missing part" => [.. standard, .. first],
            "part twice" => [.. standard, .. first, .. second, .. second],
            "length differs" => [.. standard, .. first, .. ExtensionSegment(digest, extension.Length + 1, half, extension.AsSpan(half))],
            "beyond the end" => [.. standard, .. first, .. ExtensionSegment(digest, extension.Length, half + 1, extension.AsSpan(half))],
            _ => [.. first, .. second]
        };
        byte[] image = JpegFixture.Build(segments);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, ReadRefusal(image));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(image, Edit(edit => edit.Set(Tool, XmpValue.CreateText("x"))))).Code);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(image, Edit(edit => edit.RemoveAllXmp()))).Code);
        Assert.Equal(image, Save(image, new XmpEdit()));
    }

    [Fact]
    public void APropertyInBothPartsIsAmbiguous()
    {
        byte[] extension = ExtensionXml("<p:small>other</p:small>");
        string digest = Convert.ToHexString(MD5.HashData(extension));
        byte[] image = JpegFixture.Build([.. StandardSegment(digest), .. ExtensionSegment(digest, extension.Length, 0, extension)]);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, ReadRefusal(image));
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => Save(image, Edit(edit => edit.Set(Tool, XmpValue.CreateText("x"))))).Code);
    }

    [Fact]
    public void WhatExceedsThePacketBudgetIsRefusedBeforeAnythingIsWritten()
    {
        using var document = JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build()));
        using var output = new MemoryStream();
        XmpEdit edit = Edit(change =>
        {
            change.Set(Big, XmpValue.CreateText(new string('a', 600_000)));
            change.Set(Other, XmpValue.CreateText(new string('b', 600_000)));
        });
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => document.SaveXmp(output, edit)).Code);
        Assert.Empty(output.ToArray());
    }
}
