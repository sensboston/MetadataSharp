using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Operations;
using MetadataSharp.Formats.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

/// <summary>
/// The thumbnail directory of a TIFF-based file: created by appending it to the end of the file,
/// replaced and removed without moving a byte of the image data, and refused where the directory
/// after the primary one is a picture of the file rather than a thumbnail of an EXIF block.
/// </summary>
public sealed class TiffThumbnailWriteTests
{
    private static readonly byte[] Picture = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0xFF, 0xD9];

    private static byte[] Save(byte[] file, Action<ExifEdit> change, TiffReadOptions? options = null)
    {
        var edit = new ExifEdit();
        change(edit);
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file), options);
        using var output = new MemoryStream();
        document.SaveExif(output, edit);
        return output.ToArray();
    }

    private static MetadataErrorCode Code(byte[] file, Action<ExifEdit> change) =>
        Assert.Throws<MetadataException>(() => Save(file, change)).Code;

    private static ExifMetadata Read(byte[] file) => ExifTiffReader.InspectFile(file);

    private static TiffBasedDirectory? Directory(byte[] file) =>
        TiffBasedFileMap.Read(file, new MetadataLimits(), 1 << 20).Find(TiffBasedRole.Thumbnail);

    private static int? Number(ExifMetadata metadata, ushort tag)
    {
        if (!metadata.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Thumbnail, tag), out ExifEntry? entry)) return null;
        byte[] raw = entry.Value.RawBytes.ToArray();
        return entry.Value.FieldType switch
        {
            TiffFieldType.Short => metadata.ByteOrder == Endianness.LittleEndian
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw)
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(raw),
            TiffFieldType.Long => (int)(metadata.ByteOrder == Endianness.LittleEndian
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(raw)
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(raw)),
            _ => null
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AThumbnailIsAppendedAndReadBackByTheReader(bool big)
    {
        TiffBasedFixture source = TiffBasedFixture.Tiff(big);
        byte[] file = source.Bytes;
        byte[] saved = Save(file, edit => edit.SetThumbnail(Picture));

        // Nothing of the source moved: the output begins with it, but for the successor link.
        Assert.True(saved.Length > file.Length);
        Assert.Equal(Picture, MetadataOperations.ExtractThumbnail(saved));
        Assert.Equal(file.AsSpan(300, 64).ToArray(), saved.AsSpan(300, 64).ToArray());

        TiffReadResult read = ExifTiffReader.ReadFile(saved);
        Assert.Equal(Picture, read.Thumbnail);
        Assert.Equal("TestCompany", read.Metadata.Make);
        Assert.Contains(ExifDirectoryKind.Thumbnail, read.Metadata.Entries.Select(entry => entry.Key.Directory));
        Assert.Contains(ExifDirectoryKind.Ifd0, read.Metadata.Entries.Select(entry => entry.Key.Directory));

        // The three tags Exif requires of a JPEG thumbnail, and Compression 6.
        Assert.Equal(6, Number(read.Metadata, 0x0103));
        Assert.Equal(72, Number(read.Metadata, 0x011A));
        Assert.Equal(72, Number(read.Metadata, 0x011B));
        Assert.Equal(2, Number(read.Metadata, 0x0128));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AThumbnailIsReplacedKeepingTheOtherTagsOfItsDirectory(bool big)
    {
        byte[] first = Save(TiffBasedFixture.Tiff(big).Bytes, edit => edit.SetThumbnail(Picture));
        byte[] withTag = Save(first, edit => edit.Set(new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x0112), (ushort)3));
        Assert.Equal(3, Number(Read(withTag), 0x0112));

        byte[] other = [0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x04, 0x01, 0x02, 0xFF, 0xD9];
        byte[] replaced = Save(withTag, edit => edit.SetThumbnail(other));
        Assert.Equal(other, MetadataOperations.ExtractThumbnail(replaced));
        Assert.Equal(3, Number(Read(replaced), 0x0112));
        Assert.Equal(6, Number(Read(replaced), 0x0103));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AThumbnailIsRemovedWithItsDirectoryAndTheOtherBytesStay(bool big)
    {
        byte[] withThumbnail = Save(TiffBasedFixture.Tiff(big).Bytes, edit => edit.SetThumbnail(Picture));
        byte[] removed = Save(withThumbnail, edit => edit.RemoveThumbnail());

        Assert.Null(MetadataOperations.ExtractThumbnail(removed));
        Assert.Null(Directory(removed));
        Assert.Equal("TestCompany", Read(removed).Make);
        Assert.Equal(withThumbnail.Length, removed.Length);
        // The link of the primary directory, the old directory and the old stream are what differs.
        int differing = 0;
        for (int at = 0; at < removed.Length; at++)
            if (removed[at] != withThumbnail[at]) differing++;
        Assert.True(differing >= 4, $"{differing} bytes differ");

        // Removing it again is no change at all.
        Assert.Equal(removed, Save(removed, edit => edit.RemoveThumbnail()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADirectoryThatIsAPictureOfTheFileIsNeitherReplacedNorRemoved(bool big)
    {
        TiffBasedFixture pages = TiffBasedFixture.Pages(big);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(pages.Bytes, edit => edit.SetThumbnail(Picture)));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(pages.Bytes, edit => edit.RemoveThumbnail()));
        Assert.Equal(pages.Bytes, Save(pages.Bytes, _ => { }));
    }

    [Fact]
    public void TheDeleteOperationRemovesTheThumbnailOfATiffBasedFile()
    {
        byte[] withThumbnail = Save(TiffBasedFixture.Tiff(false).Bytes, edit => edit.SetThumbnail(Picture));
        Assert.Equal(Picture, MetadataOperations.ExtractThumbnail(withThumbnail));
        byte[] deleted = MetadataOperations.Delete(withThumbnail, MetadataFamilies.Thumbnail);
        Assert.Null(MetadataOperations.ExtractThumbnail(deleted));
        Assert.Equal("TestCompany", Read(deleted).Make);
        // A file without a thumbnail has nothing to remove.
        Assert.Equal(deleted, MetadataOperations.Delete(deleted, MetadataFamilies.Thumbnail));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStreamThatIsNoJpegIsRefusedAndOneOverItsBudgetToo(bool big)
    {
        byte[] file = TiffBasedFixture.Tiff(big).Bytes;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(file, edit => edit.SetThumbnail([0x00, 0x01, 0x02, 0x03])));
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Code(file, edit => edit.SetThumbnail(Picture), new TiffReadOptions { Limits = new() { MaxThumbnailBytes = 4 } }));
    }
}
