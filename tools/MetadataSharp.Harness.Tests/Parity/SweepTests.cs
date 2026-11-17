using MetadataSharp.Harness.Parity;
using MetadataSharp.Harness.Python;
using Xunit;

namespace MetadataSharp.Harness.Tests.Parity;

// Offline rules of the soundness sweep: how a dump is read and what is counted. No external tool runs here.
// (Formerly tests/parity/test_sweep.py.)
public sealed class SweepTests
{
    private const string Dump = "FILE\t/c/one.jpg\nCOMMENT\t1\tfirst\nCOMMENT\t2\tsecond\nSTATUS\tok\n"
                                + "FILE\t/c/two.png\nSTATUS\tok\n"
                                + "FILE\t/c/bad.png\nSTATUS\trefused\tInvalidPng\n"
                                + "FILE\t/c/worse.jpg\nSTATUS\terror\tIndexOutOfRangeException: Index was outside the bounds\n";

    private static readonly List<string> Paths = new[] { "one.jpg", "two.png", "bad.png", "worse.jpg" }.Select(name => PyPath.Join("/c", name)).ToList();

    [Fact]
    public void RecordsAreCountedPerFile()
    {
        var blocks = Sweep.ParseDump(Dump, "COMMENT");
        Assert.Equal((2L, "ok"), (blocks["/c/one.jpg"].Records, blocks["/c/one.jpg"].Status));
        Assert.Equal((0L, "refused", "InvalidPng"), (blocks["/c/bad.png"].Records, blocks["/c/bad.png"].Status, blocks["/c/bad.png"].Detail));
    }

    [Theory]
    [InlineData("ICC")]
    [InlineData("FILE\t/a\n")]
    [InlineData("STATUS\tok\n")]
    [InlineData("FILE\t/a\nFILE\t/b\nSTATUS\tok\n")]
    [InlineData("FILE\t/a\nSTATUS\tok\nCOMMENT\t1\ttext\n")]
    [InlineData("FILE\t/a\nCOMMENT\t1\nSTATUS\tok\n")]
    public void ARecordOfAnotherModeAndAMalformedDumpAreRefused(string text)
    {
        string dump = text == "ICC" ? Dump.Replace("COMMENT\t1", "ICC\t1", StringComparison.Ordinal) : text;
        Assert.Throws<SweepError>(() => Sweep.ParseDump(dump, "COMMENT"));
    }

    [Fact]
    public void TheTallyNamesEveryStatusEveryRefusalCodeAndEveryFailure()
    {
        var part = Sweep.Tally(Sweep.ParseDump(Dump, "COMMENT"), Paths);
        Assert.Equal(new Dictionary<string, long> { ["error"] = 1, ["ok"] = 2, ["refused"] = 1 }, ToCounts(part.Dict("statuses")));
        Assert.Equal(new Dictionary<string, long> { ["InvalidPng"] = 1 }, ToCounts(part.Dict("refused")));
        Assert.Equal((4L, 1L, 2L), (part.Long("files"), part.Long("files_with_records"), part.Long("records")));
        Assert.Equal(["worse.jpg"], part.List("errors").Select(entry => ((PyDict)entry!).Str("file")).ToList());
        string text = Sweep.TextReport(PyDict.Of(("comments", part)));
        Assert.Contains("comments: 4 files", text);
        Assert.Contains("ERROR worse.jpg: IndexOutOfRangeException", text);
    }

    [Fact]
    public void AFileIsLookedUpUnderTheForwardSlashPathTheDumpToolWasGiven()
    {
        // A native Windows path would come back from the tool with its backslashes escaped.
        if (OperatingSystem.IsWindows()) Assert.Equal("C:/corpus/one.jpg", Sweep.Key(@"C:\corpus\one.jpg"));
        Assert.Equal(["/c/one.jpg", "/c/two.png", "/c/bad.png", "/c/worse.jpg"], Paths.Select(Sweep.Key).ToList());
    }

    [Fact]
    public void AFileTheDumpDidNotAnswerForIsAStructuralFailure()
    {
        Assert.Throws<SweepError>(() => Sweep.Tally(Sweep.ParseDump(Dump, "COMMENT"), [.. Paths, "/c/absent.jpg"]));
    }

    private static Dictionary<string, long> ToCounts(PyDict counts) => counts.ToDictionary(pair => (string)pair.Key, pair => Convert.ToInt64(pair.Value));
}
