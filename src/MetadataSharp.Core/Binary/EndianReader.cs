using System.Buffers.Binary;

namespace MetadataSharp.Core.Binary;

/// <summary>A bounded span cursor. Failed operations never advance or modify data.</summary>
internal ref struct EndianReader
{
    private readonly ReadOnlySpan<byte> buffer;
    // Keep the source order with the cursor; every multibyte read uses the same header-selected order.
    private readonly Endianness byteOrder;

    /// <summary>Starts a cursor at the first byte of a span.</summary>
    /// <param name="buffer">The bytes to read.</param>
    /// <param name="byteOrder">The order used to assemble multi-byte values.</param>
    /// <exception cref="ArgumentOutOfRangeException">The byte order is not little- or big-endian.</exception>
    internal EndianReader(ReadOnlySpan<byte> buffer, Endianness byteOrder)
    {
        if (byteOrder is not Endianness.LittleEndian and not Endianness.BigEndian)
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        this.buffer = buffer;
        this.byteOrder = byteOrder;
        Position = 0;
    }

    /// <summary>The offset of the next byte to read.</summary>
    internal int Position { get; private set; }
    /// <summary>The number of bytes remaining in the span.</summary>
    internal int Remaining => buffer.Length - Position;

    /// <summary>Reads one unsigned 16-bit integer in the configured byte order.</summary>
    /// <param name="value">Receives the integer, or zero when fewer than two bytes remain.</param>
    /// <returns>True if the value was read and the cursor advanced; otherwise false and the cursor is unchanged.</returns>
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

    /// <summary>Reads one signed 16-bit integer in the configured byte order.</summary>
    /// <param name="value">Receives the integer, or zero when fewer than two bytes remain.</param>
    /// <returns>True if the value was read and the cursor advanced; otherwise false and the cursor is unchanged.</returns>
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

    /// <summary>Reads one unsigned 32-bit integer in the configured byte order.</summary>
    /// <param name="value">Receives the integer, or zero when fewer than four bytes remain.</param>
    /// <returns>True if the value was read and the cursor advanced; otherwise false and the cursor is unchanged.</returns>
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

    /// <summary>Reads one signed 32-bit integer in the configured byte order.</summary>
    /// <param name="value">Receives the integer, or zero when fewer than four bytes remain.</param>
    /// <returns>True if the value was read and the cursor advanced; otherwise false and the cursor is unchanged.</returns>
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

    /// <summary>Reads one unsigned 64-bit integer in the configured byte order.</summary>
    /// <param name="value">Receives the integer, or zero when fewer than eight bytes remain.</param>
    /// <returns>True if the value was read and the cursor advanced; otherwise false and the cursor is unchanged.</returns>
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

    /// <summary>Reads one signed 64-bit integer in the configured byte order.</summary>
    /// <param name="value">Receives the integer, or zero when fewer than eight bytes remain.</param>
    /// <returns>True if the value was read and the cursor advanced; otherwise false and the cursor is unchanged.</returns>
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
