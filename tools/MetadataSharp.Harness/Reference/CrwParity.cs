using MetadataSharp.Harness.Python;
using Mk = MetadataSharp.Harness.Reference.MakerNoteParity;

namespace MetadataSharp.Harness.Reference;

// Edits of CRW files by this library, read back by the pinned exiv2. Formerly tools/reference/crw_parity.py.
//
//     crw-parity CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."
//
// Copies the CRW files of CORPUS to WORK, has the fixture generator (crw-parity) set tags and the comment of each,
// every change in a copy of its own, and prints the source and every copy with "exiv2 -u -PEkv" (and the comment with
// "exiv2 -pc"). A copy passes when
//
//   * the tool prints the key that was set with the value that was set (for the comment: the comment),
//   * every other key is printed with the lines the source has for it, and no key came or went,
//   * the tool exits as it does for the source and warns of nothing it does not warn of there.
//
// The rules are those of makernote-parity, whose functions are used. A refusal of the library is no failure: it is
// counted, by the operation and its code. The lines of "exiv2 --version --verbose" that say which formats the pinned
// build was made with are printed as a notice.
internal static class CrwParity
{
    public const long MaxBytes = 24 * 1024 * 1024;
    public static readonly string[] Build = ["exiv2=", "version=", "enable_video=", "enable_bmff=", "enable_brotli=", "enable_inih=", "have_inttypes="];

    public static string Comment(string exiv2, string path, Runner runner)
    {
        var (_, output, _) = runner([exiv2, "-pc", path]);
        return PyText.Strip(output, "\r\n\0 ");
    }

    private static string First(List<string> values) => Py.Repr(values.Take(1).ToList());

    /// <summary>As MakerNoteParity.Verdict, for the comment: it reads as set and no key changed.</summary>
    public static (string Verdict, string Detail) CommentVerdict(string exiv2, string source, PrintOut sourcePrint, string copy, string value, Runner runner)
    {
        if (Comment(exiv2, copy, runner) != value)
            return ("comment the tool does not print", $"wanted {Py.ReprString(value)}, printed {Py.ReprString(PyText.Head(Comment(exiv2, copy, runner), 60))}");
        var (before, codeBefore, warnedBefore) = sourcePrint;
        var (after, codeAfter, warnedAfter) = Mk.Printed(exiv2, copy, runner);
        foreach (var (key, values) in before)
        {
            if (!after.TryGetValue(key, out var now)) return ("another key gone", key);
            if (!now.SequenceEqual(values, StringComparer.Ordinal)) return ("another key changed", $"{key}: {First(values)} -> {First(now)}");
        }
        foreach (string key in after.Keys)
        {
            if (!before.ContainsKey(key)) return ("a key appeared", key);
        }
        if (codeAfter != codeBefore) return ("exit code changed", $"{codeBefore} -> {codeAfter}");
        var fresh = warnedAfter.Where(line => !warnedBefore.Contains(line)).ToList();
        return fresh.Count > 0 ? ("new warning", fresh[0]) : ("ok", "");
    }

    public static (Counter Counts, Dictionary<(string Operation, string Outcome), string> Examples) Check(string work, IReadOnlyDictionary<string, string> names, string exiv2,
                                                                                                       Runner runner)
    {
        work = PyPath.Normalize(work);
        var counts = new Counter();
        var examples = new Dictionary<(string, string), string>();
        var prints = new Dictionary<string, PrintOut>(StringComparer.Ordinal);
        foreach (var row in Mk.RowsOf(work))
        {
            string name = row[0], number = row[1], operation = row[2], key = row[3], status = row[4], value = row[5];
            if (operation == "none")
            {
                counts.Add(new PyTuple("none", status));
                continue;
            }
            string outcome, detail;
            if (status != "written")
            {
                (outcome, detail) = (status, $"{key}: {value}");
            }
            else
            {
                string source = PyPath.Join(work, $"{name}.crw");
                if (!prints.ContainsKey(name)) prints[name] = Mk.Printed(exiv2, source, runner);
                string copy = PyPath.Join(work, $"{name}.{number}.ours.crw");
                (outcome, detail) = operation == "comment"
                    ? CommentVerdict(exiv2, source, prints[name], copy, value, runner)
                    : Mk.Verdict(prints[name], Mk.Printed(exiv2, copy, runner), key, value);
                if (File.Exists(copy)) File.Delete(copy);
            }
            counts.Add(new PyTuple(operation, outcome));
            if (!examples.ContainsKey((operation, outcome)))
            {
                if (!names.TryGetValue(name, out string? path)) throw new PyException("KeyError", Py.ReprString(name));
                examples[(operation, outcome)] = $"{PyPath.Name(path)}: {detail}";
            }
        }
        return (counts, examples);
    }

    public static int Run(string[] argv) => Execute(argv, ToolRunner.Run);

    public static int Execute(IReadOnlyList<string> argv, Runner runner)
    {
        var parser = new ArgParser("crw-parity", "Edits of CRW files by this library, read back by the pinned exiv2.")
            .Add("corpus")
            .Add("work")
            .Add("--exiv2", required: true)
            .Add("--fixture", required: true, help: "the fixture generator, to which the command and WORK are added");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string work = PyPath.Normalize(args.Str("work")), exiv2 = args.Str("exiv2");
        PyFile.MakeDirs(work);

        var (_, verbose, _) = runner([exiv2, "--version", "--verbose"]);
        ToolRunner.Annotate("notice", "CRW parity - the pinned build (exiv2 --version --verbose)",
                            PyText.SplitLines(verbose).Where(line => Build.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal))).ToList());

        var names = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in PyPath.RGlob(args.Str("corpus"), "*.crw").Where(path => File.Exists(path) && new FileInfo(path).Length <= MaxBytes))
        {
            string name = $"s{names.Count:00000}";
            PyShutil.CopyFile(path, PyPath.Join(work, $"{name}.crw"));
            names[name] = path;
        }
        if (names.Count == 0)
        {
            Out.Print("::error title=CRW parity::no CRW file of the corpus was prepared");
            return 1;
        }
        var (code, output, errors) = runner([.. Shlex.Split(args.Str("fixture")), "crw-parity", work], 900);
        Console.Out.Write(output);
        if (code != 0)
        {
            Console.Out.Write(errors);
            var tail = PyText.SplitLines(PyText.Strip(errors)).TakeLast(12).ToList();
            if (tail.Count == 0) tail = PyText.SplitLines(PyText.Strip(output)).TakeLast(12).ToList();
            ToolRunner.Annotate("error", $"CRW parity - the fixture generator failed (exit {code})", tail.Count > 0 ? tail : ["no output"]);
            return 1;
        }

        var (counts, examples) = Check(work, names, exiv2, runner);
        var failures = new List<string>();
        var summary = new List<string>();
        foreach (var (key, count) in Py.Sorted(counts.Items(), pair => (object?)new PyTuple(pair.Key, pair.Value)))
        {
            string operation = ((PyTuple)key).S(0), outcome = ((PyTuple)key).S(1);
            string line = $"{operation}: {outcome} {count}";
            if (operation == "none" || outcome == "ok") summary.Add(line);
            else if (outcome.StartsWith("refused ", StringComparison.Ordinal)) summary.Add($"{line}; e.g. {examples[(operation, outcome)]}");
            else failures.Add($"{line}; e.g. {examples[(operation, outcome)]}");
        }
        long passed = counts.Items().Where(pair => ((PyTuple)pair.Key).S(1) == "ok").Sum(pair => pair.Value);
        ToolRunner.Annotate("error", "CRW parity - edits the tool does not read back as set", failures, size: 2500, limit: 8);
        ToolRunner.Annotate("notice", $"CRW parity - {names.Count} files; {passed} edits read back by the tool", summary);
        if (passed == 0)
        {
            Out.Print("::error title=CRW parity::no edit was written and read back");
            return 1;
        }
        return failures.Count > 0 ? 1 : 0;
    }
}
