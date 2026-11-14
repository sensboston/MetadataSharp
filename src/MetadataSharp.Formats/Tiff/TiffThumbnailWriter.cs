using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// Sets or removes the thumbnail directory (IFD1) of a TIFF-based file: the directory the primary
/// directory's successor link names, and the JPEG stream it addresses.
/// </summary>
/// <remarks>
/// <para>The file is copied and the directory is appended to its end, so nothing moves: no offset the
/// file already holds changes, and every byte of the image data stays where it is. Only the four
/// bytes of the primary directory's successor link are rewritten, and, where a picture is replaced or
/// removed, the ranges that only the old thumbnail addressed are cleared. This is
/// <c>exiv2 -it</c> and <c>exiv2 -dt</c>, which re-encode the whole file; here nothing else is
/// written at all.</para>
/// <para>A directory that is appended states the three tags Exif requires of a JPEG thumbnail
/// (XResolution, YResolution and ResolutionUnit of 72 dpi) when it is created and keeps the other
/// tags of the directory it replaces as they are, at the offsets where their values already lie.</para>
/// <para>The result is read back before it is returned: the strict reader of the file has to return
/// the new stream as the thumbnail (or none), every tag outside the thumbnail directory has to read
/// byte for byte as before, and the bytes of the source have to be unchanged but where the link and
/// the cleared ranges are.</para>
/// </remarks>
internal static class TiffThumbnailWriter
{
    private const ushort Compression = 0x0103;
    private const ushort XResolution = 0x011A;
    private const ushort YResolution = 0x011B;
    private const ushort ResolutionUnit = 0x0128;
    private const ushort StripOffsets = 0x0111;
    private const ushort StripByteCounts = 0x0117;
    private const ushort TileOffsets = 0x0144;
    private const ushort TileByteCounts = 0x0145;
    private const ushort JpegInterchangeFormat = 0x0201;
    private const ushort JpegInterchangeFormatLength = 0x0202;

    /// <summary>
    /// The file with the thumbnail set to <paramref name="jpeg"/>, or with it removed for null; the
    /// source itself when there is nothing to remove.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the directory after the primary one
    /// is an image of the file rather than a thumbnail of an EXIF block, and for a stream the reader
    /// does not take; <see cref="MetadataErrorCode.LimitExceeded"/> for a stream over the thumbnail
    /// budget; <see cref="MetadataErrorCode.UnsafeRelocation"/> when the output does not read back as
    /// planned.
    /// </exception>
    internal static byte[] Apply(byte[] file, TiffBasedFileMap map, byte[]? jpeg, MetadataLimits limits, int maxRanges)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(map);
        TiffBasedDirectory primary = map.Find(TiffBasedRole.Primary)
            ?? throw new MetadataException(MetadataErrorCode.InvalidTiff, "The file has no primary directory.");
        if (primary.References > 1)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "The primary directory is reached from more than one pointer: its successor link is not changed.");
        long link = (long)primary.Offset + primary.Length - 4;
        if (link < 8 || link > file.Length - 4)
            throw new MetadataException(MetadataErrorCode.InvalidTiff, "The primary directory does not lie inside the file.");

        TiffBasedDirectory? old = map.Find(TiffBasedRole.Thumbnail);
        // A successor that is an image of the file is not a thumbnail: it is not replaced, and it is
        // not unlinked. That is a picture of the file, and its bytes are the picture's.
        if (old is not null && (old.Next != 0 || !IsJpegThumbnail(old)))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The directory after the primary one is an image of the file, not a thumbnail of an EXIF block: it is neither replaced nor removed.");
        if (old is not null && old.References > 1)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "The thumbnail directory is reached from more than one pointer: it is not replaced or removed.");

        // The successor the map did not reach as a directory of its own is not one this writer knows
        // how to replace: it is left alone rather than unlinked.
        if (old is null && primary.Next != 0)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "The primary directory names a successor that is no directory this reader reached: it is not replaced.");
        if (jpeg is null && primary.Next == 0) return file;
        if (jpeg is not null)
        {
            if (jpeg.Length > limits.MaxThumbnailBytes)
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "The thumbnail exceeds its budget.");
            // Appended bytes have to land outside every range the file accounts for.
            if (map.HasRangePastEnd || map.HasOpenEndedImageData)
                throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                    "The file addresses bytes it does not hold, or image data whose end it does not state: nothing is appended to it.");
            if ((long)file.Length + jpeg.Length + 4 * 1024 > limits.MaxInputBytes)
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "The file with the thumbnail exceeds the input budget.");
        }

        bool little = map.Order == Endianness.LittleEndian;
        // The tags of the directory the thumbnail replaces, as their entries lie: their values do not
        // move, so the entries are copied as they are.
        var kept = new List<(ushort Tag, byte[] Entry)>();
        if (jpeg is not null && old is not null)
        {
            foreach (TiffBasedField field in old.Fields)
            {
                if (field.Tag is Compression or JpegInterchangeFormat or JpegInterchangeFormatLength or StripOffsets or StripByteCounts or TileOffsets or TileByteCounts) continue;
                if (field.EntryOffset < 0 || field.EntryOffset > file.Length - 12) continue;
                if (kept.Exists(held => held.Tag == field.Tag)) continue;
                kept.Add((field.Tag, file.AsSpan(field.EntryOffset, 12).ToArray()));
            }
        }
        bool created = old is null;
        int entries = kept.Count + 3 + (created ? 3 : 0);
        int start = (file.Length + 1) & ~1;
        int directoryBytes = 2 + (entries * 12) + 4, valueBytes = created ? 16 : 0;
        long length = jpeg is null ? file.Length : (long)start + directoryBytes + valueBytes + jpeg.Length;

        byte[] output = new byte[length];
        file.CopyTo(output, 0);
        var cleared = new List<(long Start, long Length)>();
        if (old is not null)
        {
            TiffBasedField? at = old.Find(JpegInterchangeFormat), count = old.Find(JpegInterchangeFormatLength);
            if (Scalar(file, at, little) is uint offset && Scalar(file, count, little) is uint bytes &&
                offset <= file.Length && (long)offset + bytes <= file.Length && map.IsExclusive(offset, bytes))
            {
                output.AsSpan((int)offset, (int)bytes).Clear();
                cleared.Add((offset, bytes));
            }
            if (map.IsExclusive(old.Offset, old.Length))
            {
                output.AsSpan((int)old.Offset, old.Length).Clear();
                cleared.Add((old.Offset, old.Length));
            }
        }

        void Put16(int at, ushort value)
        {
            if (little) BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at), value);
            else BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at), value);
        }
        void Put32(int at, uint value)
        {
            if (little) BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at), value);
            else BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at), value);
        }

        if (jpeg is null) Put32((int)link, 0);
        else
        {
            int values = start + directoryBytes, picture = values + valueBytes;
            var table = new List<(ushort Tag, byte[] Entry)>(kept);
            byte[] Entry(ushort tag, ushort type, uint value, bool isShort)
            {
                byte[] made = new byte[12];
                if (little) { BinaryPrimitives.WriteUInt16LittleEndian(made.AsSpan(0), tag); BinaryPrimitives.WriteUInt16LittleEndian(made.AsSpan(2), type); BinaryPrimitives.WriteUInt32LittleEndian(made.AsSpan(4), 1); }
                else { BinaryPrimitives.WriteUInt16BigEndian(made.AsSpan(0), tag); BinaryPrimitives.WriteUInt16BigEndian(made.AsSpan(2), type); BinaryPrimitives.WriteUInt32BigEndian(made.AsSpan(4), 1); }
                if (isShort) { if (little) BinaryPrimitives.WriteUInt16LittleEndian(made.AsSpan(8), (ushort)value); else BinaryPrimitives.WriteUInt16BigEndian(made.AsSpan(8), (ushort)value); }
                else { if (little) BinaryPrimitives.WriteUInt32LittleEndian(made.AsSpan(8), value); else BinaryPrimitives.WriteUInt32BigEndian(made.AsSpan(8), value); }
                return made;
            }
            table.Add((Compression, Entry(Compression, 3, 6, true)));
            if (created)
            {
                table.Add((XResolution, Entry(XResolution, 5, (uint)values, false)));
                table.Add((YResolution, Entry(YResolution, 5, (uint)values + 8, false)));
                table.Add((ResolutionUnit, Entry(ResolutionUnit, 3, 2, true)));
                Put32(values, 72); Put32(values + 4, 1);
                Put32(values + 8, 72); Put32(values + 12, 1);
            }
            table.Add((JpegInterchangeFormat, Entry(JpegInterchangeFormat, 4, (uint)picture, false)));
            table.Add((JpegInterchangeFormatLength, Entry(JpegInterchangeFormatLength, 4, (uint)jpeg.Length, false)));
            table.Sort((left, right) => left.Tag.CompareTo(right.Tag));
            Put16(start, (ushort)table.Count);
            int at = start + 2;
            foreach ((_, byte[] made) in table) { made.CopyTo(output, at); at += 12; }
            Put32(at, 0);
            jpeg.CopyTo(output, picture);
            Put32((int)link, (uint)start);
        }

        Verify(file, output, (int)link, cleared, jpeg, limits, maxRanges);
        return output;
    }

    // The file has to read back as planned: the directory after the primary one is the stream that
    // was asked for, or none; every tag outside it reads as before; and the bytes of the source are
    // unchanged but at the successor link and the ranges that were cleared.
    private static void Verify(byte[] source, byte[] output, int link, List<(long Start, long Length)> cleared,
        byte[]? jpeg, MetadataLimits limits, int maxRanges)
    {
        ExifMetadata before, after;
        TiffBasedFileMap afterMap;
        try
        {
            before = ExifTiffReader.InspectFile(source, limits);
            after = ExifTiffReader.InspectFile(output, limits);
            afterMap = TiffBasedFileMap.Read(output, limits, maxRanges);
        }
        catch (MetadataException failure)
        {
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "The thumbnail change was not written: the reader refuses the output (" + failure.Message + ")", failure.SourceOffset);
        }

        TiffBasedDirectory? thumbnail = afterMap.Find(TiffBasedRole.Thumbnail);
        if (jpeg is null)
        {
            if (thumbnail is not null)
                throw Unsafe("the file still has a directory after the primary one");
        }
        else
        {
            byte[]? read = thumbnail is null ? null : Stream(output, thumbnail, afterMap.Order);
            if (read is null || !read.AsSpan().SequenceEqual(jpeg))
                throw Unsafe("the directory after the primary one does not address the stream that was asked for");
        }

        foreach (ExifEntry entry in before.Entries)
        {
            if (entry.Key.Directory == ExifDirectoryKind.Thumbnail) continue;
            if (!after.TryGetEntry(entry.Key, out ExifEntry? same) || same.Value.FieldType != entry.Value.FieldType
                || same.Value.Count != entry.Value.Count || !same.Value.RawBytes.SequenceEqual(entry.Value.RawBytes))
                throw Unsafe("a tag outside the thumbnail directory would read differently");
        }
        foreach (ExifEntry entry in after.Entries)
        {
            if (entry.Key.Directory == ExifDirectoryKind.Thumbnail) continue;
            if (!before.TryGetEntry(entry.Key, out _))
                throw Unsafe("a tag outside the thumbnail directory appeared");
        }

        for (int at = 0; at < source.Length; at++)
        {
            if (source[at] == output[at]) continue;
            if (at >= link && at < link + 4) continue;
            if (cleared.Exists(range => at >= range.Start && at < range.Start + range.Length)) continue;
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "The thumbnail change was not written: a byte of the source that is no part of the change would differ.", at);
        }
    }

    private static MetadataException Unsafe(string what) =>
        new(MetadataErrorCode.UnsafeRelocation, "The thumbnail change was not written: " + what + ".");

    /// <summary>The JPEG stream a thumbnail directory addresses, or null where it does not state one.</summary>
    private static byte[]? Stream(byte[] file, TiffBasedDirectory directory, Endianness order)
    {
        bool little = order == Endianness.LittleEndian;
        if (Scalar(file, directory.Find(JpegInterchangeFormat), little) is not uint offset ||
            Scalar(file, directory.Find(JpegInterchangeFormatLength), little) is not uint bytes) return null;
        if (offset > file.Length || (long)offset + bytes > file.Length) return null;
        return file.AsSpan((int)offset, (int)bytes).ToArray();
    }

    // A directory that only addresses a JPEG stream: no strips, no tiles, and nothing chained after it.
    private static bool IsJpegThumbnail(TiffBasedDirectory directory) =>
        directory.Find(StripOffsets) is null && directory.Find(StripByteCounts) is null &&
        directory.Find(TileOffsets) is null && directory.Find(TileByteCounts) is null &&
        directory.Find(JpegInterchangeFormat) is not null && directory.Find(JpegInterchangeFormatLength) is not null;

    /// <summary>The value of a single SHORT or LONG field; null for a field that is not one or lies outside the file.</summary>
    private static uint? Scalar(byte[] file, TiffBasedField? field, bool little)
    {
        if (field is null || field.Count != 1 || field.Type is not (3 or 4)) return null;
        int width = field.Type == 3 ? 2 : 4;
        if (field.ValueOffset < 0 || field.ValueOffset > file.Length - width) return null;
        ReadOnlySpan<byte> bytes = file.AsSpan((int)field.ValueOffset, width);
        return width == 2
            ? (little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes))
            : (little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes));
    }
}
