using MetadataSharp.Harness.Parity;
using MetadataSharp.Harness.Python;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Parity;

// The interpreted MakerNote gate (formerly tests/parity/test_makernote_render_gate.py).
public sealed class MakerNoteRenderGateTests
{
    private const string Group = "Exif.PentaxDng";

    private static PyDict Report(string interpreted = "match-interpreted", string raw = "match-value", long rowsDropped = 0) => D("""
        {"by_family": {"@G": {"match-value": 1}},
         "interpreted": {"by_family": {"@G": {"match-interpreted": 1}}},
         "per_file": [{"corpus": "exiv2", "label": "exiv2:sample.dng", "rows_dropped": @DROPPED,
                       "rows": [{"key": "@G.Quality", "outcome": "@RAW", "translated": true, "interpreted": "@INTERPRETED",
                                 "our_interpreted": "Best", "their_interpreted": "Best"}]}]}
        """.Replace("@G", Group, StringComparison.Ordinal).Replace("@DROPPED", rowsDropped.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
           .Replace("@RAW", raw, StringComparison.Ordinal).Replace("@INTERPRETED", interpreted, StringComparison.Ordinal));

    private static List<object?> Rows(PyDict report) => report.List("per_file").Cast<PyDict>().First().List("rows");

    private static OrderedDictionary<string, string> Exceptions(string key, string reason) => new(StringComparer.Ordinal) { [key] = reason };

    [Fact]
    public void ReportsRawAndInterpretedCountsSeparately()
    {
        var metrics = MakerNoteRenderGate.GroupMetrics(Report(), Group);
        Assert.Equal(1, metrics.Long("keys_found"));
        Assert.Equal(1, metrics.Long("raw_values_equal"));
        Assert.Equal(1, metrics.Long("interpreted_keys_found"));
        Assert.Equal(1, metrics.Long("interpreted_values_equal"));
        Assert.True(MakerNoteRenderGate.Evaluate(Report(), [Group]).Passed);
    }

    [Theory]
    [InlineData("not-interpreted")]
    [InlineData("interpreted-diff")]
    public void UntranslatedOrDifferentRenderingFails(string verdict)
    {
        var (passed, message) = MakerNoteRenderGate.Evaluate(Report(interpreted: verdict), [Group]);
        Assert.False(passed);
        Assert.Contains("interpreted=" + Py.Repr(verdict), message);
    }

    [Theory]
    [InlineData("value-diff")]
    [InlineData("match-key")]
    public void RawKeyOrValueDifferenceFails(string outcome)
    {
        var report = Report(raw: outcome);
        report.Dict("by_family")[Group] = PyDict.Of((outcome, 1L));
        var (passed, message) = MakerNoteRenderGate.Evaluate(report, [Group]);
        Assert.False(passed);
        Assert.Contains("value-diff=1", message);
    }

    [Fact]
    public void UnknownNumericTagFallbackIsCountedButDoesNotFailRawParity()
    {
        var report = Report();
        report.Dict("by_family").Dict(Group)["extra-key"] = 1L;
        Rows(report).Add(D($$"""{"key": "{{Group}}.0x006b", "outcome": "extra-key"}"""));
        var (passed, message) = MakerNoteRenderGate.Evaluate(report, [Group]);
        Assert.True(passed, message);
        Assert.Contains("retained numeric unknown-tag rows: 1", message);
    }

    [Fact]
    public void NamedExtraKeyStillFails()
    {
        var report = Report();
        report.Dict("by_family").Dict(Group)["extra-key"] = 1L;
        Rows(report).Add(D($$"""{"key": "{{Group}}.UnexpectedNamedTag", "outcome": "extra-key"}"""));
        var (passed, message) = MakerNoteRenderGate.Evaluate(report, [Group]);
        Assert.False(passed);
        Assert.Contains("raw outcome extra-key", message);
    }

    [Fact]
    public void ExactDocumentedCanonRawExtrasAreRetained()
    {
        foreach (string key in MakerNoteRenderGate.DocumentedRawExtraKeys.Keys)
        {
            string group = key[..key.LastIndexOf('.')];
            var report = D("""
                {"by_family": {"@G": {"match-value": 1, "extra-key": 1}},
                 "interpreted": {"by_family": {"@G": {"match-interpreted": 1}}},
                 "per_file": [{"label": "exiv2:sample.exv", "rows_dropped": 0, "rows": [
                     {"key": "@G.Known", "outcome": "match-value", "translated": true, "interpreted": "match-interpreted",
                      "our_interpreted": "x", "their_interpreted": "x"},
                     {"key": "@K", "outcome": "extra-key"}]}]}
                """.Replace("@G", group, StringComparison.Ordinal).Replace("@K", key, StringComparison.Ordinal));
            var (passed, message) = MakerNoteRenderGate.Evaluate(report, [group]);
            Assert.True(passed, key + ": " + message);
            Assert.Contains("retained documented raw extra", message);
        }
    }

    [Fact]
    public void ExactDocumentedFileRefusalIsScopedByNameAndReason()
    {
        var report = Report();
        report.Dict("by_family")[Group] = PyDict.Of(("not-read", 1L));
        report["per_file"] = J($$"""
            [{"label": "exiv2:issue_1706_poc.exv", "detail": "LimitExceeded", "status": "refused",
              "rows_dropped": 0, "rows": [{"key": "{{Group}}.Quality", "outcome": "not-read"}]}]
            """);
        var (passed, message) = MakerNoteRenderGate.Evaluate(report, [Group]);
        Assert.True(passed, message);
        Assert.Contains("documented reader refusal", message);
        ((PyDict)report.List("per_file")[0]!)["detail"] = "InvalidExif";
        (passed, message) = MakerNoteRenderGate.Evaluate(report, [Group]);
        Assert.False(passed);
        Assert.Contains("raw outcome not-read", message);
    }

    [Fact]
    public void AllRowsMayBeADocumentedExactReaderRefusal()
    {
        var report = D("""
            {"by_family": {"Exif.PentaxDng": {"not-read": 1}}, "interpreted": {"by_family": {}},
             "per_file": [{"label": "exiv2:issue_1706_poc.exv", "detail": "LimitExceeded", "status": "refused", "rows_dropped": 0,
                           "rows": [{"key": "Exif.PentaxDng.Quality", "outcome": "not-read"}]}]}
            """);
        var (passed, message) = MakerNoteRenderGate.Evaluate(report, ["Exif.PentaxDng"]);
        Assert.True(passed, message);
    }

    [Fact]
    public void NotReadAttributionExplainsEachRowByExactFileRefusal()
    {
        var report = D("""
            {"per_file": [
                {"label": "exiv2:poc_2542.exv", "status": "refused", "detail": "AmbiguousMetadata", "rows": [
                    {"key": "Exif.OlympusCs.A", "outcome": "not-read"}, {"key": "Exif.OlympusEq.B", "outcome": "not-read"}]},
                {"label": "exiv2:issue_1706_poc.exv", "status": "refused", "detail": "LimitExceeded", "rows": [
                    {"key": "Exif.PentaxDng.C", "outcome": "not-read"}]},
                {"label": "exiv2:surprise.exv", "status": "refused", "detail": "InvalidExif", "rows": [
                    {"key": "Exif.PentaxDng.D", "outcome": "not-read"}]}]}
            """);
        var rows = MakerNoteRenderGate.UnreadAttribution(report);
        Assert.Equal([("Exif.OlympusCs", 1L), ("Exif.OlympusEq", 1L), ("Exif.PentaxDng", 1L), ("Exif.PentaxDng", 1L)],
                     rows.Select(row => (row.Str("group"), row.Long("rows"))).ToList());
        Assert.Equal("the file has ambiguous EXIF metadata", rows[0].Str("reason"));
        Assert.Equal("the file has ambiguous EXIF metadata", rows[1].Str("reason"));
        Assert.Equal("the EXIF directory exceeds configured reader limits", rows[2].Str("reason"));
        Assert.StartsWith("UNDOCUMENTED", rows[3].Str("reason"));
        Assert.Equal("refused", rows[3].Str("status"));
    }

    [Fact]
    public void TruncatedRowsFailClosed()
    {
        var (passed, message) = MakerNoteRenderGate.Evaluate(Report(rowsDropped: 1), [Group]);
        Assert.False(passed);
        Assert.Contains("report dropped rows", message);
    }

    [Fact]
    public void ExactDocumentedKeyExceptionIsScopedToThatKey()
    {
        var report = Report(interpreted: "not-interpreted");
        Assert.False(MakerNoteRenderGate.Evaluate(report, [Group], Exceptions(Group + ".LensType", "lens database excluded")).Passed);
        var (passed, message) = MakerNoteRenderGate.Evaluate(report, [Group], Exceptions(Group + ".Quality", "explicit corpus exception"));
        Assert.True(passed, message);
    }

    [Fact]
    public void LensCodesAreNoLongerExcepted()
    {
        // The lens tables of the notes are part of this project, so a lens code is graded like any other
        // interpreted value, and its raw value still has to match.
        foreach (string key in new[]
                 {
                     "Exif.CanonCs.LensType", "Exif.Pentax.LensType", "Exif.PentaxDng.LensType", "Exif.NikonLd2.LensIDNumber",
                     "Exif.NikonLd3.LensIDNumber", "Exif.NikonLd4.LensIDNumber", "Exif.NikonLd4.LensID", "Exif.Sony1.LensID",
                     "Exif.Sony2.LensID", "Exif.SonyMinolta.LensID", "Exif.Minolta.LensID",
                 })
        {
            Assert.DoesNotContain(key, MakerNoteRenderGate.DocumentedExceptions.Keys);
        }
        var report = D("""
            {"by_family": {"Exif.NikonLd4": {"match-value": 1}},
             "interpreted": {"by_family": {"Exif.NikonLd4": {"not-interpreted-translated": 1}}},
             "per_file": [{"label": "exiv2:sample.exv", "rows_dropped": 0, "rows": [{
                 "key": "Exif.NikonLd4.LensIDNumber", "outcome": "match-value", "translated": true,
                 "interpreted": "not-interpreted", "our_interpreted": "(119)",
                 "their_interpreted": "Nikon AF-S VR Zoom-Nikkor 70-200mm f/2.8G IF-ED"}]}]}
            """);
        var (passed, message) = MakerNoteRenderGate.Evaluate(report, ["Exif.NikonLd4"], MakerNoteRenderGate.DocumentedExceptions);
        Assert.False(passed);
        Assert.Contains("interpreted", message);
        ((PyDict)Rows(report)[0]!)["outcome"] = "value-diff";
        report.Dict("by_family")["Exif.NikonLd4"] = PyDict.Of(("value-diff", 1L));
        (passed, message) = MakerNoteRenderGate.Evaluate(report, ["Exif.NikonLd4"], MakerNoteRenderGate.DocumentedExceptions);
        Assert.False(passed);
        Assert.Contains("raw parity has missing=0, value-diff=1", message);
    }

    [Fact]
    public void AllMakerNoteGroupDiscoveryIncludesRawOnlyVendors()
    {
        var report = D("""
            {"by_family": {"Exif.Image": {"match-value": 1}, "Exif.Photo": {"match-value": 1}, "Exif.Iop": {"match-value": 1},
                           "Exif.SubImage1": {"match-value": 1}, "Exif.Panasonic": {"match-value": 1},
                           "Exif.OlympusCs": {"extra-key": 1}, "Exif.Casio": {"match-value": 1}},
             "interpreted": {"by_family": {"Exif.OlympusCs": {"not-interpreted-translated": 2}}}}
            """);
        Assert.Equal(["Exif.Casio", "Exif.OlympusCs", "Exif.Panasonic"], MakerNoteRenderGate.MakerNoteGroups(report));
    }

    [Fact]
    public void RawOnlyMakerNoteFamilyIsStillGated()
    {
        var report = D("""
            {"by_family": {"Exif.Kodak": {"match-value": 1}}, "interpreted": {"by_family": {}},
             "per_file": [{"label": "exiv2:sample.jpg", "rows_dropped": 0, "rows": [{"key": "Exif.Kodak.0x0001", "outcome": "match-value"}]}]}
            """);
        var (passed, message) = MakerNoteRenderGate.Evaluate(report, ["Exif.Kodak"]);
        Assert.True(passed, message);
        Assert.Contains("interpreted keys found/equal=0/0", message);
        report.Dict("by_family")["Exif.Kodak"] = PyDict.Of(("missing-key", 1L));
        ((PyDict)Rows(report)[0]!)["outcome"] = "missing-key";
        (passed, message) = MakerNoteRenderGate.Evaluate(report, ["Exif.Kodak"]);
        Assert.False(passed);
        Assert.Contains("raw parity has missing=1", message);
    }

    [Fact]
    public void InventoryOrdersLargestUnrenderedGapFirst()
    {
        var report = D("""
            {"by_family": {}, "interpreted": {"by_family": {"Exif.Other": {"not-interpreted-translated": 2},
                                                            "Exif.PentaxDng": {"not-interpreted-translated": 7}}}}
            """);
        Assert.Equal(["Exif.PentaxDng", "Exif.Other"], MakerNoteRenderGate.Inventory(report).Select(row => row.Group).ToList());
    }

    [Fact]
    public void GapDetailsKeepActionableExamples()
    {
        var details = MakerNoteRenderGate.GapDetails(Report(interpreted: "not-interpreted"), [Group]);
        var first = details[Group][0];
        Assert.Equal(Group + ".Quality", first.Str("key"));
        Assert.Equal(["exiv2:sample.dng"], first.List("files").Cast<string>().ToList());
        Assert.Equal("not-interpreted", first.Str("interpretation"));
        Assert.Contains("our_value", first.Keys);
        var report = Report();
        Rows(report).Add(D($$"""{"key": "{{Group}}.NamedExtra", "outcome": "extra-key", "our_type": "Byte", "our_count": 1, "our_value": "7"}"""));
        details = MakerNoteRenderGate.GapDetails(report, [Group]);
        Assert.Contains(Group + ".NamedExtra", details[Group].Select(row => row.Str("key")));
    }
}
