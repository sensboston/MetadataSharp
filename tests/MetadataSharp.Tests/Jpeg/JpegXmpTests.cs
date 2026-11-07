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
    [Fact]
    public void OpaqueRdfAndCombinedExifBudgetRemainConservative()
    {
        byte[] xml = Encoding.UTF8.GetBytes("<r:RDF xmlns:r='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><r:Description r:about='' xmlns:p='urn:owned'><p:structure r:parseType='Resource'><p:field>keep</p:field></p:structure></r:Description></r:RDF>");
        byte[] image = JpegFixture.Build(Segment(xml));
        using var doc = JpegMetadataDocument.Open(new MemoryStream(image)); Assert.False(doc.ReadXmp()!.IsEditable);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(image, Set("loss"))).Code);
        var remove = new XmpEdit(); remove.RemoveAllXmp(); Assert.Equal(JpegFixture.Build(), Save(image, remove));
        byte[] combined = JpegFixture.Build([.. JpegFixture.Segment(0xe1, JpegFixture.Exif()), .. Segment(Packet())]);
        using var limited = JpegMetadataDocument.Open(new MemoryStream(combined), new() { Limits = new() { MaxRetainedMetadataBytes = JpegFixture.OrientationTiff.Length + Packet().Length - 1 } });
        Assert.NotNull(limited.Exif); Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => limited.ReadXmp()).Code);
    }
    [Fact]
    public void LateStandardPacketIsReplacedInPlaceWithoutReorderingOtherMarkers()
    {
        byte[] baseline = JpegFixture.Build(); using var scanInput = new MemoryStream(baseline);
        JpegScanResult scan = JpegScanner.Scan(scanInput, new()); int at = (int)scan.ScanOffset;
        byte[] source = [.. baseline[..at], .. Segment(Packet()), .. baseline[at..]];
        byte[] output = Save(source, Set("late")); using var checkInput = new MemoryStream(output);
        JpegScanResult changed = JpegScanner.Scan(checkInput, new()); JpegSegment xmp = Assert.Single(changed.Segments, s => s.IsStandardXmp);
        Assert.Equal(at, xmp.Start); Assert.Equal(baseline[..at], output[..at]); Assert.Equal(baseline[at..], output[(int)(xmp.Start + xmp.Length)..]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PathFailureOrCompetingCreatorCleansTemporaryAndPreservesSource(bool competitor)
    {
        string dir = Path.Combine(Path.GetTempPath(), "jpeg-xmp-failure-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string target = Path.Combine(dir, "target.jpg"); byte[] original = JpegFixture.Build(Segment(Packet()));
            using var source = new OnCopyInput(original); using var doc = JpegMetadataDocument.Open(source);
            source.Arm(() => { if (competitor) File.WriteAllBytes(target, [77]); else throw new IOException("Injected source copy failure."); });
            Assert.Throws<IOException>(() => doc.SaveXmpAs(target, new())); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.Equal(original, source.ToArray());
            if (competitor) Assert.Equal(new byte[] { 77 }, File.ReadAllBytes(target)); else Assert.False(File.Exists(target));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void ExifOnlyEditsPreserveMalformedAndExtendedXmpAsOpaqueBytes()
    {
        byte[] source = JpegFixture.Build([.. Segment(Encoding.UTF8.GetBytes("<bad")), .. JpegFixture.Segment(0xe1, "http://ns.adobe.com/xmp/extension/\0"u8.ToArray())]);
        using var doc = JpegMetadataDocument.Open(new MemoryStream(source)); var edit = new ExifEdit();
        edit.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [1, 0]));
        using var output = new MemoryStream(); doc.Save(output, edit);
        using var edited = JpegMetadataDocument.Open(new MemoryStream(output.ToArray())); var removal = new ExifEdit(); removal.RemoveAllExif();
        using var restored = new MemoryStream(); edited.Save(restored, removal); Assert.Equal(source, restored.ToArray());
    }
    private sealed class OnCopyInput(byte[] bytes) : MemoryStream(bytes)
    {
        private Action? action; private int zeroSeeks;
        internal void Arm(Action callback) { action = callback; zeroSeeks = 0; }
        public override long Position
        {
            get => base.Position;
            set { base.Position = value; if (value == 0 && action is not null && ++zeroSeeks == 2) action(); }
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
