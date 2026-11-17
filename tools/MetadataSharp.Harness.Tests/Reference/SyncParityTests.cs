using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Reference;

// Offline checks for the comparison of this library's conversions with the pinned tool's (formerly
// tests/reference/test_sync_parity.py). Neither the tool nor the fixture generator is run here: a runner stands in
// for both.
public sealed class SyncParityTests
{
    private const string PrintedText =
        "Xmp.tiff.Make                                Canon\n"
        + "Xmp.exif.ISOSpeedRatings                     100 200\n"
        + "Xmp.dc.description                           lang=\"x-default\" Two words\n"
        + "Xmp.exif.Empty\n"
        + "Exif.Image.Make                              Canon\n";

    private static OrderedDictionary<string, List<string>> Values(params (string Key, string[] Values)[] pairs)
    {
        var found = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, values) in pairs) found[key] = [.. values];
        return found;
    }

    /// <summary>Runs a block with the documented differences as they are, and puts them back after it.</summary>
    internal static void KeepingDocumented(Action block)
    {
        var saved = SyncParity.Documented.ToList();
        try
        {
            block();
        }
        finally
        {
            SyncParity.Documented.Clear();
            foreach (var (key, reason) in saved) SyncParity.Documented[key] = reason;
        }
    }

    // --- the print-out

    [Fact]
    public void PropertiesAreTheKeysOfOneKindWithTheirValues()
    {
        var found = SyncParity.Properties(PrintedText, "Xmp.");
        Assert.Equal(["Xmp.tiff.Make", "Xmp.exif.ISOSpeedRatings", "Xmp.dc.description", "Xmp.exif.Empty"], found.Keys.ToList());
        Assert.Equal(["lang=\"x-default\" Two words"], found["Xmp.dc.description"]);
        Assert.Equal([""], found["Xmp.exif.Empty"]);
        var exif = SyncParity.Properties(PrintedText, "Exif.");
        Assert.Equal(["Exif.Image.Make"], exif.Keys.ToList());
        Assert.Equal(["Canon"], exif["Exif.Image.Make"]);
    }

    [Fact]
    public void AKeyPrintedTwiceKeepsBothValues()
    {
        var found = SyncParity.Properties("Xmp.a.b 1\nXmp.a.b 2\n", "Xmp.");
        Assert.Equal(["Xmp.a.b"], found.Keys.ToList());
        Assert.Equal(["1", "2"], found["Xmp.a.b"]);
    }

    // --- outcomes

    [Theory]
    [InlineData("same", "1/250", "1/250")]
    [InlineData("form", "100 200", "100, 200")]
    [InlineData("differs", "100 200", "100, 201")]
    [InlineData("form", "36/1 26/1 54/1", "36/1 26/1 540000/10000")]
    [InlineData("differs", "36/1 26/1 54/1", "36/1 26/1 541/10")]
    [InlineData("differs", "1/0", "2/0")]
    [InlineData("character code", "charset=Unicode a b", "charset=Ascii a b")]
    [InlineData("differs", "charset=Unicode a b", "charset=Ascii a c")]
    [InlineData("differs", "charset=Ascii HYBRID-FIX", "charset=Ascii charset=Ascii HYBRID-FIX")]
    [InlineData("tool only", "1", null)]
    [InlineData("tool only (empty)", "lang=\"x-default\"", null)]
    [InlineData("tool only (empty)", "charset=Unicode", null)]
    [InlineData("tool only (empty)", "", null)]
    [InlineData("tool only", "lang=\"x-default\" text", null)]
    [InlineData("ours only", null, "1")]
    public void Outcomes(string expected, string? theirs, string? ours) =>
        Assert.Equal(expected, SyncParity.Outcome(theirs is null ? null : [theirs], ours is null ? null : [ours]));

    [Fact]
    public void CompareListsEveryKeyOfEitherSideOnceAndLeavesOutTheIgnored()
    {
        var theirs = Values(("Exif.Image.Make", ["Canon"]), ("Exif.Photo.FNumber", ["4/1"]));
        var ours = Values(("Exif.Image.Make", ["Canon"]), ("Exif.Image.ExifTag", ["90"]), ("Exif.Image.Model", ["X"]));
        var rows = SyncParity.Compare(theirs, ours, SyncParity.Structure);
        Assert.Equal([("Exif.Image.Make", "same"), ("Exif.Photo.FNumber", "tool only"), ("Exif.Image.Model", "ours only")], rows.Select(row => (row.Key, row.Outcome)));
    }

    [Fact]
    public void ADocumentedDifferenceIsFoundByKeyAndByPrefix() => KeepingDocumented(() =>
    {
        SyncParity.Documented.Clear();
        SyncParity.Document(SyncParity.ToXmp, "Xmp.exif.A", "differs, tool only", "reason A");
        SyncParity.Document(SyncParity.ToExif, "Exif.GPSInfo.*", "differs", "reason B");
        Assert.Equal("reason A", SyncParity.IsDocumented(SyncParity.ToXmp, "Xmp.exif.A", "tool only"));
        Assert.Null(SyncParity.IsDocumented(SyncParity.ToXmp, "Xmp.exif.A", "ours only"));
        Assert.Null(SyncParity.IsDocumented(SyncParity.ToExif, "Xmp.exif.A", "differs"));
        Assert.Equal("reason B", SyncParity.IsDocumented(SyncParity.ToExif, "Exif.GPSInfo.GPSLatitude", "differs"));
    });
}

/// <summary>A corpus of two files; the stand-in tool refuses to rewrite the second.</summary>
public sealed class SyncParityRunTests : IDisposable
{
    private readonly TempDirectory directory = Temp();
    private readonly string corpus, work;
    private string oursXmp = "Xmp.tiff.Make Canon\nXmp.exif.ISOSpeedRatings 100, 200\n";
    private string back = "Exif.Image.Make Canon\nExif.Image.ExifTag 90\n";

    public SyncParityRunTests()
    {
        corpus = directory["corpus"];
        work = directory["work"];
        Directory.CreateDirectory(corpus);
        File.WriteAllBytes(PyPath.Join(corpus, "a.jpg"), "a"u8.ToArray());
        File.WriteAllBytes(PyPath.Join(corpus, "b.JPG"), "b"u8.ToArray());
        File.WriteAllBytes(PyPath.Join(corpus, "c.png"), "c"u8.ToArray());
    }

    public void Dispose() => directory.Dispose();

    private ToolResult Runner(IReadOnlyList<string> command, int timeout = 120)
    {
        if (command[0] != "exiv2")
        {
            Assert.Equal(["fixture", "--", "sync-parity", work], command);
            foreach (string name in new[] { "s00000" })
            {
                File.WriteAllText(PyPath.Join(work, name + ".ours.xmp"), "ours");
                File.WriteAllText(PyPath.Join(work, name + ".back.jpg"), "back");
            }
            return new(0, "::notice title=Sync parity fixture::done\n", "");
        }
        string mode = command[1], path = command[^1];
        switch (mode)
        {
            case "-d":
                Assert.Equal("xi", command[2]);
                return File.ReadAllBytes(path).AsSpan().SequenceEqual("b"u8) ? new(1, "", "refused") : new(0, "", "");
            case "-eX":
                File.WriteAllText(Path.ChangeExtension(path, ".xmp"), "theirs");
                return new(0, "", "");
            case "-PXkv":
                return new(0, PyPath.Name(path).EndsWith(".ours.xmp", StringComparison.Ordinal) ? oursXmp : "Xmp.tiff.Make Canon\nXmp.exif.ISOSpeedRatings 100 200\n", "");
            case "-PEkv":
                return new(0, PyPath.Name(path).EndsWith(".back.jpg", StringComparison.Ordinal) ? back : "Exif.Image.Make Canon\n", "");
            default:
                throw new InvalidOperationException(string.Join(" ", command));
        }
    }

    private (int Code, string Out) Main(Runner? runner = null)
    {
        var (code, stdout, _) = Capture(() => SyncParity.Execute([corpus, work, "--exiv2", "exiv2", "--fixture", "fixture --"], runner ?? Runner));
        return (code, stdout);
    }

    [Fact]
    public void TheSameKeysAndValuesPassAndAFormIsCounted()
    {
        var (code, output) = Main();
        if (code != 0) Assert.Fail(output);
        Assert.Contains("1 files compared of 1 prepared (1 not rewritten by the tool); left out, not read by this library: none; "
                        + "values: same 1, form 1, differs 0, character code 0, tool only 0", output);
        Assert.Contains("XMP to EXIF::1 files compared of 1 prepared", output);
        Assert.Contains("EXIF to XMP Xmp.exif.ISOSpeedRatings: 1; s00000: tool ['100 200'] ours ['100, 200']", output);
        Assert.False(File.Exists(PyPath.Join(work, "s00001.jpg")));
    }

    [Fact]
    public void ADifferenceThatIsNotDocumentedFails()
    {
        oursXmp = "Xmp.tiff.Make Nikon\n";
        back = "Exif.Image.Model X\n";
        var (code, output) = Main();
        if (code != 1) Assert.Fail(output);
        Assert.Contains("FAIL EXIF to XMP Xmp.tiff.Make differs: 1; s00000: tool ['Canon'] ours ['Nikon']", output);
        Assert.Contains("FAIL EXIF to XMP Xmp.exif.ISOSpeedRatings tool only: 1", output);
        Assert.Contains("FAIL XMP to EXIF Exif.Image.Make tool only: 1", output);
        Assert.Contains("FAIL XMP to EXIF Exif.Image.Model ours only: 1", output);
        Assert.Contains("::error title=Sync parity - differences that are not documented::", output);
    }

    [Fact]
    public void ADocumentedDifferencePassesAndIsReportedWithItsReason()
    {
        oursXmp = "Xmp.tiff.Make Nikon\nXmp.exif.ISOSpeedRatings 100 200\n";
        (int Code, string Out) result = default;
        SyncParityTests.KeepingDocumented(() =>
        {
            SyncParity.Document(SyncParity.ToXmp, "Xmp.tiff.*", "differs", "an authored reason");
            result = Main();
        });
        if (result.Code != 0) Assert.Fail(result.Out ?? "");
        Assert.Contains("EXIF to XMP Xmp.tiff.Make differs: 1; an authored reason; s00000", result.Out);
    }

    [Fact]
    public void AFileThisLibraryRefusesIsLeftOutAndCounted()
    {
        ToolResult Refusing(IReadOnlyList<string> command, int timeout = 120)
        {
            var result = Runner(command, timeout);
            if (command[0] != "exiv2")
            {
                File.Delete(PyPath.Join(work, "s00000.ours.xmp"));
                Write(PyPath.Join(work, "ours.tsv"),
                      "s00000\tto-xmp\trefused InvalidJpeg\ta reason\ns00000\tto-exif\twritten\tExif.A -> Xmp.b: a note | Exif.C -> Xmp.d: another\n");
            }
            return result;
        }
        var (code, text) = Main(Refusing);
        if (code != 0) Assert.Fail(text);
        Assert.Contains("EXIF to XMP::0 files compared of 1 prepared (1 not rewritten by the tool); left out, not read by this library: 1 refused InvalidJpeg; values: same 0", text);
        Assert.Contains("1 file(s), first s00000: to-xmp refused InvalidJpeg: a reason", text);
        Assert.Contains("1 file(s), first s00000: to-exif Exif.C -> Xmp.d: another", text);
    }

    [Fact]
    public void AFailingFixtureGeneratorFails()
    {
        var (code, output) = Main((command, timeout) => command[0] != "exiv2" ? new(3, "", "boom") : Runner(command, timeout));
        Assert.Equal(1, code);
        Assert.Contains("the fixture generator failed (exit 3)", output);
    }
}
