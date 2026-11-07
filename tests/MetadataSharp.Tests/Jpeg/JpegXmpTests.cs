using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Jpeg;

public sealed class JpegXmpTests
{
    private static readonly XmpPropertyName Tool = new("http://ns.adobe.com/xap/1.0/", "CreatorTool");
    private static byte[] Segment(byte[] bytes) => JpegFixture.Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. bytes]);
    private static byte[] Raw(XmpPacket packet) { byte[] bytes = new byte[packet.ByteLength]; packet.CopyRawTo(bytes); return bytes; }
    private static XmpEdit Set(string value) { var edit = new XmpEdit(); edit.Set(Tool, XmpValue.CreateText(value)); return edit; }
    private static byte[] Packet(string value = "owned") => Raw(XmpPacket.CreateEmpty().Apply(Set(value))!);
    private static byte[] Save(byte[] image, XmpEdit edit)
    {
        using var source = new MemoryStream(image); using var doc = JpegMetadataDocument.Open(source);
        using var output = new MemoryStream(); doc.SaveXmp(output, edit); return output.ToArray();
    }
    [Fact]
    public void InsertReplaceRemovePreserveEncodedAndUnrelatedBytes()
    {
        byte[] prefix = [.. JpegFixture.Segment(0xE1, JpegFixture.Exif()), .. JpegFixture.Segment(0xE1, [9, 8, 7]), .. JpegFixture.Segment(0xED, [4, 5, 6])];
        byte[] original = JpegFixture.Build(prefix); byte[] inserted = Save(original, Set("first"));
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(inserted)))
        { Assert.Equal("first", doc.ReadXmp()!.Properties[Tool].Text); Assert.Single(doc.Exif!.Entries); }
        byte[] replaced = Save(inserted, Set("second"));
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(replaced))) Assert.Equal("second", doc.ReadXmp()!.Properties[Tool].Text);
        var remove = new XmpEdit(); remove.RemoveAllXmp(); Assert.Equal(original, Save(replaced, remove));
        Assert.Equal(replaced, Save(replaced, new()));
        using var scanSource = new MemoryStream(replaced); JpegScanResult scan = JpegScanner.Scan(scanSource, new());
        Assert.Equal(original[^13..], replaced[(int)scan.ScanOffset..]);
        Assert.Single(scan.Segments, s => s.IsStandardXmp);
    }
    [Fact]
    public void EmptyAndMissingRemovalDoNotInsertPacket()
    {
        byte[] original = JpegFixture.Build(); var remove = new XmpEdit(); remove.Remove(Tool);
        Assert.Equal(original, Save(original, remove)); remove = new(); remove.RemoveAllXmp(); Assert.Equal(original, Save(original, remove));
        using var doc = JpegMetadataDocument.Open(new MemoryStream(original)); Assert.Null(doc.ReadXmp()); Assert.False(doc.HasExtendedXmp);
    }
    [Theory]
    [InlineData(65502)]
    [InlineData(65503)]
    [InlineData(65504)]
    public void FullContainerCapacityIsReadableAndWritable(int size)
    {
        int overhead = Packet("").Length; byte[] result = Save(JpegFixture.Build(), Set(new string('a', size - overhead)));
        using var source = new MemoryStream(result); JpegScanResult scan = JpegScanner.Scan(source, new());
        Assert.Equal(size + 29, Assert.Single(scan.Segments, s => s.IsStandardXmp).PayloadLength);
        source.Position = 0; using var doc = JpegMetadataDocument.Open(source); Assert.Equal(size, doc.ReadXmp()!.ByteLength);
    }
    [Fact]
    public void OversizeRefusesBeforeCallerOutputOrPathPublication()
    {
        using var doc = JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build()));
        using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.XmpTooLarge, Assert.Throws<MetadataException>(() => doc.SaveXmp(output, Set(new string('a', 65505)))).Code);
        Assert.Empty(output.ToArray());
        string target = Path.Combine(Path.GetTempPath(), "xmp-oversize-" + Guid.NewGuid().ToString("N") + ".jpg");
        Assert.Equal(MetadataErrorCode.XmpTooLarge, Assert.Throws<MetadataException>(() => doc.SaveXmpAs(target, Set(new string('a', 65505)))).Code);
        Assert.False(File.Exists(target));
    }
    [Theory]
    [InlineData("http://ns.adobe.com/xap/1.0/")]
    [InlineData("http://ns.adobe.com/xap/1.0/\u0001")]
    [InlineData("HTTP://ns.adobe.com/xap/1.0/\0")]
    [InlineData("http://ns.adobe.com/xmp/extension/")]
    public void NearSignaturesRemainOpaque(string signature)
    {
        byte[] image = JpegFixture.Build(JpegFixture.Segment(0xE1, Encoding.UTF8.GetBytes(signature)));
        using var doc = JpegMetadataDocument.Open(new MemoryStream(image)); Assert.Null(doc.ReadXmp()); Assert.False(doc.HasExtendedXmp);
        var remove = new XmpEdit(); remove.RemoveAllXmp(); Assert.Equal(image, Save(image, remove));
    }
    [Fact]
    public void DuplicatePacketsOnlyBlockXmpOperations()
    {
        byte[] image = JpegFixture.Build([.. Segment(Packet()), .. Segment(Packet())]);
        using var doc = JpegMetadataDocument.Open(new MemoryStream(image));
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => doc.ReadXmp()).Code);
        using var output = new MemoryStream(); Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => doc.SaveXmp(output, Set("x"))).Code); Assert.Empty(output.ToArray());
        var remove = new XmpEdit(); remove.RemoveAllXmp(); Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => Save(image, remove)).Code);
        using var exifCopy = new MemoryStream(); doc.Save(exifCopy, new()); Assert.Equal(image, exifCopy.ToArray()); Assert.Equal(image, Save(image, new()));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnySignedExtensionBlocksXmpButNotExifOrExactCopy(bool withStandard)
    {
        byte[] extension = JpegFixture.Segment(0xE1, "http://ns.adobe.com/xmp/extension/\0"u8.ToArray());
        byte[] image = JpegFixture.Build([.. withStandard ? Segment(Packet()) : Array.Empty<byte>(), .. extension]);
        using var doc = JpegMetadataDocument.Open(new MemoryStream(image)); Assert.True(doc.HasExtendedXmp);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => doc.ReadXmp()).Code);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(image, Set("x"))).Code);
        var removal = new XmpEdit(); removal.RemoveAllXmp(); Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(image, removal)).Code);
        Assert.Equal(image, Save(image, new()));
        using var output = new MemoryStream(); doc.Save(output, new()); Assert.Equal(image, output.ToArray());
    }
    [Fact]
    public void DanglingExtensionReferencesAreRefusedExceptWholeRemoval()
    {
        byte[] xml = Encoding.UTF8.GetBytes("<r:RDF xmlns:r='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><r:Description r:about='' xmlns:n='http://ns.adobe.com/xmp/note/' n:HasExtendedXMP='0123456789ABCDEF0123456789ABCDEF'/></r:RDF>");
        byte[] image = JpegFixture.Build(Segment(xml));
        using var doc = JpegMetadataDocument.Open(new MemoryStream(image)); Assert.False(doc.HasExtendedXmp);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => doc.ReadXmp()).Code);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(image, Set("x"))).Code);
        var remove = new XmpEdit(); remove.RemoveAllXmp(); Assert.Equal(JpegFixture.Build(), Save(image, remove));
        var add = new XmpEdit(); add.Set(new("http://ns.adobe.com/xmp/note/", "HasExtendedXMP"), XmpValue.CreateText("stale"));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(JpegFixture.Build(), add)).Code);
    }
    [Theory]
    [InlineData("")]
    [InlineData("<malformed")]
    [InlineData("<!DOCTYPE rdf [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><rdf>&x;</rdf>")]
    public void MalformedStandardCanOnlyBeCopiedOrRemovedWhole(string xml)
    {
        byte[] image = JpegFixture.Build(Segment(Encoding.UTF8.GetBytes(xml)));
        using var doc = JpegMetadataDocument.Open(new MemoryStream(image));
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => doc.ReadXmp()).Code);
        Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => Save(image, Set("x"))).Code);
        Assert.Equal(image, Save(image, new())); var remove = new XmpEdit(); remove.RemoveAllXmp(); Assert.Equal(JpegFixture.Build(), Save(image, remove));
    }
    [Fact]
    public void ChangedSourceAndLogicalBudgetArePreflightFailures()
    {
        byte[] image = JpegFixture.Build(Segment(Packet())); using var source = new MemoryStream(image.ToArray()); using var doc = JpegMetadataDocument.Open(source);
        source.Position = source.Length - 3; source.WriteByte(0x2f);
        Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => doc.ReadXmp()).Code);
        using var output = new MemoryStream(); Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => doc.SaveXmp(output, new())).Code); Assert.Empty(output.ToArray());
        using var limited = JpegMetadataDocument.Open(new MemoryStream(image), new() { Limits = new() { MaxRetainedMetadataBytes = 2 } });
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => limited.ReadXmp()).Code);
        using var normal = JpegMetadataDocument.Open(new MemoryStream(image));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => normal.ReadXmp(new() { Limits = new() { MaxPacketBytes = 2 } })).Code);
    }
    [Fact]
    public async Task AsyncReadsWritesCancellationAndPathOwnership()
    {
        byte[] image = JpegFixture.Build(Segment(Packet()));
        using var source = new JpegScannerTests.ShortReads(image, true); await using var doc = await JpegMetadataDocument.OpenAsync(source);
        Assert.Equal("owned", (await doc.ReadXmpAsync())!.Properties[Tool].Text);
        using var output = new AsyncOutput(); await doc.SaveXmpAsync(output, Set("async"));
        using var check = JpegMetadataDocument.Open(new MemoryStream(output.ToArray())); Assert.Equal("async", check.ReadXmp()!.Properties[Tool].Text);
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveXmpAsync(empty, Set("x"), canceled.Token)); Assert.Empty(empty.ToArray());
        string dir = Path.Combine(Path.GetTempPath(), "jpeg-xmp-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string target = Path.Combine(dir, "result.jpg"); await doc.SaveXmpAsAsync(target, Set("path"));
            using (var pathDoc = JpegMetadataDocument.Open(target)) Assert.Equal("path", pathDoc.ReadXmp()!.Properties[Tool].Text);
            await Assert.ThrowsAsync<IOException>(() => doc.SaveXmpAsAsync(target, Set("overwrite"))); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void AliasingNonemptyAndDisposedDestinationsAreRejected()
    {
        using var source = new MemoryStream(JpegFixture.Build()); var doc = JpegMetadataDocument.Open(source);
        Assert.Throws<ArgumentException>(() => doc.SaveXmp(source, Set("x")));
        using var output = new MemoryStream(new byte[] { 7 }); Assert.Throws<ArgumentException>(() => doc.SaveXmp(output, Set("x"))); Assert.Equal(new byte[] { 7 }, output.ToArray());
        doc.Dispose(); Assert.True(source.CanRead); Assert.Throws<ObjectDisposedException>(() => doc.ReadXmp());
    }
    private sealed class AsyncOutput : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Sync write used.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Sync write used.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); byte[] b = buffer.ToArray(); base.Write(b, 0, b.Length); return ValueTask.CompletedTask; }
    }
}
