using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Reference;

// Offline checks for the comparer of MakerNote edits; a runner stands in for the tool and the fixture (formerly
// tests/reference/test_makernote_parity.py).
public sealed class MakerNoteParityTests
{
    private static PrintOut Side(IEnumerable<(string Key, string[] Values)> keys, int code = 0, params string[] warnings)
    {
        var values = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, items) in keys) values[key] = [.. items];
        return new(values, code, [.. warnings]);
    }

    private static readonly PrintOut Before = Side([("Exif.Canon.OwnerName", ["Old"]), ("Exif.Canon.ModelID", ["1"])]);

    [Fact]
    public void TheValueSetAndTheRestKeptIsOk() =>
        Assert.Equal(("ok", ""), MakerNoteParity.Verdict(Before, Side([("Exif.Canon.OwnerName", ["New"]), ("Exif.Canon.ModelID", ["1"])]), "Exif.Canon.OwnerName", "New"));

    [Fact]
    public void AValueTheToolDoesNotPrintIsNamed() =>
        Assert.Equal("value the tool does not print",
                     MakerNoteParity.Verdict(Before, Side([("Exif.Canon.OwnerName", ["Old"]), ("Exif.Canon.ModelID", ["1"])]), "Exif.Canon.OwnerName", "New").Verdict);

    [Fact]
    public void AnotherKeyThatChangedWentOrCameIsNamed()
    {
        var changed = Side([("Exif.Canon.OwnerName", ["New"]), ("Exif.Canon.ModelID", ["2"])]);
        var gone = Side([("Exif.Canon.OwnerName", ["New"])]);
        var came = Side([("Exif.Canon.OwnerName", ["New"]), ("Exif.Canon.ModelID", ["1"]), ("Exif.Canon.0x0001", ["3"])]);
        Assert.Equal("another key changed", MakerNoteParity.Verdict(Before, changed, "Exif.Canon.OwnerName", "New").Verdict);
        Assert.Equal("another key gone", MakerNoteParity.Verdict(Before, gone, "Exif.Canon.OwnerName", "New").Verdict);
        Assert.Equal("a key appeared", MakerNoteParity.Verdict(Before, came, "Exif.Canon.OwnerName", "New").Verdict);
    }

    [Fact]
    public void AKeyTheToolNeverPrintedAnExitCodeAndAWarningAreNamed()
    {
        var after = Side([("Exif.Canon.OwnerName", ["New"]), ("Exif.Canon.ModelID", ["1"])], 1);
        var warned = Side([("Exif.Canon.OwnerName", ["New"]), ("Exif.Canon.ModelID", ["1"])], 0, "Warning: offset");
        Assert.Equal("key the tool does not print", MakerNoteParity.Verdict(Before, after, "Exif.Canon.Nothing", "1").Verdict);
        Assert.Equal("exit code changed", MakerNoteParity.Verdict(Before, after, "Exif.Canon.OwnerName", "New").Verdict);
        Assert.Equal(("new warning", "Warning: offset"), MakerNoteParity.Verdict(Before, warned, "Exif.Canon.OwnerName", "New"));
    }

    [Fact]
    public void ANumberHasToBeOneFieldOfTheValueAndNotAPartOfOne()
    {
        var before = Side([("Exif.MinoltaCsNew.SomeArray", ["1 2 3 4"])]);
        var after = Side([("Exif.MinoltaCsNew.SomeArray", ["1 12345 3 4"])]);
        Assert.Equal("value the tool does not print", MakerNoteParity.Verdict(before, after, "Exif.MinoltaCsNew.SomeArray", "1234", "number").Verdict);
        Assert.Equal(("ok", ""), MakerNoteParity.Verdict(before, after, "Exif.MinoltaCsNew.SomeArray", "12345", "number"));
    }

    [Fact]
    public void Exiv2CarriedSkipsNeedAStructuredRelocationRefusal()
    {
        Assert.True(MakerNoteParity.DocumentedCopySkip("UnsafeMakerNoteRelocation", "unknown address-like tag 0x002b"));
        Assert.True(MakerNoteParity.DocumentedCopySkip("MakerNoteCipherKeysUnavailable", "cipher key missing"));
        Assert.False(MakerNoteParity.DocumentedCopySkip("", "unknown"));
        Assert.False(MakerNoteParity.DocumentedCopySkip("UnsafeMakerNoteRelocation", ""));
        Assert.False(MakerNoteParity.DocumentedCopySkip("CopyRefused", "writer failed"));
    }

    [Fact]
    public void ARunCountsWhatPassesAndFailsOnWhatDoesNot()
    {
        using var folder = Temp();
        string corpus = folder["corpus"], work = folder["work"];
        Directory.CreateDirectory(corpus);
        File.WriteAllBytes(PyPath.Join(corpus, "a.jpg"), "x"u8.ToArray());

        ToolResult Runner(IReadOnlyList<string> command, int timeout = 120)
        {
            if (command.Contains("makernote-parity"))
            {
                File.WriteAllBytes(PyPath.Join(work, "s00000.1.ours.jpg"), "y"u8.ToArray());
                File.WriteAllBytes(PyPath.Join(work, "s00000.2.ours.jpg"), "z"u8.ToArray());
                Write(PyPath.Join(work, "ours.tsv"),
                      "s00000\t1\tsame\tExif.Canon.OwnerName\twritten\tNew\n"
                      + "s00000\t2\tgrow\tExif.Canon.OwnerName\twritten\tLonger\n"
                      + "s00000\t3\tgrow\tExif.Canon.ImageType\trefused UnsupportedFeature\tG\n");
                return new(0, "", "");
            }
            string path = command[^1];
            string value = path.EndsWith(".1.ours.jpg", StringComparison.Ordinal) ? "New" : "Old";
            return new(0, $"Exif.Canon.OwnerName   {value}\n", $"Warning: {path} odd\n");
        }

        // This test deliberately constructs a failing parity result. Keep its simulated GitHub annotations from
        // becoming real errors on the workflow run that executes the test suite.
        var (code, _, _) = Capture(() => MakerNoteParity.Execute([corpus, work, "--exiv2", "exiv2", "--fixture", "fixture"], Runner, (_, _, _, _, _) => { }));
        Assert.Equal(1, code);
        Assert.False(File.Exists(PyPath.Join(work, "s00000.1.ours.jpg")));
    }

    [Fact]
    public void ThePrintOutKeepsValuesWithSpacesAndDropsThePathFromWarnings()
    {
        var (found, code, warnings) = MakerNoteParity.Printed("exiv2", "/w/a.jpg", (_, _) => new(0, "Exif.Canon.OwnerName   A  B\nExif.Canon.ModelID\n", "Warning: /w/a.jpg: bad\n"));
        Assert.Equal(["Exif.Canon.OwnerName", "Exif.Canon.ModelID"], found.Keys.ToList());
        Assert.Equal(["A  B"], found["Exif.Canon.OwnerName"]);
        Assert.Equal([""], found["Exif.Canon.ModelID"]);
        Assert.Equal(0, code);
        Assert.Equal(["Warning: : bad"], warnings);
    }
}

// Offline checks for the comparer of CRW edits and the authored MRW / RAF files (formerly
// tests/reference/test_crw_parity.py).
public sealed class CrwParityTests
{
    /// <summary>Stands in for exiv2: prints what the test says a file holds.</summary>
    private sealed class Tool(Dictionary<string, (string Key, string Value)[]> keys, Dictionary<string, string> comments)
    {
        public ToolResult Run(IReadOnlyList<string> command, int timeout = 120)
        {
            string name = PyPath.Name(command[^1]);
            if (command[1] == "-pc") return new(0, comments.GetValueOrDefault(name, "") + "\n", "");
            return new(0, string.Concat(keys.GetValueOrDefault(name, []).Select(pair => $"{pair.Key}  {pair.Value}\n")), "");
        }
    }

    private static TempDirectory Work(params string[][] rows)
    {
        var directory = Temp();
        Write(directory["ours.tsv"], string.Join("\n", rows.Select(row => string.Join("\t", row))) + "\n");
        return directory;
    }

    private static Dictionary<(string, string), long> Counts(Counter counts) =>
        counts.Items().ToDictionary(pair => (((PyTuple)pair.Key).S(0), ((PyTuple)pair.Key).S(1)), pair => pair.Value);

    private static readonly Dictionary<string, string> Names = new() { ["s00000"] = "a.crw" };

    [Fact]
    public void ATagAndACommentReadBackAreOkAndARefusalIsCounted()
    {
        using var work = Work(["s00000", "1", "exif", "Exif.Image.Model", "written", "New"],
                              ["s00000", "2", "comment", "Comment", "written", "hello"],
                              ["s00000", "3", "canon", "Exif.Canon.OwnerName", "refused TagNotEditable", "no"]);
        var tool = new Tool(new()
        {
            ["s00000.crw"] = [("Exif.Image.Model", "Old")],
            ["s00000.1.ours.crw"] = [("Exif.Image.Model", "New")],
            ["s00000.2.ours.crw"] = [("Exif.Image.Model", "Old")],
        }, new() { ["s00000.2.ours.crw"] = "hello" });
        var (counts, _) = CrwParity.Check(work.Path, Names, "exiv2", tool.Run);
        Assert.Equal(new Dictionary<(string, string), long> { [("exif", "ok")] = 1, [("comment", "ok")] = 1, [("canon", "refused TagNotEditable")] = 1 }, Counts(counts));
    }

    [Fact]
    public void ACommentTheToolDoesNotPrintAndAKeyItLostAreNamed()
    {
        using var work = Work(["s00000", "1", "comment", "Comment", "written", "hello"],
                              ["s00000", "2", "comment", "Comment", "written", "hello"]);
        var tool = new Tool(new()
        {
            ["s00000.crw"] = [("Exif.Image.Model", "Old")],
            ["s00000.1.ours.crw"] = [("Exif.Image.Model", "Old")],
        }, new() { ["s00000.1.ours.crw"] = "other", ["s00000.2.ours.crw"] = "hello" });
        var (counts, _) = CrwParity.Check(work.Path, Names, "exiv2", tool.Run);
        Assert.Equal(new Dictionary<(string, string), long> { [("comment", "comment the tool does not print")] = 1, [("comment", "another key gone")] = 1 }, Counts(counts));
    }

    [Fact]
    public void TheMrwFileHoldsTheTiffFileInItsTtwBlock()
    {
        byte[] tiff = [.. "II*\0"u8, .. new byte[10]];
        byte[] data = RawContainerFixtures.Mrw(tiff);
        Assert.Equal("\0MRM"u8.ToArray(), data[..4]);
        int at = 8 + 8 + (int)PyStruct.U(">I", data, 12);
        Assert.Equal("\0TTW"u8.ToArray(), data[at..(at + 4)]);
        Assert.Equal(tiff, data[(at + 8)..(at + 8 + (int)PyStruct.U(">I", data, at + 4))]);
        Assert.Equal(at + 8 + tiff.Length, 8 + PyStruct.U(">I", data, 4));
    }

    [Fact]
    public void TheRafHeaderPlacesTheJpegFileAndTheTiffStructure()
    {
        byte[] jpeg = [0xFF, 0xD8, .. new byte[20], 0xFF, 0xD9];
        byte[] tiff = RawContainerFixtures.TiffStructure();
        byte[] data = RawContainerFixtures.Raf(jpeg, tiff);
        Assert.Equal("FUJIFILM"u8.ToArray(), data[..8]);
        var place = PyStruct.UnpackFrom(">II", data, 84);
        int start = (int)(long)place[0], length = (int)(long)place[1];
        Assert.Equal(jpeg, data[start..(start + length)]);
        place = PyStruct.UnpackFrom(">II", data, 100);
        (start, length) = ((int)(long)place[0], (int)(long)place[1]);
        Assert.Equal(tiff, data[start..(start + length)]);
        Assert.Equal("raf structure\0"u8.ToArray(), tiff[(int)PyStruct.U("<I", tiff, 18)..]);
    }
}
