using System.Buffers.Binary;
using System.Security.Cryptography;
using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;

namespace MetadataSharp.Harness.Tests.Reference;

// Offline checks for the authored DNG fixture generator (formerly tests/reference/test_dng_fixtures.py). These tests
// do not decode or render DNG; they pin the byte layout the C# tests of the library also assert.
public sealed class DngFixtureTests
{
    // (order, shape) -> (length, sha256, raw strip, thumbnail strip, raw IFD, IFD0).
    private static readonly Dictionary<(string, string), (long Length, string Sha, long RawStrip, long? ThumbStrip, long RawIfd, long Ifd0)> Pinned = new()
    {
        [("II", "raw")] = (598, "474952a49242556b9bec981483df2afc9c06546a0c7705a529f789c9ad52d2a0", 582, null, 8, 8),
        [("II", "thumb")] = (1034, "3208c70940419fbd5655d9ed7643702626f4ada7c22d2dcb8346b738c0f11b96", 1018, 420, 444, 8),
        [("MM", "raw")] = (598, "64dee07029c8ed6fb9fcddac5a578ec79141789e6834fdf22809f8bca08685f8", 582, null, 8, 8),
        [("MM", "thumb")] = (1034, "5d32ea1c44c204fcd3040c1a12204d64dfe84fcb83412a0bd4dcfa9f577be5e9", 1018, 420, 444, 8),
    };

    private static readonly Dictionary<string, string> RawPixelSha256 = new()
    {
        ["II"] = "067436bafb67573443706a095729cfe4b4d0427dd1c25d165777df8ff41780b9",
        ["MM"] = "6472f018a621000b17e1b6c90e5555b2f11ed33f71d113a55af0c1c85e74716c",
    };

    public static TheoryData<string, string> Shapes => new() { { "II", "raw" }, { "II", "thumb" }, { "MM", "raw" }, { "MM", "thumb" } };

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    [Theory]
    [MemberData(nameof(Shapes))]
    public void DeterministicAndByteIdenticalAcrossCalls(string order, string shape)
    {
        var (first, firstInfo) = DngFixtures.Build(order, shape);
        var (second, secondInfo) = DngFixtures.Build(order, shape);
        Assert.Equal(first, second);
        TestSupport.AssertJsonEqual(firstInfo, secondInfo);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void LayoutAndSha256Pinned(string order, string shape)
    {
        var (length, sha, rawStrip, thumbStrip, rawIfd, ifd0) = Pinned[(order, shape)];
        var (data, info) = DngFixtures.Build(order, shape);
        Assert.Equal(length, data.Length);
        Assert.Equal(sha, Sha(data));
        Assert.Equal(ifd0, info.Long("ifd0"));
        Assert.Equal(rawStrip, info.Long("raw_strip"));
        Assert.Equal(thumbStrip, info["thumb_strip"] is null ? null : info.Long("thumb_strip"));
        Assert.Equal(rawIfd, info.Long("raw_ifd"));
        Assert.Equal(16, info.Long("raw_strip_length"));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void StructureInvariants(string order, string shape)
    {
        var (data, _) = DngFixtures.Build(order, shape);
        bool little = order == "II";
        int U16(int at) => little ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));
        long U32(int at) => little ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
        Assert.Equal(little ? "II"u8.ToArray() : "MM"u8.ToArray(), data[..2]);
        Assert.Equal(42, U16(2));
        Assert.Equal(8, U32(4));
        int count = U16(8);
        Assert.True(count >= 15, $"{count} entries");
        var tags = Enumerable.Range(0, count).Select(i => U16(10 + 12 * i)).ToList();
        Assert.Equal(tags.Order().ToList(), tags);
        Assert.Equal(tags.Count, tags.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void RawStripPayloadHash(string order, string shape)
    {
        var (data, info) = DngFixtures.Build(order, shape);
        int start = (int)info.Long("raw_strip");
        Assert.Equal(RawPixelSha256[order], Sha(data[start..(start + (int)info.Long("raw_strip_length"))]));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void DngVersionIs1100OnTheRawLeaf(string order, string shape)
    {
        var (data, info) = DngFixtures.Build(order, shape);
        string fmt = order == "II" ? "<" : ">";
        var fields = new Dictionary<long, (long Type, long Count, int At)>();
        int offset = (int)info.Long("raw_ifd");
        long count = (long)PyStruct.UnpackFrom(fmt + "H", data, offset)[0];
        for (int i = 0; i < count; i++)
        {
            int at = offset + 2 + 12 * i;
            var entry = PyStruct.UnpackFrom(fmt + "HHI", data, at);
            fields[(long)entry[0]] = ((long)entry[1], (long)entry[2], at);
        }
        var (type, n, place) = fields[50706];
        Assert.Equal((1L, 4L), (type, n));
        Assert.Equal(new byte[] { 1, 1, 0, 0 }, data[(place + 8)..(place + 12)]); // inline (count 4, 1 byte each)
    }
}

// Offline checks for the authored EPS and PGF fixture generator (formerly tests/reference/test_eps_pgf_fixtures.py).
// These tests do not decode or render PostScript or PGF; they pin the byte layout the C# tests of the library also
// assert, and they keep the writer and the manifest byte-identical.
public sealed class EpsPgfFixtureTests
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void DeterministicAcrossCalls()
    {
        var first = EpsPgfFixtures.BuildCatalog();
        var second = EpsPgfFixtures.BuildCatalog();
        Assert.Equal(first.Select(item => item.Name), second.Select(item => item.Name));
        foreach (var (one, two) in first.Zip(second)) Assert.Equal(one.Spec.Bytes, two.Spec.Bytes);
    }

    [Fact]
    public void ManifestIsPinnedToTheBytesWrittenToday() =>
        // The fixture bytes and tests/fixtures/b11eps/manifest.json must not drift apart.
        TestSupport.AssertJsonEqual(PyJson.Loads(PyFile.ReadText(EpsPgfFixtures.Manifest)), EpsPgfFixtures.ManifestEntries());

    [Fact]
    public void EveryFileHashesToItsManifestEntry()
    {
        foreach (PyDict entry in EpsPgfFixtures.ManifestEntries().Cast<PyDict>())
        {
            string path = PyPath.Join(PyPath.Parent(EpsPgfFixtures.Manifest), entry.Str("file"));
            Assert.True(File.Exists(path), entry.Str("file"));
            byte[] data = File.ReadAllBytes(path);
            Assert.Equal(entry.Long("length"), data.Length);
            Assert.Equal(entry.Str("sha256"), Convert.ToHexStringLower(SHA256.HashData(data)));
        }
    }

    [Fact]
    public void EpsFilesBeginWithAnAcceptedFirstLine()
    {
        byte[][] accepted = [EpsPgfFixtures.EpsFirst, EpsPgfFixtures.EpsFirstSpace, EpsPgfFixtures.EpsFirst31];
        foreach (var (name, spec) in EpsPgfFixtures.Fixtures.Where(item => item.Name.EndsWith(".eps", StringComparison.Ordinal)))
        {
            byte[] data = spec.Bytes;
            byte[] body = data.AsSpan().StartsWith(EpsPgfFixtures.DosSignature) ? data[EpsPgfFixtures.DosHeaderBytes..] : data;
            if (!accepted.Any(line => body.AsSpan().StartsWith(line))) Assert.Fail(name);
        }
    }

    [Fact]
    public void DosHeaderPlacesBothSectionsInsideTheFile()
    {
        byte[] data = EpsPgfFixtures.FixtureBytes("dos-eps-xmp.eps");
        Assert.Equal(EpsPgfFixtures.DosSignature, data[..EpsPgfFixtures.DosSignature.Length]);
        Assert.Equal(30, data[..EpsPgfFixtures.DosHeaderBytes].Length);
        var fields = PyStruct.UnpackFrom("<IIIIII", data, 4).Select(value => (long)value).ToList();
        var (psOffset, psLength, wmfOffset, wmfLength, tiffOffset, tiffLength) = (fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]);
        Assert.Equal(30, psOffset);
        Assert.Equal(0, wmfOffset + wmfLength);
        Assert.Equal(psOffset + psLength, tiffOffset);
        Assert.Equal(data.Length, tiffOffset + tiffLength);
    }

    [Fact]
    public void PgfHeaderSizeCountsTheStructureAndTheRegion()
    {
        byte[] data = EpsPgfFixtures.FixtureBytes("pgf-png.pgf");
        Assert.Equal("PGF"u8.ToArray(), data[..3]);
        long headerSize = PyStruct.U("<I", data, 4);
        var size = PyStruct.UnpackFrom("<II", data, 8);
        // The region begins after the sixteen-byte structure, which this fixture does not enlarge.
        Assert.Equal(16 + data.Length - 24, headerSize);
        Assert.Equal((320L, 211L), ((long)size[0], (long)size[1]));
        Assert.Equal(PngSignature, data[24..32]);
    }

    [Fact]
    public void IndexedPgfPutsItsColourTableBetweenStructureAndRegion()
    {
        byte[] data = EpsPgfFixtures.FixtureBytes("pgf-colour-table.pgf");
        // The colour mode is the thirteenth byte of the sixteen-byte structure.
        Assert.Equal(2, data[20]);
        long headerSize = PyStruct.U("<I", data, 4);
        Assert.Equal(16 + 768 + data.Length - (24 + 768), headerSize);
        Assert.Equal(PngSignature, data[(24 + 768)..(24 + 768 + 8)]);
    }

    [Fact]
    public void EmptyRegionPgfIsTheHeaderAlone()
    {
        byte[] data = EpsPgfFixtures.FixtureBytes("pgf-empty-region.pgf");
        Assert.Equal(24, data.Length);
        Assert.Equal(16L, PyStruct.U("<I", data, 4));
    }

    [Fact]
    public void XmpVariantsCarryTheAuthoredPacket()
    {
        // The file states the trailer once, after the packet, so the packet it holds is the authored one without the
        // trailer that packet itself ends with.
        byte[] full = EpsPgfFixtures.XmpPacket();
        byte[] packet = full[..^EpsPgfFixtures.PacketTrailer.Length];
        foreach (string name in new[] { "eps-xmp.eps", "eps-xmp-space.eps", "eps-xmp-31.eps", "eps-xmp-crlf.eps", "dos-eps-xmp.eps" })
            Assert.True(EpsPgfFixtures.FixtureBytes(name).AsSpan().IndexOf(packet) >= 0, name);
        Assert.True(EpsPgfFixtures.FixtureBytes("eps-plain.eps").AsSpan().IndexOf(packet) < 0);
    }
}
