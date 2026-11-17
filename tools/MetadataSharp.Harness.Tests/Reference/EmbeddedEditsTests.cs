using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Reference;

// Offline checks for the rule that the pinned tool prints the profile and the comment a file was given (formerly
// tests/reference/test_embedded_edits.py). The tool itself is not run here: its print-outs are authored bytes.
public sealed class EmbeddedEditsTests
{
    private static readonly byte[] Profile = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
    private static readonly byte[] Other = Enumerable.Range(0, 200).Reverse().Select(i => (byte)i).ToArray();
    private static readonly byte[] Keys = "Exif.Image.Artist  Ascii  6  Alice\nXmp.xmp.CreatorTool  XmpText  13  MetadataSharp\n"u8.ToArray();
    private static readonly byte[] Hi = "Hi"u8.ToArray();
    private static readonly byte[] HiLine = "Hi\n"u8.ToArray();
    private static readonly byte[] NewLine = "\n"u8.ToArray();

    private static BytesResult Ok(byte[] output) => new(0, output, "");

    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private static readonly string[] Named = ["Exif.Image.InterColorProfile"];

    [Fact]
    public void ARowNamesSourceEditedProfileAndComment()
    {
        var entries = EmbeddedEdits.ParseManifest("a.jpg\tb.jpg\tp.icc\t4869\n\na.jpg\tc.jpg\t-\t=\na.jpg\td.jpg\t=\t-\n");
        Assert.Equal(3, entries.Count);
        Assert.Equal(("a.jpg", "b.jpg", "p.icc", (string?)null), (entries[0].Source, entries[0].Edited, entries[0].Profile, entries[0].CommentMode));
        Assert.Equal(Hi, entries[0].Comment);
        Assert.Equal(("a.jpg", "c.jpg", "-", "="), (entries[1].Source, entries[1].Edited, entries[1].Profile, entries[1].CommentMode));
        Assert.Equal(("a.jpg", "d.jpg", "=", "-"), (entries[2].Source, entries[2].Edited, entries[2].Profile, entries[2].CommentMode));
        Assert.Null(entries[1].Comment);
    }

    [Theory]
    [InlineData("a.jpg\tb.jpg\tp.icc\n")]
    [InlineData("a.jpg\tb.jpg\t\t-\n")]
    [InlineData("a.jpg\tb.jpg\t-\tHi\n")]
    [InlineData("\n")]
    public void AShortRowAnEmptyFieldACommentThatIsNoHexadecimalOrAnEmptyManifestIsAnError(string text) =>
        Assert.Equal("ValueError", Assert.Throws<PyException>(() => EmbeddedEdits.ParseManifest(text)).Type);

    private static List<string> Compare(BytesResult? profile = null, BytesResult? comment = null, BytesResult? keys = null, BytesResult? sourceKeys = null) =>
        EmbeddedEdits.Compare(Profile, Hi, sourceKeys ?? Ok(Keys), profile ?? Ok(Profile), comment ?? Ok(HiLine), keys ?? Ok(Keys));

    [Fact]
    public void TheProfileTheCommentAndTheUnchangedKeysPass() => Assert.Empty(Compare());

    [Fact]
    public void TheProfileHasToBePrintedByteForByte()
    {
        Assert.Equal(["-pC prints 199 bytes 000102030405060708090a0b0c0d0e0f1011121314151617..., expected 200 bytes 000102030405060708090a0b0c0d0e0f1011121314151617..."],
                     Compare(profile: Ok(Profile[..^1])));
        Assert.Single(Compare(profile: Ok([])));
        Assert.Single(Compare(profile: Ok(Other)));
    }

    [Fact]
    public void TheCommentIsWhatStandsBeforeTheOneLineBreakTheToolAdds()
    {
        Assert.Empty(Compare(comment: Ok(HiLine)));
        Assert.Equal(["-pc prints 3 bytes 48690a, expected 2 bytes 4869"], Compare(comment: Ok("Hi\n\n"u8.ToArray())));
        Assert.Equal(["-pc prints 0 bytes , expected 2 bytes 4869"], Compare(comment: Ok(NewLine)));
        Assert.Equal(["-pc prints no line: 2 bytes 4869"], Compare(comment: Ok(Hi)));
    }

    [Fact]
    public void NoProfileAndNoCommentAreAnEmptyPrintOutAndAnEmptyLine()
    {
        Assert.Empty(EmbeddedEdits.Compare([], [], Ok(Keys), Ok([]), Ok(NewLine), Ok(Keys)));
        Assert.Equal(2, EmbeddedEdits.Compare([], [], Ok(Keys), Ok(Profile), Ok(HiLine), Ok(Keys)).Count);
    }

    [Fact]
    public void ARunThatDoesNotExitWithZeroIsAProblemWhateverItPrinted()
    {
        Assert.Equal(["-pC does not read the file (exit 1): Exiv2 exception"], Compare(profile: new(1, Profile, "Exiv2 exception\n")));
        Assert.Equal(["-pc does not read the file (exit 1): Exiv2 exception"], Compare(comment: new(1, HiLine, "Exiv2 exception\n")));
    }

    [Fact]
    public void TheKeysHaveToBePrintedAsForTheSourceWithTheSameExitCode()
    {
        byte[] fewer = EditChecks.SplitLinesKeepEnds(Keys)[1];
        Assert.Equal(["-pa prints 1 line(s) fewer and 0 more than for the source: Exif.Image.Artist  Ascii  6  Alice"], Compare(keys: Ok(fewer)));
        Assert.Equal(["-pa prints 0 line(s) fewer and 1 more than for the source: Iptc.Application2.Caption  String  1  x"],
                     Compare(keys: Ok(Cat(Keys, "Iptc.Application2.Caption  String  1  x\n"u8.ToArray()))));
        Assert.Equal(["-pa exits with 253, with 0 for the source"], Compare(keys: new(253, Keys, "")));
        // A file without Exif, IPTC or XMP data: the tool prints nothing and does not exit with 0, before and after.
        var none = new BytesResult(253, [], "No Exif data found in the file");
        Assert.Empty(Compare(keys: none, sourceKeys: none));
    }

    [Fact]
    public void AKeyThatHoldsTheProfileIsLeftOutOfTheComparisonAndCounted()
    {
        byte[] key = "Exif.Image.InterColorProfile  Undefined  200  0 1 2 3\n"u8.ToArray();
        // Without the option the line is one more than the source has.
        Assert.Single(Compare(keys: Ok(Cat(Keys, key))));
        Assert.Empty(EmbeddedEdits.Compare(Profile, Hi, Ok(Keys), Ok(Profile), Ok(HiLine), Ok(Cat(key, Keys)), Named));
        Assert.Empty(EmbeddedEdits.Compare(Profile, Hi, Ok(Cat(key, Keys)), Ok(Profile), Ok(HiLine), Ok(Cat(Keys, key)), Named));
        Assert.Empty(EmbeddedEdits.Compare([], Hi, Ok(Cat(Keys, key)), Ok([]), Ok(HiLine), Ok(Keys), Named));
        Assert.Equal(["-pa prints 0 line(s) of Exif.Image.InterColorProfile, expected 1"],
                     EmbeddedEdits.Compare(Profile, Hi, Ok(Keys), Ok(Profile), Ok(HiLine), Ok(Keys), Named));
        Assert.Equal(["-pa prints 1 line(s) of Exif.Image.InterColorProfile, expected 0"],
                     EmbeddedEdits.Compare([], Hi, Ok(Keys), Ok([]), Ok(HiLine), Ok(Cat(Keys, key)), Named));
        Assert.Equal(["-pa prints 2 line(s) of Exif.Image.InterColorProfile, expected 1"],
                     EmbeddedEdits.Compare(Profile, Hi, Ok(Keys), Ok(Profile), Ok(HiLine), Ok(Cat(key, Keys, key)), Named));
        // Another line is still a difference, and a key that only begins like the named one is another line.
        byte[] other = "Exif.Image.InterColorProfileX  Undefined  1  0\n"u8.ToArray();
        Assert.Single(EmbeddedEdits.Compare(Profile, Hi, Ok(Keys), Ok(Profile), Ok(HiLine), Ok(Cat(Keys, key, other)), Named));
        // Where the tool does not exit with 0, before and after, the lines are not counted.
        Assert.Empty(EmbeddedEdits.Compare(Profile, Hi, new(253, Keys, ""), Ok(Profile), Ok(HiLine), new(253, Keys, ""), Named));
    }

    [Fact]
    public void OneOfSeveralKeysCounts()
    {
        string[] named = ["Exif.Image.InterColorProfile", "Exif.PanasonicRaw.0x8773"];
        byte[] key = "Exif.PanasonicRaw.0x8773  Undefined  200  0 1 2 3\n"u8.ToArray();
        Assert.Empty(EmbeddedEdits.Compare(Profile, Hi, Ok(Keys), Ok(Profile), Ok(HiLine), Ok(Cat(Keys, key)), named));
        Assert.Equal(["-pa prints 0 line(s) of Exif.Image.InterColorProfile or Exif.PanasonicRaw.0x8773, expected 1"],
                     EmbeddedEdits.Compare(Profile, Hi, Ok(Keys), Ok(Profile), Ok(HiLine), Ok(Keys), named));
    }

    [Fact]
    public void TheLineBeforeTheProfileOfAnOrfOrRw2FileIsNoPartOfIt()
    {
        byte[] orf = "ORF IMAGE\n"u8.ToArray(), rw2 = "RW2 IMAGE\n"u8.ToArray();
        Assert.Equal(Profile, EmbeddedEdits.PrintedProfile("a.orf", Cat(orf, Profile)));
        Assert.Equal([], EmbeddedEdits.PrintedProfile("a.ORF", orf));
        Assert.Equal(Profile, EmbeddedEdits.PrintedProfile("a.rw2", Cat(rw2, Profile)));
        // Only of such a file, and only its own line.
        Assert.Equal(Cat(orf, Profile), EmbeddedEdits.PrintedProfile("a.tif", Cat(orf, Profile)));
        Assert.Equal(Cat(rw2, Profile), EmbeddedEdits.PrintedProfile("a.orf", Cat(rw2, Profile)));
        Assert.Equal(Profile, EmbeddedEdits.PrintedProfile("a.orf", Profile));
    }

    [Fact]
    public void TheSameLinesInAnotherOrderAreADifference()
    {
        byte[] swapped = Cat([.. EditChecks.SplitLinesKeepEnds(Keys).AsEnumerable().Reverse()]);
        Assert.Equal(["-pa prints 0 line(s) fewer and 0 more than for the source: the same lines in another order"], Compare(keys: Ok(swapped)));
    }

    private static readonly string[] Options = ["-pC", "-pc", "-pa"];

    /// <summary>outputs: by file name, the three results of -pC, -pc and -pa.</summary>
    private static (List<(string Name, string Message)> Problems, int Checked, int Skipped) RunCheck(string row, Dictionary<string, BytesResult[]> outputs, bool skipUnreadable = false)
    {
        var asked = new List<string>();
        using var directory = Temp();
        Write(directory["manifest.tsv"], row);
        File.WriteAllBytes(directory["p.icc"], Profile);
        var result = EmbeddedEdits.Check(directory.Path, "tool", (command, path) =>
        {
            asked.Add(command[0]);
            return outputs[PyPath.Name(path)][Array.IndexOf(Options, command[1])];
        }, skipUnreadable);
        Assert.All(asked, tool => Assert.Equal("tool", tool));
        return result;
    }

    private static readonly BytesResult[] SourceOutputs = [Ok(Other), Ok("Old\n"u8.ToArray()), Ok(Keys)];

    [Fact]
    public void AGivenProfileAndCommentPass()
    {
        var (problems, checkedCount, skipped) = RunCheck("a.jpg\tb.jpg\tp.icc\t4869\n", new() { ["a.jpg"] = SourceOutputs, ["b.jpg"] = [Ok(Profile), Ok(HiLine), Ok(Keys)] });
        Assert.Equal((0, 1, 0), (problems.Count, checkedCount, skipped));
    }

    [Fact]
    public void TheEqualsSignIsWhatTheToolPrintsForTheSource()
    {
        const string row = "a.jpg\tb.jpg\t=\t=\n";
        var (problems, checkedCount, skipped) = RunCheck(row, new() { ["a.jpg"] = SourceOutputs, ["b.jpg"] = SourceOutputs });
        Assert.Equal((0, 1, 0), (problems.Count, checkedCount, skipped));
        (problems, _, _) = RunCheck(row, new() { ["a.jpg"] = SourceOutputs, ["b.jpg"] = [Ok(Profile), Ok(HiLine), Ok(Keys)] });
        Assert.Equal(["b.jpg", "b.jpg"], problems.Select(problem => problem.Name));
        Assert.Contains("-pC prints", problems[0].Message);
        Assert.Contains("-pc prints 2 bytes 4869, expected 3 bytes 4f6c64", problems[1].Message);
    }

    [Fact]
    public void TheMinusSignIsNone()
    {
        const string row = "a.jpg\tb.jpg\t-\t-\n";
        var (problems, checkedCount, skipped) = RunCheck(row, new() { ["a.jpg"] = SourceOutputs, ["b.jpg"] = [Ok([]), Ok(NewLine), Ok(Keys)] });
        Assert.Equal((0, 1, 0), (problems.Count, checkedCount, skipped));
        (problems, _, _) = RunCheck(row, new() { ["a.jpg"] = SourceOutputs, ["b.jpg"] = SourceOutputs });
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void ASourceTheToolCannotReadIsAFailureNotAPass()
    {
        BytesResult[] unread = [new(1, [], "not an image"), Ok(NewLine), Ok(Keys)];
        var (problems, checkedCount, skipped) = RunCheck("a.jpg\tb.jpg\tp.icc\t4869\n", new() { ["a.jpg"] = unread, ["b.jpg"] = [Ok(Profile), Ok(HiLine), Ok(Keys)] });
        Assert.Equal([("a.jpg", "the source is not read with -pC")], problems);
        Assert.Equal((0, 0), (checkedCount, skipped));
    }

    [Fact]
    public void AFileThatWasNeverReadableCanBeLeftOutAndIsCounted()
    {
        const string row = "a.jpg\tb.jpg\tp.icc\t4869\n";
        BytesResult[] unread = [Ok([]), new(1, [], "not an image"), Ok(Keys)];
        var (problems, checkedCount, skipped) = RunCheck(row, new() { ["a.jpg"] = unread, ["b.jpg"] = unread }, true);
        Assert.Equal((0, 0, 1), (problems.Count, checkedCount, skipped));
        // Leaving unreadable sources out does not excuse an edited file whose source did read.
        (problems, checkedCount, skipped) = RunCheck(row, new() { ["a.jpg"] = SourceOutputs, ["b.jpg"] = unread }, true);
        Assert.Equal((2, 1, 0), (problems.Count, checkedCount, skipped));
    }

    [Fact]
    public void TheProfileKeysAndTheFormatLineReachEveryRow()
    {
        byte[] key = "Exif.Image.InterColorProfile  Undefined  200  0 1 2 3\n"u8.ToArray();
        byte[] orf = "ORF IMAGE\n"u8.ToArray();
        var outputs = new Dictionary<string, BytesResult[]>
        {
            ["a.orf"] = [Ok(orf), Ok(NewLine), Ok(Keys)],
            ["b.orf"] = [Ok(Cat(orf, Profile)), Ok(NewLine), Ok(Cat(Keys, key))],
            ["c.orf"] = [Ok(orf), Ok(NewLine), Ok(Keys)],
        };
        var asked = new List<string>();
        BytesResult Runner(IReadOnlyList<string> command, string path)
        {
            asked.Add(command[1]);
            return outputs[PyPath.Name(path)][Array.IndexOf(Options, command[1])];
        }
        using var directory = Temp();
        Write(directory["manifest.tsv"], "a.orf\tb.orf\tp.icc\t=\nb.orf\tc.orf\t-\t=\n");
        File.WriteAllBytes(directory["p.icc"], Profile);
        var (problems, checkedCount, skipped) = EmbeddedEdits.Check(directory.Path, "tool", Runner, profileKeys: Named);
        Assert.Equal((0, 2, 0), (problems.Count, checkedCount, skipped));
        (problems, _, _) = EmbeddedEdits.Check(directory.Path, "tool", Runner);
        Assert.Equal(["-pC", "-pc", "-pa"], asked.Take(3));
        Assert.Equal(["b.orf", "c.orf"], problems.Select(problem => problem.Name));
    }

    [Fact]
    public void TheSourceIsReadOnceForAllItsRows()
    {
        var calls = new List<(string Option, string Name)>();
        using var directory = Temp();
        Write(directory["manifest.tsv"], "a.jpg\tb.jpg\t-\t-\na.jpg\tc.jpg\t=\t=\n");
        var (problems, checkedCount, skipped) = EmbeddedEdits.Check(directory.Path, "tool", (command, path) =>
        {
            calls.Add((command[1], PyPath.Name(path)));
            return Ok(command[1] == "-pc" ? NewLine : []);
        });
        Assert.Equal((0, 2, 0), (problems.Count, checkedCount, skipped));
        Assert.Equal(3, calls.Count(call => call.Name == "a.jpg"));
        Assert.Equal(9, calls.Count);
    }

    [Fact]
    public void MainReportsTheOutcomeThroughItsExitCode()
    {
        using var directory = Temp();
        Write(directory["manifest.tsv"], "a.jpg\tb.jpg\t-\t-\n");
        var passed = Capture(() => EmbeddedEdits.Execute([directory.Path, "--exiv2", "tool"], (command, _) => Ok(command[1] == "-pc" ? NewLine : [])));
        var failed = Capture(() => EmbeddedEdits.Execute([directory.Path, "--exiv2", "tool", "--title", "Corpus"],
                                                         (command, path) => PyPath.Name(path) == "a.jpg" ? Ok(command[1] == "-pc" ? NewLine : []) : new(1, [], "damaged")));
        Assert.Equal((0, 1), (passed.Code, failed.Code));
        Assert.Contains("::notice title=JPEG profile and comment edits::1 edited files: the pinned tool prints the profile (-pC)", passed.Stdout);
        Assert.Contains("::error title=Corpus b.jpg::-pC does not read the file (exit 1): damaged", failed.Stdout);
        Assert.Contains("3 problem(s) in 1 edited file(s)", failed.Stdout);
    }

    [Fact]
    public void MainNamesTheProfileKeysItWasGiven()
    {
        using var directory = Temp();
        Write(directory["manifest.tsv"], "a.tif\tb.tif\t-\t=\n");
        var passed = Capture(() => EmbeddedEdits.Execute([directory.Path, "--exiv2", "tool", "--profile-key", "Exif.Image.InterColorProfile", "--profile-key", "Exif.X.Y"],
                                                         (command, _) => Ok(command[1] == "-pc" ? NewLine : [])));
        Assert.Equal(0, passed.Code);
        Assert.Contains("for the source, but for Exif.Image.InterColorProfile and Exif.X.Y, which it prints where there is a profile and nowhere else", passed.Stdout);
    }
}
