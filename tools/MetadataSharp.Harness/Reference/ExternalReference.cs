using System.Collections;
using System.Text.RegularExpressions;
using System.Xml;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

// Optional independent CLI comparison. No third-party test code or fixtures. Formerly tools/reference/harness.py:
// the owned fixtures of tools/MetadataSharp.ReferenceFixture, read and edited by the pinned exiv2, exiftool and
// dwebp of the external-reference job, every command recorded in artifacts/reference/report.json.
internal static class ExternalReference
{
    public const string ExpectedExiv2 = "0.27.6";
    public const string ExpectedExiftool = "12.76";
    public const string ExpectedDwebp = "1.3.2";

    private static PyException Runtime(string message) => new("RuntimeError", message);

    private static void Assert(bool condition)
    {
        if (!condition) throw new PyException("AssertionError", "");
    }

    private static void Assert(bool condition, object? message)
    {
        if (!condition) throw new PyException("AssertionError", Py.Str(message));
    }

    public static bool Available(bool required, Func<string, string?>? which = null)
    {
        which ??= PyShutil.Which;
        var missing = new[] { "dotnet", "exiv2", "exiftool", "dwebp" }.Where(name => which(name) is null).ToList();
        if (missing.Count > 0 && required) throw Runtime("Required tools missing: " + string.Join(", ", missing));
        return missing.Count == 0;
    }

    private static readonly Regex Exiv2Version = new($@"\bexiv2{PyRe.Space}+(\d+\.\d+\.\d+)\b", RegexOptions.CultureInvariant);
    private static readonly Regex PlainVersion = new($@"^{PyRe.Space}*(\d+(?:\.\d+)+){PyRe.Space}*\z", RegexOptions.CultureInvariant);

    public static void CheckVersion(string name, string text, string expected)
    {
        var actual = name == "exiv2" ? Exiv2Version.Match(text) : PlainVersion.Match(text);
        if (!actual.Success || actual.Groups[1].Value != expected)
            throw Runtime($"{name} version mismatch: expected {expected}, got {Py.ReprString(PyText.Strip(text))}");
    }

    public static PyDict ExivValues(string text)
    {
        var values = new PyDict();
        foreach (string line in PyText.SplitLines(text))
        {
            if (PyText.Strip(line).Length == 0) continue;
            var fields = PyText.Split(line, 3);
            if (fields.Count != 4 || values.ContainsKey(fields[0])) throw Runtime("Unexpected or duplicate Exiv2 value row: " + line);
            values[fields[0]] = new PyTuple(fields[1], ParseInt(fields[2]), fields[3]);
        }
        return values;
    }

    public static List<PyTuple> IptcRows(string text)
    {
        var rows = new List<PyTuple>();
        foreach (string line in PyText.SplitLines(text))
        {
            if (PyText.Strip(line).Length == 0) continue;
            var fields = PyText.Split(line, 3);
            if (fields.Count != 4) throw Runtime("Unexpected IPTC value row: " + line);
            rows.Add(new PyTuple(fields[0], fields[1], ParseInt(fields[2]), fields[3]));
        }
        return rows;
    }

    // int(text): Python's ValueError text for a value that is no integer.
    private static long ParseInt(string text) =>
        PyText.TryParseInt(text, out long value) ? value : throw new PyException("ValueError", $"invalid literal for int() with base 10: {Py.ReprString(text)}");

    /// <summary>str.casefold() for the comparisons below: lower case, with the full foldings Unicode adds to it.</summary>
    public static string CaseFold(string text) =>
        text.ToLowerInvariant().Replace("\u00df", "ss", StringComparison.Ordinal).Replace("\u017f", "s", StringComparison.Ordinal)
            .Replace("\ufb00", "ff", StringComparison.Ordinal).Replace("\ufb01", "fi", StringComparison.Ordinal).Replace("\ufb02", "fl", StringComparison.Ordinal)
            .Replace("\ufb03", "ffi", StringComparison.Ordinal).Replace("\ufb04", "ffl", StringComparison.Ordinal).Replace("\ufb05", "st", StringComparison.Ordinal)
            .Replace("\ufb06", "st", StringComparison.Ordinal);

    // Missing mandatory default-resolution tags are an explicit minimal-EXIF limitation.
    public static bool ClassifyWarning(string text)
    {
        var accepted = new[] { ("0x011a", "XResolution"), ("0x011b", "YResolution"), ("0x0128", "ResolutionUnit"), ("0x0213", "YCbCrPositioning") }
            .Select(pair => CaseFold("Missing required JPEG IFD0 tag " + pair.Item1 + " " + pair.Item2)).ToHashSet(StringComparer.Ordinal);
        return accepted.Contains(CaseFold(text));
    }

    // This is fixture-specific, not a generic JPEG parser: generated metadata never contains FFDA.
    public static byte[] ScanTail(byte[] data)
    {
        int start = data.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0xDA]);
        if (start < 0) throw Runtime("Authored fixture has no SOS");
        return data[start..];
    }

    /// <summary>ET.fromstring(text).find(".//{namespace}local").text: the character data before the first child of
    /// the first such element, None when it has none; an element that is not there fails as None.text does.</summary>
    public static string? ElementText(string xml, string ns, string local)
    {
        var document = new XmlDocument { XmlResolver = null };
        try
        {
            document.LoadXml(xml);
        }
        catch (XmlException error)
        {
            throw new PyException("xml.etree.ElementTree.ParseError", error.Message);
        }
        var element = document.DocumentElement!.GetElementsByTagName(local, ns).Cast<XmlElement>().FirstOrDefault()
                      ?? throw new PyException("AttributeError", "'NoneType' object has no attribute 'text'");
        var text = new System.Text.StringBuilder();
        bool any = false;
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlText or XmlCDataSection or XmlWhitespace or XmlSignificantWhitespace)
            {
                text.Append(child.Value);
                any = true;
                continue;
            }
            if (child is XmlComment or XmlProcessingInstruction) continue;
            break;
        }
        return any ? text.ToString() : null;
    }

    private static object? Index(object? container, int index) => container switch
    {
        IList list when index < list.Count => list[index],
        IList => throw new PyException("IndexError", "list index out of range"),
        PyDict dict => dict[(long)index],
        _ => throw new PyTypeError($"'{Py.TypeName(container)}' object is not subscriptable"),
    };

    private static object? Item(object? container, string key) => container switch
    {
        PyDict dict => dict[key],
        IList => throw new PyTypeError("list indices must be integers or slices, not str"),
        _ => throw new PyTypeError($"'{Py.TypeName(container)}' object is not subscriptable"),
    };

    private static bool Eq(object? left, object? right) => Py.Eq(left, right);

    private static bool Same(byte[] left, byte[] right) => left.AsSpan().SequenceEqual(right);

    private static PyTuple T(params object?[] items) => new(items);

    private static List<object?> L(params object?[] items) => [.. items];

    /// <summary>Runs one command of the harness: argv and the environment, as subprocess.run(..., timeout=60).</summary>
    internal delegate Completed CommandRunner(IReadOnlyList<string> command, IReadOnlyDictionary<string, string> environment);

    public static int Run(string[] argv) =>
        Execute(argv, required => Available(required), (command, environment) => PyProcess.Run(command, timeout: 60L, env: environment, strict: true));

    /// <summary>The harness with its two outside dependencies given: the tool check (available) and the process
    /// runner (subprocess.run), which the tests stand in for.</summary>
    public static int Execute(IReadOnlyList<string> argv, Func<bool, bool> available, CommandRunner run)
    {
        var parser = new ArgParser("external-reference").Add("--required", ArgAction.StoreTrue).Add("--output", @default: "artifacts/reference");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string output = PyPath.Resolve(args.Str("output"));
        PyFile.MakeDirs(output);
        var report = PyDict.Of(("status", "running"), ("locale", "C"), ("commands", new List<object?>()), ("warnings", new List<object?>()));
        void Save() => PyFile.WriteText(PyPath.Join(output, "report.json"), PyJson.Dumps(report, indent: 2));

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) environment[(string)entry.Key] = (string?)entry.Value ?? "";
        environment["LC_ALL"] = "C";
        environment["LANG"] = "C";

        string RunCommand(params string[] command)
        {
            var result = run(command, environment);
            string stdout = result.Stdout, stderr = result.Stderr;
            report.List("commands").Add(PyDict.Of(("args", command.Cast<object?>().ToList()), ("exitCode", (long)result.ReturnCode), ("stdout", stdout), ("stderr", stderr)));
            Save();
            if (result.ReturnCode != 0) throw Runtime($"Command failed ({result.ReturnCode}): {Py.Repr(command.Cast<object?>().ToList())}: {stdout} {stderr}");
            return stdout;
        }

        PyDict Facts(params string[] command) => (PyDict)Index(PyJson.Loads(RunCommand(command)), 0)!;

        void ValidateImage(string image)
        {
            var validation = PyJson.Loads(RunCommand("exiftool", "-validate", "-warning", "-a", "-j", image), pairs: true);
            var pairs = (IList)Index(validation, 0)!;
            foreach (PyTuple pair in pairs)
            {
                string name = (string)pair[0]!;
                object? value = pair[1];
                if (name.ToLowerInvariant().StartsWith("warning", StringComparison.Ordinal))
                {
                    if (value is not string text) throw new PyException("AttributeError", $"'{Py.TypeName(value)}' object has no attribute 'casefold'");
                    string classification = ClassifyWarning(text) ? "documented-minimal-exif-default-tag" : "unclassified";
                    report.List("warnings").Add(PyDict.Of(("file", PyPath.Name(image)), ("text", text), ("classification", classification)));
                    if (classification == "unclassified") throw Runtime("Unclassified validation warning in " + PyPath.Name(image) + ": " + text);
                    Out.Print("::notice title=Reference discrepancy::" + text);
                }
                else if (name.ToLowerInvariant().StartsWith("error", StringComparison.Ordinal))
                {
                    throw Runtime("Validation error: " + Py.Str(value));
                }
            }
        }

        string At(string name) => PyPath.Join(output, name);
        byte[] Read(string name) => PyFile.ReadBytes(At(name));

        try
        {
            // (The Python refused to run under -O, which strips asserts; these checks are never stripped.)
            if (!available(args.Flag("required")))
            {
                report["status"] = "skipped";
                report["reason"] = "Optional tools missing";
                Save();
                Out.Print("SKIP: optional reference tools unavailable");
                return 0;
            }
            string exivVersion = RunCommand("exiv2", "-V");
            string toolVersion = RunCommand("exiftool", "-ver");
            CheckVersion("exiv2", exivVersion, ExpectedExiv2);
            CheckVersion("exiftool", toolVersion, ExpectedExiftool);
            CheckVersion("dwebp", RunCommand("dwebp", "-version"), ExpectedDwebp);
            report["sourceCommit"] = PyText.Strip(RunCommand("git", "rev-parse", "HEAD"));
            var recipeHashes = new PyDict();
            foreach (string recipe in new[]
                     {
                         "tools/MetadataSharp.ReferenceFixture/Program.cs", "tests/MetadataSharp.Tests/Jpeg/JpegFixture.cs", "tests/MetadataSharp.Tests/Png/PngFixture.cs",
                         "tests/MetadataSharp.Tests/WebP/WebPFixture.cs", "tests/MetadataSharp.Tests/Tiff/StandaloneTiffFixture.cs",
                         "tests/MetadataSharp.Tests/Tiff/TiffBasedFixture.cs", "tests/MetadataSharp.Tests/Dng/DngFixture.cs", "tools/MetadataSharp.Harness/Reference/DngFixtures.cs",
                     })
            {
                recipeHashes[recipe] = JpegOwnedFixtures.Sha256(PyFile.ReadBytes(recipe));
            }
            report["recipeHashes"] = recipeHashes;
            report["versions"] = PyDict.Of(("exiv2", ExpectedExiv2), ("exiftool", ExpectedExiftool), ("dwebp", ExpectedDwebp));
            string target = Environment.GetEnvironmentVariable("METADATASHARP_REFERENCE_TFM") ?? "net8.0";
            if (target is not ("net8.0" or "net10.0")) throw new PyException("ValueError", "Unsupported reference target framework");
            string fixture = PyPath.Resolve($"tools/MetadataSharp.ReferenceFixture/bin/Release/{target}/MetadataSharp.ReferenceFixture.dll");
            string Ours(params string[] parameters) => RunCommand(["dotnet", fixture, .. parameters]);

            Ours("generate", output);
            string image = At("ours.jpg");
            byte[] baseline = Read("base.jpg");
            byte[] authored = PyFile.ReadBytes(image);
            Assert(Same(Read("noop.jpg"), authored), "No-op differs");
            Assert(Same(Read("removed.jpg"), baseline), "Whole removal changed unrelated bytes");
            Assert(Same(ScanTail(baseline), ScanTail(authored)), "Encoded-image range changed");
            var values = Facts("exiftool", "-j", "-n", "-Artist", "-Orientation", image);
            Assert(Eq(values["Artist"], "Alice") && Eq(values["Orientation"], 1L), values);
            var rows = ExivValues(RunCommand("exiv2", "-P", "kycv", "-K", "Exif.Image.Artist", "-K", "Exif.Image.Orientation", image));
            Assert(Eq(rows, PyDict.Of(("Exif.Image.Artist", T("Ascii", 6L, "Alice")), ("Exif.Image.Orientation", T("Short", 1L, "1")))), rows);
            ValidateImage(image);
            foreach (var (tool, artist) in new[] { ("exiftool", "External"), ("exiv2", "Exiv2") })
            {
                string changed = At(tool + ".jpg");
                PyShutil.CopyFile(image, changed);
                if (tool == "exiftool") RunCommand(tool, "-overwrite_original", "-Artist=" + artist, changed);
                else RunCommand(tool, "-M", "set Exif.Image.Artist Ascii " + artist, changed);
                var actual = PyJson.Loads(Ours("inspect", changed));
                Assert(Eq(actual, PyDict.Of(("artist", artist), ("orientation", 1L))), actual);
                string copied = At(tool + "-noop.jpg");
                Ours("noop", changed, copied);
                Assert(Same(PyFile.ReadBytes(changed), PyFile.ReadBytes(copied)), tool + " reverse no-op mismatch");
                Assert(Same(ScanTail(PyFile.ReadBytes(changed)), ScanTail(baseline)), tool + " changed encoded data");
            }
            string xmp = At("xmp.jpg");
            ValidateImage(xmp);
            ValidateImage(At("xmp-rich.xmp"));
            Assert(Same(Read("xmp-noop.jpg"), PyFile.ReadBytes(xmp)), "XMP no-op differs");
            Assert(Same(Read("xmp-removed.jpg"), authored), "XMP removal changed EXIF or unrelated bytes");
            Assert(Same(ScanTail(PyFile.ReadBytes(xmp)), ScanTail(baseline)), "XMP changed encoded bytes");
            values = Facts("exiftool", "-j", "-XMP-xmp:CreatorTool", xmp);
            Assert(Eq(values["CreatorTool"], "MetadataSharp"), values);
            rows = ExivValues(RunCommand("exiv2", "-P", "kycv", "-K", "Xmp.xmp.CreatorTool", xmp));
            Assert(Eq(rows, PyDict.Of(("Xmp.xmp.CreatorTool", T("XmpText", 13L, "MetadataSharp")))), rows);
            var rich = Facts("exiftool", "-j", "-XMP-dc:Subject", "-XMP-dc:Title", "-XMP-dc:Title-ru", At("xmp-rich.xmp"));
            Assert(Eq(rich["Subject"], L("alpha", "beta")) && Eq(rich["Title"], "Hello") && Eq(rich["Title-ru"], "\u041f\u0440\u0438\u0432\u0435\u0442"), rich);
            foreach (int size in new[] { 65502, 65503, 65504 })
            {
                string boundary = At($"xmp-boundary-{size}.jpg");
                ValidateImage(boundary);
                object? actual = Item(PyJson.Loads(Ours("inspect-xmp", boundary)), "creatorTool");
                Assert(Py.Truthy(actual) && actual is string text && text.All(c => c == 'a'), "Boundary packet value differs");
                var external = Facts("exiftool", "-j", "-XMP-xmp:CreatorTool", boundary);
                Assert(Eq(external["CreatorTool"], actual), "ExifTool boundary mismatch");
                var boundaryRows = ExivValues(RunCommand("exiv2", "-P", "kycv", "-K", "Xmp.xmp.CreatorTool", boundary));
                Assert(Eq(boundaryRows, PyDict.Of(("Xmp.xmp.CreatorTool", T("XmpText", (long)Py.Len((string)actual!), actual)))), "Exiv2 boundary mismatch");
                Assert(Same(ScanTail(PyFile.ReadBytes(boundary)), ScanTail(baseline)), "Boundary image changed");
            }
            foreach (var (tool, text) in new[] { ("exiftool", "ExternalXmp"), ("exiv2", "Exiv2Xmp") })
            {
                string changed = At(tool + "-xmp.jpg");
                PyShutil.CopyFile(xmp, changed);
                if (tool == "exiftool") RunCommand(tool, "-overwrite_original", "-XMP-xmp:CreatorTool=" + text, changed);
                else RunCommand(tool, "-M", "set Xmp.xmp.CreatorTool XmpText " + text, changed);
                Assert(Eq(PyJson.Loads(Ours("inspect-xmp", changed)), PyDict.Of(("creatorTool", text))), "Reverse XMP value differs");
                string copied = At(tool + "-xmp-noop.jpg");
                Ours("noop-xmp", changed, copied);
                Assert(Same(PyFile.ReadBytes(copied), PyFile.ReadBytes(changed)), "Reverse XMP no-op differs");
                Assert(Same(ScanTail(PyFile.ReadBytes(changed)), ScanTail(baseline)), "Reverse XMP encoded bytes differ");
            }
            string iptc = At("iptc.jpg");
            ValidateImage(iptc);
            Assert(Same(Read("iptc-noop.jpg"), PyFile.ReadBytes(iptc)), "IPTC no-op differs");
            Assert(Same(Read("iptc-removed.jpg"), Read("iptc-expected-removed.jpg")), "IPTC removal lost unrelated resources");
            Assert(Same(ScanTail(PyFile.ReadBytes(iptc)), ScanTail(baseline)), "IPTC changed encoded bytes");
            var own = PyJson.Loads(Ours("inspect-iptc", iptc));
            Assert(Eq(Item(own, "caption"), "\u041f\u0440\u0438\u0432\u0435\u0442") && Eq(Item(own, "keywords"), L("alpha", "beta", "alpha")), own);
            var facts = Facts("exiftool", "-j", "-n", "-IPTC:Caption-Abstract", "-IPTC:Keywords", "-Photoshop:IPTCDigest", iptc);
            Assert(Eq(facts["Caption-Abstract"], Item(own, "caption")) && Eq(facts["Keywords"], Item(own, "keywords")), facts);
            Assert(Eq(((string)facts["IPTCDigest"]!).ToLowerInvariant(), Item(own, "digest")), "Stored IPTC digest is stale");
            var iptcRows = IptcRows(RunCommand("exiv2", "-P", "kycv", "-K", "Iptc.Application2.Keywords", "-K", "Iptc.Application2.Caption", iptc));
            var rowsRepr = iptcRows.Cast<object?>().ToList();
            Assert(Eq(iptcRows.Where(row => Eq(row[0], "Iptc.Application2.Keywords")).Select(row => (object?)T(row[1], row[2], row[3])).ToList(),
                      L(T("String", 5L, "alpha"), T("String", 4L, "beta"), T("String", 5L, "alpha"))), rowsRepr);
            Assert(Eq(iptcRows.Where(row => Eq(row[0], "Iptc.Application2.Caption")).Select(row => (object?)T(row[1], row[2], row[3])).ToList(),
                      L(T("String", 12L, "\u041f\u0440\u0438\u0432\u0435\u0442"))), rowsRepr);
            string full = At("iptc-boundary.jpg");
            ValidateImage(full);
            var fullOwn = Item(PyJson.Loads(Ours("inspect-iptc", full)), "keywords");
            var fullExternal = Facts("exiftool", "-j", "-IPTC:Keywords", full)["Keywords"];
            Assert(Eq(fullExternal, fullOwn) && fullOwn is IList { Count: 950 }, "IPTC full-capacity repetitions differ");
            var fullRows = IptcRows(RunCommand("exiv2", "-P", "kycv", "-K", "Iptc.Application2.Keywords", full));
            Assert(Eq(fullRows.Select(row => row[3]).ToList(), fullOwn), "Exiv2 full-capacity repetitions differ");
            foreach (var (tool, caption) in new[] { ("exiftool", "ExternalCaption"), ("exiv2", "Exiv2Caption") })
            {
                string changed = At(tool + "-iptc.jpg");
                PyShutil.CopyFile(iptc, changed);
                if (tool == "exiftool") RunCommand(tool, "-overwrite_original", "-IPTC:Caption-Abstract=" + caption, changed);
                else RunCommand(tool, "-M", "set Iptc.Application2.Caption String " + caption, changed);
                var actual = PyJson.Loads(Ours("inspect-iptc", changed));
                Assert(Eq(Item(actual, "caption"), caption) && Eq(Item(actual, "keywords"), Item(own, "keywords")), actual);
                string copied = At(tool + "-iptc-noop.jpg");
                Ours("noop-iptc", changed, copied);
                Assert(Same(PyFile.ReadBytes(copied), PyFile.ReadBytes(changed)), "Reverse IPTC no-op differs");
                Assert(Same(ScanTail(PyFile.ReadBytes(changed)), ScanTail(baseline)), "Reverse IPTC encoded bytes differ");
                string refreshed = At(tool + "-iptc-refreshed.jpg");
                Ours("rewrite-iptc", changed, refreshed);
                var current = PyJson.Loads(Ours("inspect-iptc", refreshed));
                var checksum = Facts("exiftool", "-j", "-n", "-Photoshop:IPTCDigest", refreshed);
                Assert(Eq(((string)checksum["IPTCDigest"]!).ToLowerInvariant(), Item(current, "digest")), "Refreshed IPTC digest is stale");
            }
            // PNG independent structural accounting: CRC and IDAT zlib are checked here, without reusing the library
            // scanner or its CRC implementation.
            static PyTuple PngPixels(string path)
            {
                byte[] data = PyFile.ReadBytes(path);
                Assert(data.AsSpan().StartsWith(PngOwnedFixtures.Sig));
                long at = 8;
                var image = new List<byte>();
                while (at < data.Length)
                {
                    long length = BigEndian(PyBytes.Slice(data, at, at + 4));
                    byte[] kind = PyBytes.Slice(data, at + 4, at + 8);
                    byte[] body = PyBytes.Slice(data, at + 8, at + 8 + length);
                    Assert(Zlib.Crc32([.. kind, .. body]) == BigEndian(PyBytes.Slice(data, at + 8 + length, at + 12 + length)));
                    if (kind.AsSpan().SequenceEqual("IDAT"u8)) image.AddRange(body);
                    at += length + 12;
                }
                Assert(at == data.Length);
                byte[] compressed = [.. image];
                return new PyTuple(compressed, Zlib.Decompress(compressed));
            }
            string png = At("ours.png");
            Assert(Eq(PngPixels(png), PngPixels(At("base.png"))));
            Assert(Eq(PngPixels(png)[1], new byte[] { 0, 10, 20, 30 }));
            Assert(Same(Read("noop.png"), PyFile.ReadBytes(png)));
            Assert(Same(Read("removed.png"), Read("exif.png")));
            ValidateImage(png);
            values = Facts("exiftool", "-j", "-n", "-Artist", "-Orientation", "-XMP-xmp:CreatorTool", png);
            Assert(Eq(values["Artist"], "Alice") && Eq(values["Orientation"], 1L) && Eq(values["CreatorTool"], "MetadataSharp"), values);
            rows = ExivValues(RunCommand("exiv2", "-P", "kycv", "-K", "Exif.Image.Artist", "-K", "Xmp.xmp.CreatorTool", png));
            Assert(Eq(rows, PyDict.Of(("Exif.Image.Artist", T("Ascii", 6L, "Alice")), ("Xmp.xmp.CreatorTool", T("XmpText", 13L, "MetadataSharp")))), rows);
            {
                string changed = At("external.png");
                PyShutil.CopyFile(png, changed);
                RunCommand("exiftool", "-overwrite_original", "-EXIF:Artist=ExternalPng", "-XMP-xmp:CreatorTool=ExternalPng", changed);
                var actual = PyJson.Loads(Ours("inspect-png", changed));
                Assert(Eq(Item(actual, "artist"), "ExternalPng") && Item(actual, "xmpEditable") is false, actual);
                // ExifTool emits a read-only xpacket in PNG. This is intentionally opaque under the already approved XMP
                // profile, not an editable reverse-read pass.
                Assert(((string)Item(actual, "rawXmp")!).Contains("end='r'", StringComparison.Ordinal));
                Assert(Eq(ElementText((string)Item(actual, "rawXmp")!, "http://ns.adobe.com/xap/1.0/", "CreatorTool"), "ExternalPng"));
                Ours("probe-png-opaque", changed);
                ((List<object?>)report.SetDefault("limitations", new List<object?>())!).Add(
                    "ExifTool 12.76 PNG XMP uses read-only xpacket: owned raw packet preserved, property editing refused before output, whole removal tested; semantic reverse property read unsupported");
                string copied = At("external-noop.png");
                Ours("noop-png", changed, copied);
                Assert(Same(PyFile.ReadBytes(copied), PyFile.ReadBytes(changed)));
                Assert(Eq(PngPixels(changed), PngPixels(png)));
            }
            static byte[] WebpCoded(string path)
            {
                byte[] data = PyFile.ReadBytes(path);
                Assert(PyBytes.Slice(data, 0, 4).AsSpan().SequenceEqual("RIFF"u8) && PyBytes.Slice(data, 8, 12).AsSpan().SequenceEqual("WEBP"u8));
                Assert(LittleEndian(PyBytes.Slice(data, 4, 8)) == data.Length - 8);
                long at = 12;
                var coded = new List<byte>();
                while (at < data.Length)
                {
                    long size = LittleEndian(PyBytes.Slice(data, at + 4, at + 8));
                    long end = at + 8 + size + size % 2;
                    Assert(end <= data.Length);
                    if (size % 2 != 0) Assert(data[end - 1] == 0);
                    byte[] fourcc = PyBytes.Slice(data, at, at + 4);
                    if (fourcc.AsSpan().SequenceEqual("VP8 "u8) || fourcc.AsSpan().SequenceEqual("VP8L"u8) || fourcc.AsSpan().SequenceEqual("ALPH"u8)
                        || fourcc.AsSpan().SequenceEqual("ICCP"u8))
                        coded.AddRange(PyBytes.Slice(data, at, end));
                    at = end;
                }
                Assert(at == data.Length);
                return [.. coded];
            }
            for (int kind = 0; kind < 4; kind++)
            {
                string webp = At($"ours-{kind}.webp"), baseWebp = At($"base-{kind}.webp");
                Assert(Same(WebpCoded(webp), WebpCoded(baseWebp)));
                Assert(Same(Read($"noop-{kind}.webp"), PyFile.ReadBytes(webp)));
                Assert(Same(Read($"removed-{kind}.webp"), Read($"exif-{kind}.webp")));
                ValidateImage(webp);
                values = Facts("exiftool", "-j", "-n", "-Artist", "-Orientation", "-XMP-xmp:CreatorTool", webp);
                Assert(Eq(values["Artist"], "Alice") && Eq(values["Orientation"], 1L) && Eq(values["CreatorTool"], "MetadataSharp"), values);
                rows = ExivValues(RunCommand("exiv2", "-P", "kycv", "-K", "Exif.Image.Artist", "-K", "Xmp.xmp.CreatorTool", webp));
                Assert(Eq(rows, PyDict.Of(("Exif.Image.Artist", T("Ascii", 6L, "Alice")), ("Xmp.xmp.CreatorTool", T("XmpText", 13L, "MetadataSharp")))), rows);
                string decoded = At($"base-{kind}.pam"), editedPixels = At($"ours-{kind}.pam");
                RunCommand("dwebp", baseWebp, "-pam", "-o", decoded);
                RunCommand("dwebp", webp, "-pam", "-o", editedPixels);
                Assert(Same(PyFile.ReadBytes(decoded), PyFile.ReadBytes(editedPixels)), "Independent WebP decoded pixels differ");
                string external = At($"external-{kind}.webp");
                PyShutil.CopyFile(webp, external);
                RunCommand("exiftool", "-overwrite_original", "-EXIF:Artist=ExternalWebP", "-XMP-xmp:CreatorTool=ExternalWebP", external);
                var actual = PyJson.Loads(Ours("inspect-webp", external));
                Assert(Eq(Item(actual, "artist"), "ExternalWebP"), actual);
                if (Py.Truthy(Item(actual, "xmpEditable")))
                {
                    Assert(Eq(Item(actual, "creatorTool"), "ExternalWebP"), actual);
                }
                else
                {
                    Assert(((string)Item(actual, "rawXmp")!).Contains("end='r'", StringComparison.Ordinal));
                    Assert(Eq(ElementText((string)Item(actual, "rawXmp")!, "http://ns.adobe.com/xap/1.0/", "CreatorTool"), "ExternalWebP"));
                    Ours("probe-webp-opaque", external);
                    ((List<object?>)report.SetDefault("limitations", new List<object?>())!).Add(
                        $"ExifTool 12.76 WebP kind {kind}: read-only xpacket opaque; property refusal/whole removal tested; semantic reverse property read unsupported");
                }
                string copied = At($"external-noop-{kind}.webp");
                Ours("noop-webp", external, copied);
                Assert(Same(PyFile.ReadBytes(copied), PyFile.ReadBytes(external)) && Same(WebpCoded(external), WebpCoded(baseWebp)));
            }
            foreach (string name in new[] { "legacy.webp", "legacy-rebuilt.webp" })
            {
                var legacy = Facts("exiftool", "-j", "-n", "-Orientation", At(name));
                Assert(Eq(legacy["Orientation"], 1L), legacy);
            }
            Assert(Same(WebpCoded(At("legacy.webp")), WebpCoded(At("legacy-rebuilt.webp"))));
            Assert(PyBytes.Contains(Read("legacy.webp"), "Exif\0\0"u8));
            Assert(!PyBytes.Contains(Read("legacy-rebuilt.webp"), "Exif\0\0"u8));
            foreach (bool big in new[] { false, true })
            {
                foreach (bool tiles in new[] { false, true })
                {
                    string B(bool value) => value ? "True" : "False";
                    string tiff = At($"owned-{B(big)}-{B(tiles)}.tif");
                    var ownTiff = PyJson.Loads(Ours("inspect-tiff", tiff));
                    Assert(Eq(ownTiff, PyDict.Of(("width", 16L), ("height", 16L), ("supported", true), ("segments", 1L), ("storage", tiles ? "Tiles" : "Strips"))), ownTiff);
                    var tiffFacts = Facts("exiftool", "-j", "-n", "-ImageWidth", "-ImageHeight", "-BitsPerSample", "-Compression", tiff);
                    Assert(Eq(tiffFacts["ImageWidth"], 16L) && Eq(tiffFacts["ImageHeight"], 16L) && Eq(tiffFacts["BitsPerSample"], 8L) && Eq(tiffFacts["Compression"], 1L), tiffFacts);
                    var tiffRows = ExivValues(RunCommand("exiv2", "-P", "kycv", "-K", "Exif.Image.ImageWidth", "-K", "Exif.Image.ImageLength", tiff));
                    Assert(Eq(tiffRows, PyDict.Of(("Exif.Image.ImageWidth", T("Long", 1L, "16")), ("Exif.Image.ImageLength", T("Long", 1L, "16")))), tiffRows);
                    string changed = At($"external-{B(big)}-{B(tiles)}.tif");
                    PyShutil.CopyFile(tiff, changed);
                    RunCommand("exiftool", "-overwrite_original", "-IFD0:Artist=ExternalTiff", changed);
                    Assert(Eq(PyJson.Loads(Ours("inspect-tiff", changed)), ownTiff));
                    string editedTiff = At($"edited-{B(big)}-{B(tiles)}.tif");
                    byte[] baseBytes = PyFile.ReadBytes(tiff), editedBytes = PyFile.ReadBytes(editedTiff);
                    Assert(Same(PyBytes.Slice(editedBytes, 0, 4), PyBytes.Slice(baseBytes, 0, 4)) && Same(PyBytes.Slice(editedBytes, 8, baseBytes.Length), PyBytes.Slice(baseBytes, 8)));
                    Assert(Same(Read($"edit-noop-{B(big)}-{B(tiles)}.tif"), editedBytes));
                    // Independent bounded pointer/value accounting for the owned one-IFD profile.
                    long Number(byte[] data, long at, int size)
                    {
                        Assert(at >= 0 && at + size <= data.Length);
                        byte[] slice = PyBytes.Slice(data, at, at + size);
                        return big ? BigEndian(slice) : LittleEndian(slice);
                    }
                    long root = Number(editedBytes, 4, 4);
                    Assert(root >= baseBytes.Length && root % 2 == 0);
                    long count = Number(editedBytes, root, 2);
                    Assert(1 <= count && count <= 512);
                    Assert(Number(editedBytes, root + 2 + 12 * count, 4) == 0);
                    var fields = new Dictionary<long, (long Type, long Count, byte[] Value)>();
                    var extents = new List<(long Start, long End)> { (0, 8), (root, root + 6 + 12 * count) };
                    long last = -1;
                    for (long i = 0; i < count; i++)
                    {
                        long at = root + 2 + i * 12;
                        long tag = Number(editedBytes, at, 2), typ = Number(editedBytes, at + 2, 2), n = Number(editedBytes, at + 4, 4);
                        Assert(tag > last);
                        last = tag;
                        long width = typ switch { 2 => 1, 3 => 2, 4 => 4, _ => throw new PyException("KeyError", typ.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
                        long size = width * n;
                        long pos = size <= 4 ? at + 8 : Number(editedBytes, at + 8, 4);
                        Assert(pos + size <= editedBytes.Length);
                        if (size > 4)
                        {
                            Assert(pos % 2 == 0);
                            extents.Add((pos, pos + size));
                        }
                        fields[tag] = (typ, n, PyBytes.Slice(editedBytes, pos, pos + size));
                    }
                    var (offsetTag, countTag) = tiles ? (324L, 325L) : (273L, 279L);
                    byte[] Field(long tag) => fields.TryGetValue(tag, out var field) ? field.Value : throw new PyException("KeyError", tag.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    long stripStart = big ? BigEndian(Field(offsetTag)) : LittleEndian(Field(offsetTag));
                    long stripSize = big ? BigEndian(Field(countTag)) : LittleEndian(Field(countTag));
                    Assert(stripStart == 2048 && stripSize == 256 && Same(PyBytes.Slice(editedBytes, stripStart, stripStart + stripSize), PyBytes.Slice(baseBytes, stripStart, stripStart + stripSize)));
                    extents.Add((stripStart, stripStart + stripSize));
                    extents.Sort();
                    Assert(extents.Zip(extents.Skip(1)).All(pair => pair.First.End <= pair.Second.Start));
                    Assert(Same(Field(305), "MetadataSharp\0"u8.ToArray()) && Same(Field(315), "Alice\0"u8.ToArray()));
                    var editedFacts = Facts("exiftool", "-j", "-IFD0:Software", "-IFD0:Artist", editedTiff);
                    Assert(Eq(editedFacts["Software"], "MetadataSharp") && Eq(editedFacts["Artist"], "Alice"), editedFacts);
                    var editedRows = ExivValues(RunCommand("exiv2", "-P", "kycv", "-K", "Exif.Image.Software", "-K", "Exif.Image.Artist", editedTiff));
                    Assert(Eq(editedRows, PyDict.Of(("Exif.Image.Software", T("Ascii", 14L, "MetadataSharp")), ("Exif.Image.Artist", T("Ascii", 6L, "Alice")))), editedRows);
                    string removedTiff = At($"edit-removed-{B(big)}-{B(tiles)}.tif");
                    Assert(Eq(PyJson.Loads(Ours("inspect-tiff-values", removedTiff)), PyDict.Of(("artist", null), ("software", "MetadataSharp"))));
                    Assert(PyBytes.Contains(PyFile.ReadBytes(removedTiff), "Alice\0"u8)); // Deliberate physical retention, not privacy scrubbing.
                    Assert(Eq(Item(PyJson.Loads(Ours("inspect-tiff-values", changed)), "artist"), "ExternalTiff"));
                    string refreshed = At($"refreshed-{B(big)}-{B(tiles)}.tif");
                    Ours("rewrite-tiff", changed, refreshed);
                    var refreshedFacts = Facts("exiftool", "-j", "-IFD0:Software", "-IFD0:Artist", refreshed);
                    Assert(Eq(refreshedFacts["Software"], "Refreshed") && Eq(refreshedFacts["Artist"], "ExternalTiff"), refreshedFacts);
                }
            }
            // DNG read-only profile: the two independent writers of the owned fixtures must agree byte-for-byte.
            foreach (string order in new[] { "II", "MM" })
            {
                foreach (string shape in new[] { "raw", "thumb" })
                {
                    var (data, _) = DngFixtures.Build(order, shape);
                    string name = $"dng-{shape}-{(order == "II" ? "ii" : "mm")}.dng";
                    if (!Same(Read(name), data)) throw Runtime($"DNG fixture byte mismatch between independent writers: {name}");
                }
            }
            report["dngFixtureAgreement"] = "byte-identical";

            // Structural oracle, independent of both writers and of the library: classic II/MM magic 42, sorted unique
            // tags, two-byte payload alignment, exact strip extents, DNGVersion on the raw leaf.
            var rawDims = DngCheck(At("dng-raw-ii.dng"), true);
            Assert(Eq(rawDims[3], 32803L));
            DngCheck(At("dng-raw-mm.dng"), true);
            var thumbDims = DngCheck(At("dng-thumb-ii.dng"), false);
            Assert(Eq(thumbDims[3], 2L));
            DngCheck(At("dng-thumb-mm.dng"), false);
            static string DngVersionDigits(object? value) => string.Concat(Py.Str(value).Where(ch => PyText.IsDigit(ch.ToString())));
            facts = Facts("exiftool", "-j", "-n", "-DNGVersion", "-ImageWidth", "-ImageHeight", "-Compression", "-SamplesPerPixel", "-PhotometricInterpretation", At("dng-raw-ii.dng"));
            Assert(DngVersionDigits(facts["DNGVersion"]) == "1100" && Eq(facts["ImageWidth"], 4L) && Eq(facts["ImageHeight"], 2L) && Eq(facts["Compression"], 1L)
                   && Eq(facts["SamplesPerPixel"], 1L) && Eq(facts["PhotometricInterpretation"], 32803L), facts);
            // exiftool surfaces the raw SubIFD image tags (not the IFD0 thumbnail tags) for DNG, so
            // SamplesPerPixel/PhotometricInterpretation below reflect the raw leaf; the thumbnail's own geometry/role is
            // asserted by the structural oracle and inspect-dng.
            facts = Facts("exiftool", "-j", "-n", "-DNGVersion", "-ImageWidth", "-ImageHeight", "-Compression", "-SamplesPerPixel", "-PhotometricInterpretation", At("dng-thumb-ii.dng"));
            Assert(DngVersionDigits(facts["DNGVersion"]) == "1100" && Eq(facts["ImageWidth"], 4L) && Eq(facts["ImageHeight"], 2L) && Eq(facts["Compression"], 1L)
                   && Eq(facts["SamplesPerPixel"], 1L) && Eq(facts["PhotometricInterpretation"], 32803L), facts);
            var ownDng = PyJson.Loads(Ours("inspect-dng", At("dng-raw-ii.dng")));
            Assert(Eq(ownDng, PyDict.Of(("width", 4L), ("height", 2L), ("bitsPerSample", 16L), ("samplesPerPixel", 1L), ("supported", true), ("role", "CfaRaw"), ("strips", 1L))), ownDng);
            ownDng = PyJson.Loads(Ours("inspect-dng", At("dng-thumb-ii.dng")));
            Assert(Eq(ownDng, PyDict.Of(("width", 4L), ("height", 2L), ("bitsPerSample", 8L), ("samplesPerPixel", 3L), ("supported", true), ("role", "RgbThumbnail"), ("strips", 1L))), ownDng);

            var entries = PyPath.IterDir(output).ToList();
            entries.Sort(PyPath.Comparer);
            report["fixtures"] = entries.Where(path => PyPath.Suffix(path) is ".jpg" or ".xmp" or ".png" or ".webp" or ".tif" or ".dng")
                .Select(path => (object?)PyDict.Of(("name", PyPath.Name(path)), ("sha256", JpegOwnedFixtures.Sha256(PyFile.ReadBytes(path))),
                                                   ("source", "MetadataSharp authored synthetic generator"), ("license", "MIT"))).ToList();
            report["status"] = "passed";
            Save();
            Out.Print("PASS: external reads, reverse edits, byte preservation and validation classification");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            report["status"] = "failed";
            report["error"] = error.Message;
            Save();
            string message = error.Message.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);
            Out.Print("::error title=Reference validation::" + message);
            Out.Error(error.Message);
            return 1;
        }
    }

    private static long BigEndian(byte[] data)
    {
        long value = 0;
        foreach (byte b in data) value = (value << 8) | b;
        return value;
    }

    private static long LittleEndian(byte[] data)
    {
        long value = 0;
        for (int i = data.Length - 1; i >= 0; i--) value = (value << 8) | data[i];
        return value;
    }

    private static readonly Dictionary<long, int> TypeWidth = new() { [1] = 1, [2] = 1, [3] = 2, [4] = 4, [5] = 8, [10] = 8 };

    /// <summary>One classic DNG file read by the structural oracle: its directories, the SubIFD of the root and the
    /// checks of an image directory.</summary>
    private sealed class DngFile
    {
        private readonly string name;
        private readonly string fmt;
        private readonly bool little;

        public DngFile(string path)
        {
            Data = PyFile.ReadBytes(path);
            name = PyPath.Name(path);
            little = Data.Length >= 2 && Data[0] == 'I' && Data[1] == 'I';
            fmt = little ? "<" : ">";
            Assert(Eq(new PyTuple(PyStruct.UnpackFrom(fmt + "HI", Data, 2)), T(42L, 8L)), name);
            Root = Ifd(8);
            Sub = Root.Any(field => field.Tag == 330) ? Unsigned(Root, 330) : null;
        }

        public byte[] Data { get; }

        public List<(long Tag, long Type, long Count, byte[] Payload)> Root { get; }

        public long? Sub { get; }

        public List<(long Tag, long Type, long Count, byte[] Payload)> Ifd(long offset)
        {
            long n = Convert.ToInt64(PyStruct.UnpackFrom(fmt + "H", Data, checked((int)offset))[0], System.Globalization.CultureInfo.InvariantCulture);
            Assert(1 <= n && n <= 64, T(name, offset));
            var fields = new List<(long, long, long, byte[])>();
            long last = -1;
            for (long i = 0; i < n; i++)
            {
                long at = offset + 2 + 12 * i;
                var unpacked = PyStruct.UnpackFrom(fmt + "HHI", Data, checked((int)at));
                long tag = Convert.ToInt64(unpacked[0], System.Globalization.CultureInfo.InvariantCulture);
                long typ = Convert.ToInt64(unpacked[1], System.Globalization.CultureInfo.InvariantCulture);
                long count = Convert.ToInt64(unpacked[2], System.Globalization.CultureInfo.InvariantCulture);
                Assert(tag > last && count >= 1, T(name, offset));
                last = tag;
                int width = TypeWidth.TryGetValue(typ, out int known) ? known : throw new PyException("KeyError", typ.ToString(System.Globalization.CultureInfo.InvariantCulture));
                long loc = at + 8;
                byte[] payload;
                if (width * count <= 4)
                {
                    payload = PyBytes.Slice(Data, loc, loc + width * count);
                }
                else
                {
                    long ptr = Convert.ToInt64(PyStruct.UnpackFrom(fmt + "I", Data, checked((int)loc))[0], System.Globalization.CultureInfo.InvariantCulture);
                    Assert(ptr % 2 == 0 && ptr + width * count <= Data.Length, T(name, offset));
                    payload = PyBytes.Slice(Data, ptr, ptr + width * count);
                }
                fields.Add((tag, typ, count, payload));
            }
            long next = Convert.ToInt64(PyStruct.UnpackFrom(fmt + "I", Data, checked((int)(offset + 2 + 12 * n)))[0], System.Globalization.CultureInfo.InvariantCulture);
            Assert(next == 0, T(name, offset));
            return fields;
        }

        public static (long Tag, long Type, long Count, byte[] Payload) Field(List<(long Tag, long Type, long Count, byte[] Payload)> fields, long tag)
        {
            foreach (var field in fields)
                if (field.Tag == tag) return field;
            throw new PyException("StopIteration", "");
        }

        public long Unsigned(List<(long Tag, long Type, long Count, byte[] Payload)> fields, long tag)
        {
            var field = Field(fields, tag);
            byte[] bytes = field.Type == 4 ? PyBytes.Slice(field.Payload, 0, 4) : PyBytes.Slice(field.Payload, 0, 2);
            return little ? LittleEndian(bytes) : BigEndian(bytes);
        }

        public PyTuple CheckImage(List<(long Tag, long Type, long Count, byte[] Payload)> fields)
        {
            long width = Unsigned(fields, 256), height = Unsigned(fields, 257);
            long samples = Unsigned(fields, 277), photometric = Unsigned(fields, 262);
            var bits = Field(fields, 258);
            var bps = PyStruct.Unpack(fmt + new string('H', (int)bits.Count), bits.Payload).Select(value => (object?)Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            List<object?> expected = samples == 1 ? [16L] : [8L, 8L, 8L];
            Assert(Eq(bps, expected), name);
            long strip = Unsigned(fields, 273), stripBytes = Unsigned(fields, 279);
            Assert(stripBytes == width * height * samples * (samples == 1 ? 2 : 1), name);
            Assert(strip + stripBytes <= Data.Length, name);
            return T(width, height, samples, photometric);
        }

        public string Name => name;
    }

    private static PyTuple DngCheck(string path, bool expectLeaf)
    {
        var file = new DngFile(path);
        var dims = file.CheckImage(file.Root);
        if (expectLeaf)
        {
            Assert(Same(DngFile.Field(file.Root, 50706).Payload, [0x01, 0x01, 0x00, 0x00]), file.Name); // DNGVersion 1.1.0.0
        }
        else
        {
            Assert(file.Sub is not null, file.Name);
            var raw = file.Ifd(file.Sub!.Value);
            file.CheckImage(raw);
            Assert(Same(DngFile.Field(raw, 50706).Payload, [0x01, 0x01, 0x00, 0x00]), file.Name);
            Assert(file.Unsigned(raw, 262) == 32803, file.Name); // CFA photometric on raw leaf
        }
        return dims;
    }
}
