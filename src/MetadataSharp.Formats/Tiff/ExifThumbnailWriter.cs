using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// Sets or removes the thumbnail of an EXIF block: the directory after the primary one, with the
/// JPEG stream it addresses.
/// </summary>
/// <remarks>
/// <para>No byte of the block moves. A new thumbnail is appended with a directory of its own
/// (Compression, XResolution, YResolution, ResolutionUnit, JPEGInterchangeFormat and its length),
/// and the successor link of the primary directory is pointed at it; removing sets that link to
/// zero. The old directory and the old stream are cleared to zeros where the map of the block shows
/// that nothing else addresses them, so that a picture that was removed is not left in the file.</para>
/// <para>The output is read back: the reader has to return the new stream as the thumbnail (or none),
/// and every tag outside the thumbnail directory as it was.</para>
/// </remarks>
internal static class ExifThumbnailWriter
{
    /// <summary>The block with the thumbnail set to <paramref name="jpeg"/>, or removed for null; null when there is nothing to remove.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.LimitExceeded"/> for a stream over the thumbnail budget,
    /// <see cref="MetadataErrorCode.ExifTooLarge"/> when the block would pass the ceiling,
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a stream the reader does not take as a JPEG,
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the output does not read back as planned.
    /// </exception>
    internal static byte[]? Apply(byte[] block, byte[]? jpeg, MetadataLimits limits, int ceiling)
    {
        TiffReadResult before = ExifTiffReader.Read(block, limits);
        TiffBasedFileMap map = TiffBasedFileMap.Read(block, limits, ExifBlockEditor.MaxAccountedRanges);
        TiffBasedDirectory primary = map.Find(TiffBasedRole.Primary)
            ?? throw new MetadataException(MetadataErrorCode.InvalidExif, "The EXIF block has no primary directory.");
        TiffBasedDirectory? old = map.Find(TiffBasedRole.Thumbnail);
        if (jpeg is null && primary.Next == 0) return null;
        if (primary.References > 1)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "The primary directory is reached from more than one pointer: its successor link is not changed.");
        bool little = map.Order == Endianness.LittleEndian;
        long link = (long)primary.Offset + primary.Length - 4;
        if (link < 8 || link > block.Length - 4)
            throw new MetadataException(MetadataErrorCode.InvalidExif, "The primary directory does not lie inside the EXIF block.");

        int start = (block.Length + 1) & ~1;
        const int Directory = 2 + (6 * 12) + 4, Values = 16;
        long length = jpeg is null ? block.Length : (long)start + Directory + Values + jpeg.Length;
        if (jpeg is not null && jpeg.Length > limits.MaxThumbnailBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The thumbnail exceeds its budget.");
        if (length > ceiling)
            throw new MetadataException(MetadataErrorCode.ExifTooLarge, ceiling == ExifTiffReader.SegmentCeiling
                ? "The EXIF block with the thumbnail does not fit a single JPEG APP1 segment."
                : "The EXIF block with the thumbnail is longer than its container allows.");
        byte[] output = new byte[length];
        block.CopyTo(output, 0);

        // What only the old thumbnail directory addressed is cleared: the stream, then the directory.
        if (old is not null && old.References <= 1)
        {
            if (Scalar(block, old.Find(0x0201), little) is uint at && Scalar(block, old.Find(0x0202), little) is uint count && map.IsExclusive(at, count))
                output.AsSpan((int)at, (int)count).Clear();
            if (map.IsExclusive(old.Offset, old.Length))
                output.AsSpan((int)old.Offset, old.Length).Clear();
        }

        void Put16(int offset, ushort value)
        {
            if (little) BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset), value);
            else BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(offset), value);
        }
        void Put32(int offset, uint value)
        {
            if (little) BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset), value);
            else BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(offset), value);
        }

        if (jpeg is null)
        {
            Put32((int)link, 0);
        }
        else
        {
            int values = start + Directory, picture = values + Values, entry = start + 2;
            void Entry(ushort tag, ushort type, uint value, bool isShort)
            {
                Put16(entry, tag);
                Put16(entry + 2, type);
                Put32(entry + 4, 1);
                if (isShort) Put16(entry + 8, (ushort)value); else Put32(entry + 8, value);
                entry += 12;
            }
            Put16(start, 6);
            Entry(0x0103, 3, 6, true);                    // Compression: JPEG
            Entry(0x011A, 5, (uint)values, false);        // XResolution 72/1
            Entry(0x011B, 5, (uint)values + 8, false);    // YResolution 72/1
            Entry(0x0128, 3, 2, true);                    // ResolutionUnit: inch
            Entry(0x0201, 4, (uint)picture, false);       // JPEGInterchangeFormat
            Entry(0x0202, 4, (uint)jpeg.Length, false);   // JPEGInterchangeFormatLength
            Put32(entry, 0);
            Put32(values, 72); Put32(values + 4, 1);
            Put32(values + 8, 72); Put32(values + 12, 1);
            jpeg.CopyTo(output, picture);
            Put32((int)link, (uint)start);
        }

        TiffReadResult after;
        try { after = ExifTiffReader.Read(output, limits); }
        catch (MetadataException failure)
        {
            throw new MetadataException(failure.Code is MetadataErrorCode.LimitExceeded or MetadataErrorCode.ExifTooLarge ? failure.Code : MetadataErrorCode.UnsafeRelocation,
                "The block with the thumbnail change was not written: the reader refuses it (" + failure.Message + ")");
        }
        if (jpeg is null ? after.Thumbnail is not null || after.Directories.Contains(ExifDirectoryKind.Thumbnail)
            : after.Thumbnail is null || !after.Thumbnail.AsSpan().SequenceEqual(jpeg))
        {
            throw new MetadataException(jpeg is null ? MetadataErrorCode.UnsafeRelocation : MetadataErrorCode.UnsupportedFeature, jpeg is null
                ? "The thumbnail was not removed: the block still reads with a thumbnail directory."
                : "The thumbnail is not a JPEG stream the reader takes: it starts with FF D8, ends with FF D9 and is within the thumbnail budget.");
        }
        if (!SameOutside(before.Metadata, after.Metadata) || !SameOutside(after.Metadata, before.Metadata))
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "The thumbnail change was not written: a tag outside the thumbnail directory would read differently.");
        return output;
    }

    /// <summary>The value of a single SHORT or LONG field; null for a field that is not one or lies outside the block.</summary>
    private static uint? Scalar(byte[] block, TiffBasedField? field, bool little)
    {
        if (field is null || field.Count != 1 || field.Type is not (3 or 4)) return null;
        int width = field.Type == 3 ? 2 : 4;
        if (field.ValueOffset < 0 || field.ValueOffset > block.Length - width) return null;
        ReadOnlySpan<byte> bytes = block.AsSpan((int)field.ValueOffset, width);
        return width == 2
            ? (little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes))
            : (little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes));
    }

    private static bool SameOutside(ExifMetadata one, ExifMetadata other)
    {
        foreach (ExifEntry entry in one.Entries)
        {
            if (entry.Key.Directory == ExifDirectoryKind.Thumbnail) continue;
            if (!other.TryGetEntry(entry.Key, out ExifEntry? same) || same.Value.FieldType != entry.Value.FieldType
                || same.Value.Count != entry.Value.Count || !same.Value.RawBytes.SequenceEqual(entry.Value.RawBytes))
                return false;
        }
        return true;
    }
}
