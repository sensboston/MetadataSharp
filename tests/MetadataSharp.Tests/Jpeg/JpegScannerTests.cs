using MetadataSharp.Core;
using MetadataSharp.Formats.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Jpeg;

public sealed class JpegScannerTests
{
    [Fact]
    public void RecordsExactSegmentsAndPreservesUnrelatedAppSignatures()
    {
        byte[] prefix = [.. JpegFixture.Segment(0xE0, 1, 2), .. JpegFixture.Segment(0xE1, 88, 77, 80),
            .. JpegFixture.Segment(0xED, 9, 8, 7), .. JpegFixture.Segment(0xE1, JpegFixture.Exif())];
        byte[] data = JpegFixture.Build(prefix);
        JpegScanResult result = JpegScanner.Scan(new MemoryStream(data), new());
        Assert.Equal(data.Length, result.Length);
        Assert.Equal(8L, result.InsertionOffset);
        Assert.NotNull(result.Exif);
        Assert.Equal(JpegFixture.Exif(), data.AsSpan((int)result.Exif.PayloadOffset, result.Exif.PayloadLength).ToArray());
        Assert.Single(result.Segments, s => s.Marker == 0xED);
        Assert.Equal(2, result.Segments.Count(s => s.Marker == 0xE1));
        Assert.Equal(data.Length - 2, result.EoiOffset);
        Assert.True(result.EntropyOffset > result.ScanOffset);
        foreach (JpegSegment segment in result.Segments)
            Assert.Equal(segment.PayloadOffset + segment.PayloadLength, segment.Start + segment.Length);
    }

    [Fact]
    public void DuplicateExifIsAmbiguousButSimilarSignaturesAreOpaque()
    {
        byte[] duplicate = JpegFixture.Build([.. JpegFixture.Segment(0xE1, JpegFixture.Exif()), .. JpegFixture.Segment(0xE1, JpegFixture.Exif())]);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Failure(duplicate).Code);
        Assert.Null(JpegScanner.Scan(new MemoryStream(JpegFixture.Build(JpegFixture.Segment(0xE1, 69, 120, 105, 102, 0))), new()).Exif);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothIoModesHandleShortReadsWithoutClosingSource(bool asynchronous)
    {
        byte[] bytes = JpegFixture.Build(entropy: [1, 0xFF, 0, 2, 0xFF, 0xD0, 3, 0xFF, 0xD1, 4], restart: true);
        using var stream = new ShortReads(bytes, asynchronous);
        JpegScanResult result = asynchronous ? await JpegScanner.ScanAsync(stream, new(), default) : JpegScanner.Scan(stream, new());
        Assert.Equal(bytes.Length - 2, result.EoiOffset);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void FillBytesAndBufferBoundariesDoNotCreateFakeMetadata()
    {
        byte[] entropy = Enumerable.Repeat((byte)1, 4095).Concat(new byte[] { 0xFF, 0, 1, 0xFF, 0xFF, 0xD0, 1 }).ToArray();
        byte[] source = JpegFixture.Build(prefix: [0xFF, .. JpegFixture.Segment(0xE1, 1)], entropy: entropy, restart: true);
        Assert.Null(JpegScanner.Scan(new MemoryStream(source), new MetadataLimits { CopyBufferBytes = 4096 }).Exif);
    }

    [Theory]
    [InlineData("early-eoi", MetadataErrorCode.InvalidJpeg)]
    [InlineData("progressive", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("precision", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("dimension", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("sampling", MetadataErrorCode.InvalidJpeg)]
    [InlineData("selector", MetadataErrorCode.InvalidJpeg)]
    [InlineData("spectral", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("restart", MetadataErrorCode.InvalidJpeg)]
    [InlineData("restart-order", MetadataErrorCode.InvalidJpeg)]
    [InlineData("post-app", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("second-scan", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("stuff-fill", MetadataErrorCode.InvalidJpeg)]
    [InlineData("empty", MetadataErrorCode.InvalidJpeg)]
    [InlineData("trailing", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("short-length", MetadataErrorCode.InvalidJpeg)]
    public void RejectsUnsupportedAndMalformedStructures(string mutation, MetadataErrorCode expected)
    {
        byte[] bytes = mutation switch
        {
            "early-eoi" => [0xFF, 0xD8, 0xFF, 0xD9],
            "progressive" => JpegFixture.Build(frameMarker: 0xC2),
            "precision" => JpegFixture.Build(frame: [12, 0, 8, 0, 8, 1, 1, 0x11, 0]),
            "dimension" => JpegFixture.Build(frame: [8, 0, 0, 0, 8, 1, 1, 0x11, 0]),
            "sampling" => JpegFixture.Build(frame: [8, 0, 8, 0, 8, 1, 1, 0, 0]),
            "selector" => JpegFixture.Build(scan: [1, 1, 0x10, 0, 63, 0]),
            "spectral" => JpegFixture.Build(scan: [1, 1, 0, 1, 63, 0]),
            "restart" => JpegFixture.Build(entropy: [1, 0xFF, 0xD0]),
            "restart-order" => JpegFixture.Build(entropy: [1, 0xFF, 0xD1], restart: true),
            "post-app" => JpegFixture.Build(entropy: [1, .. JpegFixture.Segment(0xE1, JpegFixture.Exif())]),
            "second-scan" => JpegFixture.Build(entropy: [1, .. JpegFixture.Segment(0xDA, 1, 1, 0, 0, 63, 0)]),
            "stuff-fill" => JpegFixture.Build(entropy: [1, 0xFF, 0xFF, 0]),
            "empty" => JpegFixture.Build(entropy: []),
            "trailing" => [.. JpegFixture.Build(), 0],
            _ => JpegFixture.Build(prefix: [0xFF, 0xE1, 0, 1])
        };
        Assert.Equal(expected, Failure(bytes).Code);
    }

    [Fact]
    public void EveryTruncatedPrefixAndBoundedMutationIsStructured()
    {
        byte[] source = JpegFixture.Build();
        for (int n = 0; n < source.Length; n++) Assert.Throws<MetadataException>(() => JpegScanner.Scan(new MemoryStream(source[..n]), new()));
        var random = new Random(707);
        for (int n = 0; n < 256; n++)
        {
            byte[] copy = (byte[])source.Clone(); copy[random.Next(copy.Length)] = (byte)random.Next(256);
            try { JpegScanner.Scan(new MemoryStream(copy), new()); } catch (MetadataException) { }
        }
    }

    [Fact]
    public async Task BudgetsCancellationAndSourcePreconditionsAreEnforced()
    {
        byte[] bytes = JpegFixture.Build();
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => JpegScanner.Scan(new MemoryStream(bytes), new() { MaxInputBytes = 1 })).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => JpegScanner.Scan(new MemoryStream(bytes), new() { MaxSegments = 1 })).Code);
        using var positioned = new MemoryStream(bytes); positioned.Position = 1;
        Assert.Throws<ArgumentException>(() => JpegScanner.Scan(positioned, new()));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => JpegScanner.ScanAsync(new MemoryStream(bytes), new(), cts.Token));
    }

    private static MetadataException Failure(byte[] data) => Assert.Throws<MetadataException>(() => JpegScanner.Scan(new MemoryStream(data), new()));

    internal sealed class ShortReads(byte[] data, bool asynchronous) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (asynchronous) throw new InvalidOperationException("Async code used synchronous I/O.");
            return base.Read(buffer, offset, Math.Min(count, 3));
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!asynchronous) throw new InvalidOperationException("Sync code used async I/O.");
            cancellationToken.ThrowIfCancellationRequested();
            byte[] temporary = new byte[Math.Min(buffer.Length, 3)];
            int read = base.Read(temporary, 0, temporary.Length);
            temporary.AsMemory(0, read).CopyTo(buffer);
            return ValueTask.FromResult(read);
        }
    }
}
