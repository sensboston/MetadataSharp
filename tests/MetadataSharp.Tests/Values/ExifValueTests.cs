using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Values;

public sealed class ExifValueTests
{
    [Theory]
    [InlineData(TiffFieldType.Byte, 1)]
    [InlineData(TiffFieldType.Ascii, 1)]
    [InlineData(TiffFieldType.Short, 2)]
    [InlineData(TiffFieldType.Long, 4)]
    [InlineData(TiffFieldType.Rational, 8)]
    [InlineData(TiffFieldType.SByte, 1)]
    [InlineData(TiffFieldType.Undefined, 1)]
    [InlineData(TiffFieldType.SShort, 2)]
    [InlineData(TiffFieldType.SLong, 4)]
    [InlineData(TiffFieldType.SRational, 8)]
    [InlineData(TiffFieldType.Float, 4)]
    [InlineData(TiffFieldType.Double, 8)]
    public void AllKnownWidthsPreserveRawBytesInBothOrders(TiffFieldType type, int width)
    {
        foreach (Endianness order in Enum.GetValues<Endianness>())
        {
            byte[] source = Enumerable.Range(0, width * 3).Select(i => (byte)(i * 7)).ToArray();
            byte[] expected = (byte[])source.Clone();
            ExifValue value = ExifValue.FromRaw(type, 3, order, source);
            source.AsSpan().Fill(0xFF);
            Assert.Equal(type, value.FieldType);
            Assert.Equal(3U, value.Count);
            Assert.Equal(order, value.ByteOrder);
            Assert.Equal(width * 3, value.ByteLength);
            byte[] output = new byte[expected.Length + 1];
            output[^1] = 0xAC;
            value.CopyRawTo(output);
            Assert.Equal(expected, output[..^1]);
            Assert.Equal((byte)0xAC, output[^1]);
            output.AsSpan().Clear();
            value.CopyRawTo(output);
            Assert.Equal(expected, output[..^1]);
            byte[] shortOutput = Enumerable.Repeat((byte)0xCC, expected.Length - 1).ToArray();
            Assert.Throws<ArgumentException>(() => value.CopyRawTo(shortOutput));
            Assert.All(shortOutput, b => Assert.Equal((byte)0xCC, b));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(65535)]
    public void UnknownTypeHasNoGuessedWidth(int type)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExifValue.FromRaw((TiffFieldType)type, 0, Endianness.LittleEndian, []));
    }

    [Fact]
    public void ArgumentsAreValidatedBeforeCopying()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExifValue.FromRaw(TiffFieldType.Byte, 1, (Endianness)99, new byte[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExifValue.FromRaw(TiffFieldType.Rational, uint.MaxValue, Endianness.LittleEndian, []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExifValue.FromRaw(TiffFieldType.Byte, (uint)int.MaxValue + 1, Endianness.LittleEndian, []));
        Assert.Throws<ArgumentException>(() =>
            ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, new byte[1]));
        Assert.Throws<ArgumentException>(() =>
            ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, new byte[3]));
        Assert.Throws<ArgumentException>(() =>
            ExifValue.FromRaw(TiffFieldType.Byte, 0, Endianness.LittleEndian, new byte[1]));
    }

    [Fact]
    public void ZeroCountCanBeRepresentedWithoutInventingData()
    {
        ExifValue value = ExifValue.FromRaw(TiffFieldType.Rational, 0, Endianness.BigEndian, []);
        Assert.Equal(0U, value.Count);
        Assert.Equal(0, value.ByteLength);
        value.CopyRawTo(Span<byte>.Empty);
    }

    [Fact]
    public void FloatAndDoublePayloadsAreNotNormalized()
    {
        // Deliberately preserve NaN payloads and signed zero as uninterpreted bytes.
        byte[] floatBits = [0x7F, 0xC0, 0x00, 0x17];
        byte[] doubleBits = [0x80, 0, 0, 0, 0, 0, 0, 0];
        byte[] output = new byte[8];
        ExifValue.FromRaw(TiffFieldType.Float, 1, Endianness.BigEndian, floatBits).CopyRawTo(output);
        Assert.Equal(floatBits, output[..4]);
        ExifValue.FromRaw(TiffFieldType.Double, 1, Endianness.BigEndian, doubleBits).CopyRawTo(output);
        Assert.Equal(doubleBits, output);
    }

    [Fact]
    public void RawObjectsHaveReferenceRatherThanImplicitSemanticEquality()
    {
        ExifValue first = ExifValue.FromRaw(TiffFieldType.Byte, 1, Endianness.LittleEndian, [1]);
        ExifValue second = ExifValue.FromRaw(TiffFieldType.Byte, 1, Endianness.LittleEndian, [1]);
        Assert.NotSame(first, second);
        Assert.False(first.Equals(second));
    }
}
