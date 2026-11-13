using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Bmff;

/// <summary>
/// Holds a planned output against the source before it is written. It does not trust the plan: it
/// reads the output again and compares what it finds with what the source had. Every failure is a
/// refusal, so nothing is written.
/// </summary>
/// <remarks>
/// Four independent checks. The bytes: every byte of the source that is not in a planned range is
/// in the output at the same offset. The boxes, walked again: every top-level box of the source is
/// where it was and as long as it was, the <c>meta</c> box alone may have become a <c>free</c> box
/// or grown at the end of the file, and what follows the source is a media data box, a
/// <c>meta</c> box, or both; the children of the <c>meta</c> box that the edit has no business with
/// are the ones of the source, byte for byte (the item data box of an item written where it is
/// keeps its place and its length; its bytes are held by the first and the third check). The items, through the item location box of the
/// output: every item of the source but the edited one is still declared and yields the same bytes,
/// the edited item yields the planned bytes, and no other item addresses them. The reader of the
/// library: it reads the output, and what it returns differs from the source only in what the edit
/// named.
/// </remarks>
internal static class BmffWriteVerifier
{
    private const uint Description = 0x63647363; // 'cdsc'

    /// <summary>Throws unless the output is the source with the planned edit and nothing else.</summary>
    /// <param name="source">The whole source file.</param>
    /// <param name="before">The boxes and items of the source.</param>
    /// <param name="readBefore">What the reader of the library returns for the source.</param>
    /// <param name="output">The planned output, applied in memory.</param>
    /// <param name="plan">The plan the output was made from.</param>
    /// <param name="limits">The budgets the output is read with.</param>
    /// <param name="tiffPlan">The plan of the EXIF block of an item that was edited, or null.</param>
    /// <param name="desired">The EXIF of an item that was added, or null.</param>
    /// <param name="allowMakerNoteCopy">Whether the plan explicitly adds a MakerNote and its relocation is verified separately.</param>
    internal static void Verify(byte[] source, BmffFileMap before, BmffMetadata readBefore, byte[] output,
        BmffWritePlan plan, MetadataLimits limits, TiffBasedWritePlan? tiffPlan, ExifMetadata? desired,
        bool allowMakerNoteCopy = false)
    {
        Bytes(source, output, plan.SourceLength, plan.OutputLength, plan.Patches, plan.Tail);
        BmffFileMap after;
        try { after = BmffFileMap.Read(output, limits); }
        catch (MetadataException failure) { throw Limit(failure) ?? Refuse("the box structure of the output is not readable"); }
        Boxes(source, before, output, after, plan);
        Items(source, before, output, after, plan);
        BmffMetadata readAfter;
        try { readAfter = BmffReader.Read(output, limits); }
        catch (MetadataException failure) { throw Limit(failure) ?? Refuse("the reader refuses the output"); }
        Reader(readBefore, readAfter, plan, tiffPlan, desired, allowMakerNoteCopy);
    }

    /// <summary>
    /// The output is the source, the planned ranges overwritten and the planned bytes after it:
    /// every other byte is the one of the source, at the offset it had.
    /// </summary>
    internal static void Bytes(byte[] source, byte[] output, int sourceLength, int outputLength,
        IReadOnlyList<(int Offset, byte[] Bytes)> patches, byte[] tail)
    {
        if (sourceLength != source.Length || output.Length != outputLength)
            throw Refuse("the output does not have the planned length");
        int at = 0;
        foreach ((int offset, byte[] bytes) in patches.OrderBy(patch => patch.Offset))
        {
            if (offset < at || bytes.Length > source.Length - offset)
                throw Refuse("planned ranges overlap or leave the source");
            if (!source.AsSpan(at, offset - at).SequenceEqual(output.AsSpan(at, offset - at)))
                throw Refuse("a byte outside the planned ranges differs");
            if (!output.AsSpan(offset, bytes.Length).SequenceEqual(bytes))
                throw Refuse("a planned range was not written as planned");
            at = offset + bytes.Length;
        }
        if (!source.AsSpan(at).SequenceEqual(output.AsSpan(at, source.Length - at)))
            throw Refuse("a byte outside the planned ranges differs");
        if (!output.AsSpan(source.Length).SequenceEqual(tail))
            throw Refuse("the appended bytes were not written as planned");
    }

    private static void Boxes(byte[] source, BmffFileMap before, byte[] output, BmffFileMap after, BmffWritePlan plan)
    {
        if (after.TopLevel.Count < before.TopLevel.Count || after.MetaBox is null || before.MetaBox is null)
            throw Refuse("a top-level box of the source is missing");
        int last = before.TopLevel.Count - 1;
        for (int i = 0; i <= last; i++)
        {
            BmffBox was = before.TopLevel[i];
            BmffBox now = after.TopLevel[i];
            if (now.Start != was.Start) throw Refuse("a top-level box moved");
            if (ReferenceEquals(was, before.MetaBox))
            {
                bool same = now.Type == BmffFileMap.Meta && now.Length == was.Length;
                bool grown = now.Type == BmffFileMap.Meta && i == last && now.Length > was.Length;
                bool freed = now.Type == BmffFileMap.Free && now.Length == was.Length;
                if (!same && !grown && !freed) throw Refuse("the meta box is neither where it was nor replaced");
                continue;
            }
            if (now.Type != was.Type) throw Refuse("a top-level box changed its type");
            // A media data box that runs to the end of the file takes in what is appended to it.
            bool open = i == last && was.OpenEnded && was.Type == BmffFileMap.MediaData && now.OpenEnded;
            if (now.Length != was.Length && !open) throw Refuse("a top-level box changed its length");
        }
        int added = after.TopLevel.Count - before.TopLevel.Count;
        if (added > 2) throw Refuse("more than two boxes follow the source");
        for (int i = last + 1; i < after.TopLevel.Count; i++)
            if (after.TopLevel[i].Type is not (BmffFileMap.MediaData or BmffFileMap.Meta))
                throw Refuse("a box that is neither media data nor the meta box follows the source");

        // The children of the meta box the edit has no business with: everything but the item
        // location box and free space, and the item information and reference boxes of a file that
        // gained an item.
        List<BmffBox> kept = Kept(before, plan.Added), found = Kept(after, plan.Added);
        if (kept.Count != found.Count) throw Refuse("the meta box lost or gained a child");
        for (int i = 0; i < kept.Count; i++)
        {
            if (kept[i].Type != found[i].Type) throw Refuse("the children of the meta box changed their order");
            // An item that was written where it is may lie in the item data box. The box is then
            // where it was, every byte of it outside the planned ranges is the one of the source,
            // and the items it holds are compared one by one below.
            if (plan.Placement == BmffMetaPlacement.Untouched && kept[i].Type == BmffFileMap.ItemData)
            {
                if (kept[i] != found[i]) throw Refuse("the item data box moved or changed its length");
                continue;
            }
            if (!Payload(source, kept[i]).SequenceEqual(Payload(output, found[i])))
                throw Refuse("a child of the meta box the edit did not name changed");
        }
        if (!Payload(source, before.MetaBox)[..4].SequenceEqual(Payload(output, after.MetaBox)[..4]))
            throw Refuse("the version of the meta box changed");
        if (plan.Added) Reference(source, before, output, after, plan);
    }

    private static List<BmffBox> Kept(BmffFileMap map, bool added)
    {
        var kept = new List<BmffBox>();
        foreach (BmffBox child in map.Children)
        {
            if (child.Type is BmffFileMap.ItemLocation or BmffFileMap.Free) continue;
            if (added && child.Type is BmffFileMap.ItemInformation or BmffFileMap.ItemReference) continue;
            kept.Add(child);
        }
        return kept;
    }

    /// <summary>The item reference box of the output is the one of the source and one reference more.</summary>
    private static void Reference(byte[] source, BmffFileMap before, byte[] output, BmffFileMap after, BmffWritePlan plan)
    {
        if (after.References is null || after.ReferenceBoxes != 1 || before.ReferenceBoxes > 1)
            throw Refuse("the new item is not referenced from one item reference box");
        ReadOnlySpan<byte> now = Payload(output, after.References);
        ReadOnlySpan<byte> was = before.References is null ? now[..Math.Min(4, now.Length)] : Payload(source, before.References);
        if (now.Length < 4 || !now.StartsWith(was)) throw Refuse("a reference of the source is gone");
        ReadOnlySpan<byte> reference = now[was.Length..];
        int idSize = now[0] == 0 ? 2 : 4;
        if (now[0] > 1 || reference.Length != 8 + idSize + 2 + idSize ||
            BinaryPrimitives.ReadUInt32BigEndian(reference) != (uint)reference.Length ||
            BinaryPrimitives.ReadUInt32BigEndian(reference[4..]) != Description)
            throw Refuse("the item reference box did not gain exactly one description reference");
        uint from = idSize == 2 ? BinaryPrimitives.ReadUInt16BigEndian(reference[8..]) : BinaryPrimitives.ReadUInt32BigEndian(reference[8..]);
        ReadOnlySpan<byte> rest = reference[(8 + idSize)..];
        uint to = idSize == 2 ? BinaryPrimitives.ReadUInt16BigEndian(rest[2..]) : BinaryPrimitives.ReadUInt32BigEndian(rest[2..]);
        if (from != plan.ItemId || BinaryPrimitives.ReadUInt16BigEndian(rest) != 1 || to != before.PrimaryItem)
            throw Refuse("the new reference does not lead from the new item to the primary item");
    }

    private static void Items(byte[] source, BmffFileMap before, byte[] output, BmffFileMap after, BmffWritePlan plan)
    {
        if (after.PrimaryItem != before.PrimaryItem) throw Refuse("the primary item changed");
        int extra = plan.Added ? 1 : 0;
        if (after.Items.Count != before.Items.Count + extra || after.DeclaredItems != before.DeclaredItems + extra ||
            after.Entries.Count != before.Entries.Count + extra)
            throw Refuse("the number of declared items is not the planned one");
        for (int i = 0; i < before.Items.Count; i++)
            if (after.Items[i] != before.Items[i]) throw Refuse("an item of the source is declared differently");
        if (plan.Added)
        {
            foreach (BmffItem item in before.Items)
                if (item.Id == plan.ItemId) throw Refuse("the new item took the ID of another");
            if (before.Location(plan.ItemId) is not null) throw Refuse("the new item took the location of another");
            BmffItem added = after.Items[^1];
            bool typed = plan.Kind == BmffItemKind.Exif
                ? added.Type == BmffFileMap.ExifItem
                : added.Type == BmffFileMap.MimeItem &&
                  string.Equals(added.ContentType, BmffFileMap.XmpContentType, StringComparison.Ordinal);
            if (added.Id != plan.ItemId || !typed) throw Refuse("the new item is not declared as planned");
        }

        var was = new List<BmffSegment>();
        var now = new List<BmffSegment>();
        var others = new List<BmffSegment>();
        foreach (BmffLocation location in before.Entries)
        {
            if (!plan.Added && location.Id == plan.ItemId) continue;
            BmffLocation same = after.Location(location.Id) ?? throw Refuse("an item of the source has no location");
            if (same.Method != location.Method) throw Refuse("an item of the source is constructed differently");
            bool whole = before.TryResolve(location, wide: true, was);
            bool stillWhole = after.TryResolve(same, wide: true, now);
            others.AddRange(now);
            if (!whole)
            {
                // What the source addressed outside itself is not to be compared: the entry has to
                // be the same, number for number.
                if (stillWhole || same.BaseOffset != location.BaseOffset || !same.Extents.SequenceEqual(location.Extents))
                    throw Refuse("an item that could not be resolved is addressed differently");
                continue;
            }
            if (!stillWhole || !SameBytes(source, was, output, now))
                throw Refuse("the bytes of an item of the source are not the ones it had");
        }

        BmffLocation target = after.Location(plan.ItemId) ?? throw Refuse("the edited item has no location");
        if (BmffFileMap.IsOpenEnded(target) || !after.TryResolve(target, wide: false, now))
            throw Refuse("the edited item is not addressed inside the file");
        long length = 0;
        foreach (BmffSegment segment in now) length += segment.Length;
        if (length != plan.Payload.Length) throw Refuse("the edited item does not have the planned length");
        int at = 0;
        foreach (BmffSegment segment in now)
        {
            if (!output.AsSpan(segment.Start, segment.Length).SequenceEqual(plan.Payload.AsSpan(at, segment.Length)))
                throw Refuse("the edited item does not have the planned bytes");
            at += segment.Length;
            foreach (BmffSegment other in others)
                if (segment.Start < other.End && other.Start < segment.End)
                    throw Refuse("another item addresses the bytes of the edited item");
        }
    }

    /// <summary>Whether two lists of segments yield the same sequence of bytes, however they are cut.</summary>
    internal static bool SameBytes(byte[] left, List<BmffSegment> leftSegments, byte[] right, List<BmffSegment> rightSegments)
    {
        int i = 0, j = 0, leftAt = 0, rightAt = 0;
        while (true)
        {
            while (i < leftSegments.Count && leftAt == leftSegments[i].Length) { i++; leftAt = 0; }
            while (j < rightSegments.Count && rightAt == rightSegments[j].Length) { j++; rightAt = 0; }
            bool leftDone = i == leftSegments.Count, rightDone = j == rightSegments.Count;
            if (leftDone || rightDone) return leftDone && rightDone;
            int count = Math.Min(leftSegments[i].Length - leftAt, rightSegments[j].Length - rightAt);
            if (!left.AsSpan(leftSegments[i].Start + leftAt, count).SequenceEqual(right.AsSpan(rightSegments[j].Start + rightAt, count)))
                return false;
            leftAt += count;
            rightAt += count;
        }
    }

    private static void Reader(BmffMetadata before, BmffMetadata after, BmffWritePlan plan, TiffBasedWritePlan? tiffPlan,
        ExifMetadata? desired, bool allowMakerNoteCopy)
    {
        if (before.MajorBrand != after.MajorBrand) throw Refuse("the reader returns another brand");
        if (plan.Kind == BmffItemKind.Xmp)
        {
            if (!SameExif(before.Exif, after.Exif)) throw Refuse("the reader returns different EXIF after an XMP edit");
            if (after.Xmp is null || !Raw(after.Xmp).AsSpan().SequenceEqual(plan.Payload))
                throw Refuse("the reader does not return the XMP packet as it was written");
            return;
        }
        if ((before.Xmp is null) != (after.Xmp is null) ||
            (before.Xmp is not null && !Raw(before.Xmp).AsSpan().SequenceEqual(Raw(after.Xmp!))))
            throw Refuse("the reader returns different XMP after an EXIF edit");
        if (after.Exif is null) throw Refuse("the reader returns no EXIF");
        if (tiffPlan is not null)
        {
            if (before.Exif is null) throw Refuse("the reader returned no EXIF for the item that was edited");
            TiffBasedWriteVerifier.Reader(before.Exif, after.Exif, tiffPlan, allowMakerNoteCopy);
            return;
        }
        if (before.Exif is not null || desired is null) throw Refuse("an EXIF item was added to a file that had one");
        foreach (ExifEntry entry in desired.Entries)
            if (!(allowMakerNoteCopy && entry.Key == new ExifTagKey(ExifDirectoryKind.Exif, 0x927C)) &&
                (!after.Exif.TryGetEntry(entry.Key, out ExifEntry? written) || !SameValue(entry.Value, written.Value)))
                throw Refuse("the reader does not return a tag of the new EXIF as it was written");
        foreach (ExifEntry entry in after.Exif.Entries)
        {
            // The pointers to the Exif, GPS and Interoperability directories come with the layout.
            bool pointer = (entry.Key.Directory == ExifDirectoryKind.Ifd0 && entry.Key.TagId is 0x8769 or 0x8825) ||
                (entry.Key.Directory == ExifDirectoryKind.Exif && entry.Key.TagId == 0xA005);
            if (!pointer && !desired.TryGetEntry(entry.Key, out _))
                throw Refuse("the reader returns a tag the edit did not name");
        }
    }

    internal static bool SameExif(ExifMetadata? left, ExifMetadata? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.Entries.Count != right.Entries.Count || left.ByteOrder != right.ByteOrder) return false;
        for (int i = 0; i < left.Entries.Count; i++)
            if (left.Entries[i].Key != right.Entries[i].Key || !SameValue(left.Entries[i].Value, right.Entries[i].Value))
                return false;
        return true;
    }

    private static bool SameValue(ExifValue left, ExifValue right) =>
        left.FieldType == right.FieldType && left.Count == right.Count && left.RawBytes.SequenceEqual(right.RawBytes);

    internal static byte[] Raw(XmpPacket packet)
    {
        byte[] bytes = new byte[packet.ByteLength];
        packet.CopyRawTo(bytes);
        return bytes;
    }

    internal static ReadOnlySpan<byte> Payload(byte[] file, BmffBox box) => file.AsSpan(box.Payload, box.End - box.Payload);

    /// <summary>
    /// An output over a budget is a budget refusal, not a failed check: the edit asked for more than
    /// the limits of the document allow to be read back.
    /// </summary>
    internal static MetadataException? Limit(MetadataException failure) =>
        failure.Code is MetadataErrorCode.LimitExceeded
            ? new MetadataException(failure.Code,
                "The edited file exceeds a budget of the limits it would be read with: " + failure.Message)
            : null;

    internal static MetadataException Refuse(string what) =>
        new(MetadataErrorCode.UnsafeRelocation, "The planned output failed verification and was not written: " + what + ".");
}
