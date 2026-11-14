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
        file.Directory(8, 0, (NewSubfileType, 4, 1, 0), (ImageWidth, 4, 1, 32), (SubIfds, 4, 2, 64));
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

    // A chain of picture directories: the primary directory's next link is the thumbnail one, and the
    // directories after it are pictures, as Canon CR2 and Pentax PEF keep them.
    private static Sample ChainedImages()
    {
        var file = new Sample(1024).Header();
        file.Directory(8, 200, (ImageWidth, 4, 1, 32));
        file.Directory(200, 300, (Compression, 3, 1, 1));
        file.Directory(300, 400, (ImageWidth, 4, 1, 64));
        file.Directory(400, 0, (ImageWidth, 4, 1, 16));
        return file;
    }

    [Fact]
    public void PictureDirectoriesChainedAfterTheThumbnailAreListedAsImageDirectories()
    {
        ExifMetadata metadata = TiffExif.Read(ChainedImages().Bytes);

        // The reference numbers a chain from the second directory, after the thumbnail one.
        Assert.Equal(new[] { 2, 3 }, metadata.SubImages.Select(image => image.Index));
        Assert.All(metadata.SubImages, image => Assert.Equal(ExifSubImageKind.ImageDirectory, image.Kind));
        Assert.Equal(64u, metadata.SubImages[0].Entries[0].Value.TryGetUInt32(out uint raw) ? raw : 0);
        Assert.Equal(16u, metadata.SubImages[1].Entries[0].Value.TryGetUInt32(out uint reduced) ? reduced : 0);
        // The chain is not metadata: the primary directory and the thumbnail one are.
        Assert.Equal(2, metadata.Entries.Count);
        Assert.Contains(metadata.Entries,
            entry => entry.Key == new ExifTagKey(ExifDirectoryKind.Thumbnail, Compression));
    }

    [Fact]
    public void ASubIfdListAndAChainAreNumberedApart()
    {
        var file = new Sample(1024).Header();
        file.Directory(8, 500, (ImageWidth, 4, 1, 32), (SubIfds, 4, 1, 64));
        file.U32(64, 200);
        file.Directory(200, 0, (ImageWidth, 4, 1, 8));
        file.Directory(500, 600, (ImageWidth, 4, 1, 16));
        file.Directory(600, 0, (ImageWidth, 4, 1, 24));

        ExifMetadata metadata = TiffExif.Read(file.Bytes);

        Assert.Equal(new[] { 1, 2, 3 }, metadata.SubImages.Select(image => image.Index));
        Assert.Equal(ExifSubImageKind.SubIfd, metadata.SubImages[0].Kind);
        Assert.Equal(ExifSubImageKind.ImageDirectory, metadata.SubImages[1].Kind);
        Assert.Equal(ExifSubImageKind.ImageDirectory, metadata.SubImages[2].Kind);
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
    public void AnImageDirectoryThatRepeatsAnotherOffsetIsListedOnce()
    {
        var file = new Sample(1024).Header();
        file.Directory(8, 0, (ImageWidth, 4, 1, 32), (SubIfds, 4, 2, 64));
        file.U32(64, 200);
        file.U32(68, 200);
        file.Directory(200, 0, (ImageWidth, 4, 1, 16));

        // The tag names one picture twice; the picture is listed once and the metadata is unaffected.
        ExifMetadata metadata = TiffExif.Read(file.Bytes);
        Assert.Equal(new[] { 1 }, metadata.SubImages.Select(image => image.Index));
        Assert.Single(metadata.SubImages[0].Entries);
    }
}
