using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Png;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Png;

public sealed class PngTests
{
    private static readonly XmpPropertyName Tool = new("http://ns.adobe.com/xap/1.0/", "CreatorTool");
    private static XmpEdit Set(string value) { var edit = new XmpEdit(); edit.Set(Tool, XmpValue.CreateText(value)); return edit; }
    private static byte[] Packet(string value = "owned") { XmpPacket packet = XmpPacket.CreateEmpty().Apply(Set(value))!; byte[] bytes = new byte[packet.ByteLength]; packet.CopyRawTo(bytes); return bytes; }
    private static byte[] Save(byte[] bytes, XmpEdit edit)
    { using var doc = PngMetadataDocument.Open(new MemoryStream(bytes)); using var output = new MemoryStream(); doc.SaveXmp(output, edit); return output.ToArray(); }
    private static byte[] ExifSave(byte[] bytes, ExifEdit edit)
    { using var doc = PngMetadataDocument.Open(new MemoryStream(bytes)); using var output = new MemoryStream(); doc.SaveExif(output, edit); return output.ToArray(); }
    [Fact]
    public void KnownCrcVectorAndExactNoOp()
    {
        Assert.Equal(0xcbf43926u, ~Crc.Update(uint.MaxValue, "123456789"u8));
        Assert.Equal(Convert.FromHexString("0000000049454E44AE426082"), PngFixture.Chunk("IEND", []));
        byte[] bytes = PngFixture.Build(); Assert.Equal(bytes, Save(bytes, new())); Assert.Equal(bytes, ExifSave(bytes, new()));
    }
    [Theory]
    [InlineData("IHDR")]
    [InlineData("IDAT")]
    [InlineData("IEND")]
    [InlineData("iTXt")]
    [InlineData("vpAg")]
    public void CorruptCrcIsAlwaysRefused(string type)
    {
        byte[] bytes = PngFixture.Build([.. PngFixture.Xmp(Packet()), .. PngFixture.Chunk("vpAg", [1])]);
        int at = 8;
        while (Encoding.ASCII.GetString(bytes, at + 4, 4) != type) at += (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at)) + 12;
        bytes[at + 8 + (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at))] ^= 1;
        Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => PngMetadataDocument.Open(new MemoryStream(bytes))).Code);
    }
    [Fact]
    public void ExifAndXmpInsertReplaceRemovePreserveEveryOtherChunk()
    {
        byte[] original = PngFixture.Build(PngFixture.Chunk("vpAg", [7, 8]));
        var edit = new ExifEdit(); edit.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [1, 0]));
        byte[] exif = ExifSave(original, edit); using (var doc = PngMetadataDocument.Open(new MemoryStream(exif))) Assert.Single(doc.ReadExif()!.Entries);
        byte[] xmp = Save(exif, Set("first")); using (var doc = PngMetadataDocument.Open(new MemoryStream(xmp))) { Assert.Single(doc.ReadExif()!.Entries); Assert.Equal("first", doc.ReadXmp()!.Properties[Tool].Text); }
        byte[] replaced = Save(xmp, Set("second")); var removeXmp = new XmpEdit(); removeXmp.RemoveAllXmp(); Assert.Equal(exif, Save(replaced, removeXmp));
        var removeExif = new ExifEdit(); removeExif.RemoveAllExif(); Assert.Equal(original, ExifSave(exif, removeExif));
        using var stream = new MemoryStream(xmp); PngScan scan = PngScanner.Scan(stream, new(), false, default).GetAwaiter().GetResult();
        PngChunk target = Assert.Single(scan.Chunks, c => c.Type == "eXIf"); Assert.Equal(JpegFixture.OrientationTiff, xmp.AsSpan((int)target.Payload, target.Size).ToArray());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompressedAndPostIdatXmpDecodeAndCanonicalizeOnlyOnEdit(bool compressed)
    {
        byte[] original = PngFixture.Build(after: PngFixture.Xmp(Packet("Привет"), compressed));
        using (var doc = PngMetadataDocument.Open(new MemoryStream(original))) Assert.Equal("Привет", doc.ReadXmp()!.Properties[Tool].Text);
        Assert.Equal(original, Save(original, new())); byte[] changed = Save(original, Set("edited"));
        using var source = new MemoryStream(changed); var scan = PngScanner.Scan(source, new(), false, default).GetAwaiter().GetResult();
        PngChunk xmp = Assert.Single(scan.Chunks, c => c.Xmp), idat = Assert.Single(scan.Chunks, c => c.Type == "IDAT");
        Assert.True(xmp.Start > idat.Start); Assert.Equal(0, changed[(int)xmp.Payload + 18]);
        Assert.Equal(original.AsSpan(8, (int)idat.Start - 8 + (int)idat.Length).ToArray(), changed.AsSpan(8, (int)idat.Start - 8 + (int)idat.Length).ToArray());
    }
    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void BoundedZlibHandlesStoredFixedAndDynamicStreams(CompressionLevel level)
    {
        byte[] text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("owned XML Привет 0123456789 ", 1000)));
        byte[] compressed = PngFixture.Compress(text, level);
        Assert.Equal(text, BoundedZlib.Expand(compressed, text.Length, default));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => BoundedZlib.Expand(compressed, text.Length - 1, default)).Code);
        Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => BoundedZlib.Expand([.. compressed, 0], text.Length, default)).Code);
        Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => BoundedZlib.Expand(compressed[..^1], text.Length, default)).Code);
        compressed[^1] ^= 1; Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => BoundedZlib.Expand(compressed, text.Length, default)).Code);
    }
    [Fact]
    public void KeywordFieldsEncodingAndCompressedBudgetsAreNotGuessed()
    {
        byte[] unsupported = PngFixture.Build(PngFixture.Xmp(Packet(), language: "en"u8.ToArray()));
        using (var doc = PngMetadataDocument.Open(new MemoryStream(unsupported))) Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => doc.ReadXmp()).Code);
        var remove = new XmpEdit(); remove.RemoveAllXmp(); Assert.Equal(PngFixture.Build(), Save(unsupported, remove));
        byte[] malformed = PngFixture.Build(PngFixture.Chunk("iTXt", "XML:com.adobe.xmp\0"u8.ToArray()));
        using (var doc = PngMetadataDocument.Open(new MemoryStream(malformed))) Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => doc.ReadXmp()).Code);
        Assert.Equal(PngFixture.Build(), Save(malformed, remove));
        byte[] compressed = PngFixture.Build(PngFixture.Xmp(Packet(), true));
        using var limited = PngMetadataDocument.Open(new MemoryStream(compressed), new() { MaxDecompressedXmpBytes = 4 });
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => limited.ReadXmp()).Code);
        using var encoded = PngMetadataDocument.Open(new MemoryStream(compressed), new() { MaxCompressedXmpBytes = 2 });
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => encoded.ReadXmp()).Code);
        using var uncompressed = PngMetadataDocument.Open(new MemoryStream(PngFixture.Build(PngFixture.Xmp(Packet(), method: 99))), new() { MaxCompressedXmpBytes = 1 });
        Assert.Equal("owned", uncompressed.ReadXmp()!.Properties[Tool].Text);
    }
    [Fact]
    public void UnknownUnsafeAndCriticalAndAnimationPoliciesAreExplicit()
    {
        byte[] unsafePng = PngFixture.Build(PngFixture.Chunk("vpAG", [1])); Assert.Equal(unsafePng, Save(unsafePng, new()));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(unsafePng, Set("x"))).Code);
        foreach (string type in new[] { "VpAG", "acTL", "fcTL", "fdAT" })
            Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => PngMetadataDocument.Open(new MemoryStream(PngFixture.Build(PngFixture.Chunk(type, []))))).Code);
    }
    [Fact]
    public void DuplicateAndMalformedMetadataAreIsolatedPerFamily()
    {
        byte[] duplicate = PngFixture.Build([.. PngFixture.Xmp(Packet()), .. PngFixture.Xmp(Packet())]);
        using (var doc = PngMetadataDocument.Open(new MemoryStream(duplicate))) Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => doc.ReadXmp()).Code);
        Assert.Equal(duplicate, Save(duplicate, new()));
        byte[] brokenExif = PngFixture.Build(PngFixture.Chunk("eXIf", [1]));
        byte[] xmp = Save(brokenExif, Set("independent"));
        using (var doc = PngMetadataDocument.Open(new MemoryStream(xmp))) { Assert.Equal("independent", doc.ReadXmp()!.Properties[Tool].Text); Assert.Equal(MetadataErrorCode.InvalidExif, Assert.Throws<MetadataException>(() => doc.ReadExif()).Code); }
        var remove = new ExifEdit(); remove.RemoveAllExif(); Assert.Equal(PngFixture.Build(), ExifSave(brokenExif, remove));
    }
    [Fact]
    public void StructuralOrderTruncationAndCountsAreChecked()
    {
        byte[] bytes = PngFixture.Build();
        for (int n = 0; n < bytes.Length; n++) Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => PngMetadataDocument.Open(new MemoryStream(bytes[..n]))).Code);
        Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => PngMetadataDocument.Open(new MemoryStream([.. bytes, 0]))).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => PngMetadataDocument.Open(new MemoryStream(bytes), new() { Limits = new() { MaxSegments = 1 } })).Code);
        byte[] lateExif = PngFixture.Build(after: PngFixture.Chunk("eXIf", JpegFixture.OrientationTiff));
        Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => PngMetadataDocument.Open(new MemoryStream(lateExif))).Code);
        byte[] splitIdat = PngFixture.Build(after: [.. PngFixture.Chunk("tEXt", "note\0owned"u8.ToArray()), .. PngFixture.Chunk("IDAT", [])]);
        Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => PngMetadataDocument.Open(new MemoryStream(splitIdat))).Code);
    }
    [Fact]
    public async Task AsyncShortIoOwnershipIdentityAndPublication()
    {
        byte[] original = PngFixture.Build(); using var source = new JpegScannerTests.ShortReads(original, true);
        await using var doc = await PngMetadataDocument.OpenAsync(source); using var output = new AsyncOutput(); await doc.SaveXmpAsync(output, Set("async"));
        using (var check = PngMetadataDocument.Open(new MemoryStream(output.ToArray()))) Assert.Equal("async", check.ReadXmp()!.Properties[Tool].Text);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveXmpAsync(empty, Set("x"), cancel.Token)); Assert.Empty(empty.ToArray());
        string dir = Path.Combine(Path.GetTempPath(), "png-owned-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try { string target = Path.Combine(dir, "out.png"); await doc.SaveXmpAsAsync(target, Set("path")); Assert.Throws<IOException>(() => doc.SaveXmpAs(target, Set("overwrite"))); Assert.Empty(Directory.GetFiles(dir, "*.tmp")); }
        finally { Directory.Delete(dir, true); }
        await doc.DisposeAsync(); Assert.True(source.CanRead);
        using var mutable = new MemoryStream(original.ToArray()); using var changed = PngMetadataDocument.Open(mutable); mutable.Position = 8; mutable.WriteByte(0);
        // Use an actual mutation, not an identical high byte in the IHDR length.
        mutable.Position = mutable.Length - 1; mutable.WriteByte(0); Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => changed.ReadXmp()).Code);
    }
    private sealed class AsyncOutput : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Sync write used.");
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Sync write used.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); byte[] bytes = buffer.ToArray(); base.Write(bytes, 0, bytes.Length); return ValueTask.CompletedTask; }
    }
}
