using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Harness.Parity;
using MetadataSharp.Harness.Python;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;
using DumpBlock = MetadataSharp.Harness.Parity.Previews.DumpBlock;

namespace MetadataSharp.Harness.Tests.Parity;

// Offline rules of the preview comparison: how the expectation and the dump are read, and how a file's
// previews are judged. No external tool runs here. (Formerly tests/parity/test_preview_parity.py.)
internal static class PreviewSamples
{
    public static readonly string Reference = """

        -----> one.jpg <-----

        Command: exiv2 -pp one.jpg
        Preview 1: image/jpeg, 160x120 pixels, 4158 bytes
        Preview 2: image/jpeg, 320x240 pixels, 16993 bytes
        Exit code: 0

        Command: exiv2 -f -ep one.jpg
        Exit code: 0

        -----> two.jpg <-----

        Command: exiv2 -pp two.jpg
        Warning: Directory Canon has an unexpected next pointer; ignored.
        Preview 1: image/tiff, 96x72 pixels, 20916 bytes
        Exit code: 0

        Command: exiv2 -f -ep two.jpg
        Warning: Directory Canon has an unexpected next pointer; ignored.
        Exit code: 0

        -----> none.jpg <-----

        Command: exiv2 -pp none.jpg
        Exit code: 0

        """.ReplaceLineEndings("\n");

    public static readonly PyTuple Small = Previews.Preview("image/jpeg", 160, 120, 4158);
    public static readonly PyTuple Large = Previews.Preview("image/jpeg", 320, 240, 16993);
    public static readonly PyTuple Tiff = Previews.Preview("image/tiff", 96, 72, 20916);
    public static readonly PyTuple Video = Previews.Preview("video/H265", 0, 0, 77);

    public static List<PyTuple> List(params PyTuple[] previews) => [.. previews];

    public static OrderedDictionary<string, List<PyTuple>> Expect(params (string Name, List<PyTuple> Previews)[] entries)
    {
        var result = new OrderedDictionary<string, List<PyTuple>>(StringComparer.Ordinal);
        foreach (var (name, previews) in entries) result[name] = previews;
        return result;
    }

    public static Dictionary<string, string?> Paths(params (string Name, string? Path)[] entries) =>
        entries.ToDictionary(entry => entry.Name, entry => entry.Path, StringComparer.Ordinal);

    public static OrderedDictionary<string, string> Outputs(params (string Name, string Text)[] entries)
    {
        var result = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, text) in entries) result[name] = text;
        return result;
    }
}

public sealed class PreviewReferenceTests
{
    [Fact]
    public void TheListingCommandIsReadPerFileInOrder()
    {
        var expected = Previews.ParseReference(PreviewSamples.Reference);
        Assert.Equal(["one.jpg", "two.jpg", "none.jpg"], expected.Keys.ToList());
        Assert.Equal(Json(PreviewSamples.List(PreviewSamples.Small, PreviewSamples.Large)), Json(expected["one.jpg"]));
        Assert.Equal(Json(PreviewSamples.List(PreviewSamples.Tiff)), Json(expected["two.jpg"]));
        Assert.Empty(expected["none.jpg"]);
    }

    [Fact]
    public void APreviewLineOfAnotherCommandIsNotCounted()
    {
        string text = PreviewSamples.Reference.Replace("Command: exiv2 -f -ep one.jpg\n",
                                                       "Command: exiv2 -f -ep one.jpg\nPreview 1: image/jpeg, 1x1 pixels, 9 bytes\n", StringComparison.Ordinal);
        Assert.Equal(Json(PreviewSamples.List(PreviewSamples.Small, PreviewSamples.Large)), Json(Previews.ParseReference(text)["one.jpg"]));
    }

    [Fact]
    public void DamageIsAnErrorAndNotASmallerExpectation()
    {
        Assert.Throws<PreviewParityError>(() => Previews.ParseReference(PreviewSamples.Reference.Replace("4158 bytes", "many bytes", StringComparison.Ordinal)));
        Assert.Throws<PreviewParityError>(() => Previews.ParseReference(PreviewSamples.Reference + "\n-----> one.jpg <-----\n"));
        Assert.Throws<PreviewParityError>(() => Previews.ParseReference("Preview 1: image/jpeg, 1x1 pixels, 9 bytes\n"));
    }
}

public sealed class PreviewOracleTests
{
    [Fact]
    public void WhatThePinnedToolPrintedIsReadByTheRulesOfTheExpectation()
    {
        var expected = Previews.OracleReference(PreviewSamples.Outputs(
            ("raw.cr3", "Preview 1: image/jpeg, 160x120 pixels, 4158 bytes\nPreview 2: image/jpeg, 320x240 pixels, 16993 bytes\n"),
            ("warned.cr3", "Warning: Directory Canon has an unexpected next pointer; ignored.\nPreview 1: image/tiff, 96x72 pixels, 20916 bytes\n")));
        Assert.Equal(["raw.cr3", "warned.cr3"], expected.Keys.ToList());
        Assert.Equal(Json(PreviewSamples.List(PreviewSamples.Small, PreviewSamples.Large)), Json(expected["raw.cr3"]));
        Assert.Equal(Json(PreviewSamples.List(PreviewSamples.Tiff)), Json(expected["warned.cr3"]));
    }

    [Fact]
    public void AFileTheToolPrintedNothingForExpectsNoPreview()
    {
        var expected = Previews.OracleReference(PreviewSamples.Outputs(("silent.cr3", ""), ("raw.cr3", "Preview 1: image/jpeg, 160x120 pixels, 4158 bytes")));
        Assert.Empty(expected["silent.cr3"]);
        Assert.Equal(Json(PreviewSamples.List(PreviewSamples.Small)), Json(expected["raw.cr3"]));
        var outcome = Previews.CompareFile(expected["silent.cr3"], PreviewSamples.List(PreviewSamples.Small));
        Assert.Equal((0, "[]", Json(PreviewSamples.List(PreviewSamples.Small))), (outcome.Match, Json(outcome.Missing), Json(outcome.Extra)));
    }

    [Fact]
    public void ALineOfTheToolThatDoesNotParseIsAnError()
    {
        Assert.Throws<PreviewParityError>(() => Previews.OracleReference(PreviewSamples.Outputs(("raw.cr3", "Preview 1: image/jpeg, 160 by 120"))));
    }

    [Fact]
    public void OffsetComparisonReportsListDifferencesInsteadOfAborting()
    {
        using var directory = Temp();
        string path = directory["sample.jpg"];
        File.WriteAllBytes(path, Enumerable.Repeat((byte)'x', 20000).ToArray());
        var dumped = new Dictionary<string, DumpBlock>
        {
            [path] = new() { Status = "ok", Previews = { PreviewSamples.Small }, Offsets = { 64 }, Sources = { "Exif.Thumbnail" } },
        };
        var report = Previews.Compare(PreviewSamples.Expect(("sample.jpg", PreviewSamples.List(PreviewSamples.Small, PreviewSamples.Large))), dumped,
                                      PreviewSamples.Paths(("sample.jpg", path)),
                                      new Dictionary<string, List<List<long>>> { ["sample.jpg"] = [[64], [128]] });
        Assert.Equal(1L, report.Dict("totals")["missing"]);
        Assert.Equal(0L, report.Dict("totals").Get("offset_mismatch", 0L));
        AssertJson("""[["image/jpeg", 320, 240, 16993]]""", ((PyDict)report.List("files")[0]!)["missing"]);
        AssertJson("""{"files": 1, "found": 2, "matched": 1, "missing": 1, "extra": 0}""", report.Dict("formats")["JPG"]);
        Assert.Contains("JPG: 2/1", Previews.TextReport(report));
    }

    [Fact]
    public void FixtureOracleOnlyReceivesImageFormatsWithPreviewLoaders()
    {
        Assert.Contains(".crw", Previews.PreviewFixtureSuffixes);
        Assert.Contains(".pgf", Previews.PreviewFixtureSuffixes);
        Assert.DoesNotContain(".asf", Previews.PreviewFixtureSuffixes);
        Assert.DoesNotContain(".wav", Previews.PreviewFixtureSuffixes);
        var exceptions = Previews.FixtureOracleExceptions;
        Assert.Equal(new HashSet<string>
        {
            "b11eps/dos-eps-xmp.eps", "b11eps/eps-plain.eps", "b11eps/eps-reference-write.eps", "b11eps/eps-xmp-31.eps", "b11eps/eps-xmp-crlf.eps",
            "b11eps/eps-xmp-nomain.eps", "b11eps/eps-xmp-readonly.eps", "b11eps/eps-xmp-space.eps", "b11eps/eps-xmp.eps",
            "b7/owned-codestream.jxl", "b8/owned-flat-rgb.psd",
        }, exceptions.Keys.Cast<string>().ToHashSet());
        foreach (var (relative, reason) in exceptions)
        {
            Assert.True(File.Exists(Repo.Path("tests", "fixtures", (string)relative)), (string)relative);
            Assert.False(string.IsNullOrWhiteSpace((string?)reason), (string)relative);
        }
        Assert.DoesNotContain("b11eps/*", exceptions.Keys);
        Assert.DoesNotContain("*.eps", exceptions.Keys);
    }

    [Fact]
    public void TheTwoSourcesOfTheExpectationExcludeEachOther()
    {
        // argparse rejects neither source and both sources with status 2, as parser.error() does.
        Assert.Equal(2, Capture(() => Previews.Run(["--dump-command", "dump"])).Code);
        Assert.Equal(2, Capture(() => Previews.Run(["--dump-command", "dump", "--tree", "t", "--fixtures", "f"])).Code);
        // A fixture run without the pinned tool is a structural failure of the harness.
        var (code, _, error) = Capture(() => Previews.Run(["--dump-command", "dump", "--fixtures", "f"]));
        Assert.Equal(1, code);
        Assert.Contains("structural failure: --fixtures needs --exiv2-command", error);
    }
}

public sealed class PreviewDumpTests
{
    [Fact]
    public void RecordsAreGroupedByFile()
    {
        var blocks = Previews.ParseDump("FILE\t/c/one.jpg\nPREVIEW\t1\timage/jpeg\t160x120\t4158\t64\tExif.Thumbnail.JPEGInterchangeFormat\n"
                                        + "PREVIEW\t2\tvideo/H265\t0x0\t77\t128\tBmff.thmb\nSTATUS\tok\n"
                                        + "FILE\t/c/two.jpg\nSTATUS\trefused\tLimitExceeded\n");
        var one = blocks["/c/one.jpg"];
        Assert.Equal(Json(PreviewSamples.List(PreviewSamples.Small, PreviewSamples.Video)), Json(one.Previews));
        Assert.Equal([64L, 128L], one.Offsets);
        Assert.Equal(["Exif.Thumbnail.JPEGInterchangeFormat", "Bmff.thmb"], one.Sources);
        Assert.Equal("ok", one.Status);
        var two = blocks["/c/two.jpg"];
        Assert.Equal(("refused", "LimitExceeded", 0), (two.Status, two.Detail, two.Previews.Count));
    }

    public static TheoryData<string> Malformed => new()
    {
        "PREVIEW\t1\timage/jpeg\t1x1\t9\t8\tx\n", "FILE\t/a\n", "FILE\t/a\nPREVIEW\t1\timage/jpeg\t1by1\t9\t8\tx\nSTATUS\tok\n",
        "FILE\t/a\nSTATUS\tok\nPREVIEW\t1\timage/jpeg\t1x1\t9\t8\tx\n", "FILE\t/a\nFILE\t/b\nSTATUS\tok\n",
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedDumpIsRefused(string text)
    {
        Assert.Throws<PreviewParityError>(() => Previews.ParseDump(text));
    }
}

public sealed class PreviewComparisonTests
{
    private static string Outcome(Previews.FileOutcome outcome) => $"{outcome.Match} {Json(outcome.Missing)} {Json(outcome.Extra)} {outcome.Ordered}";

    [Fact]
    public void PreviewsAreComparedAsAMultiset()
    {
        var small = PreviewSamples.Small;
        var large = PreviewSamples.Large;
        Assert.Equal("2 [] [] True", Outcome(Previews.CompareFile([small, large], [small, large])));
        Assert.Equal($"1 {Json(new[] { small })} {Json(new[] { PreviewSamples.Video })} True",
                     Outcome(Previews.CompareFile([small, large], [large, PreviewSamples.Video])));
        var outcome = Previews.CompareFile([small, small], [small]);
        Assert.Equal((1, Json(new[] { small }), "[]"), (outcome.Match, Json(outcome.Missing), Json(outcome.Extra)));
    }

    [Fact]
    public void OneDifferingNumberIsAMissingAndAnExtraPreview()
    {
        var other = Previews.Preview("image/jpeg", 160, 120, 4159);
        var outcome = Previews.CompareFile([PreviewSamples.Small], [other]);
        Assert.Equal((0, Json(new[] { PreviewSamples.Small }), Json(new[] { other })), (outcome.Match, Json(outcome.Missing), Json(outcome.Extra)));
    }

    [Fact]
    public void TheOrderOfTheCommonPreviewsIsJudgedOnItsOwn()
    {
        Assert.False(Previews.CompareFile([PreviewSamples.Small, PreviewSamples.Large], [PreviewSamples.Large, PreviewSamples.Small]).Ordered);
        Assert.True(Previews.CompareFile([PreviewSamples.Small, PreviewSamples.Large],
                                         [PreviewSamples.Small, Previews.Preview("image/x-raw-rgb", 2, 2, 12), PreviewSamples.Large]).Ordered);
    }

    [Fact]
    public void TotalsCountEveryExpectedPreviewAndOnlyOkFilesContribute()
    {
        var expected = Previews.ParseReference(PreviewSamples.Reference);
        using var root = Temp();
        File.WriteAllBytes(root["one.jpg"], new byte[30000]);
        var dumped = new Dictionary<string, DumpBlock>
        {
            [root["one.jpg"]] = new() { Previews = { PreviewSamples.Small, PreviewSamples.Large }, Offsets = { 100, 200 }, Sources = { "a", "b" }, Status = "ok" },
            [root["two.jpg"]] = new() { Previews = { PreviewSamples.Tiff }, Sources = { "a" }, Status = "refused", Detail = "InvalidJpeg" },
        };
        var report = Previews.Compare(expected, dumped, PreviewSamples.Paths(("one.jpg", root["one.jpg"]), ("two.jpg", root["two.jpg"]), ("none.jpg", null)));
        Assert.Equal("Preview", report.Str("kind"));
        AssertJson("""{"expected": 3, "match": 2, "missing": 1, "extra": 0, "files": 3, "files_equal": 2, "files_ordered": 3}""", report["totals"]);
        AssertJson("""{"ok": 1, "refused": 1, "no-input": 1}""", report["statuses"]);
        Assert.Equal(Py.Round(2.0 / 3, 6), (double)report["rate"]!);
        var differing = Previews.Summary(report).List("differing").Cast<PyDict>().Select(entry => entry.Str("file")).ToList();
        Assert.Equal(["none.jpg", "two.jpg"], differing);
        Assert.Contains("two.jpg [refused]", Previews.TextReport(report));
    }

    [Fact]
    public void AFileTheDumpDidNotAnswerForIsAStructuralFailure()
    {
        var expected = Previews.ParseReference(PreviewSamples.Reference);
        Assert.Throws<PreviewParityError>(() => Previews.Compare(expected, [], PreviewSamples.Paths(("one.jpg", "/c/one.jpg"), ("two.jpg", null), ("none.jpg", null))));
    }

    [Fact]
    public void ADifferingSourceOffsetFailsEvenWhenThePreviewListMatches()
    {
        using var temporary = Temp();
        string path = temporary["file.jpg"];
        File.WriteAllBytes(path, new byte[30000]);
        var dumped = new Dictionary<string, DumpBlock>
        {
            [path] = new() { Previews = { PreviewSamples.Small, PreviewSamples.Large }, Offsets = { 100, 200 }, Sources = { "Exif", "Exif" }, Status = "ok" },
        };
        var report = Previews.Compare(PreviewSamples.Expect(("file.jpg", PreviewSamples.List(PreviewSamples.Small, PreviewSamples.Large))), dumped,
                                      PreviewSamples.Paths(("file.jpg", path)), new Dictionary<string, List<List<long>>> { ["file.jpg"] = [[99], [200]] });
        Assert.Equal(1L, report.Dict("totals")["offset_mismatch"]);
        AssertJson("""[{"preview": ["image/jpeg", 160, 120, 4158], "actual": 100, "expected_any": [99]}]""",
                   ((PyDict)report.List("files")[0]!)["offset_mismatch"]);
        Assert.Equal(1, Previews.GateExitCode(report));
    }

    [Fact]
    public void SourceOffsetsAreRecoveredFromDirectHexAndBase64PreviewBytes()
    {
        byte[] jpeg = [0xff, 0xd8, .. "sample"u8, 0xff, 0xd9];
        Assert.Equal([3L], Previews.SourceOffsets([.. "xx:"u8, .. jpeg, .. ":yy"u8], jpeg, "image/jpeg", 1, 1));
        byte[] hexJpeg = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(jpeg));
        byte[] photoshop = [.. "%BeginPhotoshop: 1\n"u8, .. hexJpeg, .. "\n%EndPhotoshop"u8];
        Assert.Equal([(long)"%BeginPhotoshop: 1\n".Length], Previews.SourceOffsets(photoshop, jpeg, "image/jpeg", 1, 1));
        byte[] xmp = [.. "<image>"u8, .. Encoding.ASCII.GetBytes(Convert.ToBase64String(jpeg)), .. "</image>"u8];
        Assert.Equal([(long)"<image>".Length], Previews.SourceOffsets(xmp, jpeg, "image/jpeg", 1, 1));
        byte[] pnm = [.. "P6\n1 1\n255\n"u8, 1, 2, 3];
        byte[] eps = "%%BeginData: 6 Hex Bytes\n 010203\n%%EndData"u8.ToArray();
        Assert.Equal([(long)"%%BeginData: 6 Hex Bytes\n".Length], Previews.SourceOffsets(eps, pnm, "image/x-portable-anymap", 1, 1));
        byte[] profile = [.. "Exif\0\0"u8, .. jpeg];
        byte[] text = [.. "\nexif\n"u8, .. Encoding.ASCII.GetBytes(profile.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)), (byte)'\n',
                       .. Encoding.ASCII.GetBytes(Convert.ToHexStringLower(profile))];
        byte[] keyword = "Raw profile type exif"u8.ToArray();
        byte[] payload = [.. keyword, 0, 0, .. Zlib.Compress(text)];
        byte[] chunk = [.. BigEndian(payload.Length), .. "zTXt"u8, .. payload, 0, 0, 0, 0];
        byte[] png = [0x89, .. "PNG\r\n"u8, 0x1a, (byte)'\n', .. chunk, .. BigEndian(0), .. "IEND"u8, 0, 0, 0, 0];
        Assert.Equal([8L + 8 + keyword.Length + 2], Previews.SourceOffsets(png, jpeg, "image/jpeg", 1, 1));
    }

    [Fact]
    public void GeneratedTiffOffsetIsRecoveredFromItsSourceStrip()
    {
        byte[] pixels = "RGB!"u8.ToArray();
        // Two TIFF fields: StripOffsets at 38 and StripByteCounts at four bytes.
        byte[] tiff = [.. "II"u8, .. LittleEndian16(42), .. LittleEndian32(8), .. LittleEndian16(2),
                       .. LittleEndian16(273), .. LittleEndian16(4), .. LittleEndian32(1), .. LittleEndian32(38),
                       .. LittleEndian16(279), .. LittleEndian16(4), .. LittleEndian32(1), .. LittleEndian32(pixels.Length),
                       .. LittleEndian32(0), .. pixels];
        byte[] source = [.. "prefix"u8, .. pixels, .. "suffix"u8];
        Assert.Equal([6L], Previews.SourceOffsets(source, tiff, "image/tiff", 1, 1));
    }

    [Fact]
    public void AReportedOffsetOutsideItsSourceFileIsAStructuralFailure()
    {
        using var temporary = Temp();
        string path = temporary["file.jpg"];
        File.WriteAllBytes(path, new byte[10]);
        var dumped = new Dictionary<string, DumpBlock>
        {
            [path] = new() { Previews = { PreviewSamples.Small }, Offsets = { 11 }, Sources = { "EXIF" }, Status = "ok" },
        };
        Assert.Throws<PreviewParityError>(() => Previews.Compare(PreviewSamples.Expect(("file.jpg", PreviewSamples.List(PreviewSamples.Small))), dumped,
                                                                 PreviewSamples.Paths(("file.jpg", path))));
    }

    [Fact]
    public void AnyUnexplainedPreviewMismatchOrRefusalFailsTheGate()
    {
        Assert.Equal(0, Previews.GateExitCode(D("""{"statuses": {"ok": 1}, "totals": {"missing": 0, "extra": 0}}""")));
        foreach (string report in new[]
                 {
                     """{"statuses": {"ok": 1}, "totals": {"missing": 1, "extra": 0}}""",
                     """{"statuses": {"ok": 1}, "totals": {"missing": 0, "extra": 1}}""",
                     """{"statuses": {"refused": 1}, "totals": {"missing": 0, "extra": 0}}""",
                     """{"statuses": {"no-input": 1}, "totals": {"missing": 0, "extra": 0}}""",
                 })
        {
            Assert.Equal((report, 1), (report, Previews.GateExitCode(D(report))));
        }
    }

    private static byte[] BigEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] LittleEndian16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] LittleEndian32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
