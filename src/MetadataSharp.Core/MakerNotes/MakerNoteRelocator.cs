using System.Buffers.Binary;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>Copy-only relocation checks for a MakerNote that is about to move to another TIFF offset.</summary>
internal static class MakerNoteRelocator
{
    internal static bool CanRelocate(MakerNote note, ExifValue raw, ExifMetadata exif, out string reason)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(exif);
        reason = string.Empty;
        if (raw.ByteLength == 0 || note.Offset < 0 || note.Offset > int.MaxValue)
            return Refuse("The raw MakerNote range is not addressable.", out reason);
        int start = (int)note.Offset, end = start + raw.ByteLength;
        if (end < start) return Refuse("The raw MakerNote range overflows.", out reason);

        List<MakerNoteSlot> slots = Slots(note).ToList();
        if (slots.Count == 0 || slots.Any(s => s.Directory < start || s.Directory > end - 2 || s.Entry < start || s.Entry > end - 12))
            return Refuse("The decoded MakerNote has entries without relocatable directory locations.", out reason);
        foreach (MakerNoteSlot slot in slots)
        {
            if (slot.ByteLength < 0 || slot.Value < start || slot.Value > end - slot.ByteLength)
                return Refuse("A MakerNote value points outside its own bytes.", out reason);
            if (slot.OffsetBase != 0 && (slot.OffsetBase < start || slot.OffsetBase >= end))
                return Refuse("A MakerNote offset base is outside the note.", out reason);
            if (slot.Delta != 0)
                return Refuse("The MakerNote uses an offset correction that is not rebuilt by the copier.", out reason);
        }

        // A known external image/directory pointer, or an unnamed address-like value, is not proven
        // relocatable by the decoded directory walk. Nikon's two cipher keys are the exception: the
        // complete note is copied, and the normal EXIF family copy carries Make and Model too.
        foreach (MakerNoteTarget target in MakerNoteKeys.Enumerate(note))
        {
            if (note.Format == MakerNoteFormat.Nikon3 && target.Field is null && target.Entry.TagId is 0x001D or 0x00A7)
                continue;
            if (MakerNoteKeys.IsPointer(note.Format, target))
                return Refuse($"{target.Key} addresses data, a directory, or a MakerNote cipher key outside the relocatable values.", out reason);
            if (target.Field is null && target.Entry.Name is null &&
                target.Value.FieldType is TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Undefined)
                return Refuse($"Unnamed address-like MakerNote tag 0x{target.Entry.TagId:x4} is not relocated.", out reason);
        }

        // Every entry in every decoded IFD must be represented by a slot; otherwise an opaque entry
        // could contain an address that a byte-for-byte relocation would stale.
        foreach (IGrouping<int, MakerNoteSlot> directory in slots.GroupBy(s => s.Directory))
        {
            int relative = directory.Key - start;
            if (relative < 0 || relative > raw.ByteLength - 2)
                return Refuse("A MakerNote directory lies outside its byte stream.", out reason);
            ReadOnlySpan<byte> bytes = raw.RawBytes;
            ushort count = U16(bytes, relative, directory.First().Little);
            if (count != directory.Count())
                return Refuse("A MakerNote directory contains entries that were not decoded.", out reason);
            long tableEnd = relative + 2L + count * 12L;
            bool hasNext = note.Format != MakerNoteFormat.Panasonic;
            if (hasNext && tableEnd + 4 <= bytes.Length)
            {
                uint next = U32(bytes, (int)tableEnd, directory.First().Little);
                if (next != 0)
                    return Refuse("A MakerNote next-directory pointer cannot be proven local.", out reason);
            }
        }

        if (note.Format == MakerNoteFormat.Nikon3 && note.Entries.Any(e => e.TagId is 0x0091 or 0x0097 or 0x0098))
        {
            if (!note.TryGetEntry(0x00A7, out MakerNoteEntry? shutter) || shutter.Value.FieldType != TiffFieldType.Long || shutter.Value.Count == 0)
                return Refuse("Encrypted Nikon data has no copied shutter-count key.", out reason);
            bool serialPresent = note.TryGetEntry(0x001D, out MakerNoteEntry? serial) && serial.Value.FieldType == TiffFieldType.Ascii;
            bool modelPresent = exif.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x0110), out ExifEntry? model) && model.Value.FieldType == TiffFieldType.Ascii;
            if (!serialPresent && !modelPresent)
                return Refuse("Encrypted Nikon data has no copied serial-number or model key.", out reason);
        }
        return true;
    }

    internal static byte[] Relocate(MakerNote note, ExifValue raw, int destinationStart)
    {
        int sourceStart = checked((int)note.Offset);
        if (destinationStart < 0) throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "The new MakerNote offset is invalid.");
        byte[] bytes = raw.RawBytes.ToArray();
        if (note.Format == MakerNoteFormat.Canon && bytes.Length >= 8)
        {
            int footer = bytes.Length - 8;
            bool little = note.ByteOrder == Endianness.LittleEndian;
            bool marked = little
                ? bytes[footer] == 0x49 && bytes[footer + 1] == 0x49 && bytes[footer + 2] == 0x2A && bytes[footer + 3] == 0
                : bytes[footer] == 0x4D && bytes[footer + 1] == 0x4D && bytes[footer + 2] == 0 && bytes[footer + 3] == 0x2A;
            if (marked) Put32(bytes, footer + 4, checked((uint)destinationStart), little);
        }

        foreach (MakerNoteSlot slot in Slots(note))
        {
            if (slot.OffsetBase != 0 || slot.ByteLength <= 4) continue;
            int at = checked(slot.Entry + 8 - sourceStart);
            long rebased = (long)destinationStart + slot.Value - sourceStart;
            if (at < 0 || at > bytes.Length - 4 || rebased < 0 || rebased > uint.MaxValue)
                throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "A MakerNote value offset cannot be represented at its new position.");
            Put32(bytes, at, (uint)rebased, slot.Little);
        }

        if (note.Format != MakerNoteFormat.Panasonic)
        {
            foreach (int directory in Slots(note).Select(s => s.Directory).Distinct())
            {
                MakerNoteSlot slot = Slots(note).First(s => s.Directory == directory);
                int relative = directory - sourceStart;
                int count = U16(raw.RawBytes, relative, slot.Little);
                int nextAt = relative + 2 + count * 12;
                if (nextAt > bytes.Length - 4) continue;
                uint next = U32(raw.RawBytes, nextAt, slot.Little);
                if (next == 0) continue;
                throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "A nonzero MakerNote next-directory pointer cannot be safely relocated.");
            }
        }
        return bytes;
    }

    internal static bool Same(MakerNote left, MakerNote right)
    {
        if (left.Format != right.Format || left.Entries.Count != right.Entries.Count) return false;
        for (int i = 0; i < left.Entries.Count; i++)
        {
            MakerNoteEntry a = left.Entries[i], b = right.Entries[i];
            if (a.TagId != b.TagId || a.Fields.Count != b.Fields.Count ||
                (a.Fields.Count == 0 && !Same(a.Value, b.Value))) return false;
            for (int j = 0; j < a.Fields.Count; j++)
                if (!Same(a.Fields[j], b.Fields[j])) return false;
        }
        return true;
    }

    private static bool Same(MakerNoteField left, MakerNoteField right)
    {
        if (left.Index != right.Index || left.Fields.Count != right.Fields.Count ||
            (left.Fields.Count == 0 && !Same(left.Value, right.Value))) return false;
        for (int i = 0; i < left.Fields.Count; i++)
            if (!Same(left.Fields[i], right.Fields[i])) return false;
        return true;
    }

    private static bool Same(ExifValue a, ExifValue b) => a.FieldType == b.FieldType && a.Count == b.Count && a.RawBytes.SequenceEqual(b.RawBytes);

    private static IEnumerable<MakerNoteSlot> Slots(MakerNote note)
    {
        foreach (MakerNoteEntry entry in note.Entries)
        {
            if (entry.Slot is MakerNoteSlot slot) yield return slot;
            foreach (MakerNoteField field in entry.Fields)
                foreach (MakerNoteSlot nested in FieldSlots(field)) yield return nested;
        }
    }

    private static IEnumerable<MakerNoteSlot> FieldSlots(MakerNoteField field)
    {
        if (field.Slot is MakerNoteSlot slot) yield return slot;
        foreach (MakerNoteField child in field.Fields)
            foreach (MakerNoteSlot nested in FieldSlots(child)) yield return nested;
    }

    private static bool Refuse(string message, out string reason) { reason = message; return false; }

    private static ushort U16(ReadOnlySpan<byte> data, int at, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(at, 2))
        : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));

    private static uint U32(ReadOnlySpan<byte> data, int at, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));

    private static void Put32(Span<byte> data, int at, uint value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(at, 4), value);
        else BinaryPrimitives.WriteUInt32BigEndian(data.Slice(at, 4), value);
    }
}
