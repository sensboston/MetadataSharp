using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class TiffIptcTests
{
    private const int Description = 0x010E, IptcNaa = 0x83BB, ImageResources = 0x8649;
    private const int Byte = 1, Ascii = 2, Long = 4, Undefined = 7;

    /// <summary>2:120 Caption "hello": ten bytes, so stored as LONGs it is padded with two zeros.</summary>
    private static readonly byte[] Caption = [0x1C, 2, 120, 0, 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o'];
    /// <summary>2:05 ObjectName "T": six bytes.</summary>
    private static readonly byte[] Title = [0x1C, 2, 5, 0, 1, (byte)'T'];
    /// <summary>2:120 Caption with no value: five bytes that end in the two zeros of the length.</summary>
    private static readonly byte[] EmptyCaption = [0x1C, 2, 120, 0, 0];

    /// <summary>
    /// Authored synthetic TIFF bytes: a header, the primary directory at 8, and every value stored
    /// after it at an even offset. Each value here is longer than four bytes, so none is inline, and
    /// the tags are given in ascending order.
    /// </summary>
    private static byte[] Build(bool big, params (int Tag, int Type, byte[] Value)[] fields)
    {
        int at = 8 + 2 + fields.Length * 12 + 4;
        var offsets = new int[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            offsets[i] = at;
            at += fields[i].Value.Length + (fields[i].Value.Length & 1);
        }
        byte[] file = new byte[at];
        void U16(int offset, int value)
        {
            if (big) BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(offset), (ushort)value);
            else BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), (ushort)value);
        }
        void U32(int offset, int value)
        {
            if (big) BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(offset), (uint)value);
            else BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), (uint)value);
        }
        file[0] = file[1] = big ? (byte)0x4D : (byte)0x49;
        U16(2, 42);
        U32(4, 8);
        U16(8, fields.Length);
        for (int i = 0; i < fields.Length; i++)
        {
            int p = 10 + i * 12;
            U16(p, fields[i].Tag);
            U16(p + 2, fields[i].Type);
            U32(p + 4, fields[i].Value.Length / (fields[i].Type == Long ? 4 : 1));
            U32(p + 8, offsets[i]);
            fields[i].Value.CopyTo(file, offsets[i]);
        }
        return file;
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    /// <summary>One Photoshop resource block with an empty name; the data is padded to an even length.</summary>
    private static byte[] Resource(int id, byte[] data)
    {
        byte[] block = new byte[12 + data.Length + (data.Length & 1)];
        "8BIM"u8.CopyTo(block);
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(4), (ushort)id);
        BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(8), (uint)data.Length);
        data.CopyTo(block, 12);
        return block;
    }

    private static string Text(IptcDataset dataset)
    {
        byte[] bytes = new byte[dataset.Value.ByteLength];
        dataset.Value.CopyRawTo(bytes);
        return Encoding.ASCII.GetString(bytes);
    }

    private static MetadataErrorCode Code(byte[] file) => Assert.Throws<MetadataException>(() => TiffIptc.Read(file)).Code;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheIptcNaaTagIsRead(bool big)
    {
        IptcPacket packet = TiffIptc.Read(Build(big, (IptcNaa, Undefined, Join(Caption, Title))))!;
        Assert.Equal(2, packet.Datasets.Count);
        Assert.Equal(new IptcDatasetKey(2, 120), packet.Datasets[0].Key);
        Assert.Equal("hello", Text(packet.Datasets[0]));
        Assert.Equal(new IptcDatasetKey(2, 5), packet.Datasets[1].Key);
        Assert.Equal("T", Text(packet.Datasets[1]));
        Assert.Equal(16, packet.ByteLength);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongPaddingAfterTheLastDatasetIsNotPartOfTheStream(bool big)
    {
        // Ten bytes of IIM in three LONGs: the stored value ends in two zeros, in either byte order.
        IptcPacket padded = TiffIptc.Read(Build(big, (IptcNaa, Long, Join(Caption, new byte[2]))))!;
        Assert.Equal("hello", Text(Assert.Single(padded.Datasets)));
        Assert.Equal(10, padded.ByteLength);

        // The zeros of an empty last dataset belong to it; only the three after it are padding.
        IptcPacket empty = TiffIptc.Read(Build(big, (IptcNaa, Long, Join(EmptyCaption, new byte[3]))))!;
        Assert.Equal(0, Assert.Single(empty.Datasets).Value.ByteLength);
        Assert.Equal(5, empty.ByteLength);

        // A stream that fills its LONGs has nothing to remove.
        IptcPacket exact = TiffIptc.Read(Build(big, (IptcNaa, Long, Join(Caption, Title))))!;
        Assert.Equal(2, exact.Datasets.Count);
    }

    [Fact]
    public void OnlyLongPaddingIsRemoved()
    {
        // More zeros than a LONG can add, and zeros after a stream that is not stored as LONGs, are
        // left for the IPTC reader. It reads them as padding and marks the stream as not editable.
        IptcPacket wide = TiffIptc.Read(Build(false, (IptcNaa, Long, Join(Caption, new byte[6]))))!;
        Assert.False(wide.IsEditable); Assert.Equal(16, wide.ByteLength); Assert.Single(wide.Datasets);
        IptcPacket bytes = TiffIptc.Read(Build(false, (IptcNaa, Undefined, Join(Caption, new byte[2]))))!;
        Assert.False(bytes.IsEditable); Assert.Equal(12, bytes.ByteLength);
        Assert.True(TiffIptc.Read(Build(false, (IptcNaa, Long, Join(Caption, new byte[2]))))!.IsEditable);
        // A nonzero byte after the last dataset, and bytes that are no dataset at all, are refused.
        Assert.Equal(MetadataErrorCode.InvalidIptc, Code(Build(false, (IptcNaa, Long, Join(Caption, new byte[] { 0, 7 })))));
        Assert.Equal(MetadataErrorCode.InvalidIptc, Code(Build(false, (IptcNaa, Undefined, new byte[] { 1, 2, 3, 4, 5, 6 }))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhotoshopResourcesAreSearchedWhenTheTagIsAbsent(bool big)
    {
        byte[] resources = Join(Resource(0x03ED, new byte[5]), Resource(0x0404, Join(Caption, Title)));
        IptcPacket packet = TiffIptc.Read(Build(big, (ImageResources, Byte, resources)))!;
        Assert.Equal(2, packet.Datasets.Count);
        Assert.Equal("hello", Text(packet.Datasets[0]));

        Assert.Null(TiffIptc.Read(Build(big, (ImageResources, Byte, Resource(0x03ED, new byte[5])))));
    }

    [Fact]
    public void TheIptcNaaTagIsPreferredToTheResources()
    {
        IptcPacket packet = TiffIptc.Read(Build(false,
            (IptcNaa, Undefined, Title), (ImageResources, Byte, Resource(0x0404, Caption))))!;
        Assert.Equal("T", Text(Assert.Single(packet.Datasets)));
    }

    [Fact]
    public void AFileWithoutEitherTagHasNoIptc() =>
        Assert.Null(TiffIptc.Read(Build(false, (Description, Ascii, Encoding.ASCII.GetBytes("plain\0")))));

    [Fact]
    public void MalformedInputIsRefusedWithTheCodeOfTheReaderThatRefusedIt()
    {
        Assert.Equal(MetadataErrorCode.InvalidTiff, Code([1, 2, 3, 4, 5, 6, 7, 8]));
        byte[] truncated = Resource(0x0404, Join(Caption, Title))[..^4];
        Assert.Equal(MetadataErrorCode.InvalidPhotoshopResources, Code(Build(false, (ImageResources, Byte, truncated))));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => TiffIptc.Read(
            Build(false, (IptcNaa, Undefined, Join(Caption, Title))), options: new() { Limits = new() { MaxPacketBytes = 15 } })).Code);
    }
}
