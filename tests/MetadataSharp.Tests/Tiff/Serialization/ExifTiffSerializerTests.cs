using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Tiff.Serialization;
using Xunit;

namespace MetadataSharp.Tests.Tiff.Serialization;

public sealed class ExifTiffSerializerTests
{
    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void OrientationMatchesIndependentKnownBytes(Endianness order)
    {
        byte[] raw = order == Endianness.LittleEndian ? [6, 0] : [0, 6];
        var entry = new ExifEntry(new(ExifDirectoryKind.Ifd0, 0x0112),
            ExifValue.FromRaw(TiffFieldType.Short, 1, order, raw));
        ExifWritePlan plan = ExifLayoutPlanner.Create(null, new ExifMetadata(order, [entry]));
        byte[] output = new byte[plan.Length];
        ExifTiffSerializer.Write(plan, output);
        byte[] expected = order == Endianness.LittleEndian
            ? [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0]
            : [0x4D, 0x4D, 0, 42, 0, 0, 0, 8, 0, 1, 1, 0x12, 0, 3, 0, 0, 0, 1, 0, 6, 0, 0, 0, 0, 0, 0];
        Assert.Equal(expected, output);
        ExifEntry read = Assert.Single(ExifTiffReader.Read(output).Metadata.Entries);
        Assert.Equal(raw, read.Value.RawBytes.ToArray());
        Assert.Equal(raw, entry.Value.RawBytes.ToArray());
    }

    [Fact]
    public void ExternalAsciiMatchesIndependentExpectedOffsetAndBytes()
    {
        var entry = new ExifEntry(new(ExifDirectoryKind.Ifd0, 0x013B),
            ExifValue.FromRaw(TiffFieldType.Ascii, 5, Endianness.LittleEndian, [65, 66, 67, 68, 0]));
        ExifWritePlan plan = ExifLayoutPlanner.Create(null, new ExifMetadata(Endianness.LittleEndian, [entry]));
        byte[] output = new byte[31];
        ExifTiffSerializer.Write(plan, output);
        byte[] expected = [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x3B, 1, 2, 0, 5, 0, 0, 0, 26, 0, 0, 0, 0, 0, 0, 0, 65, 66, 67, 68, 0];
        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void NumericSubIfdRoundTripKeepsRationalComponentsAndSourceBytes(Endianness order)
    {
        byte[] source = order == Endianness.LittleEndian ? [1, 0, 0, 0, 0, 0, 0, 0] : [0, 0, 0, 1, 0, 0, 0, 0];
        var entry = new ExifEntry(new(ExifDirectoryKind.Exif, 0x829A), ExifValue.FromRaw(TiffFieldType.Rational, 1, order, source));
        ExifWritePlan plan = ExifLayoutPlanner.Create(null, new ExifMetadata(order, [entry]));
        byte[] output = new byte[plan.Length + 3];
        output.AsSpan().Fill(0xAA);
        ExifTiffSerializer.Write(plan, output);
        Assert.All(output[plan.Length..], b => Assert.Equal((byte)0xAA, b));
        TiffReadResult read = ExifTiffReader.Read(output.AsSpan(0, plan.Length));
        Assert.True(read.Metadata.TryGetEntry(entry.Key, out ExifEntry? parsed));
        Assert.Equal(source, parsed.Value.RawBytes.ToArray());
        Assert.Equal(source, entry.Value.RawBytes.ToArray());
        ExifWritePlan again = ExifLayoutPlanner.Create(read, read.Metadata);
        byte[] second = new byte[again.Length];
        ExifTiffSerializer.Write(again, second);
        Assert.Equal(output[..plan.Length], second);
    }

    [Fact]
    public void ShortOutputAndStalePlanDoNotModifyDestination()
    {
        var empty = new ExifMetadata(Endianness.LittleEndian, []);
        ExifWritePlan plan = ExifLayoutPlanner.Create(null, empty);
        byte[] shortOutput = Enumerable.Repeat((byte)0xAA, plan.Length - 1).ToArray();
        Assert.Throws<ArgumentException>(() => ExifTiffSerializer.Write(plan, shortOutput));
        Assert.All(shortOutput, b => Assert.Equal((byte)0xAA, b));
        var invalid = new ExifWritePlan(plan.ByteOrder, plan.Length + 1, plan.Directories, null, []);
        byte[] output = Enumerable.Repeat((byte)0xCC, plan.Length + 1).ToArray();
        Assert.Throws<MetadataException>(() => ExifTiffSerializer.Write(invalid, output));
        Assert.All(output, b => Assert.Equal((byte)0xCC, b));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("offset")]
    [InlineData("next")]
    [InlineData("unknown")]
    [InlineData("missing-link")]
    public void ForgedLayoutsAreRejectedBeforeWriting(string mutation)
    {
        var value = ExifValue.FromRaw(TiffFieldType.Ascii, 5, Endianness.LittleEndian, [65, 66, 67, 68, 0]);
        var entry = new ExifEntry(new(ExifDirectoryKind.Ifd0, 0x013B), value);
        ExifWritePlan good = ExifLayoutPlanner.Create(null, new ExifMetadata(Endianness.LittleEndian, [entry]));
        PlannedDirectory directory = good.Directories[0];
        PlannedEntry plannedValue = directory.Entries[0];
        IReadOnlyList<PlannedDirectory> directories = mutation switch
        {
            "header" => [directory with { Offset = 0 }],
            "offset" => [directory with { Entries = new[] { plannedValue with { DataOffset = 0 } } }],
            "next" => [directory with { NextIfdOffset = 8 }],
            "unknown" => [directory with { Entries = new[] { plannedValue with { TagId = 0xFFFF } } }],
            _ => [new PlannedDirectory(ExifDirectoryKind.Ifd0, 8, 0, Array.Empty<PlannedEntry>()),
                new PlannedDirectory(ExifDirectoryKind.Gps, 14, 0, Array.Empty<PlannedEntry>())]
        };
        var bad = new ExifWritePlan(good.ByteOrder, mutation == "missing-link" ? 20 : good.Length, directories, null, []);
        byte[] output = Enumerable.Repeat((byte)0xAA, 100).ToArray();
        Assert.Throws<MetadataException>(() => ExifTiffSerializer.Write(bad, output));
        Assert.All(output, b => Assert.Equal((byte)0xAA, b));
    }

    [Fact]
    public void ThumbnailBytesAndRebasedReferencesSurviveRoundTrip()
    {
        ExifValue Scalar(uint value) => ExifValue.FromRaw(TiffFieldType.Long, 1, Endianness.LittleEndian,
            [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)]);
        ExifEntry[] entries = [
            new(new(ExifDirectoryKind.Thumbnail, 0x0103), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [6, 0])),
            new(new(ExifDirectoryKind.Thumbnail, 0x0201), Scalar(100)),
            new(new(ExifDirectoryKind.Thumbnail, 0x0202), Scalar(4))];
        var metadata = new ExifMetadata(Endianness.LittleEndian, entries);
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        var original = new TiffReadResult(metadata, new[] { ExifDirectoryKind.Ifd0, ExifDirectoryKind.Thumbnail }, jpeg);
        ExifWritePlan plan = ExifLayoutPlanner.Create(original, metadata);
        byte[] output = new byte[plan.Length];
        ExifTiffSerializer.Write(plan, output);
        TiffReadResult result = ExifTiffReader.Read(output);
        Assert.Equal(jpeg, result.Thumbnail);
        Assert.Equal(56, plan.ThumbnailOffset);
        Assert.Equal(jpeg, output[56..]);
        Assert.Equal(jpeg, original.Thumbnail);
    }

    [Fact]
    public void ExactMaximumPayloadAndAlignmentGapsAreWrittenDeterministically()
    {
        ExifEntry Text(ushort tag, int size) => new(new(ExifDirectoryKind.Ifd0, tag),
            ExifValue.FromRaw(TiffFieldType.Ascii, (uint)size, Endianness.LittleEndian, new byte[size]));
        ExifWritePlan maximum = ExifLayoutPlanner.Create(null,
            new ExifMetadata(Endianness.LittleEndian, [Text(0x013B, 65501)]));
        byte[] output = new byte[65527];
        ExifTiffSerializer.Write(maximum, output);
        Assert.Single(ExifTiffReader.Read(output).Metadata.Entries);
        ExifWritePlan gap = ExifLayoutPlanner.Create(null,
            new ExifMetadata(Endianness.LittleEndian, [Text(0x013B, 5), Text(0x010F, 5)]));
        byte[] sentinel = Enumerable.Repeat((byte)0xAA, gap.Length).ToArray();
        ExifTiffSerializer.Write(gap, sentinel);
        Assert.Equal((byte)0, sentinel[43]);
        Assert.Equal(2, ExifTiffReader.Read(sentinel).Metadata.Entries.Count);
    }
}
