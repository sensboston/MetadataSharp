using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class ExifTiffReaderTests
{
    private static byte[] Orientation(bool big = false) => big
        ? [0x4D, 0x4D, 0, 42, 0, 0, 0, 8, 0, 1, 1, 0x12, 0, 3, 0, 0, 0, 1, 0, 6, 0, 0, 0, 0, 0, 0]
        : [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentInlineOrientationUsesHeaderByteOrder(bool big)
    {
        byte[] source = Orientation(big);
        TiffReadResult result = ExifTiffReader.Read(source);
        Assert.Equal(big ? Endianness.BigEndian : Endianness.LittleEndian, result.Metadata.ByteOrder);
        ExifEntry entry = Assert.Single(result.Metadata.Entries);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), entry.Key);
        byte[] copy = new byte[2];
        entry.Value.CopyRawTo(copy);
        Assert.Equal(big ? new byte[] { 0, 6 } : new byte[] { 6, 0 }, copy);
        source.AsSpan().Clear();
        entry.Value.CopyRawTo(copy);
        Assert.Equal(big ? new byte[] { 0, 6 } : new byte[] { 6, 0 }, copy);
        Assert.Null(result.Thumbnail);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FollowsAllApprovedLinksAndPreservesThumbnailAndOpaqueData(bool big)
    {
        byte[] data = Graph(big);
        TiffReadResult result = ExifTiffReader.Read(data);
        Assert.Equal(5, result.Directories.Count);
        Assert.Equal(8, result.Metadata.Entries.Count);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, result.Thumbnail);
        Assert.True(result.Metadata.TryGetEntry(new(ExifDirectoryKind.Exif, 0x927C), out ExifEntry? maker));
        byte[] payload = new byte[6];
        maker.Value.CopyRawTo(payload);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, payload);
        Assert.True(result.Metadata.TryGetEntry(new(ExifDirectoryKind.Interop, 0xFFFF), out _));
        Assert.Contains(ExifDirectoryKind.Gps, result.Directories);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownTagAndZeroCountAreRawNotInterpreted(bool big)
    {
        byte[] data = Orientation(big);
        Put16(data, 10, 0xFFFF, big);
        Put32(data, 14, 0, big);
        ExifEntry entry = Assert.Single(ExifTiffReader.Read(data).Metadata.Entries);
        Assert.Equal((ushort)0xFFFF, entry.Key.TagId);
        Assert.Equal(0U, entry.Value.Count);
        Assert.Equal(0, entry.Value.ByteLength);
    }

    [Fact]
    public void EveryTruncatedPrefixFailsWithStructuredMetadataError()
    {
        byte[] data = Orientation();
        for (int length = 0; length < data.Length; length++)
        {
            byte[] prefix = data[..length];
            MetadataException error = Assert.Throws<MetadataException>(() => ExifTiffReader.Read(prefix));
            Assert.Equal(MetadataErrorCode.InvalidExif, error.Code);
        }
    }

    [Theory]
    [InlineData("bom", MetadataErrorCode.InvalidExif)]
    [InlineData("magic", MetadataErrorCode.InvalidExif)]
    [InlineData("bigtiff", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("zero-ifd", MetadataErrorCode.InvalidExif)]
    [InlineData("odd-ifd", MetadataErrorCode.InvalidExif)]
    [InlineData("past-ifd", MetadataErrorCode.InvalidExif)]
    [InlineData("header-ifd", MetadataErrorCode.InvalidExif)]
    [InlineData("type", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("overflow", MetadataErrorCode.InvalidExif)]
    [InlineData("huge-value", MetadataErrorCode.LimitExceeded)]
    [InlineData("bad-value", MetadataErrorCode.InvalidExif)]
    [InlineData("header-value", MetadataErrorCode.InvalidExif)]
    [InlineData("self-value", MetadataErrorCode.InvalidExif)]
    [InlineData("odd-value", MetadataErrorCode.InvalidExif)]
    [InlineData("subifd", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("strip", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("pointer-type", MetadataErrorCode.InvalidExif)]
    [InlineData("pointer-count", MetadataErrorCode.InvalidExif)]
    [InlineData("pointer-role", MetadataErrorCode.InvalidExif)]
    public void RejectsMalformedAndUnsupportedStructures(string mutation, MetadataErrorCode code)
    {
        byte[] data = new byte[40];
        Orientation().CopyTo(data, 0);
        switch (mutation)
        {
            case "bom": data[0] = 0; break;
            case "magic": Put16(data, 2, 41); break;
            case "bigtiff": Put16(data, 2, 43); break;
            case "zero-ifd": Put32(data, 4, 0); break;
            case "odd-ifd": Put32(data, 4, 9); break;
            case "past-ifd": Put32(data, 4, uint.MaxValue); break;
            case "header-ifd": Put32(data, 4, 2); break;
            case "type": Put16(data, 12, 129); break;
            case "overflow": Put16(data, 12, 5); Put32(data, 14, uint.MaxValue); break;
            case "huge-value": Put16(data, 12, 5); Put32(data, 14, 9000); break;
            case "bad-value": Put32(data, 14, 3); Put32(data, 18, uint.MaxValue); break;
            case "header-value": Put32(data, 14, 3); Put32(data, 18, 0); break;
            case "self-value": Put32(data, 14, 3); Put32(data, 18, 10); break;
            case "odd-value": Put32(data, 14, 3); Put32(data, 18, 27); break;
            case "subifd": Put16(data, 10, 0x014A); break;
            case "strip": Put16(data, 10, 0x0111); break;
            case "pointer-type": Put16(data, 10, 0x8769); break;
            case "pointer-count": Put16(data, 10, 0x8769); Put16(data, 12, 4); Put32(data, 14, 0); break;
            case "pointer-role": Put16(data, 10, 0xA005); Put16(data, 12, 4); break;
        }
        MetadataException error = Assert.Throws<MetadataException>(() => ExifTiffReader.Read(data));
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("shared")]
    [InlineData("nested-next")]
    [InlineData("beyond-thumbnail")]
    public void RejectsGraphAmbiguityAndUnsupportedChains(string mutation)
    {
        byte[] data = Graph();
        switch (mutation)
        {
            case "cycle": Put32(data, 18, 8); break;
            case "shared": Put32(data, 30, 38); break;
            case "nested-next": Put32(data, 64, 80); break;
            case "beyond-thumbnail": Put32(data, 136, 80); break;
        }
        MetadataException error = Assert.Throws<MetadataException>(() => ExifTiffReader.Read(data));
        Assert.Equal(mutation is "cycle" or "shared" ? MetadataErrorCode.InvalidExif : MetadataErrorCode.UnsupportedFeature, error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateAndUnsortedEntriesAreNotFirstWins(bool duplicate)
    {
        byte[] data = Graph();
        Put16(data, 22, duplicate ? (ushort)0x8769 : (ushort)0x0100);
        MetadataException error = Assert.Throws<MetadataException>(() => ExifTiffReader.Read(data));
        Assert.Equal(duplicate ? MetadataErrorCode.AmbiguousMetadata : MetadataErrorCode.InvalidExif, error.Code);
    }

    [Theory]
    [InlineData("directories")]
    [InlineData("depth")]
    [InlineData("per-directory")]
    [InlineData("total-entries")]
    [InlineData("value")]
    [InlineData("retained")]
    [InlineData("input")]
    [InlineData("thumbnail")]
    public void EnforcesBudgetsBeforeAllocationOrTraversal(string budget)
    {
        var limits = budget switch
        {
            "directories" => new MetadataLimits { MaxDirectories = 1 },
            "depth" => new MetadataLimits { MaxDirectoryDepth = 1 },
            "per-directory" => new MetadataLimits { MaxEntriesPerDirectory = 1 },
            "total-entries" => new MetadataLimits { MaxTotalEntries = 2 },
            "value" => new MetadataLimits { MaxValueBytes = 4 },
            "retained" => new MetadataLimits { MaxRetainedMetadataBytes = Graph().Length },
            "input" => new MetadataLimits { MaxInputBytes = 10 },
            _ => new MetadataLimits { MaxThumbnailBytes = 3 }
        };
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Assert.Throws<MetadataException>(() => ExifTiffReader.Read(Graph(), limits)).Code);
    }

    [Theory]
    [InlineData("missing-pair", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("wrong-compression", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("wrong-offset-type", MetadataErrorCode.InvalidExif)]
    [InlineData("bad-offset", MetadataErrorCode.InvalidExif)]
    [InlineData("overlap", MetadataErrorCode.InvalidExif)]
    [InlineData("bad-boundary", MetadataErrorCode.InvalidExif)]
    [InlineData("short", MetadataErrorCode.InvalidExif)]
    public void ValidatesThumbnailRangesAndRepresentation(string mutation, MetadataErrorCode expected)
    {
        byte[] data = Graph();
        switch (mutation)
        {
            case "missing-pair": Put16(data, 124, 0x0203); break;
            case "wrong-compression": Put16(data, 108, 1); break;
            case "wrong-offset-type": Put16(data, 114, 3); break;
            case "bad-offset": Put32(data, 120, uint.MaxValue); break;
            case "overlap": Put32(data, 120, 8); break;
            case "bad-boundary": data[140] = 0; break;
            case "short": Put32(data, 132, 1); break;
        }
        Assert.Equal(expected, Assert.Throws<MetadataException>(() => ExifTiffReader.Read(data)).Code);
    }

    [Fact]
    public void ReportsAbsoluteOffsetsAndValidatesCallerArguments()
    {
        byte[] data = Orientation();
        Put16(data, 12, 13);
        MetadataException error = Assert.Throws<MetadataException>(() => ExifTiffReader.Read(data, sourceBase: 100));
        Assert.Equal(112L, error.SourceOffset);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExifTiffReader.Read(data, sourceBase: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExifTiffReader.Read(data, sourceBase: long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExifTiffReader.Read(data, new MetadataLimits { MaxDirectories = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExifTiffReader.Read(data, new MetadataLimits { CopyBufferBytes = 1 }));
        Assert.Equal(MetadataErrorCode.ExifTooLarge,
            Assert.Throws<MetadataException>(() => ExifTiffReader.Read(new byte[65_528])).Code);
    }

    [Fact]
    public void NullOptionalPointersAndEmptyDirectoriesAreRetained()
    {
        byte[] data = Orientation();
        Put16(data, 10, 0x8769); Put16(data, 12, 4); Put32(data, 18, 0);
        Assert.Single(ExifTiffReader.Read(data).Directories);
        byte[] empty = [0x49, 0x49, 42, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Empty(ExifTiffReader.Read(empty).Metadata.Entries);
    }

    [Fact]
    public void BoundedMutationSmokeTestNeverLeaksIndexOrOverflowExceptions()
    {
        var random = new Random(4096);
        byte[] source = Graph();
        for (int i = 0; i < 256; i++)
        {
            byte[] data = (byte[])source.Clone();
            data[random.Next(data.Length)] = (byte)random.Next(256);
            try { ExifTiffReader.Read(data); }
            catch (MetadataException) { }
        }
    }

    // Authored synthetic layout, not serialized by production code. All offsets are TIFF-relative.
    private static byte[] Graph(bool big = false)
    {
        byte[] data = new byte[150];
        data[0] = data[1] = big ? (byte)0x4D : (byte)0x49;
        Put16(data, 2, 42, big); Put32(data, 4, 8, big);
        Put16(data, 8, 2, big);
        Entry(data, 10, 0x8769, 4, 1, 38, big);
        Entry(data, 22, 0x8825, 4, 1, 68, big);
        Put32(data, 34, 98, big);
        Put16(data, 38, 2, big);
        Entry(data, 40, 0x927C, 7, 6, 144, big);
        Entry(data, 52, 0xA005, 4, 1, 80, big);
        Put16(data, 68, 0, big); // Empty GPS directory retained under the approved compatibility policy.
        Put16(data, 80, 1, big);
        Entry(data, 82, 0xFFFF, 1, 1, 0, big);
        Put16(data, 98, 3, big);
        Entry(data, 100, 0x0103, 3, 1, 6, big);
        Entry(data, 112, 0x0201, 4, 1, 140, big);
        Entry(data, 124, 0x0202, 4, 1, 4, big);
        new byte[] { 0xFF, 0xD8, 0xFF, 0xD9, 1, 2, 3, 4, 5, 6 }.CopyTo(data, 140);
        return data;
    }

    private static void Entry(byte[] data, int at, ushort tag, ushort type, uint count, uint value, bool big)
    {
        Put16(data, at, tag, big); Put16(data, at + 2, type, big); Put32(data, at + 4, count, big);
        if (type == 3 && count == 1) Put16(data, at + 8, (ushort)value, big);
        else Put32(data, at + 8, value, big);
    }

    private static void Put16(byte[] data, int at, ushort value, bool big = false)
    {
        if (big) BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), value);
        else BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), value);
    }

    private static void Put32(byte[] data, int at, uint value, bool big = false)
    {
        if (big) BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), value);
        else BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
    }
}
