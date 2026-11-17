using System.Security.Cryptography;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// The manifest is malformed, or the corpus does not match it.
internal sealed class RawCorpusError(string message) : Exception(message);

// The pinned RAW corpus: downloaded from a free-licensed source, pinned by SHA-256, never committed.
//
// The differential gates read camera raw files that the pinned upstream checkout happens to hold, which is
// one to three files for most raw formats and none at all for some. This command fetches a wider corpus
// from raw.pixls.us, whose upload form is a CC0 1.0 dedication, and keeps it out of the repository:
//
//   * tools/parity/raw-corpus.json records, for every file, the exact URL, the byte size, the SHA-256 and
//     the camera it came from. Nothing is downloaded by the repository itself.
//   * download fetches only what the manifest names, verifies the size and the hash, and refuses to keep a
//     file that does not match its pin. An entry whose pin is still null is a file that has been chosen but
//     not yet measured: it is downloaded and kept, and its measured pin is printed so that it can be
//     written down. verify refuses an unpinned entry, so a run cannot pass on one.
//   * pin prints (or, with --write, records) the measured size and hash of every downloaded file. It is the
//     only way the numbers in the manifest are produced.
//
// Usage:
//     MetadataSharp.Harness raw-corpus download DEST [--report PATH]
//     MetadataSharp.Harness raw-corpus verify DEST
//     MetadataSharp.Harness raw-corpus pin DEST [--write]
//     MetadataSharp.Harness raw-corpus list
//     MetadataSharp.Harness raw-corpus explain DEST --exiv2 EXE --dump-command CMD [--files N] [--name TEXT]
//
// (Formerly tools/parity/raw_corpus.py; the arguments, reports, annotations and exit statuses are unchanged.)
internal static class RawCorpus
{
    public static readonly string Manifest = Repo.Path("tools", "parity", "raw-corpus.json");
    public const int Chunk = 1024 * 1024;

    // One pinned sample.
    public sealed record Entry(string Name, string Url, object? Bytes, object? Sha256, string Camera, string Format, string SourceFile)
    {
        public string Slug => Name.Contains('.') ? Name[..Name.LastIndexOf('.')] : Name;
    }

    /// <summary>The manifest on disk, after the checks below.</summary>
    public static PyDict Load(string? path = null) => Check(PyJson.Loads(PyFile.ReadText(path ?? Manifest)));

    /// <summary>A manifest that is already in memory, checked the same way as one on disk.</summary>
    public static PyDict LoadFrom(object? document) => Check(document);

    private static string AsString(object? value, string method) =>
        value as string ?? throw new PyAttributeError($"'{Py.TypeName(value)}' object has no attribute '{method}'");

    /// <summary>The manifest, after checking the parts every entry needs.</summary>
    public static PyDict Check(object? document)
    {
        if (!Py.Truthy(PyOps.Get(document, "license"))) throw new RawCorpusError("the manifest has no license");
        object? files = PyOps.Get(document, "files");
        if (files is not PyDict filesDict || filesDict.Count == 0) throw new RawCorpusError("the manifest holds no files");
        foreach (var (key, item) in filesDict)
        {
            string name = (string)key;
            foreach (string field in new[] { "url", "camera", "format", "source_file" })
                if (!Py.Truthy(PyOps.Get(item, field))) throw new RawCorpusError($"{name}: no {field}");
            string url = AsString(PyOps.Item(item, "url"), "startswith");
            if (!url.StartsWith("https://raw.pixls.us/", StringComparison.Ordinal))
                throw new RawCorpusError($"{name}: the source is not raw.pixls.us: {url}");
            if (PyPath.Name(name) != name || PyPath.Suffix(name).Length == 0)
                throw new RawCorpusError($"{name}: the local name has to be a plain file name with an extension");
            string expected = AsString(PyOps.Item(item, "source_file"), "lower").ToLowerInvariant();
            if (!name.ToLowerInvariant().EndsWith(PyPath.Suffix(expected), StringComparison.Ordinal))
                throw new RawCorpusError($"{name}: the local extension does not match {Py.Str(PyOps.Item(item, "source_file"))}");
        }
        return (PyDict)document!;
    }

    /// <summary>Every entry of the manifest, ordered by local name.</summary>
    public static List<Entry> Entries(PyDict? document = null)
    {
        document ??= Load();
        return Py.Sorted(document.Dict("files"), pair => pair.Key)
            .Select(pair =>
            {
                var item = (PyDict)pair.Value!;
                return new Entry((string)pair.Key, (string)item["url"]!, item.Get("bytes"), item.Get("sha256"), Py.Str(item["camera"]),
                                 Py.Str(item["format"]), Py.Str(item["source_file"]));
            })
            .ToList();
    }

    /// <summary>The SHA-256 of a file, read in blocks so a 30 MB sample is not held twice.</summary>
    public static string Digest(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, Chunk);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception error) when (PyOSError.From(error, path) is { } python)
        {
            throw python;
        }
    }

    /// <summary>How one URL is fetched into a file: curl, or a stand-in the tests install.</summary>
    internal static Action<string, string> Fetch = Curl;

    /// <summary>Download one URL. curl is used because the runners have it and it retries.</summary>
    private static void Curl(string url, string target)
    {
        string partial = PyPath.WithSuffix(target, PyPath.Suffix(target) + ".part");
        if (PyPath.Exists(partial)) File.Delete(partial);
        string curl = PyShutil.Which("curl") ?? throw new RawCorpusError("curl is not available to download the corpus");
        var command = new List<string> { curl, "-fsSL", "--retry", "5", "--retry-delay", "5", "--max-time", "600", "-o", partial, url };
        var result = PyProcess.Run(command, strict: true);
        if (result.ReturnCode != 0)
        {
            var message = PyText.SplitLines(PyText.Strip(result.Stderr.Length > 0 ? result.Stderr : result.Stdout));
            throw new RawCorpusError($"download failed ({result.ReturnCode}): {(message.Count > 0 ? message[^1] : "no output")}");
        }
        File.Move(partial, target, overwrite: true);
    }

    /// <summary>Why a downloaded file is not the pinned one, with what was measured.</summary>
    private static (string? Problem, PyDict Measured) CheckFile(Entry entry, string path)
    {
        var measured = PyDict.Of(("bytes", new FileInfo(path).Length), ("sha256", Digest(path)));
        if (entry.Bytes is not null && !Py.Eq(measured["bytes"], entry.Bytes))
            return ($"expected {Py.Str(entry.Bytes)} bytes, got {Py.Str(measured["bytes"])}", measured);
        if (entry.Sha256 is not null && !Py.Eq(measured["sha256"], entry.Sha256))
            return ($"expected sha256 {Py.Str(entry.Sha256)}, got {Py.Str(measured["sha256"])}", measured);
        return (null, measured);
    }

    private static void Merge(PyDict target, PyDict values)
    {
        foreach (var (key, value) in values) target[key] = value;
    }

    /// <summary>
    /// Fetch every entry into destination and report what happened to each one. A file that is already
    /// there and matches its pin is left alone, so a repeated run costs nothing. A file that does not match
    /// a pinned size or hash is deleted rather than kept: a gate must never read bytes nobody pinned. An
    /// entry whose pin is null is kept and its measured pin reported.
    /// </summary>
    public static PyDict Download(string destination, PyDict? document = null)
    {
        document ??= Load();
        PyFile.MakeDirs(destination);
        var outcomes = new List<object?>();
        foreach (var entry in Entries(document))
        {
            string path = PyPath.Join(destination, entry.Name);
            var outcome = PyDict.Of(("name", entry.Name), ("camera", entry.Camera), ("format", entry.Format), ("url", entry.Url),
                                    ("pinned", entry.Sha256 is not null));
            if (File.Exists(path))
            {
                var (cachedProblem, cachedMeasure) = CheckFile(entry, path);
                if (cachedProblem is null)
                {
                    Merge(outcome, PyDict.Of(("status", "cached")));
                    Merge(outcome, cachedMeasure);
                    outcomes.Add(outcome);
                    continue;
                }
                if (entry.Sha256 is not null) File.Delete(path);
            }
            try
            {
                Fetch(entry.Url, path);
            }
            catch (RawCorpusError failure)
            {
                Merge(outcome, PyDict.Of(("status", "failed"), ("detail", failure.Message)));
                outcomes.Add(outcome);
                continue;
            }
            var (problem, measured) = CheckFile(entry, path);
            Merge(outcome, measured);
            if (entry.Sha256 is null)
            {
                // No hash to compare against: keep the bytes, report the pin so it can be written down.
                Merge(outcome, PyDict.Of(("status", "unpinned"), ("detail", problem ?? $"bytes={Py.Str(measured["bytes"])}")));
            }
            else if (problem is not null)
            {
                File.Delete(path);
                Merge(outcome, PyDict.Of(("status", "mismatch"), ("detail", problem)));
            }
            else
            {
                Merge(outcome, PyDict.Of(("status", "downloaded")));
            }
            outcomes.Add(outcome);
        }
        return PyDict.Of(("provenance", document.Get("provenance")), ("license", document.Get("license")), ("files", outcomes));
    }

    /// <summary>Every way the directory fails to match the manifest. An empty list is a pass.</summary>
    public static List<string> Verify(string destination, PyDict? document = null)
    {
        document ??= Load();
        var problems = new List<string>();
        foreach (var entry in Entries(document))
        {
            string path = PyPath.Join(destination, entry.Name);
            if (entry.Sha256 is null || entry.Bytes is null)
            {
                problems.Add($"{entry.Name}: the manifest still has no pinned size or hash");
                continue;
            }
            if (!File.Exists(path))
            {
                problems.Add($"{entry.Name}: not downloaded");
                continue;
            }
            var (problem, _) = CheckFile(entry, path);
            if (problem is not null) problems.Add($"{entry.Name}: {problem}");
        }
        return problems;
    }

    /// <summary>Write the measured size and hash of every downloaded file into the manifest.</summary>
    public static PyDict Pin(string destination, bool write = false, PyDict? document = null, string? path = null)
    {
        document ??= Load();
        var updated = (PyDict)PyJson.Loads(PyJson.Dumps(document))!;
        var missing = new List<string>();
        foreach (var entry in Entries(document))
        {
            string source = PyPath.Join(destination, entry.Name);
            if (!File.Exists(source))
            {
                missing.Add(entry.Name);
                continue;
            }
            var item = updated.Dict("files").Dict(entry.Name);
            item["bytes"] = new FileInfo(source).Length;
            item["sha256"] = Digest(source);
        }
        if (missing.Count > 0) throw new RawCorpusError("not downloaded: " + string.Join(", ", missing));
        if (write) PyFile.WriteText(path ?? Manifest, PyJson.Dumps(updated, indent: 2) + "\n");
        return updated;
    }

    /// <summary>
    /// Print what a workflow run needs: the pin of every file in one notice, failures as errors. The pins
    /// share one annotation because a step is allowed only about ten of them, and a run that cannot report
    /// all 23 pins at once cannot be pinned from its own output.
    /// </summary>
    public static void Annotate(PyDict outcomes)
    {
        var files = outcomes.List("files").Cast<PyDict>().ToList();
        var lines = files.Where(item => Py.Truthy(item.Get("sha256")))
            .Select(item => $"{Py.Str(item["name"])} {Py.Str(item["bytes"])} {Py.Str(item["sha256"])} ({Py.Str(item["status"])})")
            .ToList();
        // A workflow command is one line: a literal newline would end the annotation after the first pin, so
        // it is escaped as %0A.
        if (lines.Count > 0) Out.Print("::notice title=raw corpus pins::" + string.Join("%0A", lines));
        var failures = files.Where(item => item.Str("status") is "failed" or "mismatch").ToList();
        foreach (var item in failures.Take(10))
            Out.Print($"::error title=raw corpus {Py.Str(item["status"])}::{Py.Str(item["name"])} {Py.Str(item.Get("detail", ""))}");
        if (failures.Count > 10) Out.Print($"::error title=raw corpus::{failures.Count - 10} further files failed; see download.json");
    }

    /// <summary>A short text summary: what was fetched, what came from the cache, what failed.</summary>
    public static string Describe(PyDict outcomes)
    {
        var files = outcomes.List("files").Cast<PyDict>().ToList();
        var counts = new Counter();
        var formats = new Counter();
        foreach (var item in files)
        {
            counts.Add(item["status"]!, 1);
            formats.Add(item["format"]!, 1);
        }
        long total = files.Where(item => Py.Truthy(item.Get("bytes"))).Sum(item => item.Long("bytes"));
        string parts = string.Join(", ", Py.Sorted(counts, pair => pair.Key).Select(pair => $"{Py.Str(pair.Key)} {Py.Str(pair.Value)}"));
        string layout = string.Join(", ", Py.Sorted(formats, pair => pair.Key).Select(pair => $"{Py.Str(pair.Key)} {Py.Str(pair.Value)}"));
        return $"raw corpus: {files.Count} files, {Py.FormatFixed(total / (1024.0 * 1024.0), 1)} MiB ({parts}); formats: {layout}";
    }

    /// <summary>
    /// Print what the -pt gate still counts against the first corpus files that differ. The gate's own report
    /// is used rather than a plain row comparison, because a row that the gate accepts as a documented display
    /// alias is not a defect and must not be reported as one. Only files that fail are explained: a defect is
    /// fixed one file at a time, and a run reports a few.
    /// </summary>
    public static int Explain(string destination, string exiv2, string dumpCommand, int limit = 60, long files = 4, string name = "")
    {
        // The reason a refusal gives is what a reader defect is diagnosed from, and the gate's own record
        // carries only the code.
        var command = Shlex.Split(dumpCommand).Append("--refusal-detail").ToList();
        long explained = 0;
        foreach (var entry in Entries())
        {
            string path = PyPath.Join(destination, entry.Name);
            if (!File.Exists(path) || (name.Length > 0 && !entry.Name.Contains(name, StringComparison.Ordinal))) continue;
            var candidate = new Candidate(entry.Name, path, "raw-corpus");
            var dumped = TagPrint.RunDump(command, [path]);
            var report = TagPrint.BuildReport([candidate], [exiv2], dumped, []);
            var totals = report.Dict("totals");
            if (!Py.Truthy(totals.Get("failed_files"))) continue;
            var lines = new List<string>
            {
                $"{Py.Str(totals.Get("failed_files", 1L))} failing file(s); "
                + $"kinds={Py.Repr(new PyDict(report.Dict("failure_kinds")))}, types={Py.Repr(new PyDict(report.Dict("difference_types")))}",
            };
            // Which camera the file comes from decides which MakerNote is looked for, and the two sides can
            // disagree on the make without any row showing it.
            var dumpedFile = dumped.Get(path) as PyDict ?? new PyDict();
            var localRows = ((List<object?>?)dumpedFile.Get("rows") ?? []).Cast<PyDict>().ToList();
            var referenceRows = TagPrint.ParseExiv2(TagPrint.RunReference([exiv2], path).Output);

            static string Value(List<PyDict> rows, string key)
            {
                foreach (var row in rows)
                {
                    // The dump records the bytes and the reference the text it prints for them.
                    if (Py.Eq(row["key"], key)) return (string)Py.Or(row.Get("raw"), row.Get("value", ""))!;
                }
                return "";
            }

            lines.Add($"camera local={Py.ReprString(Value(localRows, "Exif.Image.Make"))}/{Py.ReprString(Value(localRows, "Exif.Image.Model"))} "
                      + $"reference={Py.ReprString(Value(referenceRows, "Exif.Image.Make"))}/{Py.ReprString(Value(referenceRows, "Exif.Image.Model"))}");
            foreach (PyDict group in report.List("mismatch_groups").Take(limit).Cast<PyDict>())
            {
                object? example = Py.Truthy(group.Get("examples")) ? ((PyDict)group.List("examples")[0]!)["difference"] : "";
                lines.Add($"{Py.Str(group["category"])} {Py.Str(group["key"])} {Py.Str(group["type"])} x{Py.Str(group["occurrences"])} ({Py.Str(example)})");
            }
            foreach (PyDict item in report.List("value_diff_by_key").Take(12).Cast<PyDict>())
            {
                lines.Add($"value {Py.Str(item["key"])}: reference={Py.ReprString(PyText.Head(item.Str("exiv2"), 60))} "
                          + $"local={Py.ReprString(PyText.Head(item.Str("metadatasharp"), 60))}");
            }
            string detail = Py.Str(dumpedFile.Get("detail", ""));
            var refusals = report.List("refusals");
            if (detail.Length > 0 && refusals.Count == 0) lines.Add($"detail {detail}");
            foreach (PyDict refusal in refusals.Take(4).Cast<PyDict>())
            {
                string reason = path == candidate.Path ? Py.Str(dumpedFile.Get("detail", "")) : "";
                lines.Add($"refused {Py.Str(refusal.Get("file"))}: {Py.Str(refusal.Get("kind"))} {Py.Str(refusal.Get("detail"))} "
                          + reason.Replace('\t', ' '));
            }
            // What the file lists as previews, because a row the reference reads out of an embedded picture is
            // missing from the dump until the preview itself is found.
            string listing = PyProcess.Run(command.Where(part => part != "--refusal-detail").Concat(["--previews", path]).ToList(), strict: true).Stdout;
            lines.AddRange(PyText.SplitLines(listing).Where(line => line.StartsWith("PREVIEW", StringComparison.Ordinal)).Take(4));
            Out.Print($"::notice title=RAW corpus explain::{entry.Name}::" + string.Join("%0A", lines));
            explained++;
            if (explained == files) return 0;
        }
        Out.Print("::notice title=RAW corpus explain::" + (name.Length > 0 ? $"no corpus file matches {name}" : "no corpus file differs from the reference"));
        return 0;
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("raw-corpus", "Download and verify the pinned RAW corpus.")
            .Add("command", choices: ["download", "verify", "pin", "list", "explain"])
            .Add("destination", nargs: "?", help: "where the corpus is kept")
            .Add("--report", help: "write the run report here")
            .Add("--write", ArgAction.StoreTrue, help: "pin: record the measured values in the manifest")
            .Add("--exiv2", help: "explain: the pinned reference executable")
            .Add("--dump-command", help: "explain: how to run MetadataSharp.Dump")
            .Add("--name", help: "explain: only files whose name contains this")
            .Add("--files", isInt: true, @default: 4L, help: "explain: how many differing files to print");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        try
        {
            return Execute(parser, args);
        }
        catch (RawCorpusError failure)
        {
            Out.Error($"::error title=raw corpus harness::{failure.Message}");
            return 2;
        }
    }

    private static int Execute(ArgParser parser, ParsedArgs args)
    {
        var document = Load();
        string command = args.Str("command");
        if (command == "list")
        {
            foreach (var entry in Entries(document))
            {
                string state = Py.Truthy(entry.Sha256) ? "pinned" : "unpinned";
                Out.Print($"{PyText.LJust(entry.Format, 4)} {PyText.LJust(entry.Camera, 28)} {PyText.LJust(entry.Name, 36)} {state} {entry.Url}");
            }
            return 0;
        }
        string? destinationArgument = args.StrOrNull("destination");
        if (destinationArgument is null) return parser.Fail($"{command} needs a destination directory");
        string destination = PyPath.Normalize(destinationArgument);
        if (command == "download")
        {
            var outcomes = Download(destination, document);
            Annotate(outcomes);
            Out.Print(Describe(outcomes));
            if (args.StrOrNull("report") is { } reportArgument)
            {
                string report = PyPath.Normalize(reportArgument);
                PyFile.MakeDirs(PyPath.Parent(report));
                PyFile.WriteText(report, PyJson.Dumps(outcomes, indent: 2) + "\n");
            }
            var failed = outcomes.List("files").Cast<PyDict>().Where(item => item.Str("status") is "failed" or "mismatch").Select(item => item.Str("name")).ToList();
            if (failed.Count > 0)
            {
                Out.Print("::error title=raw corpus::could not fetch " + string.Join(", ", failed));
                return 1;
            }
            return 0;
        }
        if (command == "explain")
        {
            if (string.IsNullOrEmpty(args.StrOrNull("exiv2")) || string.IsNullOrEmpty(args.StrOrNull("dump_command")))
                return parser.Fail("explain needs --exiv2 and --dump-command");
            return Explain(destination, args.Str("exiv2"), args.Str("dump_command"), files: args.Int("files"), name: args.StrOrNull("name") ?? "");
        }
        if (command == "verify")
        {
            var problems = Verify(destination, document);
            foreach (string problem in problems) Out.Print($"::error title=raw corpus::{problem}");
            Out.Print($"raw corpus verified: {Entries(document).Count} files, {problems.Count} problems");
            return problems.Count > 0 ? 1 : 0;
        }
        var updated = Pin(destination, write: args.Flag("write"), document: document);
        Out.Print(PyJson.Dumps(updated["files"], indent: 2, sortKeys: true));
        return 0;
    }
}
