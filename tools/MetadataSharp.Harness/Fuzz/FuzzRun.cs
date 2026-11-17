using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Fuzz;

// One bounded fuzz campaign for one target of tools/MetadataSharp.Fuzz.
//
// 1. Replay. Every input of the corpus runs through the target once, whole, in a harness process that reports
//    input by input. An input that fails, kills the process or takes longer than its time budget is a finding.
// 2. Fuzz. afl-fuzz mutates a selection of the inputs - two per source and extension, the smallest and the
//    median, cut to AFL's limit of 1 MiB - for the given number of seconds.
// 3. Triage. Every crash and hang afl-fuzz saves is replayed for its stack trace; the saved inputs are grouped by
//    failure, and the first input of each group is minimized with afl-tmin.
//
// report.json holds the numbers and the findings; the run exits non-zero on any finding, on an afl-fuzz failure
// and on a campaign that executed nothing. The minimized inputs, and the first few inputs of each failure under
// reproductions/, are artifacts, not source changes: each failure is reproduced, fixed and checked in under
// tests/fixtures/fuzz. (Formerly tools/MetadataSharp.Fuzz/run.py; the arguments, the report, the logs, the
// annotations, the step summary and the exit status are unchanged.)
internal static class FuzzRun
{
    public static readonly string[] Targets = "span stream previews embedded icc comments iptc xmp maker write".Split(' ');
    public static readonly HashSet<string> WholeFile = new("span stream previews embedded icc comments write".Split(' '), StringComparer.Ordinal);
    public static readonly Dictionary<string, string> PacketKey = new(StringComparer.Ordinal) { ["iptc"] = "iptc_seed", ["xmp"] = "xmp_seed", ["maker"] = "maker_seed" };
    public const int AflMax = 1024 * 1024;
    public const int Mib = 1024 * 1024;

    /// <summary>Seconds an input may take in replay, instrumented and with the first run's JIT included (the tests
    /// shorten it, as the Python tests patched BASE_BUDGET).</summary>
    public static double BaseBudget { get; internal set; } = 10.0;

    public const double PerMibBudget = 2.0;

    // Seconds one execution may take under afl-fuzz before it counts as a hang (inputs <= 1 MiB).
    public const int AflTimeoutMs = 5000;

    // Triage: the saved crashes and hangs replayed, the distinct failures minimized, and for how long each.
    public const int MaxTriage = 2000;
    public const int MaxMinimized = 8;
    public const int TminSeconds = 180;

    // Inputs of each distinct failure kept as artifacts, under names upload-artifact accepts.
    public const int KeptPerFailure = 5;

    // A seed each target always has, should the corpus give none: they parse as what the target reads.
    public static readonly Dictionary<string, byte[]> Fallback = new(StringComparer.Ordinal)
    {
        ["iptc"] = [0x1C, 1, 90, 0, 3, 0x1B, 0x25, 0x47, 0x1C, 2, 0, 0, 2, 0, 4, 0x1C, 2, 120, 0, 4, .. "fuzz"u8],
        ["xmp"] = [.. "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"u8,
                   .. "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"\""u8,
                   .. " xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">fuzz"u8,
                   .. "</rdf:li></rdf:Alt></dc:title></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>"u8],
        ["maker"] = [0, 0, 8, 0, 0, 0, 26, 0, 0, 0, 0, .. "II*\0\b\0\0\0"u8,
                     2, 0, 1, 0, 3, 0, 1, 0, 0, 0, 1, 0, 0, 0, 2, 0, 2, 0, 4, 0, 0, 0, .. "fuzz"u8, 0, 0, 0, 0, 0, 0, 0, 0],
    };

    public static double Budget(long size) => BaseBudget + PerMibBudget * size / Mib;

    public static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(PyFile.ReadBytes(path)));

    /// <summary>(label, path) of every regular file of every source, in a stable order.</summary>
    public static List<(string Label, string Path)> CorpusFiles(IEnumerable<(string Label, string Root)> sources)
    {
        var files = new List<(string, string)>();
        foreach (var (label, root) in sources)
        {
            foreach (string path in PyPath.RGlob(root))
            {
                if (File.Exists(path) && !PyPath.Parts(path).Contains(".git")) files.Add((label, PyPath.Resolve(path)));
            }
        }
        return files;
    }

    /// <summary>Python's ordering of (size, path) pairs: by size, then by the path as pathlib compares paths.</summary>
    private static int CompareSized((long Size, string Path) left, (long Size, string Path) right)
    {
        int bySize = left.Size.CompareTo(right.Size);
        return bySize != 0 ? bySize : PyPath.Compare(left.Path, right.Path);
    }

    /// <summary>The smallest and the median of a group of (size, path), without repeats.</summary>
    public static List<string> Pick(IEnumerable<(long Size, string Path)> group)
    {
        var sorted = group.ToList();
        sorted.Sort(CompareSized);
        var chosen = new[] { sorted[0], sorted[sorted.Count / 2] };
        return chosen.Select(item => item.Path).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>A workflow annotation: the one channel that reaches the pull request without the artifacts.</summary>
    public static void Annotate(string level, string title, string message)
    {
        title = title.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)
                     .Replace(":", "%3A", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal);
        message = PyText.Head(message, 6000).Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);
        Out.Print($"::{level} title={title}::{message}");
    }

    private static readonly Regex Quoted = new("'[^']*'", RegexOptions.CultureInvariant);

    private static readonly Regex AflSignalPattern = new("sig:([0-9]+)", RegexOptions.CultureInvariant);

    /// <summary>The signal an afl-fuzz file name carries (id:000000,sig:02,...), or null when the name carries none.</summary>
    internal static int? AflSignal(string path)
    {
        Match match = AflSignalPattern.Match(PyPath.Name(path));
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int signal) ? signal : null;
    }

    /// <summary>Whether a failure is not a finding of the campaign: a saved crash that passes when it is replayed
    /// alone, whose death was signalled by SIGINT or SIGTERM - the asynchronous interrupts the runner can deliver
    /// to the process group, which reach the forkserver child of afl-fuzz. A crash that reproduces, a crash that
    /// died by any other signal, and any hang are findings all the same.</summary>
    internal static bool IsNotAFinding(PyDict failure, string path)
    {
        object? kind = failure.Get("kind");
        return kind is string text && text == "crash-not-reproduced" && AflSignal(path) is 2 or 15;
    }


    /// <summary>What tells one bug from another: the exception and the frames of the library, without numbers.</summary>
    public static string Signature(PyDict failure)
    {
        var lines = PyText.SplitLines(Py.Str(failure.Get("detail", ""))).Select(line => PyText.Strip(line)).Where(line => line.Length > 0).ToList();
        string head = lines.Count > 0 ? lines[0] : "";
        var frames = lines.Where(line => line.StartsWith("at ", StringComparison.Ordinal) && !line.Contains("MetadataSharp.Fuzz", StringComparison.Ordinal))
                          .Select(line => PyText.Split(line, " in ", 1)[0]).Take(3);
        string message = PyText.Split(head, ":", 1)[0];
        if (head.StartsWith("MetadataSharp.Fuzz.FuzzInvariantException:", StringComparison.Ordinal))
        {
            // A broken invariant is told by what it says, which names the operation and the format; what it
            // quotes of the input is left out.
            message = PyText.Split(PyText.Split(PyText.Split(head, ":", 1)[1], " at line ", 1)[0], ": before ", 1)[0];
            message = PyText.Head(Quoted.Replace(message, "''"), 160);
        }
        string text = string.Join(" | ", new[] { message }.Concat(frames));
        string kind = Py.Str(failure.Get("kind", "")).Replace("-not-reproduced", "", StringComparison.Ordinal);
        var masked = new StringBuilder();
        foreach (int point in Py.CodePoints(text))
        {
            string character = Py.FromCodePoint(point);
            masked.Append(CharUnicodeInfo.GetDigitValue(character, 0) >= 0 ? "#" : character);
        }
        return kind + " " + masked;
    }

    /// <summary>An afl-fuzz file name (id:000010,sig:02,...) under one upload-artifact accepts: it refuses colons.</summary>
    public static string Portable(string name)
    {
        var kept = new StringBuilder();
        foreach (int point in Py.CodePoints(name))
            kept.Append(point is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-' ? (char)point : '_');
        return kept.ToString();
    }

    /// <summary>The bytes of a small input, deflated and in base64, or null for a large one.</summary>
    public static string? Packed(string path)
    {
        string text = Convert.ToBase64String(Zlib.Compress(PyFile.ReadBytes(path), 9));
        return text.Length <= 2400 ? text : null;
    }

    public static PyDict AflStats(string findings)
    {
        var stats = new PyDict();
        foreach (string path in PyPath.RGlob(findings, "fuzzer_stats"))
        {
            foreach (string line in PyText.SplitLines(PyFile.ReadText(path, replace: true)))
            {
                if (!line.Contains(':')) continue;
                var parts = PyText.Split(line, ":", 1);
                stats[PyText.Strip(parts[0])] = PyText.Strip(parts[1]);
            }
        }
        return stats;
    }

    /// <summary>round(value, 1) of a measured time, a float as Python keeps it.</summary>
    private static double Round1(double value) => Py.Round(value, 1);

    /// <summary>format(value, 'g').</summary>
    private static string G(double value) => value.ToString("G6", CultureInfo.InvariantCulture).Replace("E", "e", StringComparison.Ordinal);

    private static (string Label, string Root) Source(string item)
    {
        var parts = PyText.Split(item, "=", 1);
        if (parts.Count != 2) throw new PyException("ValueError", $"not enough values to unpack (expected 2, got {parts.Count})");
        return (parts[0], parts[1]);
    }

    /// <summary>Packet and MakerNote seeds from every whole file: OUTPUT/extract.jsonl and OUTPUT/{iptc,xmp,maker}.</summary>
    public static void Extract(ParsedArgs args, IReadOnlyList<string> command, IReadOnlyDictionary<string, string>? env)
    {
        string output = PyPath.Resolve(args.Str("output"));
        RemoveTree(output);
        Directory.CreateDirectory(output);
        var records = new List<object?>();
        var failures = new List<object?>();
        using (var log = OpenLog(PyPath.Join(output, "extract.log")))
        {
            foreach (var (label, root) in args.List("source").Select(Source).ToList())
            {
                var files = CorpusFiles([(label, root)]).Select(item => item.Path).ToList();
                var run = new Replay([.. command, "--extract", output, label], env, log);
                run.Run(files, PyPath.Join(output, $"{label}.list"));
                records.AddRange(run.Records);
                failures.AddRange(run.Failures);
            }
        }
        PyFile.WriteText(PyPath.Join(output, "extract.jsonl"), string.Concat(records.Select(record => PyJson.Dumps(record) + "\n")));
        var crashes = records.Where(record => record is PyDict dict && dict.ContainsKey("crash")).ToList();
        long Count(string key) => records.Count(record => record is PyDict dict && dict.ContainsKey(key));
        Out.Print(PyJson.Dumps(PyDict.Of(("files", (long)records.Count), ("iptc", Count("iptc_seed")), ("xmp", Count("xmp_seed")), ("maker", Count("maker_seed")),
                                         ("crashes", (long)crashes.Count), ("failures", failures.Take(20).ToList())), indent: 2));
        // A read that throws or kills the process is a finding of the span target's replay; it is reported there,
        // so that each finding fails one job and not all of them.
    }

    /// <summary>shutil.rmtree(path, ignore_errors=True).</summary>
    private static void RemoveTree(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // ignore_errors=True
        }
    }

    /// <summary>open(path, 'w'): text in UTF-8, written as it comes.</summary>
    internal static StreamWriter OpenLog(string path) => new(path, false, new UTF8Encoding(false));

    private static IReadOnlyDictionary<string, string> Environment(IEnumerable<KeyValuePair<string, string>> extra, IReadOnlyDictionary<string, string>? baseEnvironment = null)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (baseEnvironment is null)
        {
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables()) environment[(string)entry.Key] = (string?)entry.Value ?? "";
        }
        else
        {
            foreach (var pair in baseEnvironment) environment[pair.Key] = pair.Value;
        }
        foreach (var pair in extra) environment[pair.Key] = pair.Value;
        return environment;
    }

    private static KeyValuePair<string, string> P(string key, string value) => new(key, value);

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("fuzz-run", "One bounded fuzz campaign for one target of tools/MetadataSharp.Fuzz.")
            .Add("target", choices: [.. Targets, "extract"])
            .Add("--dll", required: true, help: "the instrumented MetadataSharp.Fuzz.dll")
            .Add("--source", ArgAction.Append, @default: new List<string>(), help: "whole files")
            .Add("--extracted", help: "the output of MetadataSharp.Fuzz --extract (extract.jsonl)")
            .Add("--dictionary", help: "an afl-fuzz dictionary")
            .Add("--output", required: true)
            .Add("--seconds", isInt: true, @default: 300L);
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string target = args.Str("target");
        string[] command = ["dotnet", PyPath.Resolve(args.Str("dll"))];
        var env = Environment([P("DOTNET_GCHeapHardLimit", "0x20000000"), P("DOTNET_gcServer", "0"), P("DOTNET_CLI_TELEMETRY_OPTOUT", "1")]);
        if (target == "extract")
        {
            Extract(args, command, env);
            return 0;
        }
        long seconds = args.Int("seconds");
        string output = PyPath.Resolve(args.Str("output"));
        RemoveTree(output);
        Directory.CreateDirectory(PyPath.Join(output, "seeds"));
        var sources = args.List("source").Select(Source).ToList();
        var failures = new List<object?>();
        var report = PyDict.Of(("target", target), ("seconds_requested", seconds), ("afl_timeout_ms", (long)AflTimeoutMs),
                               ("replay_budget", $"{G(BaseBudget)} s + {G(PerMibBudget)} s/MiB"), ("failures", failures));

        // ---- inputs: whole files, or the packets and notes extracted from them ----
        var inputs = new List<(string Label, string Path)>();
        var groups = new Dictionary<PyTuple, List<(long Size, string Path)>>();
        void Group(PyTuple key, long size, string path)
        {
            if (!groups.TryGetValue(key, out var members)) groups[key] = members = [];
            members.Add((size, path));
        }
        if (WholeFile.Contains(target))
        {
            inputs = CorpusFiles(sources);
            foreach (var (label, path) in inputs)
            {
                string extension = PyText.LStrip(PyPath.Suffix(path).ToLowerInvariant(), ".");
                Group(new PyTuple(label, extension.Length > 0 ? extension : "none"), new FileInfo(path).Length, path);
            }
        }
        else
        {
            string key = PacketKey[target];
            string extracted = args.StrOrNull("extracted") ?? throw new PyException("TypeError", "unsupported operand type(s) for /: 'NoneType' and 'str'");
            foreach (string line in PyText.SplitLines(PyFile.ReadText(PyPath.Join(extracted, "extract.jsonl"))))
            {
                var record = (PyDict)PyJson.Loads(line)!;
                if (!Py.Truthy(record.Get(key))) continue;
                string seed = PyPath.Normalize((string)record[key]!);
                string label = (string)record["label"]!;
                inputs.Add((label, seed));
                object? group = target == "maker" ? record.Get("maker") : PyPath.Suffix((string)record["path"]!).ToLowerInvariant();
                Group(new PyTuple(label, group), StatSize(seed), seed);
            }
            if (target == "xmp")
            {
                // Sidecar files are packets as they are.
                foreach (var (label, path) in CorpusFiles(sources))
                {
                    if (PyPath.Suffix(path).ToLowerInvariant() != ".xmp") continue;
                    inputs.Add((label, path));
                    Group(new PyTuple(label, "sidecar"), new FileInfo(path).Length, path);
                }
            }
            string fallback = PyPath.Join(output, $"fallback.{target}");
            PyFile.WriteBytes(fallback, Fallback[target]);
            inputs.Add(("fallback", fallback));
            Group(new PyTuple("fallback", target), new FileInfo(fallback).Length, fallback);
        }
        var unique = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, path) in inputs) unique.TryAdd(path, label);
        var counts = new PyDict();
        foreach (string label in unique.Values) counts[label] = (counts.ContainsKey(label) ? (long)counts[label]! : 0L) + 1;
        report["corpus"] = PyDict.Of(("inputs", (long)unique.Count), ("by_source", counts), ("bytes", unique.Keys.Sum(StatSize)));

        // ---- 1. replay ----
        var clock = Stopwatch.StartNew();
        Replay replay;
        using (var log = OpenLog(PyPath.Join(output, "replay.log")))
        {
            replay = new Replay([.. command, "--replay", target], env, log);
            replay.Run([.. unique.Keys], PyPath.Join(output, "replay.list"));
        }
        var passed = replay.Results.Where(pair => pair.Value.Status == "pass").Select(pair => (Ms: pair.Value.Ms, Path: pair.Key)).ToList();
        var slowest = passed.ToList();
        slowest.Sort((left, right) =>
        {
            int byMs = right.Ms.CompareTo(left.Ms);
            return byMs != 0 ? byMs : Py.CompareStrings(right.Path, left.Path);
        });
        var replayReport = PyDict.Of(("inputs", (long)unique.Count), ("passed", (long)passed.Count), ("failed", (long)replay.Failures.Count),
                                     ("seconds", Round1(clock.Elapsed.TotalSeconds)),
                                     ("slowest", slowest.Take(10).Select(item => (object?)PyDict.Of(("path", item.Path), ("ms", item.Ms))).ToList()));
        report["replay"] = replayReport;
        foreach (PyDict failure in replay.Failures.Cast<PyDict>())
        {
            var staged = PyDict.Of(("stage", "replay"));
            foreach (var pair in failure) staged[pair.Key] = pair.Value;
            failures.Add(staged);
        }

        // ---- 2. fuzz: the seeds are inputs that passed the replay ----
        var seeds = new List<object?>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in Py.Sorted(groups.Keys))
        {
            var group = groups[key].Where(item => replay.Results.TryGetValue(item.Path, out var result) && result.Status == "pass").ToList();
            if (group.Count == 0) continue;
            foreach (string path in Pick(group))
            {
                byte[] data = PyFile.ReadBytes(path);
                byte[] cut = data.Length > AflMax ? data[..AflMax] : data;
                string digest = Convert.ToHexStringLower(SHA256.HashData(cut));
                if (!seen.Add(digest)) continue;
                PyFile.WriteBytes(PyPath.Join(output, "seeds", digest[..32]), cut);
                seeds.Add(PyDict.Of(("source", key[0]), ("group", Py.Str(key[1])), ("path", path), ("bytes", (long)data.Length),
                                    ("mutation_bytes", (long)Math.Min(data.Length, AflMax))));
            }
        }
        report["seeds"] = seeds;
        var aflEnv = Environment([P("AFL_SKIP_BIN_CHECK", "1"), P("AFL_NO_UI", "1"), P("AFL_I_DONT_CARE_ABOUT_MISSING_CRASHES", "1"), P("AFL_SKIP_CPUFREQ", "1"),
                                  P("AFL_NO_AFFINITY", "1"), P("AFL_IGNORE_SEED_PROBLEMS", "1"), P("AFL_FORKSRV_INIT_TMOUT", "60000"), P("AFL_SKIP_CRASHES", "1")], env);
        string findings = PyPath.Join(output, "findings");
        var afl = new List<string> { "afl-fuzz", "-i", PyPath.Join(output, "seeds"), "-o", findings, "-V", seconds.ToString(CultureInfo.InvariantCulture),
                                     "-t", AflTimeoutMs.ToString(CultureInfo.InvariantCulture), "-m", "none" };
        if (args.StrOrNull("dictionary") is { } dictionary) afl.AddRange(["-x", PyPath.Resolve(dictionary)]);
        clock.Restart();
        object aflCode;
        {
            int? code = ChildProcess.RunToFile([.. afl, "--", .. command, target], aflEnv, PyPath.Join(output, "afl.log"), seconds + 900);
            aflCode = code is null ? "timeout" : (long)code.Value;
        }
        report["afl_exit"] = aflCode;
        report["elapsed_seconds"] = Round1(clock.Elapsed.TotalSeconds);
        var stats = AflStats(findings);
        var aflNumbers = new PyDict();
        foreach (string name in new[] { "run_time", "execs_done", "execs_per_sec", "cycles_done", "corpus_count", "corpus_found", "saved_crashes", "saved_hangs",
                                        "bitmap_cvg", "stability", "afl_version" })
            aflNumbers[name] = stats.Get(name);
        report["afl"] = aflNumbers;

        // ---- 3. triage: every saved crash and hang is replayed; one input of each distinct failure is minimized ----
        var found = PyPath.RGlob(findings, "id:*").Where(path => PyPath.Name(PyPath.Parent(path)) is "crashes" or "hangs").Take(MaxTriage).ToList();
        var tolerated = new List<(string Path, PyDict Failure)>();
        report["afl_findings"] = (long)found.Count;
        if (found.Count > 0)
        {
            clock.Restart();
            Replay triage;
            using (var log = OpenLog(PyPath.Join(output, "triage.log")))
            {
                triage = new Replay([.. command, "--replay", target], env, log);
                triage.Run(found, PyPath.Join(output, "triage.list"));
            }
            var byPath = new Dictionary<string, PyDict>(StringComparer.Ordinal);
            foreach (PyDict failure in triage.Failures.Cast<PyDict>()) byPath[(string)failure["path"]!] = failure;
            var distinctGroups = new OrderedDictionary<string, List<(string Path, PyDict Failure)>>(StringComparer.Ordinal);
            foreach (string path in found)
            {
                string kind = PyPath.Name(PyPath.Parent(path)) == "crashes" ? "crash" : "hang";
                if (!byPath.TryGetValue(path, out var failure))
                {
                    long ms = triage.Results.TryGetValue(path, out var result) ? result.Ms : 0;
                    failure = PyDict.Of(("kind", $"{kind}-not-reproduced"), ("path", path), ("detail", $"afl-fuzz saved this {kind}; replayed alone it passes in {ms} ms."));
                }
                if (IsNotAFinding(failure, path))
                {
                    tolerated.Add((path, failure));
                    continue;
                }
                string signature = Signature(failure);
                if (!distinctGroups.TryGetValue(signature, out var members)) distinctGroups[signature] = members = [];
                members.Add((path, failure));
            }
            int number = 0;
            foreach (var members in distinctGroups.Values)
            {
                var (path, failure) = members[0];
                var kept = new List<object?>();
                var entry = PyDict.Of(("stage", "afl"), ("kind", failure["kind"]), ("path", PyPath.RelativeTo(path, output)), ("bytes", new FileInfo(path).Length),
                                      ("count", (long)members.Count), ("also", members.Skip(1).Take(3).Select(other => (object?)PyPath.RelativeTo(other.Path, output)).ToList()),
                                      ("detail", failure.Get("detail", "")), ("kept", kept));
                foreach (var (other, _) in members.Take(KeptPerFailure))
                {
                    string copy = PyPath.Join(output, "reproductions", $"{number}-{PyPath.Name(PyPath.Parent(other))}-{Portable(PyPath.Name(other))}");
                    Directory.CreateDirectory(PyPath.Parent(copy));
                    PyShutil.CopyFile(other, copy);
                    kept.Add(PyPath.RelativeTo(copy, output));
                }
                if (number < MaxMinimized)
                {
                    string minimized = PyPath.Join(output, $"minimized-{number}");
                    var tmin = new List<string> { "afl-tmin", "-i", path, "-o", minimized, "-t", AflTimeoutMs.ToString(CultureInfo.InvariantCulture), "-m", "none" };
                    if (PyPath.Name(PyPath.Parent(path)) == "hangs") tmin.Add("-H");
                    ChildProcess.RunToFile([.. tmin, "--", .. command, target], aflEnv, null, TminSeconds);
                    if (!File.Exists(minimized) || new FileInfo(minimized).Length == 0)
                    {
                        minimized = PyPath.Join(output, $"unminimized-{number}");
                        PyShutil.CopyFile(path, minimized);
                    }
                    entry["minimized"] = PyPath.Name(minimized);
                }
                failures.Add(entry);
                number++;
            }
            if (tolerated.Count > 0)
            {
                var toleratedGroups = new OrderedDictionary<string, List<(string Path, PyDict Failure)>>(StringComparer.Ordinal);
                foreach (var (path, failure) in tolerated)
                {
                    string signature = Signature(failure);
                    if (!toleratedGroups.TryGetValue(signature, out var members)) toleratedGroups[signature] = members = [];
                    members.Add((path, failure));
                }
                int kept = 0;
                var toleratedReport = new List<object?>();
                foreach (var members in toleratedGroups.Values)
                {
                    var (path, failure) = members[0];
                    int? signal = AflSignal(path);
                    string signalName = signal switch { 2 => "SIGINT", 15 => "SIGTERM", _ => "signal " + (signal?.ToString(CultureInfo.InvariantCulture) ?? "?") };
                    var keptPaths = new List<object?>();
                    foreach (var (other, _) in members.Take(KeptPerFailure))
                    {
                        string copy = PyPath.Join(output, "reproductions", $"signal-{kept}-{Portable(PyPath.Name(other))}");
                        Directory.CreateDirectory(PyPath.Parent(copy));
                        PyShutil.CopyFile(other, copy);
                        keptPaths.Add(PyPath.RelativeTo(copy, output));
                        kept++;
                    }
                    string source = path;
                    long size = new FileInfo(source).Length;
                    string? blob = File.Exists(source) ? Packed(source) : null;
                    var lines = new List<string> { $"{members.Count} input(s); first: {Py.Str(path)}" };
                    lines.AddRange(members.Skip(1).Take(3).Select(other => $"also: {Py.Str(other.Path)}"));
                    lines.Add(PyText.Head(Py.Str(failure.Get("detail", "")), 4200));
                    lines.Add(blob is not null ? $"INPUT {PyPath.Name(source)} ({size} bytes) zlib+base64: {blob}" : $"INPUT {PyPath.Name(source)}: {size} bytes, too large to show");
                    lines.Add("Not a finding: the input passes when it is replayed alone and the death was " + signalName +
                              ", which the runner can deliver to the process group asynchronously, reaching the forkserver child of afl-fuzz. " +
                              "The input is kept as an artifact; a crash that reproduces or dies by any other signal is a finding.");
                    Annotate("warning", $"Fuzz {target} afl crash-not-reproduced ({signalName})", string.Join("\n", lines));
                    toleratedReport.Add(PyDict.Of(("signal", (long)(signal ?? 0)), ("signal_name", signalName), ("count", (long)members.Count), ("kept", keptPaths)));
                }
                report["not_reproduced_signals"] = toleratedReport;
                Annotate("notice", $"Fuzz {target} not reproduced", $"{tolerated.Count} saved crash(es) died by SIGINT or SIGTERM and passed on replay; kept as artifacts, not findings.");
            }
            report["triage_seconds"] = Round1(clock.Elapsed.TotalSeconds);
        }

        PyFile.WriteText(PyPath.Join(output, "report.json"), PyJson.Dumps(report, indent: 2));
        var replaySummary = new PyDict();
        foreach (string name in new[] { "inputs", "passed", "failed", "seconds" }) replaySummary[name] = replayReport[name];
        Annotate("notice", $"Fuzz {target}", PyJson.Dumps(PyDict.Of(("corpus", report["corpus"]), ("replay", replaySummary),
                                                                    ("slowest", ((List<object?>)replayReport["slowest"]!).Take(3).ToList()), ("seeds", (long)seeds.Count),
                                                                    ("afl_exit", aflCode), ("afl", aflNumbers), ("findings", (long)failures.Count))));
        bool aflFailed = aflCode is not 0L;
        if (aflFailed || stats.Count == 0)
        {
            var tail = PyText.SplitLines(PyFile.ReadText(PyPath.Join(output, "afl.log"), replace: true)).TakeLast(80);
            Annotate("error", $"Fuzz {target} afl-fuzz", string.Join("\n", tail.Select(line => Escapes.Replace(line, ""))));
        }
        // One annotation a distinct failure, with its first input (deflated, base64) when it is small.
        var distinct = new OrderedDictionary<string, List<PyDict>>(StringComparer.Ordinal);
        foreach (PyDict failure in failures.Cast<PyDict>())
        {
            string signature = Signature(failure);
            if (!distinct.TryGetValue(signature, out var group)) distinct[signature] = group = [];
            group.Add(failure);
        }
        int index = 0;
        foreach (var group in distinct.Values)
        {
            if (index == 9)
            {
                Annotate("error", $"Fuzz {target} more", $"{distinct.Count - 9} further distinct failures");
                break;
            }
            var first = group[0];
            string source = Py.Truthy(first.Get("minimized")) ? (string)first["minimized"]! : (string)first["path"]!;
            if (!PyPath.IsAbsolute(source)) source = PyPath.Join(output, source);
            long inputCount = group.Sum(failure => Convert.ToInt64(failure.Get("count", 1L), CultureInfo.InvariantCulture));
            var lines = new List<string> { $"{inputCount} input(s); first: {Py.Str(first["path"])}" };
            var others = group.Skip(1).Select(failure => failure["path"]).Concat(PyOps.Iterate(first.Get("also", new List<object?>()))).Take(3);
            lines.AddRange(others.Select(other => $"also: {Py.Str(other)}"));
            lines.Add(PyText.Head(Py.Str(first.Get("detail", "")), 4200));
            if (File.Exists(source) || Directory.Exists(source))
            {
                string? blob = File.Exists(source) ? Packed(source) : throw PyOSError.For(21, source);
                long size = new FileInfo(source).Length;
                lines.Add(blob is not null ? $"INPUT {PyPath.Name(source)} ({size} bytes) zlib+base64: {blob}" : $"INPUT {PyPath.Name(source)}: {size} bytes, too large to show");
            }
            Annotate("error", $"Fuzz {target} {Py.Str(first["stage"])} {Py.Str(first["kind"])}", string.Join("\n", lines));
            index++;
        }
        var summary = new PyDict();
        foreach (var pair in report)
        {
            if (!Py.Eq(pair.Key, "seeds")) summary[pair.Key] = pair.Value;
        }
        Out.Print(PyText.Head(PyJson.Dumps(summary, indent: 2), 20000));
        string? step = System.Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(step))
        {
            var text = new StringBuilder();
            text.Append($"### {target}\n\n| replayed | replay failures | seeds | seconds | executions | exec/s | crashes | hangs | coverage |\n|---|---|---|---|---|---|---|---|---|\n");
            text.Append($"| {Py.Str(replayReport["inputs"])} | {Py.Str(replayReport["failed"])} | {seeds.Count} | {Py.Str(aflNumbers["run_time"])} | {Py.Str(aflNumbers["execs_done"])} | "
                        + $"{Py.Str(aflNumbers["execs_per_sec"])} | {Py.Str(aflNumbers["saved_crashes"])} | {Py.Str(aflNumbers["saved_hangs"])} | {Py.Str(aflNumbers["bitmap_cvg"])} |\n\n");
            foreach (PyDict failure in failures.Take(20).Cast<PyDict>()) text.Append($"- {Py.Str(failure["stage"])} {Py.Str(failure["kind"])}: `{Py.Str(failure["path"])}`\n");
            if (tolerated.Count > 0) text.Append($"- not reproduced (asynchronous signal): {tolerated.Count} input(s), kept as artifacts\n");
            PyFile.AppendText(step, text.ToString());
        }
        var problems = new List<string>();
        if (failures.Count > 0) problems.Add($"{failures.Count} finding(s)");
        if (aflFailed) problems.Add($"afl-fuzz exit {Py.Str(aflCode)}");
        if (stats.Count == 0 || ExecsDone(stats) < 1) problems.Add("no executions");
        if (problems.Count > 0) throw new SystemExit(1, "Fuzz campaign failed: " + string.Join(", ", problems) + " (see the artifacts)");
        return 0;
    }

    private static readonly Regex Escapes = new("\u001b\\[[0-9;]*[A-Za-z]", RegexOptions.CultureInvariant);

    /// <summary>int(stats.get('execs_done', '0') or 0).</summary>
    private static long ExecsDone(PyDict stats)
    {
        object? value = stats.Get("execs_done", "0");
        return value is string text && text.Length > 0 ? PyText.ParseInt(text) : 0;
    }

    private static long StatSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (FileNotFoundException)
        {
            throw PyOSError.For(2, path);
        }
    }
}
