using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Bmff;

internal enum BmffItemKind
{
    Exif,
    Xmp
}

/// <summary>What a write does to the <c>meta</c> box.</summary>
internal enum BmffMetaPlacement
{
    /// <summary>Not a byte of it changes: the item was rewritten where it is.</summary>
    Untouched,
    /// <summary>
    /// One child is rewritten in the bytes it has, the item location box or the item properties
    /// box; every other child stays.
    /// </summary>
    InPlace,
    /// <summary>The box is the last one of the file and is rewritten, longer, where it starts.</summary>
    Extended,
    /// <summary>A new box is written at the end of the file and the old one becomes a <c>free</c> box.</summary>
    Moved
}

/// <summary>
/// What a write does to the bytes of the source: overwrite a few ranges, and add a tail. Every other
/// byte of the source is in the output at the offset it had.
/// </summary>
internal sealed class BmffWritePlan(int sourceLength, List<(int Offset, byte[] Bytes)> patches, byte[] tail,
    BmffItemKind kind, uint itemId, bool added, byte[] payload, BmffMetaPlacement placement)
{
    internal int SourceLength { get; } = sourceLength;
    /// <summary>Ranges of the source that are overwritten: none overlaps another.</summary>
    internal IReadOnlyList<(int Offset, byte[] Bytes)> Patches { get; } = patches;
    /// <summary>The bytes after the last source byte.</summary>
    internal byte[] Tail { get; } = tail;
    internal BmffItemKind Kind { get; } = kind;
    /// <summary>The item that was edited, or the ID of the one that was added.</summary>
    internal uint ItemId { get; } = itemId;
    internal bool Added { get; } = added;
    /// <summary>The bytes the item is to have.</summary>
    internal byte[] Payload { get; } = payload;
    internal BmffMetaPlacement Placement { get; } = placement;
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
/// Plans the replacement or the addition of one metadata item of a BMFF file without moving the
/// data of any other item.
/// </summary>
/// <remarks>
/// <para>An item whose new bytes are as many as the old ones is written where it is, provided that
/// it lies in a media data or item data box and no other item addresses those bytes. Otherwise the
/// new bytes are appended to the file in a media data box of their own, and the entry of the item in
/// the item location box is pointed at them; the bytes they replace stay in the file, no longer
/// addressed.</para>
/// <para>The item location box is rewritten with the field sizes it has; a size is raised only when
/// a value does not fit it, and only together with the whole <c>meta</c> box. When the rewritten box
/// is as long as the old one, or shorter by enough for a <c>free</c> box, it is written over the old
/// one and nothing else in the <c>meta</c> box is touched. When the <c>meta</c> box has to grow -
/// for a new item, with its entry in the item information box and its reference to the primary
/// item - it grows where it is if it is the last box of the file; otherwise a new one is appended
/// and the old one is turned into a <c>free</c> box of the same length by changing its type.</para>
/// <para>No box other than the <c>meta</c> box changes its offset or its length, so every absolute
/// offset in the file addresses what it addressed. The item data box moves with a <c>meta</c> box
/// that moves, and the items in it are addressed from its start.</para>
/// </remarks>
internal sealed class BmffWritePlanner
{
    /// <summary>The top-level boxes that new bytes may follow: none of them counts on being the last.</summary>
    internal static readonly uint[] KnownTopLevel =
    [
        BmffFileMap.FileType, BmffFileMap.Meta, BmffFileMap.MediaData, BmffFileMap.Free,
        0x736b6970, // 'skip'
        0x77696465, // 'wide'
        0x6d6f6f76, // 'moov': chunk offsets count from the start of the file, and nothing before them moves
        0x75756964  // 'uuid'
    ];

    private readonly byte[] source;
    private readonly BmffFileMap map;
    private readonly BmffItemKind kind;
    private readonly BmffItem? target;
    private readonly byte[] payload;
    private readonly MetadataLimits limits;
    /// <summary>What every item but the edited one addresses, in the widest reading of its extents.</summary>
    private readonly List<(BmffSegment Segment, int Method)> others = [];

    private BmffWritePlanner(byte[] source, BmffFileMap map, BmffItemKind kind, BmffItem? target, byte[] payload,
        MetadataLimits limits)
    {
        this.source = source;
        this.map = map;
        this.kind = kind;
        this.target = target;
        this.payload = payload;
        this.limits = limits;
    }

    /// <summary>Plans the output in which one metadata item has new bytes.</summary>
    /// <param name="source">The whole source file.</param>
    /// <param name="map">The boxes and items of the source.</param>
    /// <param name="kind">The kind of the item: Exif or XMP.</param>
    /// <param name="target">The item to replace, or null to add one of the kind.</param>
    /// <param name="payload">The bytes of the item in the output; at least one.</param>
    /// <param name="limits">The budgets the output must stay within.</param>
    internal static BmffWritePlan Create(byte[] source, BmffFileMap map, BmffItemKind kind, BmffItem? target,
        byte[] payload, MetadataLimits limits)
    {
        if (payload.Length == 0) throw new ArgumentException("An item has at least one byte.", nameof(payload));
        if (map.MetaBox is null || map.Information is null || map.Locations is null)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The file has no meta box with item information and item locations: there is no item structure to edit.");
        return new BmffWritePlanner(source, map, kind, target, payload, limits).Plan();
    }

    private BmffWritePlan Plan()
    {
        BmffLocation? old = null;
        if (target is not null)
        {
            old = map.Location(target.Id) ??
                throw new MetadataException(MetadataErrorCode.InvalidBmff, "The item to edit has no item location.", null);
        }
        var segments = new List<BmffSegment>();
        foreach (BmffLocation location in map.Entries)
        {
            if (old is not null && location.Id == old.Id) continue;
            map.TryResolve(location, wide: true, segments);
            foreach (BmffSegment segment in segments) others.Add((segment, location.Method));
        }

        if (old is not null && TryInPlace(old) is { } inPlace) return inPlace;
        return Append(old);
    }

    /// <summary>The item written over itself, or null when it has to go elsewhere.</summary>
    private BmffWritePlan? TryInPlace(BmffLocation old)
    {
        if (BmffFileMap.IsOpenEnded(old)) return null;
        var segments = new List<BmffSegment>();
        if (!map.TryResolve(old, wide: false, segments)) return null;
        long total = 0;
        foreach (BmffSegment segment in segments) total += segment.Length;
        if (total != payload.Length) return null;
        for (int i = 0; i < segments.Count; i++)
        {
            BmffSegment segment = segments[i];
            if (!InsideData(segment)) return null;
            // Bytes that another item, or another extent of this one, also addresses are not this
            // item's to change.
            if (Touched(segment.Start, segment.Length, anyMethod: true)) return null;
            for (int j = 0; j < i; j++)
                if (segment.Start < segments[j].End && segments[j].Start < segment.End) return null;
        }
        var patches = new List<(int Offset, byte[] Bytes)>();
        int at = 0;
        foreach (BmffSegment segment in segments)
        {
            ReadOnlySpan<byte> bytes = payload.AsSpan(at, segment.Length);
            if (!bytes.SequenceEqual(source.AsSpan(segment.Start, segment.Length)))
                patches.Add((segment.Start, bytes.ToArray()));
            at += segment.Length;
        }
        return new BmffWritePlan(source.Length, patches, [], kind, old.Id, false, payload, BmffMetaPlacement.Untouched);
    }

    /// <summary>Whether the bytes lie in the payload of a top-level media data box or of the item data box.</summary>
    private bool InsideData(BmffSegment segment)
    {
        if (map.Data is { } data && segment.Start >= data.Payload && segment.End <= data.End) return true;
        foreach (BmffBox box in map.TopLevel)
            if (box.Type == BmffFileMap.MediaData && segment.Start >= box.Payload && segment.End <= box.End)
                return true;
        return false;
    }

    /// <summary>
    /// Whether another item addresses any of the bytes. An item of the item data box is addressed
    /// from the start of that box, so it only counts where the bytes themselves are to change.
    /// </summary>
    private bool Touched(long start, long length, bool anyMethod)
    {
        foreach ((BmffSegment segment, int method) in others)
            if ((anyMethod || method == 0) && start < segment.End && segment.Start < start + length)
                return true;
        return false;
    }

    private BmffWritePlan Append(BmffLocation? old)
    {
        BmffBox meta = map.MetaBox!;
        BmffBox iloc = map.Locations!;
        foreach (BmffBox box in map.TopLevel)
            if (Array.IndexOf(KnownTopLevel, box.Type) < 0)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "The edit needs to append, and the file has a top-level box whose relation to the end of the file is not known.",
                    box.Start);
        BmffBox last = map.TopLevel[^1];
        // Bytes after a media data box that runs to the end of the file are part of it.
        bool intoOpenData = last.OpenEnded && last.Type == BmffFileMap.MediaData;
        if (last.OpenEnded && !intoOpenData)
            throw Unsafe("The last box of the file runs to its end: the edit needs to append, and nothing can follow that box.", last.Start);
        var segments = new List<BmffSegment>();
        foreach (BmffLocation location in map.Entries)
        {
            if (location.Method != 0 || (old is not null && location.Id == old.Id)) continue;
            if (BmffFileMap.IsOpenEnded(location))
                throw Unsafe("An item is addressed to the end of the file: the edit needs to append, and that would make the item longer.", null);
            if (!map.TryResolve(location, wide: true, segments))
                throw Unsafe("An item is addressed past the end of the file: the edit needs to append, and that would put new bytes there.", null);
        }

        uint id = old?.Id ?? NewItemId();
        int header = intoOpenData ? 0 : 8;
        long ceiling = Math.Min(limits.MaxInputBytes, Array.MaxLength);

        // The item location box as it is if the data follows the last byte of the source.
        long atEnd = (long)source.Length + header;
        byte[] locations = WriteLocations(old, id, atEnd);
        if (old is not null)
        {
            int spare = iloc.Length - locations.Length;
            if ((spare == 0 || spare >= 8) && !Touched(iloc.Start, iloc.Length, anyMethod: true))
            {
                byte[] replacement = new byte[iloc.Length];
                locations.CopyTo(replacement, 0);
                if (spare != 0)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(locations.Length), (uint)spare);
                    BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(locations.Length + 4), BmffFileMap.Free);
                }
                return Finish([(iloc.Start, replacement)], Data(header), id, false, BmffMetaPlacement.InPlace, ceiling);
            }
        }

        // The meta box changes its length.
        if (meta.OpenEnded)
            throw Unsafe("The meta box runs to the end of the file and cannot change its length.", meta.Start);
        if (intoOpenData)
            throw Unsafe("A media data box runs to the end of the file: the meta box has to change its length, and no box can follow that one.", last.Start);
        byte[] information = old is null ? WriteInformation(id) : [];
        byte[] references = old is null ? WriteReferences(id) : [];
        int length = MetaLength(locations, information, references);

        if (ReferenceEquals(last, meta) && length >= meta.Length && !Touched(meta.Start, meta.Length, anyMethod: false))
        {
            // The last box of the file: it grows where it is, and the data follows it.
            long data = (long)meta.Start + length + 8;
            byte[] rebuilt = WriteMeta(WriteLocations(old, id, data), information, references);
            if (rebuilt.Length != length)
                throw Unsafe("The length of the rewritten meta box depends on where the item is written.", meta.Start);
            byte[] all = [.. rebuilt, .. Data(8)];
            byte[] over = all.AsSpan(0, meta.Length).ToArray();
            byte[] rest = all.AsSpan(meta.Length).ToArray();
            return Finish([(meta.Start, over)], rest, id, old is null, BmffMetaPlacement.Extended, ceiling);
        }

        if (Touched(meta.Start + 4, 4, anyMethod: true))
            throw Unsafe("An item is addressed inside the header of the meta box, which has to be replaced.", meta.Start);
        byte[] moved = WriteMeta(locations, information, references);
        byte[] type = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(type, BmffFileMap.Free);
        return Finish([(meta.Start + 4, type)], [.. Data(8), .. moved], id, old is null, BmffMetaPlacement.Moved, ceiling);
    }

    private BmffWritePlan Finish(List<(int Offset, byte[] Bytes)> patches, byte[] tail, uint id, bool added,
        BmffMetaPlacement placement, long ceiling)
    {
        // The output is read back under the same limits, so it may not be larger than an input may be.
        if (tail.Length > ceiling - source.Length)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The edited file exceeds the input budget.");
        return new BmffWritePlan(source.Length, patches, tail, kind, id, added, payload, placement);
    }

    /// <summary>The new bytes of the item, in a media data box of their own unless one is open to them.</summary>
    private byte[] Data(int header)
    {
        byte[] data = new byte[header + payload.Length];
        if (header != 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), BmffFileMap.MediaData);
        }
        payload.CopyTo(data, header);
        return data;
    }

    private uint NewItemId()
    {
        if (map.HasUnknownItemEntry)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "An item entry of an unknown version hides an item ID: a new item could take it.");
        if (map.PrimaryItem is not uint primary)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The file names no primary item for a new metadata item to describe.");
        uint highest = primary;
        foreach (BmffItem item in map.Items) highest = Math.Max(highest, item.Id);
        foreach (BmffLocation location in map.Entries) highest = Math.Max(highest, location.Id);
        if (highest == uint.MaxValue || (highest >= ushort.MaxValue && map.LocationVersion < 2))
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "No item ID is left for a new item.");
        return highest + 1;
    }

    /// <summary>
    /// The item location box with the entry of the item pointed at one extent at
    /// <paramref name="position"/>, counted from the start of the file. Every other entry keeps its
    /// values. A field size is raised only when the new entry does not fit the one the box has.
    /// </summary>
    private byte[] WriteLocations(BmffLocation? old, uint id, long position)
    {
        int version = map.LocationVersion;
        int offsetSize = map.OffsetSize, lengthSize = map.LengthSize, baseSize = map.BaseOffsetSize;
        int indexSize = map.IndexSize;
        // Where the box has a base offset, that is where the address goes: the extent then starts
        // at the base, as it does in files that use one.
        bool useBase = baseSize != 0;
        if (useBase) baseSize = Math.Max(baseSize, Width((ulong)position));
        else offsetSize = Math.Max(offsetSize, Width((ulong)position));
        lengthSize = Math.Max(lengthSize, Width((ulong)payload.Length));
        var entry = new BmffLocation(id, 0, useBase ? (ulong)position : 0,
            [new BmffExtent(0, useBase ? 0 : (ulong)position, (ulong)payload.Length)]);

        var entries = new List<BmffLocation>(map.Entries.Count + 1);
        foreach (BmffLocation location in map.Entries)
            entries.Add(old is not null && location.Id == old.Id ? entry : location);
        if (old is null) entries.Add(entry);
        int idSize = version < 2 ? 2 : 4;
        if (idSize == 2 && entries.Count > ushort.MaxValue)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The item location box holds no further entry.");

        BmffBox box = map.Locations!;
        var bytes = new List<byte>(box.Length + 32);
        Put32(bytes, 0); // the size, written last
        Put32(bytes, BmffFileMap.ItemLocation);
        bytes.Add((byte)version);
        Put(bytes, source.AsSpan(box.Payload + 1, 3)); // the flags
        bytes.Add((byte)((offsetSize << 4) | lengthSize));
        bytes.Add((byte)((baseSize << 4) | indexSize));
        PutSized(bytes, idSize, (ulong)entries.Count);
        foreach (BmffLocation location in entries)
        {
            PutSized(bytes, idSize, location.Id);
            if (version >= 1) Put16(bytes, (ushort)location.Method);
            Put16(bytes, 0); // data reference index: this file
            PutSized(bytes, baseSize, location.BaseOffset);
            Put16(bytes, (ushort)location.Extents.Count);
            foreach (BmffExtent extent in location.Extents)
            {
                PutSized(bytes, indexSize, extent.Index);
                PutSized(bytes, offsetSize, extent.Offset);
                PutSized(bytes, lengthSize, extent.Length);
            }
        }
        return Sized(bytes);
    }

    /// <summary>The item information box with one more entry, for the new item.</summary>
    private byte[] WriteInformation(uint id)
    {
        BmffBox box = map.Information!;
        int countSize = map.InformationVersion == 0 ? 2 : 4;
        if (countSize == 2 && map.DeclaredItems >= ushort.MaxValue)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The item information box holds no further entry.");
        var bytes = new List<byte>(box.Length + 48);
        Put32(bytes, 0);
        Put32(bytes, BmffFileMap.ItemInformation);
        Put(bytes, source.AsSpan(box.Payload, 4));
        PutSized(bytes, countSize, (ulong)map.DeclaredItems + 1);
        int entries = box.Payload + 4 + countSize;
        Put(bytes, source.AsSpan(entries, box.End - entries));

        // The new entry: version 2 with a 16-bit ID where that holds it, hidden, as an item that is
        // not an image is; no protection, an empty name.
        var entry = new List<byte>(48);
        Put32(entry, 0);
        Put32(entry, BmffFileMap.ItemEntry);
        bool narrow = id <= ushort.MaxValue;
        entry.Add((byte)(narrow ? 2 : 3));
        entry.Add(0);
        entry.Add(0);
        entry.Add(1);
        PutSized(entry, narrow ? 2 : 4, id);
        Put16(entry, 0);
        Put32(entry, kind == BmffItemKind.Exif ? BmffFileMap.ExifItem : BmffFileMap.MimeItem);
        entry.Add(0);
        if (kind == BmffItemKind.Xmp)
        {
            entry.AddRange(Encoding.ASCII.GetBytes(BmffFileMap.XmpContentType));
            entry.Add(0);
        }
        bytes.AddRange(Sized(entry));
        return Sized(bytes);
    }

    /// <summary>
    /// The item reference box with one more reference: the new item describes (<c>cdsc</c>) the
    /// primary item. A file without the box gets one.
    /// </summary>
    private byte[] WriteReferences(uint id)
    {
        uint primary = map.PrimaryItem!.Value;
        var bytes = new List<byte>(64);
        Put32(bytes, 0);
        Put32(bytes, BmffFileMap.ItemReference);
        int version;
        if (map.References is { } box)
        {
            if (map.ReferenceBoxes != 1)
                throw new MetadataException(MetadataErrorCode.InvalidBmff, "The item reference box appears more than once.", box.Start);
            if (box.End - box.Payload < 4)
                throw new MetadataException(MetadataErrorCode.InvalidBmff, "The item reference box is truncated.", box.Start);
            version = source[box.Payload];
            if (version > 1)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "The item reference box has a version whose references cannot be added to.", box.Start);
            Put(bytes, source.AsSpan(box.Payload, box.End - box.Payload));
        }
        else
        {
            version = id <= ushort.MaxValue && primary <= ushort.MaxValue ? 0 : 1;
            bytes.Add((byte)version);
            bytes.Add(0);
            bytes.Add(0);
            bytes.Add(0);
        }
        if (version == 0 && (id > ushort.MaxValue || primary > ushort.MaxValue))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The item reference box has 16-bit item IDs and the new reference needs wider ones.");
        var reference = new List<byte>(18);
        Put32(reference, 0);
        Put32(reference, 0x63647363); // 'cdsc'
        PutSized(reference, version == 0 ? 2 : 4, id);
        Put16(reference, 1);
        PutSized(reference, version == 0 ? 2 : 4, primary);
        bytes.AddRange(Sized(reference));
        return Sized(bytes);
    }

    private int MetaLength(byte[] locations, byte[] information, byte[] references)
    {
        long length = 8 + 4;
        foreach (BmffBox child in map.Children)
        {
            if (ReferenceEquals(child, map.Locations)) length += locations.Length;
            else if (information.Length != 0 && ReferenceEquals(child, map.Information))
                length += information.Length + (map.References is null ? references.Length : 0);
            else if (references.Length != 0 && ReferenceEquals(child, map.References)) length += references.Length;
            else length += child.Length;
        }
        return checked((int)length);
    }

    /// <summary>
    /// The meta box with the rewritten children in the places of the old ones; a new item reference
    /// box follows the item information box. Every other child is copied as it is.
    /// </summary>
    private byte[] WriteMeta(byte[] locations, byte[] information, byte[] references)
    {
        BmffBox meta = map.MetaBox!;
        var bytes = new List<byte>(meta.Length + 128);
        Put32(bytes, 0);
        Put32(bytes, BmffFileMap.Meta);
        Put(bytes, source.AsSpan(meta.Payload, 4));
        foreach (BmffBox child in map.Children)
        {
            if (ReferenceEquals(child, map.Locations))
            {
                bytes.AddRange(locations);
            }
            else if (information.Length != 0 && ReferenceEquals(child, map.Information))
            {
                bytes.AddRange(information);
                if (map.References is null) bytes.AddRange(references);
            }
            else if (references.Length != 0 && ReferenceEquals(child, map.References))
            {
                bytes.AddRange(references);
            }
            else
            {
                int at = bytes.Count;
                Put(bytes, source.AsSpan(child.Start, child.Length));
                // A child that ran to the end of the old box is given its length: the new box may
                // not be the last thing in the file.
                if (child.OpenEnded)
                {
                    bytes[at] = (byte)(child.Length >> 24);
                    bytes[at + 1] = (byte)(child.Length >> 16);
                    bytes[at + 2] = (byte)(child.Length >> 8);
                    bytes[at + 3] = (byte)child.Length;
                }
            }
        }
        return Sized(bytes);
    }

    /// <summary>The bytes of a box with its length written into its first four bytes.</summary>
    private static byte[] Sized(List<byte> bytes)
    {
        byte[] box = bytes.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        return box;
    }

    /// <summary>The smallest field size of an item location box that holds the value.</summary>
    private static int Width(ulong value) => value > uint.MaxValue ? 8 : 4;

    private static void PutSized(List<byte> bytes, int size, ulong value)
    {
        if (size == 0) return;
        if (size == 2) Put16(bytes, (ushort)value);
        else if (size == 4) Put32(bytes, (uint)value);
        else
        {
            Put32(bytes, (uint)(value >> 32));
            Put32(bytes, (uint)value);
        }
    }

    private static void Put(List<byte> bytes, ReadOnlySpan<byte> span) => CollectionExtensions.AddRange(bytes, span);

    private static void Put16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void Put32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static MetadataException Unsafe(string message, long? offset) =>
        new(MetadataErrorCode.UnsafeRelocation, message, offset);
}
