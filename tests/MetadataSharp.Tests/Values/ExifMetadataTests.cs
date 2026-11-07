using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Values;

public sealed class ExifMetadataTests
{
    [Fact]
    public void MetadataOwnsItsSnapshotAndUnknownIdsRemainDistinguishableByDirectory()
    {
        byte[] input = [0xAB, 0xCD];
        var firstKey = new ExifTagKey(ExifDirectoryKind.Ifd0, 0xFFFF);
        var secondKey = new ExifTagKey(ExifDirectoryKind.Gps, 0xFFFF);
        ExifValue raw = ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.BigEndian, input);
        var first = new ExifEntry(firstKey, raw);
        var second = new ExifEntry(secondKey, raw);
        ExifEntry[] entries = [first, second];
        var metadata = new ExifMetadata(Endianness.BigEndian, entries);
        entries[0] = second;
        input.AsSpan().Clear();
        Assert.Equal(Endianness.BigEndian, metadata.ByteOrder);
        Assert.Same(first, metadata.Entries[0]);
        Assert.True(metadata.TryGetEntry(firstKey, out ExifEntry? found));
        Assert.Same(first, found);
        Assert.True(metadata.TryGetEntry(secondKey, out found));
        Assert.Same(second, found);
        byte[] copy = new byte[2];
        metadata.Entries[0].Value.CopyRawTo(copy);
        Assert.Equal(new byte[] { 0xAB, 0xCD }, copy);
        Assert.False(metadata.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Exif, 0xFFFF), out found));
        Assert.Null(found);
        var exposed = Assert.IsAssignableFrom<IList<ExifEntry>>(metadata.Entries);
        Assert.Throws<NotSupportedException>(() => exposed[0] = second);
        Assert.Throws<NotSupportedException>(() => exposed.Clear());
    }

    [Fact]
    public void InternalConstructionRejectsAmbiguousOrInconsistentState()
    {
        ExifValue raw = ExifValue.FromRaw(TiffFieldType.Byte, 1, Endianness.LittleEndian, [42]);
        var key = new ExifTagKey(ExifDirectoryKind.Ifd0, 1);
        var entry = new ExifEntry(key, raw);
        Assert.Throws<ArgumentException>(() => new ExifMetadata(Endianness.LittleEndian, [entry, entry]));
        Assert.Throws<ArgumentException>(() => new ExifMetadata(Endianness.BigEndian, [entry]));
        Assert.Throws<ArgumentNullException>(() => new ExifMetadata(Endianness.LittleEndian, null!));
        Assert.Throws<ArgumentNullException>(() => new ExifMetadata(Endianness.LittleEndian, [null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExifMetadata((Endianness)99, []));
        Assert.Throws<ArgumentNullException>(() => new ExifEntry(key, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExifEntry(new ExifTagKey((ExifDirectoryKind)99, 1), raw));
    }

    [Fact]
    public void EmptySnapshotAndDirectoryAwareKeyEqualityAreSupported()
    {
        var metadata = new ExifMetadata(Endianness.LittleEndian, []);
        Assert.Empty(metadata.Entries);
        Assert.False(metadata.TryGetEntry(default, out _));
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 1), new ExifTagKey(ExifDirectoryKind.Ifd0, 1));
        Assert.NotEqual(new ExifTagKey(ExifDirectoryKind.Ifd0, 1), new ExifTagKey(ExifDirectoryKind.Exif, 1));
    }
}
