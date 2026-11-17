using System.Text;
using MetadataSharp.Harness.Python;
using O = MetadataSharp.Harness.Reference.JpegOwnedFixtures;

namespace MetadataSharp.Harness.Reference;

// Owned synthetic census PNG fixtures (B2 observation batch), formerly tools/reference/png_owned_fixtures.py.
//
// Our own deterministic writer; not derived from Exiv2, libpng, camera samples, or any imported corpus. PNG
// chunks are framed exactly (length, type, data, CRC-32) with an 8x8 8-bit truecolor baseline body. eXIf chunks
// carry classic II TIFF payloads (same authored TIFF core as the JPEG fixtures, minus the JPEG APP1 signature
// prefix, per the PNG eXIf convention). ICC profiles are zlib-compressed into iCCP exactly as PNG requires
// (minimal synthetic RGB profile, never decodable real-world data). IPTC rides the "Raw profile type ipc" zTXt
// carrier the pinned CLI itself writes (hex-encoded IIM/8BIM payload); XMP rides an uncompressed iTXt with keyword
// "XML:com.adobe.xmp". Deterministic; CI regenerates and compares SHA-256 against tests/fixtures/png/manifest.json.
internal static class PngOwnedFixtures
{
    public static readonly string Manifest = Repo.Path("tests/fixtures/png/manifest.json");
    public static readonly string Provenance = Repo.Path("tests/fixtures/png/provenance.json");

    public static readonly byte[] Sig = [0x89, .. "PNG\r\n"u8, 0x1A, (byte)'\n'];

    public static byte[] Chunk(string ctype, byte[] data) => Chunk(Encoding.ASCII.GetBytes(ctype), data);

    public static byte[] Chunk(byte[] ctype, byte[] data) =>
        [.. PyStruct.Pack(">I", data.Length), .. ctype, .. data, .. PyStruct.Pack(">I", Zlib.Crc32([.. ctype, .. data]) & 0xFFFFFFFF)];

    /// <summary>chunks: fully-framed chunk byte strings (see Chunk()).</summary>
    public static byte[] Png(IEnumerable<byte[]> chunks)
    {
        const int width = 8, height = 8;
        byte[] ihdr = PyStruct.Pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0);
        byte[] row = [0x00, .. Enumerable.Range(0, width).SelectMany(_ => new byte[] { 0x80, 0x80, 0x80 })];
        byte[] raw = Enumerable.Range(0, height).SelectMany(_ => row).ToArray();
        return [.. Sig, .. Chunk("IHDR", ihdr), .. chunks.SelectMany(chunk => chunk), .. Chunk("IDAT", Zlib.Compress(raw)), .. Chunk("IEND", [])];
    }

    public static byte[] TextChunk(string keyword, byte[] text) => Chunk("tEXt", [.. Encoding.Latin1.GetBytes(keyword), 0, .. text]);

    public static byte[] ZtxtChunk(string keyword, byte[] text) => Chunk("zTXt", [.. Encoding.Latin1.GetBytes(keyword), 0, 0, .. Zlib.Compress(text)]);

    public static byte[] Itxt(string keyword, byte[] text) => Chunk("iTXt", [.. Encoding.Latin1.GetBytes(keyword), 0, 0, 0, 0, 0, .. text]);

    public static byte[] IccpChunk(byte[] profile, byte[] name) => Chunk("iCCP", [.. name, 0, 0, .. Zlib.Compress(profile)]);

    private static byte[] IptcRawProfileRaw(IEnumerable<byte[]> payloads)
    {
        byte[] bim = payloads.SelectMany(payload => O.BimBlock(0x0404, [], payload)).ToArray();
        return [.. "\niptc\n     "u8, .. Encoding.ASCII.GetBytes(bim.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)), (byte)'\n',
                .. Encoding.ASCII.GetBytes(Convert.ToHexStringLower(bim)), (byte)'\n'];
    }

    /// <summary>Exiv2's raw-profile carrier: zTXt 'Raw profile type iptc' with a text blob
    /// '\niptc\n     &lt;byte-length&gt;\n&lt;hex of IIM/8BIM blocks&gt;\n'.</summary>
    public static byte[] IptcRawProfileChunk(IEnumerable<byte[]> payloads) =>
        Chunk("zTXt", [.. "Raw profile type iptc\0\0"u8, .. Zlib.Compress(IptcRawProfileRaw(payloads))]);

    /// <summary>The same raw-profile text in an uncompressed iTXt carrier (spec-legal PNG; the pinned CLI rejects it
    /// with 'corrupted image metadata' - recorded).</summary>
    public static byte[] IptcRawProfileItxtChunk(IEnumerable<byte[]> payloads) => Itxt("Raw profile type iptc", IptcRawProfileRaw(payloads));

    public static byte[] XmpItxtChunk(byte[] packet) => Itxt("XML:com.adobe.xmp", packet);

    public static byte[] XmpZtxtChunk(byte[] packet) => Chunk("zTXt", [.. "XML:com.adobe.xmp\0\0"u8, .. Zlib.Compress(packet)]);

    public static byte[] ExifChunk(byte[] tiff) => Chunk("eXIf", tiff);

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
        byte[] alphaBeta = [.. O.IptcAlpha, .. O.IptcBeta];
        Register("png-base.png", Png([]), "container", "none", "png-base-v1");
        Register("png-text.png", Png([TextChunk("Description", "MetadataSharp PNG text"u8.ToArray()), ZtxtChunk("Comment", "MetadataSharp PNG zTXt"u8.ToArray()),
                                      Itxt("Comment", Encoding.UTF8.GetBytes("MetadataSharp iTXt \u00e9 \u2713"))]),
                 "png-text", "none", "png-text-ztxt-itxt-trio-v1");
        Register("png-exif.png", Png([ExifChunk(O.ExifIfd0Tiff())]), "png-exif", "none", "png-exif-orientation-artist-v1");
        Register("png-exif-thumb-jpeg.png", Png([ExifChunk(O.ThumbTiff(O.ThumbJpegBytes(), true))]), "exif-ifd1-jpeg", "none", "png-exif-ifd1-jpeg-thumbnail-v1");
        Register("png-exif-thumb-raw.png", Png([ExifChunk(O.ThumbTiff(RawStrip, false))]), "exif-ifd1-uncompressed", "none", "png-exif-ifd1-uncompressed-thumbnail-v1");
        Register("png-iccp.png", Png([IccpChunk(O.ProfileA, "MetadataSharp"u8.ToArray())]), "iccp", "none", "png-iccp-named-v1");
        Register("png-iccp-dup.png", Png([IccpChunk(O.ProfileA, "MetadataSharp"u8.ToArray()), IccpChunk(O.ProfileA, "MetadataSharp"u8.ToArray())]),
                 "iccp", "first-wins", "png-iccp-two-byte-identical-chunks-v1");
        Register("png-iptc.png", Png([IptcRawProfileChunk([alphaBeta])]), "ztxt-raw-profile-iptc", "none", "png-iptc-ztxt-raw-profile-alpha-beta-v1");
        Register("png-iptc-dup.png", Png([IptcRawProfileChunk([alphaBeta]), IptcRawProfileChunk([alphaBeta])]),
                 "ztxt-raw-profile-iptc", "first-wins", "png-iptc-two-byte-identical-ztxt-v1");
        Register("png-iptc-itxt.png", Png([IptcRawProfileItxtChunk([alphaBeta])]), "itxt-raw-profile-iptc", "none", "png-iptc-itxt-raw-profile-alpha-beta-v1");
        Register("png-iptc-itxt-dup.png", Png([IptcRawProfileItxtChunk([alphaBeta]), IptcRawProfileItxtChunk([alphaBeta])]),
                 "itxt-raw-profile-iptc", "first-wins", "png-iptc-two-byte-identical-itxt-v1");
        Register("png-xmp.png", Png([XmpItxtChunk(XmpPktA)]), "itxt-xmp", "none", "png-xmp-itxt-standard-v1");
        Register("png-xmp-dup.png", Png([XmpItxtChunk(XmpPktA2), XmpItxtChunk(XmpPktB)]), "itxt-xmp", "first-wins", "png-xmp-two-itxt-different-creators-v1");
        Register("png-xmp-nonutf8.png", Png([XmpItxtChunk(XmpPktNonUtf8)]), "itxt-xmp", "none", "png-xmp-itxt-non-utf8-payload-v1");
        Register("png-xmp-ztxt.png", Png([XmpZtxtChunk(XmpPktA)]), "ztxt-xmp", "none", "png-xmp-ztxt-standard-v1");
        Register("png-xmp-ztxt-dup.png", Png([XmpZtxtChunk(XmpPktA2), XmpZtxtChunk(XmpPktB)]), "ztxt-xmp", "first-wins", "png-xmp-two-ztxt-different-creators-v1");
        Register("png-xmp-ztxt-nonutf8.png", Png([XmpZtxtChunk(XmpPktNonUtf8)]), "ztxt-xmp", "none", "png-xmp-ztxt-non-utf8-payload-v1");
        return fixtures;
    }

    public static byte[] FixtureBytes(string name) => Fixtures[name].Bytes;

    private static PyDict ProvenanceOf(List<object?> fixtures) => PyDict.Of(
        ("encoder", "None (authored byte-level writer; tools/MetadataSharp.Harness/Reference/PngOwnedFixtures.cs)"),
        ("recipe", "Deterministic PNG writer: signature + IHDR (8x8 8-bit truecolor RGB) + "
                   + "owned chunks + IDAT (zlib of filter-0 scanlines) + IEND; every chunk carries "
                   + "a correct CRC-32. eXIf chunks carry classic II TIFF payloads without the "
                   + "JPEG APP1 prefix (PNG eXIf convention); iCCP carries a minimal synthetic RGB "
                   + "profile, zlib-compressed (never decodable real-world data); IPTC rides the "
                   + "zTXt \"Raw profile type iptc\" carrier the pinned CLI itself writes "
                   + "(hex-encoded IIM/8BIM payload in the raw-profile text format); XMP rides an "
                   + "uncompressed iTXt with keyword \"XML:com.adobe.xmp\". Pinned by "
                   + "tests/fixtures/png/manifest.json; CI regenerates and compares SHA-256."),
        ("inputRights", "MetadataSharp authored synthetic PNG fixtures, MIT; no imported images, "
                        + "no imported codec code/tests, no upstream fixture corpus."),
        ("fixtures", fixtures));

    public static readonly OwnedFixtureSet Set = new("png-owned-fixtures", Manifest, "*.png", Repo.Path("artifacts/owned-png"), () => Fixtures, ProvenanceOf);

    public static int Run(string[] argv) => Set.Run(argv);
}
