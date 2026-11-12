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

    // The read keeps a thumbnail IFD whose JPEG failed validation, but retains no bytes for it. A rebuild
    // of that source cannot carry a picture it never owned: refuse, whether the offset/length pair survives
    // in the directory or the directory holds only the compression field.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceThumbnailDirectoryWithoutRetainedBytesCannotBeRebuilt(bool pairPresent)
    {
        var compression = new ExifEntry(new(ExifDirectoryKind.Thumbnail, 0x0103),
            ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [6, 0]));
        ExifMetadata metadata = pairPresent
            ? Snapshot(compression, Scalar(ExifDirectoryKind.Thumbnail, 0x0201, 140),
                Scalar(ExifDirectoryKind.Thumbnail, 0x0202, 4))
            : Snapshot(compression);
        var original = new TiffReadResult(metadata,
            new[] { ExifDirectoryKind.Ifd0, ExifDirectoryKind.Thumbnail }, null);
        MetadataException error = Assert.Throws<MetadataException>(
            () => ExifLayoutPlanner.Create(original, metadata));
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, error.Code);
    }

    [Fact]
    public void ASourceWithoutAnyThumbnailDirectoryIsNeverBlockedByTheCarryRule()
    {
        ExifMetadata metadata = Snapshot(Text(ExifDirectoryKind.Ifd0, 0x0110, 5));
        var original = new TiffReadResult(metadata, new[] { ExifDirectoryKind.Ifd0 }, null);
        Assert.NotNull(ExifLayoutPlanner.Create(original, metadata));
    }

    // A tag the vocabulary does not list may address bytes the layout would move, so a source that
    // holds one is not laid out again. Set in a block that is new, the same tag addresses nothing.
    [Fact]
    public void AnUnlistedTagBlocksARebuildOfItsSourceAndIsLaidOutInANewBlock()
    {
        var entry = new ExifEntry(new(ExifDirectoryKind.Exif, 0xFFFF),
            ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [7, 0]));
        ExifMetadata metadata = Snapshot(entry);
        var original = new TiffReadResult(metadata, new[] { ExifDirectoryKind.Ifd0, ExifDirectoryKind.Exif }, null);
        Assert.Equal(MetadataErrorCode.UnsafeRelocation,
            Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(original, Snapshot())).Code);
        ExifWritePlan plan = ExifLayoutPlanner.Create(null, metadata);
        PlannedDirectory exif = Assert.Single(plan.Directories, directory => directory.Kind == ExifDirectoryKind.Exif);
        Assert.Equal(0xFFFF, Assert.Single(exif.Entries).TagId);
    }

    [Theory]
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

    [Fact]
    public void TheCeilingIsTheContainersAndTheBudgetsBoundALayoutWithoutOne()
    {
        // A text a JPEG segment cannot hold. The default ceiling is that segment; a container that
        // has none of its own passes none, and the plan is written and read back whole.
        ExifEntry text = new(new(ExifDirectoryKind.Ifd0, 0x010E),
            ExifValue.FromRaw(TiffFieldType.Ascii, 70_000, Endianness.LittleEndian, new byte[70_000]));
        ExifMetadata metadata = Snapshot(text);
        var limits = new MetadataLimits { MaxValueBytes = 70_000 };
        Assert.Equal(MetadataErrorCode.ExifTooLarge,
            Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(null, metadata, limits)).Code);

        ExifWritePlan plan = ExifLayoutPlanner.Create(null, metadata, limits, ExifTiffReader.NoCeiling);
        Assert.Equal(8 + 18 + 70_000, plan.Length);
        Assert.Equal(ExifTiffReader.NoCeiling, plan.Ceiling);
        byte[] block = new byte[plan.Length];
        ExifTiffSerializer.Write(plan, block);
        ExifMetadata read = ExifTiffReader.Inspect(block, limits, 0, ExifTiffReader.NoCeiling);
        Assert.True(read.TryGetEntry(text.Key, out ExifEntry? same));
        Assert.Equal(70_000, same.Value.ByteLength);

        // The same plan under the ceiling of a segment is not one the serializer writes.
        var capped = new ExifWritePlan(plan.ByteOrder, plan.Length, plan.Directories, plan.ThumbnailOffset, plan.Thumbnail);
        Assert.Equal(ExifTiffReader.SegmentCeiling, capped.Ceiling);
        Assert.Equal(MetadataErrorCode.InvalidExif,
            Assert.Throws<MetadataException>(() => ExifTiffSerializer.Write(capped, new byte[capped.Length])).Code);

        // Without a ceiling the budgets are the bound: the value budget, then the retained one.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(
            () => ExifLayoutPlanner.Create(null, metadata, new MetadataLimits(), ExifTiffReader.NoCeiling)).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(
            () => ExifLayoutPlanner.Create(null, metadata, new MetadataLimits { MaxValueBytes = 70_000, MaxRetainedMetadataBytes = 100_000 }, ExifTiffReader.NoCeiling)).Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExifLayoutPlanner.Create(null, metadata, limits, 13));
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
    public void RejectsUnsupportedShapeAndOrderAndTakesTextAsGiven()
    {
        // The type and count of a listed tag are held to its definition by ExifEdit.Set; the layout
        // holds only what it generates itself to a shape.
        var wrong = new ExifEntry(new(ExifDirectoryKind.Thumbnail, 0x0103),
            ExifValue.FromRaw(TiffFieldType.Long, 1, Endianness.LittleEndian, [6, 0, 0, 0]));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature,
            Assert.Throws<MetadataException>(() => ExifLayoutPlanner.Create(null, Snapshot(wrong))).Code);
        // Text that ExifEdit.Set refuses and SetUnchecked takes is laid out as it is given.
        foreach (byte[] bytes in new byte[][] { [65], [0xFF, 0] })
        {
            var artist = new ExifEntry(new(ExifDirectoryKind.Ifd0, 0x013B),
                ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes));
            Assert.Equal(0x013B, Assert.Single(Assert.Single(ExifLayoutPlanner.Create(null, Snapshot(artist)).Directories).Entries).TagId);
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
