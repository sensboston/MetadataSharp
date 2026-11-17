using System.Diagnostics;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Ci;

// Assemble the reading-cost report: what BenchmarkDotNet measured, beside what exiv2 costs.
//
// The benchmarks compare this library with itself (a whole-file read against a range read of the same
// file), so their tables are quoted as BenchmarkDotNet wrote them. The pinned exiv2 binary is timed on the
// same files and appended, because the question the change answers is how the cost of reading compares
// with the tool this library is measured against, not how it compares with itself.
//
// Run by the streaming-bench job of .github/workflows/build-and-test.yml; nothing here is a gate.
// (Formerly tools/ci/read_cost_table.py; the arguments and the report are unchanged.)
internal static class ReadCostTable
{
    // How many times each file is read by exiv2; the fastest run is reported, so that a busy runner inflates
    // neither tool.
    public const int Runs = 3;

    /// <summary>The markdown tables BenchmarkDotNet wrote, in a stable order.</summary>
    public static List<string> BenchmarkTables(string results)
    {
        var tables = new List<string>();
        foreach (string path in PyPath.Glob(results, "*.md"))
        {
            string text = PyText.Strip(PyFile.ReadText(path));
            if (text.Contains('|')) tables.Add($"### `{PyPath.Stem(path)}`\n\n{text}");
        }
        return tables;
    }

    /// <summary>Time `exiv2 -pa` on every file the benchmarks measured, fastest of a few runs.</summary>
    public static List<string> Exiv2Tables(string exiv2, string corpus)
    {
        if (!PyPath.Exists(exiv2)) return ["### exiv2\n\nexiv2 was not available in this run."];
        // Mirror Corpus.Files(): largest file per extension, then six largest overall.
        var groups = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in PyPath.RGlob(corpus))
        {
            if (!File.Exists(path)) continue;
            string suffix = PyPath.Suffix(path).ToLowerInvariant();
            if (!groups.TryGetValue(suffix, out string? known) || new FileInfo(path).Length > new FileInfo(known).Length) groups[suffix] = path;
        }
        // A stable sort, largest first: files of one size keep the order of their extensions.
        var chosen = groups.Values.Select((path, index) => (path, index, size: new FileInfo(path).Length))
            .OrderByDescending(item => item.size).ThenBy(item => item.index).Select(item => item.path).Take(6).ToList();
        var rows = new List<string>();
        foreach (string path in chosen)
        {
            double? best = null;
            var failures = new List<int>();
            for (int run = 0; run < Runs; run++)
            {
                long started = Stopwatch.GetTimestamp();
                var result = PyProcess.Run([exiv2, "-pa", path], stdout: Pipe.DevNull);
                double spent = Stopwatch.GetElapsedTime(started).TotalSeconds;
                if (result.ReturnCode != 0) failures.Add(result.ReturnCode);
                else best = best is null ? spent : Math.Min(best.Value, spent);
            }
            string timing = best is not null ? Py.FormatFixed(best.Value * 1000, 1) : "failed";
            string status = failures.Count == 0 ? "ok" : $"{failures.Count}/{Runs} failed (exit {failures[0]})";
            rows.Add($"| `{PyPath.Name(path)}` | {Py.FormatFixed(new FileInfo(path).Length / (double)(1 << 20), 1)} | {timing} | {status} |");
            if (rows.Count == 6) break;
        }
        if (rows.Count == 0) return ["### exiv2\n\nNo raw file was large enough to time."];
        const string header = "| File | Size (MB) | exiv2 -pa (ms) | Status |\n| --- | ---: | ---: | --- |";
        return ["### exiv2 0.28.9, the same files\n\n" + header + "\n" + string.Join("\n", rows)];
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("read-cost-table", "Assemble the reading-cost report: what BenchmarkDotNet measured, beside what exiv2 costs.")
            .Add("--results", required: true, help: "BenchmarkDotNet results directory")
            .Add("--corpus", required: true, help: "the files that were measured")
            .Add("--exiv2", required: true, help: "the pinned exiv2 binary")
            .Add("--out", required: true, help: "the markdown file to write");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        var tables = BenchmarkTables(PyPath.Normalize(args.Str("results")));
        if (tables.Count == 0) tables = ["### BenchmarkDotNet\n\nNo benchmark table was written; see the job log."];
        var parts = new List<string>
        {
            "## What a file costs to read\n",
            "`Read(byte[])` measures preloaded parsing only. `File.ReadAllBytes + Read` includes\n"
            + "whole-file I/O and allocation, as the earlier path did. `Read(path)` and\n"
            + "`Read(Stream)` read the ranges the parsers ask for. The numbers belong to the runner; the\n"
            + "ratios are also runner- and cache-dependent.\n",
        };
        parts.AddRange(tables);
        parts.Add("## The same files, with the reference tool\n");
        parts.AddRange(Exiv2Tables(PyPath.Normalize(args.Str("exiv2")), PyPath.Normalize(args.Str("corpus"))));
        string output = PyPath.Normalize(args.Str("out"));
        PyFile.MakeDirs(PyPath.Parent(output));
        PyFile.WriteText(output, string.Join("\n\n", parts) + "\n");
        Out.Print(PyFile.ReadText(output));
        return 0;
    }
}
