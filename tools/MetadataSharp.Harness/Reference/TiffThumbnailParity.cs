using MetadataSharp.Harness.Python;
using Ops = MetadataSharp.Harness.Reference.OperationsParity;

namespace MetadataSharp.Harness.Reference;

// The thumbnail of a TIFF-based file, held against the same operation of the pinned exiv2. Formerly
// tools/reference/tiff_thumbnail_parity.py.
//
// A separate check from key parity. On a copy of every TIFF-based file of the pinned corpus the tool sets a
// thumbnail with "exiv2 -i t in" and removes it with "exiv2 -d t rm"; this library sets it with
// ExifEdit.SetThumbnail and removes it with ExifEdit.RemoveThumbnail, the fixture generator (tiff-thumbnails) writing
// what each makes of the file. Both results are printed by the tool with "exiv2 -pa" and the print-outs are compared
// key by key, as operations-parity compares them; the thumbnail stream of each result is then extracted by the tool
// with "exiv2 -et" and the two streams are compared.
//
// A file this library refuses is no failure: it is one where the directory after the primary one is a picture of the
// file, or one the reader refuses. Nothing is written for it, which is checked (the file is absent), and every
// refusal is counted and printed with its message; a refusal of a code not listed in Allowed is a failure, as is one
// for a different reason in a file the tool handles. A file the tool fails on has nothing to compare and is counted.
//
// Usage: tiff-thumbnail-parity CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."
internal static class TiffThumbnailParity
{
    // The signatures the library reads as TIFF-based: TIFF itself, DNG, the raw formats built as one.
    public static readonly string[] Extensions = [".tif", ".tiff", ".dng", ".nef", ".nrw", ".cr2", ".arw", ".sr2", ".pef", ".orf", ".rw2"];

    // The operations, named as operations-parity names them, so that the differences it documents for them are the
    // differences this gate accepts: the tool's arguments before the file.
    public static readonly OrderedDictionary<string, string[]> Operations = new(StringComparer.Ordinal)
    {
        ["thumbnail"] = ["-i", "t", "in"],
        ["delete-t"] = ["-d", "t", "rm"],
    };

    // The codes this library refuses a thumbnail change with, each for a reason of its own: a directory that is a
    // picture of the file (UnsupportedFeature), one nothing may be appended to (UnsafeRelocation), one over a budget
    // (LimitExceeded), and a file the reader refuses.
    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "UnsupportedFeature", "UnsafeRelocation", "LimitExceeded", "InvalidTiff", "InvalidExif" };

    // A raw file of the corpus is larger than the buffer a print of a JPEG file needs: a camera raw of 60 MB is a file
    // this library and the tool both hold whole.
    public const long MaxBytes = 128 * 1024 * 1024;

    public static List<string> Sources(string corpus) =>
        PyPath.RGlob(corpus).Where(path => File.Exists(path) && Extensions.Contains(PyPath.Suffix(path).ToLowerInvariant()) && new FileInfo(path).Length <= MaxBytes).ToList();

    /// <summary>The thumbnail stream the tool extracts from a file, or null when it extracts none.</summary>
    public static byte[]? Stream(string exiv2, string path, Runner runner)
    {
        string extracted = path + "-thumb.jpg";
        if (PyPath.Exists(extracted)) File.Delete(extracted);
        runner([exiv2, "-et", path]);
        byte[]? found = PyPath.Exists(extracted) ? PyFile.ReadBytes(extracted) : null;
        if (PyPath.Exists(extracted)) File.Delete(extracted);
        return found;
    }

    private static string LastLine(string errors, int code)
    {
        var lines = PyText.SplitLines(PyText.Strip(errors));
        return lines.Count > 0 ? lines[^1] : $"exit {code}";
    }

    /// <summary>Runs the tool's operations on copies; (name, operation) -> its complaint when it failed.</summary>
    public static Dictionary<(string Name, string Operation), string> ToolSide(string work, IEnumerable<string> names, string exiv2, Runner runner)
    {
        work = PyPath.Normalize(work);
        var failed = new Dictionary<(string, string), string>();
        foreach (string name in names)
        {
            foreach (var (operation, arguments) in Operations)
            {
                string copy = PyPath.Join(work, $"{name}.{operation}.tool");
                PyShutil.CopyFile(PyPath.Join(work, name + ".src"), copy);
                // The tool reads the stream to insert from its own path with the last extension taken off and
                // "-thumb.jpg" added (app/actions.cpp, newFilePath).
                PyShutil.CopyFile(PyPath.Join(work, "thumb.jpg"), PyPath.Join(work, PyText.RSplit(PyPath.Name(copy), ".", 1)[0] + "-thumb.jpg"));
                var (code, _, errors) = runner([exiv2, .. arguments, copy]);
                if (code != 0) failed[(name, operation)] = LastLine(errors, code);
            }
        }
        return failed;
    }

    /// <summary>(name, operation) -> (status, text) from the ours-tiff.tsv the fixture generator wrote.</summary>
    public static Dictionary<(string Name, string Operation), (string Status, string Text)> Statuses(string work)
    {
        var found = new Dictionary<(string, string), (string, string)>();
        string path = PyPath.Join(work, "ours-tiff.tsv");
        if (PyPath.Exists(path))
        {
            foreach (string line in PyText.SplitLines(PyFile.ReadText(path)))
            {
                var fields = line.Split('\t');
                if (fields.Length >= 4) found[(fields[0], fields[1])] = (fields[2], fields[3]);
            }
        }
        return found;
    }

    /// <summary>Rows (operation, file, key, outcome, theirs, ours), the files compared per operation, the files left out
    /// per (operation, who, why) and the refusals per (operation, code, message).</summary>
    public static (List<OperationRow> Rows, Counter Files, Counter LeftOut, Counter Refusals) Check(string work, IEnumerable<string> names, string exiv2,
                                                                                                Dictionary<(string Name, string Operation), string> failed, Runner runner)
    {
        work = PyPath.Normalize(work);
        var rows = new List<OperationRow>();
        var files = new Counter();
        var leftOut = new Counter();
        var refusals = new Counter();
        var state = Statuses(work);
        foreach (string name in names)
        {
            var before = Ops.Printed(exiv2, PyPath.Join(work, name + ".src"), runner);
            foreach (string operation in Operations.Keys)
            {
                if (failed.TryGetValue((name, operation), out string? complaint))
                {
                    leftOut.Add(new PyTuple(operation, "tool", complaint));
                    continue;
                }
                var (status, text) = state.TryGetValue((name, operation), out var found) ? found : ("missing", "");
                if (status.StartsWith("refused", StringComparison.Ordinal))
                {
                    var parts = PyText.Split(status, " ", 1);
                    if (parts.Count < 2) throw new PyException("IndexError", "list index out of range");
                    refusals.Add(new PyTuple(operation, parts[1], text));
                    // A refusal writes nothing: the file the library was given is what it returns.
                    if (PyPath.Exists(PyPath.Join(work, $"{name}.{operation}.ours"))) rows.Add(new(operation, name, "the file", "written and refused", ["a file"], ["the source"]));
                    continue;
                }
                if (status is "missing" or "unchanged")
                {
                    leftOut.Add(new PyTuple(operation, "ours", status));
                    continue;
                }
                files.Add(operation);
                string theirFile = PyPath.Join(work, $"{name}.{operation}.tool"), ourFile = PyPath.Join(work, $"{name}.{operation}.ours");
                var theirs = Ops.Printed(exiv2, theirFile, runner);
                var ours = Ops.Printed(exiv2, ourFile, runner);
                if (theirs is null || ours is null)
                {
                    rows.Add(new(operation, name, "the file", "differs", [theirs is null ? "not printed" : "printed"], [ours is null ? "not printed" : "printed"]));
                    continue;
                }
                rows.AddRange(Ops.Compare(theirs, ours, before).Select(row => new OperationRow(operation, name, row.Key, row.Outcome, row.Theirs, row.Ours)));
                // The stream itself: the tool extracts the same bytes from both results, or none from both.
                byte[]? theirStream = Stream(exiv2, theirFile, runner), ourStream = Stream(exiv2, ourFile, runner);
                bool same = theirStream is null ? ourStream is null : ourStream is not null && theirStream.AsSpan().SequenceEqual(ourStream);
                rows.Add(new(operation, name, "the thumbnail stream", same ? "same" : "differs",
                             [theirStream is not null ? $"{theirStream.Length} bytes" : "none"], [ourStream is not null ? $"{ourStream.Length} bytes" : "none"]));
            }
        }
        return (rows, files, leftOut, refusals);
    }

    /// <summary>(operation, key, outcome) -> (count, first example) for every outcome that is not "same".</summary>
    public static OrderedDictionary<(string Operation, string Key, string Outcome), (long Count, string Example)> Summarise(IEnumerable<OperationRow> rows)
    {
        var table = new OrderedDictionary<(string, string, string), (long, string)>();
        foreach (var row in rows)
        {
            if (row.Outcome == "same") continue;
            var key = (row.Operation, Ops.GroupedKey(row.Operation, row.Key), row.Outcome);
            if (!table.TryGetValue(key, out var entry))
                entry = (0, $"{row.File} {row.Key}: tool {PyText.Head(Py.Repr(row.Theirs), 70)} ours {PyText.Head(Py.Repr(row.Ours), 70)}");
            table[key] = (entry.Item1 + 1, entry.Item2);
        }
        return table;
    }

    public static int Run(string[] argv)
    {
        try
        {
            return Execute(argv, ToolRunner.Run);
        }
        catch (Exception error) when (error is not SystemExit)
        {
            // The log of a job is not always at hand: the failure is said where the report is.
            var trace = PyText.SplitLines(PyException.TraceOf(error));
            ToolRunner.Annotate("error", "TIFF thumbnail parity - the comparer failed", trace.TakeLast(12));
            return 1;
        }
    }

    public static int Execute(IReadOnlyList<string> argv, Runner runner)
    {
        var parser = new ArgParser("tiff-thumbnail-parity", "The thumbnail of a TIFF-based file, held against the same operation of the pinned exiv2.")
            .Add("corpus")
            .Add("work")
            .Add("--exiv2", required: true)
            .Add("--fixture", required: true, help: "the fixture generator, to which the command and WORK are added");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string work = PyPath.Normalize(args.Str("work")), exiv2 = args.Str("exiv2");
        PyFile.MakeDirs(work);
        var fixture = Shlex.Split(args.Str("fixture"));

        var names = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Sources(args.Str("corpus")))
        {
            string name = $"t{names.Count:00000}";
            PyShutil.CopyFile(path, PyPath.Join(work, name + ".src"));
            names[name] = path;
            Ops.Names[name] = PyPath.Name(path);
        }
        if (names.Count == 0)
        {
            Out.Print("::error title=TIFF thumbnail parity::no TIFF-based file of the corpus was prepared");
            return 1;
        }

        var (code, output, errors) = runner([.. fixture, "operations-parity-blank", PyPath.Join(work, "thumb.jpg")], 600);
        if (code != 0)
        {
            Console.Out.Write(output + errors);
            Out.Print($"::error title=TIFF thumbnail parity::the fixture generator failed (exit {code})");
            return 1;
        }
        var failed = ToolSide(work, names.Keys, exiv2, runner);
        (code, output, errors) = runner([.. fixture, "tiff-thumbnails", work], 1500);
        Console.Out.Write(output);
        if (code != 0)
        {
            Console.Out.Write(errors);
            var tail = PyText.SplitLines(PyText.Strip(errors)).TakeLast(12).ToList();
            ToolRunner.Annotate("error", $"TIFF thumbnail parity - the fixture generator failed (exit {code})", tail.Count > 0 ? tail : ["no output"]);
            return 1;
        }

        var (rows, files, leftOut, refusals) = Check(work, names.Keys, exiv2, failed, runner);
        var totals = new Counter(rows.Select(row => (object)new PyTuple(row.Operation, row.Outcome)));
        var table = Summarise(rows);
        var unexplained = table.Where(pair => Ops.IsDocumented(pair.Key.Operation, pair.Key.Key, pair.Key.Outcome) is null).ToList();
        var explained = table.Where(pair => Ops.IsDocumented(pair.Key.Operation, pair.Key.Key, pair.Key.Outcome) is not null).ToList();
        unexplained = unexplained.OrderBy(pair => -pair.Value.Count).ToList();
        // A refusal the library has no reason of its own for is a defect, not a limit.
        var unknown = refusals.Items().Where(pair => !Allowed.Contains(((PyTuple)pair.Key).S(1))).ToList();

        ToolRunner.Annotate("error", "TIFF thumbnail parity - differences that are not documented",
                            unexplained.Select(pair => $"{pair.Key.Operation} {pair.Key.Key} {pair.Key.Outcome}: {pair.Value.Count}; {pair.Value.Example}"), size: 2500, limit: 8);
        ToolRunner.Annotate("error", "TIFF thumbnail parity - refusals of a code that is not a reason of its own",
                            unknown.Select(pair => $"{((PyTuple)pair.Key).S(0)} refused {((PyTuple)pair.Key).S(1)}: {pair.Value}; {((PyTuple)pair.Key).S(2)}"), size: 2500, limit: 4);
        var summary = new List<string>();
        foreach (string operation in Operations.Keys)
        {
            var kinds = totals.Keys.Cast<PyTuple>().Where(key => key.S(0) == operation && key.S(1) != "same").Select(key => key.S(1)).Distinct().ToList();
            kinds.Sort(Py.CompareStrings);
            string counts = string.Join(", ", kinds.Prepend("same").Select(kind => $"{kind} {totals.CountOf(new PyTuple(operation, kind))}"));
            long tool = leftOut.Items().Where(pair => ((PyTuple)pair.Key).S(0) == operation && ((PyTuple)pair.Key).S(1) == "tool").Sum(pair => pair.Value);
            long refused = refusals.Items().Where(pair => ((PyTuple)pair.Key).S(0) == operation).Sum(pair => pair.Value);
            summary.Add($"{operation}: {files.CountOf(operation)} files compared (tool failed on {tool}, refused here {refused}); {counts}");
        }
        ToolRunner.Annotate("notice", $"TIFF thumbnail parity - {names.Count} files", summary, limit: 2);
        ToolRunner.Annotate("notice", "TIFF thumbnail parity - refusals and files left out",
                            refusals.MostCommon().Select(pair => $"{((PyTuple)pair.Key).S(0)} refused {((PyTuple)pair.Key).S(1)}: {pair.Value}; {((PyTuple)pair.Key).S(2)}")
                                .Concat(leftOut.MostCommon().Select(pair => $"{((PyTuple)pair.Key).S(0)}, {((PyTuple)pair.Key).S(1)}: {pair.Value} file(s): {((PyTuple)pair.Key).S(2)}")).ToList(),
                            size: 3000, limit: 4);
        ToolRunner.Annotate("notice", "TIFF thumbnail parity - documented differences",
                            explained.Select(pair => $"{pair.Key.Operation} {pair.Key.Key} {pair.Key.Outcome}: {pair.Value.Count}; "
                                                     + $"{Ops.IsDocumented(pair.Key.Operation, pair.Key.Key, pair.Key.Outcome)}; {pair.Value.Example}"), limit: 3);
        foreach (var pair in unexplained) Out.Print($"FAIL {pair.Key.Operation} {pair.Key.Key} {pair.Key.Outcome}: {pair.Value.Count}; {pair.Value.Example}");
        foreach (var pair in unknown) Out.Print($"FAIL {((PyTuple)pair.Key).S(0)} refused {((PyTuple)pair.Key).S(1)}: {pair.Value}; {((PyTuple)pair.Key).S(2)}");
        return unexplained.Count > 0 || unknown.Count > 0 ? 1 : 0;
    }
}
