using MetadataSharp.Harness.Parity;
using MetadataSharp.Harness.Python;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Parity;

// The video key/value gate (formerly tests/parity/test_video_gate.py).
public sealed class VideoGateTests
{
    private static PyDict Report(string rows = "[]")
    {
        var entries = new[]
        {
            ("generated:authored-asf.asf", "asf"), ("generated:authored-avi.avi", "avi"), ("generated:authored-matroska.mkv", "mkv"),
            ("generated:authored-quicktime.mp4", "mp4"), ("generated:authored-webm.webm", "webm"),
        }.Select(entry => $$"""{"corpus": "generated", "label": "{{entry.Item1}}", "format": "{{entry.Item2}}", "status": "ok", "rows": []}""");
        return D($$"""{"per_file": [{{string.Join(", ", entries)}}, {"corpus": "exiv2", "label": "exiv2:flame.avi", "format": "avi", "status": "ok", "rows": {{rows}}}]}""");
    }

    [Fact]
    public void AllKeysMustMatchAndOnlyTheTwoDocumentedAviValuesAreAllowed()
    {
        var (passed, message) = VideoGate.Evaluate(Report("""
            [{"key": "Xmp.video.NumOfColours", "outcome": "value-diff", "our_value": "Unspecified", "their_value": "1263424842  1263424842"},
             {"key": "Xmp.video.NumIfImpColours", "outcome": "value-diff", "our_value": "All", "their_value": "1816  1816"}]
            """));
        Assert.True(passed, message);
        Assert.Contains("PASS", message);
        Assert.Contains("generated:authored-wave.wav", message);
        Assert.Contains("unknown image type", message);
    }

    [Fact]
    public void AnyExtraOrMissingKeyFailsAndNamesWhichSideHasIt()
    {
        var (passed, message) = VideoGate.Evaluate(Report("""
            [{"key": "Xmp.video.Extra", "outcome": "extra-key", "our_value": "1"},
             {"key": "Xmp.video.Missing", "outcome": "missing-key", "their_value": "2"}]
            """));
        Assert.False(passed);
        Assert.Contains("ours-only keys (must be removed): Xmp.video.Extra", message);
        Assert.Contains("reference-only keys (must be implemented): Xmp.video.Missing", message);
    }

    [Fact]
    public void PermittedAviDivergencesDoNotHaveToOccur()
    {
        var (passed, message) = VideoGate.Evaluate(Report());
        Assert.True(passed, message);
    }

    [Fact]
    public void TruncatedRowsFailClosedInsteadOfClaimingKeySetParity()
    {
        var report = Report();
        ((PyDict)report.List("per_file")[0]!)["rows_dropped"] = 1L;
        var (passed, message) = VideoGate.Evaluate(report);
        Assert.False(passed);
        Assert.Contains("key/value gate requires complete rows", message);
    }

    [Fact]
    public void MissingAuthoredMkvFailsTheGate()
    {
        var report = Report();
        report["per_file"] = report.List("per_file").Where(entry => ((PyDict)entry!).Str("label") != "generated:authored-matroska.mkv").ToList();
        var (passed, message) = VideoGate.Evaluate(report);
        Assert.False(passed);
        Assert.Contains("generated:authored-matroska.mkv", message);
        Assert.Contains("generated report entries:", message);
        Assert.Contains("generated:authored-webm.webm [format=webm]", message);
    }

    [Fact]
    public void MainWritesFailureReasonToGithubStepSummary()
    {
        var report = Report("""[{"key": "Xmp.video.Extra", "outcome": "extra-key"}]""");
        using var directory = Temp();
        string reportPath = Write(directory["parity-report.json"], PyJson.Dumps(report));
        string summaryPath = directory["summary.md"];
        int result;
        using (Environment(("GITHUB_STEP_SUMMARY", summaryPath)))
        {
            result = Capture(() => VideoGate.Run([reportPath])).Code;
        }
        Assert.Equal(1, result);
        string contents = File.ReadAllText(summaryPath);
        Assert.Contains("ours-only keys (must be removed): Xmp.video.Extra", contents);
        Assert.EndsWith("```\n", contents);
    }
}
