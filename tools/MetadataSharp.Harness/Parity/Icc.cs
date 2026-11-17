using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// The run cannot be trusted; nothing should be concluded from its numbers.
internal sealed class IccParityError(string message) : Exception(message);

// ICC parity: the profile MetadataSharp.Dump --icc reports against what exiv2 -pC printed.
//
// exiv2 -pC writes the bytes of the profile, so the pinned tree does not keep that output as text; its
// own tests keep what they compare it with. Three of them say something about a file that no command
// has yet changed, and each is read here as text. No external code is configured, built or run: the two
// Python files are parsed, never imported (Python/PyAst.cs).
//
// tests/bash_tests/testcases.py and test/data/test_reference_files/icc-test.out
//     icc_test loops over a list of images and, for each, ends by printing six MD5 sums on six lines.
//     The first of the six is of exiv2 -pC on the image as it is in the tree; the other five are of
//     files the test has rewritten, and say nothing of a reader.
//
// tests/bugfixes/redmine/test_issue_1074.py
//     One exiv2 -pC per file name, and the MD5 of what each must print.
//
// tests/bugfixes/github/test_pr1475_*.py
//     One class per BMFF file; among its commands exiv2 -pC with or without --binary, and at the same
//     place in stdout what it must print: nothing, or "data:" and the profile in base64.
//
// A file is judged against the one expectation the tests agree on:
//
//     match    the same profile (the same MD5, and the same length where the test gives one), or no
//              profile on either side
//     differs  a profile on both sides, and not the same one
//     missing  the reference prints a profile and we return none
//     extra    we return a profile where the reference prints nothing
//
// Unlike the key and preview comparisons, this one is a gate: every file must match.
//
// (Formerly tools/parity/run_icc.py; the arguments, reports and annotations are unchanged.)
internal static class Icc
{
    public const string Kind = "Icc";
    public const string Title = "PARITY icc";
    public static readonly string BashTest = PyPath.Join("tests", "bash_tests", "testcases.py");
    public static readonly string BashOutput = PyPath.Join("test", "data", "test_reference_files", "icc-test.out");
    public static readonly string Md5Case = PyPath.Join("tests", "bugfixes", "redmine", "test_issue_1074.py");
    public static readonly string BinaryCases = PyPath.Join("tests", "bugfixes", "github");
    public const string BinaryPattern = "test_pr1475_*.py";
    public const int SumsPerImage = 6;

    private static readonly Regex Md5 = new("^[0-9a-f]{32}\\z", RegexOptions.CultureInvariant);

    // What the reference prints for one file: null for nothing, otherwise the MD5 of the profile and,
    // where the test holds the bytes themselves, their number.
    public sealed record Expectation(string Md5, long? Bytes);

    private static PyAst.Module Tree(string text, string name)
    {
        try
        {
            return PyAst.Parse(text);
        }
        catch (PySyntaxError error)
        {
            throw new IccParityError($"{name} does not parse as Python: {error.Message}");
        }
    }

    /// <summary>The elements of a list or tuple written out as string literals, or null when it is not one.</summary>
    private static List<string>? Strings(PyAst.Expr? node)
    {
        if (node is not PyAst.Sequence sequence) return null;
        var values = sequence.Elements.OfType<PyAst.Constant>().Where(element => element.Value is string).Select(element => (string)element.Value!).ToList();
        return values.Count == sequence.Elements.Count ? values : null;
    }

    /// <summary>Every value assigned to name anywhere inside scope, in source order.</summary>
    private static List<PyAst.Expr> Assigned(PyAst.Node scope, string name) =>
        PyAst.Walk(scope).OfType<PyAst.Assign>().Where(node => node.Target == name).Select(node => node.Value).ToList();

    /// <summary>The images icc_test loops over, in its order.</summary>
    public static List<string> BashImages(string testText)
    {
        foreach (var function in PyAst.Walk(Tree(testText, BashTest)).OfType<PyAst.FunctionDef>())
        {
            if (function.Name != "icc_test") continue;
            foreach (var loop in PyAst.Walk(function).OfType<PyAst.For>())
            {
                if (loop.Target is PyAst.NameExpr { Id: "img" })
                {
                    var images = Strings(loop.Iter);
                    if (images is { Count: > 0 }) return images;
                }
            }
            throw new IccParityError("icc_test has no loop over a written-out list of images");
        }
        throw new IccParityError($"{BashTest} has no icc_test");
    }

    /// <summary>The runs of consecutive lines of icc-test.out that are an MD5 sum and nothing else.</summary>
    public static List<List<string>> BashSums(string outputText)
    {
        var runs = new List<List<string>>();
        bool inside = false;
        foreach (string line in PyText.SplitLines(outputText))
        {
            if (Md5.IsMatch(line))
            {
                if (!inside) runs.Add([]);
                runs[^1].Add(line);
                inside = true;
            }
            else
            {
                inside = false;
            }
        }
        return runs;
    }

    /// <summary>What icc_test expects exiv2 -pC to print for each image before it changes it.</summary>
    public static OrderedDictionary<string, Expectation?> ParseBash(string testText, string outputText)
    {
        var images = BashImages(testText);
        var runs = BashSums(outputText);
        if (runs.Count != images.Count) throw new IccParityError($"icc_test names {images.Count} images and its output has {runs.Count} runs of sums");
        var expected = new OrderedDictionary<string, Expectation?>(StringComparer.Ordinal);
        for (int i = 0; i < images.Count; i++)
        {
            string image = images[i];
            var run = runs[i];
            if (run.Count != SumsPerImage) throw new IccParityError($"the sums of {image} are {run.Count}, not {SumsPerImage}");
            if (expected.ContainsKey(image)) throw new IccParityError($"icc_test names {Py.ReprString(image)} twice");
            expected[image] = new Expectation(run[0], null);
        }
        return expected;
    }

    /// <summary>test_issue_1074.py: a tuple of file names, one -pC each, and the MD5 of each output.</summary>
    public static OrderedDictionary<string, Expectation?> ParseMd5Case(string text)
    {
        var expected = new OrderedDictionary<string, Expectation?>(StringComparer.Ordinal);
        foreach (var @case in PyAst.Walk(Tree(text, Md5Case)).OfType<PyAst.ClassDef>())
        {
            List<string>? names = null;
            foreach (var value in Assigned(@case, "filenames"))
                if (value is PyAst.ListComp { Iterables.Count: 1 } comprehension) names = Strings(comprehension.Iterables[0]);
            var commands = Assigned(@case, "commands").Select(PyAst.Unparse).ToList();
            var sums = Assigned(@case, "stdout").Select(Strings).Where(found => found is { Count: > 0 }).Select(found => found!).ToList();
            var number = Assigned(@case, "num").OfType<PyAst.Constant>().Select(value => value.Value).ToList();
            if (names is not { Count: > 0 } || sums.Count != 1 || commands.Count != 1 || !commands[0].Contains("-pC", StringComparison.Ordinal))
                throw new IccParityError($"{Md5Case}: {@case.Name} is not a list of files, one -pC each, and their sums");
            if (sums[0].Count != names.Count || !sums[0].All(value => Md5.IsMatch(value)))
                throw new IccParityError($"{Md5Case}: {@case.Name} does not give one MD5 per file");
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                string digest = sums[0][i];
                if (name.Contains("$num", StringComparison.Ordinal))
                {
                    if (number.Count != 1) throw new IccParityError($"{Md5Case}: {Py.ReprString(name)} needs a number the class does not give");
                    name = name.Replace("$num", Py.Str(number[0]), StringComparison.Ordinal);
                }
                if (name.Contains('$') || expected.ContainsKey(name)) throw new IccParityError($"{Md5Case}: the file name {Py.ReprString(name)} is not usable");
                expected[name] = new Expectation(digest, null);
            }
        }
        if (expected.Count == 0) throw new IccParityError($"{Md5Case} has no test class");
        return expected;
    }

    /// <summary>A test_pr1475_*.py file: per class one file, its commands, and the output of each.</summary>
    public static OrderedDictionary<string, Expectation?> ParseBinaryCase(string text, string source)
    {
        var expected = new OrderedDictionary<string, Expectation?>(StringComparer.Ordinal);
        foreach (var @case in PyAst.Walk(Tree(text, source)).OfType<PyAst.ClassDef>())
        {
            var names = Assigned(@case, "filename").OfType<PyAst.Constant>().Where(value => value.Value is string).Select(value => (string)value.Value!).ToList();
            // A class assigns both lists twice: empty where the build has no BMFF support, then in full.
            var commands = Assigned(@case, "commands").Select(Strings).Where(found => found is { Count: > 0 }).Select(found => found!).ToList();
            var outputs = Assigned(@case, "stdout").Select(Strings).Where(found => found is { Count: > 0 }).Select(found => found!).ToList();
            if (names.Count != 1 || commands.Count != 1 || outputs.Count != 1 || commands[0].Count != outputs[0].Count)
                throw new IccParityError($"{source}: {@case.Name} is not one file with as many outputs as commands");
            string name = PyText.RSplit(names[0], "/", 1)[^1];
            var printed = commands[0].Zip(outputs[0]).Where(pair => PyText.Split(pair.First).Contains("-pC")).Select(pair => pair.Second).ToList();
            if (printed.Count != 1) throw new IccParityError($"{source}: {@case.Name} prints the profile {printed.Count} times");
            if (name.Contains('$') || expected.ContainsKey(name)) throw new IccParityError($"{source}: the file name {Py.ReprString(name)} is not usable");
            if (printed[0] == "")
            {
                expected[name] = null;
            }
            else if (printed[0].StartsWith("data:", StringComparison.Ordinal))
            {
                byte[] profile;
                try
                {
                    profile = PyBase64.Decode(string.Concat(PyText.Split(printed[0][5..])), validate: true);
                }
                catch (Exception error) when (error is BinasciiError or ArgumentException)
                {
                    throw new IccParityError($"{source}: the profile of {name} is not base64: {error.Message}");
                }
                if (profile.Length == 0) throw new IccParityError($"{source}: the profile of {name} is empty");
                expected[name] = new Expectation(Convert.ToHexStringLower(MD5.HashData(profile)), profile.Length);
            }
            else
            {
                throw new IccParityError($"{source}: the output of -pC for {name} is neither empty nor a data: line");
            }
        }
        if (expected.Count == 0) throw new IccParityError($"{source} has no test class");
        return expected;
    }

    /// <summary>One expectation per file, and the tests that give it. Two tests that disagree are damage.</summary>
    public static (OrderedDictionary<string, Expectation?> Expected, Dictionary<string, List<string>> Origin) Merge(
        Dictionary<string, OrderedDictionary<string, Expectation?>> sources)
    {
        var expected = new OrderedDictionary<string, Expectation?>(StringComparer.Ordinal);
        var origin = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string source in Py.Sorted(sources.Keys))
        {
            foreach (var (name, value) in sources[source])
            {
                if (expected.TryGetValue(name, out var known))
                {
                    bool same = (known is null && value is null) || (known is not null && value is not null && known.Md5 == value.Md5);
                    if (!same) throw new IccParityError($"the tests disagree about the profile of {name}");
                    // The test that holds the bytes also knows their number.
                    if (known is not null && value is not null && known.Bytes is null) expected[name] = value;
                }
                else
                {
                    expected[name] = value;
                }
                if (!origin.TryGetValue(name, out var tests)) origin[name] = tests = [];
                tests.Add(source);
            }
        }
        return (expected, origin);
    }

    // One FILE block of the dump: the profile it reports (MD5 and length), its status and detail.
    public sealed class DumpBlock
    {
        public (string Md5, long Bytes)? Profile { get; set; }

        public string? Status { get; set; }

        public string Detail { get; set; } = "";
    }

    /// <summary>
    /// The name a file travels under, to the dump tool and back: its path with forward slashes. The dump tool
    /// echoes the path it was given and escapes a backslash, so the path of a Windows file written the native
    /// way would come back as another string.
    /// </summary>
    public static string Key(string path) => PyPath.Normalize(path);

    /// <summary>FILE / ICC / STATUS records of MetadataSharp.Dump --icc, by the path the tool echoes.</summary>
    public static Dictionary<string, DumpBlock> ParseDump(string text)
    {
        var blocks = new Dictionary<string, DumpBlock>(StringComparer.Ordinal);
        DumpBlock? current = null;
        int number = 0;
        foreach (string line in PyText.SplitLines(text))
        {
            number++;
            if (line.Length == 0) continue;
            string[] fields = line.Split('\t');
            if (fields[0] == "FILE" && fields.Length == 2)
            {
                if (current is not null && current.Status is null)
                    throw new IccParityError($"dump:{number}: FILE record before the previous one was closed");
                current = new DumpBlock();
                blocks[fields[1]] = current;
            }
            else if (fields[0] == "ICC" && fields.Length == 3 && current is not null && current.Status is null)
            {
                if (current.Profile is not null || !PyText.IsDigit(fields[1]) || !Md5.IsMatch(fields[2]))
                    throw new IccParityError($"dump:{number}: ICC record does not parse or is the second of its file: {Py.ReprString(line)}");
                current.Profile = (fields[2], PyText.ParseInt(fields[1]));
            }
            else if (fields[0] == "STATUS" && fields.Length >= 2 && current is not null && current.Status is null)
            {
                current.Status = fields[1];
                current.Detail = fields.Length > 2 ? fields[2] : "";
            }
            else
            {
                throw new IccParityError($"dump:{number}: unexpected record: {Py.ReprString(line)}");
            }
        }
        if (current is not null && current.Status is null) throw new IccParityError("the dump ends inside a FILE block");
        return blocks;
    }

    /// <summary>match, differs, missing or extra for one file that was read.</summary>
    public static string Judge(Expectation? expected, (string Md5, long Bytes)? found)
    {
        if (expected is null) return found is null ? "match" : "extra";
        if (found is null) return "missing";
        bool same = expected.Md5 == found.Value.Md5 && (expected.Bytes is null || expected.Bytes == found.Value.Bytes);
        return same ? "match" : "differs";
    }

    public static PyDict Compare(OrderedDictionary<string, Expectation?> expected, Dictionary<string, List<string>> origin,
                                 Dictionary<string, DumpBlock> dumped, IReadOnlyDictionary<string, string?> paths)
    {
        var totals = new Counter();
        var statuses = new Counter();
        var files = new List<object?>();
        foreach (string name in Py.Sorted(expected.Keys))
        {
            string? path = paths.TryGetValue(name, out string? value) ? value : null;
            DumpBlock? block = path is not null && dumped.TryGetValue(Key(path), out var dumpedBlock) ? dumpedBlock : null;
            string status;
            (string Md5, long Bytes)? found;
            if (path is null)
            {
                (status, found) = ("no-input", null);
            }
            else if (block is null)
            {
                throw new IccParityError($"the dump has no block for {name}");
            }
            else
            {
                (status, found) = (block.Status!, block.Profile);
            }
            // A file that was not read has not been compared: it is its status, not a missing profile.
            string outcome = status == "ok" ? Judge(expected[name], found) : status;
            statuses.Add(status, 1);
            totals.Add("files", 1);
            totals.Add("profiles", expected[name] is not null ? 1 : 0);
            totals.Add(outcome, 1);
            var entry = PyDict.Of(("file", name), ("status", status), ("outcome", outcome),
                                  ("tests", (origin.TryGetValue(name, out var tests) ? tests : []).Cast<object?>().ToList()));
            if (expected[name] is { } expectation) entry["expected"] = PyDict.Of(("md5", expectation.Md5), ("bytes", expectation.Bytes));
            if (found is { } profile) entry["found"] = PyDict.Of(("md5", profile.Md5), ("bytes", profile.Bytes));
            if (status != "ok" && block is not null) entry["detail"] = block.Detail;
            files.Add(entry);
        }
        object? rate = totals.CountOf("files") != 0 ? Py.Round((double)totals.CountOf("match") / totals.CountOf("files"), 6) : null;
        return PyDict.Of(("kind", Kind), ("totals", totals.Copy()), ("rate", rate), ("statuses", statuses.Copy()), ("files", files));
    }

    public static string TextReport(PyDict report)
    {
        var totals = report.Dict("totals");
        var lines = new List<string>
        {
            $"{Kind} parity against exiv2 -pC",
            $"  files: {Py.Str(totals.Get("files", 0L))}, of which {Py.Str(totals.Get("profiles", 0L))} with a profile in the reference; "
            + $"statuses: {Py.Str(report["statuses"])}",
            $"  {Py.Str(totals.Get("match", 0L))} match, {Py.Str(totals.Get("differs", 0L))} differ, {Py.Str(totals.Get("missing", 0L))} missing, "
            + $"{Py.Str(totals.Get("extra", 0L))} extra; rate {Py.Str(report["rate"])}",
        };
        foreach (PyDict entry in report.List("files").Cast<PyDict>())
        {
            if (entry.Str("outcome") == "match") continue;
            lines.Add(PyText.RStrip($"  {entry["file"]} [{entry["status"]}] {entry["outcome"]} expected={Py.Str(entry.Get("expected"))} "
                                    + $"found={Py.Str(entry.Get("found"))} {Py.Str(entry.Get("detail", ""))}"));
        }
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>The part of the report that travels as annotations: the totals and every file that does not match.</summary>
    public static PyDict Summary(PyDict report) =>
        PyDict.Of(("kind", Kind), ("totals", report["totals"]), ("rate", report["rate"]), ("statuses", report["statuses"]),
                  ("differing", report.List("files").Cast<PyDict>().Where(entry => entry.Str("outcome") != "match").Cast<object?>().ToList()));

    public static Dictionary<string, DumpBlock> RunDump(IReadOnlyList<string> dumpCommand, IEnumerable<string> paths)
    {
        string listing = string.Concat(paths.Select(path => $"{Key(path)}\n"));
        Completed finished;
        try
        {
            finished = PyProcess.Run(dumpCommand.Concat(["--icc", "--files-from", "-"]).ToList(), input: listing, timeout: 1800L);
        }
        catch (PyOSError error) when (error.Errno == 2)
        {
            throw new IccParityError($"the dump command is not runnable: {error.Message}");
        }
        catch (PyTimeoutExpired)
        {
            throw new IccParityError("the dump tool timed out");
        }
        if (finished.ReturnCode != 0)
            throw new IccParityError($"the dump tool failed with {finished.ReturnCode}: {PyText.Head(PyText.Strip(finished.Stderr), 400)}");
        return ParseDump(finished.Stdout);
    }

    public static (OrderedDictionary<string, Expectation?> Expected, Dictionary<string, List<string>> Origin) ReadExpectations(string tree)
    {
        string Text(string relative)
        {
            string path = PyPath.Join(tree, relative);
            if (!File.Exists(path)) throw new IccParityError($"no expectation at {path}");
            return PyFile.ReadText(path, replace: true);
        }

        var sources = new Dictionary<string, OrderedDictionary<string, Expectation?>>(StringComparer.Ordinal)
        {
            ["icc_test"] = ParseBash(Text(BashTest), Text(BashOutput)),
        };
        sources[PyPath.Stem(Md5Case)] = ParseMd5Case(Text(Md5Case));
        var cases = PyPath.Glob(PyPath.Join(tree, BinaryCases), BinaryPattern);
        if (cases.Count == 0) throw new IccParityError($"no {BinaryPattern} under {PyPath.Join(tree, BinaryCases)}");
        foreach (string @case in cases) sources[PyPath.Stem(@case)] = ParseBinaryCase(PyFile.ReadText(@case, replace: true), PyPath.Name(@case));
        return Merge(sources);
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("icc", "Compare the ICC profile we read with exiv2's own expectations.")
            .Add("--tree", required: true, help: "root of a pinned exiv2 checkout")
            .Add("--dump-command", required: true, help: "how to run MetadataSharp.Dump, split POSIX-style on spaces")
            .Add("--out", @default: Repo.Path("artifacts/parity/icc"), help: "report directory")
            .Add("--annotate", ArgAction.StoreTrue, help: "emit the summary as chunked workflow annotations")
            .Add("--title", @default: Title, help: "annotation title");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        try
        {
            string tree = args.Str("tree");
            var (expected, origin) = ReadExpectations(tree);
            string data = PyPath.Join(tree, "test", "data");
            var paths = new OrderedDictionary<string, string?>(StringComparer.Ordinal);
            foreach (string name in expected.Keys) paths[name] = File.Exists(PyPath.Join(data, name)) ? PyPath.Join(data, name) : null;
            var present = paths.Values.Where(path => path is not null).Cast<string>().ToList();
            if (present.Count == 0) throw new IccParityError("none of the files the expectations name is in the tree");
            var report = Compare(expected, origin, RunDump(Shlex.Split(args.Str("dump_command")), present), paths);

            string outDirectory = args.Str("out");
            Directory.CreateDirectory(outDirectory);
            PyFile.WriteText(PyPath.Join(outDirectory, "icc-report.json"), PyJson.Dumps(report, indent: 1, sortKeys: true) + "\n");
            string text = TextReport(report);
            PyFile.WriteText(PyPath.Join(outDirectory, "icc-report.txt"), text);
            Console.Out.Write(text);
            if (args.Flag("annotate")) ParitySupport.EmitAnnotations(Summary(report), args.Str("title"));
            // The profile is a run of bytes the file holds: there is no reading of it to disagree about.
            var totals = report.Dict("totals");
            return Py.Eq(totals.Get("match", 0L), totals.Get("files", 0L)) ? 0 : 1;
        }
        catch (Exception failure) when (failure is IccParityError or StructuralError)
        {
            Out.Error($"::error title={Title} harness::{failure.Message}");
            Out.Error($"structural failure: {failure.Message}");
            return 1;
        }
    }
}
