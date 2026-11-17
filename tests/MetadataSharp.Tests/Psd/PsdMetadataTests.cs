using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Psd;
using MetadataSharp.Formats.Operations;
using Xunit;

namespace MetadataSharp.Tests.Psd;

public sealed class PsdMetadataTests
{
    private static byte[] Fixture(string name)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MetadataSharp.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "tests", "fixtures", "b8", name));
    }

    private static MetadataErrorCode Error(byte[] bytes) => Assert.Throws<MetadataException>(() => PsdMetadata.Read(bytes)).Code;

    private static byte[] ResourceRecord(byte[] file, ushort id)
    {
        int at = 30 + checked((int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(26, 4)));
        int end = at + 4 + checked((int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at, 4)));
        at += 4;
        while (at < end)
        {
            int start = at;
            ushort current = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at + 4, 2));
            at += 6;
            int name = file[at]; at += (1 + name + 1) & ~1;
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at, 4)));
            at += 4 + length + (length & 1);
            if (current == id) return file[start..at];
        }
        throw new InvalidOperationException("PSD resource not found.");
    }

    [Fact]
    public void FlatAndMetadataPresentOwnedFixturesAreRead()
    {
        byte[] flat = Fixture("owned-flat-rgb.psd");
        PsdMetadata empty = PsdMetadata.Read(flat);
        Assert.Equal(2u, empty.Width); Assert.Equal(2u, empty.Height);
        Assert.True(empty.IsRestrictedProfile);
        Assert.Null(empty.ReadExif()); Assert.Null(empty.ReadIptc()); Assert.Null(empty.ReadXmp());
        Assert.Null(empty.GetResource(1058)); Assert.Null(empty.GetResource(1060)); Assert.Null(empty.GetResource(1028));
        byte[] rich = Fixture("owned-metadata-rgb.psd");
        PsdMetadata metadata = PsdMetadata.Read(rich);
        Assert.Equal(2u, metadata.Width); Assert.Equal(2u, metadata.Height);
        Assert.True(metadata.IsRestrictedProfile);
        Assert.Contains("B8Artist", Encoding.ASCII.GetString(metadata.GetResource(1058)!));
        Assert.Contains("B8Tool", Encoding.UTF8.GetString(metadata.GetResource(1060)!));
        Assert.Contains("B8Keyword", Encoding.ASCII.GetString(metadata.GetResource(1028)!));
        Assert.Null(metadata.GetResource(1039));
        byte[] copy = metadata.GetResource(1058)!;
        copy[0] ^= 0xff; rich[0] ^= 0xff;
        Assert.Equal((byte)'I', metadata.GetResource(1058)![0]);
    }

    [Fact]
    public void ResourceWriterRoundTripsIccAndPreservesExistingMetadataAndImageBytes()
    {
        byte[] source = Fixture("owned-metadata-rgb.psd");
        PsdMetadata before = PsdMetadata.Read(source);
        byte[] profile = MetadataSharp.Tests.Containers.IccProfileTests.Profile(560, 31);
        byte[] output = PsdMetadata.Write(source, before.ReadExif(), before.ReadIptc(), before.ReadXmp(), profile);
        PsdMetadata after = PsdMetadata.Read(output);
        Assert.Equal(profile, IccProfiles.Read(output));
        byte[] extracted = MetadataSharp.Formats.Operations.MetadataOperations.ExtractExv(output, MetadataFamilies.IccProfile);
        Assert.Equal(profile, IccProfiles.Read(extracted));
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height, after.Height);
        Assert.Equal(before.ReadExif()!.Entries.Select(entry => entry.Key), after.ReadExif()!.Entries.Select(entry => entry.Key));
        Assert.Equal(before.ReadIptc()!.Datasets.Select(dataset => dataset.Key), after.ReadIptc()!.Datasets.Select(dataset => dataset.Key));
        Assert.Equal(before.ReadXmp()!.Entries.Select(entry => entry.Key), after.ReadXmp()!.Entries.Select(entry => entry.Key));
        Assert.Equal(before.GetResource(1058), after.GetResource(1058));
        Assert.Equal(before.GetResource(1060), after.GetResource(1060));
        Assert.Equal(before.GetResource(1028), after.GetResource(1028));
        Assert.Equal(ResourceRecord(source, 1058), ResourceRecord(output, 1058));
        Assert.Equal(ResourceRecord(source, 1060), ResourceRecord(output, 1060));
        Assert.Equal(ResourceRecord(source, 1028), ResourceRecord(output, 1028));

        int resourceLengthAt = 30 + (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(26, 4));
        int sourceEnd = resourceLengthAt + 4 + (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(resourceLengthAt, 4));
        int outputEnd = resourceLengthAt + 4 + (int)BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(resourceLengthAt, 4));
        Assert.Equal(source[..resourceLengthAt], output[..resourceLengthAt]);
        Assert.Equal(source[sourceEnd..], output[outputEnd..]);

        byte[] withoutIcc = PsdMetadata.Write(output, after.ReadExif(), after.ReadIptc(), after.ReadXmp());
        Assert.Null(IccProfiles.Read(withoutIcc));
        byte[] malformed = (byte[])profile.Clone(); malformed[36] = (byte)'x';
        Assert.Equal(MetadataErrorCode.InvalidIccProfile,
            Assert.Throws<MetadataException>(() => PsdMetadata.Write(source, iccProfile: malformed)).Code);

        byte[] jpeg = MetadataSharp.Tests.Jpeg.JpegFixture.Build();
        byte[] withThumbnail = PsdMetadata.Write(source, before.ReadExif(), before.ReadIptc(), before.ReadXmp(), profile, jpeg);
        byte[] resource = PsdMetadata.Read(withThumbnail).GetResource(0x040C)!;
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(resource));
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32BigEndian(resource.AsSpan(4)));
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32BigEndian(resource.AsSpan(8)));
        Assert.Equal(jpeg, resource[28..]);
        byte[] withoutThumbnail = PsdMetadata.Write(withThumbnail, after.ReadExif(), after.ReadIptc(), after.ReadXmp(), profile);
        Assert.Null(PsdMetadata.Read(withoutThumbnail).GetResource(0x040C));
        Assert.Throws<MetadataException>(() => PsdMetadata.Write(source, jpegThumbnail: [1, 2]));
    }

    [Fact]
    public void EveryTruncationIsRejected()
    {
        byte[] bytes = Fixture("owned-metadata-rgb.psd");
        for (int i = 0; i < bytes.Length; i++)
            Assert.Throws<MetadataException>(() => PsdMetadata.Read(bytes.AsSpan(0, i)));
    }

    [Fact]
    public void HeaderAndPixelErrorsFailClosed()
    {
        byte[] original = Fixture("owned-flat-rgb.psd");
        byte[] x = (byte[])original.Clone(); x[0] = 0;
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(x));
        x = (byte[])original.Clone(); x[5] = 2;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
        x = (byte[])original.Clone(); x[6] = 1;
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(x));
        x = (byte[])original.Clone(); x[17] = 0;
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(x));
        // Four channels, or run-length encoding, declared over the bytes of three raw channels.
        x = (byte[])original.Clone(); x[13] = 4;
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(x));
        x = (byte[])original.Clone(); x[39] = 1;
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(x));
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error([.. original, 0]));
    }

    [Theory]
    [InlineData(12, 0, 13, 0)]   // no channels
    [InlineData(12, 0, 13, 57)]  // more channels than the format has
    [InlineData(22, 0, 23, 7)]   // a depth the format does not define
    [InlineData(24, 0, 25, 5)]   // a color mode the format does not define
    [InlineData(38, 0, 39, 4)]   // a compression method the format does not define
    public void HeaderValuesTheFormatDoesNotDefineAreRejected(int high, byte highValue, int low, byte lowValue)
    {
        byte[] x = Fixture("owned-flat-rgb.psd");
        x[high] = highValue; x[low] = lowValue;
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(x));
    }

    [Fact]
    public void MetadataResourcesAreReadThroughTheirOwnReaders()
    {
        PsdMetadata metadata = PsdMetadata.Read(Fixture("owned-metadata-rgb.psd"));
        ExifMetadata exif = Assert.IsType<ExifMetadata>(metadata.ReadExif());
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B), Assert.Single(exif.Entries).Key);
        Assert.True(exif.IsEditable);
        Assert.Equal(new MetadataSharp.Core.Iptc.IptcDatasetKey(2, 25), Assert.Single(metadata.ReadIptc()!.Datasets).Key);
        Assert.NotEmpty(metadata.ReadXmp()!.Entries);
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.ReadExif(new MetadataLimits { MaxDirectories = 0 }));
    }

    [Fact]
    public void AnExifResourceWithoutAByteOrderMarkIsRefusedAsExif()
    {
        byte[] x = Fixture("owned-metadata-rgb.psd");
        x[46] = 0; // the first byte of resource 0x0422, the byte order mark
        PsdMetadata metadata = PsdMetadata.Read(x);
        Assert.Equal(MetadataErrorCode.InvalidExif, Assert.Throws<MetadataException>(() => metadata.ReadExif()).Code);
        Assert.NotNull(metadata.ReadIptc());
        Assert.NotNull(metadata.ReadXmp());
    }

    [Fact]
    public void AnExifResourceWithAFieldOfUnknownTypeIsReadOneBytePerElement()
    {
        // Orientation followed by an entry of field type 14, which TIFF 6.0 does not define.
        byte[] tiff = [0x49, 0x49, 42, 0, 8, 0, 0, 0, 2, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0,
            0x28, 1, 14, 0, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0];
        byte[] flat = Fixture("owned-flat-rgb.psd");
        // The flat fixture with one resource, 0x0422, in its empty resource section.
        byte[] x = [.. flat[..30], 0, 0, 0, (byte)(12 + tiff.Length), .. "8BIM"u8, 0x04, 0x22, 0, 0,
            0, 0, 0, (byte)tiff.Length, .. tiff, .. flat[34..]];
        ExifMetadata exif = Assert.IsType<ExifMetadata>(PsdMetadata.Read(x).ReadExif());
        Assert.False(exif.IsEditable);
        Assert.Equal(2, exif.Entries.Count);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), exif.Entries[0].Key);
        Assert.Equal(TiffFieldType.Unknown, exif.Entries[1].Value.FieldType);
        Assert.Equal(14, exif.Entries[1].Value.FieldTypeCode);
        Assert.Equal(1, exif.Entries[1].Value.ByteLength);
    }

    [Theory]
    [InlineData("AgHg")]
    [InlineData("DCSR")]
    [InlineData("PHUT")]
    public void ResourcesFramedWithAnotherAdobeSignatureAreRead(string signature)
    {
        byte[] x = Fixture("owned-metadata-rgb.psd");
        Encoding.ASCII.GetBytes(signature).CopyTo(x, 34);
        PsdMetadata metadata = PsdMetadata.Read(x);
        Assert.False(metadata.IsRestrictedProfile);
        Assert.NotNull(metadata.ReadExif());
        Assert.NotNull(metadata.ReadIptc());
    }

    [Fact]
    public void GrayscaleSixteenBitAndRunLengthEncodedFilesAreReadOutsideTheRestrictedProfile()
    {
        byte[] flat = Fixture("owned-flat-rgb.psd");
        // One 16-bit grayscale channel of 2 x 2 pixels: eight bytes of raw image data.
        byte[] gray = [.. flat[..40], .. new byte[8]];
        gray[13] = 1; gray[23] = 16; gray[25] = 1;
        PsdMetadata metadata = PsdMetadata.Read(gray);
        Assert.False(metadata.IsRestrictedProfile);
        Assert.Equal(2u, metadata.Width);
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(gray[..^1]));

        // Six scan lines (three channels of two rows), each a two-byte run of two equal pixels.
        byte[] table = [0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2];
        byte[] lines = [0xFF, 7, 0xFF, 7, 0xFF, 7, 0xFF, 7, 0xFF, 7, 0xFF, 7];
        byte[] encoded = [.. flat[..40], .. table, .. lines];
        encoded[39] = 1;
        Assert.False(PsdMetadata.Read(encoded).IsRestrictedProfile);
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(encoded[..^1]));
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error([.. encoded, 0]));
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(encoded[..45]));
    }

    [Fact]
    public void InvalidResourceSignatureLengthsAndDuplicateIdsAreRejected()
    {
        byte[] original = Fixture("owned-metadata-rgb.psd");
        byte[] x = (byte[])original.Clone(); x[34] = 0;
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(x));
        x = (byte[])original.Clone(); BinaryPrimitives.WriteUInt32BigEndian(x.AsSpan(42, 4), uint.MaxValue);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(x));
        x = (byte[])original.Clone(); BinaryPrimitives.WriteUInt32BigEndian(x.AsSpan(30, 4), uint.MaxValue);
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(x));
        x = (byte[])original.Clone();
        int firstLength = (int)BinaryPrimitives.ReadUInt32BigEndian(x.AsSpan(42, 4));
        int next = 46 + firstLength + (firstLength & 1);
        x[next + 4] = x[38]; x[next + 5] = x[39];
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Error(x));
    }

    [Fact]
    public void ResourceCountBudgetIsEnforced()
    {
        byte[] original = Fixture("owned-flat-rgb.psd");
        const int count = 1025;
        byte[] data = new byte[original.Length + count * 12];
        original.AsSpan(0, 34).CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(30, 4), count * 12);
        for (int i = 0; i < count; i++)
        {
            int at = 34 + i * 12;
            "8BIM"u8.CopyTo(data.AsSpan(at, 4));
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at + 4, 2), (ushort)i);
        }
        original.AsSpan(34).CopyTo(data.AsSpan(34 + count * 12));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(data));
    }

    [Fact]
    public void ALayerSectionAndColorModeDataAreSkippedOutsideTheRestrictedProfile()
    {
        byte[] bytes = Fixture("owned-metadata-rgb.psd");
        // Grow the layer section by eight bytes, retaining the original compression and pixels.
        int layers = bytes.Length - 12 - 2 - 4;
        byte[] x = new byte[bytes.Length + 8];
        bytes.AsSpan(0, layers + 4).CopyTo(x);
        BinaryPrimitives.WriteUInt32BigEndian(x.AsSpan(layers, 4), 8);
        bytes.AsSpan(layers + 4).CopyTo(x.AsSpan(layers + 12));
        PsdMetadata metadata = PsdMetadata.Read(x);
        Assert.False(metadata.IsRestrictedProfile);
        Assert.NotNull(metadata.ReadExif());
        // A section length that runs past the end of the file is still refused.
        BinaryPrimitives.WriteUInt32BigEndian(x.AsSpan(layers, 4), 1000);
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error(x));

        // Four bytes of color-mode data in front of the resources.
        byte[] y = new byte[bytes.Length + 4];
        bytes.AsSpan(0, 30).CopyTo(y);
        BinaryPrimitives.WriteUInt32BigEndian(y.AsSpan(26, 4), 4);
        bytes.AsSpan(30).CopyTo(y.AsSpan(34));
        PsdMetadata colored = PsdMetadata.Read(y);
        Assert.False(colored.IsRestrictedProfile);
        Assert.NotNull(colored.ReadIptc());
    }
}
