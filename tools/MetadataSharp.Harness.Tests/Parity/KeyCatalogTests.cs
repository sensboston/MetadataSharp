using MetadataSharp.Harness.Parity;
using MetadataSharp.Harness.Python;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Parity;

// Offline rules of the key catalog check: how the reference's tables are read and what is a gap (formerly
// tests/parity/test_key_catalog.py).
//
// No Exiv2 checkout and no external tool is used: the snippets below are the shapes the reader has to tell
// apart - a table of a class, a table of a plain function, the end-of-table marker, an entry that is commented
// out, and a table that ends without its own line break.
internal static class KeyCatalogSamples
{
    public static readonly string TagsInt = """
        namespace Exiv2 {
        constexpr GroupInfo groupInfo[] = {
            {IfdId::ifdIdNotSet, "Unknown IFD", "Unknown", nullptr},
            {IfdId::ifd0Id, "IFD0", "Image", ifdTagList},
            {IfdId::canonId, "Makernote", "Canon", CanonMakerNote::tagList},
            {IfdId::canonCsId, "Makernote", "CanonCs", CanonMakerNote::tagListCs},
            {IfdId::minoltaId, "Makernote", "Minolta", MinoltaMakerNote::tagList},
            {IfdId::samsung2Id, "Makernote", "Samsung2", Samsung2MakerNote::tagList},
        };
        const TagInfo* ifdTagList() {
          return ifdTagInfo;
        }
        }  // namespace Exiv2

        """.ReplaceLineEndings("\n");

    public static readonly string Ifd = """
        constexpr TagInfo ifdTagInfo[] = {
            {0x0100, "ImageWidth", N_("Image Width"), N_("Width"), IfdId::ifd0Id, SectionId::imageTags,
             unsignedShort, -1, printValue},
            // {0x0023, "Categories", N_("Categories"), N_("Categories"), IfdId::ifd0Id, SectionId::imageTags,
            //  unsignedLong, -1, printValue},
            {0xffff, "(UnknownIfdTag)", "(UnknownIfdTag)", "(UnknownIfdTag)", IfdId::ifd0Id,
             SectionId::imageTags, unsignedByte, -1, printValue}};

        """.ReplaceLineEndings("\n");

    public static readonly string Canon = """
        const TagInfo* CanonMakerNote::tagList() {
          return tagInfo_;
        }

        const TagInfo* CanonMakerNote::tagListCs() {
          return tagInfoCs_;
        }

        constexpr TagInfo CanonMakerNote::tagInfo_[] = {
            {0x0001, "OwnerName", N_("Owner Name"), N_("Name"), IfdId::canonId, SectionId::makerTags,
             asciiString, -1, printValue},
            {0xffff, "(UnknownCanonMakerNoteTag)", "(UnknownCanonMakerNoteTag)", "(UnknownCanonMakerNoteTag)",
             IfdId::canonId, SectionId::makerTags, unsignedByte, -1, printValue}};

        constexpr TagInfo CanonMakerNote::tagInfoCs_[] = {
            {1, "Macro", N_("Macro"), N_("Macro mode"), IfdId::canonCsId, SectionId::makerTags, unsignedShort,
             -1, EXV_PRINT_TAG(canonCsMacro)},
            {0xffff, "(UnknownCanonCsTag)", "(UnknownCanonCsTag)", "(UnknownCanonCsTag)", IfdId::canonCsId,
             SectionId::makerTags, unsignedByte, -1, printValue}};

        """.ReplaceLineEndings("\n");

    public static readonly string Minolta = """
        const TagInfo* MinoltaMakerNote::tagList() {
          return tagInfo_;
        }

        constexpr TagInfo MinoltaMakerNote::tagInfo_[] = {
            {0x0001, "CameraSettings", N_("Camera Settings"), N_("Settings"), IfdId::minoltaId,
             SectionId::makerTags, undefined, -1, printValue},
            /* {0x0002, "CommentedOut", N_("Out"), N_("Out"), IfdId::minoltaId, SectionId::makerTags,
               undefined, -1, printValue}, */
            {0xffff, "(UnknownMinoltaMakerNoteTag)", "(UnknownMinoltaMakerNoteTag)",
             "(UnknownMinoltaMakerNoteTag)", IfdId::minoltaId, SectionId::makerTags, unsignedByte, -1,
             printValue}};

        """.ReplaceLineEndings("\n");

    /// <summary>A reference checkout of the four snippets below a directory.</summary>
    public static string Source(string root)
    {
        Write(PyPath.Join(root, "src", "tags_int.cpp"), TagsInt + Ifd);
        Write(PyPath.Join(root, "src", "canonmn_int.cpp"), Canon);
        Write(PyPath.Join(root, "src", "minoltamn_int.cpp"), Minolta);
        return root;
    }
}

public sealed class KeyCatalogReferenceKeysTests : IDisposable
{
    private readonly TempDirectory temporary = Temp();

    public KeyCatalogReferenceKeysTests() => KeyCatalogSamples.Source(temporary.Path);

    void IDisposable.Dispose() => temporary.Dispose();

    [Fact]
    public void EveryGroupThatNamesATableContributesItsEntries()
    {
        var (keys, unresolved) = KeyCatalog.ReferenceKeys(temporary.Path);
        Assert.DoesNotContain(unresolved, line => !line.StartsWith("Samsung2:", StringComparison.Ordinal));
        Assert.Equal(new HashSet<string> { "Exif.Image.ImageWidth", "Exif.Canon.OwnerName", "Exif.CanonCs.Macro", "Exif.Minolta.CameraSettings" },
                     keys.Keys.ToHashSet());
    }

    [Fact]
    public void EndOfTableMarkersAndCommentedOutEntriesAreNotKeys()
    {
        var (keys, _) = KeyCatalog.ReferenceKeys(temporary.Path);
        Assert.DoesNotContain(keys.Keys, key => key.Contains("(Unknown", StringComparison.Ordinal));
        Assert.DoesNotContain("Exif.Image.Categories", keys.Keys);
        Assert.DoesNotContain("Exif.Minolta.CommentedOut", keys.Keys);
    }

    [Fact]
    public void NumbersAreKeptAsTheTableStatesThem()
    {
        var (keys, _) = KeyCatalog.ReferenceKeys(temporary.Path);
        Assert.Equal("0x0001", keys["Exif.Canon.OwnerName"].Tag);
        Assert.Equal("1", keys["Exif.CanonCs.Macro"].Tag);
    }

    [Fact]
    public void AGroupWhoseFunctionIsMissingIsReportedRatherThanSkipped()
    {
        // Samsung2MakerNote::tagList is named by groupInfo and defined nowhere in the snippets.
        var (_, unresolved) = KeyCatalog.ReferenceKeys(temporary.Path);
        Assert.Equal(["Samsung2: Samsung2MakerNote::tagList returns no table this script could find"], unresolved);
    }
}

public sealed class KeyCatalogCatalogTests : IDisposable
{
    private readonly TempDirectory temporary = Temp();
    private readonly string catalog;

    public KeyCatalogCatalogTests()
    {
        catalog = Write(temporary["catalog.json"], """
            {"exifGroups": [
                {"name": "Image", "tags": [{"id": 256, "names": ["ImageWidth"]}]},
                {"name": "Canon", "tags": [{"id": 9, "names": ["OwnerName"]}]},
                {"name": "CanonCs", "tags": [{"id": 1, "names": ["Macro", "(UnknownCanonCsTag)"]}]},
                {"name": "Canon", "tags": [{"id": 35, "names": ["Categories"]}]}]}
            """);
    }

    void IDisposable.Dispose() => temporary.Dispose();

    [Fact]
    public void PlaceholdersAreNotCatalogKeys()
    {
        var keys = KeyCatalog.CatalogKeys(catalog);
        Assert.DoesNotContain("Exif.CanonCs.(UnknownCanonCsTag)", keys.Keys);
        Assert.Contains("Exif.Canon.Categories", keys.Keys);
    }

    [Fact]
    public void AKeyTheReferenceHasAndTheCatalogLacksFailsTheRun()
    {
        string source = KeyCatalogSamples.Source(temporary["source"]);
        Assert.Equal(1, RunWithMinimum(1, source));
    }

    [Fact]
    public void AReferenceThatReadsNothingIsNotAPass()
    {
        string source = KeyCatalogSamples.Source(temporary["empty"]);
        Assert.Equal(1, RunWithMinimum(1000, source));
    }

    private int RunWithMinimum(int minimum, string source)
    {
        int previous = KeyCatalog.MinimumKeys;
        KeyCatalog.MinimumKeys = minimum;
        try
        {
            return Capture(() => KeyCatalog.Run([source, "--catalog", catalog])).Code;
        }
        finally
        {
            KeyCatalog.MinimumKeys = previous;
        }
    }
}
