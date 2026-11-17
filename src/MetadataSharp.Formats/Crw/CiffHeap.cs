using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Crw;

/// <summary>One record of the heap of a CRW file: a value, or a directory of records.</summary>
internal sealed class CiffRecord
{
    // Capture validated inputs and stable state for this CIFF object.
    internal CiffRecord(ushort tag, byte[] data, List<CiffRecord>? children = null)
    {
        Tag = tag;
        Data = data;
        Children = children;
    }

    /// <summary>The tag as the entry has it: with the bits that say where the value lies and of what type it is.</summary>
    internal ushort Tag { get; set; }

    /// <summary>The bytes of a value; the eight bytes of the entry when the value lies there. Empty for a directory.</summary>
    internal byte[] Data { get; set; }

    /// <summary>The records of a directory; null for a value.</summary>
    internal List<CiffRecord>? Children { get; }

    internal ushort Id => (ushort)(Tag & 0x3FFF);

    internal bool InEntry => (Tag & 0xC000) == 0x4000;

    /// <summary>Gives the record a value. A value longer than an entry moves to the heap, as the reference moves it.</summary>
    internal void Set(byte[] value, bool toHeap = false)
    {
        if (InEntry && (value.Length > 8 || toHeap)) Tag = Id;
        if (InEntry && value.Length < 8)
        {
            byte[] padded = new byte[8];
            value.CopyTo(padded, 0);
            value = padded;
        }
        Data = value;
    }
}

/// <summary>
/// The heap of a CRW file as a tree, for the writer: read whole, changed, and written again in the
/// layout the reference writes (Exiv2 0.28.9, <c>CiffDirectory::doWrite</c>): in each directory the
/// values in the order of the entries, each padded to an even length, then the entries, then where
/// the entries start. Every record the edit does not name keeps its bytes and its place among the
/// entries; where its value lies in the file may change, which is all an offset of this format says.
/// </summary>
internal static class CiffHeap
{
    // Read CIFF data only after the input extent and applicable resource budgets are checked.
    internal static CiffRecord Parse(ReadOnlySpan<byte> data, MetadataLimits limits, out bool little, out int heap)
    {
        if (!CrwReader.HasSignature(data)) throw Fail("The file does not start with the header of a CRW file.", 0);
        little = data[0] == 0x49;
        uint start = little ? BinaryPrimitives.ReadUInt32LittleEndian(data[2..]) : BinaryPrimitives.ReadUInt32BigEndian(data[2..]);
        if (start < 14 || start > (uint)data.Length) throw Fail("The header of the CRW file leads outside the file.", 2);
        heap = (int)start;
        int records = 0;
        return new CiffRecord(0, [], Directory(data, heap, data.Length - heap, little, limits, 0, ref records));
    }

    // Keep Directory focused on CIFF and its format-specific validation contract.
    private static List<CiffRecord> Directory(ReadOnlySpan<byte> data, int start, int size, bool little, MetadataLimits limits, int depth, ref int records)
    {
        if (depth > limits.MaxDirectoryDepth)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The directories of the CRW file are nested deeper than the budget allows.", start);
        if (size < 4) throw Fail("A directory of the CRW file is too short.", start);
        uint at = U32(data, start + size - 4, little);
        if (at > (uint)(size - 2)) throw Fail("The entries of a directory of the CRW file lie outside it.", start + size - 4);
        int count = U16(data, start + (int)at, little);
        long first = at + 2;
        if (count * 10L > size - first) throw Fail("A directory of the CRW file has more entries than it has room for.", start + at);
        var children = new List<CiffRecord>(count);
        for (int i = 0; i < count; i++)
        {
            if (++records > limits.MaxTotalEntries)
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "The CRW file exceeds the entry budget.", start + first);
            long entry = first + (i * 10L);
            ushort tag = U16(data, start + (int)entry, little);
            bool directory = (tag & 0x3800) is 0x2800 or 0x3000;
            switch (tag & 0xC000)
            {
                case 0x0000:
                    long length = U32(data, start + (int)entry + 2, little), offset = U32(data, start + (int)entry + 6, little);
                    bool fits = offset < entry
                        ? length <= entry - offset
                        : offset >= entry + 10 && offset <= size && length <= size - offset;
                    if (!fits) throw Fail("A record of the CRW file lies outside its directory or over its own entry.", start + entry);
                    int place = start + (int)offset;
                    children.Add(directory
                        ? new CiffRecord(tag, [], Directory(data, place, (int)length, little, limits, depth + 1, ref records))
                        : new CiffRecord(tag, data.Slice(place, (int)length).ToArray()));
                    break;
                case 0x4000:
                    if (directory)
                        throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "A directory of the CRW file lies in its own entry: such a file is not written.", start + entry);
                    children.Add(new CiffRecord(tag, data.Slice(start + (int)entry + 2, 8).ToArray()));
                    break;
                default:
                    throw Fail("An entry of the CRW file says neither that its data lies in the entry nor that it lies in the heap.", start + entry);
            }
        }
        return children;
    }

    /// <summary>The file: the bytes before the heap as they are, then the root directory.</summary>
    internal static byte[] Write(ReadOnlySpan<byte> header, CiffRecord root, bool little)
    {
        byte[] heap = Serialize(root, little);
        byte[] output = new byte[header.Length + heap.Length];
        header.CopyTo(output);
        heap.CopyTo(output, header.Length);
        return output;
    }

    // Serialize CIFF in its defined byte order without disturbing adjacent data.
    private static byte[] Serialize(CiffRecord directory, bool little)
    {
        List<CiffRecord> children = directory.Children!;
        if (children.Count > ushort.MaxValue)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "A directory of a CRW file holds at most 65535 records.");
        var blob = new MemoryStream();
        var places = new (uint Length, uint Offset)[children.Count];
        for (int i = 0; i < children.Count; i++)
        {
            CiffRecord child = children[i];
            if (child.InEntry) continue;
            byte[] value = child.Children is null ? child.Data : Serialize(child, little);
            places[i] = ((uint)value.Length, (uint)blob.Position);
            blob.Write(value);
            if ((value.Length & 1) != 0) blob.WriteByte(0);
            if (blob.Position > int.MaxValue / 2)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "The CRW file would be longer than this library writes.");
        }
        uint table = (uint)blob.Position;
        Span<byte> buffer = stackalloc byte[10];
        Put16(buffer, (ushort)children.Count, little);
        blob.Write(buffer[..2]);
        for (int i = 0; i < children.Count; i++)
        {
            CiffRecord child = children[i];
            buffer.Clear();
            Put16(buffer, child.Tag, little);
            if (child.InEntry) child.Data.AsSpan(0, Math.Min(8, child.Data.Length)).CopyTo(buffer[2..]);
            else
            {
                Put32(buffer[2..], places[i].Length, little);
                Put32(buffer[6..], places[i].Offset, little);
            }
            blob.Write(buffer);
        }
        Put32(buffer, table, little);
        blob.Write(buffer[..4]);
        return blob.ToArray();
    }

    /// <summary>The record of a tag in the directory a path of directory tags leads to, or null.</summary>
    internal static CiffRecord? Find(CiffRecord root, ReadOnlySpan<ushort> path, ushort id)
    {
        CiffRecord? directory = root;
        foreach (ushort tag in path)
        {
            directory = directory.Children!.FirstOrDefault(child => child.Children is not null && child.Tag == tag);
            if (directory is null) return null;
        }
        return directory.Children!.FirstOrDefault(child => child.Children is null && child.Id == id);
    }

    /// <summary>As <see cref="Find"/>; directories and the record that are not there are added at the end of their directory.</summary>
    internal static CiffRecord Ensure(CiffRecord root, ReadOnlySpan<ushort> path, ushort id)
    {
        CiffRecord directory = root;
        foreach (ushort tag in path)
        {
            CiffRecord? next = directory.Children!.FirstOrDefault(child => child.Children is not null && child.Tag == tag);
            if (next is null)
            {
                next = new CiffRecord(tag, [], []);
                directory.Children!.Add(next);
            }
            directory = next;
        }
        CiffRecord? record = directory.Children!.FirstOrDefault(child => child.Children is null && child.Id == id);
        if (record is null)
        {
            record = new CiffRecord(id, []);
            directory.Children!.Add(record);
        }
        return record;
    }

    /// <summary>Removes the record, and each directory of the path that it leaves empty.</summary>
    internal static void Remove(CiffRecord root, ReadOnlySpan<ushort> path, ushort id)
    {
        var chain = new List<CiffRecord> { root };
        foreach (ushort tag in path)
        {
            CiffRecord? next = chain[^1].Children!.FirstOrDefault(child => child.Children is not null && child.Tag == tag);
            if (next is null) return;
            chain.Add(next);
        }
        chain[^1].Children!.RemoveAll(child => child.Children is null && child.Id == id);
        for (int i = chain.Count - 1; i > 0 && chain[i].Children!.Count == 0; i--)
            chain[i - 1].Children!.Remove(chain[i]);
    }

    /// <summary>Whether two trees hold the same records with the same bytes in the same order.</summary>
    internal static bool Same(CiffRecord left, CiffRecord right)
    {
        if (left.Tag != right.Tag || (left.Children is null) != (right.Children is null)) return false;
        if (left.Children is null) return left.Data.AsSpan().SequenceEqual(right.Data);
        if (left.Children.Count != right.Children!.Count) return false;
        for (int i = 0; i < left.Children.Count; i++)
            if (!Same(left.Children[i], right.Children[i])) return false;
        return true;
    }

    // Preserve the format-specific error code and source offset for malformed CIFF data.
    private static MetadataException Fail(string message, long offset) => new(MetadataErrorCode.InvalidCrw, message, offset);

    private static ushort U16(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt16BigEndian(data[at..]);

    private static uint U32(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

    private static void Put16(Span<byte> at, ushort value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(at, value); else BinaryPrimitives.WriteUInt16BigEndian(at, value);
    }

    private static void Put32(Span<byte> at, uint value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(at, value); else BinaryPrimitives.WriteUInt32BigEndian(at, value);
    }
}
