using System.Buffers.Binary;
using MetadataSharp.Core.Operations;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>
/// Changes tags of the MakerNote of an EXIF block where they are.
/// </summary>
/// <remarks>
/// <para>The MakerNote keeps its offset and nothing in it moves. A value is written
/// into the bytes the old one has, or into the four bytes of its entry when it fits them; a value
/// that fits neither is appended, where the note's own way of addressing allows it (see <c>Grow</c>); a field of
/// an array into its bytes of the array, which is deciphered first and enciphered again when it is
/// stored so. A tag is removed by moving the entries after it up in their directory. Nothing is
/// written anywhere else, so no offset of the note, whatever it counts from, has to change.</para>
/// <para>The result is checked before it is returned. Every byte that differs from the source lies
/// in a range a change was planned for. The block is read again by the reader that read the source:
/// the tags outside the note read as before; in the note every tag, field and element that was not
/// named reads byte for byte as before, the named ones read as set, and the removed ones are gone.
/// This holds for every maker, so also for the notes whose offsets the reader corrects (Canon's
/// footer) or reads from sub-directories (Olympus, the Nikon preview, Sony's Minolta directory).</para>
/// </remarks>
internal static class MakerNoteWriter
{
    /// <summary>The block with the changes applied, in the order they were made.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a block without a MakerNote this library
    /// decodes, a key the note does not hold, a value of another type or longer than its place;
    /// <see cref="MetadataErrorCode.TagNotEditable"/> for an address, the length that goes with it or a
    /// key of a cipher; <see cref="MetadataErrorCode.UnsafeRelocation"/> when the output does not read
    /// back as planned; what the reader refuses the block with.
    /// </exception>
    internal static byte[] Apply(byte[] block, IReadOnlyList<ExifEdit.MakerNoteChange> changes, MetadataLimits limits) =>
        Apply(block, changes, limits, wholeFile: false).Output;

    /// <summary>The output and the ranges of the source that were planned to change.</summary>
    internal sealed record Result(byte[] Output, IReadOnlyList<(int Start, int Length)> Ranges);

    /// <summary>
    /// As <see cref="Apply(byte[], IReadOnlyList{ExifEdit.MakerNoteChange}, MetadataLimits)"/>; for
    /// <paramref name="wholeFile"/> the bytes are a TIFF-based file, read as one, in which every change
    /// lies inside the MakerNote and nothing grows.
    /// </summary>
    internal static Result Apply(byte[] block, IReadOnlyList<ExifEdit.MakerNoteChange> changes, MetadataLimits limits, bool wholeFile)
    {
        (ExifMetadata source, byte[]? picture) = ReadSource(block, limits, wholeFile);
        MakerNote first = source.MakerNote ?? throw Unsupported("The file has no MakerNote this library decodes: its tags cannot be changed.");
        byte[] output = (byte[])block.Clone();
        var allowed = new List<(int Start, int Length)>();
        var state = new State(wholeFile, Holder(source, first));
        // Path of what was named -> the value it has to read as afterwards; null for a removal.
        var planned = new Dictionary<string, ExifValue?>(StringComparer.Ordinal);

        foreach (ExifEdit.MakerNoteChange change in changes)
        {
            // Read again for every change: a removal moves the entries after it.
            MakerNote note = Read(output, limits, wholeFile).Metadata.MakerNote ?? throw Refuse("the MakerNote no longer reads after a change");
            MakerNoteTarget target = Find(note, change.Key);
            if (MakerNoteKeys.IsPointer(note.Format, target))
                throw new MetadataException(MetadataErrorCode.TagNotEditable,
                    $"{change.Key} addresses data or a directory, is the length of such data, or is a key of the cipher of the note: it is not edited as a value.");
            if (!change.Unchecked && IsUnnamedAddress(target))
                throw new MetadataException(MetadataErrorCode.TagNotEditable,
                    $"{change.Key} is a tag this library has no name for, of a type that may hold an address ({target.Value.FieldType}): it is edited only when the caller says so (allowUnnamed).");
            if (change.Value is null && change.Text is null)
            {
                Remove(output, note, target, change.Key, allowed);
                planned[target.Path] = null;
                continue;
            }
            ExifValue value = Value(change, target);
            if (target.Element is not null || (target.Field is not null && target.Field.Slot is null))
                SetField(output, target, value, change.Key, allowed);
            else
                SetTag(ref output, note, target, value, change.Key, allowed, state);
            planned[target.Path] = value;
        }

        Verify(block, source, picture, first, output, planned, allowed, limits, state);
        return new Result(output, allowed);
    }

    /// <summary>What a run keeps: how the bytes are read, where the note is held, and what was appended.</summary>
    private sealed class State(bool wholeFile, (ExifTagKey Key, int Start, int End)? holder)
    {
        internal bool WholeFile { get; } = wholeFile;
        /// <summary>The tag whose value is the note, and the range of that value.</summary>
        internal (ExifTagKey Key, int Start, int End)? Holder { get; } = holder;
        /// <summary>Bytes appended to the block.</summary>
        internal int Appended { get; set; }
        /// <summary>Of those, the bytes the note grew by.</summary>
        internal int NoteGrowth { get; set; }
    }

    /// <summary>The tag whose value is the note: MakerNote, or DNGPrivateData where a DNG keeps it there.</summary>
    private static (ExifTagKey Key, int Start, int End)? Holder(ExifMetadata source, MakerNote note)
    {
        foreach (ExifTagKey key in new[] { new ExifTagKey(ExifDirectoryKind.Exif, 0x927C), new ExifTagKey(ExifDirectoryKind.Ifd0, 0xC634) })
            if (source.TryGetEntry(key, out ExifEntry? entry) && entry.Value.ByteLength != 0 && note.Offset <= int.MaxValue - entry.Value.ByteLength)
                return (key, (int)note.Offset, (int)note.Offset + entry.Value.ByteLength);
        return null;
    }

    private static (ExifMetadata Metadata, byte[]? Thumbnail) ReadSource(byte[] block, MetadataLimits limits, bool wholeFile)
    {
        if (wholeFile) return (ExifTiffReader.InspectFile(block, limits), null);
        TiffReadResult read = ExifTiffReader.Read(block, limits);
        return (read.Metadata, read.Thumbnail);
    }

    private static (ExifMetadata Metadata, byte[]? Thumbnail) Read(byte[] block, MetadataLimits limits, bool wholeFile)
    {
        try { return ReadSource(block, limits, wholeFile); }
        catch (MetadataException failure) when (failure.Code is MetadataErrorCode.ExifTooLarge or MetadataErrorCode.LimitExceeded)
        {
            throw new MetadataException(failure.Code, "The block with the grown MakerNote value exceeds a limit: " + failure.Message);
        }
        catch (MetadataException) { throw Refuse("the reader refuses the block after a change"); }
    }

    /// <summary>
    /// True for a tag without a name in the tables of this library whose type is LONG, SLONG or
    /// UNDEFINED (IFD is read as LONG): it may be an address that the rules for named tags do not see.
    /// </summary>
    private static bool IsUnnamedAddress(MakerNoteTarget target)
    {
        if (target.Element is not null) return false;
        bool unnamed = target.Field is null
            ? target.Entry.Name is null
            : target.Field.Slot is not null && target.Field.Name.StartsWith("0x", StringComparison.Ordinal);
        return unnamed && target.Value.FieldType is TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Undefined;
    }

    private static MakerNoteTarget Find(MakerNote note, string key)
    {
        foreach (MakerNoteTarget target in MakerNoteKeys.Enumerate(note))
            if (target.Key == key) return target;
        throw Unsupported($"The MakerNote of the file ({MakerNoteKeys.Group(note.Format)}) holds no {key}: a tag that is not in the note is not added.");
    }

    /// <summary>The value to store: of the type the target has, in its byte order.</summary>
    private static ExifValue Value(ExifEdit.MakerNoteChange change, MakerNoteTarget target)
    {
        ExifValue stored = target.Value;
        ExifValue value;
        if (change.Value is not null) value = change.Value;
        else
        {
            try { value = ExifValueText.Parse(stored.FieldType, change.Text!); }
            catch (Exception refusal) when (refusal is ArgumentException or FormatException or OverflowException)
            {
                throw Unsupported($"{change.Key} is stored as {stored.FieldType}: the text given is not a value of that type ({refusal.Message}).");
            }
        }
        if (value.FieldType != stored.FieldType)
            throw Unsupported($"{change.Key} is stored as {stored.FieldType}: a value of the type {value.FieldType} is not written in its place.");
        return ExifEdit.ConvertOrder(value, stored.ByteOrder);
    }

    // ---- A field of an array --------------------------------------------------------------------------

    private static void SetField(byte[] output, MakerNoteTarget target, ExifValue value, string key, List<(int, int)> allowed)
    {
        // The array is the value of the tag, or, for an element, of a tag of a sub-directory.
        MakerNoteField field = target.Element ?? target.Field!;
        MakerNoteSlot slot = (target.Element is null ? target.Entry.Slot : target.Field!.Slot)
            ?? throw Unsupported($"Where {key} lies in the file is not known: it is not written.");
        bool enciphered = target.Element is null && target.Entry.Decode is not null;
        if (value.Count != field.Value.Count)
            throw Unsupported($"{key} is a field of {field.Value.Count} element(s) of an array: a value of {value.Count} is not written in its place.");
        if (slot.Value < 0 || slot.Value > output.Length - slot.ByteLength)
            throw Refuse("an array lies outside the block");
        byte[] storedBytes = output.AsSpan(slot.Value, slot.ByteLength).ToArray();
        byte[] plain = enciphered ? target.Entry.Decode!(storedBytes) : storedBytes;
        if (field.Offset < 0 || plain.Length != slot.ByteLength || field.Offset > plain.Length - value.ByteLength)
            throw Refuse("a field lies outside its array");
        value.RawBytes.CopyTo(plain.AsSpan(field.Offset));
        byte[] written = enciphered ? target.Entry.Encode!(plain) : plain;
        if (written.Length != slot.ByteLength) throw Refuse("an array changed its length when it was enciphered");
        written.CopyTo(output, slot.Value);
        allowed.Add((slot.Value, slot.ByteLength));
    }

    // ---- A tag of a directory -------------------------------------------------------------------------

    private static MakerNoteSlot SlotOf(MakerNoteTarget target, string key) =>
        (target.Field is null ? target.Entry.Slot : target.Field.Slot)
            ?? throw Unsupported($"Where {key} lies in the file is not known: it is not written.");

    private static void SetTag(ref byte[] output, MakerNote note, MakerNoteTarget target, ExifValue value, string key, List<(int, int)> allowed, State state)
    {
        MakerNoteSlot slot = SlotOf(target, key);
        CheckSlot(output, slot);
        int old = slot.ByteLength, length = value.ByteLength;
        bool shared = IsShared(note, slot);
        if (length > Math.Max(old, 4))
        {
            Grow(ref output, note, slot, value, key, shared, allowed, state);
            return;
        }
        int inline = slot.Entry + 8;
        if (length <= 4)
        {
            if (old > 4 && !shared) output.AsSpan(slot.Value, old).Clear();
            output.AsSpan(inline, 4).Clear();
            value.RawBytes.CopyTo(output.AsSpan(inline));
        }
        else
        {
            // Longer than four bytes, so the old value is too, and lies where its offset leads.
            value.RawBytes.CopyTo(output.AsSpan(slot.Value));
            if (!shared) output.AsSpan(slot.Value + length, old - length).Clear();
        }
        Put32(output, slot.Entry + 4, value.Count, slot.Little);
        allowed.Add((slot.Entry + 4, 8));
        allowed.Add((slot.Value, old));
    }

    /// <summary>
    /// A value longer than its place and than the four bytes of its entry. Nothing moves: the value is
    /// appended and the entry addresses it there.
    /// </summary>
    /// <remarks>
    /// <para>Where the note counts its offsets from the TIFF header, the value goes to the end of the
    /// EXIF block, after everything the block holds: the entry addresses it as it addresses any value,
    /// and the note keeps its length. A Canon note whose footer says it was moved is not grown: its
    /// offsets count from where it was.</para>
    /// <para>Where the note counts from itself (or from a header of its own), the value has to lie in
    /// the note, so it goes to the end of the note, and only when the note is the last thing of the
    /// block: then the note grows into nothing, and the count of the tag that holds it grows with it.</para>
    /// <para>In a TIFF-based file nothing is appended for the note.</para>
    /// </remarks>
    private static void Grow(ref byte[] output, MakerNote note, MakerNoteSlot slot, ExifValue value, string key, bool shared,
        List<(int, int)> allowed, State state)
    {
        int old = slot.ByteLength, length = value.ByteLength;
        string what = $"{key} holds {old} byte(s) and the value has {length}";
        if (state.WholeFile)
            throw Unsupported(what + ": in a TIFF-based file a MakerNote value that grows is not written, since nothing is appended for the note.");
        if (!MakerNoteKeys.Grows(note.Format))
            throw Unsupported(what + $": a value that grows is not written in a {MakerNoteKeys.Group(note.Format)} note, where the reference tool has not confirmed the result.");
        if (slot.Delta != 0)
            throw Unsupported(what + ": the offsets of this note count from where it was before it was moved (its footer says so), so no value is appended for it.");
        int at = output.Length + (output.Length & 1);
        int end = at + length + (length & 1);
        long stored;
        int holderEntry = -1;
        if (slot.OffsetBase == 0) stored = at;
        else
        {
            if (state.Holder is not { } holder || holder.Key.TagId != 0x927C || slot.RegionEnd != output.Length ||
                holder.End + state.NoteGrowth != output.Length)
                throw Unsupported(what + ": the offsets of this note count from the note, so a value that grows goes to its end, and the note is not the last thing in the EXIF block.");
            holderEntry = HolderEntry(output, holder.Start);
            if (holderEntry < 0)
                throw Unsupported(what + ": the entry of the MakerNote tag was not found where the block is expected to have it.");
            stored = at - (long)slot.OffsetBase;
        }
        if (stored < 0 || stored > uint.MaxValue || end < 0) throw Refuse("an appended value cannot be addressed");

        byte[] grown = new byte[end];
        output.CopyTo(grown, 0);
        value.RawBytes.CopyTo(grown.AsSpan(at));
        if (old > 4 && !shared)
        {
            grown.AsSpan(slot.Value, old).Clear();
            allowed.Add((slot.Value, old));
        }
        Put32(grown, slot.Entry + 4, value.Count, slot.Little);
        Put32(grown, slot.Entry + 8, (uint)stored, slot.Little);
        allowed.Add((slot.Entry + 4, 8));
        int added = end - output.Length;
        if (holderEntry >= 0)
        {
            bool little = grown[0] == 0x49;
            uint count = Get32(grown, holderEntry + 4, little);
            Put32(grown, holderEntry + 4, checked(count + (uint)added), little);
            allowed.Add((holderEntry + 4, 4));
            state.NoteGrowth += added;
        }
        state.Appended += added;
        output = grown;
    }

    /// <summary>
    /// The entry of the MakerNote tag in the Exif directory of an EXIF block, whose value lies at
    /// <paramref name="noteStart"/>; -1 when the block does not have it there.
    /// </summary>
    private static int HolderEntry(byte[] block, int noteStart)
    {
        if (block.Length < 8) return -1;
        bool little = block[0] == 0x49;
        int exif = Find(Get32(block, 4, little), 0x8769);
        if (exif < 0 || Get16(block, exif + 2, little) is not (4 or 13) || Get32(block, exif + 4, little) != 1) return -1;
        int entry = Find(Get32(block, exif + 8, little), 0x927C);
        if (entry < 0 || Get16(block, entry + 2, little) != 7 || Get32(block, entry + 4, little) <= 4 ||
            Get32(block, entry + 8, little) != (uint)noteStart) return -1;
        return entry;

        int Find(uint directory, ushort tag)
        {
            if (directory < 8 || directory > block.Length - 2) return -1;
            int count = Get16(block, (int)directory, little);
            if (count * 12L > block.Length - 2 - directory) return -1;
            for (int i = 0; i < count; i++)
            {
                int at = (int)directory + 2 + (i * 12);
                if (Get16(block, at, little) == tag) return at;
            }
            return -1;
        }
    }

    private static void Remove(byte[] output, MakerNote note, MakerNoteTarget target, string key, List<(int, int)> allowed)
    {
        if (target.Element is not null || (target.Field is not null && target.Field.Slot is null))
            throw Unsupported($"{key} is a field of an array: the array has no place for fewer fields, so it is set, not removed.");
        MakerNoteSlot slot = SlotOf(target, key);
        CheckSlot(output, slot);
        int count = Get16(output, slot.Directory, slot.Little);
        int table = slot.Directory + 2, tableEnd = table + (count * 12);
        if (count < 2)
            throw Unsupported($"{key} is the last tag of its directory: a directory without tags does not read as one, so it is not removed.");
        if (slot.Entry < table || slot.Entry > tableEnd - 12 || (slot.Entry - table) % 12 != 0 || tableEnd > output.Length)
            throw Refuse("an entry lies outside its directory");
        int read = 0;
        foreach (MakerNoteSlot other in Slots(note))
            if (other.Directory == slot.Directory) read++;
        if (read != count)
            throw Unsupported($"The directory of {key} holds entries this library does not read (a type TIFF does not define, or a tag twice): what their values address is not known, so no entry of it is moved.");

        // The offset of the next directory follows the entries where the note has room for one that
        // nothing else uses. Panasonic's directory has none.
        bool next = note.Format != MakerNoteFormat.Panasonic && tableEnd <= slot.RegionEnd - 4 && tableEnd <= output.Length - 4 &&
            !Occupied(note, tableEnd, 4, slot.Directory);
        uint following = next ? BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(tableEnd)) : 0;
        bool shared = IsShared(note, slot);
        if (slot.ByteLength > 4 && !shared)
        {
            output.AsSpan(slot.Value, slot.ByteLength).Clear();
            allowed.Add((slot.Value, slot.ByteLength));
        }
        Buffer.BlockCopy(output, slot.Entry + 12, output, slot.Entry, tableEnd - (slot.Entry + 12));
        int end = tableEnd - 12;
        if (next)
        {
            // The four bytes keep their value, whatever byte order they are in: they are copied.
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(end), following);
            output.AsSpan(end + 4, 12).Clear();
            allowed.Add((slot.Directory, tableEnd + 4 - slot.Directory));
        }
        else
        {
            output.AsSpan(end, 12).Clear();
            allowed.Add((slot.Directory, tableEnd - slot.Directory));
        }
        Put16(output, slot.Directory, (ushort)(count - 1), slot.Little);
    }

    private static void CheckSlot(byte[] output, MakerNoteSlot slot)
    {
        if (slot.Directory < 0 || slot.Entry < slot.Directory + 2 || slot.Entry > output.Length - 12 ||
            slot.Value < 0 || slot.Value > output.Length - slot.ByteLength)
            throw Refuse("an entry or its value lies outside the block");
    }

    /// <summary>Every entry of the note and of the directories it leads to.</summary>
    private static IEnumerable<MakerNoteSlot> Slots(MakerNote note)
    {
        foreach (MakerNoteEntry entry in note.Entries)
        {
            if (entry.Slot is not null) yield return entry.Slot;
            foreach (MakerNoteField field in entry.Fields)
                if (field.Slot is not null) yield return field.Slot;
        }
    }

    /// <summary>True when the bytes of the value are also the bytes of another value or of a directory.</summary>
    private static bool IsShared(MakerNote note, MakerNoteSlot slot)
    {
        if (slot.ByteLength <= 4) return false;
        foreach (MakerNoteSlot other in Slots(note))
        {
            if (other.Entry == slot.Entry) continue;
            if (other.ByteLength > 4 && Overlap(slot.Value, slot.ByteLength, other.Value, other.ByteLength)) return true;
        }
        return Occupied(note, slot.Value, slot.ByteLength, -1, valuesToo: false);
    }

    /// <summary>True when a range holds a value or a part of a directory other than <paramref name="except"/>.</summary>
    private static bool Occupied(MakerNote note, int start, int length, int except, bool valuesToo = true)
    {
        var directories = new HashSet<int>();
        foreach (MakerNoteSlot other in Slots(note))
        {
            if (valuesToo && other.ByteLength > 4 && Overlap(start, length, other.Value, other.ByteLength)) return true;
            directories.Add(other.Directory);
        }
        foreach (int directory in directories)
        {
            if (directory == except) continue;
            int entries = 0;
            foreach (MakerNoteSlot other in Slots(note))
                if (other.Directory == directory) entries = Math.Max(entries, ((other.Entry - directory - 2) / 12) + 1);
            if (Overlap(start, length, directory, 2 + (entries * 12))) return true;
        }
        return false;
    }

    private static bool Overlap(int start, int length, int otherStart, int otherLength) =>
        start < otherStart + otherLength && otherStart < start + length;

    // ---- The check ------------------------------------------------------------------------------------

    private static void Verify(byte[] block, ExifMetadata source, byte[]? picture, MakerNote before, byte[] output,
        Dictionary<string, ExifValue?> planned, List<(int Start, int Length)> allowed, MetadataLimits limits, State state)
    {
        if (output.Length != block.Length + state.Appended || (state.WholeFile && state.Appended != 0))
            throw Refuse("the block does not have the planned length");
        if (state.WholeFile)
        {
            // In a file, where the directories of the image lie around the note, nothing outside it is written.
            if (state.Holder is not { } holder) throw Refuse("the tag that holds the MakerNote was not found");
            foreach ((int start, int length) in allowed)
                if (start < holder.Start || length < 0 || start > holder.End - length)
                    throw Unsupported("A value of the MakerNote lies outside the MakerNote in this TIFF-based file: nothing outside the note is written, so it is not changed.");
        }
        for (int i = 0; i < block.Length; i++)
        {
            if (block[i] == output[i]) continue;
            bool inside = false;
            foreach ((int start, int length) in allowed)
                if (i >= start && i < start + length) { inside = true; break; }
            if (!inside) throw Refuse("a byte outside the planned ranges changed");
        }

        (ExifMetadata result, byte[]? pictureAfter) = Read(output, limits, state.WholeFile);
        if ((picture is null) != (pictureAfter is null) || (picture is not null && !picture.AsSpan().SequenceEqual(pictureAfter)))
            throw Refuse("the thumbnail no longer reads as it did");
        MakerNote after = result.MakerNote ?? throw Refuse("the MakerNote no longer reads");
        if (after.Format != before.Format || after.ByteOrder != before.ByteOrder || after.Offset != before.Offset)
            throw Refuse("the MakerNote reads as another note");
        if (result.Entries.Count != source.Entries.Count) throw Refuse("the tags outside the MakerNote changed");
        foreach (ExifEntry entry in source.Entries)
        {
            if (!result.TryGetEntry(entry.Key, out ExifEntry? same))
                throw Refuse("a tag outside the MakerNote is gone");
            // The tag that holds the note (MakerNote, or DNGPrivateData) keeps its type and its length;
            // its bytes are the note, which is compared tag by tag below.
            bool holds = entry.Key.TagId is 0x927C or 0xC634;
            long grownBy = state.Holder is { } held && held.Key == entry.Key ? state.NoteGrowth : 0;
            if (holds ? entry.Value.FieldType != same.Value.FieldType || entry.Value.Count + grownBy != same.Value.Count : !Same(entry.Value, same.Value))
                throw Refuse("a tag outside the MakerNote changed");
        }

        Dictionary<string, ExifValue> old = Values(before), now = Values(after);
        foreach (KeyValuePair<string, ExifValue> pair in old)
        {
            if (planned.TryGetValue(pair.Key, out ExifValue? wanted))
            {
                if (wanted is null ? now.ContainsKey(pair.Key) : !now.TryGetValue(pair.Key, out ExifValue? set) || !Same(wanted, set))
                    throw Refuse("a tag that was named does not read as it was set");
                continue;
            }
            if (Related(pair.Key, planned)) continue;
            if (!now.TryGetValue(pair.Key, out ExifValue? kept) || !Same(pair.Value, kept))
                throw Refuse("a tag of the MakerNote that was not named no longer reads as it did");
        }
        foreach (string path in now.Keys)
            if (!old.ContainsKey(path) && !Related(path, planned))
                throw Refuse("the MakerNote reads with a tag it did not have");
    }

    /// <summary>Path -> value of everything the note holds; a path that occurs twice names the first.</summary>
    private static Dictionary<string, ExifValue> Values(MakerNote note)
    {
        var values = new Dictionary<string, ExifValue>(StringComparer.Ordinal);
        foreach (MakerNoteTarget target in MakerNoteKeys.Enumerate(note))
            values.TryAdd(target.Path, target.Value);
        return values;
    }

    /// <summary>
    /// True for the array of a field that was named, and for the fields of a tag that was named: their
    /// bytes change with it. The other fields of the array are not related, and have to stay.
    /// </summary>
    private static bool Related(string path, Dictionary<string, ExifValue?> planned)
    {
        foreach (string named in planned.Keys)
        {
            if (path.Length > named.Length && path[named.Length] == '/' && path.StartsWith(named, StringComparison.Ordinal)) return true;
            if (named.Length > path.Length && named[path.Length] == '/' && named.StartsWith(path, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool Same(ExifValue left, ExifValue right) =>
        left.FieldType == right.FieldType && left.Count == right.Count && left.RawBytes.SequenceEqual(right.RawBytes);

    private static int Get16(byte[] bytes, int at, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at));

    private static uint Get32(byte[] bytes, int at, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));

    private static void Put16(byte[] bytes, int at, ushort value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        else BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), value);
    }

    private static void Put32(byte[] bytes, int at, uint value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), value);
    }

    private static MetadataException Unsupported(string message) => new(MetadataErrorCode.UnsupportedFeature, message);

    private static MetadataException Refuse(string what) =>
        new(MetadataErrorCode.UnsafeRelocation, "The planned MakerNote failed verification and was not written: " + what + ".");
}
