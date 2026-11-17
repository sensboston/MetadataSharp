using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// A corpus directory is missing or empty, which means the run is set up wrongly.
internal sealed class CorpusError(string message) : Exception(message);

// One file to read, with the expectation to compare it against.
internal sealed class Candidate
{
    public Candidate(string label, string path, string corpus, string? reference = null, string format = "")
    {
        Label = label;
        Path = path;
        Corpus = corpus;
        Reference = reference;
        Format = format.Length > 0 ? format : (PyPath.Suffix(path).TrimStart('.').ToLowerInvariant() is { Length: > 0 } suffix ? suffix : "none");
    }

    public string Label { get; }

    public string Path { get; }

    public string Corpus { get; }

    public string? Reference { get; }

    public string Format { get; }

    public List<string> Notes { get; } = [];

    /// <summary>Unique within a run: two corpora can hold files with the same basename.</summary>
    public string Uid => $"{Corpus}:{Label}";
}

// Corpus discovery for the differential parity harness. Two corpora are read, and neither one is copied
// into this repository. "exiv2" is the upstream test tree of the pinned commit: its test/data directory
// holds the sample files and test/data/test_reference_files holds what the reference tool printed for
// them at that commit, so the expectation and the input travel together. A reference whose input cannot
// be found is reported rather than guessed at. "generated" is the fixtures this repository owns; their
// expectation is produced in the same CI job by the pinned reference build.
internal static class Corpus
{
    // Extensions tried when a reference's basename does not name an existing input file. Upstream names
    // some expectations after a converted file rather than the one on disk.
    public static readonly string[] FallbackExtensions =
    [
        ".jpg", ".jpeg", ".tiff", ".tif", ".png", ".webp", ".psd", ".jp2", ".jxl", ".avif", ".heic",
        ".crw", ".raf", ".dng", ".cr2", ".nef", ".orf", ".rw2", ".arw", ".sr2", ".srw", ".pef", ".mrw",
        ".mp4", ".mov", ".avi", ".asf", ".wav", ".xmp", ".exv", ".eps", ".pgf", ".dcp",
        ".gif", ".bmp", ".tga", ".targa",
    ];

    public static readonly string[] SkipSuffixes = [".json", ".md", ".txt", ".py", ".sh", ".out"];

    /// <summary>Pair every expected-output file under tree/test/data with its input; also the expectations without one.</summary>
    public static (List<Candidate> Candidates, List<string> Unmatched) DiscoverExiv2(string tree)
    {
        string data = PyPath.Join(tree, "test", "data");
        string references = PyPath.Join(data, "test_reference_files");
        if (!Directory.Exists(references)) throw new CorpusError($"no expected-output directory at {references}");
        var candidates = new List<Candidate>();
        var unmatched = new List<string>();
        var entries = PyPath.IterDir(references).ToList();
        entries.Sort(PyPath.Comparer);
        foreach (string output in entries)
        {
            if (PyPath.Suffix(output) != ".out" || !File.Exists(output)) continue;
            string name = PyPath.Name(output);
            string stem = name[..^".out".Length];
            string? found = ResolveInput(stem, data);
            if (found is null)
            {
                unmatched.Add(stem);
                continue;
            }
            candidates.Add(new Candidate(stem, found, "exiv2", output, FormatOf(found)));
        }
        if (candidates.Count == 0) throw new CorpusError($"{references} holds no usable expectation");
        return (candidates, unmatched);
    }

    private static string? ResolveInput(string stem, string data)
    {
        string exact = PyPath.Join(data, stem);
        if (File.Exists(exact)) return exact;
        // An upstream *_test expectation is sometimes an intentionally empty test fixture, not a
        // converted-file alias. Never guess its input from the name before the suffix.
        if (stem.EndsWith("_test", StringComparison.Ordinal)) return null;
        string baseName = stem.Contains('.') ? stem[..stem.LastIndexOf('.')] : stem;
        foreach (string extension in FallbackExtensions)
        {
            string candidate = PyPath.Join(data, baseName + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The format label a candidate is counted under.</summary>
    public static string FormatOf(string path)
    {
        string suffix = PyPath.Suffix(path).TrimStart('.').ToLowerInvariant();
        return suffix switch
        {
            "tif" => "tiff",
            "jpeg" => "jpg",
            "cr2" or "nef" or "orf" or "rw2" or "arw" or "sr2" or "srw" or "pef" or "mrw" or "raf" or "crw" => "raw",
            "dng" => "dng",
            "" => "none",
            _ => suffix,
        };
    }

    /// <summary>Every non-bookkeeping file under the given fixture directories (manifests and ledgers are skipped by suffix).</summary>
    public static List<Candidate> DiscoverGenerated(params string[] directories)
    {
        var candidates = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory)) throw new CorpusError($"missing fixture directory {PyPath.Normalize(directory)}");
            foreach (string path in PyPath.RGlob(directory))
            {
                if (!File.Exists(path) || SkipSuffixes.Contains(PyPath.Suffix(path).ToLowerInvariant())) continue;
                if (!seen.Add(path)) continue;
                candidates.Add(new Candidate(PyPath.Name(path), path, "generated", null, FormatOf(path)));
            }
        }
        if (candidates.Count == 0) throw new CorpusError("no fixture files found in " + string.Join(", ", directories.Select(PyPath.Normalize)));
        return candidates;
    }
}
