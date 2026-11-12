using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;

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
    /// <summary>
    /// Where the copy of the directory after the primary one is, when that directory gained entries:
    /// the successor link of the primary directory, which no tag holds, then names this address.
    /// </summary>
    internal uint? Successor { get; init; }
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
/// Plans an edit of the primary, Exif, GPS and Interoperability directories of a TIFF structure, and
/// of the directory after the primary one, without moving a byte of it.
/// </summary>
/// <remarks>
/// <para>A value that fits where the old one is, is written there: in the entry when it has four
/// bytes or fewer, otherwise over the old value, provided that nothing else in the file addresses
/// those bytes. A longer value is appended and the entry is pointed at it.</para>
/// <para>A directory that keeps or loses entries is rewritten where it is. A directory that gains
/// entries is appended as a whole, its entries copied as they are, and the one pointer that leads to
/// it is changed: the header for the primary directory, the pointer tag of its parent for the Exif,
/// GPS and Interoperability directories, the successor link of the primary directory for the one
/// after it. Offsets in a TIFF count from the start of the file, so a copied entry still addresses
/// the value it addressed.</para>
/// <para>An Exif, GPS or Interoperability directory the file does not have is appended when a tag is
/// set in it, and its pointer tag is added to its parent; an Interoperability directory brings an
/// Exif directory with it. One that loses its last tag is dropped: its pointer tag is removed. The
/// directory after the primary one is neither created nor dropped.</para>
/// <para>Bytes that are no longer addressed stay in the file. Image data, SubIFDs and the MakerNote
/// are never written to, and they keep their offsets because nothing before them changes length.</para>
/// </remarks>
internal sealed class TiffBasedWritePlanner
{
    private const ushort ExifPointer = 0x8769;
    private const ushort GpsPointer = 0x8825;
    private const ushort InteropPointer = 0xA005;

    private readonly byte[] source;
    private readonly TiffBasedFileMap map;
    private readonly IReadOnlyDictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes;
    private readonly MetadataLimits limits;
    private readonly long ceiling;
    private readonly bool emptyPrimary;
    private readonly List<(int Offset, byte[] Bytes)> patches = [];
    private readonly List<byte> tail = [];
    private readonly Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> effective = [];
    private readonly HashSet<TiffBasedRole> removed = [];
    /// <summary>New targets of the pointer tags of a parent directory; null removes the tag.</summary>
    private readonly Dictionary<TiffBasedRole, SortedDictionary<ushort, uint?>> pointers = [];
    private uint? successor;

    private TiffBasedWritePlanner(byte[] source, TiffBasedFileMap map,
        IReadOnlyDictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes, MetadataLimits limits, long ceiling,
        bool emptyPrimary)
    {
        this.emptyPrimary = emptyPrimary;
        this.source = source;
        this.map = map;
        this.changes = changes;
        this.limits = limits;
        this.ceiling = ceiling;
    }

    /// <summary>The directory kind the reader reports for a role the planner edits.</summary>
    internal static ExifDirectoryKind Kind(TiffBasedRole role) => role switch
    {
        TiffBasedRole.Primary => ExifDirectoryKind.Ifd0,
        TiffBasedRole.Exif => ExifDirectoryKind.Exif,
        TiffBasedRole.Gps => ExifDirectoryKind.Gps,
        TiffBasedRole.Interop => ExifDirectoryKind.Interop,
        TiffBasedRole.Thumbnail => ExifDirectoryKind.Thumbnail,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    /// <summary>The role the planner edits a directory kind under.</summary>
    internal static TiffBasedRole Role(ExifDirectoryKind kind) => kind switch
    {
        ExifDirectoryKind.Ifd0 => TiffBasedRole.Primary,
        ExifDirectoryKind.Exif => TiffBasedRole.Exif,
        ExifDirectoryKind.Gps => TiffBasedRole.Gps,
        ExifDirectoryKind.Interop => TiffBasedRole.Interop,
        ExifDirectoryKind.Thumbnail => TiffBasedRole.Thumbnail,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// Plans the changes. An output longer than <paramref name="ceiling"/> is refused with
    /// <see cref="MetadataErrorCode.ExifTooLarge"/>: the structure is an EXIF block whose container
    /// sets that length. Without a ceiling the input budget and the reach of a TIFF offset bound it.
    /// With <paramref name="emptyPrimary"/> the primary directory may lose its last entry, as the one
    /// of an EXIF block may; that of a file may not, since the file is then no image.
    /// </summary>
    internal static TiffBasedWritePlan Create(byte[] source, TiffBasedFileMap map,
        IReadOnlyDictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes, MetadataLimits limits,
        long ceiling = long.MaxValue, bool emptyPrimary = false)
    {
        foreach (KeyValuePair<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> change in changes)
        {
            if (change.Key.Role == TiffBasedRole.Other || !Enum.IsDefined(change.Key.Role))
                throw new ArgumentException("Only the primary, Exif, GPS and Interoperability directories and the directory after the primary one are edited.", nameof(changes));
            var key = new ExifTagKey(Kind(change.Key.Role), change.Key.Tag);
            if (!ExifEdit.IsEditable(key))
                throw ExifEdit.NotEditable(key);
            if (change.Key.Tag == 0x0118 && map.IsRw2 && change.Key.Role == TiffBasedRole.Primary)
                throw new MetadataException(MetadataErrorCode.TagNotEditable,
                    "Tag 0x0118 of a Panasonic RW2 file is the address of its raw data: it is not edited as a value.");
            if (change.Value is TiffBasedChange value &&
                (TiffBasedFileMap.Width(value.Type) == 0 || value.Bytes.Length == 0 ||
                 (long)value.Count * TiffBasedFileMap.Width(value.Type) != value.Bytes.Length))
                throw new ArgumentException("A value does not have the length of its type and count.", nameof(changes));
        }
        var planner = new TiffBasedWritePlanner(source, map, changes, limits, ceiling, emptyPrimary);
        // Children first: a directory that is copied, created or dropped changes a pointer of its parent.
        planner.Plan(TiffBasedRole.Interop, TiffBasedRole.Exif, InteropPointer);
        planner.Plan(TiffBasedRole.Gps, TiffBasedRole.Primary, GpsPointer);
        planner.Plan(TiffBasedRole.Exif, TiffBasedRole.Primary, ExifPointer);
        planner.Plan(TiffBasedRole.Thumbnail, TiffBasedRole.Primary, 0);
        planner.Plan(TiffBasedRole.Primary, TiffBasedRole.Primary, 0);
        return new TiffBasedWritePlan(source.Length, planner.patches, planner.tail.ToArray(), planner.effective,
            planner.removed) { Successor = planner.successor };
    }

    private void Plan(TiffBasedRole role, TiffBasedRole parent, ushort pointerTag)
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
            // Before a value is placed: there is no directory to place it in, and none is made.
            if (directory is null && role == TiffBasedRole.Thumbnail) throw NoSuccessor();
            entries[tag] = Entry(tag, value, field);
            effective[(role, tag)] = value;
            changed = true;
        }
        if (pointers.TryGetValue(role, out SortedDictionary<ushort, uint?>? mine))
        {
            foreach (KeyValuePair<ushort, uint?> pointer in mine)
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
        // The successor link is no tag: the directory is written again for it alone.
        if (role == TiffBasedRole.Primary && successor is not null) changed = true;
        if (!changed) return;

        // The pointer is there and the directory was reached another way first: it is shared, and a
        // new directory in its place would take its tags out of this role.
        if (directory is null && pointerTag != 0 && map.Find(parent)?.Find(pointerTag) is not null)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "A directory that is reached from more than one pointer cannot be replaced by a copy.");

        if (entries.Count == 0 && !(role == TiffBasedRole.Primary && emptyPrimary))
        {
            if (role is TiffBasedRole.Primary or TiffBasedRole.Thumbnail)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature, role == TiffBasedRole.Primary
                    ? "The edit would leave the primary directory without entries."
                    : "The edit would leave the directory after the primary one without entries.");
            // The last tag of the directory went: the directory goes with it, by its pointer.
            Point(parent, pointerTag, null);
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
        Put32(image, at, role == TiffBasedRole.Primary && successor is uint moved ? moved : directory?.Next ?? 0);

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
        else if (role == TiffBasedRole.Thumbnail) successor = copy;
        else Point(parent, pointerTag, copy);
    }

    private void Point(TiffBasedRole parent, ushort pointerTag, uint? target)
    {
        if (!pointers.TryGetValue(parent, out SortedDictionary<ushort, uint?>? tags))
            pointers.Add(parent, tags = []);
        tags[pointerTag] = target;
    }

    /// <summary>
    /// A tag was set in the directory after the primary one and the file has none, or the one it
    /// has was reached another way first.
    /// </summary>
    private MetadataException NoSuccessor() => map.Find(TiffBasedRole.Primary)?.Next is not (null or 0)
        ? new MetadataException(MetadataErrorCode.UnsafeRelocation,
            "A directory that is reached from more than one pointer cannot be replaced by a copy.")
        : new MetadataException(MetadataErrorCode.UnsupportedFeature,
            "The file has no directory after the primary one: a thumbnail directory is not created by a tag edit.");

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
        if (bytes.Length > ceiling - at)
            throw new MetadataException(MetadataErrorCode.ExifTooLarge, ceiling == ExifTiffReader.SegmentCeiling
                ? "The edited EXIF block does not fit a single JPEG APP1 segment."
                : "The edited EXIF block is longer than its container allows.");
        // The output is read back under the same limits, so it may not be larger than an input may be.
        long budget = Math.Min(Math.Min(limits.MaxInputBytes, uint.MaxValue), Array.MaxLength);
        if (bytes.Length > budget - at)
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
