using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Internal;

/// <summary>What the first bytes of a complete file say it is.</summary>
internal enum FileKind
{
    Unknown,
    Jpeg,
    Exv,
    Tiff,
    Psd,
    Png,
    WebP,
    Bmff,
    Jp2
}

/// <summary>
/// The framing the whole-file readers share: the signature that names the container, the input
/// budget, and one bounded step over each kind of segment, chunk or box. A step never reads past
/// the file; a structure that does not hold ends the walk rather than being reported.
/// </summary>
internal static class EmbeddedFile
{
    /// <summary>
    /// The limits of a read, validated, and the file checked against the input budget. A read that
    /// names none gets the budgets of a whole file (<see cref="TiffReadOptions.Limits"/>): an ICC
    /// profile is routinely larger than the 65,527 bytes the defaults of
    /// <see cref="MetadataLimits"/> allow one value.
    /// </summary>
    internal static MetadataLimits Checked(ReadOnlySpan<byte> file, MetadataLimits? limits)
    {
        limits ??= TiffReadOptions.FileLimits;
        limits.Validate();
        if (file.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The file exceeds the input budget.", 0);
        return limits;
    }

    /// <summary>The most one embedded block may hold: a value, which is also retained.</summary>
    internal static int Budget(MetadataLimits limits) => Math.Min(limits.MaxValueBytes, limits.MaxRetainedMetadataBytes);

    internal static MetadataException Limit(string message, long? offset = null) =>
        new(MetadataErrorCode.LimitExceeded, message, offset);

    internal static FileKind Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8) return FileKind.Jpeg;
        if (data.Length >= 7 && data[0] == 0xFF && data[1] == 0x01 && data.Slice(2, 5).SequenceEqual("Exiv2"u8)) return FileKind.Exv;
        if (data.Length >= 8)
        {
            // TIFF itself, and the raw formats that keep the structure under their own identifier:
            // ORF ("IIRO", "IIRS", "MMOR") and RW2 ("IIU\0").
            ReadOnlySpan<byte> head = data[..4];
            if (head.SequenceEqual("II*\0"u8) || head.SequenceEqual("MM\0*"u8) || head.SequenceEqual("IIRO"u8) ||
                head.SequenceEqual("IIRS"u8) || head.SequenceEqual("MMOR"u8) || head.SequenceEqual("IIU\0"u8))
                return FileKind.Tiff;
        }
        if (data.Length >= 4 && data[..4].SequenceEqual("8BPS"u8)) return FileKind.Psd;
        if (data.Length >= 8 && data[0] == 0x89 && data.Slice(1, 3).SequenceEqual("PNG"u8)) return FileKind.Png;
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return FileKind.WebP;
        if (data.Length >= 12 && data.Slice(4, 4).SequenceEqual("jP  "u8)) return FileKind.Jp2;
        if (data.Length >= 12 && data.Slice(4, 4).SequenceEqual("ftyp"u8)) return FileKind.Bmff;
        return FileKind.Unknown;
    }

    /// <summary>
    /// Step to the next JPEG segment that has a length, from <paramref name="at"/>, which is left
    /// behind it. False at the first scan, at the end of the image and where the bytes are not a
    /// segment: what a file keeps for a reader of metadata comes before the first scan.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="at">Where the step starts; after a true result, the end of the segment.</param>
    /// <param name="marker">The second byte of the marker.</param>
    /// <param name="payload">The first byte after the length field.</param>
    /// <param name="end">One past the last byte of the segment.</param>
    internal static bool Segment(ReadOnlySpan<byte> data, ref int at, out byte marker, out int payload, out int end)
    {
        marker = 0;
        payload = 0;
        end = 0;
        while (data.Length - at >= 4)
        {
            if (data[at] != 0xFF) return false;
            marker = data[at + 1];
            // A run of 0xFF is fill; TEM and the restart markers stand alone.
            if (marker == 0xFF) { at++; continue; }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) { at += 2; continue; }
            if (marker is 0x00 or 0xD8 or 0xD9 or 0xDA) return false;
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 2, 2));
            if (length < 2 || length > data.Length - at - 2) return false;
            payload = at + 4;
            end = at + 2 + length;
            at = end;
            return true;
        }
        return false;
    }

    /// <summary>The PNG chunks that end a walk for what must come before them: the image data and the end.</summary>
    internal const uint PngImageData = 0x49444154, PngEnd = 0x49454E44;

    /// <summary>
    /// Step to the next PNG chunk from <paramref name="at"/>, which is left behind its checksum.
    /// False where fewer than the twelve bytes of an empty chunk remain or the length leaves the file.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="at">Where the step starts; after a true result, the end of the chunk.</param>
    /// <param name="type">The four letters of the chunk, as a big-endian number.</param>
    /// <param name="payload">The first byte of the data.</param>
    /// <param name="length">The number of bytes of data.</param>
    internal static bool Chunk(ReadOnlySpan<byte> data, ref int at, out uint type, out int payload, out int length)
    {
        type = 0;
        payload = 0;
        length = 0;
        if (at < 8 || data.Length - at < 12) return false;
        uint size = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
        if (size > (uint)(data.Length - at - 12)) return false;
        type = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at + 4, 4));
        payload = at + 8;
        length = (int)size;
        at = payload + length + 4;
        return true;
    }

    /// <summary>Whether the checksum after a PNG chunk is the one of its type and data.</summary>
    internal static bool ChecksumHolds(ReadOnlySpan<byte> data, int payload, int length) =>
        ~Crc.Update(uint.MaxValue, data.Slice(payload - 4, length + 4)) ==
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(payload + length, 4));

    /// <summary>
    /// Step to the next chunk of a RIFF file from <paramref name="at"/>, which is left behind the
    /// chunk and its padding byte. False where no chunk header remains or the length leaves the file.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="at">Where the step starts; after a true result, the end of the chunk.</param>
    /// <param name="type">The four characters of the chunk, as a big-endian number.</param>
    /// <param name="payload">The first byte of the data.</param>
    /// <param name="length">The number of bytes of data.</param>
    internal static bool RiffChunk(ReadOnlySpan<byte> data, ref int at, out uint type, out int payload, out int length)
    {
        type = 0;
        payload = 0;
        length = 0;
        if (at < 12 || data.Length - at < 8) return false;
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at + 4, 4));
        if (size > (uint)(data.Length - at - 8)) return false;
        type = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
        payload = at + 8;
        length = (int)size;
        long next = (long)payload + length + (length & 1);
        at = next > data.Length ? data.Length : (int)next;
        return true;
    }
}
