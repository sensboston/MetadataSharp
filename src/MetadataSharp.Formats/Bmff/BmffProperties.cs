using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Bmff;

/// <summary>
/// One association of an item with a property: the number of the property in the container,
/// counted from one, and whether a reader has to understand the property to show the item. The
/// number zero associates nothing.
/// </summary>
internal readonly record struct BmffAssociation(bool Essential, int Index);

/// <summary>The properties the association box gives one item, in the order it gives them.</summary>
internal sealed record BmffAssociationEntry(uint Item, IReadOnlyList<BmffAssociation> Properties);

/// <summary>
/// The item properties box of the <c>meta</c> box, as far as the writer of the ICC profile has to
/// know it: the properties of the container, in order, and what the association box gives each
/// item. No property is decoded.
/// </summary>
/// <remarks>
/// The walk is separate from the one of <see cref="IccProfiles"/> on purpose: an edited file is
/// read back by both, and they have to agree. It is the stricter of the two. What it cannot
/// account for is a refusal, not a guess: a second item properties box or property container, a
/// box that leaves its parent, an association box that is truncated or does not end with its last
/// entry (<see cref="MetadataErrorCode.InvalidBmff"/>); a second association box, which the format
/// allows and <see cref="IccProfiles"/> does not read, and an association box of a version that
/// is not defined (<see cref="MetadataErrorCode.UnsupportedFeature"/>).
/// </remarks>
internal sealed class BmffProperties
{
    internal const uint PropertiesBox = 0x69707270;     // 'iprp'
    internal const uint ContainerBox = 0x6970636F;      // 'ipco'
    internal const uint AssociationBox = 0x69706D61;    // 'ipma'
    internal const uint Colour = 0x636F6C72;            // 'colr'
    internal const uint Profile = 0x70726F66;           // 'prof'
    internal const uint RestrictedProfile = 0x72494343; // 'rICC'

    private readonly byte[] file;

    // Capture validated inputs and stable state for this BMFF object.
    private BmffProperties(byte[] file, BmffBox box, List<BmffBox> children, BmffBox container, BmffBox associations,
        List<BmffBox> properties, int version, uint flags, List<BmffAssociationEntry> entries)
    {
        this.file = file;
        Box = box;
        Children = children;
        Container = container;
        Associations = associations;
        Properties = properties;
        Version = version;
        Flags = flags;
        Entries = entries;
    }

    /// <summary>The item properties box.</summary>
    internal BmffBox Box { get; }
    /// <summary>The children of the item properties box, in file order.</summary>
    internal IReadOnlyList<BmffBox> Children { get; }
    internal BmffBox Container { get; }
    internal BmffBox Associations { get; }
    /// <summary>The boxes of the container, in file order: an association names one by its place, from one.</summary>
    internal IReadOnlyList<BmffBox> Properties { get; }
    /// <summary>The version of the association box: item IDs are 16 bits wide in version 0, 32 from version 1.</summary>
    internal int Version { get; }
    /// <summary>The flags of the association box: bit 0 says a property number has 15 bits instead of 7.</summary>
    internal uint Flags { get; }
    internal bool Wide => (Flags & 1) != 0;
    /// <summary>The entries of the association box, in file order.</summary>
    internal IReadOnlyList<BmffAssociationEntry> Entries { get; }

    /// <summary>
    /// The item properties of a file, or null when the <c>meta</c> box has no item properties box,
    /// or that box no property container or no association box: there is nothing an item could
    /// have been given.
    /// </summary>
    internal static BmffProperties? Read(byte[] file, BmffFileMap map, MetadataLimits limits)
    {
        BmffBox? box = null;
        foreach (BmffBox child in map.Children)
        {
            if (child.Type != PropertiesBox) continue;
            if (box is not null) throw Fail("The item properties box appears more than once.", child.Start);
            box = child;
        }
        if (box is null) return null;

        int boxes = 0;
        List<BmffBox> children = Walk(file, box.Payload, box.End, limits, ref boxes);
        BmffBox? container = null, associations = null;
        foreach (BmffBox child in children)
        {
            if (child.Type == ContainerBox)
            {
                if (container is not null) throw Fail("The item property container appears more than once.", child.Start);
                container = child;
            }
            else if (child.Type == AssociationBox)
            {
                if (associations is not null)
                    throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                        "The file has more than one item property association box.", child.Start);
                associations = child;
            }
        }
        if (container is null || associations is null) return null;

        List<BmffBox> properties = Walk(file, container.Payload, container.End, limits, ref boxes);
        if (properties.Count > limits.MaxTotalEntries)
            throw Limit("The item properties exceed their budget.", container.Start);

        int at = associations.Payload, end = associations.End;
        if (end - at < 8) throw Fail("The item property association box is truncated.", at);
        int version = file[at];
        if (version > 1)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The item property association box has a version that is not defined.", at);
        uint flags = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at)) & 0x00FFFFFF;
        uint declared = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 4));
        if (declared > (uint)limits.MaxTotalEntries)
            throw Limit("The item property associations exceed their budget.", at + 4);
        at += 8;
        bool wide = (flags & 1) != 0;
        int idSize = version == 0 ? 2 : 4, width = wide ? 2 : 1;
        var entries = new List<BmffAssociationEntry>((int)declared);
        for (uint entry = 0; entry < declared; entry++)
        {
            if (end - at < idSize + 1) throw Fail("An item property association entry is truncated.", at);
            uint item = idSize == 2
                ? BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at))
                : BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            at += idSize;
            int count = file[at++];
            if (end - at < count * width) throw Fail("An item property association entry is truncated.", at);
            var list = new BmffAssociation[count];
            for (int index = 0; index < count; index++, at += width)
            {
                // The top bit says the property is essential; the rest is its number.
                int number = wide ? BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at)) & 0x7FFF : file[at] & 0x7F;
                list[index] = new BmffAssociation((file[at] & 0x80) != 0, number);
            }
            entries.Add(new(item, list));
        }
        if (at != end) throw Fail("The item property association box does not end with its last entry.", at);
        return new BmffProperties(file, box, children, container, associations, properties, version, flags, entries);
    }

    /// <summary>The entry of an item, or null when the association box has none for it.</summary>
    internal BmffAssociationEntry? Find(uint item)
    {
        BmffAssociationEntry? found = null;
        foreach (BmffAssociationEntry entry in Entries)
        {
            if (entry.Item != item) continue;
            if (found is not null) throw Fail("The item has more than one entry in the item property association box.", Associations.Start);
            found = entry;
        }
        return found;
    }

    /// <summary>
    /// The numbers of the properties of an item that hold an ICC profile, each once, in the order
    /// the item is given them.
    /// </summary>
    internal List<int> Profiles(uint item)
    {
        var found = new List<int>();
        if (Find(item) is not BmffAssociationEntry entry) return found;
        foreach (BmffAssociation association in entry.Properties)
        {
            int index = association.Index;
            if (index == 0 || index > Properties.Count || found.Contains(index)) continue;
            if (IsProfile(file, Properties[index - 1])) found.Add(index);
        }
        return found;
    }

    /// <summary>
    /// Whether a property is colour information that holds a profile: a <c>colr</c> box of the
    /// kind <c>prof</c> or <c>rICC</c>. Colour information of the kind <c>nclx</c> names a colour
    /// space by numbers and is not a profile.
    /// </summary>
    internal static bool IsProfile(byte[] file, BmffBox box) =>
        box.Type == Colour && box.End - box.Payload >= 4 &&
        BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(box.Payload)) is (Profile or RestrictedProfile);

    /// <summary>The boxes that fill a range, each validated against the range.</summary>
    private static List<BmffBox> Walk(byte[] file, int start, int end, MetadataLimits limits, ref int boxes)
    {
        var found = new List<BmffBox>();
        int position = start;
        while (position < end)
        {
            if (++boxes > limits.MaxSegments)
                throw Limit("The box count budget was exceeded.", position);
            int available = end - position;
            if (available < 8) throw Fail("A box header is truncated.", position);
            uint size32 = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(position));
            uint type = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(position + 4));
            int header = 8;
            long size = size32;
            bool open = false;
            if (size32 == 1)
            {
                if (available < 16) throw Fail("An extended box header is truncated.", position);
                ulong large = BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(position + 8));
                if (large > (ulong)available) throw Fail("A box size exceeds the box that encloses it.", position);
                size = (long)large;
                header = 16;
            }
            else if (size32 == 0)
            {
                size = available; // a zero size runs to the end of the enclosing box
                open = true;
            }
            if (size < header) throw Fail("A box size is smaller than its own header.", position);
            if (size > available) throw Fail("A box size exceeds the box that encloses it.", position);
            found.Add(new BmffBox(type, position, header, (int)size, open));
            position += (int)size;
        }
        return found;
    }

    // Preserve the format-specific error code and source offset for malformed BMFF data.
    private static MetadataException Fail(string message, long offset) =>
        new(MetadataErrorCode.InvalidBmff, message, offset);

    // Convert budget exhaustion into the format-specific LimitExceeded error.
    private static MetadataException Limit(string message, long offset) =>
        new(MetadataErrorCode.LimitExceeded, message, offset);
}
