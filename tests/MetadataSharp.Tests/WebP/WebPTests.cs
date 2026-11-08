using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.WebP;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.WebP;

public sealed class WebPTests
{
    private static readonly XmpPropertyName Tool = new("http://ns.adobe.com/xap/1.0/", "CreatorTool");
    private static XmpEdit Xmp(string value) { var e = new XmpEdit(); e.Set(Tool, XmpValue.CreateText(value)); return e; }
    private static ExifEdit Exif() { var e = new ExifEdit(); e.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [6, 0])); return e; }
    private static byte[] Save(byte[] input, XmpEdit edit) { using var doc = WebPMetadataDocument.Open(new MemoryStream(input)); using var output = new MemoryStream(); doc.SaveXmp(output, edit); return output.ToArray(); }
    private static byte[] Save(byte[] input, ExifEdit edit) { using var doc = WebPMetadataDocument.Open(new MemoryStream(input)); using var output = new MemoryStream(); doc.SaveExif(output, edit); return output.ToArray(); }
    private static (string Kind, byte[] Full, byte[] Body)[] Chunks(byte[] bytes)
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4)); Assert.Equal(bytes.Length - 8, (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        var result = new List<(string, byte[], byte[])>(); int at = 12;
        while (at < bytes.Length)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4)); int end = at + 8 + size + (size & 1);
            if ((size & 1) != 0) Assert.Equal(0, bytes[end - 1]);
            result.Add((Encoding.ASCII.GetString(bytes, at, 4), bytes[at..end], bytes[(at + 8)..(at + 8 + size)])); at = end;
        }
        Assert.Equal(bytes.Length, at); return result.ToArray();
    }
    private static byte[] Coded(byte[] bytes) => Chunks(bytes).Where(c => c.Kind is "VP8 " or "VP8L" or "ALPH" or "ICCP").SelectMany(c => c.Full).ToArray();
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void PromotionEditingRemovalAndCompleteCodedRanges(int kind)
    {
        byte[] original = WebPFixture.Image(kind); Assert.Equal(original, Save(original, new XmpEdit())); Assert.Equal(original, Save(original, new ExifEdit()));
        byte[] exif = Save(original, Exif()), xmp = Save(exif, Xmp("Привет")), replaced = Save(xmp, Xmp("replacement"));
        Assert.Equal(Coded(original), Coded(replaced));
        using (var doc = WebPMetadataDocument.Open(new MemoryStream(replaced))) { Assert.Single(doc.ReadExif()!.Entries); Assert.Equal("replacement", doc.ReadXmp()!.Properties[Tool].Text); }
        var removeXmp = new XmpEdit(); removeXmp.RemoveAllXmp(); Assert.Equal(exif, Save(replaced, removeXmp));
        var removeExif = new ExifEdit(); removeExif.RemoveAllExif(); byte[] removed = Save(exif, removeExif);
        Assert.Equal(Coded(original), Coded(removed)); Assert.Equal("VP8X", Chunks(removed)[0].Kind);
        Assert.Equal(kind is 2 or 3 ? 0x10 : 0, Chunks(removed)[0].Body[0]);
        Assert.Equal(JpegFixture.OrientationTiff[..18], Chunks(exif).Single(c => c.Kind == "EXIF").Body[..18]);
    }
    [Fact]
    public void LegacyExifWrapperIsRetainedOnNoOpAndRemovedOnRebuild()
    {
        byte[] original = WebPFixture.Extended(8, WebPFixture.Image(0)[12..], WebPFixture.Chunk("EXIF", [.. "Exif\0\0"u8, .. JpegFixture.OrientationTiff]));
        using (var doc = WebPMetadataDocument.Open(new MemoryStream(original))) Assert.Single(doc.ReadExif()!.Entries);
        Assert.Equal(original, Save(original, new ExifEdit())); byte[] changed = Save(original, Exif());
        Assert.Equal("II", Encoding.ASCII.GetString(Chunks(changed).Single(c => c.Kind == "EXIF").Body, 0, 2));
    }
    [Fact]
    public void FamiliesDuplicatesAndMalformedWholeRemovalStayIndependent()
    {
        byte[] original = WebPFixture.Extended(8, WebPFixture.Image(1)[12..], WebPFixture.Chunk("EXIF", [1]), WebPFixture.Chunk("EXIF", [2]));
        byte[] xmp = Save(original, Xmp("independent"));
        using (var doc = WebPMetadataDocument.Open(new MemoryStream(xmp))) { Assert.Equal("independent", doc.ReadXmp()!.Properties[Tool].Text); Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => doc.ReadExif()).Code); }
        byte[] malformed = WebPFixture.Extended(0xc, WebPFixture.Image(0)[12..], WebPFixture.Chunk("EXIF", JpegFixture.OrientationTiff), WebPFixture.Chunk("XMP ", [1]));
        using (var doc = WebPMetadataDocument.Open(new MemoryStream(malformed))) { Assert.Single(doc.ReadExif()!.Entries); Assert.Equal(MetadataErrorCode.InvalidXmp, Assert.Throws<MetadataException>(() => doc.ReadXmp()).Code); }
        var remove = new XmpEdit(); remove.RemoveAllXmp(); byte[] removed = Save(malformed, remove);
        using var check = WebPMetadataDocument.Open(new MemoryStream(removed)); Assert.Null(check.ReadXmp()); Assert.Single(check.ReadExif()!.Entries);
    }
    [Fact]
    public void UnknownChunksRefuseEffectiveEditsButNotNoOpsOrMissingRemovals()
    {
        byte[] original = WebPFixture.Riff(WebPFixture.Image(0)[12..], WebPFixture.Chunk("JUNK", [1, 2, 3]));
        Assert.Equal(original, Save(original, new XmpEdit())); var missing = new ExifEdit(); missing.Remove(new(ExifDirectoryKind.Ifd0, 0x013B)); Assert.Equal(original, Save(original, missing));
        using var doc = WebPMetadataDocument.Open(new MemoryStream(original)); using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => doc.SaveXmp(output, Xmp("x"))).Code); Assert.Empty(output.ToArray());
        byte[] withExif = WebPFixture.Extended(8, WebPFixture.Image(0)[12..], WebPFixture.Chunk("EXIF", JpegFixture.OrientationTiff), WebPFixture.Chunk("JUNK", [0]));
        Assert.Equal(withExif, Save(withExif, missing));
    }
    [Fact]
    public void MetadataMayBeBeforeImageAndOnlyAddressedBytesMove()
    {
        byte[] original = WebPFixture.Extended(8, WebPFixture.Chunk("EXIF", JpegFixture.OrientationTiff), WebPFixture.Image(0)[12..]);
        byte[] edited = Save(original, Exif()); Assert.Equal("EXIF", Chunks(edited)[1].Kind); Assert.Equal(Coded(original), Coded(edited));
        // The lossless alpha bit is only a hint: do not reject a VP8X discrepancy.
        byte[] hinted = WebPFixture.Extended(0, WebPFixture.Image(3)[12..]); Assert.Equal(hinted, Save(hinted, new XmpEdit()));
    }
    [Theory]
    [InlineData("ANIM")] [InlineData("ANMF")]
    public void AnimationChunksAreExplicitlyRefused(string kind)
    { Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => WebPMetadataDocument.Open(new MemoryStream(WebPFixture.Riff(WebPFixture.Image(0)[12..], WebPFixture.Chunk(kind, []))))).Code); }
    [Fact]
    public void StructureBoundsPaddingFlagsAndImageHeadersAreChecked()
    {
        byte[] original = WebPFixture.Image(3);
        for (int n = 0; n < original.Length; n++) Assert.Equal(MetadataErrorCode.InvalidWebP, Assert.Throws<MetadataException>(() => WebPMetadataDocument.Open(new MemoryStream(original[..n]))).Code);
        byte[] pad = original.ToArray(); pad[^1] = 1;
        byte[] version = original.ToArray(); version[24] |= 0x20;
        byte[] canvas = WebPFixture.Extended(0, WebPFixture.Image(0)[12..]); canvas[24] = 1;
        byte[] reserved = WebPFixture.Extended(0, WebPFixture.Image(0)[12..]); reserved[20] = 1;
        byte[][] invalid = [pad, version, canvas, reserved, [.. original, 0],
            WebPFixture.Extended(8, WebPFixture.Image(0)[12..]),
            WebPFixture.Extended(0, WebPFixture.Image(0)[12..], WebPFixture.Chunk("EXIF", [])),
            WebPFixture.Riff(WebPFixture.Image(0)[12..], WebPFixture.Image(0)[12..]),
            WebPFixture.Riff(WebPFixture.Chunk("XMP ", []), WebPFixture.Image(0)[12..]),
            WebPFixture.Extended(0x20, WebPFixture.Image(0)[12..], WebPFixture.Chunk("ICCP", [1])),
            WebPFixture.Extended(0x10, WebPFixture.Chunk("ALPH", [0]), WebPFixture.Image(0)[12..])];
        foreach (byte[] bytes in invalid) Assert.Equal(MetadataErrorCode.InvalidWebP, Assert.Throws<MetadataException>(() => WebPMetadataDocument.Open(new MemoryStream(bytes))).Code);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => WebPMetadataDocument.Open(new MemoryStream(WebPFixture.Extended(2, WebPFixture.Image(0)[12..])))).Code);
    }
    [Fact]
    public void IccAndAlphaAreOpaqueButTheirRangesAndOrderAreProtected()
    {
        byte[] original = WebPFixture.Extended(0x30, WebPFixture.Chunk("ICCP", [1, 2, 3]), WebPFixture.Image(2)[30..]);
        Assert.Equal(Coded(original), Coded(Save(original, Xmp("x"))));
        byte[] duplicate = WebPFixture.Extended(0x20, WebPFixture.Chunk("ICCP", []), WebPFixture.Chunk("ICCP", []), WebPFixture.Image(0)[12..]);
        Assert.Equal(MetadataErrorCode.InvalidWebP, Assert.Throws<MetadataException>(() => WebPMetadataDocument.Open(new MemoryStream(duplicate))).Code);
    }
    [Fact]
    public void LimitsAndOversizedExifFailBeforeOutput()
    {
        byte[] original = WebPFixture.Extended(8, WebPFixture.Image(1)[12..], WebPFixture.Chunk("EXIF", new byte[65528]));
        Assert.Equal(original, Save(original, new ExifEdit())); using var doc = WebPMetadataDocument.Open(new MemoryStream(original));
        Assert.Equal(MetadataErrorCode.ExifTooLarge, Assert.Throws<MetadataException>(() => doc.ReadExif()).Code);
        using var output = new MemoryStream(); Assert.Equal(MetadataErrorCode.ExifTooLarge, Assert.Throws<MetadataException>(() => doc.SaveExif(output, Exif())).Code); Assert.Empty(output.ToArray());
        var remove = new ExifEdit(); remove.RemoveAllExif(); using var removed = WebPMetadataDocument.Open(new MemoryStream(Save(original, remove))); Assert.Null(removed.ReadExif());
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => WebPMetadataDocument.Open(new MemoryStream(original), new() { Limits = new() { MaxInputBytes = 10 } })).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => WebPMetadataDocument.Open(new MemoryStream(original), new() { Limits = new() { MaxSegments = 1 } })).Code);
    }
    [Fact]
    public async Task ShortAsyncIoOwnershipCancellationIdentityAndPathRaces()
    {
        byte[] original = WebPFixture.Image(2); using var source = new JpegScannerTests.ShortReads(original, true);
        await using var doc = await WebPMetadataDocument.OpenAsync(source); using var output = new AsyncOutput(); await doc.SaveXmpAsync(output, Xmp("async"));
        using (var check = WebPMetadataDocument.Open(new MemoryStream(output.ToArray()))) Assert.Equal("async", check.ReadXmp()!.Properties[Tool].Text);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveExifAsync(empty, Exif(), cancel.Token)); Assert.Empty(empty.ToArray());
        await doc.DisposeAsync(); Assert.True(source.CanRead);
        using var mutable = new MemoryStream(original.ToArray()); using var changed = WebPMetadataDocument.Open(mutable); mutable.Position = mutable.Length - 1; mutable.WriteByte(0x55);
        Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => changed.ReadXmp()).Code);
        string dir = Path.Combine(Path.GetTempPath(), "webp-owned-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "out.webp"); using var raceSource = new RaceSource(original); using var raceDoc = WebPMetadataDocument.Open(raceSource);
            raceSource.Next = () => File.WriteAllText(path, "winner"); Assert.Throws<IOException>(() => raceDoc.SaveXmpAs(path, Xmp("race")));
            Assert.Equal("winner", File.ReadAllText(path)); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            string valid = Path.Combine(dir, "valid.webp"); await raceDoc.SaveXmpAsAsync(valid, Xmp("published")); Assert.True(File.Exists(valid));
        }
        finally { Directory.Delete(dir, true); }
    }
    private sealed class RaceSource(byte[] bytes) : MemoryStream(bytes)
    {
        internal Action? Next;
        public override int Read(Span<byte> buffer) { Action? action = Next; Next = null; action?.Invoke(); return base.Read(buffer); }
    }
    private sealed class AsyncOutput : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Sync write used.");
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Sync write used.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); byte[] bytes = buffer.ToArray(); base.Write(bytes, 0, bytes.Length); return ValueTask.CompletedTask; }
    }
}
