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
    public async Task ExifAndXmpInsertReplaceRemovePreserveEveryOtherChunk()
    {
        byte[] original = PngFixture.Build(PngFixture.Chunk("vpAg", [7, 8]));
        var edit = new ExifEdit(); edit.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [1, 0]));
        byte[] exif = ExifSave(original, edit); using (var doc = PngMetadataDocument.Open(new MemoryStream(exif))) Assert.Single(doc.ReadExif()!.Entries);
        byte[] xmp = Save(exif, Set("first")); using (var doc = PngMetadataDocument.Open(new MemoryStream(xmp))) { Assert.Single(doc.ReadExif()!.Entries); Assert.Equal("first", doc.ReadXmp()!.Properties[Tool].Text); }
        byte[] replaced = Save(xmp, Set("second")); var removeXmp = new XmpEdit(); removeXmp.RemoveAllXmp(); Assert.Equal(exif, Save(replaced, removeXmp));
        var removeExif = new ExifEdit(); removeExif.RemoveAllExif(); Assert.Equal(original, ExifSave(exif, removeExif));
        using var stream = new MemoryStream(xmp); PngScan scan = await PngScanner.Scan(stream, new(), false, default);
        PngChunk target = Assert.Single(scan.Chunks, c => c.Type == "eXIf"); Assert.Equal(JpegFixture.OrientationTiff, xmp.AsSpan((int)target.Payload, target.Size).ToArray());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompressedAndPostIdatXmpDecodeAndCanonicalizeOnlyOnEdit(bool compressed)
    {
        byte[] original = PngFixture.Build(after: PngFixture.Xmp(Packet("Привет"), compressed));
        using (var doc = PngMetadataDocument.Open(new MemoryStream(original))) Assert.Equal("Привет", doc.ReadXmp()!.Properties[Tool].Text);
        Assert.Equal(original, Save(original, new())); byte[] changed = Save(original, Set("edited"));
        using var source = new MemoryStream(changed); var scan = await PngScanner.Scan(source, new(), false, default);
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
    [Fact]
    public void OtherFamilyDuplicatesDoNotBecomeSemanticDependencies()
    {
        byte[] original = PngFixture.Build([.. PngFixture.Chunk("eXIf", [1]), .. PngFixture.Chunk("eXIf", [2])]);
        byte[] edited = Save(original, Set("isolated"));
        using var doc = PngMetadataDocument.Open(new MemoryStream(edited));
        Assert.Equal("isolated", doc.ReadXmp()!.Properties[Tool].Text);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => doc.ReadExif()).Code);
    }
    [Fact]
    public void OversizedExifIsCopyOrWholeRemovalOnly()
    {
        byte[] original = PngFixture.Build(PngFixture.Chunk("eXIf", new byte[65528]));
        Assert.Equal(original, ExifSave(original, new()));
        using var doc = PngMetadataDocument.Open(new MemoryStream(original));
        Assert.Equal(MetadataErrorCode.ExifTooLarge, Assert.Throws<MetadataException>(() => doc.ReadExif()).Code);
        var remove = new ExifEdit(); remove.RemoveAllExif(); Assert.Equal(PngFixture.Build(), ExifSave(original, remove));
        using var output = new MemoryStream(); var edit = new ExifEdit(); edit.Remove(new(ExifDirectoryKind.Ifd0, 0x0112));
        Assert.Equal(MetadataErrorCode.ExifTooLarge, Assert.Throws<MetadataException>(() => doc.SaveExif(output, edit)).Code); Assert.Empty(output.ToArray());
    }
    [Theory]
    [InlineData(0, 1, 0, false, true)]
    [InlineData(2, 8, 1, false, true)]
    [InlineData(3, 1, 0, true, true)]
    [InlineData(3, 1, 0, false, false)]
    [InlineData(4, 16, 0, false, true)]
    [InlineData(6, 8, 0, false, true)]
    [InlineData(2, 1, 0, false, false)]
    [InlineData(6, 8, 2, false, false)]
    public void IhdrAndPaletteProfilesAreExplicit(byte color, byte depth, byte interlace, bool palette, bool valid)
    {
        byte[] baseline = PngFixture.Build();
        byte[] bytes = [.. baseline[..8], .. PngFixture.Chunk("IHDR", PngFixture.Header(color, depth, interlace)),
            .. palette ? PngFixture.Chunk("PLTE", [1, 2, 3]) : [], .. baseline[33..]];
        if (valid) Assert.Equal(bytes, Save(bytes, new())); // No pixel decode is claimed.
        else Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => PngMetadataDocument.Open(new MemoryStream(bytes))).Code);
    }
    [Fact]
    public void MalformedDeflateMutationSmokeRemainsBounded()
    {
        byte[] compressed = PngFixture.Compress(Encoding.UTF8.GetBytes(new string('x', 1000)));
        var random = new Random(13013);
        for (int i = 0; i < 256; i++)
        {
            byte[] mutation = compressed.ToArray(); mutation[random.Next(mutation.Length)] ^= (byte)(1 << random.Next(8));
            try { Assert.True(BoundedZlib.Expand(mutation, 4096, default).Length <= 4096); }
            catch (MetadataException error) { Assert.Contains(error.Code, new[] { MetadataErrorCode.InvalidPng, MetadataErrorCode.LimitExceeded }); }
        }
    }
    [Fact]
    public void MissingExifPropertyRemovalPreservesUnsafeUnknownSourceExactly()
    {
        byte[] original = PngFixture.Build([.. PngFixture.Chunk("eXIf", JpegFixture.OrientationTiff), .. PngFixture.Chunk("vpAG", [1])]);
        var missing = new ExifEdit(); missing.Remove(new(ExifDirectoryKind.Ifd0, 0x013B)); Assert.Equal(original, ExifSave(original, missing));
    }
    [Fact]
    public void InvalidCompressionMethodAndNearMatchKeywordAreNotReinterpreted()
    {
        byte[] invalid = PngFixture.Build(PngFixture.Xmp(Packet(), true, method: 1));
        using var doc = PngMetadataDocument.Open(new MemoryStream(invalid)); using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => doc.SaveXmp(output, Set("x"))).Code); Assert.Empty(output.ToArray());
        byte[] other = PngFixture.Chunk("iTXt", [.. "XML:com.adobe.xmpA\0"u8, 0, 0, 0, 0, .. Packet("other")]);
        byte[] changed = Save(PngFixture.Build(other), Set("real"));
        using var read = PngMetadataDocument.Open(new MemoryStream(changed)); Assert.Equal("real", read.ReadXmp()!.Properties[Tool].Text);
        Assert.True(changed.AsSpan(33, other.Length).SequenceEqual(other));
        Assert.Empty(BoundedZlib.Expand(PngFixture.Compress([]), 1, default));
        Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => BoundedZlib.Expand([0x78, 0x9c, 7, 0, 0, 0, 0, 1], 100, default)).Code);
    }
    [Fact]
    public void ManyEmptyFixedBlocksAndConcatenatedStreamsHaveExactTermination()
    {
        // Independently authored RFC1951 bits: fixed block + seven-bit EOB, no literals.
        const int blocks = 10000; byte[] body = new byte[(blocks * 10 + 7) / 8]; int at = 0;
        for (int block = 0; block < blocks; block++)
        {
            int header = block == blocks - 1 ? 3 : 2;
            for (int bit = 0; bit < 3; bit++, at++) if ((header & (1 << bit)) != 0) body[at / 8] |= (byte)(1 << (at % 8));
            at += 7;
        }
        byte[] compressed = [0x78, 0x9c, .. body, 0, 0, 0, 1]; Assert.Empty(BoundedZlib.Expand(compressed, 1, default));
        Assert.Equal(MetadataErrorCode.InvalidPng, Assert.Throws<MetadataException>(() => BoundedZlib.Expand([.. compressed, .. compressed], 1, default)).Code);
    }
    private sealed class AsyncOutput : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Sync write used.");
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Sync write used.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); byte[] bytes = buffer.ToArray(); base.Write(bytes, 0, bytes.Length); return ValueTask.CompletedTask; }
    }
}
