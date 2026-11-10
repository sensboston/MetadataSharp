using System.Buffers.Binary;
using MetadataSharp.Core.Binary;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>
/// Recognises a MakerNote by camera make and signature and decodes it with the vendor's layout.
/// Best effort: any structural problem returns null and the raw MakerNote value stays authoritative.
/// </summary>
internal static class MakerNoteReader
{
    private static ReadOnlySpan<byte> NikonSignature => "Nikon\0"u8;

    internal static MakerNote? TryRead(ReadOnlySpan<byte> tiff, int start, int length, Endianness order,
        ReadOnlySpan<byte> make, MetadataLimits limits, long retainedBudget)
    {
        if (start < 0 || length < 2 || start > tiff.Length - length)
            return null;
        ReadOnlySpan<byte> note = tiff.Slice(start, length);
        if (make.StartsWith("Canon"u8))
            return CanonMakerNoteReader.TryRead(tiff, start, length, order, limits, retainedBudget);
        int end = start + length;
        if (make.StartsWith("FUJIFILM"u8))
        {
            // "FUJIFILM", then a little-endian offset of the IFD; all offsets count from the MakerNote.
            if (!note.StartsWith("FUJIFILM"u8) || length < 12)
                return null;
            uint ifd = BinaryPrimitives.ReadUInt32LittleEndian(note[8..]);
            if (ifd < 12 || ifd >= (uint)length)
                return null;
            return Directory(MakerNoteFormat.Fujifilm, tiff, start + (int)ifd, end, start, Endianness.LittleEndian, 0,
                limits, retainedBudget, VendorMakerNoteNames.Fujifilm, null);
        }
        if (make.StartsWith("PENTAX"u8) || make.StartsWith("RICOH"u8))
        {
            // "PENTAX \0" + two bytes: the IFD at 10 in the TIFF's byte order (as with "AOC", the two
            // bytes are not a reliable byte-order mark), offsets from the MakerNote.
            if (note.StartsWith("PENTAX \0"u8) && length >= 10 + 18)
                return Directory(MakerNoteFormat.PentaxDng, tiff, start + 10, end, start, order, 0,
                    limits, retainedBudget, VendorMakerNoteNames.Pentax, null);
            // "AOC\0" + two bytes, then the IFD at 6 in the TIFF's byte order (the two bytes are not a
            // reliable byte-order mark); offsets from the TIFF header.
            if (note.StartsWith("AOC\0"u8) && length >= 6 + 18)
                return Directory(MakerNoteFormat.Pentax, tiff, start + 6, end, 0, order, 0,
                    limits, retainedBudget, VendorMakerNoteNames.Pentax, null);
            return null;
        }
        if (make.StartsWith("SONY"u8))
        {
            // "SONY DSC \0\0\0" then an IFD, or a bare IFD; offsets from the TIFF header either way.
            if (note.StartsWith("SONY DSC \0\0\0"u8))
                return Directory(MakerNoteFormat.Sony1, tiff, start + 12, end, 0, order, 0,
                    limits, retainedBudget, VendorMakerNoteNames.Sony, null);
            return Directory(MakerNoteFormat.Sony2, tiff, start, end, 0, order, 0,
                limits, retainedBudget, VendorMakerNoteNames.Sony, null);
        }
        if (make.StartsWith("Panasonic"u8))
        {
            // "Panasonic\0\0\0" then an IFD without a next-IFD pointer; offsets from the TIFF header.
            if (!note.StartsWith("Panasonic\0\0\0"u8))
                return null;
            return Directory(MakerNoteFormat.Panasonic, tiff, start + 12, end, 0, order, 0,
                limits, retainedBudget, VendorMakerNoteNames.Panasonic, null);
        }
        if (make.StartsWith("NIKON"u8) || make.StartsWith("Nikon"u8))
        {
            if (note.StartsWith(NikonSignature) && note.Length >= 18 && note[6] == 0x02)
                return Nikon3(tiff, start, length, limits, retainedBudget);
            if (note.StartsWith(NikonSignature) && note.Length >= 10 && note[6] == 0x01 && note[7] == 0x00)
                return Directory(MakerNoteFormat.Nikon2, tiff, start + 8, start + length, 0, order, 0, limits,
                    retainedBudget, NikonMakerNoteNames.Nikon2, null);
            if (!note.StartsWith(NikonSignature))
                return Directory(MakerNoteFormat.Nikon1, tiff, start, start + length, 0, order, 0, limits,
                    retainedBudget, NikonMakerNoteNames.Nikon1, null);
        }
        return null;
    }

    /// <summary>"Nikon\0", a two-byte version, two bytes, then a TIFF header that the IFD and its
    /// value offsets are relative to; that header carries its own byte order.</summary>
    private static MakerNote? Nikon3(ReadOnlySpan<byte> tiff, int start, int length, MetadataLimits limits,
        long retainedBudget)
    {
        int header = start + 10;
        ReadOnlySpan<byte> h = tiff.Slice(header, 8);
        Endianness order;
        if (h[0] == 0x49 && h[1] == 0x49 && h[2] == 42 && h[3] == 0) order = Endianness.LittleEndian;
        else if (h[0] == 0x4D && h[1] == 0x4D && h[2] == 0 && h[3] == 42) order = Endianness.BigEndian;
        else return null;
        uint ifd = order == Endianness.LittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(h[4..])
            : BinaryPrimitives.ReadUInt32BigEndian(h[4..]);
        if (ifd < 8 || ifd >= (uint)(length - 10))
            return null;
        return Directory(MakerNoteFormat.Nikon3, tiff, header + (int)ifd, start + length, header, order, 0,
            limits, retainedBudget, NikonMakerNoteNames.Nikon3, null);
    }

    /// <summary>
    /// Reads one IFD at <paramref name="ifd"/>. External value offsets are relative to
    /// <paramref name="offsetBase"/>, corrected by <paramref name="delta"/>, and must lie in
    /// [<paramref name="offsetBase"/>, <paramref name="regionEnd"/>) when the base is inside the
    /// MakerNote, or anywhere in the TIFF payload when the base is the TIFF header (0).
    /// </summary>
    internal static MakerNote? Directory(MakerNoteFormat format, ReadOnlySpan<byte> tiff, int ifd, int regionEnd,
        int offsetBase, Endianness order, long delta, MetadataLimits limits, long retainedBudget,
        Func<ushort, string?> name, Func<ushort, ExifValue, IReadOnlyList<MakerNoteField>>? split)
    {
        bool little = order == Endianness.LittleEndian;
        int lowest = offsetBase == 0 ? 0 : offsetBase;
        int highest = offsetBase == 0 ? tiff.Length : regionEnd;
        if (ifd < lowest || ifd > highest - 2)
            return null;
        int count = U16(tiff, ifd, little);
        if (count == 0 || count > limits.MaxEntriesPerDirectory || 2L + count * 12L > highest - ifd)
            return null;
        var entries = new List<MakerNoteEntry>(count);
        var seen = new HashSet<ushort>();
        long decoded = 0;
        for (int i = 0; i < count; i++)
        {
            int p = ifd + 2 + i * 12;
            ushort tag = U16(tiff, p, little);
            ushort typeCode = U16(tiff, p + 2, little);
            uint valueCount = U32(tiff, p + 4, little);
            if (typeCode is < 1 or > 12)
                continue; // Not a TIFF field type; vendor-private.
            var type = (TiffFieldType)typeCode;
            if (!CheckedRange.TryMultiply(valueCount, ExifValue.GetWidth(type), out int byteLength) ||
                byteLength > limits.MaxValueBytes)
                return null;
            long valueStart = p + 8;
            if (byteLength > 4)
            {
                valueStart = offsetBase + (long)U32(tiff, p + 8, little) + delta;
                if (valueStart < lowest || valueStart > highest - byteLength)
                    return null;
            }
            decoded += 2L * byteLength; // The value and, at most, its split fields.
            if (decoded > retainedBudget)
                return null;
            if (!seen.Add(tag))
                continue;
            ExifValue value = ExifValue.FromRaw(type, valueCount, order, tiff.Slice((int)valueStart, byteLength));
            entries.Add(new MakerNoteEntry(tag, name(tag), value, split?.Invoke(tag, value) ?? []));
        }
        return new MakerNote(format, order, entries);
    }

    private static ushort U16(ReadOnlySpan<byte> s, int p, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(p, 2))
        : BinaryPrimitives.ReadUInt16BigEndian(s.Slice(p, 2));

    private static uint U32(ReadOnlySpan<byte> s, int p, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(p, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(s.Slice(p, 4));

    /// <summary>Binary search in an ascending (Id, Name) table.</summary>
    internal static string? Lookup((ushort Id, string Name)[] table, ushort tag)
    {
        int lo = 0, hi = table.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            ushort id = table[mid].Id;
            if (id == tag) return table[mid].Name;
            if (id < tag) lo = mid + 1; else hi = mid - 1;
        }
        return null;
    }
}
