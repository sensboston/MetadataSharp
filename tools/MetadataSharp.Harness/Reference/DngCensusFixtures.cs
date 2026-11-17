using MetadataSharp.Harness.Python;
using D1 = MetadataSharp.Harness.Reference.DngFixtures;

namespace MetadataSharp.Harness.Reference;

// Owned D1-derived classic DNG fixtures for the E01 B6 census, formerly tools/reference/dng_census_fixtures.py.
//
// Four raw/preview-root, II/MM shapes each have an empty-descriptive and a metadata-present IFD0 variant, plus a
// metadata-present variant with an EXIF IFD1 JPEG thumbnail. The preview root is distinct from the IFD1 thumbnail.
// No vendor samples are used; no comprehensive DNG conformance is asserted. The CFA pixels and DNG structural tags
// come from our D1 writer; metadata payloads are independently authored B4 fixtures. This is a bounded fixture
// writer, not a general TIFF serializer or DNG validator.
internal static class DngCensusFixtures
{
    public static readonly string Manifest = Repo.Path("tests/fixtures/dng/census-manifest.json");

    private static readonly string Description = """
        Owned D1-derived classic DNG fixtures for the E01 B6 census.

        Four raw/preview-root, II/MM shapes each have an empty-descriptive and a
        metadata-present IFD0 variant, plus a metadata-present variant with an
        EXIF IFD1 JPEG thumbnail. The preview root is distinct from the IFD1 thumbnail.
        No vendor samples are used; no comprehensive DNG conformance is asserted.
        The CFA pixels and DNG structural tags come from our D1 writer; metadata
        payloads are independently authored B4 fixtures. This is a bounded fixture
        writer, not a general TIFF serializer or DNG validator.
        """.ReplaceLineEndings("\n");

    private static void Replace(List<DngEntry> entries, int tag, int type, long count, long[] values)
    {
        foreach (var entry in entries)
        {
            if (entry.Tag != tag) continue;
            entry.Type = type;
            entry.Count = count;
            entry.Values = values;
            return;
        }
        throw new PyException("ValueError", "missing structural tag " + tag.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static List<DngEntry> Metadata()
    {
        byte[] artist = "B6Alice\0"u8.ToArray();
        // XPComment convention: BYTE array of UTF-16LE code units.
        byte[] comment = [.. System.Text.Encoding.Unicode.GetBytes("B6 comment"), 0, 0];
        return
        [
            new(315, D1.TAscii, artist.Length, artist),
            new(700, D1.TByte, TiffOwnedFixtures.XmpPktA.Length, TiffOwnedFixtures.XmpPktA),
            new(33723, D1.TByte, TiffOwnedFixtures.IimPayload.Length, TiffOwnedFixtures.IimPayload),
            new(34675, D1.TByte, JpegOwnedFixtures.ProfileA.Length, JpegOwnedFixtures.ProfileA),
            new(40092, D1.TByte, comment.Length, comment),
        ];
    }

    public static byte[] Build(string order, string shape, string metadata, bool ifd1Jpeg = false)
    {
        if (order is not ("II" or "MM") || shape is not ("raw" or "thumb") || metadata is not ("empty" or "present"))
            throw new PyException("ValueError", "unsupported fixture variant");
        if (ifd1Jpeg && metadata != "present") throw new PyException("ValueError", "IFD1 fixture requires metadata-present variant");
        string fmt = order == "II" ? "<" : ">";
        var raw = D1.RawEntries();
        var root = shape == "raw" ? raw : D1.ThumbnailEntries();
        // Both variants have a known Artist precondition in IFD0; the raw SubIFD's D1 Artist is left untouched on
        // thumbnail-root fixtures.
        root.RemoveAll(entry => entry.Tag == 315);
        if (metadata == "present") root.AddRange(Metadata());
        var (rootItems, end) = D1.PlanIfd(fmt, root, 8);
        bool little = order == "II";
        var blocks = new SortedDictionary<long, byte[]>
        {
            [0] = [.. System.Text.Encoding.ASCII.GetBytes(order), .. PyStruct.Pack(little ? "<H" : ">H", 42), .. PyStruct.Pack(little ? "<I" : ">I", 8)],
        };
        long rawStrip;
        if (shape == "raw")
        {
            rawStrip = end;
            Replace(root, 273, D1.TLong, 1, [rawStrip]);
            (rootItems, _) = D1.PlanIfd(fmt, root, 8);
            blocks[8] = D1.RenderIfd(fmt, rootItems, 8, 0);
        }
        else
        {
            long thumbStrip = end;
            long rawIfd = thumbStrip + D1.ThumbPixelsBytes().Length;
            Replace(root, 273, D1.TLong, 1, [thumbStrip]);
            Replace(root, 330, D1.TLong, 1, [rawIfd]);
            (rootItems, _) = D1.PlanIfd(fmt, root, 8);
            (_, rawStrip) = D1.PlanIfd(fmt, raw, rawIfd);
            Replace(raw, 273, D1.TLong, 1, [rawStrip]);
            var (rawItems, _) = D1.PlanIfd(fmt, raw, rawIfd);
            blocks[8] = D1.RenderIfd(fmt, rootItems, 8, 0);
            blocks[thumbStrip] = D1.ThumbPixelsBytes();
            blocks[rawIfd] = D1.RenderIfd(fmt, rawItems, rawIfd, 0);
        }
        blocks[rawStrip] = D1.RawPixelsBytes(fmt);
        if (ifd1Jpeg)
        {
            // An independently authored JPEG thumbnail in a chained IFD1, distinct from the RGB preview root and CFA
            // SubIFD in the 'thumb' shape.
            long ifd1Offset = rawStrip + D1.RawPixelsBytes(fmt).Length;
            byte[] thumb = JpegOwnedFixtures.ThumbJpegBytes();
            List<DngEntry> entries =
            [
                new(259, D1.TShort, 1, new long[] { 6 }), new(262, D1.TShort, 1, new long[] { 2 }),
                new(513, D1.TLong, 1, new long[] { 0 }), new(514, D1.TLong, 1, new long[] { thumb.Length }),
            ];
            var (_, jpegOffset) = D1.PlanIfd(fmt, entries, ifd1Offset);
            Replace(entries, 513, D1.TLong, 1, [jpegOffset]);
            var (items, _) = D1.PlanIfd(fmt, entries, ifd1Offset);
            blocks[8] = D1.RenderIfd(fmt, rootItems, 8, ifd1Offset);
            blocks[ifd1Offset] = D1.RenderIfd(fmt, items, ifd1Offset, 0);
            blocks[jpegOffset] = thumb;
        }
        var data = new List<byte>();
        foreach (var (offset, chunk) in blocks)
        {
            if (offset < data.Count) throw new PyException("ValueError", "fixture block overlap");
            data.AddRange(new byte[offset - data.Count]);
            data.AddRange(chunk);
        }
        return [.. data];
    }

    public static IEnumerable<(string Name, byte[] Data, PyDict Entry)> Catalog()
    {
        foreach (string order in new[] { "II", "MM" })
        {
            foreach (string shape in new[] { "raw", "thumb" })
            {
                foreach (var (metadata, ifd1) in new[] { ("empty", false), ("present", false), ("present", true) })
                {
                    string suffix = ifd1 ? "-ifd1" : "";
                    string name = $"b6-{shape}-{order.ToLowerInvariant()}-{metadata}{suffix}.dng";
                    byte[] data = Build(order, shape, metadata, ifd1Jpeg: ifd1);
                    yield return (name, data, PyDict.Of(("file", name), ("length", (long)data.Length), ("sha256", JpegOwnedFixtures.Sha256(data)),
                                                        ("byteOrder", order), ("shape", shape), ("metadata", metadata), ("ifd1Jpeg", ifd1),
                                                        ("recipe", "tools/MetadataSharp.Harness/Reference/DngCensusFixtures.cs Build: D1-derived, owned synthetic")));
                }
            }
        }
    }

    public static List<object?> ManifestEntries() => Catalog().Select(item => (object?)item.Entry).ToList();

    public static void Generate(string outdir)
    {
        PyFile.MakeDirs(outdir);
        foreach (var (name, data, _) in Catalog()) PyFile.WriteBytes(PyPath.Join(outdir, name), data);
        PyFile.WriteText(PyPath.Join(outdir, "census-manifest.json"), PyJson.Dumps(ManifestEntries(), indent: 2) + "\n");
    }

    public static void Verify()
    {
        var expected = PyJson.Loads(PyFile.ReadText(Manifest));
        if (!Py.Eq(expected, ManifestEntries())) throw new PyException("ValueError", "DNG census fixture manifest differs from generator");
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("dng-census-fixtures", Description)
            .Add("command", choices: ["generate", "verify", "print-manifest"])
            .Add("outdir", nargs: "?");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        switch (args.Str("command"))
        {
            case "generate":
                if (args.StrOrNull("outdir") is not string outdir) return parser.Fail("generate requires outdir");
                Generate(outdir);
                break;
            case "verify":
                Verify();
                break;
            default:
                Out.Print(PyJson.Dumps(ManifestEntries(), indent: 2));
                break;
        }
        return 0;
    }
}
