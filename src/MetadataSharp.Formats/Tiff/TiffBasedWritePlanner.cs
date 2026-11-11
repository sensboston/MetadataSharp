using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Tiff;

/// <summary>The value a tag is to have, in the byte order of the file.</summary>
internal sealed record TiffBasedChange(ushort Type, uint Count, byte[] Bytes);

/// <summary>
/// What a write does to the bytes of the source: overwrite a few ranges, and add a tail. Every other
/// byte of the source is in the output at the offset it had.
/// </summary>
internal sealed class TiffBasedWritePlan(int sourceLength, List<(int Offset, byte[] Bytes)> patches, byte[] tail,
    Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> effective, HashSet<TiffBasedRole> removed)
{
    internal int SourceLength { get; } = sourceLength;
    /// <summary>Ranges of the source that are overwritten: none overlaps another.</summary>
    internal IReadOnlyList<(int Offset, byte[] Bytes)> Patches { get; } = patches;
    /// <summary>The bytes after the last source byte, a padding byte for an odd length included.</summary>
    internal byte[] Tail { get; } = tail;
    /// <summary>
    /// Every tag whose entry differs from the source, with its new value or null for a removed tag.
    /// The pointers of a directory that was copied or dropped are among them.
    /// </summary>
    internal IReadOnlyDictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> Effective { get; } = effective;
    /// <summary>Directories the edit emptied: their pointer is removed and their bytes stay.</summary>
    internal IReadOnlySet<TiffBasedRole> Removed { get; } = removed;
    internal bool IsEmpty => Patches.Count == 0 && Tail.Length == 0;
    internal int OutputLength => SourceLength + Tail.Length;

    internal byte[] Apply(byte[] source)
    {
        byte[] output = new byte[OutputLength];
        source.AsSpan().CopyTo(output);
        foreach ((int offset, byte[] bytes) in Patches)
            bytes.AsSpan().CopyTo(output.AsSpan(offset));
        Tail.AsSpan().CopyTo(output.AsSpan(SourceLength));
        return output;
    }
}

/// <summary>
/// Plans an edit of the primary, Exif and GPS directories of a TIFF-based file without moving a
/// byte of it.
/// </summary>
/// <remarks>
/// <para>A value that fits where the old one is, is written there: in the entry when it has four
/// bytes or fewer, otherwise over the old value, provided that nothing else in the file addresses
/// those bytes. A longer value is appended and the entry is pointed at it.</para>
/// <para>A directory that keeps or loses entries is rewritten where it is. A directory that gains
/// entries is appended as a whole, its entries copied as they are, and the one pointer that leads to
/// it is changed: the header for the primary directory, the pointer tag of the primary directory for
/// the other two. Offsets in a TIFF count from the start of the file, so a copied entry still
/// addresses the value it addressed.</para>
/// <para>Bytes that are no longer addressed stay in the file. Image data, SubIFDs and the MakerNote
/// are never written to, and they keep their offsets because nothing before them changes length.</para>
/// </remarks>
internal sealed class TiffBasedWritePlanner
{
    private const ushort ExifPointer = 0x8769;
    private const ushort GpsPointer = 0x8825;

    private readonly byte[] source;
    private readonly TiffBasedFileMap map;
    private readonly IReadOnlyDictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes;
    private readonly MetadataLimits limits;
    private readonly List<(int Offset, byte[] Bytes)> patches = [];
    private readonly List<byte> tail = [];
    private readonly Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> effective = [];
    private readonly HashSet<TiffBasedRole> removed = [];
    /// <summary>New targets of the pointer tags of the primary directory; null removes the tag.</summary>
    private readonly SortedDictionary<ushort, uint?> pointers = [];

    private TiffBasedWritePlanner(byte[] source, TiffBasedFileMap map,
        IReadOnlyDictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes, MetadataLimits limits)
    {
        this.source = source;
        this.map = map;
        this.changes = changes;
        this.limits = limits;
    }

    internal static TiffBasedWritePlan Create(byte[] source, TiffBasedFileMap map,
        IReadOnlyDictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes, MetadataLimits limits)
    {
        foreach (KeyValuePair<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> change in changes)
        {
            if (change.Key.Role is not (TiffBasedRole.Primary or TiffBasedRole.Exif or TiffBasedRole.Gps))
                throw new ArgumentException("Only the primary, Exif and GPS directories are edited.", nameof(changes));
            if (change.Key.Tag is ExifPointer or GpsPointer or 0xA005 or 0x927C or 0xC634 or 0x014A or 0x0111 or 0x0117
                or 0x0144 or 0x0145 or 0x0201 or 0x0202 or 0x0118)
                throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                    "Pointers, image data addresses and the MakerNote are not edited.");
            if (change.Value is TiffBasedChange value &&
                (TiffBasedFileMap.Width(value.Type) == 0 || value.Bytes.Length == 0 ||
                 (long)value.Count * TiffBasedFileMap.Width(value.Type) != value.Bytes.Length))
                throw new ArgumentException("A value does not have the length of its type and count.", nameof(changes));
        }
        var planner = new TiffBasedWritePlanner(source, map, changes, limits);
        // Children first: a directory that is copied or dropped changes a pointer of the primary one.
        planner.Plan(TiffBasedRole.Gps, GpsPointer);
        planner.Plan(TiffBasedRole.Exif, ExifPointer);
        planner.Plan(TiffBasedRole.Primary, 0);
        return new TiffBasedWritePlan(source.Length, planner.patches, planner.tail.ToArray(), planner.effective,
            planner.removed);
    }

    private void Plan(TiffBasedRole role, ushort pointerTag)
    {
        TiffBasedDirectory? directory = map.Find(role);
        var entries = new SortedDictionary<ushort, byte[]>();
        if (directory is not null)
            foreach (TiffBasedField field in directory.Fields)
                entries[field.Tag] = source.AsSpan(field.EntryOffset, 12).ToArray();

        bool changed = false;
        foreach (KeyValuePair<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> change in
                 changes.Where(c => c.Key.Role == role).OrderBy(c => c.Key.Tag))
        {
            ushort tag = change.Key.Tag;
            TiffBasedField? field = directory?.Find(tag);
            if (change.Value is not TiffBasedChange value)
            {
                if (field is null) continue; // removing what is not there
                RequireSorted(directory);
                entries.Remove(tag);
                effective[(role, tag)] = null;
                changed = true;
                continue;
            }
            if (field is not null && Same(field, value)) continue; // the file already says so
            RequireSorted(directory);
            entries[tag] = Entry(tag, value, field);
            effective[(role, tag)] = value;
            changed = true;
        }
        if (role == TiffBasedRole.Primary)
        {
            foreach (KeyValuePair<ushort, uint?> pointer in pointers)
            {
                TiffBasedField? field = directory?.Find(pointer.Key);
                RequireSorted(directory);
                if (pointer.Value is not uint target)
                {
                    if (field is null) continue;
                    entries.Remove(pointer.Key);
                    effective[(role, pointer.Key)] = null;
                    changed = true;
                    continue;
                }
                // An existing pointer keeps its type, which may be IFD (13) instead of LONG.
                var value = new TiffBasedChange(field?.Type ?? (ushort)4, 1, Number(target));
                entries[pointer.Key] = Entry(pointer.Key, value, field);
                effective[(role, pointer.Key)] = value;
                changed = true;
            }
        }
        if (!changed) return;

        // The pointer is there and the directory was reached another way first: it is shared, and a
        // new directory in its place would take its tags out of this role.
        if (directory is null && pointerTag != 0 && map.Find(TiffBasedRole.Primary)?.Find(pointerTag) is not null)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "A directory that is reached from more than one pointer cannot be replaced by a copy.");

        if (entries.Count == 0)
        {
            if (role == TiffBasedRole.Primary)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "The edit would leave the primary directory without entries.");
            // The last tag of the directory went: the directory goes with it, by its pointer.
            pointers[pointerTag] = null;
            removed.Add(role);
            return;
        }
        if (entries.Count > ushort.MaxValue || entries.Count > limits.MaxEntriesPerDirectory)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The edited directory exceeds the entry budget.");

        byte[] image = new byte[2 + 12 * entries.Count + 4];
        Put16(image, 0, (ushort)entries.Count);
        int at = 2;
        foreach (byte[] entry in entries.Values)
        {
            entry.CopyTo(image, at);
            at += 12;
        }
        Put32(image, at, directory?.Next ?? 0);

        if (directory is not null && entries.Count <= directory.Fields.Count &&
            map.IsExclusive(directory.Offset, directory.Length))
        {
            patches.Add(((int)directory.Offset, image));
            return;
        }
        if (directory is not null && directory.References > 1)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "A directory that is reached from more than one pointer cannot be replaced by a copy.", directory.Offset);
        uint copy = Append(image);
        if (role == TiffBasedRole.Primary) patches.Add((4, Number(copy)));
        else pointers[pointerTag] = copy;
    }

    private static void RequireSorted(TiffBasedDirectory? directory)
    {
        if (directory is not null && !directory.IsSorted)
            throw new MetadataException(MetadataErrorCode.InvalidTiff,
                "The tags of the directory to edit are not in ascending order.", directory.Offset);
    }

    private bool Same(TiffBasedField field, TiffBasedChange value) =>
        field.Type == value.Type && field.Count == value.Count &&
        field.ValueOffset <= source.Length - field.ByteLength &&
        source.AsSpan((int)field.ValueOffset, (int)field.ByteLength).SequenceEqual(value.Bytes);

    /// <summary>The twelve bytes of an entry, with the value stored wherever it is to live.</summary>
    private byte[] Entry(ushort tag, TiffBasedChange value, TiffBasedField? old)
    {
        byte[] entry = new byte[12];
        Put16(entry, 0, tag);
        Put16(entry, 2, value.Type);
        Put32(entry, 4, value.Count);
        if (value.Bytes.Length <= 4)
        {
            value.Bytes.CopyTo(entry, 8);
        }
        else if (old is not null && !old.IsInline && value.Bytes.Length <= old.ByteLength &&
                 map.IsExclusive(old.ValueOffset, old.ByteLength))
        {
            patches.Add(((int)old.ValueOffset, value.Bytes));
            Put32(entry, 8, (uint)old.ValueOffset);
        }
        else
        {
            Put32(entry, 8, Append(value.Bytes));
        }
        return entry;
    }

    /// <summary>Adds bytes after the end of the file, at an even offset, and returns that offset.</summary>
    private uint Append(byte[] bytes)
    {
        if (map.HasOpenEndedImageData)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "Image data of this file runs to its end: the edit needs to append, and that would move the end.");
        if (map.HasRangePastEnd)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "The file addresses bytes past its end: the edit needs to append, and that would put new bytes there.");
        long at = (long)source.Length + tail.Count;
        if ((at & 1) != 0)
        {
            tail.Add(0);
            at++;
        }
        // The output is read back under the same limits, so it may not be larger than an input may be.
        long ceiling = Math.Min(Math.Min(limits.MaxInputBytes, uint.MaxValue), Array.MaxLength);
        if (bytes.Length > ceiling - at)
            throw new MetadataException(MetadataErrorCode.LimitExceeded,
                "The edited file exceeds the input budget or the four gigabytes a TIFF offset can address.");
        tail.AddRange(bytes);
        return (uint)at;
    }

    private byte[] Number(uint value)
    {
        byte[] bytes = new byte[4];
        Put32(bytes, 0, value);
        return bytes;
    }

    private void Put16(byte[] bytes, int at, ushort value)
    {
        if (map.Order == Endianness.LittleEndian) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        else BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), value);
    }

    private void Put32(byte[] bytes, int at, uint value)
    {
        if (map.Order == Endianness.LittleEndian) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), value);
    }
}
