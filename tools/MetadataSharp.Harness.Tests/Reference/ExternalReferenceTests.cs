using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Reference;

// The rules of the optional external-reference harness (formerly tests/reference/test_harness.py): what a missing
// or mismatched tool means, what counts as a reviewed warning, and what report.json records. No tool runs here.
public sealed class ExternalReferenceTests
{
    private static void AssertRuntimeError(Action action) => Assert.Equal("RuntimeError", Assert.Throws<PyException>(action).Type);

    private static PyDict Report(string folder) => (PyDict)PyJson.Loads(PyFile.ReadText(PyPath.Join(folder, "report.json")))!;

    [Fact]
    public void RequiredMissingToolsFail() => AssertRuntimeError(() => ExternalReference.Available(true, _ => null));

    [Fact]
    public void OptionalMissingToolsSkipExplicitly()
    {
        Assert.False(ExternalReference.Available(false, _ => null));
        Assert.True(ExternalReference.Available(true, _ => "/tool"));
    }

    [Fact]
    public void VersionsAreExactNotLatest()
    {
        ExternalReference.CheckVersion("exiv2", "exiv2 0.27.6\nCopyright", "0.27.6");
        ExternalReference.CheckVersion("exiftool", "12.76\n", "12.76");
        ExternalReference.CheckVersion("dwebp", "1.3.2\n", "1.3.2");
        AssertRuntimeError(() => ExternalReference.CheckVersion("dwebp", "1.6.0", "1.3.2"));
        AssertRuntimeError(() => ExternalReference.CheckVersion("exiftool", "12.77", "12.76"));
        AssertRuntimeError(() => ExternalReference.CheckVersion("exiv2", "garbage", "0.27.6"));
    }

    [Fact]
    public void PlainValuesAreNormalizedWithoutDisplayLabels()
    {
        var result = ExternalReference.ExivValues("Exif.Image.Orientation  Short  1  1\nExif.Image.Artist Ascii 6 Alice\n");
        Assert.Equal(new PyTuple("Short", 1L, "1"), result["Exif.Image.Orientation"]);
        AssertRuntimeError(() => ExternalReference.ExivValues("x Short 1 1\nx Short 1 1"));
        AssertRuntimeError(() => ExternalReference.ExivValues("not a row"));
    }

    [Fact]
    public void OnlyReviewedWarningCategoriesAreAccepted()
    {
        Assert.True(ExternalReference.ClassifyWarning("Missing required JPEG IFD0 tag 0x011a XResolution"));
        Assert.False(ExternalReference.ClassifyWarning("Bad IFD offset"));
        Assert.False(ExternalReference.ClassifyWarning("Missing required JPEG ExifIFD tag 0x9000 ExifVersion"));
    }

    [Fact]
    public void FixtureSpecificPreservationCheckDetectsChanges()
    {
        Assert.Equal([0xFF, 0xDA, .. "scan"u8], ExternalReference.ScanTail([.. "header"u8, 0xFF, 0xDA, .. "scan"u8]));
        AssertRuntimeError(() => ExternalReference.ScanTail("missing marker"u8.ToArray()));
    }

    [Fact]
    public void WarningTagAndNameMustMatch() => Assert.False(ExternalReference.ClassifyWarning("Missing required JPEG IFD0 tag 0x011a YResolution"));

    private static Completed NeverRun(IReadOnlyList<string> command, IReadOnlyDictionary<string, string> environment) =>
        throw new InvalidOperationException("no command is to run: " + string.Join(" ", command));

    [Fact]
    public void OptionalMainRecordsSkipNotPass()
    {
        using var temp = Temp();
        var (code, _, _) = Capture(() => ExternalReference.Execute(["--output", temp.Path], _ => false, NeverRun));
        Assert.Equal(0, code);
        Assert.Equal("skipped", Report(temp.Path).Str("status"));
    }

    [Fact]
    public void RequiredMainRecordsFailureNotSkip()
    {
        using var temp = Temp();
        var (code, _, _) = Capture(() => ExternalReference.Execute(["--required", "--output", temp.Path], _ => throw new PyException("RuntimeError", "Required tools missing"), NeverRun));
        Assert.Equal(1, code);
        Assert.Equal("failed", Report(temp.Path).Str("status"));
    }

    [Fact]
    public void NonzeroToolExitIsPreservedAndFails()
    {
        using var temp = Temp();
        var (code, _, _) = Capture(() => ExternalReference.Execute(["--required", "--output", temp.Path], _ => true,
                                                                    (command, _) => new Completed(["exiv2"], 2, "diagnostic"u8.ToArray(), "error"u8.ToArray(), text: true, strict: true)));
        Assert.Equal(1, code);
        var report = Report(temp.Path);
        Assert.Equal("failed", report.Str("status"));
        Assert.Equal(2L, ((PyDict)report.List("commands")[0]!).Long("exitCode"));
    }

    [Fact]
    public void RepeatedIptcRowsAreNotDeduplicated()
    {
        var rows = ExternalReference.IptcRows("Iptc.Application2.Keywords String 5 alpha\nIptc.Application2.Keywords String 5 alpha");
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0], rows[1]);
    }

    [Fact]
    public void MalformedIptcRowsFail() => AssertRuntimeError(() => ExternalReference.IptcRows("not a row"));
}

// Offline rules of the character-set comparison: the two prints, the round trip and the iconv skip (formerly
// tests/parity/test_charset_parity.py). No Exiv2 binary and no fixture generator runs here: a fake runner answers for
// both, which is what makes the gate's own rules - what counts as a difference, and what a build without iconv does
// - visible without a network.
public sealed class CharsetParityTests : IDisposable
{
    private const string PrintedText =
        "Exif.Image.Make                          MetadataSharp charset fixture\n"
        + "Exif.Image.XPTitle                       Grüße — 日本語\n"
        + "Exif.Photo.ExifVersion                   0230\n";

    private const string CommentText = "Grüße aus Köln";
    private const string ConvertedText = "Setting Exif UNICODE user comment to \"" + CommentText + "\"\n";

    private readonly TempDirectory temporary = Temp();
    private readonly string work;
    private readonly List<string> rows =
    [
        "charset-00.jpg\twindows-1252\t1252\tExif.Photo.UserComment\tGrüße aus Köln",
        "charset-00.jpg\twindows-1252\t1252\tExif.Image.XPTitle\tGrüße — 日本語",
        "charset-00.jpg\twindows-1252\t1252\twritten\tGrüße aus Köln|Grüße — 日本語",
    ];

    public CharsetParityTests()
    {
        work = temporary.Path;
        File.WriteAllBytes(PyPath.Join(work, "charset-00.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        WriteRows(rows);
    }

    public void Dispose() => temporary.Dispose();

    private void WriteRows(IEnumerable<string> lines) => Write(PyPath.Join(work, "ours.tsv"), string.Join("\n", lines) + "\n");

    private (int Result, List<List<string>> Commands) RunGate(string toolOutput = PrintedText, int toolCode = 0, string toolErrors = "",
                                                             string convertedText = ConvertedText, int convertCode = 0, string convertErrors = "")
    {
        var commands = new List<List<string>>();
        ToolResult Runner(IReadOnlyList<string> command, int timeout = 120)
        {
            commands.Add([.. command]);
            if (command[0] == "fixture") return new(0, "", "");
            if (command.Contains("fc")) return new(convertCode, convertedText, convertErrors);
            return new(toolCode, toolOutput, toolErrors);
        }
        var (result, _, _) = Capture(() => CharsetParity.Execute([work, "--exiv2", "exiv2", "--fixture", "fixture"], Runner));
        return (result, commands);
    }

    private static bool Asked(List<List<string>> commands, params string[] expected) => commands.Any(command => command.SequenceEqual(expected));

    [Fact]
    public void TheToolIsAskedWhatNMeansForTheCommentAndPrintsTheTitleTranslated()
    {
        var (result, commands) = RunGate();
        Assert.Equal(0, result);
        // -n belongs to the fc action: the tool converts a copy and says what it read in its verbose line, which is
        // what has to equal this library's text. The title is read translated.
        Assert.True(Asked(commands, "exiv2", "-v", "-n", "windows-1252", "fc", PyPath.Join(work, "charset-00.jpg.fc.jpg")));
        // Only the title is printed translated: the comment's bytes are a code page in these files.
        Assert.True(Asked(commands, "exiv2", "-PEkt", "-K", "Exif.Image.XPTitle", PyPath.Join(work, "charset-00.jpg")));
        Assert.False(File.Exists(PyPath.Join(work, "charset-00.jpg.fc.jpg")));
    }

    private void UseCharset(string name)
    {
        string codePage = name == "windows-1258" ? "1258" : "1252";
        WriteRows(rows.Select(row => row.Replace("windows-1252", name, StringComparison.Ordinal).Replace("windows-1258", name, StringComparison.Ordinal)
                                        .Replace("\t1252\t", "\t" + codePage + "\t", StringComparison.Ordinal).Replace("\t1258\t", "\t" + codePage + "\t", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheOneCodePageTheToolDoesNotFlushIsComparedWithoutItsLastCharacter()
    {
        // glibc's windows-1258 converter holds the final character until a flush the tool never does.
        const string shortText = "Setting Exif UNICODE user comment to \"Grüße aus Köl\"\n";
        UseCharset("windows-1258");
        Assert.Equal(0, RunGate(convertedText: shortText).Result);
        // The same text one character short in any other code page is still a difference.
        UseCharset("windows-1252");
        Assert.Equal(1, RunGate(convertedText: shortText).Result);
    }

    [Fact]
    public void ACommentTheToolReadsDifferentlyFails() => Assert.Equal(1, RunGate(convertedText: "Setting Exif UNICODE user comment to \"Gruesse\"\n").Result);

    [Fact]
    public void ATitleTheToolReadsDifferentlyFails() => Assert.Equal(1, RunGate(toolOutput: PrintedText.Replace("Grüße — 日本語", "Gruesse", StringComparison.Ordinal)).Result);

    [Fact]
    public void AWrittenTextThatDoesNotReadBackFailsBeforeTheToolIsAsked()
    {
        rows[0] = rows[0].Replace("\tGrüße aus Köln", "\tGruse", StringComparison.Ordinal);
        WriteRows(rows);
        Assert.Equal(1, RunGate().Result);
    }

    [Fact]
    public void AManifestThatIsMissingOrUnreadableIsReportedAndNotATraceback()
    {
        File.Delete(PyPath.Join(work, "ours.tsv"));
        Assert.Equal(1, RunGate().Result);
        File.WriteAllBytes(PyPath.Join(work, "ours.tsv"), [.. "charset-00.jpg\twindows-1252\t1252\tExif.Image.XPTitle\t"u8, 0xFF, (byte)'\n']);
        Assert.Equal(1, RunGate().Result);
    }

    [Fact]
    public void ABuildWithoutIconvSaysSoRatherThanPassingQuietly() =>
        Assert.Equal(0, RunGate(convertedText: "", convertCode: 1, convertErrors: "exiv2: Cannot convert text encoding from 'windows-1252' to 'UTF-8'").Result);

    [Fact]
    public void AConversionThatSaysNothingFailsTheGate() =>
        Assert.Equal(1, RunGate(convertedText: "", convertCode: 1, convertErrors: "exiv2: Failed to open the file").Result);
}
