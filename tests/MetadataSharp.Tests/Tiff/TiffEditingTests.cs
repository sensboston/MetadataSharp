using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class TiffEditingTests
{
    private static ExifValue Text(string text) => ExifValue.FromRaw(TiffFieldType.Ascii, (uint)Encoding.ASCII.GetByteCount(text) + 1, Endianness.LittleEndian, Encoding.ASCII.GetBytes(text + "\0"));
    private static TiffMetadataEdit Edit(uint offset = 8, string text = "new software") { var edit = new TiffMetadataEdit(); edit.Set(new(offset, 305), Text(text)); return edit; }
    private static byte[] Save(byte[] input, TiffMetadataEdit edit, TiffEditOptions? options = null)
    { using var doc = TiffMetadataDocument.Open(new MemoryStream(input), options); using var output = new MemoryStream(); doc.Save(output, edit); return output.ToArray(); }
    private static string Read(ExifValue value) { byte[] bytes = new byte[value.ByteLength]; value.CopyRawTo(bytes); return Encoding.ASCII.GetString(bytes).TrimEnd('\0'); }
    private static void Software(StandaloneTiffFixture f, int offset, (ushort Tag, ushort Type, uint[] Values)[]? fields = null, uint next = 0)
    {
        var all = (fields ?? []).Append(((ushort)305, (ushort)2, new uint[] { 0 })).ToArray(); f.Directory(offset, all, next);
        int entry = offset + 2 + 12 * (all.Length - 1); f.U32(entry + 4, 4); "old\0"u8.CopyTo(f.Bytes.AsSpan(entry + 8));
    }
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void AppendGraphPreservesEveryOriginalByteExceptHeaderPointer(bool big, bool tiles)
    {
        var f = StandaloneTiffFixture.Create(big, tiles); Software(f, 8, StandaloneTiffFixture.Fields(tiles));
        byte[] output = Save(f.Bytes, Edit());
        Assert.Equal(f.Bytes[..4], output[..4]); Assert.Equal(f.Bytes[8..], output[8..f.Bytes.Length]); Assert.True(output.Length > f.Bytes.Length);
        using var doc = TiffMetadataDocument.Open(new MemoryStream(output)); Assert.True(doc.Layout.IsSupported);
        Assert.True(doc.Layout.FirstIfdOffset >= f.Bytes.Length); Assert.Equal("new software", Read(doc.ReadValue(new(doc.Layout.FirstIfdOffset, 305))!));
        Assert.Equal(new TiffByteRange(2048, 256), Assert.Single(doc.Layout.Directories.Single().Image!.Segments));
        var remove = new TiffMetadataEdit(); remove.Remove(new(doc.Layout.FirstIfdOffset, 305)); byte[] removed = Save(output, remove);
        using var clean = TiffMetadataDocument.Open(new MemoryStream(removed)); Assert.Null(clean.ReadValue(new(clean.Layout.FirstIfdOffset, 305)));
        Assert.Equal(output[8..], removed[8..output.Length]); // Old software is still physically present.
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RewritesAllDirectoryLinksAndRetainsSharedDagTargets(bool big)
    {
        var f = StandaloneTiffFixture.Create(big);
        Software(f, 8, [.. StandaloneTiffFixture.Fields(), (330, 4, [512, 768]), (34665, 4, [1024]), (34853, 4, [1280])], 512);
        Software(f, 512, [(40965, 4, [1280])]); Software(f, 768, [(330, 4, [1024, 1280])]); Software(f, 1024); Software(f, 1280);
        // The fixture helper's default external area (768+256) collides with IFD 1024.
        // Place this pointer array in an explicitly free, independent region instead.
        f.U32(768 + 2 + 8, 1536); f.U32(1536, 1024); f.U32(1540, 1280);
        Assert.True(TiffLayout.Read(new MemoryStream(f.Bytes)).IsSupported);
        byte[] output = Save(f.Bytes, Edit(1280, "shared child"));
        using var doc = TiffMetadataDocument.Open(new MemoryStream(output)); Assert.Equal(5, doc.Layout.Directories.Count);
        Assert.All(doc.Layout.Directories, d => Assert.True(d.Offset >= f.Bytes.Length));
        var targets = doc.Layout.Directories.SelectMany(d => d.Links).ToArray(); Assert.Equal(8, targets.Length);
        Assert.All(targets, link => Assert.Contains(doc.Layout.Directories, d => d.Offset == link.TargetIfdOffset));
        var shared = doc.Layout.Directories.Single(d => targets.Count(t => t.TargetIfdOffset == d.Offset) == 3);
        Assert.Equal("shared child", Read(doc.ReadValue(new(shared.Offset, 305))!));
        Assert.Equal(f.Bytes[8..], output[8..f.Bytes.Length]);
    }
    [Fact]
    public void NoOpsRemainExactEvenWithOpaqueOtherFields()
    {
        var f = StandaloneTiffFixture.Create(); Software(f, 8, [.. StandaloneTiffFixture.Fields(), (700, 7, [0])]);
        Assert.Equal(f.Bytes, Save(f.Bytes, new())); Assert.Equal(f.Bytes, Save(f.Bytes, Edit(text: "old")));
        var remove = new TiffMetadataEdit(); remove.Remove(new(8, 315)); Assert.Equal(f.Bytes, Save(f.Bytes, remove));
        using var doc = TiffMetadataDocument.Open(new MemoryStream(f.Bytes)); using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Assert.Throws<MetadataException>(() => doc.Save(output, Edit())).Code); Assert.Empty(output.ToArray());
    }
    [Fact]
    public void MalformedTargetCanOnlyBeRemovedAndLastFieldCannotBeRemoved()
    {
        var f = StandaloneTiffFixture.Create(); Software(f, 8, [.. StandaloneTiffFixture.Fields(), (274, 3, [0])], 512); Software(f, 512);
        using var doc = TiffMetadataDocument.Open(new MemoryStream(f.Bytes)); using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => doc.ReadValue(new(8, 274))).Code);
        var last = new TiffMetadataEdit(); last.Remove(new(512, 305));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => doc.Save(output, last)).Code); Assert.Empty(output.ToArray());
        var remove = new TiffMetadataEdit(); remove.Remove(new(8, 274)); using var clean = TiffMetadataDocument.Open(new MemoryStream(Save(f.Bytes, remove))); Assert.Null(clean.ReadValue(new(clean.Layout.FirstIfdOffset, 274)));
    }
    [Fact]
    public void TypedArgumentsSourceKeysAndOutputCeilingsPreflight()
    {
        var f = StandaloneTiffFixture.Create(); using var doc = TiffMetadataDocument.Open(new MemoryStream(f.Bytes), new() { MaxOutputBytes = f.Bytes.Length }); using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => doc.Save(output, Edit())).Code); Assert.Empty(output.ToArray());
        Assert.Throws<ArgumentException>(() => new TiffMetadataEdit().Set(new(8, 273), Text("offset")));
        Assert.Throws<ArgumentException>(() => new TiffMetadataEdit().Set(new(8, 306), Text("2026:99:99 99:99:99")));
        Assert.Throws<ArgumentException>(() => new TiffMetadataEdit().Set(new(8, 305), ExifValue.FromRaw(TiffFieldType.Ascii, 2, Endianness.LittleEndian, [0xff, 0])));
        using var unrestricted = TiffMetadataDocument.Open(new MemoryStream(f.Bytes)); Assert.Throws<ArgumentException>(() => unrestricted.Save(output, Edit(1024))); Assert.Empty(output.ToArray());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OrientationConvertsOrderAndEquivalentSetDoesNotGrow(bool big)
    {
        var f = StandaloneTiffFixture.Create(big); var edit = new TiffMetadataEdit();
        edit.Set(new(8, 274), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.BigEndian, [0, 6])); byte[] output = Save(f.Bytes, edit);
        using var doc = TiffMetadataDocument.Open(new MemoryStream(output)); ExifValue orientation = doc.ReadValue(new(doc.Layout.FirstIfdOffset, 274))!;
        Assert.Equal(big ? Endianness.BigEndian : Endianness.LittleEndian, orientation.ByteOrder);
        var same = new TiffMetadataEdit(); same.Set(new(doc.Layout.FirstIfdOffset, 274), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [6, 0])); Assert.Equal(output, Save(output, same));
    }
    [Fact]
    public async Task ShortActualAsyncIoSnapshotsSourceIdentityAndNewPathPublication()
    {
        var f = StandaloneTiffFixture.Create(); using var source = new JpegScannerTests.ShortReads(f.Bytes, true);
        await using var doc = await TiffMetadataDocument.OpenAsync(source); using var output = new AsyncOutput(); await doc.SaveAsync(output, Edit());
        ExifValue value; await using (var read = await TiffMetadataDocument.OpenAsync(new MemoryStream(output.ToArray()))) value = (await read.ReadValueAsync(new(read.Layout.FirstIfdOffset, 305)))!;
        Assert.Equal("new software", Read(value)); using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveAsync(empty, Edit(), cancel.Token)); Assert.Empty(empty.ToArray());
        string dir = Path.Combine(Path.GetTempPath(), "tiff-edit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try { string target = Path.Combine(dir, "out.tif"); await doc.SaveAsAsync(target, Edit()); Assert.Throws<IOException>(() => doc.SaveAs(target, Edit())); Assert.Empty(Directory.GetFiles(dir, "*.tmp")); }
        finally { Directory.Delete(dir, true); }
        await doc.DisposeAsync(); Assert.True(source.CanRead);
        using var mutable = new MemoryStream(f.Bytes.ToArray()); using var changed = TiffMetadataDocument.Open(mutable); mutable.Position = 2048; mutable.WriteByte(255);
        Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => changed.Save(empty, new())).Code); Assert.Empty(empty.ToArray());
    }
    private sealed class AsyncOutput : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> bytes) => throw new InvalidOperationException("Sync output used.");
        public override void Write(byte[] bytes, int offset, int count) => throw new InvalidOperationException("Sync output used.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); byte[] copy = bytes.ToArray(); base.Write(copy, 0, copy.Length); return ValueTask.CompletedTask; }
    }
}
