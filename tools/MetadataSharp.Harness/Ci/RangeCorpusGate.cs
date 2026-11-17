using System.Text;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Ci;

// RuntimeError of the gate: the corpus, the dump protocol or a threshold table is not what the gate needs.
internal sealed class RangeGateError(string message) : Exception(message);

// Gate read equivalence on the pinned corpus and range costs on named real samples.
//
// (Formerly tools/ci/range_corpus_gate.py; the arguments, the table, the annotations, the thresholds and
// the exit statuses are unchanged.)
internal static class RangeCorpusGate
{
    // Each bound is max(4 KiB, fraction * size + allowance), accounting for block reads. JPEG/PNG samples
    // have ordinary metadata and large image payloads; other legitimate files can be metadata-dominated, and
    // necessarily cost their metadata size to read.
    public static readonly IReadOnlyDictionary<string, (double Fraction, long Allowance)> Limits = new Dictionary<string, (double, long)>(StringComparer.Ordinal)
    {
        ["CRW"] = (.05, 131072), ["CR2"] = (.07, 131072), ["NEF"] = (.08, 131072),
        ["ARW"] = (.05, 131072), ["ORF"] = (.03, 131072), ["PEF"] = (.12, 131072),
        ["RW2"] = (.23, 131072), ["SRW"] = (.05, 131072), ["DNG"] = (.10, 131072),
        ["MRW"] = (.03, 131072), ["RAF"] = (.06, 131072), ["SR2"] = (.04, 131072),
        ["JPEG"] = (.01, 32768), ["PNG"] = (.10, 16384), ["WEBP"] = (.20, 16384),
        ["HEIC"] = (.15, 65536), ["AVIF"] = (.50, 65536), ["CR3"] = (.60, 65536),
    };

    public static readonly OrderedDictionary<string, string> Exiv2Samples = new(StringComparer.Ordinal)
    {
        ["JPEG"] = "exiv2-bug922a.jpg", ["PNG"] = "issue_828_poc.png",
        ["WEBP"] = "exiv2-bug1199.webp", ["HEIC"] = "2021-02-13-1929.heic",
        ["AVIF"] = "avif.avif", ["CR3"] = "Canon-R6-pruned.CR3",
    };

    public static readonly IReadOnlyDictionary<string, string> Expected = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["JPEG"] = "Jpeg", ["PNG"] = "Png", ["WEBP"] = "WebP", ["HEIC"] = "Bmff",
        ["AVIF"] = "Bmff", ["CR3"] = "Cr3", ["CRW"] = "Crw", ["MRW"] = "Mrw", ["RAF"] = "Raf",
    };

    /// <summary>How a dump is run: replaced by the tests, as the Python tests patched run().</summary>
    internal static Func<IReadOnlyList<string>, string, IReadOnlyList<string>, Dictionary<string, List<List<string>>>> RunDump = Run;

    public static Dictionary<string, List<List<string>>> Run(IReadOnlyList<string> command, string option, IReadOnlyList<string> paths)
    {
        var arguments = command.Concat([option, "--files-from", "-"]).ToList();
        var process = PyProcess.Run(arguments, input: string.Concat(paths.Select(path => path + "\n")), timeout: 1800L, strict: true);
        if (process.ReturnCode != 0) throw new RangeGateError($"{option}: exit {process.ReturnCode}: {PyText.Head(process.Stderr, 1000)}");
        var blocks = new Dictionary<string, List<List<string>>>(StringComparer.Ordinal);
        string? current = null;
        foreach (string line in PyText.SplitLines(process.Stdout))
        {
            var fields = line.Split('\t').ToList();
            if (fields[0] == "FILE" && fields.Count == 2)
            {
                if (blocks.ContainsKey(fields[1]) || current is not null) throw new RangeGateError($"duplicate or unclosed FILE: {line}");
                current = fields[1];
                blocks[current] = [];
            }
            else if (current is null)
            {
                throw new RangeGateError($"record outside FILE: {line}");
            }
            else
            {
                blocks[current].Add(fields);
                if (fields[0] == "STATUS") current = null;
            }
        }
        if (current is not null || !blocks.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(paths))
            throw new RangeGateError("missing, duplicate or incomplete file blocks");
        return blocks;
    }

    private static string Repr(List<List<string>> block) => Py.Repr(block.Select(fields => (object?)fields.Cast<object?>().ToList()).ToList());

    private static bool Is(List<string> fields, params string[] expected) => fields.SequenceEqual(expected, StringComparer.Ordinal);

    private static string Escape(string text) => text.Replace("%", "%25", StringComparison.Ordinal);

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("range-corpus-gate")
            .Add("--kind", choices: ["exiv2", "raw"], required: true)
            .Add("--corpus", required: true)
            .Add("--dump-command", required: true)
            .Add("--out", required: true);
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        try
        {
            return Execute(args);
        }
        catch (Exception error) when (error is RangeGateError or PyOSError or ValueError or FormatException or DecoderFallbackException or PyTimeoutExpired)
        {
            Out.Error($"::error title=Range corpus gate::{error.Message}");
            return 1;
        }
    }

    private static int Execute(ParsedArgs args)
    {
        string corpus = PyPath.Normalize(args.Str("corpus"));
        string output = PyPath.Normalize(args.Str("out"));
        string kind = args.Str("kind");
        List<string> paths;
        var samples = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        if (kind == "exiv2")
        {
            // test_reference_files are expected text outputs, not inputs to the parsers.
            paths = PyPath.RGlob(corpus).Where(path => File.Exists(path) && !PyPath.Parts(PyPath.RelativeTo(path, corpus)).Contains("test_reference_files")).ToList();
            foreach (var (key, name) in Exiv2Samples) samples[key] = [PyPath.Join(corpus, name)];
        }
        else
        {
            // The manifest is read relative to the working directory, as the workflow runs from the checkout.
            var manifest = (PyDict)PyOps.Item(PyJson.Loads(PyFile.ReadText("tools/parity/raw-corpus.json")), "files")!;
            paths = Py.Sorted(manifest.Keys.Cast<string>()).Select(name => PyPath.Join(corpus, name)).ToList();
            foreach (var (key, entry) in manifest)
            {
                string format = ((string)PyOps.Item(entry, "format")!).ToUpperInvariant();
                if (!samples.TryGetValue(format, out var list)) samples[format] = list = [];
                list.Add(PyPath.Join(corpus, (string)key));
            }
        }
        if (paths.Count == 0 || paths.Any(path => !File.Exists(path)) || samples.Values.Any(list => list.Any(path => !File.Exists(path))))
            throw new RangeGateError("corpus or named samples missing");
        if (paths.Any(path => path.IndexOfAny(['\t', '\r', '\n', '\\']) >= 0))
            throw new RangeGateError("corpus path cannot be represented by dump protocol");
        var command = Shlex.Split(args.Str("dump_command"));
        var equivalence = RunDump(command, "--equivalence", paths);
        var failures = new List<string>();
        foreach (string path in paths)
        {
            var block = equivalence[path];
            if (!(block.Count == 2 && Is(block[0], "EQUIV", "ok") && Is(block[1], "STATUS", "ok")))
                failures.Add($"equivalence {path}: {Repr(block)}");
        }
        var measured = samples.Values.SelectMany(list => list).ToList();
        measured.Sort(PyPath.Comparer);
        var records = RunDump(command, "--bytes-read", measured);
        var rows = new List<string>
        {
            "| Format | File | Bytes read / file bytes | Read % | Limit |",
            "| --- | --- | ---: | ---: | ---: |",
        };
        foreach (var (format, files) in Py.Sorted(samples, pair => pair.Key))
        {
            if (!Limits.TryGetValue(format, out var limit)) throw new RangeGateError($"no named threshold for {format}");
            var (fraction, allowance) = limit;
            var ordered = files.ToList();
            ordered.Sort(PyPath.Comparer);
            foreach (string path in ordered)
            {
                var block = records[path];
                if (block.Count != 2 || block[0].Count != 5 || block[0][0] != "BYTES" || !Is(block[1], "STATUS", "ok"))
                {
                    failures.Add($"byte counter {path}: {Repr(block)}");
                    continue;
                }
                string detected = block[0][1];
                long read = PyText.ParseInt(block[0][2]), size = PyText.ParseInt(block[0][3]), overlaps = PyText.ParseInt(block[0][4]);
                string expected = Expected.TryGetValue(format, out string? name) ? name : "Tiff";
                long cap = Math.Max(4096, (long)(size * fraction) + allowance);
                if (size != new FileInfo(path).Length || read < 0 || detected != expected || read > cap || (format == "PNG" && overlaps != 0))
                {
                    failures.Add($"{format} {path}: format {detected} (expected {expected}), {read}/{size}, limit {cap}, overlapping reads {overlaps}");
                }
                string percent = size != 0 ? Py.FormatFixed(100.0 * read / size, 1) + "%" : "n/a";
                rows.Add($"| {format} | `{PyPath.Name(path)}` | {PyText.Thousands(read)} / {PyText.Thousands(size)} | {percent} | "
                         + $"{Py.FormatFixed(fraction * 100, 0)}% + {PyText.Thousands(allowance)} B |");
            }
        }
        string text = $"## Range gates: {kind}\n\nEquivalence: {paths.Count} files, "
                      + $"{failures.Count(failure => failure.StartsWith("equivalence", StringComparison.Ordinal))} mismatches.\n\n"
                      + string.Join("\n", rows) + "\n";
        if (failures.Count > 0) text += "\nFailures:\n" + string.Join("\n", failures.Select(failure => "- " + failure)) + "\n";
        PyFile.MakeDirs(PyPath.Parent(output));
        PyFile.WriteText(output, text);
        Out.Print(text);
        foreach (string chunk in PyText.Chunks(text, 3000))
            Out.Print("::notice title=Range corpus table::" + Escape(chunk).Replace("\n", "%0A", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal));
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary) PyFile.AppendText(summary, text);
        foreach (string failure in failures)
            Out.Print("::error title=Range corpus gate::" + Escape(failure).Replace("\n", "%0A", StringComparison.Ordinal));
        return failures.Count > 0 ? 1 : 0;
    }
}
