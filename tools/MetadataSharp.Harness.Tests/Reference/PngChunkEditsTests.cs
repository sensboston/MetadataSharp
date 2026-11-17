using System.Globalization;
using System.Text;
using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Reference;

// Offline checks for the rule that the pinned tool lists the chunks a PNG edit was to leave (formerly
// tests/reference/test_png_chunk_edits.py). The tool itself is not run here: its print-outs are authored text in the
// format of exiv2 -pS.
public sealed class PngChunkEditsTests
{
    private static string Crc(string kind, byte[] data) => Zlib.Crc32([.. Encoding.ASCII.GetBytes(kind), .. data]).ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>One row as the tool prints it: unprintable bytes as dots, a zero that ends the first 30 bytes left out.</summary>
    private static string Row(string kind, byte[] data, int address = 0)
    {
        byte[] shown = data[..Math.Min(30, data.Length)];
        var text = new StringBuilder();
        for (int index = 0; index < shown.Length; index++)
        {
            byte value = shown[index];
            if (value == 0 && index == shown.Length - 1) continue;
            text.Append(value < 32 || value >= 127 ? '.' : (char)value);
        }
        return $"{address,8} | {kind,-5} |{data.Length,8} | {text,-30} | 0x{Crc(kind, data)}";
    }

    private static byte[] Printed(params (string Kind, byte[] Data)[] rows)
    {
        var lines = new List<string> { "STRUCTURE OF PNG FILE: a.png", " address | chunk |  length | data                           | checksum" };
        lines.AddRange(rows.Select((row, index) => Row(row.Kind, row.Data, 8 + 40 * index)));
        return Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
    }

    private static readonly (string, byte[]) Ihdr = ("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
    private static readonly (string, byte[]) Gama = ("gAMA", [0, 1, 0x86, 0xA0]);
    private static readonly (string, byte[]) Srgb = ("sRGB", [0]);
    private static readonly (string, byte[]) Plte = ("PLTE", [0, 1, 2, 3, 4, 5]);
    private static readonly (string, byte[]) Idat = ("IDAT", [(byte)'x', 0x9C, (byte)'c', 0xE0, 0x12, 0x91, 0x03, 0x00, 0x00, 0x81, 0x00, 0x3D]);
    private static readonly (string, byte[]) Time = ("tIME", [7, 0xEA, 11, 11, 10, 9, 8]);
    private static readonly (string, byte[]) Iend = ("IEND", []);
    private static readonly (string, byte[]) Iccp = ("iCCP", [.. "ICC Profile\0\0x"u8, 0x9C, .. Enumerable.Range(0, 40).Select(i => (byte)i)]);
    private static readonly (string, byte[]) OtherIccp = ("iCCP", [.. "Adobe RGB\0\0x"u8, 0x9C, .. Enumerable.Range(0, 60).Select(i => (byte)i)]);

    private static (string, byte[]) Text(string comment) => ("tEXt", [.. "Comment\0"u8, .. Encoding.Latin1.GetBytes(comment)]);

    private static (string, byte[]) Wide(string comment) => ("iTXt", [.. "Comment\0\0\0\0\0"u8, .. Encoding.UTF8.GetBytes(comment)]);

    private static (string, byte[]) Packed(byte[]? payload = null) => ("zTXt", [.. "Comment\0\0"u8, .. payload ?? [(byte)'x', 0x9C, 0x01, 0x02]]);

    private static List<ChunkRow> Table(params (string, byte[])[] rows) => PngChunkEdits.Chunks(Printed(rows))!;

    // --- the manifest

    [Fact]
    public void ARowNamesSourceEditedKindAndExpectation()
    {
        var entries = PngChunkEdits.ParseManifest("a.png\tb.png\tcomment\ttEXt:4869\n\na.png\tc.png\tcomment\tiTXt:c3a9e282ac\n"
                                                  + "a.png\td.png\tcomment\t-\na.png\te.png\tprofile\tiCCP\na.png\tf.png\tprofile\t-\n");
        Assert.Equal(["b.png", "c.png", "d.png", "e.png", "f.png"], entries.Select(entry => entry.Edited));
        Assert.All(entries, entry => Assert.Equal("a.png", entry.Source));
        Assert.Equal(["comment", "comment", "comment", "profile", "profile"], entries.Select(entry => entry.Kind));
        Assert.Equal(("tEXt", "Hi"), entries[0].Comment);
        Assert.Equal(("iTXt", "\u00e9\u20ac"), entries[1].Comment);
        Assert.Equal(("-", null), (entries[2].Expected, entries[2].Comment));
        Assert.Equal(["iCCP", "-"], entries.Skip(3).Select(entry => entry.Expected));
    }

    [Theory]
    [InlineData("a.png\tb.png\tcomment\n")]
    [InlineData("a.png\tb.png\t\t-\n")]
    [InlineData("a.png\tb.png\tcomment\tzTXt:4869\n")]
    [InlineData("a.png\tb.png\tcomment\ttEXt:Hi\n")]
    [InlineData("a.png\tb.png\tcomment\tiTXt:ff\n")]
    [InlineData("a.png\tb.png\tcomment\ttEXt:\n")]
    [InlineData("a.png\tb.png\tprofile\tsRGB\n")]
    [InlineData("a.png\tb.png\texif\t-\n")]
    [InlineData("\n")]
    public void ARowThatIsNotUnderstoodIsAnError(string bad) => Assert.Equal("ValueError", Assert.Throws<PyException>(() => PngChunkEdits.ParseManifest(bad)).Type);

    // --- the print-out

    [Fact]
    public void TheRowsAreTypeLengthDataAndChecksum()
    {
        var rows = Table(Ihdr, Gama, Idat, Iend);
        Assert.Equal(["IHDR", "gAMA", "IDAT", "IEND"], rows.Select(found => found.Type));
        Assert.Equal((4L, Crc("gAMA", Gama.Item2)), (rows[1].Length, rows[1].Checksum));
        Assert.Equal(30, rows[1].Data.Length);
    }

    [Fact]
    public void ADataColumnWithBarsAndSpacesIsStillOneColumn()
    {
        var rows = Table(Ihdr, Text(" | 0x12345678 | tEXt |"), Idat, Iend);
        Assert.Equal("tEXt", rows[1].Type);
        Assert.True(PngChunkEdits.IsComment(rows[1]));
    }

    [Fact]
    public void APrintOutThatIsNoWholeFileIsNotAChunkList()
    {
        Assert.Null(PngChunkEdits.Chunks([]));
        Assert.Null(PngChunkEdits.Chunks("Exiv2 exception in print action for file a.png:\n"u8.ToArray()));
        Assert.Null(PngChunkEdits.Chunks(Printed(Ihdr, Idat)));
        Assert.Null(PngChunkEdits.Chunks(Printed(Gama, Idat, Iend)));
    }

    [Fact]
    public void ACommentIsATextChunkWithExactlyThatKeyword()
    {
        foreach (var chunk in new[] { Text("x"), Wide("x"), Packed(), ("tEXt", "Comment\0"u8.ToArray()) })
            Assert.True(PngChunkEdits.IsComment(Table(Ihdr, chunk, Idat, Iend)[1]), chunk.Item1);
        foreach (var chunk in new[] { ("tEXt", "Description\0x"u8.ToArray()), ("tEXt", "Commentary\0x"u8.ToArray()), ("tEXt", "comment\0x"u8.ToArray()),
                                      ("tEXt", "Comment"u8.ToArray()), ("eXIf", "Comment\0x"u8.ToArray()) })
            Assert.False(PngChunkEdits.IsComment(Table(Ihdr, chunk, Idat, Iend)[1]), Encoding.Latin1.GetString(chunk.Item2));
    }

    [Fact]
    public void TheExpectedChunkIsBuiltFromTheText()
    {
        Assert.Equal(("tEXt", 12L, Crc("tEXt", [.. "Comment\0caf"u8, 0xE9])), PngChunkEdits.CommentChunk("tEXt", "caf\u00e9"));
        Assert.Equal(("iTXt", 17L, Crc("iTXt", [.. "Comment\0\0\0\0\0caf"u8, 0xC3, 0xA9])), PngChunkEdits.CommentChunk("iTXt", "caf\u00e9"));
    }

    // --- comments

    private static readonly (string, byte[])[] CommentSource = [Ihdr, Gama, Idat, Time, Iend];

    private static List<string> CompareComment((string, string)? expected, (string, byte[])[] source, params (string, byte[])[] edited) =>
        PngChunkEdits.CompareComment(expected, Table(source), Table(edited));

    [Fact]
    public void AnInsertedCommentBeforeTheImageDataPasses()
    {
        Assert.Empty(CompareComment(("tEXt", "Hi"), CommentSource, Ihdr, Gama, Text("Hi"), Idat, Time, Iend));
        Assert.Empty(CompareComment(("iTXt", "\u20ac"), CommentSource, Ihdr, Gama, Wide("\u20ac"), Idat, Time, Iend));
    }

    [Fact]
    public void AnotherTextAnotherTypeOrAnotherEncodingIsAProblem()
    {
        foreach (var edited in new[] { Text("Ho"), Wide("Hi"), Packed() })
        {
            var problems = CompareComment(("tEXt", "Hi"), CommentSource, Ihdr, Gama, edited, Idat, Time, Iend);
            Assert.Single(problems);
            Assert.Contains("expected a tEXt of 10 bytes", problems[0]);
        }
    }

    [Fact]
    public void AReplacedCommentStandsWhereTheFirstStood()
    {
        (string, byte[])[] source = [Ihdr, Packed(), Gama, Idat, Wide("two"), Time, Iend];
        Assert.Empty(CompareComment(("tEXt", "Hi"), source, Ihdr, Text("Hi"), Gama, Idat, Time, Iend));
        Assert.Equal(["the comment does not stand where the first comment of the source stood"], CompareComment(("tEXt", "Hi"), source, Ihdr, Gama, Text("Hi"), Idat, Time, Iend));
    }

    [Fact]
    public void AnInsertedCommentElsewhereIsAProblem()
    {
        foreach (var edited in new[] { new[] { Ihdr, Text("Hi"), Gama, Idat, Time, Iend }, [Ihdr, Gama, Idat, Text("Hi"), Time, Iend], [Ihdr, Gama, Idat, Time, Text("Hi"), Iend] })
            Assert.Equal(["the comment does not stand directly before the first IDAT chunk"], CompareComment(("tEXt", "Hi"), CommentSource, edited));
    }

    [Fact]
    public void TwoCommentsOrNoneWhereOneWasToBeIsAProblem()
    {
        Assert.Contains("0 chunks", CompareComment(("tEXt", "Hi"), CommentSource, CommentSource)[0]);
        Assert.Contains("2 chunks", CompareComment(("tEXt", "Hi"), CommentSource, Ihdr, Gama, Text("Hi"), Text("Hi"), Idat, Time, Iend)[0]);
    }

    [Fact]
    public void ARemovalLeavesNoCommentAndEveryOtherChunk()
    {
        (string, byte[])[] source = [Ihdr, Packed(), Gama, Idat, Wide("two"), Time, Iend];
        Assert.Empty(CompareComment(null, source, CommentSource));
        Assert.Equal(["1 chunk(s) with the keyword Comment are left"], CompareComment(null, source, Ihdr, Gama, Idat, Wide("two"), Time, Iend));
    }

    [Fact]
    public void AnotherChunkThatChangedWentOrCameIsAProblem()
    {
        Assert.Contains("chunk 2 of the others was gAMA of 4 bytes", CompareComment(("tEXt", "Hi"), CommentSource, Ihdr, ("gAMA", [0, 0, 0xB1, 0x8F]), Text("Hi"), Idat, Time, Iend)[0]);
        Assert.Contains("was tIME of 7 bytes", CompareComment(("tEXt", "Hi"), CommentSource, Ihdr, Gama, Text("Hi"), Idat, Iend)[0]);
        Assert.Contains("is sRGB of 1 bytes", CompareComment(null, CommentSource, Ihdr, Gama, Srgb, Idat, Time, Iend)[0]);
        Assert.Contains("the chunks that are no comment changed", CompareComment(null, CommentSource, Ihdr, Idat, Gama, Time, Iend)[0]);
    }

    [Fact]
    public void AListThatOnlyEndsSoonerOrLaterIsToldAsFewerOrMore()
    {
        (string, long, string) one = ("IHDR", 13, "00000001"), two = ("gAMA", 4, "00000002");
        Assert.Equal("1 chunk(s) fewer than the edit names, the first a gAMA of 4 bytes", PngChunkEdits.FirstDifference([one, two], [one]));
        Assert.Equal("1 chunk(s) more than the edit names, the first a gAMA of 4 bytes", PngChunkEdits.FirstDifference([one], [one, two]));
    }

    [Fact]
    public void ATextChunkOfATextOutsideItsEncodingIsReportedNotRaised() =>
        Assert.Equal(["the manifest asks for a tEXt chunk of a text that is not ISO-8859-1"],
                     CompareComment(("tEXt", "\u20ac"), CommentSource, Ihdr, Gama, Wide("\u20ac"), Idat, Time, Iend));

    // --- profiles

    private static readonly (string, byte[])[] ProfileSource = [Ihdr, Gama, Plte, Idat, Time, Iend];

    private static List<string> CompareProfile(string expected, (string, byte[])[] source, params (string, byte[])[] edited) =>
        PngChunkEdits.CompareProfile(expected, Table(source), Table(edited));

    [Fact]
    public void AnInsertedProfileDirectlyAfterTheHeaderPasses() => Assert.Empty(CompareProfile("iCCP", ProfileSource, Ihdr, Iccp, Gama, Plte, Idat, Time, Iend));

    [Fact]
    public void AnInsertedProfileElsewhereIsAProblem()
    {
        Assert.Equal(["the iCCP chunk does not stand directly after IHDR"], CompareProfile("iCCP", ProfileSource, Ihdr, Gama, Iccp, Plte, Idat, Time, Iend));
        Assert.Equal(["the iCCP chunk follows PLTE or IDAT", "the iCCP chunk does not stand directly after IHDR"],
                     CompareProfile("iCCP", ProfileSource, Ihdr, Gama, Plte, Iccp, Idat, Time, Iend));
    }

    [Fact]
    public void TheSrgbChunkGoesWithAWrittenProfileAndStaysWithARemovedOne()
    {
        (string, byte[])[] source = [Ihdr, Srgb, Gama, Idat, Iend];
        Assert.Empty(CompareProfile("iCCP", source, Ihdr, Iccp, Gama, Idat, Iend));
        Assert.Contains("is sRGB of 1 bytes", CompareProfile("iCCP", source, Ihdr, Iccp, Srgb, Gama, Idat, Iend)[0]);
        (string, byte[])[] both = [Ihdr, Srgb, OtherIccp, Gama, Idat, Iend];
        Assert.Empty(CompareProfile("-", both, source));
        Assert.Contains("was sRGB of 1 bytes", CompareProfile("-", both, Ihdr, Gama, Idat, Iend)[0]);
    }

    [Fact]
    public void AReplacedProfileStandsWhereTheOldOneStood()
    {
        (string, byte[])[] source = [Ihdr, Gama, OtherIccp, Plte, Idat, Iend];
        Assert.Empty(CompareProfile("iCCP", source, Ihdr, Gama, Iccp, Plte, Idat, Iend));
        Assert.Equal(["the iCCP chunk does not stand where that of the source stood"], CompareProfile("iCCP", source, Ihdr, Iccp, Gama, Plte, Idat, Iend));
        // The sRGB chunk that went does not count for the place.
        Assert.Empty(CompareProfile("iCCP", [Ihdr, Srgb, Gama, OtherIccp, Idat, Iend], Ihdr, Gama, Iccp, Idat, Iend));
    }

    [Fact]
    public void TheChunkCarriesTheNameThisLibraryWrites()
    {
        var problems = CompareProfile("iCCP", ProfileSource, Ihdr, OtherIccp, Gama, Plte, Idat, Time, Iend);
        Assert.Single(problems);
        Assert.Contains("is not named 'ICC Profile'", problems[0]);
    }

    [Fact]
    public void TwoProfilesOrNoneWhereOneWasToBeIsAProblem()
    {
        Assert.Equal(["0 iCCP chunks, expected one"], CompareProfile("iCCP", ProfileSource, ProfileSource));
        Assert.Equal(["2 iCCP chunks, expected one"], CompareProfile("iCCP", ProfileSource, Ihdr, Iccp, Iccp, Gama, Plte, Idat, Time, Iend));
    }

    [Fact]
    public void ARemovalLeavesNoProfileAndEveryOtherChunk()
    {
        (string, byte[])[] source = [Ihdr, Gama, OtherIccp, Plte, Idat, Time, Iend];
        Assert.Empty(CompareProfile("-", source, ProfileSource));
        Assert.Equal(["an iCCP chunk is left"], CompareProfile("-", source, source));
        Assert.Contains("the chunks that are no profile changed", CompareProfile("-", source, Ihdr, Gama, Idat, Time, Iend)[0]);
    }

    // --- the check

    private static readonly BytesResult CheckSource = new(0, Printed(Ihdr, Gama, Idat, Iend), "");

    private static (List<(string Name, string Message)> Problems, int Checked, int Skipped) Check(TempDirectory directory, List<(string, string)> calls, string manifest,
                                                                                                Dictionary<string, BytesResult> files, bool skip = false)
    {
        Write(directory["chunks.tsv"], manifest);
        return PngChunkEdits.Check(directory.Path, "exiv2", (command, path) =>
        {
            calls.Add((command[1], PyPath.Name(path)));
            return files[PyPath.Name(path)];
        }, skip);
    }

    [Fact]
    public void TheToolIsAskedForTheStructureAndTheSourceOnce()
    {
        using var directory = Temp();
        var calls = new List<(string, string)>();
        var files = new Dictionary<string, BytesResult>
        {
            ["a.png"] = CheckSource,
            ["b.png"] = new(0, Printed(Ihdr, Gama, Text("Hi"), Idat, Iend), ""),
            ["c.png"] = new(0, Printed(Ihdr, Iccp, Gama, Idat, Iend), ""),
        };
        var (problems, checkedCount, skipped) = Check(directory, calls, "a.png\tb.png\tcomment\ttEXt:4869\na.png\tc.png\tprofile\tiCCP\n", files);
        Assert.Equal((0, 2, 0), (problems.Count, checkedCount, skipped));
        Assert.Equal([("-pS", "a.png"), ("-pS", "b.png"), ("-pS", "c.png")], calls);
    }

    [Fact]
    public void AProblemNamesTheEditedFile()
    {
        using var directory = Temp();
        var files = new Dictionary<string, BytesResult> { ["a.png"] = CheckSource, ["b.png"] = new(0, Printed(Ihdr, Gama, Text("Ho"), Idat, Iend), "") };
        var (problems, checkedCount, _) = Check(directory, [], "a.png\tb.png\tcomment\ttEXt:4869\n", files);
        Assert.Equal((1, "b.png", 1), (problems.Count, problems[0].Name, checkedCount));
    }

    [Fact]
    public void AnEditedFileTheToolDoesNotListIsAProblem()
    {
        foreach (var broken in new BytesResult[] { new(1, [], "Exiv2 exception"), new(0, "nothing\n"u8.ToArray(), "") })
        {
            using var directory = Temp();
            var (problems, _, _) = Check(directory, [], "a.png\tb.png\tcomment\t-\n", new() { ["a.png"] = CheckSource, ["b.png"] = broken });
            Assert.Single(problems);
            Assert.Contains("-pS does not print the structure", problems[0].Message);
        }
    }

    [Fact]
    public void ASourceTheToolDoesNotListIsAFailureOrLeftOutAndCounted()
    {
        using var directory = Temp();
        var files = new Dictionary<string, BytesResult> { ["a.png"] = new(1, [], "damaged"), ["b.png"] = CheckSource };
        var (problems, checkedCount, skipped) = Check(directory, [], "a.png\tb.png\tcomment\t-\n", files);
        Assert.Equal((1, "a.png", 0, 0), (problems.Count, problems[0].Name, checkedCount, skipped));
        (problems, checkedCount, skipped) = Check(directory, [], "a.png\tb.png\tcomment\t-\n", files, skip: true);
        Assert.Equal((0, 0, 1), (problems.Count, checkedCount, skipped));
    }

    [Fact]
    public void MainReportsTheOutcomeThroughItsExitCode()
    {
        using var directory = Temp();
        Write(directory["chunks.tsv"], "a.png\tb.png\tcomment\ttEXt:4869\n");
        var good = new Dictionary<string, BytesResult> { ["a.png"] = CheckSource, ["b.png"] = new(0, Printed(Ihdr, Gama, Text("Hi"), Idat, Iend), "") };
        var bad = new Dictionary<string, BytesResult>(good) { ["b.png"] = CheckSource };
        foreach (var (files, code, word) in new[] { (good, 0, "::notice title=PNG profile and comment chunks::1 edited PNG files"),
                                                     (bad, 1, "::error title=PNG profile and comment chunks b.png::") })
        {
            var result = Capture(() => PngChunkEdits.Execute([directory.Path, "--exiv2", "exiv2"], (_, path) => files[PyPath.Name(path)]));
            Assert.Equal(code, result.Code);
            Assert.Contains(word, result.Stdout);
        }
    }
}
