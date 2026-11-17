using System.Buffers.Binary;
using System.Security.Cryptography;
using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;

namespace MetadataSharp.Harness.Tests.Reference;

// Offline checks for the B6 raw-byte oracle; no raw rendering is implied (formerly tests/reference/test_dng_raw_oracle.py).
public sealed class DngRawOracleTests
{
    private static ushort U16(byte[] data, long at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)at)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan((int)at));

    [Theory]
    [InlineData("II", "raw")]
    [InlineData("II", "thumb")]
    [InlineData("MM", "raw")]
    [InlineData("MM", "thumb")]
    public void OwnedRawAndSubIfdBothByteOrders(string order, string shape)
    {
        var (data, info) = DngFixtures.Build(order, shape);
        byte[] expected = data[(int)info.Long("raw_strip")..(int)(info.Long("raw_strip") + info.Long("raw_strip_length"))];
        var strips = DngRawOracle.RawStrips(data);
        Assert.Single(strips);
        Assert.Equal(expected, strips[0]);
        TestSupport.AssertJsonEqual(new List<object?> { PyDict.Of(("length", 16L), ("sha256", Convert.ToHexStringLower(SHA256.HashData(expected)))) },
                                    DngRawOracle.RawStripFingerprints(data));
    }

    [Fact]
    public void RawMutationIsDetectedThumbnailMutationIsNotRaw()
    {
        var (data, info) = DngFixtures.Build("II", "thumb");
        string original = TestSupport.Json(DngRawOracle.RawStripFingerprints(data));
        byte[] altered = [.. data];
        altered[info.Long("thumb_strip")] ^= 1;
        Assert.Equal(original, TestSupport.Json(DngRawOracle.RawStripFingerprints(altered)));
        altered[info.Long("raw_strip")] ^= 1;
        Assert.NotEqual(original, TestSupport.Json(DngRawOracle.RawStripFingerprints(altered)));
    }

    [Fact]
    public void MalformedInputsFailClosed()
    {
        var (data, info) = DngFixtures.Build("II", "raw");
        foreach (byte[] bad in new byte[][] { [], [.. "II*\0\b"u8], data[..^1], [.. "MM"u8, .. data[2..]] })
            Assert.Throws<DngOracleError>(() => DngRawOracle.RawStrips(bad));
        byte[] cycle = [.. data];
        long ifd0 = info.Long("ifd0");
        int count = U16(data, ifd0, little: true);
        BinaryPrimitives.WriteUInt32LittleEndian(cycle.AsSpan((int)(ifd0 + 2 + count * 12)), (uint)ifd0);
        Assert.Throws<DngOracleError>(() => DngRawOracle.RawStrips(cycle));
    }

    [Fact]
    public void MissingCfaAndOutOfBoundsStripFailClosed()
    {
        var (data, info) = DngFixtures.Build("MM", "raw");
        long ifd0 = info.Long("ifd0");
        int count = U16(data, ifd0, little: false);
        var entries = Enumerable.Range(0, count).ToDictionary(i => (int)U16(data, ifd0 + 2 + 12 * i, little: false), i => ifd0 + 2 + 12 * i);
        byte[] noCfa = [.. data];
        BinaryPrimitives.WriteUInt16BigEndian(noCfa.AsSpan((int)(entries[262] + 8)), 2);
        Assert.Throws<DngOracleError>(() => DngRawOracle.RawStrips(noCfa));
        byte[] badRange = [.. data];
        BinaryPrimitives.WriteUInt32BigEndian(badRange.AsSpan((int)(entries[273] + 8)), (uint)data.Length);
        Assert.Throws<DngOracleError>(() => DngRawOracle.RawStrips(badRange));
    }
}

// Bounded thumbnail extractor tests (formerly tests/reference/test_dng_thumbnail_oracle.py).
public sealed class DngThumbnailOracleTests
{
    [Theory]
    [InlineData("II", "raw")]
    [InlineData("II", "thumb")]
    [InlineData("MM", "raw")]
    [InlineData("MM", "thumb")]
    public void Ifd1IsSeparateFromPreview(string order, string shape)
    {
        Assert.Null(DngThumbnailOracle.Ifd1Jpeg(DngCensusFixtures.Build(order, shape, "present")));
        byte[] data = DngCensusFixtures.Build(order, shape, "present", ifd1Jpeg: true);
        Assert.Equal(JpegOwnedFixtures.ThumbJpegBytes(), DngThumbnailOracle.Ifd1Jpeg(data));
        Assert.Throws<ThumbnailOracleError>(() => DngThumbnailOracle.Ifd1Jpeg(data[..^1]));
    }

    [Fact]
    public void BadHeader()
    {
        foreach (byte[] data in new byte[][] { [], [.. "II*\0"u8], [.. "XX"u8, .. new byte[20]] })
            Assert.Throws<ThumbnailOracleError>(() => DngThumbnailOracle.Ifd1Jpeg(data));
    }
}

// IFD0 byte extraction and malformed input checks for B6 (formerly tests/reference/test_dng_metadata_oracle.py).
public sealed class DngMetadataOracleTests
{
    private static readonly long[] Tags = [315, 700, 33723, 34675, 40092];

    [Theory]
    [InlineData("II", "raw")]
    [InlineData("II", "thumb")]
    [InlineData("MM", "raw")]
    [InlineData("MM", "thumb")]
    public void ExpectedCarriersBothByteOrdersAndShapes(string order, string shape)
    {
        byte[] empty = DngCensusFixtures.Build(order, shape, "empty");
        byte[] present = DngCensusFixtures.Build(order, shape, "present");
        Assert.Empty(DngMetadataOracle.RootValues(empty, Tags));
        var records = DngMetadataOracle.RootValues(present, Tags);
        Assert.Equal(Tags.Order(), records.Select(record => record.Long("tag")));
        Assert.Equal("B6Alice\0"u8.ToArray(), (byte[])records[0]["bytes"]!);
        Assert.Equal(JpegOwnedFixtures.ProfileA, (byte[])records.First(record => record.Long("tag") == 34675)["bytes"]!);
    }

    [Fact]
    public void InvalidHeaderAndPayloadFailClosed()
    {
        byte[] data = DngCensusFixtures.Build("II", "raw", "present");
        foreach (byte[] value in new byte[][] { [], [.. "II*\0"u8], [.. "MM"u8, .. data[2..]] })
            Assert.Throws<MetadataOracleError>(() => DngMetadataOracle.RootValues(value, [315]));
        byte[] mutable = [.. data];
        int count = BinaryPrimitives.ReadUInt16LittleEndian(mutable.AsSpan(8));
        int artistEntry = Enumerable.Range(0, count).Select(i => 10 + i * 12).First(at => BinaryPrimitives.ReadUInt16LittleEndian(mutable.AsSpan(at)) == 315);
        BinaryPrimitives.WriteUInt32LittleEndian(mutable.AsSpan(artistEntry + 8), (uint)data.Length);
        Assert.Throws<MetadataOracleError>(() => DngMetadataOracle.RootValues(mutable, [315]));
        BinaryPrimitives.WriteUInt16LittleEndian(mutable.AsSpan(artistEntry + 2), 99);
        Assert.Throws<MetadataOracleError>(() => DngMetadataOracle.RootValues(mutable, [315]));
    }

    [Fact]
    public void DuplicateTagIsPreserved()
    {
        // Minimal classic TIFF with two inline BYTE tag 700 records.
        byte[] data = [.. "II*\0"u8, 8, 0, 0, 0, 2, 0, .. PyStruct.Pack("<HHI", 700, 1, 1), .. "A\0\0\0"u8, .. PyStruct.Pack("<HHI", 700, 1, 1), .. "B\0\0\0"u8, 0, 0, 0, 0];
        Assert.Equal(["A"u8.ToArray(), "B"u8.ToArray()], DngMetadataOracle.RootValues(data, [700]).Select(record => (byte[])record["bytes"]!));
    }
}
