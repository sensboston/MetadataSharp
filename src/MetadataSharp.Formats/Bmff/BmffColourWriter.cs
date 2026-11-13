using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Bmff;

/// <summary>
/// What a write of the ICC profile does to the bytes of the source: overwrite a few ranges, and add
/// a tail. Every other byte of the source is in the output at the offset it had.
/// </summary>
internal sealed class BmffColourPlan(int sourceLength, List<(int Offset, byte[] Bytes)> patches, byte[] tail,
    BmffMetaPlacement placement, uint primary, int index, bool added, byte[]? profile)
{
    internal int SourceLength { get; } = sourceLength;
    /// <summary>Ranges of the source that are overwritten: none overlaps another.</summary>
    internal IReadOnlyList<(int Offset, byte[] Bytes)> Patches { get; } = patches;
    /// <summary>The bytes after the last source byte.</summary>
    internal byte[] Tail { get; } = tail;
    internal BmffMetaPlacement Placement { get; } = placement;
    /// <summary>The primary item, whose profile this is.</summary>
    internal uint Primary { get; } = primary;
    /// <summary>
    /// The number of the property in the container, from one: the one that held the profile and
    /// holds the new one or has become free space, or the one that was added.
    /// </summary>
    internal int Index { get; } = index;
    /// <summary>Whether the property is new: the primary item had no profile.</summary>
    internal bool Added { get; } = added;
    /// <summary>The profile the primary item is to have; null for none.</summary>
    internal byte[]? Profile { get; } = profile;
    internal int OutputLength => SourceLength + Tail.Length;

    // Apply the requested changes to a staged copy and preserve unrelated BMFF bytes.
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
/// Plans the replacement, the addition or the removal of the ICC profile of the primary item of a
/// BMFF file without moving the data of any item.
/// </summary>
/// <remarks>
/// <para>The profile is a property: a <c>colr</c> box of the kind <c>prof</c> in the property
/// container, which the association box gives the primary item. A profile the item has is replaced
/// where its property is, so that the property keeps its number and every item that is given it
/// has the new profile: the tiles of a grid and a thumbnail commonly share the property of the
/// image they belong to. A profile the item does not have is a new property after the last one,
/// and the primary item alone is given it, before its other properties and as one a reader may
/// ignore. A profile that is removed leaves a <c>free</c> box of eight bytes in its place, which
/// keeps the number of every later property, and no item is given that number any more. Colour
/// information of the kind <c>nclx</c> is another property and is not touched: it tells a decoder
/// how the samples are coded, which a profile does not.</para>
/// <para>The item properties box is rewritten in the bytes it has when it keeps its length or
/// loses at least the eight bytes of a <c>free</c> box, which then follows it inside the
/// <c>meta</c> box; no other child moves. Otherwise the <c>meta</c> box changes its length, as it
/// does for a new item: it grows where it is when it is the last box of the file, or a new one is
/// appended and the old one is turned into a <c>free</c> box by changing its type. The item
/// location box is copied as it is: no item moves, and the items of the item data box are
/// addressed from its start.</para>
/// </remarks>
internal sealed class BmffColourPlanner
{
    private readonly byte[] source;
    private readonly BmffFileMap map;
    private readonly BmffProperties properties;
    private readonly uint primary;
    private readonly byte[]? profile;
    private readonly MetadataLimits limits;
    /// <summary>What every item addresses, in the widest reading of its extents.</summary>
    private readonly List<(BmffSegment Segment, int Method)> others = [];

    // Hold the BMFF property map and ICC edit used to rebuild colour associations.
    private BmffColourPlanner(byte[] source, BmffFileMap map, BmffProperties properties, uint primary, byte[]? profile,
        MetadataLimits limits)
    {
        this.source = source;
        this.map = map;
        this.properties = properties;
        this.primary = primary;
        this.profile = profile;
        this.limits = limits;
    }

    /// <summary>
    /// Plans the output in which the primary item has the profile, or none for null. Null when the
    /// file is that output already: it holds exactly this profile, or has none to remove.
    /// </summary>
    internal static BmffColourPlan? Create(byte[] source, BmffFileMap map, byte[]? profile, MetadataLimits limits)
    {
        BmffProperties? properties = map.MetaBox is null ? null : BmffProperties.Read(source, map, limits);
        var held = new List<int>();
        if (properties is not null && map.PrimaryItem is uint item) held = properties.Profiles(item);
        if (held.Count > 1)
            throw new MetadataException(MetadataErrorCode.AmbiguousMetadata,
                "The primary item has more than one ICC profile.", properties!.Properties[held[1] - 1].Start);
        int index = held.Count == 0 ? 0 : held[0];
        // Nothing to take out and nothing to put in.
        if (profile is null && index == 0) return null;
        if (map.MetaBox is null)
            throw Unsupported("The file has no meta box: there is no item to give a profile to.", null);
        if (map.PrimaryItem is not uint primary)
            throw Unsupported("The file names no primary item to give a profile to.", map.MetaBox.Start);
        if (properties is null)
            throw Unsupported(
                "The meta box has no item properties box with a property container and an association box: there is no place for a profile.",
                map.MetaBox.Start);
        if (index != 0 && profile is not null)
        {
            BmffBox box = properties.Properties[index - 1];
            if (source.AsSpan(box.Payload + 4, box.End - box.Payload - 4).SequenceEqual(profile)) return null;
        }
        return new BmffColourPlanner(source, map, properties, primary, profile, limits).Plan(index);
    }

    private BmffColourPlan Plan(int index)
    {
        var segments = new List<BmffSegment>();
        foreach (BmffLocation location in map.Entries)
        {
            map.TryResolve(location, wide: true, segments);
            foreach (BmffSegment segment in segments) others.Add((segment, location.Method));
        }

        bool added = index == 0;
        int number = added ? properties.Properties.Count + 1 : index;
        if (number > 0x7FFF)
            throw new MetadataException(MetadataErrorCode.LimitExceeded,
                "The property container holds as many properties as an association can number.");
        if (added)
        {
            foreach (BmffAssociationEntry entry in properties.Entries)
                foreach (BmffAssociation association in entry.Properties)
                    if (association.Index == number)
                        throw new MetadataException(MetadataErrorCode.InvalidBmff,
                            "An item is given a property the container does not hold: a new property would take that number.",
                            properties.Associations.Start);
        }

        byte[] container = WriteContainer(index);
        // A profile that replaces one changes no association: the association box is copied.
        byte[]? associations = profile is not null && !added ? null : WriteAssociations(number, added);
        byte[] replacement = WriteProperties(container, associations);

        BmffBox box = properties.Box;
        int spare = box.Length - replacement.Length;
        if ((spare == 0 || spare >= 8) && !Touched(box.Start, box.Length, anyMethod: true))
        {
            byte[] over = new byte[box.Length];
            replacement.CopyTo(over, 0);
            if (spare != 0)
            {
                // What the box no longer needs becomes free space of the meta box, cleared.
                BinaryPrimitives.WriteUInt32BigEndian(over.AsSpan(replacement.Length), (uint)spare);
                BinaryPrimitives.WriteUInt32BigEndian(over.AsSpan(replacement.Length + 4), BmffFileMap.Free);
            }
            return Finish(Difference(box.Start, over), [], BmffMetaPlacement.InPlace, number, added);
        }

        // The meta box changes its length, and new bytes follow the last byte of the source.
        BmffBox meta = map.MetaBox!;
        foreach (BmffBox top in map.TopLevel)
            if (Array.IndexOf(BmffWritePlanner.KnownTopLevel, top.Type) < 0)
                throw Unsupported(
                    "The meta box has to change its length, which appends to the file, and the file has a top-level box whose relation to the end of the file is not known.",
                    top.Start);
        BmffBox last = map.TopLevel[^1];
        if (last.OpenEnded)
            throw Unsafe("The last box of the file runs to its end: the meta box has to change its length, and nothing can follow that box.", last.Start);
        foreach (BmffLocation location in map.Entries)
        {
            if (location.Method != 0) continue;
            if (BmffFileMap.IsOpenEnded(location))
                throw Unsafe("An item is addressed to the end of the file: the meta box has to change its length, and what is appended would make the item longer.", null);
            if (!map.TryResolve(location, wide: true, segments))
                throw Unsafe("An item is addressed past the end of the file: the meta box has to change its length, and what is appended would lie there.", null);
        }

        byte[] rebuilt = WriteMeta(replacement);
        if (ReferenceEquals(last, meta) && rebuilt.Length > meta.Length && !Touched(meta.Start, meta.Length, anyMethod: false))
        {
            // The last box of the file: it grows where it is.
            byte[] over = rebuilt.AsSpan(0, meta.Length).ToArray();
            byte[] rest = rebuilt.AsSpan(meta.Length).ToArray();
            return Finish(Difference(meta.Start, over), rest, BmffMetaPlacement.Extended, number, added);
        }

        if (Touched(meta.Start + 4, 4, anyMethod: true))
            throw Unsafe("An item is addressed inside the header of the meta box, which has to be replaced.", meta.Start);
        byte[] type = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(type, BmffFileMap.Free);
        return Finish([(meta.Start + 4, type)], rebuilt, BmffMetaPlacement.Moved, number, added);
    }

    private BmffColourPlan Finish(List<(int Offset, byte[] Bytes)> patches, byte[] tail, BmffMetaPlacement placement,
        int number, bool added)
    {
        // The output is read back under the same limits, so it may not be larger than an input may be.
        long ceiling = Math.Min(limits.MaxInputBytes, Array.MaxLength);
        if (tail.Length > ceiling - source.Length)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The edited file exceeds the input budget.");
        return new BmffColourPlan(source.Length, patches, tail, placement, primary, number, added, profile);
    }

    /// <summary>
    /// The one range of the source that the new bytes change: from the first byte that differs to
    /// the last one. No range when none differs.
    /// </summary>
    private List<(int Offset, byte[] Bytes)> Difference(int offset, byte[] bytes)
    {
        ReadOnlySpan<byte> old = source.AsSpan(offset, bytes.Length);
        int first = 0, last = bytes.Length;
        while (first < last && old[first] == bytes[first]) first++;
        while (last > first && old[last - 1] == bytes[last - 1]) last--;
        var patches = new List<(int Offset, byte[] Bytes)>();
        if (last > first) patches.Add((offset + first, bytes.AsSpan(first, last - first).ToArray()));
        return patches;
    }

    /// <summary>
    /// Whether an item addresses any of the bytes. An item of the item data box is addressed from
    /// the start of that box, so it only counts where the bytes themselves are to change.
    /// </summary>
    private bool Touched(long start, long length, bool anyMethod)
    {
        foreach ((BmffSegment segment, int method) in others)
            if ((anyMethod || method == 0) && start < segment.End && segment.Start < start + length)
                return true;
        return false;
    }

    /// <summary>
    /// The property container with the profile in the place of the property that held one, or
    /// after the last property; without a profile, with free space in that place.
    /// </summary>
    private byte[] WriteContainer(int index)
    {
        var bytes = new List<byte>(properties.Container.Length + (profile?.Length ?? 0) + 32);
        Put32(bytes, 0); // the size, written last
        Put32(bytes, BmffProperties.ContainerBox);
        for (int i = 0; i < properties.Properties.Count; i++)
        {
            if (i + 1 != index)
            {
                Copy(bytes, properties.Properties[i]);
            }
            else if (profile is null)
            {
                // A box that is no property and takes the number of one: every later number holds.
                Put32(bytes, 8);
                Put32(bytes, BmffFileMap.Free);
            }
            else
            {
                PutProfile(bytes, profile);
            }
        }
        if (index == 0 && profile is not null) PutProfile(bytes, profile);
        return Sized(bytes);
    }

    // Serialize profile in its defined byte order without disturbing adjacent data.
    private static void PutProfile(List<byte> bytes, byte[] profile)
    {
        Put32(bytes, checked((uint)(12 + profile.Length)));
        Put32(bytes, BmffProperties.Colour);
        Put32(bytes, BmffProperties.Profile);
        Put(bytes, profile);
    }

    /// <summary>
    /// The association box with the new property given to the primary item, or with the property
    /// that held the profile given to no item. Every other association is written as it was read.
    /// </summary>
    private byte[] WriteAssociations(int number, bool added)
    {
        var entries = new List<(uint Item, List<BmffAssociation> Properties)>(properties.Entries.Count + 1);
        foreach (BmffAssociationEntry entry in properties.Entries)
        {
            var list = new List<BmffAssociation>(entry.Properties.Count + 1);
            foreach (BmffAssociation association in entry.Properties)
                if (added || association.Index != number) list.Add(association);
            entries.Add((entry.Item, list));
        }
        if (added)
        {
            // Before the other properties of the item: the ones that describe an image precede the
            // ones that transform it, and a profile describes. A reader may ignore it.
            var mine = new BmffAssociation(false, number);
            int at = entries.FindIndex(entry => entry.Item == primary);
            if (at >= 0)
            {
                if (entries[at].Properties.Count >= byte.MaxValue)
                    throw new MetadataException(MetadataErrorCode.LimitExceeded,
                        "The primary item has as many properties as an entry of the association box can count.");
                entries[at].Properties.Insert(0, mine);
            }
            else
            {
                // The entries are in the order of their items.
                int next = entries.FindIndex(entry => entry.Item > primary);
                entries.Insert(next < 0 ? entries.Count : next, (primary, new List<BmffAssociation> { mine }));
            }
        }
        // A width is raised only when the new association does not fit the one the box has.
        bool wide = properties.Wide || (added && number > 0x7F);
        int version = added && primary > ushort.MaxValue ? Math.Max(properties.Version, 1) : properties.Version;

        var bytes = new List<byte>(properties.Associations.Length + 16);
        Put32(bytes, 0);
        Put32(bytes, BmffProperties.AssociationBox);
        Put32(bytes, ((uint)version << 24) | properties.Flags | (wide ? 1u : 0u));
        Put32(bytes, (uint)entries.Count);
        foreach ((uint item, List<BmffAssociation> list) in entries)
        {
            if (version == 0) Put16(bytes, (ushort)item);
            else Put32(bytes, item);
            bytes.Add((byte)list.Count);
            foreach (BmffAssociation association in list)
            {
                if (wide) Put16(bytes, (ushort)((association.Essential ? 0x8000 : 0) | association.Index));
                else bytes.Add((byte)((association.Essential ? 0x80 : 0) | association.Index));
            }
        }
        return Sized(bytes);
    }

    /// <summary>The item properties box with the rewritten children in the places of the old ones.</summary>
    private byte[] WriteProperties(byte[] container, byte[]? associations)
    {
        var bytes = new List<byte>(properties.Box.Length + container.Length);
        Put32(bytes, 0);
        Put32(bytes, BmffProperties.PropertiesBox);
        foreach (BmffBox child in properties.Children)
        {
            if (ReferenceEquals(child, properties.Container)) bytes.AddRange(container);
            else if (associations is not null && ReferenceEquals(child, properties.Associations)) bytes.AddRange(associations);
            else Copy(bytes, child);
        }
        return Sized(bytes);
    }

    /// <summary>The meta box with the rewritten item properties box; every other child is copied as it is.</summary>
    private byte[] WriteMeta(byte[] replacement)
    {
        BmffBox meta = map.MetaBox!;
        var bytes = new List<byte>(meta.Length + replacement.Length);
        Put32(bytes, 0);
        Put32(bytes, BmffFileMap.Meta);
        Put(bytes, source.AsSpan(meta.Payload, 4)); // the version and the flags
        foreach (BmffBox child in map.Children)
        {
            if (ReferenceEquals(child, properties.Box)) bytes.AddRange(replacement);
            else Copy(bytes, child);
        }
        return Sized(bytes);
    }

    /// <summary>
    /// A box of the source, as it is. One that ran to the end of its parent is given its length:
    /// it may no longer be the last.
    /// </summary>
    private void Copy(List<byte> bytes, BmffBox box)
    {
        int at = bytes.Count;
        Put(bytes, source.AsSpan(box.Start, box.Length));
        if (!box.OpenEnded) return;
        bytes[at] = (byte)(box.Length >> 24);
        bytes[at + 1] = (byte)(box.Length >> 16);
        bytes[at + 2] = (byte)(box.Length >> 8);
        bytes[at + 3] = (byte)box.Length;
    }

    /// <summary>The bytes of a box with its length written into its first four bytes.</summary>
    private static byte[] Sized(List<byte> bytes)
    {
        byte[] box = bytes.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        return box;
    }

    private static void Put(List<byte> bytes, ReadOnlySpan<byte> span) => CollectionExtensions.AddRange(bytes, span);

    private static void Put16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    // Serialize put32 in its defined byte order without disturbing adjacent data.
    private static void Put32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    // Report colour properties that this BMFF writer cannot encode.
    private static MetadataException Unsupported(string message, long? offset) =>
        new(MetadataErrorCode.UnsupportedFeature, message, offset);

    // Reject a colour-property rewrite whose retained BMFF data cannot be relocated safely.
    private static MetadataException Unsafe(string message, long? offset) =>
        new(MetadataErrorCode.UnsafeRelocation, message, offset);
}

/// <summary>
/// Holds a planned output of a profile write against the source before it is written. It does not
/// trust the plan: it reads the output again and compares what it finds with what the source had.
/// Every failure is a refusal, so nothing is written.
/// </summary>
/// <remarks>
/// Five independent checks. The bytes: every byte of the source that is not in a planned range is
/// in the output at the same offset. The boxes, walked again: every top-level box of the source is
/// where it was and as long as it was, the <c>meta</c> box alone has the placement the plan names,
/// and its children other than the item properties box and free space are the ones of the source,
/// in their order, byte for byte; the item location box is among them. The items: every item of
/// the source is declared as it was and yields the bytes it yielded. The properties, walked again:
/// the container holds the properties of the source, byte for byte and under the numbers they had,
/// but for the one the plan names, which holds the profile or is free space; the association box
/// gives every item what it gave it, but for the new property, which the primary item is given
/// first, or the removed one, which no item is given. The readers of the library: the reader of
/// items returns for the output what it returned for the source, and the reader of profiles
/// returns the profile that was asked for, or none.
/// </remarks>
internal static class BmffColourVerifier
{
    /// <summary>Throws unless the output is the source with the planned profile and nothing else.</summary>
    /// <param name="source">The whole source file.</param>
    /// <param name="before">The boxes and items of the source.</param>
    /// <param name="readBefore">What the reader of the library returns for the source.</param>
    /// <param name="output">The planned output, applied in memory.</param>
    /// <param name="plan">The plan the output was made from.</param>
    /// <param name="limits">The budgets the output is read with.</param>
    internal static void Verify(byte[] source, BmffFileMap before, BmffMetadata readBefore, byte[] output,
        BmffColourPlan plan, MetadataLimits limits)
    {
        BmffWriteVerifier.Bytes(source, output, plan.SourceLength, plan.OutputLength, plan.Patches, plan.Tail);
        BmffFileMap after;
        BmffProperties? now;
        try
        {
            after = BmffFileMap.Read(output, limits);
            now = BmffProperties.Read(output, after, limits);
        }
        catch (MetadataException failure)
        {
            throw BmffWriteVerifier.Limit(failure) ?? Refuse("the box structure of the output is not readable");
        }
        BmffProperties? was = BmffProperties.Read(source, before, limits);
        if (was is null || now is null) throw Refuse("the item properties box is missing");
        Boxes(source, before, output, after, plan);
        Items(source, before, output, after);
        Properties(source, was, output, now, plan);
        Associations(source, was, output, now, plan);

        BmffMetadata readAfter;
        try { readAfter = BmffReader.Read(output, limits); }
        catch (MetadataException failure) { throw BmffWriteVerifier.Limit(failure) ?? Refuse("the reader refuses the output"); }
        if (readBefore.MajorBrand != readAfter.MajorBrand) throw Refuse("the reader returns another brand");
        if (!BmffWriteVerifier.SameExif(readBefore.Exif, readAfter.Exif))
            throw Refuse("the reader returns different EXIF after a profile edit");
        if ((readBefore.Xmp is null) != (readAfter.Xmp is null) ||
            (readBefore.Xmp is not null &&
             !BmffWriteVerifier.Raw(readBefore.Xmp).AsSpan().SequenceEqual(BmffWriteVerifier.Raw(readAfter.Xmp!))))
            throw Refuse("the reader returns different XMP after a profile edit");

        byte[]? read;
        try { read = IccProfiles.Read(output, limits); }
        catch (MetadataException failure) { throw BmffWriteVerifier.Limit(failure) ?? Refuse("the reader of ICC profiles refuses the output"); }
        bool held = plan.Profile is null ? read is null : read is not null && read.AsSpan().SequenceEqual(plan.Profile);
        if (!held) throw Refuse("the reader does not return the ICC profile as it was asked for");
    }

    private static void Boxes(byte[] source, BmffFileMap before, byte[] output, BmffFileMap after, BmffColourPlan plan)
    {
        if (before.MetaBox is null || after.MetaBox is null) throw Refuse("the meta box is missing");
        int added = after.TopLevel.Count - before.TopLevel.Count;
        if (added != (plan.Placement == BmffMetaPlacement.Moved ? 1 : 0))
            throw Refuse("the number of top-level boxes is not the planned one");
        int last = before.TopLevel.Count - 1;
        for (int i = 0; i <= last; i++)
        {
            BmffBox was = before.TopLevel[i];
            BmffBox now = after.TopLevel[i];
            if (now.Start != was.Start) throw Refuse("a top-level box moved");
            if (ReferenceEquals(was, before.MetaBox))
            {
                bool planned = plan.Placement switch
                {
                    BmffMetaPlacement.InPlace => now.Type == BmffFileMap.Meta && now.Length == was.Length,
                    BmffMetaPlacement.Extended => now.Type == BmffFileMap.Meta && i == last && now.Length > was.Length,
                    BmffMetaPlacement.Moved => now.Type == BmffFileMap.Free && now.Length == was.Length,
                    _ => false
                };
                if (!planned) throw Refuse("the meta box is not where the plan puts it");
                continue;
            }
            if (now.Type != was.Type) throw Refuse("a top-level box changed its type");
            if (now.Length != was.Length) throw Refuse("a top-level box changed its length");
        }
        if (added == 1 && !ReferenceEquals(after.TopLevel[^1], after.MetaBox))
            throw Refuse("what follows the source is not the meta box");

        // The children of the meta box the edit has no business with: everything but the item
        // properties box and free space.
        List<BmffBox> kept = Kept(before), found = Kept(after);
        if (kept.Count != found.Count) throw Refuse("the meta box lost or gained a child");
        for (int i = 0; i < kept.Count; i++)
        {
            if (kept[i].Type != found[i].Type) throw Refuse("the children of the meta box changed their order");
            if (!BmffWriteVerifier.Payload(source, kept[i]).SequenceEqual(BmffWriteVerifier.Payload(output, found[i])))
                throw Refuse("a child of the meta box the edit did not name changed");
        }
        if (!BmffWriteVerifier.Payload(source, before.MetaBox)[..4].SequenceEqual(BmffWriteVerifier.Payload(output, after.MetaBox)[..4]))
            throw Refuse("the version of the meta box changed");
    }

    // Keep Kept focused on BMFF and its format-specific validation contract.
    private static List<BmffBox> Kept(BmffFileMap map)
    {
        var kept = new List<BmffBox>();
        foreach (BmffBox child in map.Children)
            if (child.Type is not (BmffFileMap.Free or BmffProperties.PropertiesBox)) kept.Add(child);
        return kept;
    }

    private static void Items(byte[] source, BmffFileMap before, byte[] output, BmffFileMap after)
    {
        if (after.PrimaryItem != before.PrimaryItem) throw Refuse("the primary item changed");
        if (after.Items.Count != before.Items.Count || after.DeclaredItems != before.DeclaredItems ||
            after.Entries.Count != before.Entries.Count || after.HasUnknownItemEntry != before.HasUnknownItemEntry)
            throw Refuse("the number of declared items changed");
        for (int i = 0; i < before.Items.Count; i++)
            if (after.Items[i] != before.Items[i]) throw Refuse("an item of the source is declared differently");

        var was = new List<BmffSegment>();
        var now = new List<BmffSegment>();
        for (int i = 0; i < before.Entries.Count; i++)
        {
            BmffLocation location = before.Entries[i];
            BmffLocation same = after.Entries[i];
            if (same.Id != location.Id || same.Method != location.Method || same.BaseOffset != location.BaseOffset ||
                !same.Extents.SequenceEqual(location.Extents))
                throw Refuse("an item of the source is addressed differently");
            // An item the source addressed outside itself is addressed as far outside the output.
            bool whole = before.TryResolve(location, wide: true, was);
            bool stillWhole = after.TryResolve(same, wide: true, now);
            if (whole != stillWhole || !BmffWriteVerifier.SameBytes(source, was, output, now))
                throw Refuse("the bytes of an item of the source are not the ones it had");
        }
    }

    private static void Properties(byte[] source, BmffProperties was, byte[] output, BmffProperties now, BmffColourPlan plan)
    {
        if (now.Properties.Count != was.Properties.Count + (plan.Added ? 1 : 0))
            throw Refuse("the property container does not hold the planned number of properties");
        if (plan.Index < 1 || plan.Index > now.Properties.Count || plan.Added != (plan.Index > was.Properties.Count))
            throw Refuse("the plan names a property the container does not hold");
        for (int i = 0; i < now.Properties.Count; i++)
        {
            BmffBox box = now.Properties[i];
            if (i + 1 == plan.Index)
            {
                if (plan.Profile is null)
                {
                    if (box.Type != BmffFileMap.Free) throw Refuse("the property of the removed profile is not free space");
                    continue;
                }
                ReadOnlySpan<byte> payload = BmffWriteVerifier.Payload(output, box);
                if (box.Type != BmffProperties.Colour || payload.Length != 4 + plan.Profile.Length ||
                    BinaryPrimitives.ReadUInt32BigEndian(payload) != BmffProperties.Profile ||
                    !payload[4..].SequenceEqual(plan.Profile))
                    throw Refuse("the property does not hold the profile");
                continue;
            }
            BmffBox old = was.Properties[i];
            if (box.Type != old.Type ||
                !BmffWriteVerifier.Payload(source, old).SequenceEqual(BmffWriteVerifier.Payload(output, box)))
                throw Refuse("a property the edit did not name changed");
        }

        // What the item properties box holds beside the container and the association box.
        List<BmffBox> kept = Others(was), found = Others(now);
        if (kept.Count != found.Count) throw Refuse("the item properties box lost or gained a child");
        for (int i = 0; i < kept.Count; i++)
        {
            if (kept[i].Type != found[i].Type ||
                !BmffWriteVerifier.Payload(source, kept[i]).SequenceEqual(BmffWriteVerifier.Payload(output, found[i])))
                throw Refuse("a child of the item properties box the edit did not name changed");
        }
    }

    // Keep Others focused on BMFF and its format-specific validation contract.
    private static List<BmffBox> Others(BmffProperties properties)
    {
        var others = new List<BmffBox>();
        foreach (BmffBox child in properties.Children)
            if (child.Type is not (BmffProperties.ContainerBox or BmffProperties.AssociationBox)) others.Add(child);
        return others;
    }

    private static void Associations(byte[] source, BmffProperties was, byte[] output, BmffProperties now, BmffColourPlan plan)
    {
        // Bit 0 of the flags is the width of a property number, which a new property may raise.
        if ((now.Flags | 1) != (was.Flags | 1) || (was.Wide && !now.Wide) || now.Version < was.Version)
            throw Refuse("the association box changed its flags or its version");
        if (!plan.Added && plan.Profile is not null)
        {
            // A profile was replaced where its property is: no item is given anything else.
            if (!BmffWriteVerifier.Payload(source, was.Associations).SequenceEqual(BmffWriteVerifier.Payload(output, now.Associations)))
                throw Refuse("the association box changed though no association did");
            return;
        }
        if (!plan.Added)
        {
            // The profile was removed: every entry is the one of the source without its number.
            if (now.Wide != was.Wide || now.Version != was.Version)
                throw Refuse("the association box changed its widths though it lost associations only");
            if (now.Entries.Count != was.Entries.Count) throw Refuse("the association box lost or gained an entry");
            for (int i = 0; i < was.Entries.Count; i++)
            {
                if (now.Entries[i].Item != was.Entries[i].Item ||
                    !now.Entries[i].Properties.SequenceEqual(was.Entries[i].Properties.Where(association => association.Index != plan.Index)))
                    throw Refuse("an association the edit did not name changed");
            }
            return;
        }

        // The profile is new: the primary item is given it first, and nothing else changed.
        if ((now.Wide != was.Wide && plan.Index <= 0x7F) || (now.Version != was.Version && plan.Primary <= ushort.MaxValue))
            throw Refuse("the association box changed its widths without need");
        var mine = new BmffAssociation(false, plan.Index);
        bool given = false;
        int skipped = 0;
        for (int i = 0; i < now.Entries.Count; i++)
        {
            BmffAssociationEntry entry = now.Entries[i];
            BmffAssociationEntry? old = i - skipped < was.Entries.Count ? was.Entries[i - skipped] : null;
            if (entry.Item == plan.Primary && !given)
            {
                given = true;
                if (entry.Properties.Count == 0 || entry.Properties[0] != mine)
                    throw Refuse("the primary item is not given the profile before its other properties");
                if (old is not null && old.Item == plan.Primary)
                {
                    if (!entry.Properties.Skip(1).SequenceEqual(old.Properties))
                        throw Refuse("an association of the primary item changed");
                    continue;
                }
                // The item had no entry: the new one gives it the profile and nothing else.
                if (entry.Properties.Count != 1) throw Refuse("the new entry of the primary item holds more than the profile");
                skipped = 1;
                continue;
            }
            if (old is null || old.Item != entry.Item || !entry.Properties.SequenceEqual(old.Properties))
                throw Refuse("an association the edit did not name changed");
        }
        if (!given || now.Entries.Count - skipped != was.Entries.Count)
            throw Refuse("the association box does not hold the entries of the source and the new association");
    }

    private static MetadataException Refuse(string what) => BmffWriteVerifier.Refuse(what);
}
