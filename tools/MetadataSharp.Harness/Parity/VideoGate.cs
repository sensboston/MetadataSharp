using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// Fail closed on video-container key/value parity outside recorded reader divergences.
//
// The pinned Exiv2 0.28.9 RIFF reader reads BITMAPINFOHEADER pixel densities as QWORDs, so the color-count
// fields for the upstream flame.avi sample are shifted. We intentionally decode the bounded, declared DWORD
// fields instead. It also rejects RIFF/WAVE as an unknown image type, so the authored WAV is tested by
// MetadataSharp but excluded from Exiv2 parity. No key-set difference is allowlisted for formats the oracle
// reads.
//
// (Formerly tools/parity/video_gate.py; the argument, output, step summary and annotations are unchanged.)
internal static class VideoGate
{
    public static readonly IReadOnlySet<string> VideoFormats = new HashSet<string>(StringComparer.Ordinal) { "asf", "avi", "mov", "mp4", "mkv", "wav", "webm" };

    public static readonly IReadOnlySet<PyTuple> AllowedValueDiffs = new HashSet<PyTuple>
    {
        new("exiv2:flame.avi", "Xmp.video.NumOfColours"),
        new("exiv2:flame.avi", "Xmp.video.NumIfImpColours"),
    };

    public static readonly IReadOnlySet<string> RequiredGenerated = new HashSet<string>(StringComparer.Ordinal)
    {
        "generated:authored-asf.asf",
        "generated:authored-avi.avi",
        "generated:authored-matroska.mkv",
        "generated:authored-quicktime.mp4",
        "generated:authored-webm.webm",
    };

    public static (bool Passed, string Message) Evaluate(object? report)
    {
        object? files = PyOps.Get(report, "per_file");
        if (files is not List<object?> entries) return (false, "parity report has no per_file rows");
        var selected = entries.Where(entry => PyOps.In(PyOps.Get(entry, "format"), VideoFormats)).ToList();
        // The C# parity report preserves corpus-qualified labels for each input file.
        var ids = selected.Select(entry => Py.Str(PyOps.Get(entry, "label"))).ToHashSet(StringComparer.Ordinal);
        var absent = Py.Sorted(RequiredGenerated.Where(id => !ids.Contains(id)));
        if (absent.Count > 0)
        {
            var generated = Py.Sorted(entries.Where(entry => Py.Eq(PyOps.Get(entry, "corpus"), "generated"))
                                          .Select(entry => $"{Py.Str(PyOps.Get(entry, "label"))} [format={Py.Str(PyOps.Get(entry, "format"))}]"));
            return (false, "required authored video fixtures missing from video selection: " + string.Join(", ", absent)
                           + "; generated report entries: " + (generated.Count > 0 ? string.Join(", ", generated) : "none"));
        }

        var oursOnly = new HashSet<object?>(Py.EqualityComparer);
        var referenceOnly = new HashSet<object?>(Py.EqualityComparer);
        var unexpectedValues = new List<(string Label, object? Key, object? Ours, object? Theirs)>();
        var failures = new List<string>();
        var allowedSeen = new HashSet<object?>(Py.EqualityComparer);
        foreach (object? entry in selected)
        {
            string label = Py.Str(PyOps.Get(entry, "label"));
            if (!Py.Eq(PyOps.Get(entry, "status"), "ok"))
                failures.Add($"{label}: reader status {Py.Str(PyOps.Get(entry, "status"))} ({Py.Str(PyOps.Get(entry, "detail", ""))})");
            if (Py.Truthy(PyOps.Get(entry, "rows_dropped", 0L)))
                failures.Add($"{label}: parity report dropped {Py.Str(PyOps.Item(entry, "rows_dropped"))} rows; key/value gate requires complete rows");
            foreach (object? row in PyOps.Iterate(PyOps.Get(entry, "rows", new List<object?>())))
            {
                object? outcome = PyOps.Get(row, "outcome");
                object? key = PyOps.Get(row, "key", "");
                if (Py.Eq(outcome, "extra-key"))
                {
                    oursOnly.Add(PyOps.Hashable(key));
                }
                else if (Py.Eq(outcome, "missing-key"))
                {
                    referenceOnly.Add(PyOps.Hashable(key));
                }
                else if (Py.Eq(outcome, "value-diff"))
                {
                    var pair = new PyTuple(label, PyOps.Hashable(key));
                    if (AllowedValueDiffs.Contains(pair)) allowedSeen.Add(pair);
                    else unexpectedValues.Add((label, key, PyOps.Get(row, "our_value", ""), PyOps.Get(row, "their_value", "")));
                }
                else if (Py.Eq(outcome, "match-key"))
                {
                    unexpectedValues.Add((label, key, PyOps.Get(row, "our_value", ""), PyOps.Get(row, "their_value", "")));
                }
            }
        }

        var problems = new List<string>();
        if (oursOnly.Count > 0) problems.Add("ours-only keys (must be removed): " + PyOps.Join(", ", Py.Sorted(oursOnly)));
        if (referenceOnly.Count > 0) problems.Add("reference-only keys (must be implemented): " + PyOps.Join(", ", Py.Sorted(referenceOnly)));
        if (unexpectedValues.Count > 0)
            problems.Add("unapproved value differences: " + string.Join("; ", unexpectedValues.Select(value =>
                $"{value.Label} {Py.Str(value.Key)}: ours={Py.Repr(value.Ours)}, Exiv2={Py.Repr(value.Theirs)}")));
        problems.AddRange(failures);

        string Listed(HashSet<object?> keys)
        {
            string joined = PyOps.Join(", ", Py.Sorted(keys));
            return joined.Length > 0 ? joined : "none";
        }

        string allowed = string.Join(", ", Py.Sorted(allowedSeen).Cast<PyTuple>().Select(pair => $"{Py.Str(pair[0])} {Py.Str(pair[1])}"));
        var lines = new List<string>
        {
            "Video parity gate (Exiv2 0.28.9):",
            $"  measured video files: {selected.Count}",
            "  documented oracle limitation: generated:authored-wave.wav (RIFF/WAVE is rejected as unknown image type; covered by reader tests, not key parity)",
            "  ours-only keys: " + Listed(oursOnly),
            "  reference-only keys: " + Listed(referenceOnly),
            "  allowed documented AVI value differences: " + (allowed.Length > 0 ? allowed : "none"),
        };
        if (problems.Count > 0)
        {
            lines.Add("FAIL");
            lines.AddRange(problems.Select(problem => "  " + problem));
            return (false, string.Join("\n", lines) + "\n");
        }
        lines.Add("PASS");
        return (true, string.Join("\n", lines) + "\n");
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("video-gate", "Fail closed on video-container key/value parity outside recorded reader divergences.")
            .Add("report", help: "parity-report.json written by the C# parity tool");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string path = PyPath.Normalize(args.Str("report"));
        object? report;
        try
        {
            report = PyJson.Loads(PyFile.ReadText(path));
        }
        catch (Exception error) when (error is PyOSError or JsonDecodeError)
        {
            Out.Print($"video parity gate could not read report: {error.Message}");
            Out.Print("::error::Video gate could not load parity-report.json: " + PyText.Head(error.Message, 1200));
            return 1;
        }
        Out.Print("::notice title=VideoGate::started");
        bool passed;
        string message;
        try
        {
            (passed, message) = Evaluate(report);
        }
        catch (Exception error)
        {
            Out.Print("::error::Video gate evaluation crashed: " + PyException.ReprOf(error));
            throw;
        }
        Console.Out.Write(message);
        Console.Out.Flush();
        string? summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(summaryPath))
        {
            PyFile.AppendText(summaryPath, "## Video parity gate (Exiv2 0.28.9)\n\n```text\n");
            PyFile.AppendText(summaryPath, message);
            PyFile.AppendText(summaryPath, "```\n");
        }
        if (!passed)
        {
            // Write to stderr as well as stdout: this remains visible in the failed step log even when the
            // runner or check-annotation collector suppresses stdout annotations from child tools.
            Console.Error.Write(message);
            Console.Error.Flush();
            foreach (string line in PyText.SplitLines(message))
            {
                if (PyText.Strip(line) is "FAIL" or "") continue;
                string safeLine = PyText.Head(PyText.Strip(line), 1500);
                Out.Print("::notice title=VideoGate result::" + safeLine);
                Out.Print("::error title=VideoGate::" + safeLine);
            }
        }
        return passed ? 0 : 1;
    }
}
