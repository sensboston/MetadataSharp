using System.Text;
using MetadataSharp.Harness.Python;
using O = MetadataSharp.Harness.Reference.JpegOwnedFixtures;

namespace MetadataSharp.Harness.Reference;

// Owned synthetic census TIFF fixtures (B4 observation batch), formerly tools/reference/tiff_owned_fixtures.py.
//
// Our own deterministic writer, built on the shared authored TIFF primitives of JpegOwnedFixtures (BuildTiff /
// Entry); not derived from Exiv2, libtiff, camera samples, or any imported corpus. Fixtures are classic
// little-endian II/42 TIFFs:
// - tiff-base.tif      strips, bare IFD0 (recognition + from-nothing insert base)
// - tiff-tiles.tif     16x16 uncompressed tiles (tile-path recognition)
// - tiff-comments.tif  closest plausible comment carrier: XPComment tag 0x9C9D (UTF-16LE), which the pinned CLI
//                      does not surface - observed and recorded, never asserted
// - tiff-exif.tif      IFD0 Orientation + Artist
// - tiff-exif-dup.tif  IFD0 Artist=Alice + nested EXIF SubIFD (0x8769) Artist=Bob
// - tiff-exif-thumb-jpeg.tif / tiff-exif-thumb-raw.tif
//                      IFD0->IFD1 chain: JPEG-compressed and uncompressed-strip embedded thumbnails (shared
//                      authored thumbnail payloads)
// - tiff-iccp.tif      tag 0x8773 with a minimal synthetic RGB profile
// - tiff-iccp-dup.tif  same tag in IFD0 and SubIFD (byte-identical)
// - tiff-iptc.tif      tag 0x83BB with raw IIM/8BIM (bim_block 0x0404) bytes
// - tiff-iptc-dup.tif  same tag in IFD0 and SubIFD (byte-identical)
// - tiff-xmp.tif       tag 0x02BC with the owned XMP packet
// - tiff-xmp-dup.tif   tag 0x02BC in IFD0 (A) and SubIFD (B), different creators
// - tiff-xmp-nonutf8.tif tag 0x02BC with a non-UTF-8 payload
// Full BigTIFF, strip/tile external-to-file offsets, MM (big-endian) byte order and multi-page chains beyond IFD1
// are out of scope for this batch (recorded in README/limits). Pinned by tests/fixtures/tiff/manifest.json; CI
// regenerates and compares SHA-256.
internal static class TiffOwnedFixtures
{
    public static readonly string Manifest = Repo.Path("tests/fixtures/tiff/manifest.json");
    public static readonly string Provenance = Repo.Path("tests/fixtures/tiff/provenance.json");

    public static readonly byte[] Pixel = [0x0A, 0x14, 0x1E];                                     // owned single pixel, RGB
    public static readonly byte[] TilePixel = Enumerable.Range(0, 256).SelectMany(_ => Pixel).ToArray(); // 16x16 RGB tile = 768 bytes

    private static List<TiffEntry> StripEntries(long pixOff, long pixLen) =>
    [
        O.Entry(256, O.TShort, [1]),        // ImageWidth
        O.Entry(257, O.TShort, [1]),        // ImageLength
        O.Entry(258, O.TShort, [8, 8, 8]),  // BitsPerSample
        O.Entry(259, O.TShort, [1]),        // Compression (none)
        O.Entry(262, O.TShort, [2]),        // PhotometricInterpretation (RGB)
        O.Entry(274, O.TShort, [1]),        // Orientation
        new(273, O.TLong, 1, PyStruct.Pack("<I", pixOff)),   // StripOffsets
        O.Entry(277, O.TShort, [3]),        // SamplesPerPixel
        O.Entry(278, O.TShort, [1]),        // RowsPerStrip
        new(279, O.TLong, 1, PyStruct.Pack("<I", pixLen)),   // StripByteCounts
        O.Entry(284, O.TShort, [1]),        // PlanarConfiguration
    ];

    private static List<TiffEntry> TileEntries(long pixOff, long pixLen) =>
    [
        O.Entry(256, O.TShort, [16]),
        O.Entry(257, O.TShort, [16]),
        O.Entry(258, O.TShort, [8, 8, 8]),
        O.Entry(259, O.TShort, [1]),
        O.Entry(262, O.TShort, [2]),
        O.Entry(274, O.TShort, [1]),
        O.Entry(277, O.TShort, [3]),
        O.Entry(322, O.TShort, [16]),       // TileWidth
        O.Entry(323, O.TShort, [16]),       // TileLength
        new(324, O.TLong, 1, PyStruct.Pack("<I", pixOff)),   // TileOffsets
        new(325, O.TLong, 1, PyStruct.Pack("<I", pixLen)),   // TileByteCounts
        O.Entry(284, O.TShort, [1]),
    ];

    private static Func<long, long, List<List<TiffEntry>>> StripIfds(params (int Tag, int Type, byte[] Values)[] extraIfd0) => (pixOff, pixLen) =>
    {
        var ifd0 = StripEntries(pixOff, pixLen);
        ifd0.AddRange(extraIfd0.Select(extra => O.BytesEntry(extra.Tag, extra.Type, extra.Values)));
        return [ifd0];
    };

    public static byte[] BaseTiff(params (int Tag, int Type, byte[] Values)[] extraIfd0)
    {
        var make = StripIfds(extraIfd0);
        byte[] phase1 = O.BuildTiff(make(0, Pixel.Length));
        int pixOff = phase1.Length;
        byte[] body = O.BuildTiff(make(pixOff, Pixel.Length));
        if (body.Length != pixOff) throw new PyException("AssertionError", "");
        return [.. body, .. Pixel];
    }

    public static byte[] TilesTiff()
    {
        List<List<TiffEntry>> Make(long off, long ln) => [TileEntries(off, ln)];
        byte[] phase1 = O.BuildTiff(Make(0, TilePixel.Length));
        int off = phase1.Length;
        byte[] body = O.BuildTiff(Make(off, TilePixel.Length));
        if (body.Length != off) throw new PyException("AssertionError", "");
        return [.. body, .. TilePixel];
    }

    public static readonly byte[] Comment = [.. Encoding.Unicode.GetBytes("MetadataSharp B4 comment \u00e9 \u2713"), 0, 0];

    // XPComment (0x9C9C per the Microsoft tag table), UTF-16LE, NUL-terminated.
    public static byte[] CommentsTiff() => BaseTiff((0x9C9C, O.TByte, Comment));

    public static byte[] ExifTiff() => BaseTiff((315, O.TAscii, "Alice\0"u8.ToArray()));

    // same IFD0 carrying Artist twice (invalid-but-authored repeated carrier; entries keep authored order -
    // BuildTiff's sort is stable)
    public static byte[] ExifDupTiff() => BaseTiff((315, O.TAscii, "Alice\0"u8.ToArray()), (315, O.TAscii, "Bob\0"u8.ToArray()));

    public static byte[] ExifThumbTiff(bool compressed)
    {
        byte[] blob = compressed ? O.ThumbJpegBytes() : Enumerable.Range(0, 192).Select(i => (byte)(i % 256)).ToArray();
        List<List<TiffEntry>> Make(long pixOff, long blobOff)
        {
            var ifd0 = StripEntries(pixOff, Pixel.Length);
            List<TiffEntry> ifd1 = compressed
                ? [O.Entry(259, O.TShort, [6]), O.Entry(262, O.TShort, [2]), new(513, O.TLong, 1, PyStruct.Pack("<I", blobOff)), O.Entry(514, O.TLong, [blob.Length])]
                : [O.Entry(256, O.TShort, [8]), O.Entry(257, O.TShort, [8]), O.Entry(259, O.TShort, [1]), O.Entry(262, O.TShort, [2]),
                   new(273, O.TLong, 1, PyStruct.Pack("<I", blobOff)), O.Entry(277, O.TShort, [3]), O.Entry(278, O.TShort, [8]), O.Entry(279, O.TLong, [blob.Length])];
            return [ifd0, ifd1];
        }
        byte[] phase1 = O.BuildTiff(Make(0, 0), chain: true);
        int pixOff = phase1.Length;
        int blobOff = pixOff + Pixel.Length;
        byte[] body = O.BuildTiff(Make(pixOff, blobOff), chain: true);
        if (body.Length != pixOff) throw new PyException("AssertionError", "");
        return [.. body, .. Pixel, .. blob];
    }

    public static readonly byte[] IimPayload = [.. O.IptcAlpha, .. O.IptcBeta]; // raw IIM datasets (TIFF tag 0x83BB convention)
    public static readonly byte[] XmpPktA = O.XmpPacket();
    public static readonly byte[] XmpPktB = O.Replace(XmpPktA, "MetadataSharp</xmp:CreatorTool>"u8, "MetadataSharpB</xmp:CreatorTool>"u8);
    public static readonly byte[] XmpPktNonUtf8 = [0x89, 0xFE, 0xFF, .. "RAW not xml"u8];

    public static byte[] IccpTiff() => BaseTiff((0x8773, O.TByte, O.ProfileA));

    public static byte[] IccpDupTiff() => BaseTiff((0x8773, O.TByte, O.ProfileA), (0x8773, O.TByte, O.ProfileA));

    public static byte[] IptcTiff() => BaseTiff((0x83BB, O.TByte, IimPayload));

    public static byte[] IptcDupTiff() => BaseTiff((0x83BB, O.TByte, IimPayload), (0x83BB, O.TByte, IimPayload));

    public static byte[] XmpTiff() => BaseTiff((0x02BC, O.TByte, XmpPktA));

    public static byte[] XmpDupTiff() => BaseTiff((0x02BC, O.TByte, XmpPktA), (0x02BC, O.TByte, XmpPktB));

    public static byte[] XmpNonUtf8Tiff() => BaseTiff((0x02BC, O.TByte, XmpPktNonUtf8));

    private static readonly Lazy<SortedDictionary<string, OwnedFixture>> Catalog = new(BuildCatalog);

    public static SortedDictionary<string, OwnedFixture> Fixtures => Catalog.Value;

    private static SortedDictionary<string, OwnedFixture> BuildCatalog()
    {
        var fixtures = new SortedDictionary<string, OwnedFixture>(StringComparer.Ordinal);
        void Register(string name, byte[] data, string layout, string duplicates, string recipe) => fixtures[name] = new(data, layout, duplicates, recipe);
        Register("tiff-base.tif", BaseTiff(), "container", "none", "tiff-strip-bare-ifd0-v1");
        Register("tiff-tiles.tif", TilesTiff(), "container", "none", "tiff-tile-16x16-v1");
        Register("tiff-comments.tif", CommentsTiff(), "tiff-comments", "none", "tiff-xpcomment-tag-v1");
        Register("tiff-exif.tif", ExifTiff(), "tiff-exif", "none", "tiff-exif-orientation-artist-v1");
        Register("tiff-exif-dup.tif", ExifDupTiff(), "tiff-exif", "first-wins", "tiff-exif-ifd0-artist-twice-alice-bob-v1");
        Register("tiff-exif-thumb-jpeg.tif", ExifThumbTiff(true), "exif-ifd1-jpeg", "none", "tiff-ifd0-ifd1-jpeg-thumbnail-v1");
        Register("tiff-exif-thumb-raw.tif", ExifThumbTiff(false), "exif-ifd1-uncompressed", "none", "tiff-ifd0-ifd1-uncompressed-thumbnail-v1");
        Register("tiff-iccp.tif", IccpTiff(), "tiff-icc", "none", "tiff-iccp-single-v1");
        Register("tiff-iccp-dup.tif", IccpDupTiff(), "tiff-icc", "first-wins", "tiff-iccp-ifd0-tag-twice-byte-identical-v1");
        Register("tiff-iptc.tif", IptcTiff(), "tiff-iptc", "none", "tiff-iptc-raw-iim-v1");
        Register("tiff-iptc-dup.tif", IptcDupTiff(), "tiff-iptc", "first-wins", "tiff-iptc-ifd0-tag-twice-byte-identical-v1");
        Register("tiff-xmp.tif", XmpTiff(), "tiff-xmp", "none", "tiff-xmp-tag-700-standard-v1");
        Register("tiff-xmp-dup.tif", XmpDupTiff(), "tiff-xmp", "first-wins", "tiff-xmp-ifd0-tag-twice-a-b-v1");
        Register("tiff-xmp-nonutf8.tif", XmpNonUtf8Tiff(), "tiff-xmp", "none", "tiff-xmp-non-utf8-payload-v1");
        return fixtures;
    }

    public static byte[] FixtureBytes(string name) => Fixtures[name].Bytes;

    private static PyDict ProvenanceOf(List<object?> fixtures) => PyDict.Of(
        ("encoder", "None (authored byte-level TIFF writer; tools/MetadataSharp.Harness/Reference/TiffOwnedFixtures.cs on the TIFF primitives of JpegOwnedFixtures.cs)"),
        ("recipe", "Deterministic classic II/42 TIFF writer: IFD0 (stdlib; strip or tile "
                   + "image data, uncompressed, owned 0x0A141E pixel values) + authored metadata "
                   + "authored on purpose); thumbnails "
                   + "chain IFD1 with JPEG (shared authored baseline body) or uncompressed strip "
                   + "blobs; IPTC rides raw IIM datasets in tag 0x83BB; "
                   + "XMP rides tag 0x02BC; ICC rides tag 0x8773; comments ride XPComment 0x9C9C "
                   + "(UTF-16LE), an undocumented carrier the pinned CLI does not surface. "
                   + "Pinned by tests/fixtures/tiff/manifest.json; CI regenerates and compares SHA-256."),
        ("inputRights", "MetadataSharp authored synthetic TIFF fixtures, MIT; no imported images, "
                        + "no imported codec code/tests, no upstream fixture corpus."),
        ("fixtures", fixtures));

    public static readonly OwnedFixtureSet Set = new("tiff-owned-fixtures", Manifest, "*.tif", Repo.Path("artifacts/owned-tiff"), () => Fixtures, ProvenanceOf);

    public static int Run(string[] argv) => Set.Run(argv);
}
