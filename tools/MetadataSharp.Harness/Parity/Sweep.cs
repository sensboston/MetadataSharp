using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// The run cannot be trusted; nothing should be concluded from its numbers.
internal sealed class SweepError(string message) : Exception(message);

// Soundness sweep: MetadataSharp.Dump --icc and --comments over every file of the pinned corpus.
//
// Nothing is compared with a reference here. The corpus holds files damaged on purpose, and the two static
// readers take any bytes: each has to end every file with a profile, a list of comments, nothing, or a
// refusal that carries a code. A reader that fails any other way (status "error") is a defect and fails the
// run. The counts travel as annotations so that a change in what is read or refused shows from one run to
// the next.
//
// (Formerly tools/parity/run_sweep.py; the arguments, reports and annotations are unchanged.)
internal static class Sweep
{
    public const string Title = "SWEEP icc and comments";

    // The option of the dump tool, and the one kind of record it writes between FILE and STATUS.
    public static readonly (string Mode, string Option, string Record)[] Modes = [("icc", "--icc", "ICC"), ("comments", "--comments", "COMMENT")];

    // One FILE block: how many records, the status and its detail.
    public sealed class Block
    {
        public long Records { get; set; }

        public string? Status { get; set; }

        public string Detail { get; set; } = "";
    }

    /// <summary>FILE / record / STATUS blocks by the path the tool echoes.</summary>
    /// <summary>
    /// The name a file travels under, to the dump tool and back: its path with forward slashes. The dump tool
    /// echoes the path it was given and escapes a backslash, so the path of a Windows file written the native
    /// way would come back as another string.
    /// </summary>
    public static string Key(string path) => PyPath.Normalize(path);

    public static Dictionary<string, Block> ParseDump(string text, string record)
    {
        var blocks = new Dictionary<string, Block>(StringComparer.Ordinal);
        Block? current = null;
        int number = 0;
        foreach (string line in PyText.SplitLines(text))
        {
            number++;
            if (line.Length == 0) continue;
            string[] fields = line.Split('\t');
            if (fields[0] == "FILE" && fields.Length == 2)
            {
                if (current is not null && current.Status is null) throw new SweepError($"dump:{number}: FILE record before the previous one was closed");
                current = new Block();
                blocks[fields[1]] = current;
            }
            else if (fields[0] == record && fields.Length == 3 && current is not null && current.Status is null)
            {
                current.Records++;
            }
            else if (fields[0] == "STATUS" && fields.Length >= 2 && current is not null && current.Status is null)
            {
                current.Status = fields[1];
                current.Detail = fields.Length > 2 ? fields[2] : "";
            }
            else
            {
                throw new SweepError($"dump:{number}: unexpected record: {Py.ReprString(line)}");
            }
        }
        if (current is not null && current.Status is null) throw new SweepError("the dump ends inside a FILE block");
        return blocks;
    }

    /// <summary>Statuses, refusal codes, files with a record and records, over paths; the files that failed.</summary>
    public static PyDict Tally(Dictionary<string, Block> blocks, List<string> paths)
    {
        var statuses = new Counter();
        var refusals = new Counter();
        long withRecords = 0, records = 0;
        var errors = new List<object?>();
        foreach (string path in paths)
        {
            if (!blocks.TryGetValue(Key(path), out var block)) throw new SweepError($"the dump has no block for {PyPath.Name(path)}");
            statuses.Add(block.Status!, 1);
            if (block.Status == "refused") refusals.Add(block.Detail, 1);
            else if (block.Status == "error") errors.Add(PyDict.Of(("file", PyPath.Name(path)), ("detail", block.Detail)));
            withRecords += block.Records != 0 ? 1 : 0;
            records += block.Records;
        }
        return PyDict.Of(("files", (long)paths.Count), ("statuses", SortedDict(statuses)), ("refused", SortedDict(refusals)),
                         ("files_with_records", withRecords), ("records", records), ("errors", errors));
    }

    private static PyDict SortedDict(Counter counter) => new(Py.Sorted(counter, pair => pair.Key));

    public static string TextReport(PyDict report)
    {
        var lines = new List<string> { "Soundness sweep over the corpus" };
        foreach (object mode in Py.Sorted(report.Keys))
        {
            var part = report.Dict(mode);
            lines.Add($"  {mode}: {Py.Str(part["files"])} files; statuses {Py.Str(part["statuses"])}; refused {Py.Str(part["refused"])}; "
                      + $"{Py.Str(part["records"])} records in {Py.Str(part["files_with_records"])} files");
            lines.AddRange(part.List("errors").Cast<PyDict>().Select(entry => $"    ERROR {entry["file"]}: {entry["detail"]}"));
        }
        return string.Join("\n", lines) + "\n";
    }

    public static Dictionary<string, Block> RunDump(IReadOnlyList<string> dumpCommand, string option, string record, List<string> paths)
    {
        string listing = string.Concat(paths.Select(path => $"{Key(path)}\n"));
        Completed finished;
        try
        {
            finished = PyProcess.Run(dumpCommand.Concat([option, "--files-from", "-"]).ToList(), input: listing, timeout: 1800L);
        }
        catch (PyOSError error) when (error.Errno == 2)
        {
            throw new SweepError($"the dump command is not runnable: {error.Message}");
        }
        catch (PyTimeoutExpired)
        {
            throw new SweepError("the dump tool timed out");
        }
        if (finished.ReturnCode != 0)
            throw new SweepError($"the dump tool failed with {finished.ReturnCode}: {PyText.Head(PyText.Strip(finished.Stderr), 400)}");
        return ParseDump(finished.Stdout, record);
    }

    /// <summary>Every regular file directly in test/data: no name is trusted to say what a file is.</summary>
    public static List<string> CorpusFiles(string tree)
    {
        string data = PyPath.Join(tree, "test", "data");
        if (!Directory.Exists(data)) throw new SweepError($"no corpus at {data}");
        // A tab or a line end in a name could not travel through the list the dump tool reads.
        var paths = PyPath.IterDir(data)
            .Where(path => File.Exists(path) && !PyPath.Name(path).Any(c => c is '\t' or '\r' or '\n' or '\\'))
            .ToList();
        paths.Sort(PyPath.Comparer);
        if (paths.Count == 0) throw new SweepError($"{data} holds no file");
        return paths;
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("sweep", "Run the ICC and comment readers over every corpus file.")
            .Add("--tree", required: true, help: "root of a pinned exiv2 checkout")
            .Add("--dump-command", required: true, help: "how to run MetadataSharp.Dump, split POSIX-style on spaces")
            .Add("--out", @default: Repo.Path("artifacts/parity/sweep"), help: "report directory")
            .Add("--annotate", ArgAction.StoreTrue, help: "emit the counts as chunked workflow annotations")
            .Add("--title", @default: Title, help: "annotation title");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        try
        {
            var paths = CorpusFiles(args.Str("tree"));
            var command = Shlex.Split(args.Str("dump_command"));
            var report = new PyDict();
            foreach (var (mode, option, record) in Modes) report[mode] = Tally(RunDump(command, option, record, paths), paths);

            string outDirectory = args.Str("out");
            Directory.CreateDirectory(outDirectory);
            PyFile.WriteText(PyPath.Join(outDirectory, "sweep-report.json"), PyJson.Dumps(report, indent: 1, sortKeys: true) + "\n");
            string text = TextReport(report);
            PyFile.WriteText(PyPath.Join(outDirectory, "sweep-report.txt"), text);
            Console.Out.Write(text);
            if (args.Flag("annotate")) ParitySupport.EmitAnnotations(report, args.Str("title"));
            return report.Values.Cast<PyDict>().Any(part => Py.Truthy(part["errors"])) ? 1 : 0;
        }
        catch (Exception failure) when (failure is SweepError or StructuralError)
        {
            Out.Error($"::error title={Title} harness::{failure.Message}");
            Out.Error($"structural failure: {failure.Message}");
            return 1;
        }
    }
}
