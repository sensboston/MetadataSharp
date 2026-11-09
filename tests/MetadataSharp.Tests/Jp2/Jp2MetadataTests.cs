using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Jp2;
using Xunit;

namespace MetadataSharp.Tests.Jp2;

public sealed class Jp2MetadataTests
{
    private static byte[] Fixture()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MetadataSharp.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "tests", "fixtures", "b8", "owned-rgb.jp2"));
    }
    private static MetadataErrorCode Error(byte[] data) => Assert.Throws<MetadataException>(() => Jp2Metadata.Read(data)).Code;

    [Fact]
    public void OwnedBoxedJp2YieldsDeclaredDimensions()
    {
        Jp2Metadata result = Jp2Metadata.Read(Fixture());
        Assert.Equal(8u, result.Width); Assert.Equal(8u, result.Height);
        Assert.Equal(0, result.XmlBoxCount);
        Assert.Throws<IndexOutOfRangeException>(() => result.GetXmlBox(0));
    }

    [Fact]
    public void RawXmlBoxesAreCopiedAndNotAutomaticallyCalledXmp()
    {
        byte[] data = Fixture();
        byte[] payload = "<owned>test</owned>"u8.ToArray();
        byte[] box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        "xml "u8.CopyTo(box.AsSpan(4)); payload.CopyTo(box.AsSpan(8));
        Jp2Metadata result = Jp2Metadata.Read([.. data, .. box]);
        Assert.Equal(1, result.XmlBoxCount);
        byte[] copied = result.GetXmlBox(0);
        Assert.Equal("<owned>test</owned>", Encoding.UTF8.GetString(copied));
        copied[0] = 0;
        Assert.Equal((byte)'<', result.GetXmlBox(0)[0]);
    }

    [Fact]
    public void EveryTruncationIsRejected()
    {
        byte[] data = Fixture();
        for (int i = 0; i < data.Length; i++)
            Assert.Throws<MetadataException>(() => Jp2Metadata.Read(data.AsSpan(0, i)));
    }

    [Fact]
    public void BadLengthsOrderingAndUnsupportedBoxesAreRefused()
    {
        byte[] original = Fixture();
        byte[] x = (byte[])original.Clone(); x[0] = 0; x[1] = 0; x[2] = 0; x[3] = 1;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
        x = (byte[])original.Clone(); x[3] = 0;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
        x = (byte[])original.Clone(); x[3] = 7;
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error(x));
        x = (byte[])original.Clone(); x[8] ^= 1;
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error(x));
        x = (byte[])original.Clone(); x[20] = (byte)'x';
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
        x = (byte[])original.Clone(); x[40] = 0; x[41] = 0; x[42] = 0; x[43] = 7;
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error(x));
        x = (byte[])original.Clone(); x[48] = 0; x[49] = 0; x[50] = 0; x[51] = 0;
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error(x));
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error([.. original, .. original.AsSpan(0, 12).ToArray()]));
    }
}
