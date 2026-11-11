using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Bmff;
using Xunit;

namespace MetadataSharp.Tests.Bmff;

public sealed class BmffMetadataTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XmpPropertyName Title = new("urn:owned:test", "title");

    // A little-endian TIFF with one IFD0 entry: Orientation = 6.
    private static readonly byte[] Orientation =
        [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void ExifItemIsReadThroughFileOffsets()
    {
        BmffMetadata metadata = BmffMetadata.Read(ExifFile());
        Assert.Equal("heic", metadata.MajorBrand);
        Assert.NotNull(metadata.Exif);
        ExifEntry entry = Assert.Single(metadata.Exif.Entries);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), entry.Key);
        var value = new byte[2];
        entry.Value.CopyRawTo(value);
        Assert.Equal(new byte[] { 6, 0 }, value);
        Assert.Null(metadata.Xmp);
    }

    [Fact]
    public void XmpItemIsReadThroughTheIdatBox()
    {
        byte[] xmp = XmpBytes("bmff");
        byte[] file = Concat(
            FileType("avif"),
            Meta(Handler(), PrimaryItem(1), ItemInformation(ItemEntry(1, "mime", "application/rdf+xml")),
                ItemLocationIdat(1, 0, (uint)xmp.Length), Box("idat", xmp)));
        BmffMetadata metadata = BmffMetadata.Read(file);
        Assert.Equal("avif", metadata.MajorBrand);
        Assert.Null(metadata.Exif);
        Assert.NotNull(metadata.Xmp);
        Assert.True(metadata.Xmp.IsEditable);
        Assert.Equal("bmff", metadata.Xmp.Properties[Title].Text);
    }

    [Fact]
    public void ABaseOffsetIsAddedToEveryExtent()
    {
        byte[] item = ExifPayload();
        byte[] Build(uint baseOffset) => Concat(
            FileType(),
            Meta(Handler(), PrimaryItem(1), ItemInformation(ItemEntry(1, "Exif")),
                ItemLocationFile(1, 0, (uint)item.Length, baseOffset)),
            Box("mdat", item));
        BmffMetadata metadata = BmffMetadata.Read(Build((uint)(Build(0).Length - item.Length)));
        Assert.NotNull(metadata.Exif);
        Assert.Single(metadata.Exif.Entries);
    }

    [Fact]
    public void WideItemLocationsAddressTheSameItem()
    {
        byte[] item = ExifPayload();
        byte[] Build(ulong offset) => Concat(
            FileType("heif"),
            Meta(Handler(), PrimaryItem(1), ItemInformation(ItemEntry(1, "Exif")),
                ItemLocationWide(1, offset, (ulong)item.Length)),
            Box("mdat", item));
        BmffMetadata metadata = BmffMetadata.Read(Build((ulong)(Build(0).Length - item.Length)));
        Assert.NotNull(metadata.Exif);
        Assert.Single(metadata.Exif.Entries);
    }

    [Fact]
    public void ExtendedAndOpenEndedSizesAreWalked()
    {
        byte[] item = ExifPayload();
        byte[] Build(uint offset) => Concat(
            FileType(),
            ExtendedBox("meta", U32(0), Concat(Handler(), PrimaryItem(1), ItemInformation(ItemEntry(1, "Exif")),
                ItemLocationFile(1, offset, (uint)item.Length))),
            OpenBox("mdat", item));
        BmffMetadata metadata = BmffMetadata.Read(Build((uint)(Build(0).Length - item.Length)));
        Assert.NotNull(metadata.Exif);
        Assert.Single(metadata.Exif.Entries);
    }

    [Theory]
    [InlineData("first")]
    [InlineData("absent")]
    public void AFileWithoutALeadingFileTypeBoxIsRefused(string variant)
    {
        byte[] file = variant == "first"
            ? Concat(Box("mdat", new byte[4]), FileType())
            : Meta(Handler());
        Assert.Equal(MetadataErrorCode.InvalidBmff, Error(file));
    }

    [Fact]
    public void ANonPictureHandlerIsRefused()
    {
        byte[] file = Concat(FileType(), Meta(Handler("mdir"), PrimaryItem(1), ItemInformation(ItemEntry(1, "Exif"))));
        Assert.Equal(MetadataErrorCode.InvalidBmff, Error(file));
    }

    [Fact]
    public void AnExtentPastTheEndOfTheFileIsRefused()
    {
        byte[] item = ExifPayload();
        byte[] file = Concat(FileType(), Meta(Handler(), PrimaryItem(1), ItemInformation(ItemEntry(1, "Exif")),
            ItemLocationFile(1, 4_096, (uint)item.Length)), Box("mdat", item));
        Assert.Equal(MetadataErrorCode.InvalidBmff, Error(file));
    }

    [Fact]
    public void ABoxSizePastItsParentIsRefused()
    {
        // The claimed bytes exist in the file; the meta box does not own them.
        byte[] file = Concat(FileType(), Meta(Handler(), Oversized("free", 0x100, new byte[8])), Box("mdat", new byte[256]));
        Assert.Equal(MetadataErrorCode.InvalidBmff, Error(file));
    }

    [Theory]
    [InlineData("depth")]
    [InlineData("count")]
    [InlineData("input")]
    public void EveryBudgetIsEnforcedBeforeTraversal(string budget)
    {
        var limits = budget switch
        {
            "depth" => new MetadataLimits { MaxDirectoryDepth = 1 },
            "count" => new MetadataLimits { MaxSegments = 3 },
            _ => new MetadataLimits { MaxInputBytes = 64 }
        };
        MetadataException failure = Assert.Throws<MetadataException>(() => BmffMetadata.Read(ExifFile(), limits));
        Assert.Equal(MetadataErrorCode.LimitExceeded, failure.Code);
    }

    [Fact]
    public void AnExifTiffOffsetPastItsItemIsRefused()
    {
        byte[] item = Concat(U32(0x400), Ascii("Exif"), new byte[] { 0, 0 }, Orientation);
        byte[] Build(uint offset) => Concat(FileType(), Meta(Handler(), PrimaryItem(1), ItemInformation(ItemEntry(1, "Exif")),
            ItemLocationFile(1, offset, (uint)item.Length)), Box("mdat", item));
        Assert.Equal(MetadataErrorCode.InvalidBmff, Error(Build((uint)(Build(0).Length - item.Length))));
    }

    [Fact]
    public void ItemOffsetsAsAConstructionMethodAreRefused()
    {
        byte[] item = ExifPayload();
        byte[] file = Concat(FileType(), Meta(Handler(), PrimaryItem(1), ItemInformation(ItemEntry(1, "Exif")),
            Box("iloc", U32(0x01000000), new byte[] { 0x44, 0x40 }, U16(1), U16(1), U16(2), U16(0), U32(0),
                U16(1), U32(0), U32((uint)item.Length))), Box("mdat", item));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(file));
    }

    [Fact]
    public void ExtentsAreConcatenatedInTheirDeclaredOrder()
    {
        byte[] item = ExifPayload();
        byte[] first = item[..10];
        byte[] second = item[10..];
        // The two fragments sit in the file in the opposite order to the one the item declares.
        byte[] Build(uint firstOffset, uint secondOffset) => Concat(
            FileType(),
            Meta(Handler(), PrimaryItem(1), ItemInformation(ItemEntry(1, "Exif")),
                Box("iloc", U32(0), new byte[] { 0x44, 0x40 }, U16(1), U16(1), U16(0), U32(0), U16(2),
                    U32(firstOffset), U32((uint)first.Length), U32(secondOffset), U32((uint)second.Length))),
            Box("mdat", second, first));
        byte[] draft = Build(0, 0);
        int firstAt = draft.Length - first.Length;
        int secondAt = firstAt - second.Length;
        BmffMetadata metadata = BmffMetadata.Read(Build((uint)firstAt, (uint)secondAt));
        Assert.NotNull(metadata.Exif);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), Assert.Single(metadata.Exif.Entries).Key);
    }

    [Fact]
    public void AFileWithoutAMetaBoxHasABrandAndNoMetadata()
    {
        BmffMetadata metadata = BmffMetadata.Read(Concat(FileType("qt  "), Box("mdat", new byte[] { 1, 2, 3 })));
        Assert.Equal("qt  ", metadata.MajorBrand);
        Assert.Null(metadata.Exif);
        Assert.Null(metadata.Xmp);
    }

    [Fact]
    public void EveryTruncatedPrefixIsRefused()
    {
        byte[] file = ExifFile();
        int fileType = 24; // ftyp: 8 header bytes, brand, minor version and two compatible brands
        for (int length = 0; length < fileType; length++)
            Assert.Equal(MetadataErrorCode.InvalidBmff, Error(file[..length]));
        for (int length = fileType + 1; length < file.Length; length++)
            Assert.Equal(MetadataErrorCode.InvalidBmff, Error(file[..length]));
        // A prefix that ends exactly after the file type box is a whole file with no metadata.
        BmffMetadata bare = BmffMetadata.Read(file[..fileType]);
        Assert.Equal("heic", bare.MajorBrand);
        Assert.Null(bare.Exif);
    }

    [Fact]
    public void BoundedMutationSmokeTestNeverLeaksAnotherException()
    {
        var random = new Random(4096);
        byte[] source = ExifFile();
        for (int i = 0; i < 256; i++)
        {
            byte[] data = (byte[])source.Clone();
            data[random.Next(data.Length)] = (byte)random.Next(256);
            try { BmffMetadata.Read(data); }
            catch (MetadataException) { }
        }
    }

    // Authored synthetic files, not serialized by production code: ftyp, meta, and the item bytes
    // in mdat. The item offset is patched in a second pass because the iloc field that holds it has
    // a fixed width, so the layout does not change between the two builds.
    private static byte[] ExifFile()
    {
        byte[] item = ExifPayload();
        byte[] Build(uint offset) => Concat(
            FileType(),
            Meta(Handler(), PrimaryItem(1), ItemInformation(ItemEntry(1, "Exif")),
                ItemLocationFile(1, offset, (uint)item.Length)),
            Box("mdat", item));
        return Build((uint)(Build(0).Length - item.Length));
    }

    private static byte[] ExifPayload() => Concat(U32(6), Ascii("Exif"), new byte[] { 0, 0 }, Orientation);

    private static byte[] XmpBytes(string text) => Encoding.UTF8.GetBytes(
        $"<rdf:RDF xmlns:rdf='{Rdf}' xmlns:p='urn:owned:test'><rdf:Description rdf:about=''>" +
        $"<p:title>{text}</p:title></rdf:Description></rdf:RDF>");

    private static byte[] FileType(string brand = "heic") =>
        Box("ftyp", Ascii(brand), U32(0), Ascii("mif1"), Ascii("heic"));

    private static byte[] Meta(params byte[][] children) => Box("meta", U32(0), Concat(children));

    private static byte[] Handler(string type = "pict") =>
        Box("hdlr", U32(0), U32(0), Ascii(type), new byte[13]);

    private static byte[] PrimaryItem(uint id) => Box("pitm", U32(0), U16(id));

    private static byte[] ItemEntry(uint id, string type, string? contentType = null)
    {
        // A version 2 entry: item_ID is a 16-bit field and the item type is present.
        var parts = new List<byte[]> { U32(0x02000000), U16(id), U16(0), Ascii(type), new byte[] { 0 } };
        if (contentType is not null) parts.Add(Concat(Ascii(contentType), new byte[] { 0 }));
        return Box("infe", parts.ToArray());
    }

    private static byte[] ItemInformation(params byte[][] entries) =>
        Box("iinf", U32(0), U16((uint)entries.Length), Concat(entries));

    /// <summary>iloc version 0, construction method 0: 32-bit offsets into the file, no base offset.</summary>
    private static byte[] ItemLocationFile(uint id, uint offset, uint length, uint baseOffset = 0) =>
        Box("iloc", U32(0), new byte[] { 0x44, 0x40 }, U16(1), U16(id), U16(0), U32(baseOffset), U16(1),
            U32(offset), U32(length));

    /// <summary>iloc version 1, construction method 1: 32-bit offsets into the idat box.</summary>
    private static byte[] ItemLocationIdat(uint id, uint offset, uint length) =>
        Box("iloc", U32(0x01000000), new byte[] { 0x44, 0x40 }, U16(1), U16(id), U16(1), U16(0), U32(0), U16(1),
            U32(offset), U32(length));

    /// <summary>iloc version 2: 64-bit offsets, 64-bit lengths, no base offset.</summary>
    private static byte[] ItemLocationWide(uint id, ulong offset, ulong length) =>
        Box("iloc", U32(0x02000000), new byte[] { 0x88, 0x00 }, U32(1), U32(id), U16(0), U16(0), U16(1),
            U64(offset), U64(length));

    private static byte[] Box(string type, params byte[][] payload)
    {
        byte[] body = Concat(payload);
        return Concat(U32((uint)(8 + body.Length)), Ascii(type), body);
    }

    private static byte[] ExtendedBox(string type, params byte[][] payload)
    {
        byte[] body = Concat(payload);
        return Concat(U32(1), Ascii(type), U64((ulong)(16 + body.Length)), body);
    }

    private static byte[] OpenBox(string type, params byte[][] payload) =>
        Concat(U32(0), Ascii(type), Concat(payload));

    private static byte[] Oversized(string type, uint size, byte[] payload) =>
        Concat(U32(size), Ascii(type), payload);

    private static byte[] Concat(params byte[][] parts)
    {
        int length = 0;
        foreach (byte[] part in parts) length += part.Length;
        var bytes = new byte[length];
        int at = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(bytes, at);
            at += part.Length;
        }
        return bytes;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] U16(uint value) => [(byte)(value >> 8), (byte)value];

    private static byte[] U32(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] U64(ulong value) => [.. U32((uint)(value >> 32)), .. U32((uint)value)];

    private static MetadataErrorCode Error(byte[] data) =>
        Assert.Throws<MetadataException>(() => BmffMetadata.Read(data)).Code;
}
