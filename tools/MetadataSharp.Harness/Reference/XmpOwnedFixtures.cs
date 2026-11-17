using System.Text;
using System.Xml;
using MetadataSharp.Harness.Python;
using O = MetadataSharp.Harness.Reference.JpegOwnedFixtures;

namespace MetadataSharp.Harness.Reference;

// Owned synthetic XMP packet fixtures (B5 observation batch), formerly tools/reference/xmp_owned_fixtures.py.
//
// Our own deterministic writer; not derived from Exiv2, Adobe's SDK or any imported corpus. A `.xmp` file *is* an
// XMP packet document (the sidecar artifact), so these fixtures are complete packets, Byte-for-byte the same shape
// the in-image XMP carriers hold:
// - xmp-base.xmp              canonical packet: xpacket begin/end PIs, <x:xmpmeta> wrapper,
//                             CreatorTool=MetadataSharp (attribute/element order fixed; the layout the
//                             JPEG/PNG/WebP/TIFF in-image fixtures carry)
// - xmp-no-props.xmp          same wrapper, empty rdf:Description (from-nothing insert base)
// - xmp-unwrapped.xmp         optional wrapper omitted per XMP Part 1 SS7.3-SS7.5 (edition pinned in
//                             XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md): xpacket PIs around a bare rdf:RDF;
//                             CreatorTool=MetadataSharp
// - xmp-unwrapped-no-props.xmp  unwrapped, empty rdf:Description (from-nothing insert base for the unwrapped layout)
// - xmp-nonutf8.xmp           wrapped, invalid UTF-8 bytes in CreatorTool (record-only; never asserted)
// - xmp-unwrapped-nonutf8.xmp unwrapped analog of xmp-nonutf8.xmp
// The sidecar presence/applied distinction is exercised by the B5 observation itself: image fixtures from the
// already-pinned JPEG/PNG/WebP/TIFF sets are copied with an adjacent SIDECARPROBE .xmp file and the pinned CLI's
// behavior on the *image* is recorded (presence is in scope; xmp-applied writes stay E15 per the approved
// denominators). No sidecar fixture is authored for that purpose. Encoding is UTF-8 for every packet (optional
// xpacket-carried BOM char, per Part 1); a leading raw UTF-8 BOM before `<?xpacket` is a deliberate non-goal of
// this batch (recorded in README/limits, not silently dropped). Pinned by tests/fixtures/xmp/manifest.json; CI
// regenerates and compares SHA-256.
internal static class XmpOwnedFixtures
{
    public static readonly string Manifest = Repo.Path("tests/fixtures/xmp/manifest.json");
    public static readonly string Provenance = Repo.Path("tests/fixtures/xmp/provenance.json");

    public static readonly byte[] XpOpen = O.Latin1("<?xpacket begin=\"\u00ef\u00bb\u00bf\" id=\"W5M0MpCehiHzreSzkc9d\"?>");
    public static readonly byte[] XpClose = "<?xpacket end=\"w\"?>"u8.ToArray();
    public static readonly byte[] XmetaOpen = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"MetadataSharp\">"u8.ToArray();
    public static readonly byte[] XmetaClose = "</x:xmpmeta>"u8.ToArray();
    public static readonly byte[] RdfOpen = "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"u8.ToArray();
    public static readonly byte[] RdfClose = "</rdf:RDF>"u8.ToArray();
    public static readonly byte[] DescOpen = "<rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">"u8.ToArray();
    public static readonly byte[] DescClose = "</rdf:Description>"u8.ToArray();
    public static readonly byte[] Creator = "<xmp:CreatorTool>MetadataSharp</xmp:CreatorTool>"u8.ToArray();
    public static readonly byte[] NonUtf8 = [0x89, 0xFE, 0xFF, .. "RAW not xml"u8];

    private static byte[] Wrapped(byte[] body) => [.. XpOpen, .. XmetaOpen, .. RdfOpen, .. body, .. RdfClose, .. XmetaClose, .. XpClose];

    private static byte[] Unwrapped(byte[] body) => [.. XpOpen, .. RdfOpen, .. body, .. RdfClose, .. XpClose];

    private static readonly byte[] EmptyDescription = "<rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"/>"u8.ToArray();

    public static byte[] PacketWrapped() => Wrapped([.. DescOpen, .. Creator, .. DescClose]);

    public static byte[] PacketWrappedEmpty() => Wrapped(EmptyDescription);

    public static byte[] PacketUnwrapped() => Unwrapped([.. DescOpen, .. Creator, .. DescClose]);

    public static byte[] PacketUnwrappedEmpty() => Unwrapped(EmptyDescription);

    public static byte[] PacketWrappedNonUtf8() => Wrapped([.. DescOpen, .. "<xmp:CreatorTool>"u8, .. NonUtf8, .. "</xmp:CreatorTool>"u8, .. DescClose]);

    public static byte[] PacketUnwrappedNonUtf8() => Unwrapped([.. DescOpen, .. "<xmp:CreatorTool>"u8, .. NonUtf8, .. "</xmp:CreatorTool>"u8, .. DescClose]);

    /// <summary>
    /// Structural requirements of every authored/written packet of this batch. Returns null when `data` is an
    /// authored-profile packet, else a short reason. Deliberately narrow: DOCTYPE rejected outright (never produced
    /// by our fixtures, never emitted by the pinned CLI's XMP writer), XML must be well-formed and root must be the
    /// xpacket-carried xmpmeta or a bare rdf:RDF. (The Python helper had no caller either; the parse-failure text
    /// is System.Xml's, not expat's.)
    /// </summary>
    public static string? StrictParseXmp(byte[] data)
    {
        if (data.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return "leading raw BOM outside the xpacket PI (non-goal of this batch)";
        var head = data.AsSpan(0, Math.Min(512, data.Length));
        if (head.IndexOf("<!DOCTYPE"u8) >= 0 || head.IndexOf("<!ENTITY"u8) >= 0) return "DOCTYPE/ENTITY not permitted in authored packets";
        if (!PyText.IsUtf8(data)) return "not utf-8";
        XmlElement root;
        try
        {
            var document = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(new MemoryStream(data), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
            root = document.DocumentElement!;
        }
        catch (XmlException error)
        {
            return $"XML parse failed: {error.Message}";
        }
        if (root.LocalName is not ("xmpmeta" or "RDF"))
            return $"unexpected root {Py.Repr(root.NamespaceURI.Length > 0 ? $"{{{root.NamespaceURI}}}{root.LocalName}" : root.LocalName)}";
        return null;
    }

    private static readonly Lazy<SortedDictionary<string, OwnedFixture>> Catalog = new(BuildCatalog);

    public static SortedDictionary<string, OwnedFixture> Fixtures => Catalog.Value;

    private static SortedDictionary<string, OwnedFixture> BuildCatalog()
    {
        var fixtures = new SortedDictionary<string, OwnedFixture>(StringComparer.Ordinal);
        void Register(string name, byte[] data, string layout, string duplicates, string recipe) => fixtures[name] = new(data, layout, duplicates, recipe);
        Register("xmp-base.xmp", PacketWrapped(), "xmp-packet", "none", "xmp-sidecar-standard-v1");
        Register("xmp-no-props.xmp", PacketWrappedEmpty(), "xmp-packet", "none", "xmp-sidecar-standard-empty-desc-v1");
        Register("xmp-unwrapped.xmp", PacketUnwrapped(), "xmp-packet-unwrapped", "none", "xmp-sidecar-unwrapped-v1");
        Register("xmp-unwrapped-no-props.xmp", PacketUnwrappedEmpty(), "xmp-packet-unwrapped", "none", "xmp-sidecar-unwrapped-empty-desc-v1");
        Register("xmp-nonutf8.xmp", PacketWrappedNonUtf8(), "xmp-packet", "none", "xmp-sidecar-standard-nonutf8-v1");
        Register("xmp-unwrapped-nonutf8.xmp", PacketUnwrappedNonUtf8(), "xmp-packet-unwrapped", "none", "xmp-sidecar-unwrapped-nonutf8-v1");
        return fixtures;
    }

    public static byte[] FixtureBytes(string name) => Fixtures[name].Bytes;

    private static PyDict ProvenanceOf(List<object?> fixtures) => PyDict.Of(
        ("encoder", "None (authored byte-level XMP packet writer; tools/MetadataSharp.Harness/Reference/XmpOwnedFixtures.cs)"),
        ("recipe", "Deterministic authored XMP packets: xpacket begin/end PIs (the BOM char rides "
                   + "the begin PI per XMP Part 1); the wrapped layout carries x:xmpmeta (the shape the "
                   + "in-image XMP fixtures of the JPEG/PNG/WebP/TIFF batches already hold); the unwrapped "
                   + "layout omits the optional wrapper per Part 1 SS7.3-SS7.5 (editions/blobs pinned in "
                   + "docs/archive/design/XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md). CreatorTool is the "
                   + "single simple property; non-UTF-8 variants swap the CreatorTool value for raw "
                   + "invalid-UTF-8 bytes (record-only; never asserted). Standalone .xmp files ARE XMP "
                   + "packet documents (the sidecar artifact); no image bytes are present. Pinned by "
                   + "tests/fixtures/xmp/manifest.json; CI regenerates and compares SHA-256."),
        ("inputRights", "MetadataSharp authored synthetic XMP fixtures, MIT; no imported packets, "
                        + "no Adobe SDK material, no upstream fixture corpus."),
        ("fixtures", fixtures));

    public static readonly OwnedFixtureSet Set = new("xmp-owned-fixtures", Manifest, "*.xmp", Repo.Path("artifacts/owned-xmp"), () => Fixtures, ProvenanceOf);

    public static int Run(string[] argv) => Set.Run(argv);
}
