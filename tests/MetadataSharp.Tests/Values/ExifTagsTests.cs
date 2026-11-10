using System.Globalization;
using System.Text.RegularExpressions;
using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Values;

/// <summary>
/// The tag vocabulary is data this project promises to keep honest, so these cases check its shape as well
/// as its contents: a key resolves to the shape the standard gives, IDs ascend and never collide inside a
/// directory, a count is either fixed or left to the image, an enumerated tag carries the standard's own
/// wording, and the tags the writer reviews are tags the vocabulary agrees with.
/// </summary>
public sealed class ExifTagsTests
{
    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0112, "Orientation", TiffFieldType.Short, 1U)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x011A, "XResolution", TiffFieldType.Rational, 1U)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x010E, "ImageDescription", TiffFieldType.Ascii, 0U)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x012D, "TransferFunction", TiffFieldType.Short, 768U)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0132, "DateTime", TiffFieldType.Ascii, 20U)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x8769, "ExifIFDPointer", TiffFieldType.Long, 1U)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x8825, "GPSInfoIFDPointer", TiffFieldType.Long, 1U)]
    [InlineData(ExifDirectoryKind.Exif, 0x829A, "ExposureTime", TiffFieldType.Rational, 1U)]
    [InlineData(ExifDirectoryKind.Exif, 0x9003, "DateTimeOriginal", TiffFieldType.Ascii, 20U)]
    [InlineData(ExifDirectoryKind.Exif, 0x9010, "OffsetTime", TiffFieldType.Ascii, 7U)]
    [InlineData(ExifDirectoryKind.Exif, 0x927C, "MakerNote", TiffFieldType.Undefined, 0U)]
    [InlineData(ExifDirectoryKind.Exif, 0x9290, "SubSecTime", TiffFieldType.Ascii, 0U)]
    [InlineData(ExifDirectoryKind.Exif, 0xA002, "PixelXDimension", TiffFieldType.Short, 1U)]
    [InlineData(ExifDirectoryKind.Exif, 0xA407, "GainControl", TiffFieldType.Short, 1U)]
    [InlineData(ExifDirectoryKind.Gps, 0x0000, "GPSVersionID", TiffFieldType.Byte, 4U)]
    [InlineData(ExifDirectoryKind.Gps, 0x0002, "GPSLatitude", TiffFieldType.Rational, 3U)]
    [InlineData(ExifDirectoryKind.Gps, 0x001D, "GPSDateStamp", TiffFieldType.Ascii, 11U)]
    [InlineData(ExifDirectoryKind.Interop, 0x0001, "InteroperabilityIndex", TiffFieldType.Ascii, 0U)]
    [InlineData(ExifDirectoryKind.Thumbnail, 0x0201, "JPEGInterchangeFormat", TiffFieldType.Long, 0U)]
    public void ATagResolvesToTheShapeItsStandardGives(
        ExifDirectoryKind directory, ushort id, string name, TiffFieldType type, uint fixedCount)
    {
        if (ExifTags.Find(new ExifTagKey(directory, id)) is not ExifTagDefinition definition)
        {
            Assert.Fail($"The vocabulary does not list {directory}/0x{id:X4}, which it must as {name}.");
            return;
        }

        Assert.Equal(name, definition.Name);
        Assert.Contains(definition.Types, field => field == type);
        Assert.Equal(fixedCount, definition.FixedCount);
        Assert.Equal(id, definition.Id);
    }

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0100, "ImageWidth", "Short,Long")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0111, "StripOffsets", "Short,Long")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0117, "StripByteCounts", "Short,Long")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0144, "TileOffsets", "Long")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0150, "DotRange", "Byte,Short")]
    [InlineData(ExifDirectoryKind.Exif, 0xA002, "PixelXDimension", "Short,Long")]
    public void ATagTheStandardAllowsTwoTypesToBeWrittenListsBoth(
        ExifDirectoryKind directory, ushort id, string name, string types)
    {
        if (ExifTags.Find(new ExifTagKey(directory, id)) is not ExifTagDefinition definition)
        {
            Assert.Fail($"The vocabulary does not list {directory}/0x{id:X4}.");
            return;
        }

        Assert.Equal(name, definition.Name);
        Assert.Equal(types, string.Join(',', definition.Types));
    }

    [Fact]
    public void OnlyTheTagsTheStandardLeavesOpenAreWrittenInTwoTypes()
    {
        var open = new List<string>();
        foreach (ExifDirectoryKind directory in Enum.GetValues<ExifDirectoryKind>())
        {
            foreach (ExifTagDefinition definition in ExifTags.Definitions(directory))
            {
                if (definition.Types.Length > 1) open.Add(definition.Name);
            }
        }

        // Eleven fields accept two types: the offsets, dimensions and strip or tile counts a file may hold
        // as SHORT or LONG, plus DotRange. Any more would mean the vocabulary had started guessing.
        Assert.Equal(11, open.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OnlyTextTagsRecordThatUtf8IsAnAlternativeEncoding()
    {
        var marked = new List<string>();
        foreach (ExifDirectoryKind directory in Enum.GetValues<ExifDirectoryKind>())
        {
            foreach (ExifTagDefinition definition in ExifTags.Definitions(directory))
            {
                if (definition.Utf8TextAlternative) marked.Add(definition.Name);
            }
        }

        // Exactly these rows of the reviewed Exif tables read "ASCII or UTF-8", and no other tag does.
        Assert.Equal(
            new[]
            {
                "Artist", "CameraFirmware", "CameraOwnerName", "Copyright", "ImageDescription", "ImageEditor",
                "ImageEditingSoftware", "ImageTitle", "LensMake", "LensModel", "Make", "MetadataEditingSoftware",
                "Model", "Photographer", "RAWDevelopingSoftware", "Software",
            },
            marked.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x83BB)] // IPTCNAA: an Adobe registration, not a field Exif names.
    [InlineData(ExifDirectoryKind.Exif, 0x927D)]
    [InlineData(ExifDirectoryKind.Gps, 0x0020)] // The GPS attribute table ends at 0x001F.
    [InlineData(ExifDirectoryKind.Interop, 0x1001)]
    public void TagsOutsideTheVocabularyResolveToNothing(ExifDirectoryKind directory, ushort id)
        => Assert.Null(ExifTags.Find(new ExifTagKey(directory, id)));

    [Fact]
    public void ANameBelongsToTheDirectoryThatOwnsIt()
    {
        // Names are not global, so a field stored in the wrong place keeps its number: 0x829A is
        // ExposureTime in the Exif sub-IFD and nothing anywhere else.
        Assert.Null(ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x829A)));
        Assert.Null(ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Gps, 0x0112)));
        Assert.Null(ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Exif, 0x0002)));
        ExifTagDefinition? shared = ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x0112));
        Assert.NotNull(shared);
        Assert.Equal("Orientation", shared.Name);
    }

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 76)]
    [InlineData(ExifDirectoryKind.Exif, 89)]
    [InlineData(ExifDirectoryKind.Gps, 32)]
    [InlineData(ExifDirectoryKind.Interop, 1)]
    [InlineData(ExifDirectoryKind.Thumbnail, 76)]
    public void EachDirectoryHoldsTheRecordedSetOfTags(ExifDirectoryKind directory, int expected)
    {
        IReadOnlyList<ExifTagDefinition> definitions = ExifTags.Definitions(directory);
        Assert.Equal(expected, definitions.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < definitions.Count; i++)
        {
            ExifTagDefinition definition = definitions[i];
            if (i > 0)
                Assert.True(definitions[i - 1].Id < definition.Id, "Tag IDs must be listed in ascending order.");
            Assert.Matches("^[A-Za-z][A-Za-z0-9]*$", definition.Name);
            Assert.True(names.Add(definition.Name), $"The name {definition.Name} is used twice in {directory}.");

            // One lookup path: enumerating a directory and resolving its key must reach the same definition.
            Assert.Same(definition, ExifTags.Find(new ExifTagKey(directory, definition.Id)));
        }
    }

    [Fact]
    public void EveryDefinitionIsInternallyConsistent()
    {
        foreach (ExifDirectoryKind directory in Enum.GetValues<ExifDirectoryKind>())
        {
            foreach (ExifTagDefinition definition in ExifTags.Definitions(directory))
            {
                Assert.Equal(definition.Types.Distinct().ToArray(), definition.Types);
                Assert.Equal(definition.Counts.OrderBy(count => count).ToArray(), definition.Counts);
                Assert.Equal(definition.Counts.Distinct().ToArray(), definition.Counts);
                uint fixedCount = definition.Counts.Length == 1 ? definition.Counts[0] : 0U;
                Assert.Equal(fixedCount, definition.FixedCount);
                Assert.Equal(
                    definition.Values.Select(value => value.Number).Distinct().ToArray(),
                    definition.Values.Select(value => value.Number).ToArray());
                foreach (ExifTagValueMeaning value in definition.Values)
                {
                    Assert.False(string.IsNullOrWhiteSpace(value.Meaning));
                    Assert.Equal(value.Meaning.Trim(), value.Meaning);
                    Assert.DoesNotContain("  ", value.Meaning);
                    Assert.False(value.Meaning.EndsWith('.'));  // no overload takes char with StringComparison
                }

                if (definition.Values.Length > 0)
                {
                    // A number-to-label table describes a small integer field, never a rational, a ratio or
                    // a string, and it is only stated where the standard states one.
                    TiffFieldType enumerated = Assert.Single(definition.Types);
                    Assert.True(enumerated is TiffFieldType.Byte or TiffFieldType.Short or TiffFieldType.Undefined,
                        $"{definition.Name} lists value meanings for a {enumerated} field.");
                }

                if (definition.Utf8TextAlternative)
                    Assert.Equal(TiffFieldType.Ascii, Assert.Single(definition.Types));
            }
        }
    }

    [Fact]
    public void OnlyTheTagsWhoseStandardLeavesTheTypeOpenCarryNoType()
    {
        var untyped = new List<string>();
        foreach (ExifDirectoryKind directory in Enum.GetValues<ExifDirectoryKind>())
        {
            foreach (ExifTagDefinition definition in ExifTags.Definitions(directory))
            {
                if (definition.Types.Length == 0) untyped.Add(definition.Name);
            }
        }

        // TIFF 6.0 gives these two no type at all: "the field type that best matches the samples".
        Assert.Equal(new[] { "SMaxSampleValue", "SMinSampleValue" },
            untyped.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ASectionThatQualifiesItsTableRowIsTheStrongerStatement()
    {
        // Exif 3.0 Table 9 lists GainControl as RATIONAL; section 4.6.6.7.41 gives SHORT, a count of 1 and
        // the five gain steps. The section wins, and the files agree with it: a gain of 2 in the reference
        // corpus is a SHORT field, which the standard's table of meanings reads "High gain up".
        if (ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Exif, 0xA407)) is not ExifTagDefinition definition)
        {
            Assert.Fail("GainControl is missing from the vocabulary.");
            return;
        }

        Assert.Equal(new[] { TiffFieldType.Short }, definition.Types);
        Assert.Equal(1U, definition.FixedCount);
        Assert.Equal(5, definition.Values.Length);
        Assert.Equal("None", definition.Values[0].Meaning);
        Assert.Equal((ushort)2, definition.Values[2].Number);
        Assert.Equal("High gain up", definition.Values[2].Meaning);
        Assert.Equal("High gain down", definition.Values[4].Meaning);
    }

    [Fact]
    public void AnEnumeratedTagKeepsTheStandardWordingNotAnInventedLabel()
    {
        if (ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112)) is not ExifTagDefinition orientation)
        {
            Assert.Fail("Orientation is missing from the vocabulary.");
            return;
        }

        Assert.Equal(8, orientation.Values.Length);
        Assert.Equal("The 0th row is at the visual top of the image, and the 0th column is the visual left-hand side",
            orientation.Values[0].Meaning);
        if (ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Exif, 0xA001)) is not ExifTagDefinition colorSpace)
        {
            Assert.Fail("ColorSpace is missing from the vocabulary.");
            return;
        }

        // The publication's capitalisation survives, because "sRGB" is the name and "Srgb" is not.
        Assert.Equal("sRGB", Assert.Single(colorSpace.Values).Meaning);
    }

    [Fact]
    public void EveryTagTheWriterReviewsIsNamedAndShapedAsTheVocabularySays()
    {
        // ExifEdit refuses every key outside the tuple patterns in its CheckKey, and ExifLayoutPlanner
        // restates a type and a count for the keys it is willing to rebuild. Both must agree with the
        // vocabulary: a tag a caller can write has to be a tag this release can name, and a reviewed shape
        // that contradicts the standard is a bug in one of the two.
        string root = FindRepositoryRoot();
        string edit = File.ReadAllText(Path.Combine(root, "src", "MetadataSharp.Core", "ExifEdit.cs"));
        var writable = new List<(ExifDirectoryKind Directory, ushort Id)>();
        foreach (Match arm in Regex.Matches(
                     edit[edit.IndexOf("private void CheckKey", StringComparison.Ordinal)..],
                     @"\(ExifDirectoryKind\.(\w+), ([^)]*)\)"))
        {
            ExifDirectoryKind directory = Enum.Parse<ExifDirectoryKind>(arm.Groups[1].Value);
            MatchCollection literals = Regex.Matches(arm.Groups[2].Value, @"0x([0-9A-Fa-f]{4})");
            if (literals.Count > 0)
            {
                foreach (Match literal in literals)
                    writable.Add((directory, Convert.ToUInt16(literal.Groups[1].Value, 16)));
                continue;
            }

            // A relational arm ("<= 6") covers a leading run of IDs instead of spelling them out.
            Match bound = Regex.Match(arm.Groups[2].Value, @"<=\s*(\d+)");
            Assert.True(bound.Success, $"Cannot read the writable tag IDs out of {arm.Groups[2].Value}.");
            for (int id = 0; id <= int.Parse(bound.Groups[1].Value, CultureInfo.InvariantCulture); id++)
                writable.Add((directory, (ushort)id));
        }

        Assert.True(writable.Count > 0, "ExifEdit's writable subset was not found; update this test with it.");

        string planner = File.ReadAllText(Path.Combine(root, "src", "MetadataSharp.Core", "Tiff",
            "Serialization", "ExifLayoutPlanner.cs"));
        string shape = planner[planner.IndexOf("internal static void ValidateShape", StringComparison.Ordinal)..];
        var reviewed = new Dictionary<(ExifDirectoryKind, ushort), (TiffFieldType Type, uint Count)>();
        int cursor = shape.IndexOf("switch", StringComparison.Ordinal) + "switch".Length;
        foreach (Match arm in Regex.Matches(shape, @"=>\s*\(TiffFieldType\.(\w+), (\d+)U\)"))
        {
            TiffFieldType type = Enum.Parse<TiffFieldType>(arm.Groups[1].Value);
            uint count = uint.Parse(arm.Groups[2].Value, CultureInfo.InvariantCulture);
            foreach (Match group in Regex.Matches(shape[cursor..arm.Index], @"\(ExifDirectoryKind\.(\w+), ([^)]*)\)"))
            {
                ExifDirectoryKind directory = Enum.Parse<ExifDirectoryKind>(group.Groups[1].Value);
                foreach (Match literal in Regex.Matches(group.Groups[2].Value, @"0x([0-9A-Fa-f]{2,4})|\b(\d{1,3})\b"))
                {
                    bool hex = literal.Groups[1].Success;
                    string digits = hex ? literal.Groups[1].Value : literal.Groups[2].Value;
                    int id = hex ? Convert.ToInt32(digits, 16) : int.Parse(digits, CultureInfo.InvariantCulture);
                    reviewed[(directory, (ushort)id)] = (type, count);
                }
            }

            cursor = arm.Index + arm.Length;
        }

        Assert.True(reviewed.Count >= writable.Count,
            "The layout planner states fewer shapes than ExifEdit accepts tags; update this test with them.");
        foreach ((ExifDirectoryKind directory, ushort id) in writable)
        {
            if (!reviewed.TryGetValue((directory, id), out (TiffFieldType Type, uint Count) written))
            {
                Assert.Fail($"ExifEdit writes {directory}/0x{id:X4}, which the layout planner does not shape.");
                return;
            }

            if (ExifTags.Find(new ExifTagKey(directory, id)) is not ExifTagDefinition definition)
            {
                Assert.Fail($"ExifEdit writes {directory}/0x{id:X4}, which this release cannot name.");
                return;
            }

            Assert.Contains(definition.Types, field => field == written.Type);
            if (written.Count != 0U && definition.Counts.Length > 0)
            {
                Assert.True(Array.IndexOf(definition.Counts, written.Count) >= 0,
                    $"{definition.Name} is written with {written.Count} values, which its standard rejects.");
            }
        }
    }

    [Fact]
    public void TheZerothDirectoryAndItsThumbnailShareOneTable()
    {
        // A thumbnail is a 0th IFD of its own, so the standard gives it the same fields; the two directories
        // must not drift apart in what they can and cannot describe.
        Assert.Same(ExifTags.Definitions(ExifDirectoryKind.Ifd0), ExifTags.Definitions(ExifDirectoryKind.Thumbnail));
    }

    [Fact]
    public void ADirectoryOutsideTheSupportedSetIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ExifTags.Definitions((ExifDirectoryKind)99));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MetadataSharp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
