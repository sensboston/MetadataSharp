using System.Text.RegularExpressions;
using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Values;

/// <summary>
/// A vocabulary is data a library promises to keep honest, so these cases check its shape as well as
/// its contents: names resolve in both directions, no two names in a directory collide, IDs ascend, an
/// ID outside the table stays unnamed, and every tag the editor accepts is a tag the reader can name.
/// </summary>
public sealed class ExifTagNamesTests
{
    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0100, "ImageWidth")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0112, "Orientation")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x011A, "XResolution")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x8769, "ExifIFDPointer")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x8825, "GPSInfoIFDPointer")]
    [InlineData(ExifDirectoryKind.Exif, 0x829A, "ExposureTime")]
    [InlineData(ExifDirectoryKind.Exif, 0x9003, "DateTimeOriginal")]
    [InlineData(ExifDirectoryKind.Exif, 0x927C, "MakerNote")]
    [InlineData(ExifDirectoryKind.Exif, 0xA002, "PixelXDimension")]
    [InlineData(ExifDirectoryKind.Exif, 0xA005, "InteroperabilityIFDPointer")]
    [InlineData(ExifDirectoryKind.Gps, 0x0000, "GPSVersionID")]
    [InlineData(ExifDirectoryKind.Gps, 0x0002, "GPSLatitude")]
    [InlineData(ExifDirectoryKind.Gps, 0x001F, "GPSHPositioningError")]
    [InlineData(ExifDirectoryKind.Interop, 0x0001, "InteroperabilityIndex")]
    [InlineData(ExifDirectoryKind.Thumbnail, 0x0201, "JPEGInterchangeFormat")]
    [InlineData(ExifDirectoryKind.Thumbnail, 0x0202, "JPEGInterchangeFormatLength")]
    public void NamedTagsResolveByIdAndByName(ExifDirectoryKind directory, ushort id, string name)
    {
        var key = new ExifTagKey(directory, id);
        Assert.True(ExifTagNames.TryGetName(key, out string? found));
        Assert.Equal(name, found);
        Assert.True(ExifTagNames.TryGetKey(directory, name, out ExifTagKey back));
        Assert.Equal(key, back);
    }

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x83BB)] // IPTCNAA: an Adobe registration, not a field Exif names.
    [InlineData(ExifDirectoryKind.Exif, 0x927D)]
    [InlineData(ExifDirectoryKind.Gps, 0x0020)] // The GPS attribute table ends at 0x001F.
    [InlineData(ExifDirectoryKind.Interop, 0x1001)]
    public void TagsOutsideTheVocabularyStayNumeric(ExifDirectoryKind directory, ushort id)
    {
        Assert.False(ExifTagNames.TryGetName(new ExifTagKey(directory, id), out string? name));
        Assert.Null(name);
    }

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 76)]
    [InlineData(ExifDirectoryKind.Exif, 89)]
    [InlineData(ExifDirectoryKind.Gps, 32)]
    [InlineData(ExifDirectoryKind.Interop, 1)]
    [InlineData(ExifDirectoryKind.Thumbnail, 76)]
    public void EachDirectoryNamesTheRecordedSet(ExifDirectoryKind directory, int expected)
    {
        IReadOnlyList<ExifTagKey> keys = ExifTagNames.NamedTags(directory);
        Assert.Equal(expected, keys.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0) Assert.True(keys[i - 1].TagId < keys[i].TagId, "Tag IDs must be listed in ascending order.");
            if (!ExifTagNames.TryGetName(keys[i], out string? name))
            {
                Assert.Fail($"A listed tag has no name: {directory}/0x{keys[i].TagId:X4}.");
                return;
            }
            Assert.Matches("^[A-Za-z][A-Za-z0-9]*$", name);
            Assert.True(names.Add(name), $"The name {name} is used twice in {directory}.");
            Assert.True(ExifTagNames.TryGetKey(directory, name, out ExifTagKey back));
            Assert.Equal(keys[i], back);
        }
    }

    [Fact]
    public void EveryTagTheEditorAcceptsIsOneTheVocabularyNames()
    {
        // ExifEdit refuses every key outside the tuple patterns in its CheckKey, so that switch is the
        // writable set. It must never outgrow this vocabulary: a tag a caller can write has to be a tag
        // they can also name and read back, not a number to look up in the standard. When the writer
        // widens, this is the tripwire that asks for the matching name.
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "MetadataSharp.Core", "ExifEdit.cs"));
        string policy = source[source.IndexOf("private void CheckKey", StringComparison.Ordinal)..];
        var written = new List<(ExifDirectoryKind Directory, ushort Id)>();
        foreach (Match arm in Regex.Matches(policy, @"\(ExifDirectoryKind\.(\w+), ([^)]*)\)"))
        {
            ExifDirectoryKind directory = Enum.Parse<ExifDirectoryKind>(arm.Groups[1].Value);
            MatchCollection literals = Regex.Matches(arm.Groups[2].Value, @"0x([0-9A-Fa-f]{4})");
            if (literals.Count > 0)
            {
                foreach (Match literal in literals)
                    written.Add((directory, Convert.ToUInt16(literal.Groups[1].Value, 16)));
                continue;
            }
            // A relational arm ("<= 6") covers a leading run of IDs instead of spelling them out.
            Match bound = Regex.Match(arm.Groups[2].Value, @"<=\s*(\d+)");
            Assert.True(bound.Success, $"Cannot read the writable tag IDs out of {arm.Groups[2].Value}.");
            for (int id = 0; id <= int.Parse(bound.Groups[1].Value); id++)
                written.Add((directory, (ushort)id));
        }
        Assert.True(written.Count > 0, "ExifEdit's writable subset was not found; update this test with it.");
        foreach ((ExifDirectoryKind directory, ushort id) in written)
            Assert.True(ExifTagNames.TryGetName(new ExifTagKey(directory, id), out _),
                $"ExifEdit writes {directory}/0x{id:X4}, which this release cannot name.");
    }

    [Fact]
    public void NamesAreResolvedCaseSensitivelyAndWithinTheirOwnDirectory()
    {
        Assert.False(ExifTagNames.TryGetKey(ExifDirectoryKind.Ifd0, "orientation", out _));
        Assert.False(ExifTagNames.TryGetKey(ExifDirectoryKind.Gps, "Orientation", out _));
        Assert.False(ExifTagNames.TryGetKey(ExifDirectoryKind.Ifd0, "ExposureTime", out _)); // Exif sub-IFD only.
        Assert.True(ExifTagNames.TryGetKey(ExifDirectoryKind.Ifd0, "ImageWidth", out _));
    }

    [Fact]
    public void ArgumentsOutsideTheDirectoryEnumAndMissingNamesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExifTagNames.NamedTags((ExifDirectoryKind)99));
        Assert.Throws<ArgumentNullException>(() => ExifTagNames.TryGetKey(ExifDirectoryKind.Ifd0, null!, out _));
        Assert.False(ExifTagNames.TryGetKey((ExifDirectoryKind)99, "Orientation", out _));
    }

    [Fact]
    public void TheZerothDirectoryAndItsThumbnailShareOneTable()
    {
        // The thumbnail is a 0th IFD of its own, so the standards give it the same field names; the two
        // directories must not drift apart in what they can and cannot print.
        Assert.Equal(Names(ExifDirectoryKind.Ifd0), Names(ExifDirectoryKind.Thumbnail));
    }

    private static string[] Names(ExifDirectoryKind directory)
    {
        var names = new List<string>();
        foreach (ExifTagKey key in ExifTagNames.NamedTags(directory))
        {
            if (!ExifTagNames.TryGetName(key, out string? name)) name = key.TagId.ToString("X4");
            names.Add(name);
        }
        return names.ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MetadataSharp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
