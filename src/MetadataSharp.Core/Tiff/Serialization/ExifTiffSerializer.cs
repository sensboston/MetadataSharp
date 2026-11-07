using System.Buffers.Binary;

namespace MetadataSharp.Core.Tiff.Serialization;

/// <summary>Validates a complete layout before touching caller-owned output.</summary>
internal static class ExifTiffSerializer
{
    internal static void Write(ExifWritePlan plan, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Validate(plan);
        if (destination.Length < plan.Length)
            throw new ArgumentException("The destination is too short for the EXIF plan.", nameof(destination));
        Span<byte> output = destination[..plan.Length];
        output.Clear(); // Includes inline padding, alignment gaps and next-IFD zeroes.
        output[0] = output[1] = plan.ByteOrder == Endianness.LittleEndian ? (byte)0x49 : (byte)0x4D;
        Put16(output, 2, 42, plan.ByteOrder);
        Put32(output, 4, 8, plan.ByteOrder);
        foreach (PlannedDirectory directory in plan.Directories)
        {
            Put16(output, directory.Offset, (ushort)directory.Entries.Count, plan.ByteOrder);
            for (int i = 0; i < directory.Entries.Count; i++)
            {
                PlannedEntry entry = directory.Entries[i];
                int at = directory.Offset + 2 + i * 12;
                Put16(output, at, entry.TagId, plan.ByteOrder);
                Put16(output, at + 2, (ushort)entry.FieldType, plan.ByteOrder);
                Put32(output, at + 4, entry.Count, plan.ByteOrder);
                if (entry.ScalarOverride is uint scalar)
                    Put32(output, at + 8, scalar, plan.ByteOrder);
                else if (entry.DataOffset is int offset)
                {
                    Put32(output, at + 8, (uint)offset, plan.ByteOrder);
                    entry.Value!.CopyRawTo(output[offset..]);
                }
                else entry.Value!.CopyRawTo(output.Slice(at + 8, 4));
            }
            Put32(output, directory.Offset + 2 + directory.Entries.Count * 12,
                directory.NextIfdOffset, plan.ByteOrder);
        }
        if (plan.ThumbnailOffset is int thumbnailAt)
            plan.Thumbnail.CopyTo(output[thumbnailAt..]);
    }

    private static void Validate(ExifWritePlan plan)
    {
        if (plan.ByteOrder is not Endianness.LittleEndian and not Endianness.BigEndian ||
            plan.Length is < 14 or > 65_527 || plan.Directories.Count is < 1 or > 5)
            throw Invalid("The EXIF plan header or size is invalid.");
        var offsets = new Dictionary<ExifDirectoryKind, int>();
        long cursor = 8;
        int previousRole = -1;
        foreach (PlannedDirectory directory in plan.Directories)
        {
            if ((int)directory.Kind <= previousRole || !Enum.IsDefined(directory.Kind) ||
                directory.Entries.Count > ushort.MaxValue || directory.Offset != cursor)
                throw Invalid("Planned directories are unordered, overlapping or inconsistent.");
            previousRole = (int)directory.Kind;
            offsets.Add(directory.Kind, directory.Offset);
            cursor += 2L + 12L * directory.Entries.Count + 4;
            if (cursor > plan.Length) throw Invalid("A planned directory exceeds output bounds.");
        }
        if (!offsets.ContainsKey(ExifDirectoryKind.Ifd0)) throw Invalid("The plan lacks IFD0.");
        if (offsets.ContainsKey(ExifDirectoryKind.Interop) && !offsets.ContainsKey(ExifDirectoryKind.Exif))
            throw Invalid("An interoperability directory requires an Exif parent.");
        foreach (PlannedDirectory directory in plan.Directories)
        {
            uint expectedNext = directory.Kind == ExifDirectoryKind.Ifd0 &&
                offsets.TryGetValue(ExifDirectoryKind.Thumbnail, out int thumb) ? (uint)thumb : 0;
            if (directory.NextIfdOffset != expectedNext) throw Invalid("A next-IFD pointer is inconsistent.");
            int previousTag = -1;
            foreach (PlannedEntry entry in directory.Entries)
            {
                if (entry.TagId <= previousTag) throw Invalid("Planned tags must be unique and sorted.");
                previousTag = entry.TagId;
                bool link = ExifLayoutPlanner.TryLink(directory.Kind, entry.TagId, out ExifDirectoryKind target);
                bool thumbnailPointer = directory.Kind == ExifDirectoryKind.Thumbnail && entry.TagId is 0x0201 or 0x0202;
                if (link || thumbnailPointer)
                {
                    uint expected = link ? (offsets.TryGetValue(target, out int at) ? (uint)at : 0)
                        : entry.TagId == 0x0201 ? (uint)(plan.ThumbnailOffset ?? 0) : (uint)plan.Thumbnail.Length;
                    if (entry.Value is not null || entry.DataOffset is not null || entry.ScalarOverride != expected ||
                        entry.FieldType != TiffFieldType.Long || entry.Count != 1)
                        throw Invalid("A structural pointer has an invalid representation or target.");
                }
                else
                {
                    ExifValue? value = entry.Value;
                    if (value is null || value.ByteOrder != plan.ByteOrder || value.FieldType != entry.FieldType ||
                        value.Count != entry.Count || entry.ScalarOverride is not null)
                        throw Invalid("A planned value is missing or inconsistent.");
                    ExifLayoutPlanner.ValidateShape(new ExifEntry(new(directory.Kind, entry.TagId), value));
                    if (directory.Kind == ExifDirectoryKind.Thumbnail && entry.TagId == 0x0103 &&
                        (value.RawBytes.Length != 2 ||
                         (plan.ByteOrder == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(value.RawBytes)
                         : BinaryPrimitives.ReadUInt16BigEndian(value.RawBytes)) != 6))
                        throw Invalid("The thumbnail compression selector is not JPEG.");
                    if (value.ByteLength > 4)
                    {
                        cursor = (cursor + 1) & ~1L;
                        if (entry.DataOffset != cursor) throw Invalid("An external value has a stale or overlapping offset.");
                        cursor += value.ByteLength;
                        if (cursor > plan.Length) throw Invalid("An external value exceeds output bounds.");
                    }
                    else if (entry.DataOffset is not null) throw Invalid("An inline value cannot have an external offset.");
                }
            }
            RequireParentLink(directory, ExifDirectoryKind.Ifd0, 0x8769, ExifDirectoryKind.Exif, offsets);
            RequireParentLink(directory, ExifDirectoryKind.Ifd0, 0x8825, ExifDirectoryKind.Gps, offsets);
            RequireParentLink(directory, ExifDirectoryKind.Exif, 0xA005, ExifDirectoryKind.Interop, offsets);
            if (directory.Kind == ExifDirectoryKind.Thumbnail &&
                (!directory.Entries.Any(e => e.TagId == 0x0103) || !directory.Entries.Any(e => e.TagId == 0x0201) ||
                 !directory.Entries.Any(e => e.TagId == 0x0202)))
                throw Invalid("The thumbnail directory lacks required fields.");
        }
        bool hasThumbnail = offsets.ContainsKey(ExifDirectoryKind.Thumbnail);
        if (hasThumbnail)
        {
            cursor = (cursor + 1) & ~1L;
            ReadOnlySpan<byte> jpeg = plan.Thumbnail;
            if (plan.ThumbnailOffset != cursor || jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 ||
                jpeg[^2] != 0xFF || jpeg[^1] != 0xD9)
                throw Invalid("The planned thumbnail placement or boundaries are invalid.");
            cursor += jpeg.Length;
        }
        else if (plan.ThumbnailOffset is not null || !plan.Thumbnail.IsEmpty)
            throw Invalid("Thumbnail bytes lack a directory.");
        if (cursor != plan.Length) throw Invalid("The planned output length does not match its contents.");
    }

    private static void RequireParentLink(PlannedDirectory directory, ExifDirectoryKind owner,
        ushort tag, ExifDirectoryKind target, Dictionary<ExifDirectoryKind, int> offsets)
    {
        if (directory.Kind == owner && offsets.ContainsKey(target) && !directory.Entries.Any(e => e.TagId == tag))
            throw Invalid("A child directory lacks its parent pointer.");
    }

    private static void Put16(Span<byte> output, int at, ushort value, Endianness order)
    {
        if (order == Endianness.LittleEndian) BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(at, 2), value);
        else BinaryPrimitives.WriteUInt16BigEndian(output.Slice(at, 2), value);
    }

    private static void Put32(Span<byte> output, int at, uint value, Endianness order)
    {
        if (order == Endianness.LittleEndian) BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(at, 4), value);
        else BinaryPrimitives.WriteUInt32BigEndian(output.Slice(at, 4), value);
    }

    private static MetadataException Invalid(string message) => new(MetadataErrorCode.InvalidExif, message);
}
