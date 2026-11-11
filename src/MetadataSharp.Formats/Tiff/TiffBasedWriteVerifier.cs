using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// Reads a planned output again and refuses it unless it is the source with the edit and nothing
/// else. Nothing has been handed to the caller when this runs, so a refusal writes nothing.
/// </summary>
/// <remarks>
/// Three independent checks. The bytes: every byte of the source that is not in a planned range is
/// in the output at the same offset, and no planned range touches image data or a MakerNote. The
/// layout, walked again: every directory is still reached, every tag the edit did not name has the
/// type, count, address and bytes it had, the image data and MakerNote ranges are the ones of the
/// source, and every tag the edit named reads as it was asked to. The reader of the library: it
/// reads the output, and what it returns differs from the source only in the edited tags.
/// </remarks>
internal static class TiffBasedWriteVerifier
{
    internal static void Verify(byte[] source, TiffBasedFileMap before, ExifMetadata exifBefore, byte[] output,
        TiffBasedWritePlan plan, MetadataLimits limits, int maxRanges)
    {
        Bytes(source, before, output, plan);
        TiffBasedFileMap after;
        try { after = TiffBasedFileMap.Read(output, limits, maxRanges); }
        catch (MetadataException failure) { throw Limit(failure) ?? Refuse("the layout of the output is not readable"); }
        Layout(source, before, output, after, plan);
        ExifMetadata exifAfter;
        try { exifAfter = ExifTiffReader.InspectFile(output, limits); }
        catch (MetadataException failure) { throw Limit(failure) ?? Refuse("the reader refuses the output"); }
        Reader(exifBefore, exifAfter, plan);
    }

    private static void Bytes(byte[] source, TiffBasedFileMap before, byte[] output, TiffBasedWritePlan plan)
    {
        if (plan.SourceLength != source.Length || output.Length != plan.OutputLength)
            throw Refuse("the output does not have the planned length");
        int at = 0;
        foreach ((int offset, byte[] bytes) in plan.Patches.OrderBy(patch => patch.Offset))
        {
            if (offset < at || bytes.Length > source.Length - offset)
                throw Refuse("planned ranges overlap or leave the source");
            if (!source.AsSpan(at, offset - at).SequenceEqual(output.AsSpan(at, offset - at)))
                throw Refuse("a byte outside the planned ranges differs");
            if (!output.AsSpan(offset, bytes.Length).SequenceEqual(bytes))
                throw Refuse("a planned range was not written as planned");
            foreach (TiffBasedRange range in before.Ranges)
                if (range.Kind is TiffBasedRangeKind.ImageData or TiffBasedRangeKind.MakerNote &&
                    offset < range.End && range.Start < (long)offset + bytes.Length)
                    throw Refuse("a planned range touches image data or a MakerNote");
            at = offset + bytes.Length;
        }
        if (!source.AsSpan(at).SequenceEqual(output.AsSpan(at, source.Length - at)))
            throw Refuse("a byte outside the planned ranges differs");
        if (!output.AsSpan(source.Length).SequenceEqual(plan.Tail))
            throw Refuse("the appended bytes were not written as planned");
    }

    private static void Layout(byte[] source, TiffBasedFileMap before, byte[] output, TiffBasedFileMap after,
        TiffBasedWritePlan plan)
    {
        if (!Protected(before).SequenceEqual(Protected(after)))
            throw Refuse("image data or MakerNote ranges are not the ones of the source");
        foreach (TiffBasedDirectory was in before.Directories)
        {
            bool edited = was.Role is TiffBasedRole.Primary or TiffBasedRole.Exif or TiffBasedRole.Gps;
            TiffBasedDirectory? now = edited ? after.Find(was.Role) : after.FindAt(was.Offset);
            if (now is null)
            {
                if (edited && plan.Removed.Contains(was.Role)) continue;
                throw Refuse("a directory of the source is no longer reached");
            }
            if (now.Role != was.Role || now.Next != was.Next)
                throw Refuse("a directory changed its place in the file's structure");
            foreach (TiffBasedField field in was.Fields)
            {
                if (edited && plan.Effective.ContainsKey((was.Role, field.Tag))) continue;
                TiffBasedField? same = now.Find(field.Tag);
                if (same is null || same.Type != field.Type || same.Count != field.Count ||
                    same.IsInline != field.IsInline || (!field.IsInline && same.ValueOffset != field.ValueOffset))
                    throw Refuse("a tag the edit did not name changed its type, count or address");
                if (field.ValueOffset <= source.Length - field.ByteLength &&
                    !source.AsSpan((int)field.ValueOffset, (int)field.ByteLength)
                        .SequenceEqual(output.AsSpan((int)same.ValueOffset, (int)same.ByteLength)))
                    throw Refuse("a tag the edit did not name changed its value");
            }
            foreach (TiffBasedField field in now.Fields)
                if (was.Find(field.Tag) is null && !(edited && plan.Effective.ContainsKey((was.Role, field.Tag))))
                    throw Refuse("a directory gained a tag the edit did not name");
        }
        foreach (KeyValuePair<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> change in plan.Effective)
        {
            TiffBasedField? field = after.Find(change.Key.Role)?.Find(change.Key.Tag);
            if (change.Value is not TiffBasedChange value)
            {
                if (field is not null) throw Refuse("a removed tag is still there");
                continue;
            }
            if (field is null || field.Type != value.Type || field.Count != value.Count ||
                field.ByteLength != value.Bytes.Length || field.ValueOffset > output.Length - field.ByteLength ||
                !output.AsSpan((int)field.ValueOffset, value.Bytes.Length).SequenceEqual(value.Bytes))
                throw Refuse("an edited tag does not read as it was written");
        }
    }

    /// <summary>The image data and MakerNote ranges, sorted and joined, so that two walks compare.</summary>
    private static List<(long Start, long End)> Protected(TiffBasedFileMap map)
    {
        var joined = new List<(long Start, long End)>();
        foreach (TiffBasedRange range in map.Ranges
                     .Where(r => r.Kind is TiffBasedRangeKind.ImageData or TiffBasedRangeKind.MakerNote)
                     .OrderBy(r => r.Start).ThenBy(r => r.End))
        {
            if (joined.Count != 0 && range.Start <= joined[^1].End)
                joined[^1] = (joined[^1].Start, Math.Max(joined[^1].End, range.End));
            else
                joined.Add((range.Start, range.End));
        }
        return joined;
    }

    private static void Reader(ExifMetadata before, ExifMetadata after, TiffBasedWritePlan plan)
    {
        var edited = new Dictionary<ExifTagKey, TiffBasedChange?>();
        foreach (KeyValuePair<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> change in plan.Effective)
            edited[new ExifTagKey(Kind(change.Key.Role), change.Key.Tag)] = change.Value;
        foreach (ExifEntry entry in before.Entries)
        {
            if (edited.ContainsKey(entry.Key)) continue;
            // A directory the edit emptied takes nothing else with it: it held edited tags only.
            if (!after.TryGetEntry(entry.Key, out ExifEntry? same) || !Same(entry.Value, same.Value))
                throw Refuse("the reader returns a different value for a tag the edit did not name");
        }
        foreach (ExifEntry entry in after.Entries)
            if (!edited.ContainsKey(entry.Key) && !before.TryGetEntry(entry.Key, out _))
                throw Refuse("the reader returns a tag the edit did not name");
        foreach (KeyValuePair<ExifTagKey, TiffBasedChange?> change in edited)
        {
            bool found = after.TryGetEntry(change.Key, out ExifEntry? entry);
            if (change.Value is not TiffBasedChange value)
            {
                if (found) throw Refuse("the reader still returns a removed tag");
                continue;
            }
            if (!found || (ushort)entry!.Value.FieldType != value.Type || entry.Value.Count != value.Count ||
                !entry.Value.RawBytes.SequenceEqual(value.Bytes))
                throw Refuse("the reader does not return an edited tag as it was written");
        }
        if ((before.MakerNote is null) != (after.MakerNote is null) ||
            (before.MakerNote is not null && after.MakerNote is not null &&
             (before.MakerNote.Format != after.MakerNote.Format || before.MakerNote.Offset != after.MakerNote.Offset ||
              before.MakerNote.Entries.Count != after.MakerNote.Entries.Count)))
            throw Refuse("the MakerNote no longer reads as it did");
    }

    private static bool Same(ExifValue left, ExifValue right) =>
        left.FieldType == right.FieldType && left.Count == right.Count && left.RawBytes.SequenceEqual(right.RawBytes);

    private static ExifDirectoryKind Kind(TiffBasedRole role) => role switch
    {
        TiffBasedRole.Primary => ExifDirectoryKind.Ifd0,
        TiffBasedRole.Exif => ExifDirectoryKind.Exif,
        TiffBasedRole.Gps => ExifDirectoryKind.Gps,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    /// <summary>
    /// An output over a budget is a budget refusal, not a failed check: the edit asked for more than
    /// the limits of the document allow to be read back.
    /// </summary>
    private static MetadataException? Limit(MetadataException failure) =>
        failure.Code == MetadataErrorCode.LimitExceeded
            ? new MetadataException(MetadataErrorCode.LimitExceeded,
                "The edited file exceeds a budget of the limits it would be read with: " + failure.Message)
            : null;

    private static MetadataException Refuse(string what) =>
        new(MetadataErrorCode.UnsafeRelocation, "The planned output failed verification and was not written: " + what + ".");
}
