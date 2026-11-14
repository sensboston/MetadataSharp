using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Formats.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

/// <summary>
/// An authored whole TIFF file whose primary directory points at image directories with tag 0x014A.
/// Every offset is written here; nothing comes from production code.
/// </summary>
public sealed class TiffSubImageTests
{
    private sealed class Sample(int length)
    {
        internal byte[] Bytes { get; } = new byte[length];

        internal Sample Header(uint first = 8)
        {
            Bytes[0] = Bytes[1] = 0x49; // little endian
            U16(2, 42);
            U32(4, first);
            return this;
        }

        internal Sample Directory(int at, uint next, params (ushort Tag, ushort Type, uint Count, uint Value)[] entries)
        {
            U16(at, (ushort)entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                int p = at + 2 + i * 12;
                U16(p, entries[i].Tag);
                U16(p + 2, entries[i].Type);
                U32(p + 4, entries[i].Count);
                if (entries[i].Type == 3 && entries[i].Count == 1)
                    U16(p + 8, (ushort)entries[i].Value);
                else
                    U32(p + 8, entries[i].Value);
            }
            U32(at + 2 + entries.Length * 12, next);
            return this;
        }

        internal void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(at), value);
        internal void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(at), value);
    }

    private const ushort SubIfds = 0x014A;
    private const ushort ImageWidth = 0x0100;
    private const ushort NewSubfileType = 0x00FE;
    private const ushort Compression = 0x0103;

    // Two image directories behind the primary one, and the second one's tags.
    private static Sample TwoImages()
    {
        var file = new Sample(1024).Header();
        file.Directory(8, 0, (ImageWidth, 4, 1, 32), (SubIfds, 4, 2, 64), (NewSubfileType, 4, 1, 0));
        file.U32(64, 200); // the first SubIFD offset
        file.U32(68, 300); // the second one
        file.Directory(200, 0, (NewSubfileType, 4, 1, 0), (Compression, 3, 1, 7));
        file.Directory(300, 0, (ImageWidth, 4, 1, 64));
        return file;
    }

    [Fact]
    public void ImageDirectoriesNamedBySubIfdsAreListedApartFromTheMetadata()
    {
        ExifMetadata metadata = TiffExif.Read(TwoImages().Bytes);

        Assert.Equal(2, metadata.SubImages.Count);
        Assert.Equal(1, metadata.SubImages[0].Index);
        Assert.Equal(2, metadata.SubImages[1].Index);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, NewSubfileType), metadata.SubImages[0].Entries[0].Key);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, Compression), metadata.SubImages[0].Entries[1].Key);
        Assert.True(metadata.SubImages[0].Entries[1].Value.TryGetUInt32(out uint compression));
        Assert.Equal(7u, compression);
        Assert.Single(metadata.SubImages[1].Entries);
        // The pointer stays an ordinary entry of the primary directory; the tags of the image
        // directories are not metadata of the file.
        Assert.True(metadata.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, SubIfds), out ExifEntry? pointer));
        Assert.Equal(2u, pointer.Value.Count);
        Assert.DoesNotContain(metadata.Entries, entry => entry.Key.TagId == Compression);
        Assert.Equal(3, metadata.Entries.Count);
        Assert.True(metadata.IsEditable);
    }

    [Fact]
    public void AnEmptySubIfdSlotIsNotADirectory()
    {
        var file = new Sample(1024).Header();
        file.Directory(8, 0, (ImageWidth, 4, 1, 32), (SubIfds, 4, 3, 64));
        file.U32(64, 200);
        file.U32(68, 0);
        file.U32(72, 300);
        file.Directory(200, 0, (ImageWidth, 4, 1, 16));
        file.Directory(300, 0, (ImageWidth, 4, 1, 8));

        ExifMetadata metadata = TiffExif.Read(file.Bytes);

        Assert.Equal(new[] { 1, 2 }, metadata.SubImages.Select(image => image.Index));
        Assert.Equal(16u, metadata.SubImages[0].Entries[0].Value.TryGetUInt32(out uint first) ? first : 0);
        Assert.Equal(8u, metadata.SubImages[1].Entries[0].Value.TryGetUInt32(out uint second) ? second : 0);
    }

    [Fact]
    public void AnImageDirectoryThatRepeatsAnotherOffsetIsRefused()
    {
        var file = new Sample(1024).Header();
        file.Directory(8, 0, (ImageWidth, 4, 1, 32), (SubIfds, 4, 2, 64));
        file.U32(64, 200);
        file.U32(68, 200);
        file.Directory(200, 0, (ImageWidth, 4, 1, 16));

        MetadataException failure = Assert.Throws<MetadataException>(() => TiffExif.Read(file.Bytes));
        Assert.Equal(MetadataErrorCode.InvalidExif, failure.Code);
    }
}
