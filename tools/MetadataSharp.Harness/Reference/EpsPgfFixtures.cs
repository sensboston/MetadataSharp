using MetadataSharp.Harness.Python;
using O = MetadataSharp.Harness.Reference.JpegOwnedFixtures;

namespace MetadataSharp.Harness.Reference;

// Owned synthetic EPS and PGF fixtures (B11 group: formats with their own containers), formerly
// tools/reference/eps_pgf_fixtures.py.
//
// Our own deterministic writer; not derived from Exiv2 or any imported corpus. Each file is authored byte by byte
// from the published container definitions and the DSC 3.0 convention for XMP in PostScript, so the same bytes can
// be read by both sides of the parity job.
//
// EPS: a plain EPS file is a first line (`%!PS-Adobe-3.0 EPSF-3.0` or one of the two the reference also accepts),
// a comment block, `%%EndComments`, and a small program. The XMP variants carry the packet as a comment block,
// which is where the pinned tool looks for it. The DOS variant frames the same PostScript section in the
// thirty-byte header of the Windows EPS convention and adds a TIFF section whose bytes are a classic little-endian
// TIFF.
//
// PGF: the header is `PGF` and a version byte, the size of the header region as a 32-bit word, a sixteen-byte
// structure (width, height, levels, quality, bits per pixel, channels, mode, ...), an optional 256-entry colour
// table when the mode byte is 2, and then the header region itself, which holds one whole image file. Our PGF
// files put a PNG in that region, which is what the pinned tool expects to find and hands to its own reader.
//
// Deterministic; CI regenerates and compares SHA-256 against tests/fixtures/b11eps/manifest.json.
//
// Usage: eps-pgf-fixtures generate OUTDIR   # write fixtures + manifest.json
//        eps-pgf-fixtures verify OUTDIR     # fail if manifest pins drift
//        eps-pgf-fixtures print-manifest    # emit pinned manifest JSON
internal static class EpsPgfFixtures
{
    public static readonly string Manifest = Repo.Path("tests/fixtures/b11eps/manifest.json");

    public static readonly byte[] EpsFirst = "%!PS-Adobe-3.0 EPSF-3.0"u8.ToArray();
    public static readonly byte[] EpsFirstSpace = "%!PS-Adobe-3.0 EPSF-3.0 "u8.ToArray();  // OpenOffice
    public static readonly byte[] EpsFirst31 = "%!PS-Adobe-3.1 EPSF-3.0"u8.ToArray();     // Illustrator
    public static readonly byte[] DosSignature = [0xC5, 0xD0, 0xD3, 0xC6];
    public static readonly byte[] PacketTrailer = "<?xpacket end=\"w\"?>"u8.ToArray();
    public const int DosHeaderBytes = 30;

    /// <summary>The authored packet of the JPEG/PNG fixtures, which both sides already know.</summary>
    public static byte[] XmpPacket() => O.XmpPacket();

    /// <summary>A plain EPS file: first line, comment block, %%EndComments, then a program. announce: the
    /// %ADO_ContainsXMP line, which defaults to the truth about packet.</summary>
    public static byte[] EpsFile(byte[]? packet = null, byte[]? first = null, byte[]? newline = null, byte[]? trailer = null,
                                 byte[]? announce = null, bool announceGiven = false, byte[]? extraComments = null, byte[]? body = null,
                                 bool padding = true)
    {
        first ??= EpsFirst;
        newline ??= "\n"u8.ToArray();
        trailer ??= "<?xpacket end=\"w\"?>"u8.ToArray();
        extraComments ??= [];
        body ??= [];
        if (!announceGiven && announce is null) announce = packet is not null ? "%ADO_ContainsXMP: MainFirst"u8.ToArray() : null;
        // The authored packet ends with the writable trailer of its own. A file states its trailer once, after the
        // packet, so the one the caller asks for is the one that stands there: the reference reads the first trailer
        // it meets, and a packet with two would be read by the first of them.
        if (packet is not null && packet.AsSpan().EndsWith(PacketTrailer)) packet = packet[..^PacketTrailer.Length];
        var comment = new List<byte>();
        comment.AddRange([.. first, .. newline, .. "%%BoundingBox: 0 0 100 100"u8, .. newline]);
        if (announce is not null) comment.AddRange([.. announce, .. newline]);
        comment.AddRange([.. extraComments, .. "%%EndComments"u8, .. newline]);
        // The program is padded so that no EPS file of this catalogue is as long as the twenty-six bytes a TARGA
        // footer occupies: a file that short would end in bytes a TARGA reader is told by.
        var program = new List<byte>();
        if (padding) program.AddRange([.. "% padding for a length no TARGA footer can be read into"u8, .. newline]);
        program.AddRange([.. "%%BeginProlog"u8, .. newline, .. "%%EndProlog"u8, .. newline]);
        program.AddRange([.. "%%Page: 1 1"u8, .. newline, .. "%%BeginPageSetup"u8, .. newline, .. "%%EndPageSetup"u8, .. newline]);
        program.AddRange(body);
        program.AddRange([.. "%%PageTrailer"u8, .. newline, .. "%%Trailer"u8, .. newline, .. "%%EOF"u8, .. newline]);
        if (packet is null) return [.. comment, .. program];
        return [.. comment, .. packet, .. newline, .. trailer, .. newline, .. program];
    }

    /// <summary>The same PostScript section framed by the thirty-byte DOS header, with a TIFF section.</summary>
    public static byte[] DosEpsFile(byte[] postscript, byte[]? tiff = null)
    {
        tiff ??= [];
        const int psOffset = DosHeaderBytes;
        int tiffOffset = tiff.Length > 0 ? psOffset + postscript.Length : 0;
        byte[] header = [.. DosSignature, .. PyStruct.Pack("<II", psOffset, postscript.Length),
                         .. PyStruct.Pack("<II", 0, 0),                    // no WMF section
                         .. PyStruct.Pack("<II", tiffOffset, tiff.Length),
                         .. PyStruct.Pack("<H", 0xFFFF)];                  // the documented checksum
        if (header.Length != DosHeaderBytes) throw new PyException("AssertionError", header.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return [.. header, .. postscript, .. tiff];
    }

    /// <summary>The sixteen-byte header structure: width and height first, all little endian.</summary>
    public static byte[] PgfStructure(long width, long height, int levels = 3, int quality = 24, int bpp = 8, int channels = 3, int mode = 0) =>
        [.. PyStruct.Pack("<II", width, height), (byte)levels, (byte)quality, (byte)bpp, (byte)channels, (byte)mode, 0, 0, 0];

    /// <summary>A PGF file whose header region holds image and, for an indexed image, its colour table.</summary>
    public static byte[] PgfFile(long width, long height, byte[] image, int version = '6', byte[]? colourTable = null, int mode = 0)
    {
        // An image that carries a colour table states the indexed mode in the thirteenth byte of the structure, at
        // offset 20 of the file; the caller may say the mode itself instead.
        if (colourTable is not null && mode == 0) mode = 2;
        byte[] structure = PgfStructure(width, height, mode: mode);
        byte[] regionPrefix;
        if (colourTable is not null)
        {
            if (colourTable.Length != 768) throw new PyException("AssertionError", "");
            regionPrefix = [.. structure, .. colourTable];
        }
        else
        {
            regionPrefix = structure;
        }
        int headerSize = structure.Length + (colourTable is not null ? 768 : 0) + image.Length;
        return [.. "PGF"u8, (byte)version, .. PyStruct.Pack("<I", headerSize), .. regionPrefix, .. image];
    }

    /// <summary>One fixture: its bytes, layout, recipe and the further manifest fields in order.</summary>
    internal sealed record Fixture(byte[] Bytes, string Layout, string Recipe, (string Key, object? Value)[] Extra);

    private static readonly Lazy<List<(string Name, Fixture Spec)>> Catalog = new(BuildCatalog);

    /// <summary>The catalogue in the order it was registered.</summary>
    public static List<(string Name, Fixture Spec)> Fixtures => Catalog.Value;

    internal static List<(string Name, Fixture Spec)> BuildCatalog()
    {
        var fixtures = new List<(string, Fixture)>();
        void Register(string name, byte[] data, string layout, string recipe, string dispatch, string carriers, bool xmp) =>
            fixtures.Add((name, new Fixture(data, layout, recipe, [("dispatch", dispatch), ("carriers", carriers), ("xmp", xmp)])));
        byte[] packet = XmpPacket();
        byte[] carrier = PngOwnedFixtures.Png([PngOwnedFixtures.ExifChunk(O.ExifIfd0Tiff()), PngOwnedFixtures.XmpItxtChunk(O.XmpPacket())]);
        Register("eps-plain.eps", EpsFile(), "eps-plain", "eps-plain-empty-comment-block-v1", "eps", "none", false);
        Register("eps-reference-write.eps", EpsFile(padding: false), "eps-reference-write", "eps-dsc-page-without-preceding-implicit-content-v1", "eps", "none", false);
        Register("eps-xmp.eps", EpsFile(packet), "eps-xmp", "eps-xmp-packet-in-comment-block-v1", "eps", "xmp", true);
        Register("eps-xmp-space.eps", EpsFile(packet, first: EpsFirstSpace), "eps-xmp-space", "eps-xmp-openoffice-first-line-v1", "eps", "xmp", true);
        Register("eps-xmp-31.eps", EpsFile(packet, first: EpsFirst31), "eps-xmp-31", "eps-xmp-illustrator-first-line-v1", "eps", "xmp", true);
        Register("eps-xmp-crlf.eps", EpsFile(packet, newline: "\r\n"u8.ToArray()), "eps-xmp-crlf", "eps-xmp-dos-line-endings-v1", "eps", "xmp", true);
        Register("eps-xmp-nomain.eps", EpsFile(packet, announce: "%ADO_ContainsXMP: NoMain"u8.ToArray()), "eps-xmp-nomain",
                 "eps-xmp-packet-but-header-says-none-v1", "eps", "none", false);
        Register("eps-xmp-readonly.eps", EpsFile(packet, trailer: "<?xpacket end=\"r\"?>"u8.ToArray()), "eps-xmp-readonly", "eps-xmp-read-only-trailer-v1",
                 "eps", "xmp-refused", false);
        Register("dos-eps-xmp.eps", DosEpsFile(EpsFile(packet), O.ExifIfd0Tiff()), "dos-eps-xmp", "dos-eps-postscript-and-tiff-sections-v1", "eps", "xmp", true);
        Register("pgf-png.pgf", PgfFile(320, 211, carrier), "pgf-png", "pgf-header-region-holding-a-png-v1", "pgf", "exif,xmp", true);
        Register("pgf-colour-table.pgf", PgfFile(64, 48, carrier, mode: 2, colourTable: new byte[768]), "pgf-colour-table", "pgf-indexed-mode-colour-table-v1",
                 "pgf", "exif,xmp", true);
        Register("pgf-empty-region.pgf", PgfFile(8, 8, []), "pgf-empty-region", "pgf-header-region-of-no-length-v1", "pgf", "none", false);
        return fixtures;
    }

    public static byte[] FixtureBytes(string name) => Fixtures.First(item => item.Name == name).Spec.Bytes;

    public static List<object?> ManifestEntries()
    {
        var entries = new List<object?>();
        foreach (var (name, spec) in Fixtures.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var entry = PyDict.Of(("file", name), ("length", (long)spec.Bytes.Length), ("sha256", O.Sha256(spec.Bytes)), ("layout", spec.Layout),
                                  ("recipe", spec.Recipe), ("rights", "owned-synthetic"));
            foreach (var (key, value) in spec.Extra) entry[key] = value;
            entries.Add(entry);
        }
        return entries;
    }

    public static string Generate(string outdir)
    {
        PyFile.MakeDirs(outdir);
        foreach (var (name, spec) in Fixtures) PyFile.WriteBytes(PyPath.Join(outdir, name), spec.Bytes);
        string payload = PyJson.Dumps(ManifestEntries(), indent: 2) + "\n";
        PyFile.WriteText(PyPath.Join(outdir, "manifest.json"), payload);
        return payload;
    }

    /// <summary>Fail when the bytes the writer produces now are not the bytes the manifest pins.</summary>
    public static int Verify(string outdir)
    {
        var expected = (List<object?>)PyJson.Loads(PyFile.ReadText(Manifest))!;
        var actual = ManifestEntries();
        if (!Py.Eq(expected, actual))
        {
            foreach (var (pinned, fresh) in expected.Zip(actual))
            {
                if (!Py.Eq(pinned, fresh))
                {
                    var p = (PyDict)pinned!;
                    var f = (PyDict)fresh!;
                    Out.Error($"drift: {Py.Str(p.Get("file"))} pinned {Py.Str(p.Get("sha256"))} now {Py.Str(f.Get("sha256"))}");
                }
            }
            Out.Error("the fixture bytes have drifted from the pinned manifest");
            return 1;
        }
        foreach (PyDict entry in expected.Cast<PyDict>())
        {
            string path = PyPath.Join(outdir, entry.Str("file"));
            if (File.Exists(path) && O.Sha256(PyFile.ReadBytes(path)) != entry.Str("sha256"))
            {
                Out.Error($"drift: {path} is not the pinned bytes");
                return 1;
            }
        }
        Out.Print($"{expected.Count} fixtures verified");
        return 0;
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("eps-pgf-fixtures", "Generate owned synthetic EPS and PGF fixtures (B11 group: formats with their own containers).")
            .Add("command", choices: ["generate", "verify", "print-manifest"])
            .Add("outdir", nargs: "?", @default: Repo.Path("tests/fixtures/b11eps"));
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        switch (args.Str("command"))
        {
            case "generate":
                Generate(args.Str("outdir"));
                return 0;
            case "verify":
                return Verify(args.Str("outdir"));
        }
        Out.Print(PyJson.Dumps(ManifestEntries(), indent: 2));
        return 0;
    }
}
