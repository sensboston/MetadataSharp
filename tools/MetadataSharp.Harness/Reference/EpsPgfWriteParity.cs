using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

// Bidirectional writer parity for EPS and PGF against pinned Exiv2. Formerly tools/reference/eps_pgf_write_parity.py.
//
// The fixture executable writes our packets, which Exiv2 then reads. Exiv2 also writes independent EPS and PGF copies,
// which the fixture executable reads through ImageMetadata. The command only uses authored b11eps fixtures; no corpus
// bytes are stored here.
//
// Usage: eps-pgf-write-parity FIXTURES WORK --exiv2 PATH --fixture "COMMAND ..."
internal static class EpsPgfWriteParity
{
    public const string Reference = "Exiv2 0.28.9 at b0859fc61fea5dedfc898aece6b15893ddf4314e";

    // report name ("file" or "file|family") -> (key, value) the tool has to print.
    public static readonly (string ReportName, string Key, string Value)[] ExpectedKeys =
    [
        ("ours-eps-insert.eps", "Xmp.xmp.CreatorTool", "Parity tool"),
        ("ours-eps-replace.eps", "Xmp.xmp.CreatorTool", "Parity tool"),
        ("ours-dos-eps.eps", "Xmp.xmp.CreatorTool", "Parity tool"),
        ("ours-pgf.pgf", "Exif.Image.Artist", "Parity Artist"),
        ("ours-pgf.pgf|iptc", "Iptc.Application2.Headline", "Parity headline"),
        ("ours-pgf.pgf|xmp", "Xmp.xmp.CreatorTool", "Parity tool"),
    ];

    /// <summary>subprocess.run(args, text=True, stdout=PIPE, stderr=PIPE); with check, a non-zero exit raises.</summary>
    public static Completed RunChecked(IReadOnlyList<string> args, bool check = true)
    {
        var result = PyProcess.Run(args, strict: true);
        if (check && result.ReturnCode != 0)
            throw new PyException("RuntimeError", $"command failed ({result.ReturnCode}): {Py.Repr(args.ToList())}\n{result.Stdout}\n{result.Stderr}");
        return result;
    }

    public static OrderedDictionary<string, List<string>> Exiv2Properties(string exiv2, string path)
    {
        var result = RunChecked([exiv2, "-Pkv", path], check: false);
        var properties = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string line in PyText.SplitLines(result.Stdout))
        {
            var fields = PyText.Split(line, 1);
            if (fields.Count > 0 && (fields[0].StartsWith("Exif.", StringComparison.Ordinal) || fields[0].StartsWith("Iptc.", StringComparison.Ordinal)
                                     || fields[0].StartsWith("Xmp.", StringComparison.Ordinal)))
            {
                if (!properties.TryGetValue(fields[0], out var values)) properties[fields[0]] = values = [];
                values.Add(fields.Count > 1 ? PyText.Strip(fields[1]) : "");
            }
        }
        if (result.ReturnCode is not (0 or 253) || (result.ReturnCode == 253 && properties.Count > 0))
            throw new PyException("RuntimeError", $"Exiv2 could not print {path}: {result.Stdout} {result.Stderr}");
        return properties;
    }

    public static int Run(string[] argv)
    {
        try
        {
            return Execute(argv);
        }
        catch (Exception error) when (error is not SystemExit)
        {
            Out.Error($"::error title=EPS/PGF write parity::{error.Message.Replace("%", "%25", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal)}");
            return 1;
        }
    }

    public static int Execute(IReadOnlyList<string> argv)
    {
        var parser = new ArgParser("eps-pgf-write-parity", null)
            .Add("fixtures")
            .Add("work")
            .Add("--exiv2", required: true)
            .Add("--fixture", required: true, help: "reference fixture executable command");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string fixtures = PyPath.Normalize(args.Str("fixtures")), work = PyPath.Normalize(args.Str("work")), exiv2 = args.Str("exiv2");
        PyFile.MakeDirs(work);
        var generator = Shlex.Split(args.Str("fixture"));
        RunChecked([.. generator, "small-format-writes", fixtures, work]);

        var outcomes = new PyDict();
        foreach (var (reportName, key, value) in ExpectedKeys)
        {
            string name = PyText.Split(reportName, "|", 1)[0];
            var properties = Exiv2Properties(exiv2, PyPath.Join(work, name));
            var values = properties.GetValueOrDefault(key) ?? [];
            string label = $"our-write/exiv2-read/{name}/{key}";
            outcomes[label] = values.Any(item => item.Contains(value, StringComparison.Ordinal)) ? "equal" : $"differs: wanted {Py.ReprString(value)}, found {Py.Repr(values)}";
        }
        var removed = Exiv2Properties(exiv2, PyPath.Join(work, "ours-eps-remove.eps"));
        outcomes["our-write/exiv2-read/eps-remove/Xmp.xmp.CreatorTool"] = !removed.ContainsKey("Xmp.xmp.CreatorTool") ? "equal (removed)" : "differs: packet remains visible";
        var commentResult = RunChecked([exiv2, "-pc", PyPath.Join(work, "ours-pgf.pgf")]);
        // Pinned Exiv2 0.28.9 copies EXIF/IPTC/XMP between PGF and PNG but does not copy the comment member in either
        // direction; its PgfImage implementation reads and writes no comment field.
        outcomes["our-write/exiv2-read/ours-pgf.pgf/comment"] = PyText.Strip(commentResult.Stdout).Length == 0
            ? "expected divergence: pinned Exiv2 does not expose PGF comments"
            : $"differs from pinned no-comment behavior: {Py.ReprString(commentResult.Stdout)}";

        // Exiv2's page scanner refuses the padded EPS reader fixture because its ordinary comment creates an implicit
        // page before the explicit DSC page. Use the authored no-padding DSC fixture for writes. Exiv2 writes a new XMP
        // packet there and all four PGF metadata families in the PNG carrier.
        PyFile.WriteText(PyPath.Join(work, "eps-modify.txt"), "set Xmp.xmp.CreatorTool Reference writer\n");
        string epsWritten = PyPath.Join(work, "reference.eps");
        PyFile.WriteBytes(epsWritten, PyFile.ReadBytes(PyPath.Join(fixtures, "eps-reference-write.eps")));
        RunChecked([exiv2, "-m", PyPath.Join(work, "eps-modify.txt"), "mo", epsWritten]);

        PyFile.WriteText(PyPath.Join(work, "pgf-modify.txt"),
                         "set Exif.Image.Artist Reference Artist\nset Iptc.Application2.Headline Reference headline\nset Xmp.xmp.CreatorTool Reference writer\n");
        string pgfWritten = PyPath.Join(work, "reference.pgf");
        PyFile.WriteBytes(pgfWritten, PyFile.ReadBytes(PyPath.Join(fixtures, "pgf-png.pgf")));
        RunChecked([exiv2, "-c", "Reference comment", "-m", PyPath.Join(work, "pgf-modify.txt"), "mo", pgfWritten]);

        object? Inspect(string path) => PyJson.Loads(RunChecked([.. generator, "inspect-small-format", path]).Stdout);

        var checks = new List<(string Name, object? Actual, PyDict Expected)>
        {
            ("reference.eps", Inspect(epsWritten), PyDict.Of(("format", "Eps"), ("creatorTool", "Reference writer"))),
        };
        checks.Add(("reference.pgf", Inspect(pgfWritten), PyDict.Of(("format", "Pgf"), ("artist", "Reference Artist"), ("headline", "Reference headline"),
                                                               ("creatorTool", "Reference writer"), ("comments", new List<object?>()))));
        foreach (var (name, actual, expected) in checks)
        {
            foreach (var (key, value) in expected)
            {
                string label = $"exiv2-write/our-read/{name}/{key}";
                if (actual is not PyDict found) throw new PyException("AttributeError", $"'{Py.TypeName(actual)}' object has no attribute 'get'");
                object? item = found.TryGetValue(key, out object? present) ? present : null;
                // The reference's PGF writer drops the comment before it writes the PNG carrier. Record that pinned,
                // source-supported difference instead of making a parity measurement a gate.
                if (name == "reference.pgf" && Py.Eq(key, "comments") && Py.Eq(item, value)) outcomes[label] = "expected divergence: pinned Exiv2 PGF writer omits comments";
                else outcomes[label] = Py.Eq(item, value) ? "equal" : $"differs: wanted {Py.Repr(value)}, found {Py.Repr(item)}";
            }
        }

        var texts = outcomes.Values.Cast<string>().ToList();
        var report = PyDict.Of(
            ("reference", Reference),
            ("outcomes", outcomes),
            ("keys_found", (long)outcomes.Count),
            ("keys_equal", (long)texts.Count(text => text.StartsWith("equal", StringComparison.Ordinal))),
            ("expected_divergence_count", (long)texts.Count(text => text.StartsWith("expected divergence", StringComparison.Ordinal))),
            ("difference_count", (long)texts.Count(text => text.StartsWith("differs", StringComparison.Ordinal))),
            ("problem_count", 0L));
        PyFile.WriteText(PyPath.Join(work, "write-parity.json"), PyJson.Dumps(report, indent: 2) + "\n");
        Out.Print(PyJson.Dumps(report, sortKeys: true));
        string summary = $"EPS/PGF writer parity: {report["keys_found"]} target key checks found; "
                         + $"{report["keys_equal"]} equal; {report["expected_divergence_count"]} pinned divergences; "
                         + $"{report["difference_count"]} other differences; {report["problem_count"]} structural problems.\n";
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summaryPath) PyFile.AppendText(summaryPath, summary);
        return 0;
    }
}
