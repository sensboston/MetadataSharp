using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Tiff.Serialization;
using Xunit;

namespace MetadataSharp.Tests.Tiff.Serialization;

public sealed class ExifLayoutPlannerTests
{
    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void InlineBoundaryAndOddFinalLengthAreExact(int size, bool external)
    {
        ExifEntry entry = Text(ExifDirectoryKind.Ifd0, 0x013B, size);
        ExifWritePlan plan = ExifLayoutPlanner.Create(null, Snapshot(entry));
        PlannedDirectory directory = Assert.Single(plan.Directories);
        PlannedEntry value = Assert.Single(directory.Entries);
        Assert.Equal(8, directory.Offset);
        Assert.Equal(external ? 26 : null, value.DataOffset);
        Assert.Equal(external ? 31 : 26, plan.Length);
        Assert.Null(value.ScalarOverride);
        Assert.Same(entry.Value, value.Value);
        Assert.Null(plan.ThumbnailOffset);
    }

    [Fact]
    public void SortsTagsAndAlignsExternalValuesWithoutTrailingPadding()
    {
        ExifEntry artist = Text(ExifDirectoryKind.Ifd0, 0x013B, 5);
        ExifEntry make = Text(ExifDirectoryKind.Ifd0, 0x010F, 5);
        ExifWritePlan first = ExifLayoutPlanner.Create(null, Snapshot(artist, make));
        ExifWritePlan second = ExifLayoutPlanner.Create(null, Snapshot(make, artist));
        Assert.Equal(49, first.Length);
        Assert.Equal(first.Length, second.Length);
        PlannedDirectory a = Assert.Single(first.Directories);
        PlannedDirectory b = Assert.Single(second.Directories);
        Assert.Equal(new ushort[] { 0x010F, 0x013B }, a.Entries.Select(e => e.TagId));
        Assert.Equal(new int?[] { 38, 44 }, a.Entries.Select(e => e.DataOffset));
        Assert.Equal(a.Entries.ToArray(), b.Entries.ToArray());
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void SynthesizesKnownPointersWithTiffRelativeOffsets(Endianness order)
    {
        var fnumber = new ExifEntry(new(ExifDirectoryKind.Exif, 0x829D),
            ExifValue.FromRaw(TiffFieldType.Rational, 1, order, new byte[8]));
        ExifWritePlan plan = ExifLayoutPlanner.Create(null, new ExifMetadata(order, [fnumber]));
        Assert.Equal(order, plan.ByteOrder);
        Assert.Equal(2, plan.Directories.Count);
        PlannedEntry link = Assert.Single(plan.Directories[0].Entries);
        Assert.Equal((ushort)0x8769, link.TagId);
        Assert.Equal(26U, link.ScalarOverride);
        Assert.Null(link.Value);
        Assert.Equal(26, plan.Directories[1].Offset);
        Assert.Equal(44, Assert.Single(plan.Directories[1].Entries).DataOffset);
        Assert.Equal(52, plan.Length);
    }

    [Fact]
    public void PreservesEmptyDirectoriesAndRebasesInteropLink()
    {
        ExifEntry link = Scalar(ExifDirectoryKind.Ifd0, 0x8769, 100);
        ExifEntry interop = Scalar(ExifDirectoryKind.Exif, 0xA005, 200);
        ExifMetadata metadata = Snapshot(link, interop);
        var source = new TiffReadResult(metadata,
            new[] { ExifDirectoryKind.Ifd0, ExifDirectoryKind.Exif, ExifDirectoryKind.Interop }, null);
        ExifWritePlan plan = ExifLayoutPlanner.Create(source, metadata);
        Assert.Equal(3, plan.Directories.Count);
        Assert.Equal(44U, Assert.Single(plan.Directories[1].Entries).ScalarOverride);
        Assert.Empty(plan.Directories[2].Entries);
        Assert.Equal(50, plan.Length);
    }

    [Fact]
    public void OwnsThumbnailAndRecalculatesOffsetLengthAndNextIfd()
    {
        var compression = new ExifEntry(new(ExifDirectoryKind.Thumbnail, 0x0103),
            ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [6, 0]));
        ExifMetadata metadata = Snapshot(compression,
            Scalar(ExifDirectoryKind.Thumbnail, 0x0201, 100), Scalar(ExifDirectoryKind.Thumbnail, 0x0202, 4));
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        var original = new TiffReadResult(metadata, new[] { ExifDirectoryKind.Ifd0, ExifDirectoryKind.Thumbnail }, jpeg);
        ExifWritePlan plan = ExifLayoutPlanner.Create(original, metadata);
        jpeg[0] = 0;
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, plan.Thumbnail.ToArray());
        Assert.Equal(14U, plan.Directories[0].NextIfdOffset);
        Assert.Equal(56, plan.ThumbnailOffset);
        Assert.Equal(60, plan.Length);
        Assert.Equal(56U, plan.Directories[1].Entries.Single(e => e.TagId == 0x0201).ScalarOverride);
        Assert.Equal(4U, plan.Directories[1].Entries.Single(e => e.TagId == 0x0202).ScalarOverride);
    }

    [Theory]
    [InlineData(0xFFFF, TiffFieldType.Short)]
    [InlineData(0x927C, TiffFieldType.Undefined)]
    [InlineData(0x014A, TiffFieldType.Long)]
    public void UnknownAndOpaqueSourceCannotBeDiscardedToPermitRebuild(int id, TiffFieldType type)
    {
        int width = ExifValue.GetWidth(type);
        var entry = new ExifEntry(new(ExifDirectoryKind.Exif, (ushort)id),
            ExifValue.FromRaw(type, 1, Endianness.LittleEndian, new byte[width]));
        ExifMetadata metadata = Snapshot(entry);
        var original = new TiffReadResult(metadata, new[] { ExifDirectoryKind.Ifd0, ExifDirectoryKind.Exif }, null);
        Assert.Equal(MetadataErrorCode.UnsafeRelocation,
            Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(original, Snapshot())).Code);
        Assert.Equal(MetadataErrorCode.UnsafeRelocation,
            Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(null, metadata)).Code);
    }

    [Fact]
    public void StructuralFieldsCannotBeForgedOrRemoved()
    {
        ExifMetadata metadata = Snapshot(Scalar(ExifDirectoryKind.Ifd0, 0x8769, 0));
        var original = new TiffReadResult(metadata, new[] { ExifDirectoryKind.Ifd0 }, null);
        Assert.Equal(0U, ExifLayoutPlanner.Create(original, metadata).Directories[0].Entries[0].ScalarOverride);
        Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(null, metadata));
        Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(original, Snapshot()));
        Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(original,
            Snapshot(Scalar(ExifDirectoryKind.Ifd0, 0x8769, 20))));
    }

    [Theory]
    [InlineData(65501, true)]
    [InlineData(65502, false)]
    public void ExactApp1MaximumDoesNotRequireTrailingPad(int textLength, bool fits)
    {
        ExifMetadata metadata = Snapshot(Text(ExifDirectoryKind.Ifd0, 0x013B, textLength));
        if (fits) Assert.Equal(65527, ExifLayoutPlanner.Create(null, metadata).Length);
        else Assert.Equal(MetadataErrorCode.ExifTooLarge,
            Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(null, metadata)).Code);
    }

    [Theory]
    [InlineData("directories")]
    [InlineData("depth")]
    [InlineData("entries")]
    [InlineData("value")]
    [InlineData("retained")]
    public void RechecksBudgetsAfterSynthesizingPointers(string budget)
    {
        ExifMetadata metadata = Snapshot(Text(ExifDirectoryKind.Exif, 0x9003, 20));
        MetadataLimits limits = budget switch
        {
            "directories" => new() { MaxDirectories = 1 },
            "depth" => new() { MaxDirectoryDepth = 1 },
            "entries" => new() { MaxTotalEntries = 1 },
            "value" => new() { MaxValueBytes = 10 },
            _ => new() { MaxRetainedMetadataBytes = 20 }
        };
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(null, metadata, limits)).Code);
    }

    [Fact]
    public void RejectsUnsupportedShapeAndInvalidAsciiAndOrder()
    {
        var wrong = new ExifEntry(new(ExifDirectoryKind.Ifd0, 0x0112),
            ExifValue.FromRaw(TiffFieldType.Byte, 1, Endianness.LittleEndian, [6]));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature,
            Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(null, Snapshot(wrong))).Code);
        foreach (byte[] bytes in new byte[][] { [65], [0xFF, 0] })
        {
            var artist = new ExifEntry(new(ExifDirectoryKind.Ifd0, 0x013B),
                ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes));
            Assert.Equal(MetadataErrorCode.InvalidExif,
                Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(null, Snapshot(artist))).Code);
        }
        var original = new TiffReadResult(Snapshot(), new[] { ExifDirectoryKind.Ifd0 }, null);
        Assert.Throws<ArgumentException>(() => ExifLayoutPlanner.Create(original, new ExifMetadata(Endianness.BigEndian, [])));
    }

    [Fact]
    public void EveryPlannedRangeIsInBoundsAndDisjoint()
    {
        ExifWritePlan plan = ExifLayoutPlanner.Create(null, Snapshot(
            Text(ExifDirectoryKind.Ifd0, 0x013B, 5), Text(ExifDirectoryKind.Ifd0, 0x010F, 7),
            Text(ExifDirectoryKind.Exif, 0x9003, 20)));
        var ranges = new List<(int Start, int End)> { (0, 8) };
        foreach (PlannedDirectory directory in plan.Directories)
        {
            Assert.Equal(0, directory.Offset % 2);
            ranges.Add((directory.Offset, directory.Offset + 2 + 12 * directory.Entries.Count + 4));
            foreach (PlannedEntry entry in directory.Entries)
                if (entry.DataOffset is int at)
                {
                    Assert.Equal(0, at % 2);
                    Assert.NotNull(entry.Value);
                    ranges.Add((at, at + entry.Value.ByteLength));
                }
        }
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 0; i < ranges.Count; i++)
        {
            Assert.InRange(ranges[i].Start, 0, plan.Length);
            Assert.InRange(ranges[i].End, ranges[i].Start, plan.Length);
            if (i != 0) Assert.True(ranges[i - 1].End <= ranges[i].Start);
        }
    }

    [Fact]
    public void GeneratedPointerAlsoConsumesValueBudget()
    {
        var altitude = new ExifEntry(new(ExifDirectoryKind.Gps, 5),
            ExifValue.FromRaw(TiffFieldType.Byte, 1, Endianness.LittleEndian, [0]));
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(null,
                Snapshot(altitude), new MetadataLimits { MaxValueBytes = 1 })).Code);
    }

    private static ExifMetadata Snapshot(params ExifEntry[] entries) => new(Endianness.LittleEndian, entries);
    private static ExifEntry Text(ExifDirectoryKind role, ushort tag, int size) =>
        new(new(role, tag), ExifValue.FromRaw(TiffFieldType.Ascii, (uint)size, Endianness.LittleEndian, new byte[size]));
    private static ExifEntry Scalar(ExifDirectoryKind role, ushort tag, uint number) =>
        new(new(role, tag), ExifValue.FromRaw(TiffFieldType.Long, 1, Endianness.LittleEndian,
            [(byte)number, (byte)(number >> 8), (byte)(number >> 16), (byte)(number >> 24)]));
}
