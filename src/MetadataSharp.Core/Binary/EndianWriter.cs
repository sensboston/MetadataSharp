using System.Buffers.Binary;

namespace MetadataSharp.Core.Binary;

/// <summary>A bounded span cursor. Failed operations never advance or modify data.</summary>
internal ref struct EndianWriter
{
    private readonly Span<byte> buffer;
    private readonly Endianness byteOrder;

    /// <summary>Starts a cursor at the first byte of a destination span.</summary>
    /// <param name="buffer">The bytes to write.</param>
    /// <param name="byteOrder">The order used to encode multi-byte values.</param>
    /// <exception cref="ArgumentOutOfRangeException">The byte order is not little- or big-endian.</exception>
    internal EndianWriter(Span<byte> buffer, Endianness byteOrder)
    {
        if (byteOrder is not Endianness.LittleEndian and not Endianness.BigEndian)
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        this.buffer = buffer;
        this.byteOrder = byteOrder;
        Position = 0;
    }

    /// <summary>The offset of the next byte to write.</summary>
    internal int Position { get; private set; }
    /// <summary>The number of bytes remaining in the destination span.</summary>
    internal int Remaining => buffer.Length - Position;

    /// <summary>Writes one unsigned 16-bit integer in the configured byte order.</summary>
    /// <param name="value">The integer to write.</param>
    /// <returns>True if the value was written and the cursor advanced; otherwise false and the span is unchanged.</returns>
    internal bool TryWriteUInt16(ushort value)
    {
        if (Remaining < 2)
            return false;
        if (byteOrder == Endianness.LittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(Position, 2), value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(Position, 2), value);
        Position += 2;
        return true;
    }

    /// <summary>Writes one signed 16-bit integer in the configured byte order.</summary>
    /// <param name="value">The integer to write.</param>
    /// <returns>True if the value was written and the cursor advanced; otherwise false and the span is unchanged.</returns>
    internal bool TryWriteInt16(short value)
    {
        if (Remaining < 2)
            return false;
        if (byteOrder == Endianness.LittleEndian)
            BinaryPrimitives.WriteInt16LittleEndian(buffer.Slice(Position, 2), value);
        else
            BinaryPrimitives.WriteInt16BigEndian(buffer.Slice(Position, 2), value);
        Position += 2;
        return true;
    }

    /// <summary>Writes one unsigned 32-bit integer in the configured byte order.</summary>
    /// <param name="value">The integer to write.</param>
    /// <returns>True if the value was written and the cursor advanced; otherwise false and the span is unchanged.</returns>
    internal bool TryWriteUInt32(uint value)
    {
        if (Remaining < 4)
            return false;
        if (byteOrder == Endianness.LittleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(Position, 4), value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(Position, 4), value);
        Position += 4;
        return true;
    }

    /// <summary>Writes one signed 32-bit integer in the configured byte order.</summary>
    /// <param name="value">The integer to write.</param>
    /// <returns>True if the value was written and the cursor advanced; otherwise false and the span is unchanged.</returns>
    internal bool TryWriteInt32(int value)
    {
        if (Remaining < 4)
            return false;
        if (byteOrder == Endianness.LittleEndian)
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(Position, 4), value);
        else
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(Position, 4), value);
        Position += 4;
        return true;
    }

    /// <summary>Writes one unsigned 64-bit integer in the configured byte order.</summary>
    /// <param name="value">The integer to write.</param>
    /// <returns>True if the value was written and the cursor advanced; otherwise false and the span is unchanged.</returns>
    internal bool TryWriteUInt64(ulong value)
    {
        if (Remaining < 8)
            return false;
        if (byteOrder == Endianness.LittleEndian)
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(Position, 8), value);
        else
            BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(Position, 8), value);
        Position += 8;
        return true;
    }

    /// <summary>Writes one signed 64-bit integer in the configured byte order.</summary>
    /// <param name="value">The integer to write.</param>
    /// <returns>True if the value was written and the cursor advanced; otherwise false and the span is unchanged.</returns>
    internal bool TryWriteInt64(long value)
    {
        if (Remaining < 8)
            return false;
        if (byteOrder == Endianness.LittleEndian)
            BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(Position, 8), value);
        else
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(Position, 8), value);
        Position += 8;
        return true;
    }
}
