namespace MetadataSharp.Core.Tiff.Serialization;

/// <summary>
/// Lays out an EXIF block from its tags: any tag an <see cref="ExifEdit"/> takes, with the pointers
/// and the thumbnail pair generated. A source block is carried only when every tag of it can be shown
/// free of addresses: a tag the vocabulary does not list, a MakerNote or an image-data address in the
/// source blocks rebuilding, and such a block is edited where it lies instead.
/// </summary>
internal static class ExifLayoutPlanner
{
    private static readonly ExifDirectoryKind[] Order =
        [ExifDirectoryKind.Ifd0, ExifDirectoryKind.Exif, ExifDirectoryKind.Gps,
         ExifDirectoryKind.Interop, ExifDirectoryKind.Thumbnail];

    /// <summary>
    /// Lays the block out. A layout longer than <paramref name="ceiling"/> is refused as too large
    /// for its container: by default the block has to fit a JPEG APP1 segment, and
    /// <see cref="ExifTiffReader.NoCeiling"/> leaves its length to the budgets.
    /// </summary>
    internal static ExifWritePlan Create(TiffReadResult? original, ExifMetadata desired,
        MetadataLimits? limits = null, int ceiling = ExifTiffReader.SegmentCeiling, bool allowMakerNoteCopy = false)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (ceiling < 14) throw new ArgumentOutOfRangeException(nameof(ceiling));
        limits ??= new();
        limits.Validate();
        if (original is not null && original.Metadata.ByteOrder != desired.ByteOrder)
            throw new ArgumentException("An existing EXIF payload must retain its byte order.", nameof(desired));
        // The read drops a thumbnail whose JPEG fails its own validation but keeps the thumbnail IFD, and
        // no JPEG bytes are retained for it. A rebuild of that file would have to carry a picture the
        // reader refused to own: refuse the edit rather than silently discard it or write dangling pair
        // entries. Removal of the whole payload is still allowed; it is not a rebuild.
        if (original is not null && original.Thumbnail is null &&
            original.Directories.Contains(ExifDirectoryKind.Thumbnail))
            throw Failure(MetadataErrorCode.UnsafeRelocation,
                "The source thumbnail IFD cannot be carried because its JPEG stream was not retained.");
        // Check the source too: removing an unknown tag is not permission to discard it, and a value
        // that may hold an address cannot be shown safe to move.
        if (original is not null)
            foreach (ExifEntry entry in original.Metadata.Entries)
            {
                ValidateShape(entry, allowMakerNoteCopy);
                if (!Generated(entry.Key) && ExifTags.Find(entry.Key) is null)
                    throw Failure(MetadataErrorCode.UnsafeRelocation, "An unknown tag of the source prevents rebuilding EXIF.");
            }
        foreach (ExifEntry entry in desired.Entries)
            ValidateShape(entry, allowMakerNoteCopy);
        ValidateProtectedEntries(original, desired);

        var groups = new Dictionary<ExifDirectoryKind, SortedDictionary<ushort, ExifValue?>>();
        groups.Add(ExifDirectoryKind.Ifd0, []);
        if (original is not null)
            foreach (ExifDirectoryKind role in original.Directories)
                groups.TryAdd(role, []); // Preserve empty source directories.
        foreach (ExifEntry entry in desired.Entries)
        {
            if (!groups.TryGetValue(entry.Key.Directory, out var group))
            {
                group = [];
                groups.Add(entry.Key.Directory, group);
            }
            group.Add(entry.Key.TagId, entry.Value);
        }
        if (groups.ContainsKey(ExifDirectoryKind.Interop))
            groups.TryAdd(ExifDirectoryKind.Exif, []);
        if (groups.ContainsKey(ExifDirectoryKind.Exif)) groups[ExifDirectoryKind.Ifd0][0x8769] = null;
        if (groups.ContainsKey(ExifDirectoryKind.Gps)) groups[ExifDirectoryKind.Ifd0][0x8825] = null;
        if (groups.ContainsKey(ExifDirectoryKind.Interop)) groups[ExifDirectoryKind.Exif][0xA005] = null;

        if (groups.Count > limits.MaxDirectories)
            throw Failure(MetadataErrorCode.LimitExceeded, "The planned directory count exceeds its budget.");
        int depth = groups.ContainsKey(ExifDirectoryKind.Interop) ? 3 : groups.Count > 1 ? 2 : 1;
        if (depth > limits.MaxDirectoryDepth)
            throw Failure(MetadataErrorCode.LimitExceeded, "The planned directory depth exceeds its budget.");
        int total = 0;
        int cursor = 8;
        var offsets = new Dictionary<ExifDirectoryKind, int>();
        foreach (ExifDirectoryKind role in Order)
        {
            if (!groups.TryGetValue(role, out var group)) continue;
            if (group.Count > ushort.MaxValue || group.Count > limits.MaxEntriesPerDirectory ||
                group.Count > limits.MaxTotalEntries - total)
                throw Failure(MetadataErrorCode.LimitExceeded, "The planned entry count exceeds its budget.");
            total += group.Count;
            offsets.Add(role, cursor);
            cursor = Advance(cursor, 2L + 12L * group.Count + 4, ceiling);
        }

        long retained = original?.Thumbnail?.Length ?? 0;
        if (original is not null)
            foreach (ExifEntry entry in original.Metadata.Entries) retained += entry.Value.ByteLength;
        foreach (ExifEntry entry in desired.Entries)
        {
            if (entry.Value.ByteLength > limits.MaxValueBytes)
                throw Failure(MetadataErrorCode.LimitExceeded, "A planned value exceeds its budget.");
            retained += entry.Value.ByteLength;
        }
        var planned = new List<PlannedDirectory>();
        foreach (ExifDirectoryKind role in Order)
        {
            if (!groups.TryGetValue(role, out var group)) continue;
            var values = new List<PlannedEntry>();
            foreach (var item in group)
            {
                ushort tag = item.Key;
                if (TryLink(role, tag, out ExifDirectoryKind target))
                {
                    if (limits.MaxValueBytes < 4)
                        throw Failure(MetadataErrorCode.LimitExceeded, "A generated pointer exceeds the value budget.");
                    uint pointer = offsets.TryGetValue(target, out int at) ? (uint)at : 0;
                    values.Add(new(tag, TiffFieldType.Long, 1, null, null, pointer));
                }
                else if (role == ExifDirectoryKind.Thumbnail && tag is 0x0201 or 0x0202)
                {
                    // Filled after external values are placed.
                    values.Add(new(tag, TiffFieldType.Long, 1, null, null, 0));
                }
                else
                {
                    ExifValue value = item.Value!;
                    int? at = null;
                    if (value.ByteLength > 4)
                    {
                        cursor = (cursor + 1) & ~1;
                        at = cursor;
                        cursor = Advance(cursor, value.ByteLength, ceiling);
                    }
                    values.Add(new(tag, value.FieldType, value.Count, value, at, null));
                }
            }
            uint next = role == ExifDirectoryKind.Ifd0 && offsets.TryGetValue(ExifDirectoryKind.Thumbnail, out int thumbIfd)
                ? (uint)thumbIfd : 0;
            planned.Add(new(role, offsets[role], next, values.AsReadOnly()));
        }
        byte[]? thumbnail = original?.Thumbnail;
        int? thumbnailOffset = null;
        if (thumbnail is not null)
        {
            if (thumbnail.Length > limits.MaxThumbnailBytes)
                throw Failure(MetadataErrorCode.LimitExceeded, "The planned thumbnail exceeds its budget.");
            cursor = (cursor + 1) & ~1;
            thumbnailOffset = cursor;
            cursor = Advance(cursor, thumbnail.Length, ceiling);
            retained += thumbnail.Length;
            for (int i = 0; i < planned.Count; i++)
            {
                if (planned[i].Kind != ExifDirectoryKind.Thumbnail) continue;
                PlannedEntry[] fixedEntries = planned[i].Entries.Select(e => e.TagId switch
                {
                    0x0201 => e with { ScalarOverride = (uint)thumbnailOffset.Value },
                    0x0202 => e with { ScalarOverride = (uint)thumbnail.Length },
                    _ => e
                }).ToArray();
                planned[i] = planned[i] with { Entries = Array.AsReadOnly(fixedEntries) };
            }
        }
        if (retained + cursor > limits.MaxRetainedMetadataBytes)
            throw Failure(MetadataErrorCode.LimitExceeded, "The source, desired values and staged output exceed the metadata budget.");
        return new ExifWritePlan(desired.ByteOrder, cursor, planned.AsReadOnly(),
            thumbnailOffset, thumbnail is null ? ReadOnlySpan<byte>.Empty : thumbnail.AsSpan(), ceiling);
    }

    // Add a payload extent without final alignment; the next block owns any padding before it.
    private static int Advance(int start, long bytes, int ceiling)
    {
        long end = start + bytes;
        if (end > ceiling)
            throw Failure(MetadataErrorCode.ExifTooLarge, ceiling == ExifTiffReader.SegmentCeiling
                ? "The EXIF layout does not fit a single JPEG APP1 segment."
                : "The EXIF layout is longer than its container allows.");
        return (int)end; // Align only when another block follows, not after the final payload.
    }

    // Keep pointers and thumbnail selectors identical to the source so only the planner can safely rewrite them.
    private static void ValidateProtectedEntries(TiffReadResult? original, ExifMetadata desired)
    {
        foreach (ExifEntry entry in desired.Entries)
        {
            if (!Protected(entry.Key)) continue;
            if (original is null || !original.Metadata.TryGetEntry(entry.Key, out ExifEntry? source) ||
                source.Value.FieldType != entry.Value.FieldType || source.Value.Count != entry.Value.Count ||
                !source.Value.RawBytes.SequenceEqual(entry.Value.RawBytes))
                throw Failure(MetadataErrorCode.UnsafeRelocation, "Structural and thumbnail fields cannot be edited as raw values.");
        }
        if (original is not null)
            foreach (ExifEntry entry in original.Metadata.Entries)
                if (Protected(entry.Key) && !desired.TryGetEntry(entry.Key, out _))
                    throw Failure(MetadataErrorCode.UnsafeRelocation, "Structural and thumbnail fields cannot be removed individually.");
    }

    private static bool Protected(ExifTagKey key) => key.Directory == ExifDirectoryKind.Thumbnail ||
        TryLink(key.Directory, key.TagId, out _);

    // Centralize the three supported parent-pointer tags shared by planning and serialization checks.
    internal static bool TryLink(ExifDirectoryKind role, ushort tag, out ExifDirectoryKind target)
    {
        target = (role, tag) switch
        {
            (ExifDirectoryKind.Ifd0, 0x8769) => ExifDirectoryKind.Exif,
            (ExifDirectoryKind.Ifd0, 0x8825) => ExifDirectoryKind.Gps,
            (ExifDirectoryKind.Exif, 0xA005) => ExifDirectoryKind.Interop,
            _ => ExifDirectoryKind.Ifd0
        };
        return target != ExifDirectoryKind.Ifd0;
    }

    /// <summary>The tags the layout writes itself: the directory pointers and the thumbnail pair.</summary>
    private static bool Generated(ExifTagKey key) => TryLink(key.Directory, key.TagId, out _) ||
        (key.Directory == ExifDirectoryKind.Thumbnail && key.TagId is 0x0201 or 0x0202);

    /// <summary>
    /// Whether the layout has a place for the entry: a generated tag is one LONG, the compression of a
    /// carried thumbnail one SHORT, and any other tag is one an edit takes, with at least one element.
    /// The content of a value is the business of the edit that set it.
    /// </summary>
    internal static void ValidateShape(ExifEntry entry, bool allowMakerNoteCopy = false)
    {
        ExifValue value = entry.Value;
        if (allowMakerNoteCopy && entry.Key == new ExifTagKey(ExifDirectoryKind.Exif, 0x927C) && value.FieldType == TiffFieldType.Undefined)
            return;
        if (Generated(entry.Key))
        {
            if (value.FieldType != TiffFieldType.Long || value.Count != 1)
                throw Failure(MetadataErrorCode.UnsupportedFeature, "A pointer or thumbnail address is not a single LONG.");
            return;
        }
        if (!ExifEdit.IsEditable(entry.Key))
            throw Failure(MetadataErrorCode.UnsafeRelocation, "A MakerNote, a pointer or an image-data address prevents rebuilding EXIF.");
        if (value.Count == 0)
            throw Failure(MetadataErrorCode.UnsupportedFeature, "A tag without a value has no place in the layout.");
        if (entry.Key == new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x0103) && (value.FieldType != TiffFieldType.Short || value.Count != 1))
            throw Failure(MetadataErrorCode.UnsupportedFeature, "The compression of the thumbnail is not a single SHORT.");
    }

    private static MetadataException Failure(MetadataErrorCode code, string message) => new(code, message);
}
