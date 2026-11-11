using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Bmff;

/// <summary>One box of an ISO BMFF structure: its type, where it starts, where its payload starts and where it ends.</summary>
internal readonly record struct BmffBox(uint Type, int Start, int Payload, int End)
{
    /// <summary>The number of payload bytes.</summary>
    internal int Length => End - Payload;
}

/// <summary>
/// The box walk of the readers that share the ISO BMFF framing without sharing the item model of
/// HEIF: Canon CR3 and the JPEG XL container. The rules are those of the HEIF reader. Every size is
/// validated against the range that encloses the box before it is used, every box counts against
/// <see cref="MetadataLimits.MaxSegments"/> and every level against
/// <see cref="MetadataLimits.MaxDirectoryDepth"/>, so malformed input is a
/// <see cref="MetadataException"/> and never an index, overflow or allocation failure.
/// </summary>
internal ref struct BmffBoxWalk
{
    private readonly ReadOnlySpan<byte> data;
    private readonly MetadataLimits limits;
    private int boxes;

    internal BmffBoxWalk(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        this.data = data;
        this.limits = limits;
        boxes = 0;
    }

    /// <summary>
    /// Reads the header of the box at <paramref name="position"/> and moves the position past the box.
    /// A size of one is followed by a 64-bit size; a size of zero runs to <paramref name="end"/>.
    /// </summary>
    internal BmffBox Next(ref int position, int end, int depth)
    {
        if (depth > limits.MaxDirectoryDepth)
            throw Limit("The box nesting budget was exceeded.", position);
        if (++boxes > limits.MaxSegments)
            throw Limit("The box count budget was exceeded.", position);
        int start = position;
        int available = end - start;
        if (available < 8) throw Fail("A box header is truncated.", start);
        uint size32 = U32(start);
        uint type = U32(start + 4);
        int header = 8;
        long size = size32;
        if (size32 == 1)
        {
            if (available < 16) throw Fail("An extended box header is truncated.", start);
            ulong large = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(start + 8, 8));
            if (large > (ulong)available) throw Fail("A box size exceeds the box that encloses it.", start);
            size = (long)large;
            header = 16;
        }
        else if (size32 == 0)
        {
            size = available;
        }
        if (size < header) throw Fail("A box size is smaller than its own header.", start);
        if (size > available) throw Fail("A box size exceeds the box that encloses it.", start);
        position = start + (int)size;
        return new BmffBox(type, start, start + header, position);
    }

    internal readonly uint U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));

    internal readonly MetadataException Fail(string message, long offset) =>
        new(MetadataErrorCode.InvalidBmff, message, offset >= 0 && offset <= data.Length ? offset : null);

    internal readonly MetadataException Limit(string message, long offset) =>
        new(MetadataErrorCode.LimitExceeded, message, offset >= 0 && offset <= data.Length ? offset : null);
}
