using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Containers;

/// <summary>
/// The metadata of the image a container embeds, read apart from the container's own: a file's
/// preview is a file, and what it carries is its own.
/// </summary>
public sealed class EmbeddedImageMetadataTests
{
    private static readonly ExifTagKey Orientation = new(ExifDirectoryKind.Ifd0, 0x0112);

    /// <summary>A stream that starts and ends as a JPEG does, with the shortest valid EXIF block.</summary>
    private static byte[] Picture(byte filler, int length = 24)
    {
        byte[] bytes = new byte[length];
        Array.Fill(bytes, filler);
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xFE;
        bytes[4] = (byte)((length - 6) >> 8); bytes[5] = (byte)(length - 6);
        bytes[^2] = 0xFF; bytes[^1] = 0xD9;
        return bytes;
    }

    private static byte[] JpegWithThumbnail(byte[] thumbnail)
    {
        var edit = new ExifEdit();
        edit.SetThumbnail(thumbnail);
        using JpegMetadataDocument document = JpegMetadataDocument.Open(
            new MemoryStream(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif()))));
        var output = new MemoryStream();
        document.Save(output, edit);
        return output.ToArray();
    }

    [Fact]
    public void AFileWithoutAPreviewHasNoEmbeddedMetadata()
    {
        Assert.Null(EmbeddedImageMetadata.Read(JpegFixture.Build()));
        Assert.Null(EmbeddedImageMetadata.Read(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif()))));
    }

    [Fact]
    public void APreviewWithoutMetadataIsNotASnapshot()
    {
        // The thumbnail is a JPEG and nothing else: there is metadata to read from the container, but
        // none from the picture it embeds.
        byte[] file = JpegWithThumbnail(Picture(0x11));

        Assert.NotNull(ImageMetadata.Read(file).Exif);
        Assert.Null(EmbeddedImageMetadata.Read(file));
    }

    [Fact]
    public void APreviewThatCarriesMetadataIsASnapshotOfItsOwn()
    {
        byte[] file = JpegWithThumbnail(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif())));

        EmbeddedImageMetadata? embedded = EmbeddedImageMetadata.Read(file);

        Assert.NotNull(embedded);
        Assert.Equal("image/jpeg", embedded!.Source.MimeType);
        // The rows are the embedded picture's: its own orientation, and not the container's entries.
        Assert.NotNull(embedded.Exif);
        Assert.Contains(embedded.Exif!.Entries, entry => entry.Key == Orientation);
        // The container's EXIF is a different block, and reading the snapshot did not change it.
        Assert.NotNull(ImageMetadata.Read(file).Exif);
    }

    [Fact]
    public void AThumbnailCanBeNamedAndAnUnknownOneCannot()
    {
        byte[] file = JpegWithThumbnail(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif())));
        IReadOnlyList<PreviewImage> previews = Previews.List(file);

        Assert.NotEmpty(previews);
        Assert.NotNull(EmbeddedImageMetadata.Read(file, previews[0]));

        var other = new PreviewImage("image/jpeg", null, null, 0, 4, "Exif.Thumbnail.JPEGInterchangeFormat");
        Assert.Null(EmbeddedImageMetadata.Read(file, other));
    }
}
