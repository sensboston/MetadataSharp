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
        ReadOnlySpan<byte> make, ReadOnlySpan<byte> model, MetadataLimits limits, long retainedBudget)
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
            MakerNote? sony = note.StartsWith("SONY DSC \0\0\0"u8)
                ? Directory(MakerNoteFormat.Sony1, tiff, start + 12, end, 0, order, 0, limits, retainedBudget, VendorMakerNoteNames.Sony, null)
                : Directory(MakerNoteFormat.Sony2, tiff, start, end, 0, order, 0, limits, retainedBudget, VendorMakerNoteNames.Sony, null);
            return sony is null ? null : SplitSony(sony, model);
        }
        if (make.StartsWith("Panasonic"u8))
        {
            // "Panasonic\0\0\0" then an IFD without a next-IFD pointer; offsets from the TIFF header.
            if (!note.StartsWith("Panasonic\0\0\0"u8))
                return null;
            return Directory(MakerNoteFormat.Panasonic, tiff, start + 12, end, 0, order, 0,
                limits, retainedBudget, VendorMakerNoteNames.Panasonic, null);
        }
        if (make.StartsWith("OLYMPUS"u8) || make.StartsWith("OM Digital"u8))
            return Olympus(tiff, start, length, order, note, limits, retainedBudget);
        if (make.StartsWith("NIKON"u8) || make.StartsWith("Nikon"u8))
        {
            if (note.StartsWith(NikonSignature) && note.Length >= 18 && note[6] == 0x02)
                return Nikon3(tiff, start, length, model, limits, retainedBudget);
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
    private static MakerNote? Nikon3(ReadOnlySpan<byte> tiff, int start, int length, ReadOnlySpan<byte> model,
        MetadataLimits limits, long retainedBudget)
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
        MakerNote? note = Directory(MakerNoteFormat.Nikon3, tiff, header + (int)ifd, start + length, header, order, 0,
            limits, retainedBudget, NikonMakerNoteNames.Nikon3, NikonMakerNoteArrays.Decompose);
        return note is null ? null : DecryptNikon3(note, model);
    }

    /// <summary>
    /// Olympus: "OLYMP" + 3 bytes, then an IFD with offsets from the TIFF header; or "OLYMPUS" + 0 +
    /// byte order + 2 bytes (or "OM SYSTEM" + 3 zeros + byte order + 2 bytes), then an IFD whose
    /// offsets, including those of its sub-directories, count from the MakerNote.
    /// </summary>
    private static MakerNote? Olympus(ReadOnlySpan<byte> tiff, int start, int length, Endianness order,
        ReadOnlySpan<byte> note, MetadataLimits limits, long retainedBudget)
    {
        int end = start + length;
        if (note.StartsWith("OLYMPUS\0"u8) && length >= 12 + 18 && OrderMark(note.Slice(8, 2)) is Endianness o2)
            return OlympusSubDirectories(Directory(MakerNoteFormat.Olympus2, tiff, start + 12, end, start, o2, 0,
                limits, retainedBudget, OlympusMakerNoteNames.Main, null), tiff, start, end, limits, retainedBudget);
        if (note.StartsWith("OM SYSTEM\0\0\0"u8) && length >= 16 + 18 && OrderMark(note.Slice(12, 2)) is Endianness om)
            return OlympusSubDirectories(Directory(MakerNoteFormat.OMSystem, tiff, start + 16, end, start, om, 0,
                limits, retainedBudget, OlympusMakerNoteNames.Main, null), tiff, start, end, limits, retainedBudget);
        if (note.StartsWith("OLYMP\0"u8) && length >= 8 + 18)
            return Directory(MakerNoteFormat.Olympus, tiff, start + 8, end, 0, order, 0,
                limits, retainedBudget, OlympusMakerNoteNames.Main, null);
        return null;
    }

    /// <summary>Reads the sub-directories that the Olympus2 tags 0x2010-0x3000 point to, as their fields.</summary>
    private static MakerNote? OlympusSubDirectories(MakerNote? note, ReadOnlySpan<byte> tiff, int start, int end,
        MetadataLimits limits, long retainedBudget)
    {
        if (note is null)
            return null;
        var entries = new List<MakerNoteEntry>(note.Entries.Count);
        bool changed = false;
        foreach (MakerNoteEntry entry in note.Entries)
        {
            Func<ushort, string?>? names = entry.TagId switch
            {
                0x2010 => OlympusMakerNoteNames.Eq,
                0x2020 => OlympusMakerNoteNames.Cs,
                0x2030 => OlympusMakerNoteNames.Rd,
                0x2031 => OlympusMakerNoteNames.Rd2,
                0x2040 => OlympusMakerNoteNames.Ip,
                0x2050 => OlympusMakerNoteNames.Fi,
                0x3000 => OlympusMakerNoteNames.Ri,
                _ => null
            };
            MakerNote? sub = null;
            if (names is not null && entry.Value.FieldType == TiffFieldType.Long && entry.Value.Count == 1)
            {
                ReadOnlySpan<byte> raw = entry.Value.RawBytes;
                uint offset = note.ByteOrder == Endianness.LittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(raw) : BinaryPrimitives.ReadUInt32BigEndian(raw);
                if (offset < (uint)(end - start))
                    sub = Directory(note.Format, tiff, start + (int)offset, end, start, note.ByteOrder, 0, limits,
                        retainedBudget, names, null);
            }
            if (sub is null)
            {
                entries.Add(entry);
                continue;
            }
            var fields = new List<MakerNoteField>(sub.Entries.Count);
            foreach (MakerNoteEntry field in sub.Entries)
                fields.Add(new MakerNoteField(field.TagId, field.Name ?? "0x" + field.TagId.ToString("x4"), field.Value));
            entries.Add(new MakerNoteEntry(entry.TagId, entry.Name, entry.Value, fields));
            changed = true;
        }
        return changed ? new MakerNote(note.Format, note.ByteOrder, entries) : note;
    }

    private static Endianness? OrderMark(ReadOnlySpan<byte> mark) =>
        mark[0] == 0x49 && mark[1] == 0x49 ? Endianness.LittleEndian :
        mark[0] == 0x4D && mark[1] == 0x4D ? Endianness.BigEndian : null;

    /// <summary>Splits the Sony array tags, whose layouts may depend on the camera model.</summary>
    private static MakerNote SplitSony(MakerNote note, ReadOnlySpan<byte> model)
    {
        var entries = new List<MakerNoteEntry>(note.Entries.Count);
        bool changed = false;
        foreach (MakerNoteEntry entry in note.Entries)
        {
            IReadOnlyList<MakerNoteField> fields = SonyMakerNoteArrays.Decompose(entry.TagId, entry.Value, model);
            entries.Add(fields.Count == 0 ? entry : new MakerNoteEntry(entry.TagId, entry.Name, entry.Value, fields));
            changed |= fields.Count != 0;
        }
        return changed ? new MakerNote(note.Format, note.ByteOrder, entries) : note;
    }

    /// <summary>Splits the encrypted Nikon3 arrays when the serial number and shutter count are present.</summary>
    private static MakerNote DecryptNikon3(MakerNote note, ReadOnlySpan<byte> model)
    {
        if (!note.TryGetEntry(0x00A7, out MakerNoteEntry? count) || count.Value.FieldType != TiffFieldType.Long ||
            count.Value.Count == 0 || !note.TryGetEntry(0x001D, out MakerNoteEntry? serial) ||
            serial.Value.FieldType != TiffFieldType.Ascii)
            return note;
        ReadOnlySpan<byte> c = count.Value.RawBytes;
        uint shutterCount = note.ByteOrder == Endianness.LittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(c) : BinaryPrimitives.ReadUInt32BigEndian(c);
        if (NikonMakerNoteCipher.SerialKey(serial.Value.RawBytes, model) is not uint key)
            return note;
        var entries = new List<MakerNoteEntry>(note.Entries.Count);
        bool changed = false;
        foreach (MakerNoteEntry entry in note.Entries)
        {
            if (entry.Fields.Count == 0 && entry.TagId is 0x0091 or 0x0097 or 0x0098 &&
                NikonMakerNoteArrays.DecomposeEncrypted(entry.TagId, entry.Value, shutterCount, key) is { Count: > 0 } fields)
            {
                entries.Add(new MakerNoteEntry(entry.TagId, entry.Name, entry.Value, fields));
                changed = true;
            }
            else
                entries.Add(entry);
        }
        return changed ? new MakerNote(note.Format, note.ByteOrder, entries) : note;
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
            if (typeCode is < 1 or > 13)
                continue; // Not a TIFF field type; vendor-private.
            // Type 13 (IFD) is a LONG offset of a sub-directory.
            var type = typeCode == 13 ? TiffFieldType.Long : (TiffFieldType)typeCode;
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
