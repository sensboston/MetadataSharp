using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Reference;

// Offline checks for the comparison of this library's file operations with the pinned tool's (formerly
// tests/reference/test_operations_parity.py). Neither the tool nor the fixture generator is run here: a runner
// stands in for the tool.
public sealed class OperationsParityTests
{
    private static OrderedDictionary<string, List<string>> Lines(params (string Key, string[] Values)[] keys)
    {
        var found = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, values) in keys) found[key] = [.. values];
        return found;
    }

    private static OrderedDictionary<string, List<string>>? Printed(int code, string output)
    {
        using var folder = Temp();
        string path = folder["a.jpg"];
        File.WriteAllBytes(path, "x"u8.ToArray());
        return OperationsParity.Printed("exiv2", path, (_, _) => new(code, output, ""));
    }

    // --- the print-out

    [Fact]
    public void AKeyHoldsItsLinesWithoutTheKeyAndWithSingleSpaces()
    {
        var found = Printed(0, "Exif.Image.Make   Ascii   6  Canon\nIptc.Application2.Keywords String 1 a\nIptc.Application2.Keywords String 1 b\nWarning: something\n")!;
        Assert.Equal(["Exif.Image.Make", "Iptc.Application2.Keywords"], found.Keys.ToList());
        Assert.Equal(["Ascii 6 Canon"], found["Exif.Image.Make"]);
        Assert.Equal(["String 1 a", "String 1 b"], found["Iptc.Application2.Keywords"]);
    }

    [Fact]
    public void AFileWithoutMetadataIsAnEmptyPrintOutAndAFailureIsNone()
    {
        Assert.Empty(Printed(253, "")!);
        Assert.Null(Printed(1, ""));
    }

    [Fact]
    public void AFileThatIsNotThereIsNone() => Assert.Null(OperationsParity.Printed("exiv2", "/nowhere/a.jpg", (_, _) => new(0, "", "")));

    // --- the comparison

    private static string Kind(string[]? theirs, string[]? ours, string[]? source = null)
    {
        var rows = OperationsParity.Compare(theirs is null ? Lines() : Lines(("K", theirs)), ours is null ? Lines() : Lines(("K", ours)),
                                            source is null ? Lines() : Lines(("K", source)));
        return rows[0].Outcome;
    }

    [Fact]
    public void TheSourceSaysWhoChangedAKey()
    {
        Assert.Equal("same", Kind(["a"], ["a"], ["b"]));
        Assert.Equal("differs (tool changed)", Kind(["a"], ["b"], ["b"]));
        Assert.Equal("differs (ours changed)", Kind(["a"], ["b"], ["a"]));
        Assert.Equal("differs", Kind(["a"], ["b"], ["c"]));
        Assert.Equal("differs", Kind(["a"], ["b"]));
    }

    [Fact]
    public void TheSourceSaysWhoDroppedOrAddedAKey()
    {
        Assert.Equal("ours only (tool removed)", Kind(null, ["a"], ["a"]));
        Assert.Equal("ours only", Kind(null, ["a"]));
        Assert.Equal("ours only", Kind(null, ["a"], ["b"]));
        Assert.Equal("tool only (ours removed)", Kind(["a"], null, ["a"]));
        Assert.Equal("tool only (tool added)", Kind(["a"], null));
        Assert.Equal("tool only", Kind(["a"], null, ["b"]));
    }

    [Fact]
    public void ValuesNoEditWritesAreNamed()
    {
        Assert.Equal("tool only (no elements)", Kind(["Ascii 0"], null, ["Ascii 0"]));
        Assert.Equal("tool only (undefined type)", Kind(["0x000e 12 0 0"], null, ["0x000e 12 0 0"]));
    }

    [Fact]
    public void LayoutKeysAreNoRows()
    {
        string key = OperationsParity.Layout.First();
        Assert.Empty(OperationsParity.Compare(Lines((key, ["1"])), Lines((key, ["2"]))));
    }

    // --- documented differences

    [Fact]
    public void AKeyTheOperationAddressesIsNeverExcusedByTheSource()
    {
        Assert.True(OperationsParity.Addressed("fixiso", "Exif.Photo.ISOSpeedRatings"));
        Assert.Null(OperationsParity.IsDocumented("fixiso", "Exif.Photo.ISOSpeedRatings", "differs (tool changed)"));
        Assert.True(OperationsParity.Addressed("delete-xc", "Xmp.dc.title"));
        Assert.Null(OperationsParity.IsDocumented("delete-xc", "Xmp.dc.title", "ours only (tool removed)"));
        Assert.Null(OperationsParity.IsDocumented("thumbnail", "Exif.Thumbnail.Orientation", "ours only (tool removed)"));
    }

    [Fact]
    public void WhatTheToolDidToAKeyTheOperationDoesNotAddressIsIntended()
    {
        Assert.False(OperationsParity.Addressed("fixiso", "Exif.Image.Make"));
        foreach (string outcome in new[] { "differs (tool changed)", "ours only (tool removed)", "tool only (tool added)" })
            Assert.NotNull(OperationsParity.IsDocumented("fixiso", "Exif.Image.Make", outcome));
    }

    [Fact]
    public void WhatThisLibraryDidToAKeyItDoesNotAddressIsNotIntended()
    {
        foreach (string outcome in new[] { "differs (ours changed)", "tool only (ours removed)", "differs", "ours only", "tool only" })
            Assert.Null(OperationsParity.IsDocumented("fixiso", "Exif.Image.Make", outcome));
        // An extract addresses nothing: whatever is lost on the way is a difference.
        Assert.Null(OperationsParity.IsDocumented("extract", "Exif.Image.Make", "tool only (ours removed)"));
    }

    [Fact]
    public void NamedDifferencesCarryTheirReason()
    {
        Assert.Contains("MakerNote", OperationsParity.IsDocumented("modify", "Exif.Photo.MakerNote", "differs (tool changed)"));
        Assert.NotNull(OperationsParity.IsDocumented("insert", "Exif.(MakerNote).*", "tool only (ours removed)"));
        Assert.NotNull(OperationsParity.IsDocumented("insert", "Iptc.Application2.Byline", "tool only (ours removed)"));
        Assert.Null(OperationsParity.IsDocumented("insert", "Exif.Image.Make", "tool only (ours removed)"));
    }

    [Fact]
    public void EveryReasonIsASentenceAndEveryOperationIsKnown()
    {
        var known = OperationsParity.Operations.Keys.Append("rename").ToList();
        foreach (var ((operation, key, outcome), reason) in OperationsParity.Documented)
        {
            Assert.Contains(operation, known);
            Assert.True(reason.Length > 20, $"({operation}, {key}, {outcome})");
        }
    }

    // --- the summary

    [Fact]
    public void TheTagsOfAMakerNoteGroupAreOneKey()
    {
        Assert.Equal("Exif.(MakerNote).*", OperationsParity.GroupedKey("extract", "Exif.Nikon3.Preview"));
        Assert.Equal("Exif.Image.Make", OperationsParity.GroupedKey("extract", "Exif.Image.Make"));
        Assert.Equal("Xmp.dc.title", OperationsParity.GroupedKey("extract", "Xmp.dc.title"));
    }

    [Fact]
    public void OutcomesAreCountedPerKeyWithTheFirstExampleAndSameIsLeftOut()
    {
        OperationRow[] rows =
        [
            new("modify", "s1", "Exif.Image.Make", "differs", ["a"], ["b"]),
            new("modify", "s2", "Exif.Image.Make", "differs", ["c"], ["d"]),
            new("modify", "s2", "Exif.Image.Model", "same", ["c"], ["c"]),
        ];
        var table = OperationsParity.Summarise(rows);
        Assert.Equal([("modify", "Exif.Image.Make", "differs")], table.Keys.ToList());
        var (count, example) = table[("modify", "Exif.Image.Make", "differs")];
        Assert.Equal(2, count);
        Assert.Contains("s1 Exif.Image.Make", example);
    }
}
