using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Cr3;
using Xunit;
using static MetadataSharp.Tests.Cr3.Cr3Fixture;

namespace MetadataSharp.Tests.Cr3;

/// <summary>The two JPEG pictures of a CR3 file: THMB in the Canon box, PRVW in a top-level box of its own.</summary>
public sealed class Cr3PreviewTests
{
    private const string Jpeg = "image/jpeg";

    /// <summary>A THMB box: version, flags, width, height, the stated length, two fields, the bytes.</summary>
    private static byte[] Thumbnail(byte version, uint length, byte[] bytes) =>
        Box("THMB", [version, 0, 0, 0], Be16(160), Be16(120), Be32(length), Be16(1), Be16(0), bytes);

    /// <summary>A PRVW box in the box that holds it: eight bytes, then four bytes, a field, width, height, a field, the stated length.</summary>
    private static byte[] Preview(uint length, byte[] bytes) =>
        Uuid(PreviewBox, new byte[4], Be32(1), Box("PRVW", new byte[4], Be16(1), Be16(1620), Be16(1080), Be16(1), Be32(length), bytes));

    private static byte[] WithThumbnail(byte[] box) => Join(FileType(), Box("moov", Uuid(CanonBox, box)), Box("mdat", new byte[32]));

    private static byte[] WithPreview(byte[] box) => Join(FileType(), Box("moov", Box("mvhd", new byte[100])), box, Box("mdat", new byte[32]));

    [Fact]
    public void TheThumbnailAndThePreviewAreListedSmallestFirstAndExtracted()
    {
        var plan = new Layout();
        Cr3Fixture file = Create(plan);

        IReadOnlyList<PreviewImage> found = Previews.List(file.Bytes);

        Assert.Equal(2, found.Count);
        Assert.Equal(new PreviewImage(Jpeg, 160, 120, file.At["THMB"], plan.Thumbnail!.Length, "Canon.THMB"), found[0]);
        Assert.Equal(new PreviewImage(Jpeg, 1620, 1080, file.At["PRVW"], plan.Preview!.Length, "Canon.PRVW"), found[1]);
        Assert.Equal(plan.Thumbnail, Previews.Extract(file.Bytes, found[0]));
        Assert.Equal(plan.Preview, Previews.Extract(file.Bytes, found[1]));
    }

    [Fact]
    public void EitherPictureMayBeAbsent()
    {
        PreviewImage thumbnail = Assert.Single(Previews.List(Create(new Layout { Preview = null }).Bytes));
        PreviewImage preview = Assert.Single(Previews.List(Create(new Layout { Thumbnail = null }).Bytes));

        Assert.Equal("Canon.THMB", thumbnail.Source);
        Assert.Equal("Canon.PRVW", preview.Source);
        Assert.Empty(Previews.List(Create(new Layout { Thumbnail = null, Preview = null }).Bytes));
    }

    [Fact]
    public void ThePicturesNeedNoDirectoriesAndTheDirectoriesNoPictures()
    {
        var plan = new Layout { Ifd0 = null, Exif = null, MakerNote = null, Gps = null, Xmp = null };

        Assert.Equal(2, Previews.List(Create(plan).Bytes).Count);
        Assert.Equal(2, Previews.List(Create(new Layout { WideMovie = true, OpenData = true }).Bytes).Count);
        Assert.NotNull(Cr3Metadata.Read(Create(new Layout { Thumbnail = null, Preview = null }).Bytes).Exif);
    }

    [Fact]
    public void TheDimensionsAreThoseOfTheJpegFrameNotOfTheBox()
    {
        byte[] picture = Picture(8, 6);

        PreviewImage thumbnail = Assert.Single(Previews.List(WithThumbnail(Thumbnail(0, (uint)picture.Length, picture))));
        PreviewImage preview = Assert.Single(Previews.List(WithPreview(Preview((uint)picture.Length, picture))));

        Assert.Equal(8, thumbnail.Width);
        Assert.Equal(6, thumbnail.Height);
        Assert.Equal(8, preview.Width);
        Assert.Equal(6, preview.Height);
    }

    [Fact]
    public void BytesOfTheBoxAfterTheStatedLengthAreNotPartOfThePicture()
    {
        byte[] picture = Picture(8, 6, 10);
        byte[] file = WithThumbnail(Thumbnail(0, (uint)picture.Length, Join(picture, new byte[7])));

        PreviewImage found = Assert.Single(Previews.List(file));

        Assert.Equal(picture.Length, found.Length);
        Assert.Equal(picture, Previews.Extract(file, found));
    }

    [Fact]
    public void APictureThatLeavesItsBoxIsNotListed()
    {
        byte[] picture = Picture(8, 6, 10);
        uint length = (uint)picture.Length;

        Assert.Empty(Previews.List(WithThumbnail(Thumbnail(0, length + 1, picture))));
        Assert.Empty(Previews.List(WithPreview(Preview(length + 1, picture))));
        Assert.Empty(Previews.List(WithThumbnail(Thumbnail(0, uint.MaxValue, picture))));
        Assert.Empty(Previews.List(WithPreview(Preview(0x8000_0000, picture))));
        Assert.Empty(Previews.List(WithThumbnail(Thumbnail(0, 0, picture))));
        Assert.Empty(Previews.List(WithPreview(Preview(0, picture))));
        // A box too short for its own fields.
        Assert.Empty(Previews.List(WithThumbnail(Box("THMB", new byte[12]))));
        Assert.Empty(Previews.List(WithPreview(Uuid(PreviewBox, new byte[8], Box("PRVW", new byte[12])))));
        Assert.Empty(Previews.List(WithPreview(Uuid(PreviewBox, new byte[4]))));
    }

    [Fact]
    public void BytesThatAreNotAJpegAreNotListed()
    {
        byte[] other = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

        Assert.Empty(Previews.List(WithThumbnail(Thumbnail(0, (uint)other.Length, other))));
        Assert.Empty(Previews.List(WithPreview(Preview((uint)other.Length, other))));
    }

    [Fact]
    public void AThumbnailBoxOfAnotherVersionIsNotListed()
    {
        // Version 1 of the box holds a picture of another coding; a JPEG signature in it proves nothing.
        byte[] picture = Picture(8, 6);

        Assert.Empty(Previews.List(WithThumbnail(Thumbnail(1, (uint)picture.Length, picture))));
        Assert.Single(Previews.List(WithThumbnail(Thumbnail(0, (uint)picture.Length, picture))));
    }

    [Fact]
    public void ThePicturesAreFoundOnlyWhereACameraPutsThem()
    {
        byte[] picture = Picture(8, 6);
        byte[] thumbnail = Thumbnail(0, (uint)picture.Length, picture);
        byte[] preview = Box("PRVW", new byte[4], Be16(1), Be16(1620), Be16(1080), Be16(1), Be32((uint)picture.Length), picture);

        // THMB at the top level or straight in the movie box, PRVW outside its box or in the Canon box.
        Assert.Empty(Previews.List(Join(FileType(), thumbnail)));
        Assert.Empty(Previews.List(Join(FileType(), Box("moov", thumbnail))));
        Assert.Empty(Previews.List(Join(FileType(), preview)));
        Assert.Empty(Previews.List(Join(FileType(), Box("moov", Uuid(CanonBox, preview)))));
        Assert.Empty(Previews.List(Join(FileType(), Uuid(XmpBox, new byte[8], preview))));
    }

    [Fact]
    public void ATruncatedFileListsOnlyWholePicturesAndNeverThrows()
    {
        byte[] file = Create().Bytes;
        int whole = Previews.List(file).Count;

        for (int length = 0; length < file.Length; length++)
        {
            IReadOnlyList<PreviewImage> found = Previews.List(file.AsSpan(0, length));
            Assert.True(found.Count <= whole);
            foreach (PreviewImage preview in found)
                Assert.True(preview.Offset + preview.Length <= length);
        }
    }

    [Fact]
    public void TheInputBudgetApplies()
    {
        byte[] file = Create().Bytes;
        var limits = new MetadataLimits { MaxInputBytes = file.Length - 1 };

        MetadataException refusal = Assert.Throws<MetadataException>(() => Previews.List(file, limits));

        Assert.Equal(MetadataErrorCode.LimitExceeded, refusal.Code);
        Assert.Equal(2, Previews.List(file, new MetadataLimits { MaxInputBytes = file.Length }).Count);
    }
}
