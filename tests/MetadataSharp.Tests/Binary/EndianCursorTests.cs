using MetadataSharp.Core;
using MetadataSharp.Core.Binary;
using Xunit;

namespace MetadataSharp.Tests.Binary;

public sealed class EndianCursorTests
{

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void UInt16UsesKnownBytesAndKeepsCursorOnFailure(Endianness order)
    {
        byte[] expected = [0x12, 0x34];
        if (order == Endianness.LittleEndian)
            Array.Reverse(expected);
        const ushort value = 0x1234;
        var reader = new EndianReader(expected, order);
        Assert.True(reader.TryReadUInt16(out ushort actual));
        Assert.Equal(value, actual);
        Assert.Equal(2, reader.Position);
        Assert.Equal(0, reader.Remaining);
        Assert.False(reader.TryReadUInt16(out ushort failed));
        Assert.Equal(default(ushort), failed);
        Assert.Equal(2, reader.Position);
        byte[] output = new byte[2];
        var writer = new EndianWriter(output, order);
        Assert.True(writer.TryWriteUInt16(value));
        Assert.Equal(expected, output);
        Assert.False(writer.TryWriteUInt16(value));
        Assert.Equal(2, writer.Position);
        Assert.Equal(expected, output);
        for (int length = 0; length < 2; length++)
        {
            var shortReader = new EndianReader(expected.AsSpan(0, length), order);
            Assert.False(shortReader.TryReadUInt16(out _));
            Assert.Equal(0, shortReader.Position);
            byte[] sentinel = Enumerable.Repeat((byte)0xAA, length).ToArray();
            var shortWriter = new EndianWriter(sentinel, order);
            Assert.False(shortWriter.TryWriteUInt16(value));
            Assert.Equal(0, shortWriter.Position);
            Assert.All(sentinel, b => Assert.Equal((byte)0xAA, b));
        }
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void Int16UsesKnownBytesAndKeepsCursorOnFailure(Endianness order)
    {
        byte[] expected = [0xFF, 0xFE];
        if (order == Endianness.LittleEndian)
            Array.Reverse(expected);
        const short value = -2;
        var reader = new EndianReader(expected, order);
        Assert.True(reader.TryReadInt16(out short actual));
        Assert.Equal(value, actual);
        Assert.Equal(2, reader.Position);
        Assert.Equal(0, reader.Remaining);
        Assert.False(reader.TryReadInt16(out short failed));
        Assert.Equal(default(short), failed);
        Assert.Equal(2, reader.Position);
        byte[] output = new byte[2];
        var writer = new EndianWriter(output, order);
        Assert.True(writer.TryWriteInt16(value));
        Assert.Equal(expected, output);
        Assert.False(writer.TryWriteInt16(value));
        Assert.Equal(2, writer.Position);
        Assert.Equal(expected, output);
        for (int length = 0; length < 2; length++)
        {
            var shortReader = new EndianReader(expected.AsSpan(0, length), order);
            Assert.False(shortReader.TryReadInt16(out _));
            Assert.Equal(0, shortReader.Position);
            byte[] sentinel = Enumerable.Repeat((byte)0xAA, length).ToArray();
            var shortWriter = new EndianWriter(sentinel, order);
            Assert.False(shortWriter.TryWriteInt16(value));
            Assert.Equal(0, shortWriter.Position);
            Assert.All(sentinel, b => Assert.Equal((byte)0xAA, b));
        }
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void UInt32UsesKnownBytesAndKeepsCursorOnFailure(Endianness order)
    {
        byte[] expected = [0x12, 0x34, 0x56, 0x78];
        if (order == Endianness.LittleEndian)
            Array.Reverse(expected);
        const uint value = 0x12345678U;
        var reader = new EndianReader(expected, order);
        Assert.True(reader.TryReadUInt32(out uint actual));
        Assert.Equal(value, actual);
        Assert.Equal(4, reader.Position);
        Assert.Equal(0, reader.Remaining);
        Assert.False(reader.TryReadUInt32(out uint failed));
        Assert.Equal(default(uint), failed);
        Assert.Equal(4, reader.Position);
        byte[] output = new byte[4];
        var writer = new EndianWriter(output, order);
        Assert.True(writer.TryWriteUInt32(value));
        Assert.Equal(expected, output);
        Assert.False(writer.TryWriteUInt32(value));
        Assert.Equal(4, writer.Position);
        Assert.Equal(expected, output);
        for (int length = 0; length < 4; length++)
        {
            var shortReader = new EndianReader(expected.AsSpan(0, length), order);
            Assert.False(shortReader.TryReadUInt32(out _));
            Assert.Equal(0, shortReader.Position);
            byte[] sentinel = Enumerable.Repeat((byte)0xAA, length).ToArray();
            var shortWriter = new EndianWriter(sentinel, order);
            Assert.False(shortWriter.TryWriteUInt32(value));
            Assert.Equal(0, shortWriter.Position);
            Assert.All(sentinel, b => Assert.Equal((byte)0xAA, b));
        }
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void Int32UsesKnownBytesAndKeepsCursorOnFailure(Endianness order)
    {
        byte[] expected = [0x80, 0, 0, 0];
        if (order == Endianness.LittleEndian)
            Array.Reverse(expected);
        const int value = int.MinValue;
        var reader = new EndianReader(expected, order);
        Assert.True(reader.TryReadInt32(out int actual));
        Assert.Equal(value, actual);
        Assert.Equal(4, reader.Position);
        Assert.Equal(0, reader.Remaining);
        Assert.False(reader.TryReadInt32(out int failed));
        Assert.Equal(default(int), failed);
        Assert.Equal(4, reader.Position);
        byte[] output = new byte[4];
        var writer = new EndianWriter(output, order);
        Assert.True(writer.TryWriteInt32(value));
        Assert.Equal(expected, output);
        Assert.False(writer.TryWriteInt32(value));
        Assert.Equal(4, writer.Position);
        Assert.Equal(expected, output);
        for (int length = 0; length < 4; length++)
        {
            var shortReader = new EndianReader(expected.AsSpan(0, length), order);
            Assert.False(shortReader.TryReadInt32(out _));
            Assert.Equal(0, shortReader.Position);
            byte[] sentinel = Enumerable.Repeat((byte)0xAA, length).ToArray();
            var shortWriter = new EndianWriter(sentinel, order);
            Assert.False(shortWriter.TryWriteInt32(value));
            Assert.Equal(0, shortWriter.Position);
            Assert.All(sentinel, b => Assert.Equal((byte)0xAA, b));
        }
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void UInt64UsesKnownBytesAndKeepsCursorOnFailure(Endianness order)
    {
        byte[] expected = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];
        if (order == Endianness.LittleEndian)
            Array.Reverse(expected);
        const ulong value = 0x0123456789ABCDEFUL;
        var reader = new EndianReader(expected, order);
        Assert.True(reader.TryReadUInt64(out ulong actual));
        Assert.Equal(value, actual);
        Assert.Equal(8, reader.Position);
        Assert.Equal(0, reader.Remaining);
        Assert.False(reader.TryReadUInt64(out ulong failed));
        Assert.Equal(default(ulong), failed);
        Assert.Equal(8, reader.Position);
        byte[] output = new byte[8];
        var writer = new EndianWriter(output, order);
        Assert.True(writer.TryWriteUInt64(value));
        Assert.Equal(expected, output);
        Assert.False(writer.TryWriteUInt64(value));
        Assert.Equal(8, writer.Position);
        Assert.Equal(expected, output);
        for (int length = 0; length < 8; length++)
        {
            var shortReader = new EndianReader(expected.AsSpan(0, length), order);
            Assert.False(shortReader.TryReadUInt64(out _));
            Assert.Equal(0, shortReader.Position);
            byte[] sentinel = Enumerable.Repeat((byte)0xAA, length).ToArray();
            var shortWriter = new EndianWriter(sentinel, order);
            Assert.False(shortWriter.TryWriteUInt64(value));
            Assert.Equal(0, shortWriter.Position);
            Assert.All(sentinel, b => Assert.Equal((byte)0xAA, b));
        }
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void Int64UsesKnownBytesAndKeepsCursorOnFailure(Endianness order)
    {
        byte[] expected = [0x80, 0, 0, 0, 0, 0, 0, 0];
        if (order == Endianness.LittleEndian)
            Array.Reverse(expected);
        const long value = long.MinValue;
        var reader = new EndianReader(expected, order);
        Assert.True(reader.TryReadInt64(out long actual));
        Assert.Equal(value, actual);
        Assert.Equal(8, reader.Position);
        Assert.Equal(0, reader.Remaining);
        Assert.False(reader.TryReadInt64(out long failed));
        Assert.Equal(default(long), failed);
        Assert.Equal(8, reader.Position);
        byte[] output = new byte[8];
        var writer = new EndianWriter(output, order);
        Assert.True(writer.TryWriteInt64(value));
        Assert.Equal(expected, output);
        Assert.False(writer.TryWriteInt64(value));
        Assert.Equal(8, writer.Position);
        Assert.Equal(expected, output);
        for (int length = 0; length < 8; length++)
        {
            var shortReader = new EndianReader(expected.AsSpan(0, length), order);
            Assert.False(shortReader.TryReadInt64(out _));
            Assert.Equal(0, shortReader.Position);
            byte[] sentinel = Enumerable.Repeat((byte)0xAA, length).ToArray();
            var shortWriter = new EndianWriter(sentinel, order);
            Assert.False(shortWriter.TryWriteInt64(value));
            Assert.Equal(0, shortWriter.Position);
            Assert.All(sentinel, b => Assert.Equal((byte)0xAA, b));
        }
    }

    [Fact]
    public void MixedCursorOperationsAdvanceByTheirExactWidths()
    {
        byte[] data = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];
        var reader = new EndianReader(data, Endianness.BigEndian);
        Assert.True(reader.TryReadUInt16(out ushort first));
        Assert.Equal((ushort)0x1234, first);
        Assert.False(reader.TryReadUInt64(out _));
        Assert.Equal(2, reader.Position);
        Assert.True(reader.TryReadUInt32(out uint second));
        Assert.Equal(0x56789ABCU, second);
        byte[] output = new byte[6];
        var writer = new EndianWriter(output, Endianness.BigEndian);
        Assert.True(writer.TryWriteUInt16(first));
        Assert.False(writer.TryWriteUInt64(0));
        Assert.Equal(2, writer.Position);
        Assert.True(writer.TryWriteUInt32(second));
        Assert.Equal(data, output);
    }

    [Fact]
    public void InvalidByteOrderIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var reader = new EndianReader([], (Endianness)99);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var writer = new EndianWriter([], (Endianness)99);
        });
    }
}
