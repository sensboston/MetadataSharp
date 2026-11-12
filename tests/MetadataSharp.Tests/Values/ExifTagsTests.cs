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
    [InlineData(ExifDirectoryKind.Thumbnail, 0x0201, "JPEGInterchangeFormat", TiffFieldType.Long, 1U)]
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
                "Artist", "CameraFirmware", "CameraOwnerName", "Copyright", "ImageDescription",
                "ImageEditingSoftware", "ImageEditor", "ImageTitle", "LensMake", "LensModel", "Make",
                "MetadataEditingSoftware", "Model", "Photographer", "RAWDevelopingSoftware", "Software",
            },
            marked.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x8649)] // Photoshop resources: no listed publication names the tag.
    [InlineData(ExifDirectoryKind.Ifd0, 0xC611)] // One below DNGVersion, the first DNG tag.
    [InlineData(ExifDirectoryKind.Exif, 0x927D)]
    [InlineData(ExifDirectoryKind.Exif, 0xC612)] // DNG names belong to the first directory only.
    [InlineData(ExifDirectoryKind.Gps, 0x0020)] // The GPS attribute table ends at 0x001F.
    [InlineData(ExifDirectoryKind.Interop, 0x0003)]
    [InlineData(ExifDirectoryKind.Interop, 0x1003)]
    public void TagsOutsideTheVocabularyResolveToNothing(ExifDirectoryKind directory, ushort id)
        => Assert.Null(ExifTags.Find(new ExifTagKey(directory, id)));

    [Fact]
    public void ANameBelongsToTheDirectoryThatOwnsIt()
    {
        // Names are not global, so a field stored in the wrong place keeps its number: 0xA002 is
        // PixelXDimension in the Exif sub-IFD and nothing anywhere else.
        Assert.Null(ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Ifd0, 0xA002)));
        Assert.Null(ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Interop, 0x829A)));
        Assert.Null(ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Gps, 0x0112)));
        Assert.Null(ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Exif, 0x0002)));
        ExifTagDefinition? shared = ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x0112));
        Assert.NotNull(shared);
        Assert.Equal("Orientation", shared.Name);
    }

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 246)]
    [InlineData(ExifDirectoryKind.Exif, 89)]
    [InlineData(ExifDirectoryKind.Gps, 32)]
    [InlineData(ExifDirectoryKind.Interop, 5)]
    [InlineData(ExifDirectoryKind.Thumbnail, 246)]
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
                    definition.ValueMeanings.Select(value => value.Number).Distinct().ToArray(),
                    definition.ValueMeanings.Select(value => value.Number).ToArray());
                foreach (ExifTagValueMeaning value in definition.ValueMeanings)
                {
                    Assert.False(string.IsNullOrWhiteSpace(value.Meaning));
                    Assert.Equal(value.Meaning.Trim(), value.Meaning);
                    Assert.DoesNotContain("  ", value.Meaning);
                    Assert.False(value.Meaning.EndsWith('.'));  // no overload takes char with StringComparison
                }

                if (definition.ValueMeanings.Length > 0)
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
                if (definition.Source == ExifTagSource.ExifAndTiff && definition.Types.Length == 0)
                    untyped.Add(definition.Name);
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
        Assert.Equal(5, definition.ValueMeanings.Length);
        Assert.Equal("None", definition.ValueMeanings[0].Meaning);
        Assert.Equal((ushort)2, definition.ValueMeanings[2].Number);
        Assert.Equal("High gain up", definition.ValueMeanings[2].Meaning);
        Assert.Equal("High gain down", definition.ValueMeanings[4].Meaning);
    }

    [Fact]
    public void AnEnumeratedTagKeepsTheStandardWordingNotAnInventedLabel()
    {
        if (ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112)) is not ExifTagDefinition orientation)
        {
            Assert.Fail("Orientation is missing from the vocabulary.");
            return;
        }

        Assert.Equal(8, orientation.ValueMeanings.Length);
        Assert.Equal("The 0th row is at the visual top of the image, and the 0th column is the visual left-hand side",
            orientation.ValueMeanings[0].Meaning);
        if (ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Exif, 0xA001)) is not ExifTagDefinition colorSpace)
        {
            Assert.Fail("ColorSpace is missing from the vocabulary.");
            return;
        }

        // The publication's capitalisation survives, because "sRGB" is the name and "Srgb" is not.
        Assert.Equal("sRGB", Assert.Single(colorSpace.ValueMeanings).Meaning);
    }

    [Fact]
    public void TheWriterHoldsEveryListedTagToItsDefinition()
    {
        // ExifEdit has no list of its own: a tag the vocabulary shapes is accepted in that shape and
        // refused in another, so a definition that contradicts the standard shows as a refused write.
        int held = 0;
        foreach (ExifDirectoryKind directory in Enum.GetValues<ExifDirectoryKind>())
        {
            foreach (ExifTagDefinition definition in ExifTags.Definitions(directory))
            {
                var key = new ExifTagKey(directory, definition.Id);
                if (!ExifEdit.IsEditable(key) || definition.Types.IsEmpty) continue;
                held++;
                uint count = definition.Counts.IsEmpty ? 3U : definition.Counts[0];
                var edit = new ExifEdit();
                foreach (TiffFieldType type in definition.Types)
                    edit.Set(key, Value(type, count));

                TiffFieldType other = Enum.GetValues<TiffFieldType>().First(type => !definition.Types.Contains(type));
                Assert.Throws<ArgumentException>(() => edit.Set(key, Value(other, count)));
                edit.SetUnchecked(key, Value(other, count));
                if (definition.Counts.IsEmpty) continue;
                uint wrong = definition.Counts[^1] + 1U;
                Assert.Throws<ArgumentException>(() => edit.Set(key, Value(definition.Types[0], wrong)));
                edit.SetUnchecked(key, Value(definition.Types[0], wrong));
            }
        }

        Assert.True(held > 200, $"Only {held} definitions state a field type; the vocabulary lost its shapes.");
    }

    /// <summary>A value of zero bytes: an ASCII value of that kind ends with NUL and is seven-bit.</summary>
    private static ExifValue Value(TiffFieldType type, uint count) =>
        ExifValue.FromRaw(type, count, Endianness.LittleEndian, new byte[checked((int)count * Width(type))]);

    private static int Width(TiffFieldType type) => type switch
    {
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float => 4,
        TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double => 8,
        _ => 1
    };

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, ExifTagSource.ExifAndTiff, 76)]
    [InlineData(ExifDirectoryKind.Ifd0, ExifTagSource.TiffEp, 41)]
    [InlineData(ExifDirectoryKind.Ifd0, ExifTagSource.Dng, 121)]
    [InlineData(ExifDirectoryKind.Ifd0, ExifTagSource.Vendor, 8)]
    [InlineData(ExifDirectoryKind.Ifd0, ExifTagSource.Dcf, 0)]
    [InlineData(ExifDirectoryKind.Exif, ExifTagSource.ExifAndTiff, 89)]
    [InlineData(ExifDirectoryKind.Gps, ExifTagSource.ExifAndTiff, 32)]
    [InlineData(ExifDirectoryKind.Interop, ExifTagSource.ExifAndTiff, 1)]
    [InlineData(ExifDirectoryKind.Interop, ExifTagSource.Dcf, 4)]
    public void EachSourceContributesTheRecordedNumberOfTags(ExifDirectoryKind directory, ExifTagSource source, int expected)
        => Assert.Equal(expected, ExifTags.Definitions(directory).Count(definition => definition.Source == source));

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x014A, "SubIFDs", ExifTagSource.TiffEp)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x829A, "ExposureTime", ExifTagSource.TiffEp)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x83BB, "IPTCNAA", ExifTagSource.TiffEp)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x8773, "InterColorProfile", ExifTagSource.TiffEp)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x8827, "ISOSpeedRatings", ExifTagSource.TiffEp)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x9003, "DateTimeOriginal", ExifTagSource.TiffEp)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x9216, "TIFFEPStandardID", ExifTagSource.TiffEp)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x4746, "Rating", ExifTagSource.Vendor)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x4749, "RatingPercent", ExifTagSource.Vendor)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x9C9B, "XPTitle", ExifTagSource.Vendor)]
    [InlineData(ExifDirectoryKind.Ifd0, 0x9C9F, "XPSubject", ExifTagSource.Vendor)]
    [InlineData(ExifDirectoryKind.Ifd0, 0xC4A5, "PrintImageMatching", ExifTagSource.Vendor)]
    [InlineData(ExifDirectoryKind.Ifd0, 0xC612, "DNGVersion", ExifTagSource.Dng)]
    [InlineData(ExifDirectoryKind.Ifd0, 0xC614, "UniqueCameraModel", ExifTagSource.Dng)]
    [InlineData(ExifDirectoryKind.Ifd0, 0xC621, "ColorMatrix1", ExifTagSource.Dng)]
    [InlineData(ExifDirectoryKind.Ifd0, 0xC634, "DNGPrivateData", ExifTagSource.Dng)]
    [InlineData(ExifDirectoryKind.Ifd0, 0xC65A, "CalibrationIlluminant1", ExifTagSource.Dng)]
    [InlineData(ExifDirectoryKind.Ifd0, 0xC71A, "PreviewColorSpace", ExifTagSource.Dng)]
    [InlineData(ExifDirectoryKind.Ifd0, 0xC7A7, "NewRawImageDigest", ExifTagSource.Dng)]
    [InlineData(ExifDirectoryKind.Ifd0, 0xCD4B, "JXLDecodeSpeed", ExifTagSource.Dng)]
    [InlineData(ExifDirectoryKind.Thumbnail, 0xC612, "DNGVersion", ExifTagSource.Dng)]
    [InlineData(ExifDirectoryKind.Interop, 0x0002, "InteroperabilityVersion", ExifTagSource.Dcf)]
    [InlineData(ExifDirectoryKind.Interop, 0x1000, "RelatedImageFileFormat", ExifTagSource.Dcf)]
    [InlineData(ExifDirectoryKind.Interop, 0x1001, "RelatedImageWidth", ExifTagSource.Dcf)]
    [InlineData(ExifDirectoryKind.Interop, 0x1002, "RelatedImageLength", ExifTagSource.Dcf)]
    public void ATagAnotherPublicationNamesResolvesToThatNameAndSaysWhereItCameFrom(
        ExifDirectoryKind directory, ushort id, string name, ExifTagSource source)
    {
        ExifTagDefinition? definition = ExifTags.Find(new ExifTagKey(directory, id));
        Assert.NotNull(definition);
        Assert.Equal(name, definition.Name);
        Assert.Equal(source, definition.Source);
        Assert.Equal(id, definition.Id);
    }

    [Fact]
    public void ATagFromAnotherPublicationCarriesItsNumberAndNameAndNothingElse()
    {
        // Only the reviewed Exif and TIFF tables state shapes. A row taken from another publication must
        // not look as if its type or count had been checked, and it offers no interpretation.
        int seen = 0;
        foreach (ExifDirectoryKind directory in Enum.GetValues<ExifDirectoryKind>())
        {
            foreach (ExifTagDefinition definition in ExifTags.Definitions(directory))
            {
                if (definition.Source == ExifTagSource.ExifAndTiff) continue;
                seen++;
                Assert.Empty(definition.Types);
                Assert.Empty(definition.Counts);
                Assert.Equal(0U, definition.FixedCount);
                Assert.Empty(definition.ValueMeanings);
                Assert.Null(definition.Default);
                Assert.False(definition.Utf8TextAlternative);
                Assert.Null(ExifTags.Interpret(
                    new ExifTagKey(directory, definition.Id), ExifValue.FromRaw(TiffFieldType.Short, 1U, Endianness.LittleEndian, new byte[] { 1, 0 })));
            }
        }

        // Ifd0 and Thumbnail share one table, so its 170 rows are met twice; Interop adds four.
        Assert.Equal(170 + 170 + 4, seen);
    }

    [Fact]
    public void TheReviewedTableKeepsItsRowWhereAnotherPublicationNamesTheSameNumber()
    {
        // TIFF/EP and DNG restate ordinary TIFF fields. The reviewed row, with its shape, is the one kept.
        foreach (ushort id in new ushort[] { 0x0100, 0x010E, 0x0112, 0x8298, 0x8769, 0x8825 })
        {
            ExifTagDefinition? definition = ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Ifd0, id));
            Assert.NotNull(definition);
            Assert.Equal(ExifTagSource.ExifAndTiff, definition.Source);
            Assert.NotEmpty(definition.Types);
        }

        // The camera fields Exif keeps in its own directory stay there with their reviewed shape; the
        // TIFF/EP name applies in the first directory only.
        ExifTagDefinition? photo = ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Exif, 0x829A));
        ExifTagDefinition? image = ExifTags.Find(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x829A));
        Assert.NotNull(photo);
        Assert.NotNull(image);
        Assert.Equal(ExifTagSource.ExifAndTiff, photo.Source);
        Assert.Equal(ExifTagSource.TiffEp, image.Source);
        Assert.Equal(photo.Name, image.Name);
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
}
