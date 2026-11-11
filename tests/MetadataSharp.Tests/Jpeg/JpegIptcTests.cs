using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Tests.Iptc;
using Xunit;

namespace MetadataSharp.Tests.Jpeg;

public sealed class JpegIptcTests
{
    private static IptcEdit Caption(string value) { var edit = new IptcEdit(); edit.ReplaceText(new(2, 120), [value]); return edit; }
    private static byte[] Save(byte[] bytes, IptcEdit edit)
    {
        using var doc = JpegMetadataDocument.Open(new MemoryStream(bytes)); using var output = new MemoryStream(); doc.SaveIptc(output, edit); return output.ToArray();
    }
    private static string? GetCaption(IptcPacket packet)
    { for (int i = 0; i < packet.Datasets.Count; i++) if (packet.Datasets[i].Key == new IptcDatasetKey(2, 120)) return packet.GetText(i); return null; }
    private static byte[] Photos(params byte[][] blocks) => JpegFixture.Segment(0xed, IptcCodecTests.Photoshop(blocks));
    private static PhotoshopResources Resources(byte[] jpeg)
    {
        using var source = new MemoryStream(jpeg); var scan = JpegScanner.Scan(source, new()); JpegSegment segment = Assert.Single(scan.Segments, s => s.IsPhotoshop);
        return PhotoshopResources.Parse(jpeg.AsSpan((int)segment.PayloadOffset, segment.PayloadLength).ToArray(), new());
    }
    [Fact]
    public void InsertReplaceRemoveAndNoOpAreIndependentOfExifXmpAndOpaqueApp13()
    {
        byte[] image = JpegFixture.Build([.. JpegFixture.Segment(0xe1, JpegFixture.Exif()), .. JpegFixture.Segment(0xe1, "http://ns.adobe.com/xap/1.0/\0<bad"u8.ToArray()), .. JpegFixture.Segment(0xed, [8, 9])]);
        byte[] added = Save(image, Caption("Привет"));
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(added))) { Assert.Single(doc.Exif!.Entries); Assert.Equal("Привет", GetCaption(doc.ReadIptc()!)); }
        byte[] changed = Save(added, Caption("new")); using (var doc = JpegMetadataDocument.Open(new MemoryStream(changed))) Assert.Equal("new", GetCaption(doc.ReadIptc()!));
        Assert.Equal(changed, Save(changed, new())); var remove = new IptcEdit(); remove.RemoveAllIptc(); Assert.Equal(image, Save(changed, remove));
        Assert.Equal(image[^13..], changed[^13..]);
    }
    [Fact]
    public void ExistingNamesPaddingResourcesAndDigestRemainConsistent()
    {
        byte[] packet = IptcCodecTests.Raw(IptcPacket.CreateEmpty()); byte[] unrelated = IptcCodecTests.Block(0x0424, [1, 2, 3], [65, 66], 0x7e);
        byte[] image = JpegFixture.Build(Photos(unrelated, IptcCodecTests.Block(0x0404, packet, [78]), IptcCodecTests.Block(0x0425, new byte[16], [68, 71])));
        byte[] result = Save(image, Caption("owned")); PhotoshopResources resources = Resources(result);
        Assert.Equal(unrelated, resources.Resources[0].Block.ToArray()); Assert.Equal(MD5.HashData(resources.Iptc!.Data.Span), resources.Resources[2].Data.ToArray());
        var remove = new IptcEdit(); remove.RemoveAllIptc(); Assert.Equal(JpegFixture.Build(Photos(unrelated)), Save(result, remove));
    }
    [Theory]
    [InlineData("Photoshop 3.0")]
    [InlineData("Photoshop 3.0\u0001")]
    [InlineData("photoshop 3.0\0")]
    public void NearSignatureIsUnrelatedOpaqueApp13(string signature)
    {
        byte[] image = JpegFixture.Build(JpegFixture.Segment(0xed, System.Text.Encoding.ASCII.GetBytes(signature)));
        using var doc = JpegMetadataDocument.Open(new MemoryStream(image)); Assert.Null(doc.ReadIptc()); var remove = new IptcEdit(); remove.RemoveAllIptc(); Assert.Equal(image, Save(image, remove));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateSegmentsOrResourcesRefuseEffectiveOperationsButNotNoOp(bool multipleSegments)
    {
        byte[] block = IptcCodecTests.Block(0x0404, IptcCodecTests.Raw(IptcPacket.CreateEmpty()));
        byte[] prefix = multipleSegments ? [.. Photos(block), .. Photos(block)] : Photos(block, block);
        byte[] image = JpegFixture.Build(prefix); using var doc = JpegMetadataDocument.Open(new MemoryStream(image));
        // The same stream twice is one stream to a reader; which copy an edit should change is still ambiguous.
        IptcPacket read = doc.ReadIptc()!; Assert.False(read.IsEditable); Assert.Equal(2, read.Datasets.Count);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => Save(image, Caption("x"))).Code);
        var remove = new IptcEdit(); remove.RemoveAllIptc(); Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => Save(image, remove)).Code);
        Assert.Equal(image, Save(image, new())); using var output = new MemoryStream(); doc.Save(output, new()); Assert.Equal(image, output.ToArray());
    }
    [Fact]
    public void FormsThatCanBeReadAndNotEditedAreReadOnly()
    {
        // Two streams that differ: nothing says which one is the file's.
        byte[] one = IptcCodecTests.Block(0x0404, IptcCodecTests.Dataset(2, 25, 65)), two = IptcCodecTests.Block(0x0404, IptcCodecTests.Dataset(2, 25, 66));
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build(Photos(one, two)))))
            Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => doc.ReadIptc()).Code);
        // A digest without a stream: there is nothing to read, and an edit is still refused.
        byte[] orphan = JpegFixture.Build(Photos(IptcCodecTests.Block(0x0425, new byte[16])));
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(orphan))) Assert.Null(doc.ReadIptc());
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(orphan, Caption("x"))).Code);
        // A digest twice beside one stream.
        byte[] digest = IptcCodecTests.Block(0x0425, new byte[16]);
        byte[] digests = JpegFixture.Build(Photos(one, digest, digest));
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(digests)))
        { IptcPacket packet = doc.ReadIptc()!; Assert.False(packet.IsEditable); Assert.Single(packet.Datasets); }
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => Save(digests, Caption("x"))).Code);
        // A zero byte after the last dataset.
        byte[] padded = JpegFixture.Build(Photos(IptcCodecTests.Block(0x0404, [.. IptcCodecTests.Dataset(2, 120, 104, 105), 0])));
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(padded)))
        { IptcPacket packet = doc.ReadIptc()!; Assert.False(packet.IsEditable); Assert.Equal("hi", GetCaption(packet)); }
        Assert.Equal(MetadataErrorCode.InvalidIptc, Assert.Throws<MetadataException>(() => Save(padded, Caption("x"))).Code);
        Assert.Equal(padded, Save(padded, new()));
    }
    [Fact]
    public void AResourceFramedWithAnotherAdobeSignatureIsReadAndEdited()
    {
        byte[] block = IptcCodecTests.Block(0x0404, IptcCodecTests.Raw(IptcPacket.CreateEmpty())); "DCSR"u8.CopyTo(block);
        byte[] image = JpegFixture.Build(Photos(block));
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(image))) Assert.True(doc.ReadIptc()!.IsEditable);
        byte[] changed = Save(image, Caption("new"));
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(changed))) Assert.Equal("new", GetCaption(doc.ReadIptc()!));
        Assert.Equal("DCSR"u8.ToArray(), Resources(changed).Resources[0].Block[..4].ToArray());
    }
    [Fact]
    public void MalformedIimCanBeRemovedButMalformedResourceBoundariesCannot()
    {
        byte[] image = JpegFixture.Build(Photos(IptcCodecTests.Block(0x0404, [0x1c]))); var remove = new IptcEdit(); remove.RemoveAllIptc();
        using (var doc = JpegMetadataDocument.Open(new MemoryStream(image))) Assert.Equal(MetadataErrorCode.InvalidIptc, Assert.Throws<MetadataException>(() => doc.ReadIptc()).Code);
        Assert.Equal(JpegFixture.Build(), Save(image, remove)); Assert.Equal(image, Save(image, new()));
        byte[] broken = JpegFixture.Build(JpegFixture.Segment(0xed, [.. "Photoshop 3.0\0"u8, 1, 2]));
        Assert.Equal(MetadataErrorCode.InvalidPhotoshopResources, Assert.Throws<MetadataException>(() => Save(broken, remove)).Code); Assert.Equal(broken, Save(broken, new()));
    }
    [Fact]
    public void EmptyAbsentRemovalAndInsertionIntoResourceOnlySegmentPreserveUntouchedBlocks()
    {
        byte[] unrelated = IptcCodecTests.Block(0x1234, [9]); byte[] source = JpegFixture.Build(Photos(unrelated));
        var missing = new IptcEdit(); missing.Remove(new(2, 25)); Assert.Equal(source, Save(source, missing));
        byte[] changed = Save(source, Caption("insert")); Assert.Equal(unrelated, Resources(changed).Resources[0].Block.ToArray());
        var remove = new IptcEdit(); remove.RemoveAllIptc(); Assert.Equal(source, Save(changed, remove));
    }
    [Theory]
    [InlineData(65505)]
    [InlineData(65506)]
    public void CapacityAccountsForEvenPadding(int packetSize)
    {
        int valueSize = packetSize - 22; var edit = new IptcEdit(); edit.ReplaceRaw(new(2, 250), [IptcValue.FromRaw(new byte[valueSize])]);
        byte[] output = Save(JpegFixture.Build(), edit); PhotoshopResources resources = Resources(output);
        Assert.Equal(packetSize, resources.Iptc!.Data.Length); Assert.Equal(65532, resources.Resources.Sum(r => r.Block.Length) + 14);
    }
    [Fact]
    public void OversizeAndWorkingBudgetsFailBeforeWrites()
    {
        byte[] original = JpegFixture.Build(); using var doc = JpegMetadataDocument.Open(new MemoryStream(original));
        var big = new IptcEdit(); big.ReplaceRaw(new(2, 250), [IptcValue.FromRaw(new byte[65485])]); using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.IptcTooLarge, Assert.Throws<MetadataException>(() => doc.SaveIptc(output, big)).Code); Assert.Empty(output.ToArray());
        using var limited = JpegMetadataDocument.Open(new MemoryStream(original), new() { Limits = new() { MaxRetainedMetadataBytes = 2 } });
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => limited.SaveIptc(output, Caption("x"))).Code); Assert.Empty(output.ToArray());
    }
    [Fact]
    public async Task ActualAsyncShortReadsSnapshotOwnershipAndCancellation()
    {
        byte[] original = Save(JpegFixture.Build(), Caption("async")); using var source = new JpegScannerTests.ShortReads(original, true);
        await using var doc = await JpegMetadataDocument.OpenAsync(source); IptcPacket packet = (await doc.ReadIptcAsync())!; Assert.Equal("async", GetCaption(packet));
        using var output = new AsyncOutput(); await doc.SaveIptcAsync(output, Caption("changed"));
        using var result = JpegMetadataDocument.Open(new MemoryStream(output.ToArray())); Assert.Equal("changed", GetCaption(result.ReadIptc()!));
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveIptcAsync(empty, Caption("x"), cancel.Token)); Assert.Empty(empty.ToArray());
        await doc.DisposeAsync(); Assert.Equal("async", GetCaption(packet)); Assert.True(source.CanRead);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PathCopyFailureOrPublicationRacePreservesTargetsAndCleansTemporary(bool competitor)
    {
        string dir = Path.Combine(Path.GetTempPath(), "iptc-save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string target = Path.Combine(dir, "new.jpg"); byte[] image = Save(JpegFixture.Build(), Caption("source"));
            using var source = new OnCopyInput(image); using var doc = JpegMetadataDocument.Open(source);
            source.Arm(() => { if (competitor) File.WriteAllBytes(target, [77]); else throw new IOException("Injected source copy failure."); });
            Assert.Throws<IOException>(() => doc.SaveIptcAs(target, new())); Assert.Empty(Directory.GetFiles(dir, "*.tmp")); Assert.Equal(image, source.ToArray());
            if (competitor) Assert.Equal(new byte[] { 77 }, File.ReadAllBytes(target)); else Assert.False(File.Exists(target));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task AsyncPathSaveNewTargetRefusesOverwriteAndSourceMutationOrAliasing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "iptc-path-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            byte[] original = JpegFixture.Build(); using var source = new MemoryStream(original.ToArray()); await using var doc = await JpegMetadataDocument.OpenAsync(source);
            string target = Path.Combine(dir, "new.jpg"); await doc.SaveIptcAsAsync(target, Caption("path"));
            using (var check = JpegMetadataDocument.Open(target)) Assert.Equal("path", GetCaption(check.ReadIptc()!));
            await Assert.ThrowsAsync<IOException>(() => doc.SaveIptcAsAsync(target, Caption("overwrite")));
            Assert.Throws<ArgumentException>(() => doc.SaveIptc(source, Caption("alias")));
            source.Position = source.Length - 3; source.WriteByte(0);
            Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => doc.ReadIptc()).Code);
            using var output = new MemoryStream(); Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => doc.SaveIptc(output, new())).Code); Assert.Empty(output.ToArray());
        }
        finally { Directory.Delete(dir, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutputFailureCanBePartialButSourceAndSnapshotStayUntouched(bool asynchronous)
    {
        byte[] image = Save(JpegFixture.Build(), Caption("source")); using var source = new MemoryStream(image.ToArray()); using var doc = JpegMetadataDocument.Open(source);
        IptcPacket snapshot = doc.ReadIptc()!; using var destination = new PartialOutput();
        if (asynchronous) await Assert.ThrowsAsync<IOException>(() => doc.SaveIptcAsync(destination, Caption("change")));
        else Assert.Throws<IOException>(() => doc.SaveIptc(destination, Caption("change")));
        Assert.True(destination.Length > 0); Assert.Equal(image, source.ToArray()); Assert.Equal("source", GetCaption(snapshot)); Assert.True(destination.CanWrite);
    }
    [Fact]
    public async Task CancellationAfterAsyncPreflightReadLeavesBorrowedOutputEmpty()
    {
        using var source = new CancelInput(JpegFixture.Build()); using var doc = JpegMetadataDocument.Open(source);
        using var cancel = new CancellationTokenSource(); source.Arm(cancel);
        using var output = new MemoryStream(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveIptcAsync(output, new(), cancel.Token));
        Assert.Empty(output.ToArray()); Assert.True(source.CanRead);
    }
    private sealed class PartialOutput : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        { byte[] bytes = buffer[..Math.Min(buffer.Length, 2)].ToArray(); base.Write(bytes, 0, bytes.Length); throw new IOException("Injected output failure."); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { byte[] bytes = buffer[..Math.Min(buffer.Length, 2)].ToArray(); base.Write(bytes, 0, bytes.Length); return ValueTask.FromException(new IOException("Injected async output failure.")); }
    }
    private sealed class CancelInput(byte[] bytes) : MemoryStream(bytes)
    {
        private CancellationTokenSource? cancel;
        internal void Arm(CancellationTokenSource token) => cancel = token;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { byte[] bytes = new byte[buffer.Length]; int count = base.Read(bytes, 0, bytes.Length); bytes.AsSpan(0, count).CopyTo(buffer.Span); cancel?.Cancel(); return ValueTask.FromResult(count); }
    }
    private sealed class OnCopyInput(byte[] bytes) : MemoryStream(bytes)
    {
        private Action? action; private int zeroSeeks;
        internal void Arm(Action callback) { action = callback; zeroSeeks = 0; }
        public override long Position { get => base.Position; set { base.Position = value; if (value == 0 && action is not null && ++zeroSeeks == 2) action(); } }
    }
    private sealed class AsyncOutput : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Sync write used.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Sync write used.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { byte[] bytes = buffer.ToArray(); base.Write(bytes, 0, bytes.Length); return ValueTask.CompletedTask; }
    }
    [Fact]
    public void AResourceBlockSplitAcrossTwoSegmentsIsReadButNotEdited()
    {
        var caption = new IptcEdit(); caption.ReplaceText(new(2, 120), ["split"]);
        IptcPacket packet = IptcPacket.CreateEmpty().Apply(caption)!;
        byte[] whole = IptcCodecTests.Photoshop(IptcCodecTests.Block(0x0404, IptcCodecTests.Raw(packet)));
        int cut = 14 + 9;
        byte[] first = whole[..cut];
        byte[] second = [.. "Photoshop 3.0 "u8, .. whole[cut..]];
        byte[] image = JpegFixture.Build([.. JpegFixture.Segment(0xed, first), .. JpegFixture.Segment(0xed, second)]);
        using var doc = JpegMetadataDocument.Open(new MemoryStream(image));
        IptcPacket? read = doc.ReadIptc();
        Assert.NotNull(read);
        Assert.False(read.IsEditable);
        Assert.Equal("split", GetCaption(read));
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => Save(image, Caption("x"))).Code);
    }
}
