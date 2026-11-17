using System.Security.Cryptography;
using System.Text;
using MetadataSharp.Harness.Parity;
using MetadataSharp.Harness.Python;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Parity;

// Offline checks of the pinned RAW corpus manifest and its downloader (formerly tests/parity/test_raw_corpus.py).
//
// The corpus itself is never in the repository, so what can be checked here is the manifest and the rules the
// downloader holds it to: every entry names a free-licensed source, a local name, a camera and a format, and a
// file whose bytes are not the pinned ones is refused and deleted rather than kept for a gate to read.
public sealed class RawCorpusManifestTests
{
    private static readonly HashSet<string> Formats = ["CRW", "CR2", "NEF", "ARW", "ORF", "PEF", "RW2", "SR2", "SRW", "DNG", "MRW", "RAF"];

    // raw.pixls.us publishes exactly one .SR2 file (the Sony DSC-R1 is the camera that wrote that format), so
    // that format cannot reach two cameras from the only free-licensed source there is.
    private static readonly Dictionary<string, int> KnownSingle = new() { ["SR2"] = 1 };

    private readonly PyDict document = RawCorpus.Load();

    [Fact]
    public void EveryEntryNamesACc0SourceAndNeverALocalFile()
    {
        Assert.Equal("CC0-1.0", document.Str("license"));
        foreach (var entry in RawCorpus.Entries(document))
        {
            Assert.StartsWith("https://raw.pixls.us/data/", entry.Url);
            Assert.True(entry.Camera.Length > 0, entry.Name);
            Assert.Equal(entry.Name, PyPath.Name(entry.Name));
            // The corpus is downloaded, never committed: nothing of it may live in the tree.
            Assert.False(File.Exists(Repo.Path("tests", "fixtures", entry.Name)), entry.Name);
        }
    }

    [Fact]
    public void EachNamedFormatHoldsMoreThanOneCamera()
    {
        var entries = RawCorpus.Entries(document);
        var cameras = entries.GroupBy(entry => entry.Format).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(Formats, cameras.Keys.ToHashSet());
        foreach (var (name, count) in cameras)
            Assert.True(count >= KnownSingle.GetValueOrDefault(name, 2), $"{name} holds {count} file(s)");
        // A camera may supply two formats (a Pentax K10D writes PEF and DNG); it still counts once.
        foreach (var group in entries.GroupBy(entry => entry.Format))
        {
            var models = group.Select(entry => entry.Camera).ToHashSet();
            Assert.True(models.Count >= KnownSingle.GetValueOrDefault(group.Key, 2), $"{group.Key} covers {string.Join(", ", models.Order())}");
        }
    }

    [Fact]
    public void AManifestEntryWithoutASourceIsRefused()
    {
        var copy = (PyDict)PyJson.Loads(PyJson.Dumps(document))!;
        var files = copy.Dict("files");
        files.Dict(files.Keys.First())["url"] = "https://example.invalid/sample.cr2";
        Assert.Throws<RawCorpusError>(() => RawCorpus.LoadFrom(copy));
    }

    [Fact]
    public void ALocalNameThatIsAPathIsRefused()
    {
        var copy = (PyDict)PyJson.Loads(PyJson.Dumps(document))!;
        var files = copy.Dict("files");
        object name = files.Keys.First();
        object? item = files[name];
        files.Remove(name);
        files["nested/" + (string)name] = item;
        Assert.Throws<RawCorpusError>(() => RawCorpus.LoadFrom(copy));
    }
}

public sealed class RawCorpusDownloadTests
{
    private static readonly string Good = Convert.ToHexStringLower(SHA256.HashData("good"u8));

    private readonly PyDict document;
    private readonly RawCorpus.Entry entry;

    public RawCorpusDownloadTests()
    {
        document = (PyDict)PyJson.Loads(PyJson.Dumps(RawCorpus.Load()))!;
        foreach (object? item in document.Dict("files").Values)
        {
            ((PyDict)item!)["bytes"] = 4L;
            ((PyDict)item)["sha256"] = Good;
        }
        entry = RawCorpus.Entries(document)[0];
    }

    private PyDict One() => PyDict.Of(("license", document["license"]), ("provenance", document["provenance"]),
                                      ("files", PyDict.Of((entry.Name, document.Dict("files")[entry.Name]))));

    private static PyDict DownloadWith(string destination, PyDict manifest, string bytes)
    {
        var original = RawCorpus.Fetch;
        RawCorpus.Fetch = (_, target) => File.WriteAllBytes(target, Encoding.ASCII.GetBytes(bytes));
        try
        {
            return RawCorpus.Download(destination, manifest);
        }
        finally
        {
            RawCorpus.Fetch = original;
        }
    }

    private static string Status(PyDict outcomes) => ((PyDict)outcomes.List("files")[0]!).Str("status");

    [Fact]
    public void AFileThatDoesNotMatchItsPinIsDeleted()
    {
        using var destination = Temp();
        var outcomes = DownloadWith(destination.Path, One(), "evil");
        Assert.Equal("mismatch", Status(outcomes));
        Assert.False(File.Exists(destination[entry.Name]));
        Assert.NotEmpty(RawCorpus.Verify(destination.Path, One()));
    }

    [Fact]
    public void AFileThatMatchesItsPinIsKeptAndVerified()
    {
        using var destination = Temp();
        var outcomes = DownloadWith(destination.Path, One(), "good");
        Assert.Equal("downloaded", Status(outcomes));
        Assert.Empty(RawCorpus.Verify(destination.Path, One()));
    }

    [Fact]
    public void AnUnpinnedEntryCanBeDownloadedButNeverVerified()
    {
        var manifest = One();
        manifest.Dict("files").Dict(entry.Name)["sha256"] = null;
        using var destination = Temp();
        var outcomes = DownloadWith(destination.Path, manifest, "good");
        Assert.Equal("unpinned", Status(outcomes));
        Assert.True(File.Exists(destination[entry.Name]));
        Assert.NotEmpty(RawCorpus.Verify(destination.Path, manifest));
    }

    [Fact]
    public void PinRecordsWhatTheDownloadMeasured()
    {
        using var destination = Temp();
        File.WriteAllBytes(destination[entry.Name], "good"u8.ToArray());
        var updated = RawCorpus.Pin(destination.Path, write: false, document: One(), path: destination["unused.json"]);
        var item = updated.Dict("files").Dict(updated.Dict("files").Keys.First());
        Assert.Equal(4, item.Long("bytes"));
        Assert.Equal(Good, item.Str("sha256"));
    }

    [Fact]
    public void ThePinReportFitsInOneWorkflowCommand()
    {
        var files = Enumerable.Range(0, 23)
            .Select(i => (object?)PyDict.Of(("name", $"s{i}.rw2"), ("bytes", (long)i), ("sha256", i.ToString("x64", System.Globalization.CultureInfo.InvariantCulture)),
                                            ("status", "downloaded")))
            .ToList();
        string output = Capture(() =>
        {
            RawCorpus.Annotate(PyDict.Of(("files", files)));
            return 0;
        }).Stdout;
        var lines = output.Split('\n').Where(line => line.Trim().Length > 0).ToList();
        string line = Assert.Single(lines);
        Assert.Contains("%0A", line);
    }
}
