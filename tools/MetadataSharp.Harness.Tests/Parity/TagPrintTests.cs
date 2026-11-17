using MetadataSharp.Harness.Parity;
using MetadataSharp.Harness.Python;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Parity;

// The -pt tag listing gate (formerly tests/parity/test_tag_print.py).
public sealed class TagPrintProtocolTests
{
    /// <summary>One row of the reference's -pt output, laid out as its print columns do.</summary>
    private static string PtLine(string key, string typeName, int count, string value) =>
        $"{PyText.LJust(key, 44)} {PyText.LJust(typeName, 9)} {PyText.RJust(count.ToString(System.Globalization.CultureInfo.InvariantCulture), 3)}  {value}";

    private static List<PyDict> Rows(string json) => ((List<object?>)J(json)!).Cast<PyDict>().ToList();

    private static PyDict With(PyDict row, string key, object? value)
    {
        var copy = new PyDict(row) { [key] = value };
        return copy;
    }

    private static (int, string, string) Reference(string output) => (0, output, "");

    [Fact]
    public void Exiv2PtRowsHaveKeyTypeCountAndOnePrintedValue()
    {
        string text = PtLine("Exif.Image.NewSubfileType", "Long", 1, "Thumbnail/Preview image") + "\n"
                      + PtLine("Exif.Image.ImageWidth", "Long", 1, "160") + "\n"
                      + PtLine("Exif.Image.Make", "Ascii", 18, "NIKON CORPORATION") + "\n"
                      + PtLine("Iptc.Application2.Caption", "String", 1, "not part of -pt") + "\n";
        AssertJson("""
            [{"key": "Exif.Image.NewSubfileType", "type": "Long", "count": 1, "value": "Thumbnail/Preview image"},
             {"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "value": "160"},
             {"key": "Exif.Image.Make", "type": "Ascii", "count": 18, "value": "NIKON CORPORATION"}]
            """, TagPrint.ParseExiv2(text));
    }

    [Fact]
    public void PtColumnsKeepLeadingValueSpacesAndAKeyWithASpace()
    {
        // Real bytes: the stored value starts with spaces, and one MakerNote group names a tag with a space in
        // it, so a whitespace split would lose both facts.
        AssertJson("""
            [{"key": "Exif.Photo.OffsetTime", "type": "Ascii", "count": 7, "value": "   :  "},
             {"key": "Exif.MinoltaCs5D.Color Temperature", "type": "SShort", "count": 1, "value": "5500"},
             {"key": "Exif.SubImage1.LinearizationTable", "type": "Short", "count": 4096, "value": "0 0 0"},
             {"key": "Exif.Photo.UserComment", "type": "0x000e", "count": 125, "value": ""}]
            """, TagPrint.ParseExiv2(
            PtLine("Exif.Photo.OffsetTime", "Ascii", 7, "   :  ") + "\n"
            + PtLine("Exif.MinoltaCs5D.Color Temperature", "SShort", 1, "5500") + "\n"
            + PtLine("Exif.SubImage1.LinearizationTable", "Short", 4096, "0 0 0") + "\n"
            + "Exif.Photo.UserComment                      0x000e    125\n"));
    }

    [Fact]
    public void PtRowsOfAWideKeyAreReadByTheirContent()
    {
        const string key = "Exif.NikonFl7.FlashSourceAndMasterControlGroupSettings";
        Assert.True(key.Length > TagPrint.PrintKeyWidth);
        AssertJson($$"""[{"key": "{{key}}", "type": "Byte", "count": 3, "value": "1 2 3"}]""", TagPrint.ParseExiv2(key + " Byte         3  1 2 3\n"));
    }

    [Fact]
    public void DumpProjectionUsesLocalInterpretationOrRawValueForPt()
    {
        AssertJson("""
            [{"key": "Exif.Image.Orientation", "type": "Short", "count": 1, "value": "top, left", "_raw_value": "1", "_has_translation": true},
             {"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "value": "50/1", "_raw_value": "50/1", "_has_translation": false},
             {"key": "Exif.Image.ImageDescription", "type": "Ascii", "count": 3, "value": "   ", "_raw_value": "   ", "_has_translation": false}]
            """, TagPrint.ProjectDump(Rows("""
            [{"key": "Exif.Image.Orientation", "type": "Short", "count": 1, "raw": "1", "translated": "top, left"},
             {"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "raw": "50/1", "translated": ""},
             {"key": "Exif.Image.ImageDescription", "type": "Ascii", "count": 3, "raw": "   ", "translated": ""},
             {"key": "Iptc.Application2.Caption", "type": "String", "count": 1, "raw": "x", "translated": "x"}]
            """)));
    }

    [Fact]
    public void ComparisonIgnoresRecordIterationOrderButNotValues()
    {
        var expected = Rows("""
            [{"key": "Exif.Image.Make", "type": "Ascii", "count": 1, "value": "Camera"},
             {"key": "Exif.Image.Model", "type": "Ascii", "count": 1, "value": "Body"}]
            """);
        Assert.Empty(TagPrint.CompareRows(expected, Enumerable.Reverse(expected).ToList()));
        var differences = TagPrint.CompareRows(expected, [expected[0], With(expected[1], "value", "Other body")]);
        Assert.Equal(["value-diff"], differences.Select(row => row.Str("kind")).ToList());
    }

    [Fact]
    public void NumericFallbackMatchesOnlyTheSameCataloguedTag()
    {
        var accepted = new Counter();
        var expected = Rows("""[{"key": "Exif.Image.0x0100", "type": "Long", "count": 1, "value": "640"}]""");
        var actual = Rows("""[{"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "value": "640"}]""");
        Assert.Empty(TagPrint.CompareRows(expected, actual, acceptedNames: accepted));
        var hit = Assert.Single(accepted);
        Assert.Equal(new PyTuple("Exif.Image.0x0100", "Exif.Image.ImageWidth"), hit.Key);
        Assert.Equal(1L, hit.Value);
        var otherDirectory = Rows("""[{"key": "Exif.Photo.0x0100", "type": "Long", "count": 1, "value": "640"}]""");
        Assert.Equal(["extra", "missing"], TagPrint.CompareRows(otherDirectory, actual).Select(row => row.Str("kind")).Order(StringComparer.Ordinal).ToList());
        var wrongValue = Rows("""[{"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "value": "641"}]""");
        Assert.Single(TagPrint.CompareRows(expected, wrongValue));
    }

    [Fact]
    public void PtTextDifferenceRequiresExactIndependentRawValue()
    {
        var difference = D("""
            {"kind": "value-diff",
             "exiv2": {"key": "Exif.Sony1MltCsA100.AELButton", "type": "Short", "count": 1, "value": "Hold"},
             "metadatasharp": {"key": "Exif.Sony1MltCsA100.AELButton", "type": "Short", "count": 1, "value": "0"}}
            """);
        var rawReference = Rows("""[{"key": "Exif.Sony1MltCsA100.AELButton", "type": "Short", "count": 1, "value": "0"}]""");
        var sourceRows = Rows("""[{"key": "Exif.Sony1MltCsA100.AELButton", "type": "Short", "count": 1, "raw": "0", "translated": ""}]""");
        var (remaining, verified) = TagPrint.VerifyRawValueDifferences([difference], rawReference, sourceRows);
        Assert.Empty(remaining);
        Assert.Equal("0", verified[0].Str("raw_value"));
        (remaining, verified) = TagPrint.VerifyRawValueDifferences([difference], [With(rawReference[0], "value", "1")], sourceRows);
        AssertJsonEqual(new List<PyDict> { difference }, remaining);
        Assert.Empty(verified);
        (remaining, verified) = TagPrint.VerifyRawValueDifferences([difference], [With(rawReference[0], "type", "SShort")], sourceRows);
        AssertJsonEqual(new List<PyDict> { difference }, remaining);
        Assert.Empty(verified);
    }

    [Fact]
    public void OnlyTrailingAsciiAndUserCommentPaddingIsIgnoredAsFormat()
    {
        var accepted = new Counter();
        var expected = Rows("""[{"key": "Exif.Image.Make", "type": "Ascii", "count": 8, "value": "Camera "}]""");
        var actual = Rows("""[{"key": "Exif.Image.Make", "type": "Ascii", "count": 8, "value": "Camera"}]""");
        Assert.Empty(TagPrint.CompareRows(expected, actual, acceptedPadding: accepted));
        AssertJson("""{"Ascii": 1}""", accepted);
        expected = Rows("""[{"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "value": "50/1 "}]""");
        actual = Rows("""[{"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "value": "50/1"}]""");
        Assert.Single(TagPrint.CompareRows(expected, actual));
    }

    [Fact]
    public void TiffIfdOffsetsMatchTheSameLongValue()
    {
        var accepted = new Counter();
        var expected = Rows("""[{"key": "Exif.Olympus2.CameraSettings", "type": "Ifd", "count": 1, "value": "512"}]""");
        var actual = Rows("""[{"key": "Exif.Olympus2.CameraSettings", "type": "Long", "count": 1, "value": "512"}]""");
        Assert.Empty(TagPrint.CompareRows(expected, actual, acceptedTypeAliases: accepted));
        var hit = Assert.Single(accepted);
        Assert.Equal(new PyTuple("Ifd", "Long"), hit.Key);
        Assert.Single(TagPrint.CompareRows(expected, [With(actual[0], "value", "513")]));
    }

    [Fact]
    public void MultilineTextOnlyAcceptsExactPinnedFirstLine()
    {
        var accepted = new Counter();
        var expected = Rows("""[{"key": "Exif.Image.ImageDescription", "type": "Ascii", "count": 20, "value": "caption"}]""");
        var actual = Rows("""[{"key": "Exif.Image.ImageDescription", "type": "Ascii", "count": 20, "value": "caption\nlonger description"}]""");
        Assert.Empty(TagPrint.CompareRows(expected, actual, acceptedLineTruncations: accepted));
        AssertJson("""{"Ascii": 1}""", accepted);
        Assert.Single(TagPrint.CompareRows(expected, [With(actual[0], "value", "different\nlonger description")]));
        Assert.Single(TagPrint.CompareRows([With(expected[0], "type", "Undefined")], [With(actual[0], "type", "Undefined")]));
    }

    [Fact]
    public void AValueThatEndsAtTheReferenceLineBreakMatchesIt()
    {
        var accepted = new Counter();
        var expected = Rows("""[{"key": "Exif.Photo.UserComment", "type": "Undefined", "count": 4980, "value": "charset=Ascii "}]""");
        var actual = Rows("""[{"key": "Exif.Photo.UserComment", "type": "Undefined", "count": 4980, "value": "charset=Ascii \n"}]""");
        Assert.Empty(TagPrint.CompareRows(expected, actual, acceptedLineTruncations: accepted));
        AssertJson("""{"Undefined": 1}""", accepted);
        // Only a line break may continue the reference text; any other continuation still differs.
        foreach (string continuation in new[] { "charset=Ascii x", "charset=Ascii\r" })
            Assert.Single(TagPrint.CompareRows(expected, [With(actual[0], "value", continuation)]));
    }

    [Fact]
    public void DumpProtocolUnescapesRecordsAndRejectsOpenOrMalformedBlocks()
    {
        var parsed = TagPrint.ParseDump("FILE\t/tmp/a\\tb.jpg\nTAG\tExif.Image.Make\tAscii\t2\tA\\\\B\t\nSTATUS\tok\n");
        Assert.Equal("A\\B", ((PyDict)parsed.Dict("/tmp/a\tb.jpg").List("rows")[0]!).Str("raw"));
        Assert.Throws<TagPrintError>(() => TagPrint.ParseDump("FILE\ta\nFILE\tb\nSTATUS\tok\n"));
        Assert.Throws<TagPrintError>(() => TagPrint.ParseDump("FILE\ta\nTAG\tExif.Image.Width\tLong\tbad\t1\nSTATUS\tok\n"));
    }

    [Fact]
    public void EachUnmatchedReferenceRequiresANamedReason()
    {
        Assert.Equal(new HashSet<string>
        {
            "addmoddel", "conversions", "crw-test", "exifdata-test", "exiv2-bug1145a.exv_test", "exiv2-test", "geotag-test", "icc-test",
            "iotest", "iso65k-test", "modify-test", "nls-test", "png-test", "preview-test", "stdin-test", "stringto-test",
            "test_pr_1905_poc1_ref", "tiff-test", "webp-test", "write-test-debug", "write-test", "write2-test", "xmpparser-test",
        }, TagPrint.MissingInputExceptions.Keys.ToHashSet());
        Assert.DoesNotContain(TagPrint.MissingInputExceptions.Values, string.IsNullOrWhiteSpace);
        Assert.Equal(new HashSet<string> { "Exif.GPSInfo.GPSAltitudeRef", "Exif.Canon.Categories", "Exif.Canon.PictureStylePC", "Exif.Canon.FilterInfo" },
                     TagPrint.KeyExceptions.Keys.ToHashSet());
        Assert.DoesNotContain(TagPrint.KeyExceptions.Values, string.IsNullOrWhiteSpace);
        Assert.Contains("Exif.Image.ImageWidth", TagPrint.CatalogKeys);
        Assert.DoesNotContain("Exif.Canon.0x0019", TagPrint.CatalogKeys);
        Assert.Equal(new HashSet<(string, string)>
        {
            ("BlueSquare.xmp", "Exif.*"), ("StaffPhotographer-Example.xmp", "Exif.*"), ("exiv2-bug1112.xmp", "Exif.*"),
            ("issue_1959_poc.xmp", "Exif.*"), ("issue_1998.xmp", "Exif.*"),
        }, TagPrint.RowExceptions.Keys.Select(key => (key.File, key.Pattern)).ToHashSet());
        Assert.DoesNotContain(TagPrint.RowExceptions.Values, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void RowRulesCoverOnlyTheirNamedFileKeyAndReferenceRow()
    {
        var sidecar = D("""{"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "value": "1"}""");
        Assert.NotNull(TagPrint.RowException("BlueSquare.xmp", sidecar, hasLocalExif: false));
        Assert.Null(TagPrint.RowException("BlueSquare.xmp", sidecar, hasLocalExif: true));
        Assert.Null(TagPrint.RowException("issue_1713_poc.xmp", sidecar, hasLocalExif: false));
        // Only the sidecar conversion remains: every other file is compared row by row.
        Assert.Null(TagPrint.RowException("IMG_1361.dng", D("""{"key": "Exif.SubImage1.ImageWidth"}"""), hasLocalExif: true));
        Assert.Null(TagPrint.RowException("exiv2-bug447.jpg", D("""{"key": "Exif.Photo.UserComment", "type": "0x000e", "count": 125, "value": ""}"""),
                                          hasLocalExif: true));
    }

    [Fact]
    public void ADocumentedRowRuleIsReportedAndItsLocalRowEndsIt()
    {
        var candidate = new Candidate("BlueSquare.xmp", "/tmp/BlueSquare.xmp", "test");
        string reference = PtLine("Exif.Image.ImageWidth", "Long", 1, "1") + "\n";
        var dump = D("""{"/tmp/BlueSquare.xmp": {"status": "ok", "rows": []}}""");
        var report = TagPrint.BuildReport([candidate], [], dump, runReference: (_, _, _) => Reference(reference));
        Assert.Equal(0L, report.Dict("totals").Get("failed_files", 0L));
        AssertJson($$"""
            [{"file": "BlueSquare.xmp", "key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "occurrences": 1,
              "rule": {{PyJson.Dumps(TagPrint.RowExceptions[("BlueSquare.xmp", "Exif.*")])}}}]
            """, report["row_rule_hits"]);
        // A file that does print EXIF rows is not the case the rule was reviewed for: its rows are compared, and a
        // difference between them still fails the gate.
        dump.Dict("/tmp/BlueSquare.xmp")["rows"] = J("""[{"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "raw": "2", "translated": ""}]""");
        report = TagPrint.BuildReport([candidate], [], dump, runReference: (_, _, _) => Reference(reference));
        Assert.Equal(1L, report.Dict("totals").Get("failed_files", 0L));
        Assert.Empty(report.List("row_rule_hits"));
    }

    [Fact]
    public void EveryObservedRefusalIsNamedAndRequiresTheSameResult()
    {
        Assert.Equal(38, TagPrint.RefusalExceptions.Count);
        var faults = new List<string>();
        foreach (var (name, refusal) in TagPrint.RefusalExceptions)
        {
            if (string.IsNullOrWhiteSpace(refusal.Reason)) faults.Add(name + ": no reason");
            var problem = PyDict.Of(("kind", refusal.Kind), ("status", refusal.Status), ("detail", refusal.Detail ?? "observed exiv2 detail"));
            if (TagPrint.DocumentRefusal(name, problem) is null) faults.Add(name + ": the observed result is not documented");
            if (TagPrint.DocumentRefusal(name, With(problem, "status", "changed")) is not null) faults.Add(name + ": another status is documented");
            if (refusal.Detail is not null && TagPrint.DocumentRefusal(name, With(problem, "detail", "different-code")) is not null)
                faults.Add(name + ": another detail is documented");
        }
        Assert.Empty(faults);
    }

    [Fact]
    public void OnlyNumericRawTagsWithoutAPinnedNamedRowAreReportedAsRawOnly()
    {
        Assert.True(TagPrint.IsRawNumericTag("Exif.Canon.0x0019"));
        Assert.False(TagPrint.IsRawNumericTag("Exif.Photo.ImageWidth"));
        Assert.True(TagPrint.IsRawNumericTag("Exif.Canon.0X0019"));
    }

    [Fact]
    public void UnregisteredNumericFallbackIsReportedButUnknownNamedKeyStillFails()
    {
        var candidate = new Candidate("sample.jpg", "/tmp/sample.jpg", "test");
        var dump = D("""
            {"/tmp/sample.jpg": {"status": "ok", "rows": [
                {"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "raw": "160", "translated": ""},
                {"key": "Exif.Canon.0x0019", "type": "Short", "count": 1, "raw": "1", "translated": "1"}]}}
            """);
        string reference = PtLine("Exif.Image.ImageWidth", "Long", 1, "160") + "\n";
        var report = TagPrint.BuildReport([candidate], [], dump, runReference: (_, _, _) => Reference(reference));
        Assert.Equal(0L, report.Dict("totals").Get("failed_files", 0L));
        Assert.Equal("Exif.Canon.0x0019", ((PyDict)report.List("raw_only_tag_exceptions")[0]!).Str("key"));
        dump.Dict("/tmp/sample.jpg").List("rows").Add(D("""{"key": "Exif.Canon.CameraSerialNumber", "type": "Ascii", "count": 1, "raw": "x", "translated": "x"}"""));
        report = TagPrint.BuildReport([candidate], [], dump, runReference: (_, _, _) => Reference(reference));
        Assert.Equal(1L, report.Dict("totals")["failed_files"]);
        Assert.Equal("extra", ((PyDict)((PyDict)report.List("differing")[0]!).List("rows")[0]!).Str("kind"));
    }

    [Fact]
    public void AnnotationChunksKeepAllFailureGroupsAndRefusalsUnderGithubLimit()
    {
        var payload = PyDict.Of(
            ("totals", PyDict.Of(("files", 40L))),
            ("mismatch_groups", Enumerable.Range(0, 40).Select(i => (object?)PyDict.Of(
                ("category", "description"), ("key", $"Exif.Photo.Tag{i}"), ("type", "Short"), ("occurrences", (long)i),
                ("examples", new List<object?> { PyDict.Of(("difference", "text differs"), ("occurrences", (long)i)) }))).ToList()),
            ("refusals", Enumerable.Range(0, 34).Select(i => (object?)PyDict.Of(
                ("file", $"failed-{i}.jpg"), ("kind", "metadatasharp-refused"), ("detail", new string('a', 160)))).ToList()),
            ("value_diff_by_key", Enumerable.Range(0, 30).Select(i => (object?)PyDict.Of(
                ("key", $"Exif.Photo.Tag{i}"), ("exiv2", "x"), ("metadatasharp", "y"), ("occurrences", (long)i))).ToList()),
            ("display_rule_hits", new List<object?>()));
        string output = Capture(() =>
        {
            TagPrint.EmitAnnotation(payload);
            return 0;
        }).Stdout;
        var lines = PyText.SplitLines(output);
        Assert.True(lines.Count > 1);
        Assert.All(lines, line => Assert.True(line.Length < 4000));
        Assert.Equal(2, lines.Count(line => line.Contains("refusals ", StringComparison.Ordinal)));
    }

    [Fact]
    public void ReportGroupsTextOnlyDifferencesByKeyAndCategory()
    {
        var candidate = new Candidate("metering.jpg", "/tmp/metering.jpg", "test");
        var dump = D("""
            {"/tmp/metering.jpg": {"status": "ok", "rows": [
                {"key": "Exif.Photo.MeteringMode", "type": "Short", "count": 1, "raw": "5", "translated": "Pattern"}]}}
            """);
        string reference = PtLine("Exif.Photo.MeteringMode", "Short", 1, "Multi-segment") + "\n";
        var report = TagPrint.BuildReport([candidate], [], dump, runReference: (_, _, _) => Reference(reference));
        AssertJson($$"""
            [{"key": "Exif.Photo.MeteringMode", "exiv2": "Multi-segment", "metadatasharp": "Pattern", "occurrences": 1,
              "rule": {{PyJson.Dumps(TagPrint.DisplayReasons["Exif.Photo.MeteringMode"])}}}]
            """, report["display_rule_hits"]);
        Assert.Empty(report.List("mismatch_groups"));
    }
}
