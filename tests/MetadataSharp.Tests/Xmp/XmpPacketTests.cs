using System.Text;
using Xunit;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Tests.Xmp;

public sealed class XmpPacketTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XmpPropertyName Title = new("urn:owned:test", "title");
    private static byte[] Xml(string content, string attributes = "") => Encoding.UTF8.GetBytes(
        $"<rdf:RDF xmlns:rdf='{Rdf}' xmlns:p='urn:owned:test'><rdf:Description rdf:about='' {attributes}>{content}</rdf:Description></rdf:RDF>");
    private static byte[] Raw(XmpPacket packet) { byte[] b = new byte[packet.ByteLength]; packet.CopyRawTo(b); return b; }

    [Fact]
    public void AttributesUnicodeOwnershipAndExactNoOp()
    {
        byte[] input = Xml("<p:other><![CDATA[ <a> ]]></p:other>", "p:title='Привет &amp; 世界'");
        byte[] expected = input.ToArray();
        XmpPacket packet = XmpPacket.Parse(input); input.AsSpan().Clear();
        Assert.True(packet.IsEditable);
        Assert.Equal("Привет & 世界", packet.Properties[Title].Text);
        Assert.Equal(" <a> ", packet.Properties[new("urn:owned:test", "other")].Text);
        Assert.Equal(expected, Raw(packet.Apply(new())!));
        using var saved = new MemoryStream(); packet.Save(saved); Assert.Equal(expected, saved.ToArray());
    }
    [Fact]
    public void AddressedChangesPreserveUnknownNamespaceAndEscapedText()
    {
        XmpPacket packet = XmpPacket.Parse(Xml("<p:title>old</p:title><p:unknown>  keep &#13;\n text  </p:unknown>"));
        var edit = new XmpEdit(); edit.Set(Title, XmpValue.CreateText("new<&>\r\n世界"));
        XmpPacket changed = packet.Apply(edit)!;
        Assert.Equal("new<&>\r\n世界", changed.Properties[Title].Text);
        Assert.Equal(packet.Properties[new("urn:owned:test", "unknown")].Text, changed.Properties[new("urn:owned:test", "unknown")].Text);
        Assert.Equal("old", packet.Properties[Title].Text);
        Assert.Equal(XmpValueKind.Text, changed.Properties[Title].Kind);
        Assert.Empty(changed.Properties[Title].Items); Assert.Empty(changed.Properties[Title].Languages);
    }
    [Theory]
    [InlineData(XmpValueKind.Bag)]
    [InlineData(XmpValueKind.Sequence)]
    public void TextArraysPreserveDuplicatesAndOrder(XmpValueKind kind)
    {
        var input = new[] { "z", "a", "a", "" }; var edit = new XmpEdit();
        XmpValue value = XmpValue.CreateArray(kind, input); input[0] = "changed";
        edit.Set(Title, value);
        XmpValue actual = XmpPacket.CreateEmpty().Apply(edit)!.Properties[Title];
        Assert.Equal(kind, actual.Kind); Assert.Equal(new[] { "z", "a", "a", "" }, actual.Items);
        Assert.Null(actual.Text);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)actual.Items)[0] = "bad");
    }
    [Fact]
    public void LanguageAlternativePreservesValuesAndWritesDefaultFirst()
    {
        var source = new Dictionary<string, string> { ["fr-FR"] = "Bonjour", ["x-default"] = "Hello" };
        XmpValue value = XmpValue.CreateLanguageAlternative(source); source.Clear();
        var edit = new XmpEdit(); edit.Set(Title, value);
        XmpPacket packet = XmpPacket.CreateEmpty().Apply(edit)!;
        Assert.Equal("Bonjour", packet.Properties[Title].Languages["FR-fr"]);
        string xml = Encoding.UTF8.GetString(Raw(packet)); Assert.True(xml.IndexOf("x-default", StringComparison.Ordinal) < xml.IndexOf("fr-FR", StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)value.Languages).Add("en", "bad"));
    }
    [Theory]
    [InlineData("<p:title rdf:resource='urn:value' />")]
    [InlineData("<p:title rdf:parseType='Resource'><p:field>x</p:field></p:title>")]
    [InlineData("<p:title xml:lang='en'>qualified</p:title>")]
    [InlineData("<p:title><rdf:Alt><rdf:li>unqualified alternative</rdf:li></rdf:Alt></p:title>")]
    [InlineData("<p:title><rdf:Bag><rdf:li><rdf:Description /></rdf:li></rdf:Bag></p:title>")]
    public void UnsupportedRdfIsOpaqueAndCannotBeDestructivelyEdited(string content)
    {
        byte[] input = Xml(content); XmpPacket packet = XmpPacket.Parse(input);
        Assert.False(packet.IsEditable);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => packet.Properties).Code);
        Assert.Equal(input, Raw(packet.Apply(new())!));
        var edit = new XmpEdit(); edit.Set(Title, XmpValue.CreateText("loss"));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => packet.Apply(edit)).Code);
        var removal = new XmpEdit(); removal.RemoveAllXmp(); Assert.Null(packet.Apply(removal));
    }
    [Fact]
    public void MultipleDescriptionsAndNonemptyAboutRemainOpaque()
    {
        Assert.False(XmpPacket.Parse(Encoding.UTF8.GetBytes($"<rdf:RDF xmlns:rdf='{Rdf}'><rdf:Description rdf:about=''/><rdf:Description rdf:about=''/></rdf:RDF>")).IsEditable);
        Assert.False(XmpPacket.Parse(Encoding.UTF8.GetBytes($"<rdf:RDF xmlns:rdf='{Rdf}'><rdf:Description rdf:about='urn:subject'/></rdf:RDF>")).IsEditable);
    }
    [Theory]
    [InlineData("<p:title>a</p:title><p:title>b</p:title>")]
    [InlineData("<p:title><rdf:Alt><rdf:li xml:lang='en'>a</rdf:li><rdf:li xml:lang='EN'>b</rdf:li></rdf:Alt></p:title>")]
    public void DuplicateExpandedNamesAndLanguagesAreAmbiguous(string xml) =>
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => XmpPacket.Parse(Xml(xml))).Code);
    [Fact]
    public void PrefixAliasesAndAttributeElementDuplicatesAreAmbiguous() =>
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => XmpPacket.Parse(Xml("<q:title xmlns:q='urn:owned:test'>b</q:title>", "p:title='a'"))).Code);
    [Theory]
    [InlineData("<!DOCTYPE root [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><root>&e;</root>")]
    [InlineData("<!DOCTYPE root [<!ENTITY a 'abc'><!ENTITY b '&a;&a;'>]><root>&b;</root>")]
    [InlineData("<root/>")]
    [InlineData("<rdf:RDF")]
    [InlineData("<?xml version='1.0' encoding='UTF-16'?><root/>")]
    public void MalformedXmlEntitiesAndConflictingEncodingFail(string xml) =>
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => XmpPacket.Parse(Encoding.UTF8.GetBytes(xml))).Code);
    [Fact]
    public void InvalidUtf8AndUtf16AreRejected()
    {
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => XmpPacket.Parse(new byte[] { 0xff, 0xfe, 0 })).Code);
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => XmpPacket.Parse(new byte[] { 0xc3, 0x28 })).Code);
    }
    [Theory]
    [InlineData("bytes")]
    [InlineData("characters")]
    [InlineData("depth")]
    [InlineData("nodes")]
    [InlineData("attributes")]
    [InlineData("properties")]
    [InlineData("items")]
    [InlineData("names")]
    public void EachFiniteBudgetIsEnforced(string budget)
    {
        var limits = budget switch
        {
            "bytes" => new XmpLimits { MaxPacketBytes = 2 }, "characters" => new() { MaxXmlCharacters = 2 },
            "depth" => new() { MaxDepth = 2 }, "nodes" => new() { MaxNodes = 2 }, "attributes" => new() { MaxAttributes = 1 },
            "properties" => new() { MaxProperties = 1 }, "items" => new() { MaxArrayItems = 1 }, _ => new() { MaxNameCharacters = 2 }
        };
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => XmpPacket.Parse(
            Xml("<p:title><rdf:Bag><rdf:li>a</rdf:li><rdf:li>b</rdf:li></rdf:Bag></p:title><p:other>z</p:other>"), new() { Limits = limits })).Code);
    }
    [Fact]
    public void EditValidationLastWinsAndWholeRemovalRules()
    {
        var edit = new XmpEdit(); Assert.True(edit.IsEmpty);
        Assert.Throws<ArgumentException>(() => edit.Set(default, XmpValue.CreateText("x"))); Assert.True(edit.IsEmpty);
        edit.Set(Title, XmpValue.CreateText("first")); edit.Set(Title, XmpValue.CreateText("last"));
        XmpPacket packet = XmpPacket.CreateEmpty().Apply(edit)!; Assert.Equal("last", packet.Properties[Title].Text);
        Assert.Throws<ArgumentException>(() => edit.RemoveAllXmp());
        edit.Remove(Title); Assert.Empty(packet.Apply(edit)!.Properties);
        var remove = new XmpEdit(); remove.RemoveAllXmp(); remove.RemoveAllXmp();
        Assert.Throws<ArgumentException>(() => remove.Remove(Title)); Assert.Null(packet.Apply(remove));
        var missing = new XmpEdit(); missing.Remove(new("urn:owned:test", "missing")); Assert.Same(packet, packet.Apply(missing));
    }
    [Fact]
    public void InvalidValuesAndFactoriesRejectBeforeMutation()
    {
        Assert.Throws<ArgumentException>(() => new XmpPropertyName("relative", "x"));
        Assert.Throws<ArgumentException>(() => new XmpPropertyName(Rdf, "x"));
        Assert.Throws<ArgumentException>(() => new XmpPropertyName("urn:owned:test", "a:b"));
        Assert.Throws<ArgumentException>(() => XmpValue.CreateText("\ud800"));
        Assert.Throws<ArgumentException>(() => XmpValue.CreateArray(XmpValueKind.Text, []));
        Assert.Throws<ArgumentException>(() => XmpValue.CreateArray(XmpValueKind.Bag, Enumerable.Repeat("a", 8193)));
        Assert.Throws<ArgumentException>(() => XmpValue.CreateLanguageAlternative(new[] { new KeyValuePair<string, string>("en", "a"), new("EN", "b") }));
        Assert.Throws<ArgumentException>(() => XmpValue.CreateLanguageAlternative(new[] { new KeyValuePair<string, string>("bad--tag", "a") }));
        Assert.Throws<ArgumentOutOfRangeException>(() => XmpPacket.CreateEmpty(new() { Limits = new() { MaxDepth = 0 } }));
    }
    [Fact]
    public void OutputLimitsArePreflightedAndCaptured()
    {
        byte[] xml = Xml("<p:title>x</p:title>");
        XmpPacket packet = XmpPacket.Parse(xml, new() { Limits = new() { MaxPacketBytes = xml.Length + 10 } });
        var edit = new XmpEdit(); edit.Set(Title, XmpValue.CreateText(new string('<', 100)));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => packet.Apply(edit)).Code);
        Assert.Equal(xml, Raw(packet));
    }
    [Theory]
    [InlineData("w", true)]
    [InlineData("r", false)]
    public void WrappersBomAndReadOnlyPolicy(string end, bool editable)
    {
        byte[] xml = Encoding.UTF8.GetBytes("\uFEFF<?xpacket begin='' id='W5M0MpCehiHzreSzNTczkc9d'?>" +
            "<x:xmpmeta xmlns:x='adobe:ns:meta/'>" + Encoding.UTF8.GetString(Xml("<p:title>x</p:title>")) + $"</x:xmpmeta><?xpacket end='{end}'?>");
        XmpPacket packet = XmpPacket.Parse(xml); Assert.Equal(editable, packet.IsEditable); Assert.Equal(xml, Raw(packet));
    }
    [Fact]
    public async Task ShortNonseekableAsyncInputAndBorrowedOutputRemainOpen()
    {
        byte[] xml = Xml("<p:title>async</p:title>");
        using var source = new AsyncShortInput(xml); XmpPacket packet = await XmpPacket.LoadAsync(source);
        using var output = new AsyncOutput(); await packet.SaveAsync(output);
        Assert.Equal(xml, output.ToArray()); Assert.True(source.CanRead); Assert.True(output.CanWrite);
        using var exact = new MemoryStream(xml); Assert.Equal(xml, Raw(XmpPacket.Load(exact, new() { Limits = new() { MaxPacketBytes = xml.Length } })));
    }
    [Fact]
    public async Task CancellationAndInvalidDestinationDoNotWrite()
    {
        XmpPacket packet = XmpPacket.CreateEmpty(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => packet.SaveAsync(output, canceled.Token)); Assert.Empty(output.ToArray());
        using var source = new MemoryStream(Raw(packet));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => XmpPacket.LoadAsync(source, cancellationToken: canceled.Token));
        using var occupied = new MemoryStream(new byte[] { 9 }); Assert.Throws<ArgumentException>(() => packet.Save(occupied)); Assert.Equal(new byte[] { 9 }, occupied.ToArray());
    }
    [Fact]
    public async Task SidecarPublicationRefusesExistingTargetAndLeavesNoTemporary()
    {
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-xmp-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "owned.xmp"); XmpPacket packet = XmpPacket.Parse(Xml("<p:title>owned</p:title>"));
            await packet.SaveAsAsync(file); Assert.Equal(Raw(packet), File.ReadAllBytes(file));
            XmpPacket loaded = await XmpPacket.LoadAsync(file); Assert.Equal("owned", loaded.Properties[Title].Text);
            Assert.Throws<IOException>(() => packet.SaveAs(file)); Assert.Equal(Raw(packet), File.ReadAllBytes(file));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => packet.SaveAsAsync(Path.Combine(directory, "cancel.xmp"), canceled.Token));
            Assert.Single(Directory.GetFiles(directory));
            Assert.Throws<DirectoryNotFoundException>(() => packet.SaveAs(Path.Combine(directory, "missing", "x.xmp")));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void LoadUsesCurrentPositionAndSnapshotSurvivesSourceDispose()
    {
        byte[] xml = Xml("<p:title>x</p:title>"); XmpPacket packet;
        using (var source = new MemoryStream(new byte[] { 1, 2 }.Concat(xml).ToArray())) { source.Position = 2; packet = XmpPacket.Load(source); }
        Assert.Equal(xml, Raw(packet)); Assert.Throws<ArgumentException>(() => packet.CopyRawTo(new byte[1]));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<XmpPropertyName, XmpValue>)packet.Properties).Clear());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutputIoFailureMayLeavePartialBytesWithoutChangingPacket(bool asynchronous)
    {
        XmpPacket packet = XmpPacket.Parse(Xml("<p:title>owned</p:title>")); byte[] expected = Raw(packet);
        using var output = new PartialOutput();
        if (asynchronous) await Assert.ThrowsAsync<IOException>(() => packet.SaveAsync(output));
        else Assert.Throws<IOException>(() => packet.Save(output));
        Assert.Equal(2, output.Length); Assert.Equal(expected, Raw(packet)); Assert.True(output.CanWrite);
    }
    [Fact]
    public async Task CancellationDuringReadAndWriteIsObservedAndStreamsStayOpen()
    {
        using var canceledRead = new CancellationTokenSource();
        using var source = new CancelingInput(Xml("<p:title>x</p:title>"), canceledRead);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => XmpPacket.LoadAsync(source, cancellationToken: canceledRead.Token));
        Assert.True(source.CanRead);
        XmpPacket packet = XmpPacket.Parse(Xml("<p:title>" + new string('a', 20000) + "</p:title>"));
        using var canceledWrite = new CancellationTokenSource(); using var output = new CancelingOutput(canceledWrite);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => packet.SaveAsync(output, canceledWrite.Token));
        Assert.Equal(8192, output.Length); Assert.True(output.CanWrite);
    }
    [Fact]
    public void FailedPublicationPreservesCompetingDirectoryAndCleansOwnedTemporary()
    {
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-xmp-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(directory, "target.xmp"); Directory.CreateDirectory(target);
        try
        {
            string sentinel = Path.Combine(target, "sentinel"); File.WriteAllText(sentinel, "keep");
            Exception? error = Record.Exception(() => XmpPacket.CreateEmpty().SaveAs(target));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Equal("keep", File.ReadAllText(sentinel)); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void AlternativeWithoutExplicitDefaultKeepsOriginalPreferredLanguage()
    {
        XmpPacket packet = XmpPacket.Parse(Xml("<p:title><rdf:Alt><rdf:li xml:lang='fr'>preferred</rdf:li><rdf:li xml:lang='en'>other</rdf:li></rdf:Alt></p:title>"));
        var edit = new XmpEdit(); edit.Set(new("urn:owned:test", "unrelated"), XmpValue.CreateText("change"));
        string output = Encoding.UTF8.GetString(Raw(packet.Apply(edit)!));
        Assert.True(output.IndexOf("preferred", StringComparison.Ordinal) < output.IndexOf("other", StringComparison.Ordinal));
    }
    private sealed class PartialOutput : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        { base.Write(buffer, offset, Math.Min(count, 2)); throw new IOException("Injected output failure."); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { byte[] b = buffer.ToArray(); base.Write(b, 0, Math.Min(b.Length, 2)); return ValueTask.FromException(new IOException("Injected async failure.")); }
    }
    private sealed class CancelingInput(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { byte[] b = new byte[Math.Min(buffer.Length, 3)]; int n = base.Read(b, 0, b.Length); b.AsSpan(0, n).CopyTo(buffer.Span); cancellation.Cancel(); return ValueTask.FromResult(n); }
    }
    private sealed class CancelingOutput(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { byte[] b = buffer.ToArray(); base.Write(b, 0, b.Length); cancellation.Cancel(); return ValueTask.CompletedTask; }
    }
    private sealed class AsyncShortInput(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Sync read used.");
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Sync read used.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] small = new byte[Math.Min(3, buffer.Length)]; int n = base.Read(small, 0, small.Length); small.AsSpan(0, n).CopyTo(buffer.Span); return ValueTask.FromResult(n);
        }
    }
    private sealed class AsyncOutput : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Sync write used.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Sync write used.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); byte[] b = buffer.ToArray(); base.Write(b, 0, b.Length); return ValueTask.CompletedTask; }
    }
}
