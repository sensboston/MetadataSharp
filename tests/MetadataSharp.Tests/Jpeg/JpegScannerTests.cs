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
        Assert.Equal((1, 1), (result.ScanCount, result.FrameCount));
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
    [InlineData((byte)0xC0)] [InlineData((byte)0xC1)] [InlineData((byte)0xC2)] [InlineData((byte)0xC5)]
    [InlineData((byte)0xC6)] [InlineData((byte)0xC9)] [InlineData((byte)0xCA)] [InlineData((byte)0xCD)] [InlineData((byte)0xCE)]
    public void AcceptsEveryLossyFrameProcess(byte frameMarker)
    {
        byte[] data = JpegFixture.Build(frameMarker: frameMarker);
        JpegScanResult result = JpegScanner.Scan(new MemoryStream(data), new());
        Assert.Equal((1, 1), (result.ScanCount, result.FrameCount));
        Assert.Equal(new byte[] { 0xDB, 0xC4, frameMarker, 0xDA }, result.Segments.Select(s => s.Marker).ToArray());
    }

    [Theory]
    [InlineData((byte)0xC3)] [InlineData((byte)0xC7)] [InlineData((byte)0xCB)] [InlineData((byte)0xCF)]
    public void AcceptsLosslessFramesAndTheirPredictorScan(byte frameMarker)
    {
        byte[] data = JpegFixture.Build(frameMarker: frameMarker, scan: [1, 1, 0, 1, 0, 0]);
        JpegScanResult result = JpegScanner.Scan(new MemoryStream(data), new());
        Assert.Equal((1, 1), (result.ScanCount, result.FrameCount));
        Assert.DoesNotContain(result.Segments, s => s.Marker == 0xDC);
    }

    [Fact]
    public void AcceptsPrecisionAndTableSelectorsBeyondBaseline()
    {
        Assert.Equal(4, JpegScanner.Scan(new MemoryStream(JpegFixture.Build(frame: [12, 0, 8, 0, 8, 1, 1, 0x11, 0])), new()).Segments.Count);
        byte[] dht = new byte[36]; dht[0] = 0x01; dht[1] = 1; dht[18] = 0x11; dht[19] = 1;
        byte[] selectors = JpegFixture.Build(prefix: JpegFixture.Segment(0xC4, dht), scan: [1, 1, 0x11, 0, 63, 0]);
        // DC1/AC1 are named by the scan and defined by the leading DHT, so the file is structurally sound.
        Assert.Equal(5, JpegScanner.Scan(new MemoryStream(selectors), new()).Segments.Count);
    }

    [Fact]
    public void ProgressiveMultiScanKeepsEveryScanAndTheRedefinedTableBetweenThem()
    {
        byte[] data = JpegFixture.Progressive();
        JpegScanResult result = JpegScanner.Scan(new MemoryStream(data), new());
        Assert.Equal(3, result.ScanCount);
        Assert.Equal(1, result.FrameCount);
        Assert.Equal(new int[] { 0xDB, 0xC4, 0xC2, 0xDA, 0xC4, 0xDA, 0xDA }, result.Segments.Select(s => (int)s.Marker).ToArray());
        Assert.Equal(data.Length - 2, result.EoiOffset);
        Assert.True(result.EntropyOffset > result.ScanOffset);
        // Entropy-coded bytes are never recorded as segments, so the first scan starts inside the
        // range the later SOS and DHT segments sit in.
        Assert.True(result.EntropyOffset < result.Segments[4].Start);
    }

    [Fact]
    public async Task MultiScanAndArithmeticFramesReadTheSameInBothIoModes()
    {
        foreach (byte[] data in new[] { JpegFixture.Progressive(), JpegFixture.Arithmetic(), JpegFixture.Build() })
        {
            JpegScanResult sync = JpegScanner.Scan(new MemoryStream(data), new());
            JpegScanResult asyncResult = await JpegScanner.ScanAsync(new MemoryStream(data), new(), default);
            Assert.Equal(sync.Length, asyncResult.Length);
            Assert.Equal(sync.ScanCount, asyncResult.ScanCount);
            Assert.Equal(sync.FrameCount, asyncResult.FrameCount);
            Assert.Equal(sync.EoiOffset, asyncResult.EoiOffset);
            Assert.Equal(sync.InsertionOffset, asyncResult.InsertionOffset);
            Assert.Equal(sync.Segments.Select(s => (s.Marker, s.Start, s.Length)).ToArray(),
                asyncResult.Segments.Select(s => (s.Marker, s.Start, s.Length)).ToArray());
        }
        Assert.Equal(1, JpegScanner.Scan(new MemoryStream(JpegFixture.Arithmetic()), new()).Segments.Count(s => s.Marker == 0xCC));
    }

    [Fact]
    public void ArithmeticFrameNeedsNoHuffmanTableButStillAQuantizer()
    {
        byte[] data = JpegFixture.Arithmetic();
        JpegScanResult result = JpegScanner.Scan(new MemoryStream(data), new());
        Assert.Equal(1, result.ScanCount);
        Assert.DoesNotContain(result.Segments, s => s.Marker == 0xC4);
        byte[] withoutQuant = [0xFF, 0xD8, .. JpegFixture.Segment(0xCC, 0x10, 0x21, 0),
            .. JpegFixture.Segment(0xC9, 8, 0, 8, 0, 8, 1, 1, 0x11, 0), .. JpegFixture.Segment(0xDA, 1, 1, 0, 0, 63, 0),
            0x3F, 0xFF, 0xD9];
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Failure(withoutQuant).Code);
    }

    [Fact]
    public void MultipleScansAndPostScanAppSegmentsAreAccepted()
    {
        byte[] second = JpegFixture.Build(entropy: [1, .. JpegFixture.Segment(0xDA, 1, 1, 0, 0, 63, 0), 2]);
        JpegScanResult rescan = JpegScanner.Scan(new MemoryStream(second), new());
        Assert.Equal(2, rescan.ScanCount);
        Assert.Equal(2, rescan.Segments.Count(s => s.Marker == 0xDA));
        byte[] tables = JpegFixture.Build(entropy: [1, .. JpegFixture.Segment(0xC4, JpegFixture.HuffmanTable),
            .. JpegFixture.Segment(0xFE, 104, 105)]);
        Assert.Equal(new int[] { 0xDB, 0xC4, 0xC0, 0xDA, 0xC4, 0xFE },
            JpegScanner.Scan(new MemoryStream(tables), new()).Segments.Select(s => (int)s.Marker).ToArray());
        byte[] post = JpegFixture.Build(entropy: [1, .. JpegFixture.Segment(0xE1, JpegFixture.Exif())]);
        JpegScanResult afterEntropy = JpegScanner.Scan(new MemoryStream(post), new());
        Assert.NotNull(afterEntropy.Exif);
        // The APP1 sits between the scan data and EOI: its 36 bytes plus the two EOI bytes.
        Assert.Equal(post.Length - 38, afterEntropy.Exif!.Start);
        Assert.Equal(2L, afterEntropy.InsertionOffset); // no leading APP0, so a writer still inserts after SOI
        Assert.Equal(1, afterEntropy.ScanCount);
    }

    [Fact]
    public void ZeroHeightFrameIsAcceptedOnlyOnceDNLHasArrived()
    {
        byte[] withDnl = JpegFixture.Build(frame: [8, 0, 0, 0, 8, 1, 1, 0x11, 0], entropy: [0x3F, .. JpegFixture.Segment(0xDC, 0, 9)]);
        JpegScanResult result = JpegScanner.Scan(new MemoryStream(withDnl), new());
        Assert.Equal(5, result.Segments.Count);
        Assert.Equal(1, result.Segments.Count(s => s.Marker == 0xDC));
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Failure(JpegFixture.Build(frame: [8, 0, 0, 0, 8, 1, 1, 0x11, 0])).Code);
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Failure(JpegFixture.Build(prefix: JpegFixture.Segment(0xDC, 0, 9))).Code);
        Assert.Equal(MetadataErrorCode.InvalidJpeg, Failure(JpegFixture.Build(frame: [8, 0, 0, 0, 8, 1, 1, 0x11, 0],
            entropy: [0x3F, .. JpegFixture.Segment(0xDC, 0, 0)])).Code);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothIoModesWalkEveryScanOfAProgressiveImage(bool asynchronous)
    {
        byte[] bytes = JpegFixture.Progressive();
        using var stream = new ShortReads(bytes, asynchronous);
        JpegScanResult result = asynchronous ? await JpegScanner.ScanAsync(stream, new(), default) : JpegScanner.Scan(stream, new());
        Assert.Equal(3, result.ScanCount);
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
    [InlineData("zero-height", MetadataErrorCode.InvalidJpeg)]
    [InlineData("sampling", MetadataErrorCode.InvalidJpeg)]
    [InlineData("selector", MetadataErrorCode.InvalidJpeg)]
    [InlineData("restart", MetadataErrorCode.InvalidJpeg)]
    [InlineData("restart-order", MetadataErrorCode.InvalidJpeg)]
    [InlineData("stuff-fill", MetadataErrorCode.InvalidJpeg)]
    [InlineData("empty", MetadataErrorCode.InvalidJpeg)]
    [InlineData("trailing", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("short-length", MetadataErrorCode.InvalidJpeg)]
    [InlineData("reserved-sof", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("extension-marker", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("lossless-predictor", MetadataErrorCode.InvalidJpeg)]
    [InlineData("dnl-before-frame", MetadataErrorCode.InvalidJpeg)]
    [InlineData("dac-length", MetadataErrorCode.InvalidJpeg)]
    [InlineData("dac-selector", MetadataErrorCode.InvalidJpeg)]
    [InlineData("interleaved-scan", MetadataErrorCode.InvalidJpeg)]
    [InlineData("scan-before-frame", MetadataErrorCode.InvalidJpeg)]
    [InlineData("bytes-between-segments", MetadataErrorCode.InvalidJpeg)]
    public void RejectsUnsupportedAndMalformedStructures(string mutation, MetadataErrorCode expected)
    {
        byte[] bytes = mutation switch
        {
            "early-eoi" => [0xFF, 0xD8, 0xFF, 0xD9],
            "zero-height" => JpegFixture.Build(frame: [8, 0, 0, 0, 8, 1, 1, 0x11, 0]),
            "sampling" => JpegFixture.Build(frame: [8, 0, 8, 0, 8, 1, 1, 0, 0]),
            "selector" => JpegFixture.Build(scan: [1, 1, 0x10, 0, 63, 0]),
            "restart" => JpegFixture.Build(entropy: [1, 0xFF, 0xD0]),
            "restart-order" => JpegFixture.Build(entropy: [1, 0xFF, 0xD1], restart: true),
            "stuff-fill" => JpegFixture.Build(entropy: [1, 0xFF, 0xFF, 0]),
            "empty" => JpegFixture.Build(entropy: []),
            "trailing" => [.. JpegFixture.Build(), 0],
            "reserved-sof" => JpegFixture.Build(frameMarker: 0xC8),
            "extension-marker" => JpegFixture.Build(prefix: [0xFF, 0xF0, 0, 2]),
            "lossless-predictor" => JpegFixture.Build(frameMarker: 0xC3),
            "dnl-before-frame" => JpegFixture.Build(prefix: JpegFixture.Segment(0xDC, 0, 9)),
            "dac-length" => JpegFixture.Build(prefix: JpegFixture.Segment(0xCC, 0x10, 0x21)),
            "dac-selector" => JpegFixture.Build(prefix: JpegFixture.Segment(0xCC, 0x51, 0x21, 0)),
            "interleaved-scan" => JpegFixture.Build(frame: [8, 0, 8, 0, 8, 3, 1, 0x44, 0, 2, 0x44, 0, 3, 0x44, 0],
                scan: [3, 1, 0, 2, 0, 3, 0, 0, 63, 0]),
            "scan-before-frame" => [0xFF, 0xD8, .. JpegFixture.Segment(0xDA, 1, 1, 0, 0, 63, 0), 0x3F, 0xFF, 0xD9],
            "bytes-between-segments" => JpegFixture.Build(prefix: [.. JpegFixture.Segment(0xFE, 104, 105), 0x99]),
            _ => JpegFixture.Build(prefix: [0xFF, 0xE1, 0, 1])
        };
        Assert.Equal(expected, Failure(bytes).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryTruncatedPrefixAndBoundedMutationIsStructured(int shape)
    {
        byte[] source = shape switch { 0 => JpegFixture.Build(), 1 => JpegFixture.Progressive(), _ => JpegFixture.Arithmetic() };
        for (int n = 0; n < source.Length; n++) Assert.Throws<MetadataException>(() => JpegScanner.Scan(new MemoryStream(source[..n]), new()));
        var random = new Random(707 + shape);
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
        // A progressive image spends its budget on the same segment arithmetic, mid-file.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() =>
            JpegScanner.Scan(new MemoryStream(JpegFixture.Progressive()), new() { MaxSegments = 6 })).Code);
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
