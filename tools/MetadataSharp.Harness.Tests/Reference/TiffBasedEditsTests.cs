using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Reference;

// Offline checks for the rule that an edited TIFF-based file must still read in the pinned tool (formerly
// tests/reference/test_tiff_based_edits.py). The tool itself is not run here: its print-outs are authored strings.
public sealed class TiffBasedEditsTests
{
    private const string Source =
        "Exif.Image.Make                              Ascii      12  TestCompany\n"
        + "Exif.Image.StripOffsets                      Long        1  300\n"
        + "Exif.Image.ExifTag                           Long        1  170\n"
        + "Exif.Photo.DateTimeOriginal                  Ascii      20  2020:01:02 03:04:05\n";

    private const string Edited =
        "Exif.Image.Make                              Ascii      12  TestCompany\n"
        + "Exif.Image.StripOffsets                      Long        1  300\n"
        + "Exif.Image.ExifTag                           Long        1  364\n"
        + "Exif.Photo.DateTimeOriginal                  Ascii      20  2021:06:07 08:09:10\n"
        + "Exif.Photo.DateTimeDigitized                 Ascii      20  2021:06:07 08:09:10\n";

    private static HashSet<string> Set(params string[] keys) => new(keys, StringComparer.Ordinal);

    private static readonly HashSet<string> Changed = Set("Exif.Image.ExifTag", "Exif.Photo.DateTimeOriginal");

    /// <summary>A runner that answers by the name of the file, as the Python tests' lambdas did.</summary>
    internal static TextRunner ByName(Dictionary<string, ToolResult> outputs) => (_, path) => outputs[PyPath.Name(path)];

    [Fact]
    public void AnEditThatChangesOnlyWhatItNamesPasses() =>
        Assert.Empty(TiffBasedEdits.Compare(Source, Edited, Changed, ["2021:06:07 08:09:10", "Exif.Photo.DateTimeDigitized"]));

    [Fact]
    public void AKeyTheEditDidNotNameMayNotChange()
    {
        var problems = TiffBasedEdits.Compare(Source, Edited, Set("Exif.Photo.DateTimeOriginal"), []);
        Assert.Single(problems);
        Assert.Contains("Exif.Image.ExifTag changed", problems[0]);
    }

    [Fact]
    public void AMovedStripIsAProblem()
    {
        string moved = Edited.Replace("1  300", "1  420", StringComparison.Ordinal);
        Assert.Contains("Exif.Image.StripOffsets changed", TiffBasedEdits.Compare(Source, moved, Changed, [])[0]);
    }

    [Fact]
    public void AKeyThatIsGoneIsAProblem()
    {
        string gone = string.Concat(PyText.SplitLines(Edited).Where(line => !line.Contains("Make", StringComparison.Ordinal)).Select(line => line + "\n"));
        Assert.Equal(["Exif.Image.Make is no longer printed"], TiffBasedEdits.Compare(Source, gone, Changed, []));
    }

    [Fact]
    public void AKeyThatEndsInAStarNamesAFamilyOfKeys()
    {
        const string source = "Xmp.dc.title  LangAlt  1  old\nIptc.Application2.Caption  String  3  old\n";
        const string edited = "Xmp.xmp.CreatorTool  XmpText  3  new\nIptc.Application2.Caption  String  3  old\n";
        Assert.Empty(TiffBasedEdits.Compare(source, edited, Set("Xmp.*"), []));
        Assert.Equal(["Xmp.dc.title is no longer printed"], TiffBasedEdits.Compare(source, edited, Set("Iptc.*"), []));
    }

    [Fact]
    public void ExpectedTextHasToBePrinted() =>
        Assert.Equal(["expected text is not printed: Xmp.xmp.CreatorTool"], TiffBasedEdits.Compare(Source, Edited, Changed, ["Xmp.xmp.CreatorTool"]));

    [Fact]
    public void ColumnPaddingIsNotADifferenceAndRepeatedKeysKeepTheirOrder()
    {
        Assert.Empty(TiffBasedEdits.Compare("Exif.a.b  Ascii   2  x\nExif.a.b  Ascii   2  y\n", "Exif.a.b Ascii 2 x\nExif.a.b Ascii 2 y\n", Set(), []));
        Assert.Single(TiffBasedEdits.Compare("Exif.a.b Ascii 2 x\nExif.a.b Ascii 2 y\n", "Exif.a.b Ascii 2 y\nExif.a.b Ascii 2 x\n", Set(), []));
    }

    [Fact]
    public void AValueWithALineBreakBelongsToTheKeyItStartedUnder()
    {
        const string source = "Iptc.Application2.Caption  String  9  first line\nUSS second line\nExif.Image.Make  Ascii  2  x\n";
        var rows = TiffBasedEdits.Rows(source);
        Assert.Equal(["Iptc.Application2.Caption", "Exif.Image.Make"], rows.Keys.ToList());
        Assert.Equal(["String 9 first line\nUSS second line"], rows["Iptc.Application2.Caption"]);
        Assert.Equal(["Ascii 2 x"], rows["Exif.Image.Make"]);
        // The continuation is part of the caption: when the caption may change, so may it.
        const string edited = "Iptc.Application2.Caption  String  3  new\nExif.Image.Make  Ascii  2  x\n";
        Assert.Empty(TiffBasedEdits.Compare(source, edited, Set("Iptc.*"), []));
        Assert.Single(TiffBasedEdits.Compare(source, edited, Set(), []));
    }

    [Fact]
    public void ARowNamesSourceEditedChangedKeysAndExpectedText()
    {
        var entry = Assert.Single(TiffBasedEdits.ParseManifest("a.tif\ta-gps.tif\tK.one,K.two\ttext one|two\n"));
        Assert.Equal(("a.tif", "a-gps.tif"), (entry.Source, entry.Edited));
        Assert.True(entry.Changed.SetEquals(["K.one", "K.two"]));
        Assert.Equal(["text one", "two"], entry.Expected);
        entry = Assert.Single(TiffBasedEdits.ParseManifest("a.tif\tb.tif\t\t\n"));
        Assert.Equal(("a.tif", "b.tif", 0, 0), (entry.Source, entry.Edited, entry.Changed.Count, entry.Expected.Count));
    }

    [Fact]
    public void AShortRowOrAnEmptyManifestIsAnError()
    {
        Assert.Equal("ValueError", Assert.Throws<PyException>(() => TiffBasedEdits.ParseManifest("a.tif\tb.tif\tK.one\n")).Type);
        Assert.Equal("ValueError", Assert.Throws<PyException>(() => TiffBasedEdits.ParseManifest("\n")).Type);
    }

    private static (List<(string Name, string Message)> Problems, int Checked, int Skipped) RunCheck(Dictionary<string, ToolResult> outputs, bool skipUnreadable = false)
    {
        using var directory = Temp();
        Write(directory["manifest.tsv"], "a.tif\ta-edit.tif\tExif.Image.ExifTag,Exif.Photo.DateTimeOriginal\t2021:06:07 08:09:10\n");
        return TiffBasedEdits.Check(directory.Path, ["tool"], ByName(outputs), skipUnreadable);
    }

    [Fact]
    public void AFileThatStillReadsPasses()
    {
        var (problems, checkedCount, skipped) = RunCheck(new() { ["a.tif"] = new(0, Source, ""), ["a-edit.tif"] = new(0, Edited, "") });
        Assert.Equal((0, 1, 0), (problems.Count, checkedCount, skipped));
    }

    [Fact]
    public void AnEditedFileTheToolRefusesFails()
    {
        var (problems, _, _) = RunCheck(new() { ["a.tif"] = new(0, Source, ""), ["a-edit.tif"] = new(1, "", "corrupted image metadata") });
        Assert.Equal("a-edit.tif", problems[0].Name);
        Assert.Contains("not read (exit 1): corrupted image metadata", problems[0].Message);
    }

    [Fact]
    public void ASourceTheToolCannotReadIsAFailureNotAPass()
    {
        var (problems, _, _) = RunCheck(new() { ["a.tif"] = new(1, "", "not an image"), ["a-edit.tif"] = new(0, Edited, "") });
        Assert.Equal("a.tif", problems[0].Name);
        (problems, _, _) = RunCheck(new() { ["a.tif"] = new(0, "", ""), ["a-edit.tif"] = new(0, Edited, "") });
        Assert.Contains("the source is not read", problems[0].Message);
    }

    [Fact]
    public void AFileThatWasNeverReadableCanBeLeftOutAndIsCounted()
    {
        var (problems, checkedCount, skipped) = RunCheck(new() { ["a.tif"] = new(1, "", "not an image"), ["a-edit.tif"] = new(1, "", "") }, true);
        Assert.Equal((0, 0, 1), (problems.Count, checkedCount, skipped));
        // Leaving unreadable sources out does not excuse an edited file whose source did read.
        (problems, checkedCount, skipped) = RunCheck(new() { ["a.tif"] = new(0, Source, ""), ["a-edit.tif"] = new(1, "", "damaged") }, true);
        Assert.Equal((1, 1, 0), (problems.Count, checkedCount, skipped));
    }

    [Fact]
    public void ARowWithoutASourceNeedsOnlyTheEditedFileToReadAndPrintWhatWasAdded()
    {
        (List<(string Name, string Message)> Problems, int Checked, int Skipped) Run(Dictionary<string, ToolResult> outputs)
        {
            using var directory = Temp();
            Write(directory["manifest.tsv"], "-\tnew.heic\t\tXmp.xmp.CreatorTool|MetadataSharp\n");
            return TiffBasedEdits.Check(directory.Path, ["tool"], ByName(outputs));
        }

        const string added = "Xmp.xmp.CreatorTool  XmpText  13  MetadataSharp\n";
        // The tool is never asked for a file named "-": the runner knows only the edited one.
        var (problems, checkedCount, skipped) = Run(new() { ["new.heic"] = new(0, added, "") });
        Assert.Equal((0, 1, 0), (problems.Count, checkedCount, skipped));
        (problems, _, _) = Run(new() { ["new.heic"] = new(253, "", "No Exif data found in the file") });
        Assert.Contains("not read (exit 253)", problems[0].Message);
        (problems, _, _) = Run(new() { ["new.heic"] = new(0, "Exif.Image.Make  Ascii  2  x\n", "") });
        Assert.Equal(["expected text is not printed: Xmp.xmp.CreatorTool", "expected text is not printed: MetadataSharp"], problems.Select(problem => problem.Message));
    }

    [Fact]
    public void MainReportsTheOutcomeThroughItsExitCode()
    {
        using var directory = Temp();
        Write(directory["manifest.tsv"], "a.tif\tb.tif\t\t\n");
        var failed = Capture(() => TiffBasedEdits.Execute([directory.Path, "--exiv2-command", "tool -pa"], (_, _) => new(3, "", "refused")));
        var passed = Capture(() => TiffBasedEdits.Execute([directory.Path, "--exiv2-command", "tool -pa"], (_, _) => new(0, Source, "")));
        Assert.Equal((1, 0), (failed.Code, passed.Code));
        Assert.Contains("::error title=TIFF-based edits a.tif::the source is not read (exit 3): refused", failed.Stdout);
        Assert.Contains("::notice title=TIFF-based edits::1 edited files", passed.Stdout);
    }
}
