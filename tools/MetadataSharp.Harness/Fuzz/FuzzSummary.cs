using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Fuzz;

// One table for all targets: the report.json of every fuzz job, downloaded into one directory (one subdirectory per
// artifact), as rows of a Markdown table. (Formerly the Python program of the summary job of fuzz.yml; the table
// is unchanged.)
internal static class FuzzSummary
{
    public static int Run(string[] argv)
    {
        string reports = argv.Length > 0 ? argv[0] : "reports";
        Out.Print("| target | replayed | seeds | seconds | executions | exec/s | crashes | hangs | findings |");
        Out.Print("|---|---|---|---|---|---|---|---|---|");
        var paths = !Directory.Exists(reports) ? []
            : PyPath.IterDir(reports).Where(Directory.Exists).Select(directory => PyPath.Join(directory, "report.json")).Where(PyPath.Exists).ToList();
        paths.Sort(PyPath.Comparer);
        foreach (string path in paths)
        {
            object? report = PyJson.Loads(PyFile.ReadText(path));
            object? afl = Py.Or(PyOps.Get(report, "afl"), new PyDict());
            string Afl(string key) => Py.Str(PyOps.Get(afl, key));
            Out.Print($"| {Py.Str(PyOps.Item(report, "target"))} | {Py.Str(PyOps.Item(PyOps.Item(report, "replay"), "inputs"))} | {Length(PyOps.Item(report, "seeds"))} | {Afl("run_time")} | "
                      + $"{Afl("execs_done")} | {Afl("execs_per_sec")} | {Afl("saved_crashes")} | {Afl("saved_hangs")} | {Length(PyOps.Item(report, "failures"))} |");
        }
        return 0;
    }

    /// <summary>len(value) of a list or dict of a report.</summary>
    private static int Length(object? value) => value switch
    {
        PyDict dict => dict.Count,
        string text => Py.Len(text),
        System.Collections.ICollection collection => collection.Count,
        _ => throw new PyException("TypeError", $"object of type '{Py.TypeName(value)}' has no len()"),
    };
}
