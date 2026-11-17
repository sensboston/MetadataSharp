using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Tiff.Serialization;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// Applies an <see cref="ExifEdit"/> to the EXIF block of a container that holds one as a unit: a
/// JPEG APP1 segment, a PNG eXIf chunk, a WebP EXIF chunk, a BMFF Exif item.
/// </summary>
/// <remarks>
/// <para>A block the file has is edited as the directories of a TIFF file are, by
/// <see cref="TiffBasedWritePlanner"/>: no byte of it moves, a value that fits is written where the
/// old one is, and what grows is appended. So a MakerNote, and whatever a tag this library cannot
/// name may address, stays at its offset. The output is checked before it is returned: by
/// <see cref="TiffBasedWriteVerifier"/>, and by the reader of the container, which has to return
/// the source with the named tags changed and the same thumbnail.</para>
/// <para>A block the file does not have is laid out by <see cref="ExifLayoutPlanner"/> and read
/// back: it holds the tags that were set and the pointers that reach them.</para>
/// </remarks>
internal static class ExifBlockEditor
{
    /// <summary>The ranges a block may be accounted in; a block of 64 KiB cannot hold more.</summary>
    internal const int MaxAccountedRanges = 16_384;

    /// <summary>The changes of an edit as the planner takes them, in the byte order of the structure.</summary>
    internal static void AddChanges(ExifEdit.EditSnapshot edit, Endianness order,
        Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes, bool thumbnailApart = false, bool makerNoteApart = false)
    {
        if (edit.HasThumbnailChange && !thumbnailApart) throw ThumbnailElsewhere();
        if (edit.MakerNote.Count != 0 && !makerNoteApart) throw MakerNoteElsewhere();
        foreach (KeyValuePair<ExifTagKey, ExifValue?> change in edit.Changes)
        {
            TiffBasedRole role = TiffBasedWritePlanner.Role(change.Key.Directory);
            if (change.Value is null)
            {
                changes[(role, change.Key.TagId)] = null;
                continue;
            }
            ExifValue value = ExifEdit.ConvertOrder(change.Value, order);
            changes[(role, change.Key.TagId)] = new((ushort)value.FieldType, value.Count, value.RawBytes.ToArray());
        }
        if (edit.MakerNoteCopy is { } copy)
        {
            ExifValue value = ExifEdit.ConvertOrder(copy.Value, order);
            changes[(TiffBasedRole.Exif, 0x927C)] = new((ushort)value.FieldType, value.Count, value.RawBytes.ToArray());
        }
    }

    /// <summary>
    /// The block of a file that has none: little-endian, the tags the edit sets and the pointers to
    /// their directories. Null when the edit sets nothing, since removing a tag from no block is no
    /// change.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a tag of the thumbnail directory, which
    /// is not created; <see cref="MetadataErrorCode.ExifTooLarge"/> over the ceiling;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> over a budget.
    /// </exception>
    internal static byte[]? Create(ExifEdit.EditSnapshot edit, MetadataLimits limits, int ceiling, out ExifMetadata? desired)
    {
        desired = null;
        if (edit.HasThumbnailChange && ceiling == ExifTiffReader.NoCeiling) throw ThumbnailElsewhere();
        if (edit.MakerNote.Count != 0)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "The file has no EXIF block, so no MakerNote: its tags cannot be changed.");
        if (edit.Changes.Values.All(value => value is null) && edit.MakerNoteCopy is null)
        {
            if (edit.Thumbnail is null) return null;
            // A block of nothing but the thumbnail: a primary directory without entries leads to it.
            desired = new ExifMetadata(Endianness.LittleEndian, []);
            return ExifThumbnailWriter.Apply([0x49, 0x49, 0x2A, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0], edit.Thumbnail, limits, ceiling);
        }
        if (edit.Changes.Any(change => change.Value is not null && change.Key.Directory == ExifDirectoryKind.Thumbnail))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The file has no thumbnail directory: one is not created by a tag edit.");
        desired = edit.Apply(null, Endianness.LittleEndian, limits);
        ExifWritePlan plan = ExifLayoutPlanner.Create(null, desired, limits, ceiling, allowMakerNoteCopy: edit.MakerNoteCopy is not null);
        if (plan.Length > Array.MaxLength)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EXIF block exceeds the length of an array.");
        byte[] block = new byte[plan.Length];
        ExifTiffSerializer.Write(plan, block);

        ExifMetadata written;
        try { written = ExifTiffReader.Inspect(block, limits, 0, ceiling); }
        catch (MetadataException failure) { throw Limit(failure) ?? Refuse("the reader refuses the new block"); }
        if (!written.IsEditable) throw Refuse("the new block is outside the editable profile");
        foreach (ExifEntry entry in desired.Entries)
            if (!written.TryGetEntry(entry.Key, out ExifEntry? same) || !Same(entry.Value, same.Value))
                throw Refuse("the reader does not return a tag as it was set");
        foreach (ExifEntry entry in written.Entries)
            if (!desired.TryGetEntry(entry.Key, out _) && !ExifLayoutPlanner.TryLink(entry.Key.Directory, entry.Key.TagId, out _))
                throw Refuse("the new block holds a tag that was not set");
        if (edit.Thumbnail is not null) block = ExifThumbnailWriter.Apply(block, edit.Thumbnail, limits, ceiling) ?? block;
        if (edit.MakerNoteCopy is { } copy) block = RelocateMakerNote(block, copy, limits);
        return block;
    }

    /// <summary>
    /// The block of a JPEG APP1 segment, or of a chunk held to the same length, with the edit
    /// applied; null when the edit changes nothing in it. <paramref name="sourceBase"/> is the offset
    /// of the block in its file, for the offsets of errors.
    /// </summary>
    /// <exception cref="MetadataException">
    /// What the reader of the block refuses it with; <see cref="MetadataErrorCode.ExifTooLarge"/> when
    /// the edited block is longer than a segment holds;
    /// <see cref="MetadataErrorCode.TagNotEditable"/>, <see cref="MetadataErrorCode.UnsupportedFeature"/>
    /// and <see cref="MetadataErrorCode.UnsafeRelocation"/> as <see cref="TiffBasedWritePlanner"/>
    /// and <see cref="TiffBasedWriteVerifier"/> give them.
    /// </exception>
    internal static byte[]? Edit(byte[] block, long sourceBase, ExifEdit.EditSnapshot edit, MetadataLimits limits)
    {
        // The MakerNote first: nothing of the block moves (a value that grows is appended), and the
        // tag edit that follows moves nothing either.
        byte[]? noted = null;
        if (edit.MakerNote.Count != 0)
        {
            MakerNoteWriter.Result result = MakerNoteWriter.Apply(block, edit.MakerNote, limits, wholeFile: false);
            if (!result.Output.AsSpan().SequenceEqual(block))
            {
                if (result.Output.Length > ExifTiffReader.SegmentCeiling)
                    throw new MetadataException(MetadataErrorCode.ExifTooLarge, "The EXIF block with the grown MakerNote value exceeds the limit of a segment.");
                TiffBasedWriteVerifier.MakerNote(block, TiffBasedFileMap.Read(block, limits, MaxAccountedRanges), result.Output,
                    result.Ranges, limits, MaxAccountedRanges, wholeFile: false);
                noted = result.Output;
            }
        }
        byte[]? output = noted is not null && edit.Changes.Count == 0 && edit.MakerNoteCopy is null
            ? noted : EditTags(noted ?? block, sourceBase, edit, limits) ?? noted;
        if (noted is not null) CheckMakerNote(noted, output!, limits);
        if (edit.MakerNoteCopy is { } copy && output is not null) output = RelocateMakerNote(output, copy, limits);
        if (!edit.HasThumbnailChange) return output;
        // The thumbnail after the tags: its directory is no tag edit, and it is checked on its own.
        return ExifThumbnailWriter.Apply(output ?? block, edit.Thumbnail, limits, ExifTiffReader.SegmentCeiling) ?? output;
    }

    /// <summary>The MakerNote is changed where the EXIF block is a unit of its own.</summary>
    internal static MetadataException MakerNoteElsewhere() => new(MetadataErrorCode.UnsupportedFeature,
        "Tags of the MakerNote are changed in a JPEG, PNG, WebP or TIFF-based file: in this file they are not written.");

    /// <summary>After the tag edit the MakerNote still lies where it did and reads as the change left it.</summary>
    private static void CheckMakerNote(byte[] noted, byte[] output, MetadataLimits limits)
    {
        if (ReferenceEquals(noted, output)) return;
        MakerNote? before = ExifTiffReader.Read(noted, limits).Metadata.MakerNote, after = ExifTiffReader.Read(output, limits).Metadata.MakerNote;
        if (before is null || after is null || before.Offset != after.Offset || before.Format != after.Format ||
            before.Entries.Count != after.Entries.Count)
            throw Refuse("the MakerNote no longer reads as its change left it");
        for (int i = 0; i < before.Entries.Count; i++)
            if (before.Entries[i].TagId != after.Entries[i].TagId || !Same(before.Entries[i].Value, after.Entries[i].Value))
                throw Refuse("a tag of the MakerNote no longer reads as its change left it");
    }

    /// <summary>The thumbnail is written where the EXIF block is a unit of its own.</summary>
    internal static MetadataException ThumbnailElsewhere() => new(MetadataErrorCode.UnsupportedFeature,
        "A thumbnail is set or removed in the EXIF block of a JPEG, PNG, WebP or TIFF-based file: the metadata of this container is read only.");

    private static byte[]? EditTags(byte[] block, long sourceBase, ExifEdit.EditSnapshot edit, MetadataLimits limits)
    {
        // The reader of the container first: a block it refuses is not edited.
        TiffReadResult before = ExifTiffReader.Read(block, limits, sourceBase);
        TiffBasedFileMap map = TiffBasedFileMap.Read(block, limits, MaxAccountedRanges);
        ExifMetadata whole = ExifTiffReader.InspectFile(block, limits);
        var changes = new Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?>();
        AddChanges(edit, map.Order, changes, thumbnailApart: true, makerNoteApart: true);
        TiffBasedWritePlan plan = TiffBasedWritePlanner.Create(block, map, changes, limits, ExifTiffReader.SegmentCeiling,
            emptyPrimary: true, allowMakerNoteCopy: edit.MakerNoteCopy is not null);
        if (plan.IsEmpty) return null;
        byte[] output = plan.Apply(block);
        TiffBasedWriteVerifier.Verify(block, map, whole, output, plan, limits, MaxAccountedRanges,
            allowMakerNoteCopy: edit.MakerNoteCopy is not null);

        TiffReadResult after;
        try { after = ExifTiffReader.Read(output, limits); }
        catch (MetadataException failure) { throw Limit(failure) ?? Refuse("the reader of the block refuses the output"); }
        TiffBasedWriteVerifier.Reader(before.Metadata, after.Metadata, plan,
            allowMakerNoteCopy: edit.MakerNoteCopy is not null);
        if ((before.Thumbnail is null) != (after.Thumbnail is null) ||
            (before.Thumbnail is not null && !before.Thumbnail.AsSpan().SequenceEqual(after.Thumbnail)))
            throw Refuse("the thumbnail no longer reads as it did");
        return output;
    }

    // Keep RelocateMakerNote focused on TIFF/DNG and its format-specific validation contract.
    internal static byte[] RelocateMakerNote(byte[] block, ExifEdit.MakerNoteCopyPayload copy, MetadataLimits limits)
    {
        TiffBasedFileMap map = TiffBasedFileMap.Read(block, limits, MaxAccountedRanges);
        TiffBasedRange? targetRange = map.Ranges
            .Where(range => range.Kind == TiffBasedRangeKind.MakerNote)
            .Select(range => (TiffBasedRange?)range).FirstOrDefault();
        if (targetRange is not TiffBasedRange target || target.Start < 0 || target.Start > block.Length - copy.Value.ByteLength)
            throw Refuse("the copied MakerNote has no safe value range at its new position");
        byte[] moved = MakerNoteRelocator.Relocate(copy.Note, copy.Value, checked((int)target.Start));
        moved.CopyTo(block, (int)target.Start);
        ExifMetadata after = ExifTiffReader.Inspect(block, limits);
        if (after.MakerNote is not MakerNote verified || !MakerNoteRelocator.Same(copy.Note, verified))
            throw Refuse("the MakerNote does not read with the same keys and values after relocation");
        return block;
    }

    private static bool Same(ExifValue left, ExifValue right) =>
        left.FieldType == right.FieldType && left.Count == right.Count && left.RawBytes.SequenceEqual(right.RawBytes);

    // Preserve a nested TIFF limit failure when translating editor errors.
    private static MetadataException? Limit(MetadataException failure) =>
        failure.Code is MetadataErrorCode.LimitExceeded or MetadataErrorCode.ExifTooLarge
            ? new MetadataException(failure.Code, failure.Message)
            : null;

    private static MetadataException Refuse(string what) =>
        new(MetadataErrorCode.UnsafeRelocation, "The planned output failed verification and was not written: " + what + ".");
}
