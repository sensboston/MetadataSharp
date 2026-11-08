using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class StandaloneTiffTests
{
    private static TiffLayout Read(byte[] bytes, TiffReadOptions? options = null) => TiffLayout.Read(new MemoryStream(bytes), options);
    private static MetadataErrorCode Error(byte[] bytes, TiffReadOptions? options = null) => Assert.Throws<MetadataException>(() => Read(bytes, options)).Code;
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void EndianStripsTilesAndOwnedRanges(bool big, bool tiles)
    {
        var f = StandaloneTiffFixture.Create(big, tiles); var layout = Read(f.Bytes);
        Assert.True(layout.IsSupported); Assert.Equal(big ? Endianness.BigEndian : Endianness.LittleEndian, layout.ByteOrder);
        var image = Assert.Single(layout.Directories).Image!; Assert.Equal(tiles ? TiffStorageKind.Tiles : TiffStorageKind.Strips, image.Storage);
        Assert.Equal(new TiffByteRange(2048, 256), Assert.Single(image.Segments));
        Array.Clear(f.Bytes); Assert.Equal(16u, image.Width); Assert.Equal(8u, layout.FirstIfdOffset);
        Assert.False(layout.Directories is TiffDirectoryInfo[]); Assert.False(layout.Ranges is TiffAccountedRange[]);
    }
    [Fact]
    public void SharedDagNextAndPointerLocationsArePreserved()
    {
        var f = StandaloneTiffFixture.Create();
        f.Directory(8, [.. StandaloneTiffFixture.Fields(), (330, 4, [512, 1024])], 1024);
        f.Directory(512, [(34665, 4, [1024])]); f.Directory(1024, [(305, 2, [0])]);
        var layout = Read(f.Bytes); Assert.Equal(3, layout.Directories.Count);
        Assert.Equal(3, layout.Directories[0].Links.Count); Assert.All(layout.Directories[0].Links, link => Assert.True(link.PointerOffset >= 8));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(f.Bytes, new() { Limits = new() { MaxDirectoryDepth = 2 } }));
        f.Directory(1024, [(305, 2, [0])], 8); Assert.Equal(MetadataErrorCode.InvalidTiff, Error(f.Bytes));
    }
    [Theory]
    [InlineData(34665)] [InlineData(34853)] [InlineData(40965)]
    public void MetadataDirectoryRelationshipsAreExplicit(ushort tag)
    {
        var f = StandaloneTiffFixture.Create(); f.Directory(8, [.. StandaloneTiffFixture.Fields(), (tag, 4, [512])]); f.Directory(512, [(305, 2, [0])]);
        Assert.Equal(tag, Assert.Single(Read(f.Bytes).Directories[0].Links).TagId);
        f.Directory(8, [.. StandaloneTiffFixture.Fields(), (tag, 3, [512])]); Assert.Equal(MetadataErrorCode.InvalidTiff, Error(f.Bytes));
    }
    [Fact]
    public void SeparatePlanesAndMultipleStripsUseArrayOrder()
    {
        var f = StandaloneTiffFixture.Create();
        var fields = StandaloneTiffFixture.Fields().Where(v => v.Tag is not (258 or 262 or 273 or 277 or 278 or 279)).ToArray();
        f.Directory(8, [.. fields, (258, 3, [8, 8, 8]), (262, 3, [2]), (277, 3, [3]), (284, 3, [2]), (278, 4, [16]), (273, 4, [2048, 2304, 2560]), (279, 4, [256, 256, 256])]);
        var image = Assert.Single(Read(f.Bytes).Directories).Image!; Assert.True(image.IsSupported); Assert.Equal(3, image.Segments.Count);
        Assert.Equal(2560, image.Segments[2].Offset);
    }
    [Fact]
    public void PreviewsLimitsAndUnsupportedLayoutsAreNotDecodingClaims()
    {
        var f = StandaloneTiffFixture.Create(); f.Directory(8, [.. StandaloneTiffFixture.Fields(), (513, 4, [3000]), (514, 4, [20])]);
        Assert.Equal(new TiffByteRange(3000, 20), Read(f.Bytes).Directories[0].Image!.JpegPreview);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(f.Bytes, new() { Limits = new() { MaxThumbnailBytes = 10 } }));
        f.Directory(8, [.. StandaloneTiffFixture.Fields().Where(x => x.Tag != 259), (259, 3, [6]), (519, 4, [3000]), (65000, 4, [1])]);
        var layout = Read(f.Bytes); Assert.False(layout.IsSupported); Assert.Contains(layout.Issues, i => i.Code == TiffLayoutIssueCode.UnsupportedCompression);
        Assert.Contains(layout.Issues, i => i.Code == TiffLayoutIssueCode.UnsupportedPointerLayout); Assert.Contains(layout.Issues, i => i.Code == TiffLayoutIssueCode.UnknownFieldSemantics);
    }
    [Theory]
    [InlineData(1)] [InlineData(5)] [InlineData(7)] [InlineData(8)] [InlineData(32946)] [InlineData(32773)]
    public void CompressionAccountingNeverDecodesPixels(uint compression)
    {
        var f = StandaloneTiffFixture.Create(); f.Directory(8, [.. StandaloneTiffFixture.Fields().Where(x => x.Tag != 259), (259, 3, [compression])]);
        Assert.True(Read(f.Bytes).IsSupported); // Synthetic bytes are not compressed; only extents are asserted.
    }
    [Fact]
    public void OverlapsDuplicateTagsAndInvalidReferencesRefuse()
    {
        var f = StandaloneTiffFixture.Create();
        foreach (uint offset in new uint[] { 0, 8, 4090, uint.MaxValue })
        { f.Directory(8, [.. StandaloneTiffFixture.Fields().Where(x => x.Tag != 273), (273, 4, [offset])]); Assert.Equal(MetadataErrorCode.InvalidTiff, Error(f.Bytes)); }
        f.Directory(8, [.. StandaloneTiffFixture.Fields(), (256, 4, [16])]); Assert.Equal(MetadataErrorCode.InvalidTiff, Error(f.Bytes));
        f.Directory(8, [.. StandaloneTiffFixture.Fields(), (513, 4, [2048]), (514, 4, [10])]); Assert.Equal(MetadataErrorCode.InvalidTiff, Error(f.Bytes));
        f.Directory(8, [(330, 4, [512, 512])]); f.Directory(512, [(305, 2, [0])]); Assert.Equal(2, Read(f.Bytes).Directories.Count);
    }
    [Fact]
    public void ExactSharedValuesAreAllowedButPartialAliasesRefuse()
    {
        var f = StandaloneTiffFixture.Create(); f.Directory(8, [(330, 4, [512, 1024])]);
        f.Directory(512, [(65000, 4, [1, 2])]); f.Directory(1024, [(65001, 4, [1, 2])]);
        f.U32(1024 + 10, 768); Assert.Equal(3, Read(f.Bytes).Directories.Count);
        f.U32(1024 + 10, 770); Assert.Equal(MetadataErrorCode.InvalidTiff, Error(f.Bytes));
    }
    [Fact]
    public void LimitsTypesBigTiffDngAndTruncationsAreBounded()
    {
        var f = StandaloneTiffFixture.Create();
        foreach (var limits in new MetadataLimits[] { new() { MaxInputBytes = 100 }, new() { MaxEntriesPerDirectory = 1 }, new() { MaxTotalEntries = 1 }, new() { MaxRetainedMetadataBytes = 100 } })
            Assert.Equal(MetadataErrorCode.LimitExceeded, Error(f.Bytes, new() { Limits = limits }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(f.Bytes, new() { MaxAccountedRanges = 1 }));
        f.U16(2, 43); Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(f.Bytes)); f.U16(2, 42);
        f.Directory(8, [(50706, 1, [1])]); Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(f.Bytes));
        f.Directory(8, [(256, 13, [1])]); Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(f.Bytes));
        f = StandaloneTiffFixture.Create(); for (int n = 0; n < 150; n++) Assert.Throws<MetadataException>(() => Read(f.Bytes[..n]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(f.Bytes, new() { MaxDirectoryLinks = 0 }));
    }
    [Fact]
    public async Task TrueAsyncShortReadsCancellationAndOwnedPathClosure()
    {
        var f = StandaloneTiffFixture.Create(true); using var shortReads = new JpegScannerTests.ShortReads(f.Bytes, true);
        var result = await TiffLayout.ReadAsync(shortReads); Assert.True(result.IsSupported); Assert.True(shortReads.CanRead);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TiffLayout.ReadAsync(new MemoryStream(f.Bytes), cancellationToken: cancellation.Token));
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tif"); File.WriteAllBytes(path, f.Bytes);
        try { Assert.True((await TiffLayout.ReadAsync(path)).IsSupported); File.Delete(path); Assert.Equal(16u, result.Directories[0].Image!.Width); }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(258, 1)] [InlineData(258, 32)] [InlineData(262, 3)]
    [InlineData(284, 3)] [InlineData(339, 2)] [InlineData(339, 3)]
    public void UnsupportedSamplesReturnIssuesRatherThanSuccessfulSupport(ushort tag, uint value)
    {
        var f = StandaloneTiffFixture.Create(); f.Directory(8, [.. StandaloneTiffFixture.Fields().Where(v => v.Tag != tag), (tag, 3, [value])]);
        var layout = Read(f.Bytes); Assert.False(layout.IsSupported); Assert.False(layout.Directories[0].Image!.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == TiffLayoutIssueCode.UnsupportedSampleLayout);
    }
    [Fact]
    public void StandaloneFileCanExceedExifCapWithoutReadingPixels()
    {
        byte[] bytes = new byte[131072]; StandaloneTiffFixture.Create().Bytes.CopyTo(bytes, 0);
        using var source = new HeaderOnlyStream(bytes); var layout = TiffLayout.Read(source);
        Assert.True(layout.IsSupported); Assert.Equal(131072, layout.SourceLength); Assert.True(source.CanRead);
        var fields = (IList<TiffDirectoryInfo>)layout.Directories;
        Assert.Throws<NotSupportedException>(() => fields[0] = fields[0]);
    }
    [Fact]
    public void GraphValueAndSegmentBudgetsAndNullPointersRefuse()
    {
        var f = StandaloneTiffFixture.Create(); f.Directory(8, [.. StandaloneTiffFixture.Fields(), (330, 4, [512, 1024])]);
        f.Directory(512, [(305, 2, [0])]); f.Directory(1024, [(305, 2, [0])]);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(f.Bytes, new() { MaxDirectoryLinks = 1 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(f.Bytes, new() { Limits = new() { MaxDirectories = 1 } }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(f.Bytes, new() { Limits = new() { MaxValueBytes = 4 } }));
        f.Directory(8, []); Assert.Equal(MetadataErrorCode.InvalidTiff, Error(f.Bytes));
        f.Directory(8, [(330, 4, [0])]); Assert.Equal(MetadataErrorCode.InvalidTiff, Error(f.Bytes));
        f.Directory(8, [(330, 4, [513])]); Assert.Equal(MetadataErrorCode.InvalidTiff, Error(f.Bytes));
        f.Directory(8, [(305, 2, [0])]); Assert.Contains(Read(f.Bytes).Issues, i => i.Code == TiffLayoutIssueCode.MissingImageLayout);
    }
    private sealed class HeaderOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(Span<byte> buffer)
        { if (Position >= 2048 || Position + buffer.Length > 2048) throw new InvalidOperationException("Pixel reads are forbidden."); return base.Read(buffer); }
    }
    [Fact]
    public void BoundedMutationSmokeDoesNotLeakArithmeticOrIndexErrors()
    {
        var random = new Random(15015); byte[] original = StandaloneTiffFixture.Create().Bytes;
        for (int n = 0; n < 512; n++)
        {
            byte[] bytes = original.ToArray(); bytes[random.Next(150)] ^= (byte)(1 << random.Next(8));
            try { var result = Read(bytes); Assert.True(result.Ranges.Count <= 16384); }
            catch (MetadataException e) { Assert.Contains(e.Code, new[] { MetadataErrorCode.InvalidTiff, MetadataErrorCode.LimitExceeded, MetadataErrorCode.UnsupportedFeature }); }
        }
    }
}
