using System.Buffers.Binary;

namespace MetadataSharp.Core.Binary;

/// <summary>A bounded span cursor. Failed operations never advance or modify data.</summary>
internal ref struct EndianReader
{
    private readonly ReadOnlySpan<byte> buffer;
    private readonly Endianness byteOrder;

    internal EndianReader(ReadOnlySpan<byte> buffer, Endianness byteOrder)
    {
        if (byteOrder is not Endianness.LittleEndian and not Endianness.BigEndian)
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        this.buffer = buffer;
        this.byteOrder = byteOrder;
        Position = 0;
    }

    internal int Position { get; private set; }
    internal int Remaining => buffer.Length - Position;

    internal bool TryReadUInt16(out ushort value)
    {
        value = default;
        if (Remaining < 2)
            return false;
        value = byteOrder == Endianness.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(Position, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(Position, 2));
        Position += 2;
        return true;
    }

    internal bool TryReadInt16(out short value)
    {
        value = default;
        if (Remaining < 2)
            return false;
        value = byteOrder == Endianness.LittleEndian
            ? BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(Position, 2))
            : BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(Position, 2));
        Position += 2;
        return true;
    }

    internal bool TryReadUInt32(out uint value)
    {
        value = default;
        if (Remaining < 4)
            return false;
        value = byteOrder == Endianness.LittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(Position, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(Position, 4));
        Position += 4;
        return true;
    }

    internal bool TryReadInt32(out int value)
    {
        value = default;
        if (Remaining < 4)
            return false;
        value = byteOrder == Endianness.LittleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(Position, 4))
            : BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(Position, 4));
        Position += 4;
        return true;
    }

    internal bool TryReadUInt64(out ulong value)
    {
        value = default;
        if (Remaining < 8)
            return false;
        value = byteOrder == Endianness.LittleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(Position, 8))
            : BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(Position, 8));
        Position += 8;
        return true;
    }

    internal bool TryReadInt64(out long value)
    {
        value = default;
        if (Remaining < 8)
            return false;
        value = byteOrder == Endianness.LittleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(Position, 8))
            : BinaryPrimitives.ReadInt64BigEndian(buffer.Slice(Position, 8));
        Position += 8;
        return true;
    }
}
