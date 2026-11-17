using System.Security.Cryptography;
using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;

namespace MetadataSharp.Harness.Tests.Reference;

// Generic extent arithmetic tests; no image format fixture is asserted (formerly tests/reference/test_bmff_extents.py).
public sealed class BmffExtentsTests
{
    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static List<object?> Ranges(params object?[][] triplets) => triplets.Select(triplet => (object?)triplet.ToList()).ToList();

    [Fact]
    public void SplitRangesOrderAndBaseOffsets()
    {
        var result = BmffExtents.InspectExtents("abcdefghij"u8.ToArray(), Ranges([2L, 1L, 3L], [0L, 7L, 2L]));
        var extents = result.List("extents").Cast<PyDict>().ToList();
        Assert.Equal([3L, 7L], extents.Select(fact => fact.Long("start")));
        Assert.Equal([Sha("def"u8.ToArray()), Sha("hi"u8.ToArray())], extents.Select(fact => fact.Str("sha256")));
        Assert.Empty(result.List("overlaps"));
    }

    [Fact]
    public void OverlapDuplicateAndEmptyRangesAreNotHidden()
    {
        var result = BmffExtents.InspectExtents("abcdef"u8.ToArray(), Ranges([0L, 1L, 3L], [0L, 3L, 2L], [0L, 1L, 3L], [6L, 0L, 0L]));
        TestSupport.AssertJson("[[0, 1], [0, 2], [1, 2]]", result.List("overlaps"));
        Assert.Equal(Sha([]), ((PyDict)result.List("extents")[3]!).Str("sha256"));
    }

    public static TheoryData<int> InvalidRangeCases => new() { 0, 1, 2, 3, 4, 5, 6, 7 };

    private static readonly object?[][] Invalid =
    [
        [-1L, 0L, 0L], [0L, 0L, -1L], [0L, 9L, 0L], [0L, 5L, 4L], [true, 0L, 1L], [0L, 1.0, 1L], [0L, 1L], [System.Numerics.BigInteger.Pow(2, 64), 0L, 1L],
    ];

    [Theory]
    [MemberData(nameof(InvalidRangeCases))]
    public void InvalidValuesFail(int index) =>
        Assert.Throws<ExtentError>(() => BmffExtents.InspectExtents("12345678"u8.ToArray(), Ranges(Invalid[index])));

    [Fact]
    public void LimitsFail()
    {
        long budget = BmffExtents.MaxTotalExtentBytes;
        BmffExtents.MaxTotalExtentBytes = 2;
        try
        {
            Assert.Throws<ExtentError>(() => BmffExtents.InspectExtents("abc"u8.ToArray(), Ranges([0L, 0L, 2L], [0L, 2L, 1L])));
        }
        finally
        {
            BmffExtents.MaxTotalExtentBytes = budget;
        }
        Assert.Throws<ExtentError>(() => BmffExtents.InspectExtents("a"u8.ToArray(), Enumerable.Repeat<object?>(new List<object?> { 0L, 0L, 0L }, BmffExtents.MaxExtents + 1).ToList()));
        Assert.Throws<ExtentError>(() => BmffExtents.InspectExtents("a"u8.ToArray(), Enumerable.Repeat<object?>(new List<object?> { 0L, 0L, 1L }, 100).ToList()));
        Assert.Throws<ExtentError>(() => BmffExtents.InspectExtents(new byte[BmffExtents.MaxInputBytes + 1], []));
    }
}

// Toy structural byte tests, not valid image fixtures or codec tests (formerly tests/reference/test_bmff_structure.py).
public sealed class BmffStructureTests
{
    private static byte[] Box(string kind, byte[]? payload = null)
    {
        payload ??= [];
        byte[] size = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(size, (uint)(8 + payload.Length));
        return [.. size, .. System.Text.Encoding.ASCII.GetBytes(kind), .. payload];
    }

    private static byte[] BigEndian8(long value)
    {
        byte[] bytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        return bytes;
    }

    [Fact]
    public void FlatRangesAndFtypBrands()
    {
        // Fabricated fourcc values are only structural test data.
        byte[] data = [.. Box("ftyp", [.. "abcd"u8, 0, 0, 0, 2, .. "efghijkl"u8]), .. Box("free", "X"u8.ToArray())];
        var boxes = BmffStructure.Boxes(data);
        Assert.Equal(2, boxes.Count);
        var (one, two) = (boxes[0], boxes[1]);
        Assert.Equal((0L, 24L, 8L), (one.Start, one.Size, one.HeaderSize));
        Assert.Equal((24L, 33L), (two.Start, two.End));
        var ftyp = BmffStructure.Ftyp(data, one);
        Assert.Equal("abcd"u8.ToArray(), (byte[])ftyp["majorBrand"]!);
        Assert.Equal(2L, ftyp.Long("minorVersion"));
        Assert.Equal(["efgh"u8.ToArray(), "ijkl"u8.ToArray()], ftyp.List("compatibleBrands").Cast<byte[]>());
        Assert.Equal([two], BmffStructure.Boxes(data, 24, data.Length));
        Assert.Empty(BmffStructure.Boxes(data, data.Length, data.Length));
    }

    [Fact]
    public void ExtendedZeroAndUuidHeaders()
    {
        byte[] ext = [0, 0, 0, 1, .. "wide"u8, .. BigEndian8(19), .. "abc"u8];
        byte[] uuid = [0, 0, 0, 26, .. "uuid"u8, .. Enumerable.Range(0, 16).Select(i => (byte)i), .. "XY"u8];
        byte[] final = [0, 0, 0, 0, .. "last"u8, .. "z"u8];
        byte[] all = [.. ext, .. uuid, .. final];
        var parsed = BmffStructure.Boxes(all);
        Assert.Equal([16L, 24L, 8L], parsed.Select(box => box.HeaderSize));
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(), parsed[1].UserType);
        Assert.Equal(9, parsed[2].Size);
        Assert.Equal(all.Length, parsed[^1].End);
    }

    public static TheoryData<int> MalformedCases => new() { 0, 1, 2, 3, 4, 5, 6 };

    private static readonly byte[][] Malformed =
    [
        new byte[7], [0, 0, 0, 7, .. "free"u8], [0, 0, 0, 9, .. "free"u8], [0, 0, 0, 1, .. "wide"u8], [0, 0, 0, 1, .. "wide"u8, .. BigEndian8(8)],
        [0, 0, 0, 8, .. "uuid"u8], [0, 0, 0, 0x18, .. "uuid"u8, .. new byte[15]],
    ];

    [Theory]
    [MemberData(nameof(MalformedCases))]
    public void MalformedFailsClosed(int index) => Assert.Throws<BoxError>(() => BmffStructure.Boxes(Malformed[index]));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    [InlineData(0, 10)]
    public void BoundsFailClosed(long start, long end) => Assert.Throws<BoxError>(() => BmffStructure.Boxes("123"u8.ToArray(), start, end));

    [Fact]
    public void LimitsFailClosed()
    {
        Assert.Throws<BoxError>(() => BmffStructure.Boxes(new byte[BmffStructure.MaxInputBytes + 1]));
        Assert.Throws<BoxError>(() => BmffStructure.Boxes(Enumerable.Repeat(Box("free"), BmffStructure.MaxBoxes + 1).SelectMany(box => box).ToArray()));
    }

    [Fact]
    public void FtypRejectsWrongKindAndBadPayload()
    {
        byte[] data = [.. Box("free"), .. Box("ftyp", "bad"u8.ToArray()), .. Box("ftyp", "12345678x"u8.ToArray())];
        foreach (var item in BmffStructure.Boxes(data)) Assert.Throws<BoxError>(() => BmffStructure.Ftyp(data, item));
        Assert.Throws<BoxError>(() => BmffStructure.Ftyp(data, new Box("ftyp"u8.ToArray(), 0, data.Length + 1, 8)));
    }

    [Fact]
    public void JxlSignatureDoesNotClaimFormatValidity()
    {
        Assert.Equal("container-signature", BmffStructure.JxlSignature([0, 0, 0, 0x0C, .. "JXL "u8, 0x0D, 0x0A, 0x87, 0x0A]));
        Assert.Equal("codestream-signature", BmffStructure.JxlSignature([0xFF, 0x0A]));
        foreach (byte[] value in new byte[][] { [], [0xFF], [0, 0, 0, 0x0C, .. "JXL "u8], "no format"u8.ToArray() })
            Assert.Equal("unknown", BmffStructure.JxlSignature(value));
    }
}
