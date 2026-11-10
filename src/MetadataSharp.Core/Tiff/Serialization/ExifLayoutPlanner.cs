namespace MetadataSharp.Core.Tiff.Serialization;

/// <summary>Plans only reviewed tag shapes; unknown or opaque source data blocks rebuilding.</summary>
internal static class ExifLayoutPlanner
{
    private static readonly ExifDirectoryKind[] Order =
        [ExifDirectoryKind.Ifd0, ExifDirectoryKind.Exif, ExifDirectoryKind.Gps,
         ExifDirectoryKind.Interop, ExifDirectoryKind.Thumbnail];

    internal static ExifWritePlan Create(TiffReadResult? original, ExifMetadata desired,
        MetadataLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(desired);
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
        // Check the source too: removing an unknown tag is not permission to discard it.
        if (original is not null)
            foreach (ExifEntry entry in original.Metadata.Entries)
                ValidateShape(entry);
        foreach (ExifEntry entry in desired.Entries)
            ValidateShape(entry);
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
            cursor = Advance(cursor, 2L + 12L * group.Count + 4);
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
                        cursor = Advance(cursor, value.ByteLength);
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
            cursor = Advance(cursor, thumbnail.Length);
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
            thumbnailOffset, thumbnail is null ? ReadOnlySpan<byte>.Empty : thumbnail.AsSpan());
    }

    private static int Advance(int start, long bytes)
    {
        long end = start + bytes;
        if (end > 65_527)
            throw Failure(MetadataErrorCode.ExifTooLarge, "The EXIF layout does not fit a single JPEG APP1 segment.");
        return (int)end; // Align only when another block follows, not after the final payload.
    }

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

    internal static void ValidateShape(ExifEntry entry)
    {
        ExifValue value = entry.Value;
        var (type, count) = (entry.Key.Directory, entry.Key.TagId) switch
        {
            (ExifDirectoryKind.Ifd0, 0x010E or 0x010F or 0x0110 or 0x0131 or 0x013B or 0x8298) => (TiffFieldType.Ascii, 0U),
            (ExifDirectoryKind.Ifd0, 0x0132) or (ExifDirectoryKind.Exif, 0x9003 or 0x9004) => (TiffFieldType.Ascii, 20U),
            (ExifDirectoryKind.Ifd0, 0x0112) or (ExifDirectoryKind.Thumbnail, 0x0103) => (TiffFieldType.Short, 1U),
            (ExifDirectoryKind.Exif, 0x829A or 0x829D or 0x920A) => (TiffFieldType.Rational, 1U),
            (ExifDirectoryKind.Gps, 0) => (TiffFieldType.Byte, 4U),
            (ExifDirectoryKind.Gps, 1 or 3) => (TiffFieldType.Ascii, 2U),
            (ExifDirectoryKind.Gps, 2 or 4) => (TiffFieldType.Rational, 3U),
            (ExifDirectoryKind.Gps, 5) => (TiffFieldType.Byte, 1U),
            (ExifDirectoryKind.Gps, 6) => (TiffFieldType.Rational, 1U),
            (ExifDirectoryKind.Ifd0, 0x8769 or 0x8825) or (ExifDirectoryKind.Exif, 0xA005) or
                (ExifDirectoryKind.Thumbnail, 0x0201 or 0x0202) => (TiffFieldType.Long, 1U),
            _ => throw Failure(MetadataErrorCode.UnsafeRelocation, "An unknown, opaque or unclassified tag prevents rebuilding EXIF.")
        };
        if (value.FieldType != type || value.Count == 0 || (count != 0 && value.Count != count))
            throw Failure(MetadataErrorCode.UnsupportedFeature, "A tag type or count is outside the reviewed write subset.");
        if (type == TiffFieldType.Ascii)
        {
            if (value.RawBytes[^1] != 0)
                throw Failure(MetadataErrorCode.InvalidExif, "ASCII values must end with NUL.");
            foreach (byte b in value.RawBytes)
                if (b > 127)
                    throw Failure(MetadataErrorCode.InvalidExif, "ASCII values must use seven-bit characters.");
        }
    }

    private static MetadataException Failure(MetadataErrorCode code, string message) => new(code, message);
}
