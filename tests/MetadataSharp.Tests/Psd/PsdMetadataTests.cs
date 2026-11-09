using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Psd;
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

    [Fact]
    public void FlatAndMetadataPresentOwnedFixturesAreRead()
    {
        byte[] flat = Fixture("owned-flat-rgb.psd");
        PsdMetadata empty = PsdMetadata.Read(flat);
        Assert.Equal(2u, empty.Width); Assert.Equal(2u, empty.Height);
        Assert.Null(empty.GetResource(1058)); Assert.Null(empty.GetResource(1060)); Assert.Null(empty.GetResource(1028));
        byte[] rich = Fixture("owned-metadata-rgb.psd");
        PsdMetadata metadata = PsdMetadata.Read(rich);
        Assert.Equal(2u, metadata.Width); Assert.Equal(2u, metadata.Height);
        Assert.Contains("B8Artist", Encoding.ASCII.GetString(metadata.GetResource(1058)!));
        Assert.Contains("B8Tool", Encoding.UTF8.GetString(metadata.GetResource(1060)!));
        Assert.Contains("B8Keyword", Encoding.ASCII.GetString(metadata.GetResource(1028)!));
        Assert.Null(metadata.GetResource(1039));
        byte[] copy = metadata.GetResource(1058)!;
        copy[0] ^= 0xff; rich[0] ^= 0xff;
        Assert.Equal((byte)'I', metadata.GetResource(1058)![0]);
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
        x = (byte[])original.Clone(); x[13] = 4;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
        x = (byte[])original.Clone(); x[39] = 1;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
        Assert.Equal(MetadataErrorCode.InvalidPsd, Error([.. original, 0]));
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
    public void RejectsUnreviewedNonemptyLayerSection()
    {
        byte[] bytes = Fixture("owned-flat-rgb.psd");
        // Grow a layer section by one byte, retaining the original compression and pixels.
        byte[] x = new byte[bytes.Length + 1];
        bytes.AsSpan(0, 38).CopyTo(x);
        BinaryPrimitives.WriteUInt32BigEndian(x.AsSpan(34, 4), 1);
        bytes.AsSpan(38).CopyTo(x.AsSpan(39));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
    }
}
