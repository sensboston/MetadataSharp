using MetadataSharp.Harness.Python;
using O = MetadataSharp.Harness.Reference.JpegOwnedFixtures;

namespace MetadataSharp.Harness.Reference;

// Owned synthetic census WebP fixtures (B3 observation batch), formerly tools/reference/webp_owned_fixtures.py.
//
// Our own deterministic writer; not derived from Exiv2, libwebp, camera samples, or any imported corpus. WebP is a
// RIFF container: 'RIFF' + uint32le size + 'WEBP' + chunks (fourcc, uint32le size, payload, zero pad to even). The
// fixtures carry a hand-authored fake VP8 lossy keyframe header as the coded image data (the pinned CLI does not
// decode VP8 for metadata operations; the VP8 payload is preserved byte-for-byte across metadata rewrites, which is
// the B3 analog of the byte-preserved coded-scan invariant). EXIF chunk carries a classic II TIFF payload (same
// authored TIFF core as the JPEG/PNG fixtures; no Exif\0\0 signature prefix - the CLI accepts either); ICCP
// carries a minimal synthetic RGB profile; XMP rides the raw 'XMP ' chunk; the IPTC/comments fixtures carry the
// closest plausible carriers ('IPTC' chunk with IIM/8BIM payload, 'COM ' chunk with bytes) which the pinned CLI
// does NOT support - recorded, not asserted. Deterministic; CI regenerates and compares SHA-256 against
// tests/fixtures/webp/census-manifest.json.
internal static class WebpOwnedFixtures
{
    public static readonly string Manifest = Repo.Path("tests/fixtures/webp/census-manifest.json");
    public static readonly string Provenance = Repo.Path("tests/fixtures/webp/census-provenance.json");

    // Hand-authored fake VP8 lossy keyframe header (10 bytes) + filler. NOT a real decodable VP8 frame; metadata
    // operations never decode it, and the pinned CLI rewrites it byte-for-byte. The PROVENANCE/README record this
    // honestly.
    public static readonly byte[] Vp8Payload = [0x10, 0x00, 0x00, 0x9D, 0x01, 0x2A, 0x08, 0x00, 0x08, 0x00, .. new byte[8]];

    public static byte[] Chunk(byte[] cid, byte[] payload)
    {
        if (cid.Length != 4) throw new PyException("AssertionError", "");
        byte[] pad = payload.Length % 2 != 0 ? [0] : [];
        return [.. cid, .. PyStruct.Pack("<I", payload.Length), .. payload, .. pad];
    }

    public static byte[] Riff(IEnumerable<byte[]> chunks)
    {
        byte[] body = [.. "WEBP"u8, .. chunks.SelectMany(chunk => chunk)];
        return [.. "RIFF"u8, .. PyStruct.Pack("<I", body.Length), .. body];
    }

    public static byte[] Vp8xChunk(int flags, int width = 8, int height = 8) =>
        Chunk("VP8X"u8.ToArray(), [(byte)flags, 0, 0, 0, .. ThreeLittle(width - 1), .. ThreeLittle(height - 1)]);

    /// <summary>int.to_bytes(3, 'little').</summary>
    private static byte[] ThreeLittle(int value)
    {
        if (value < 0 || value > 0xFFFFFF) throw new PyException("OverflowError", "int too big to convert");
        return [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];
    }

    public static byte[] Vp8Chunk() => Chunk("VP8 "u8.ToArray(), Vp8Payload);

    public static byte[] Base(int flags = 0x00) => Riff([Vp8xChunk(flags), Vp8Chunk()]);

    public static byte[] ExifChunk(byte[] tiff) => Chunk("EXIF"u8.ToArray(), tiff);

    public static byte[] ExifWebp(byte[] tiff, int flags = 0x08) => Riff([Vp8xChunk(flags), ExifChunk(tiff), Vp8Chunk()]);

    public static byte[] ExifDupWebp(byte[] tiffA, byte[] tiffB) => Riff([Vp8xChunk(0x08), ExifChunk(tiffA), ExifChunk(tiffB), Vp8Chunk()]);

    public static byte[] IccpChunk(byte[] profile) => Chunk("ICCP"u8.ToArray(), profile);

    public static byte[] IccpWebp(int flags = 0x02) => Riff([Vp8xChunk(flags), IccpChunk(O.ProfileA), Vp8Chunk()]);

    public static byte[] IccpDupWebp() => Riff([Vp8xChunk(0x02), IccpChunk(O.ProfileA), IccpChunk(O.ProfileA), Vp8Chunk()]);

    /// <summary>Closest plausible IPTC carrier: an 'IPTC' chunk holding IIM/8BIM bytes. The pinned CLI does not
    /// surface it; observed and recorded, never asserted.</summary>
    public static byte[] IptcWebp()
    {
        byte[] iim = O.BimBlock(0x0404, [], [.. O.IptcAlpha, .. O.IptcBeta]);
        return Riff([Vp8xChunk(0x00), Chunk("IPTC"u8.ToArray(), iim), Vp8Chunk()]);
    }

    /// <summary>Closest plausible comment carrier: a classic 'COM ' chunk. The pinned CLI does not surface it;
    /// observed and recorded, never asserted.</summary>
    public static byte[] CommentsWebp() => Riff([Vp8xChunk(0x00), Chunk("COM "u8.ToArray(), "MetadataSharp B3 comment"u8.ToArray()), Vp8Chunk()]);

    public static byte[] XmpChunk(byte[] packet) => Chunk("XMP "u8.ToArray(), packet);

    public static byte[] XmpWebp(byte[] packet, int flags = 0x04) => Riff([Vp8xChunk(flags), XmpChunk(packet), Vp8Chunk()]);

    public static byte[] XmpDupWebp(byte[] a, byte[] b) => Riff([Vp8xChunk(0x04), XmpChunk(a), XmpChunk(b), Vp8Chunk()]);

    public static readonly byte[] RawStrip = Enumerable.Range(0, 192).Select(i => (byte)(i % 256)).ToArray();
    public static readonly byte[] XmpPktA = O.XmpPacket();
    public static readonly byte[] XmpPktB = O.Replace(XmpPktA, "MetadataSharp</xmp:CreatorTool>"u8, "MetadataSharpB</xmp:CreatorTool>"u8);
    public static readonly byte[] XmpPktA2 = O.Replace(XmpPktA, "MetadataSharp</xmp:CreatorTool>"u8, "MetadataSharpA</xmp:CreatorTool>"u8);
    public static readonly byte[] XmpPktNonUtf8 = [0x89, 0xFE, 0xFF, .. "RAW not xml"u8];

    private static readonly Lazy<SortedDictionary<string, OwnedFixture>> Catalog = new(BuildCatalog);

    public static SortedDictionary<string, OwnedFixture> Fixtures => Catalog.Value;

    private static SortedDictionary<string, OwnedFixture> BuildCatalog()
    {
        var fixtures = new SortedDictionary<string, OwnedFixture>(StringComparer.Ordinal);
        void Register(string name, byte[] data, string layout, string duplicates, string recipe) => fixtures[name] = new(data, layout, duplicates, recipe);
        Register("webp-base.webp", Base(0x00), "container", "none", "webp-vp8x-base-v1");
        Register("webp-comments.webp", CommentsWebp(), "webp-comments", "none", "webp-com-chunk-carrier-v1");
        Register("webp-exif.webp", ExifWebp(O.ExifIfd0Tiff()), "webp-exif", "none", "webp-exif-orientation-artist-v1");
        byte[] tiffA = O.ExifIfd0Tiff();
        byte[] tiffB = O.Replace(O.ExifIfd0Tiff(), "Alice"u8, "Bob  "u8);
        Register("webp-exif-dup.webp", ExifDupWebp(tiffA, tiffB), "webp-exif", "multiple-independent", "webp-two-exif-chunks-distinct-artists-v1");
        Register("webp-exif-thumb-jpeg.webp", ExifWebp(O.ThumbTiff(O.ThumbJpegBytes(), true)), "exif-ifd1-jpeg", "none", "webp-exif-ifd1-jpeg-thumbnail-v1");
        Register("webp-exif-thumb-raw.webp", ExifWebp(O.ThumbTiff(RawStrip, false)), "exif-ifd1-uncompressed", "none", "webp-exif-ifd1-uncompressed-thumbnail-v1");
        Register("webp-iccp.webp", IccpWebp(), "webp-iccp", "none", "webp-iccp-single-v1");
        Register("webp-iccp-dup.webp", IccpDupWebp(), "webp-iccp", "first-wins", "webp-iccp-two-byte-identical-v1");
        Register("webp-iptc.webp", IptcWebp(), "webp-iptc", "none", "webp-iptc-chunk-carrier-v1");
        Register("webp-xmp.webp", XmpWebp(XmpPktA), "webp-xmp", "none", "webp-xmp-chunk-standard-v1");
        Register("webp-xmp-dup.webp", XmpDupWebp(XmpPktA2, XmpPktB), "webp-xmp", "last-wins", "webp-xmp-two-chunks-different-creators-v1");
        Register("webp-xmp-nonutf8.webp", XmpWebp(XmpPktNonUtf8), "webp-xmp", "none", "webp-xmp-non-utf8-payload-v1");
        return fixtures;
    }

    public static byte[] FixtureBytes(string name) => Fixtures[name].Bytes;

    private static PyDict ProvenanceOf(List<object?> fixtures) => PyDict.Of(
        ("encoder", "None (authored byte-level writer; tools/MetadataSharp.Harness/Reference/WebpOwnedFixtures.cs)"),
        ("recipe", "Deterministic WebP (RIFF) writer: RIFF header + VP8X (flags/8x8 canvas) + "
                   + "authored metadata chunks + VP8 fake keyframe header (18 bytes). The VP8 "
                   + "payload is not a decodable frame: metadata operations never decode it and "
                   + "the pinned CLI rewrites it byte-for-byte (the B3 analog of the JPEG "
                   + "coded-scan invariant, verified at observation time). EXIF chunks carry "
                   + "classic II TIFF payloads (no Exif\\0\\0 prefix); ICCP carries a minimal "
                   + "synthetic RGB profile (never decodable real-world data); XMP rides the raw "
                   + "XMP chunk; the IPTC and COM chunks are undocumented carriers the pinned CLI "
                   + "does not surface (observed, never asserted). Pinned by "
                   + "tests/fixtures/webp/census-manifest.json; CI regenerates and compares SHA-256."),
        ("inputRights", "MetadataSharp authored synthetic WebP fixtures, MIT; no imported images, "
                        + "no imported codec code/tests, no upstream fixture corpus."),
        ("fixtures", fixtures));

    public static readonly OwnedFixtureSet Set = new("webp-owned-fixtures", Manifest, "*.webp", Repo.Path("artifacts/owned-webp"), () => Fixtures, ProvenanceOf);

    public static int Run(string[] argv) => Set.Run(argv);
}
