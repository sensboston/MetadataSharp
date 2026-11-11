using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Png;

namespace MetadataSharp.Formats;

/// <summary>
/// Read-only access to the ICC colour profile a file embeds. The profile is returned as the bytes
/// its header declares and nothing in it is interpreted: the reader checks the size field and the
/// <c>acsp</c> signature of the header, and no more. Nothing is written.
/// </summary>
public static class IccProfiles
{
    /// <summary>The header of a profile, which is the least a profile can be.</summary>
    internal const int HeaderBytes = 128;

    /// <summary>
    /// Return the ICC profile embedded in a complete file, or null when the file has none or is of
    /// a kind that is not read. The bytes the carrier holds after the length the profile header
    /// declares are padding and are not returned.
    /// <list type="bullet">
    /// <item>JPEG, and EXV, which is framed the same way: the APP2 segments marked
    /// <c>ICC_PROFILE</c> before the first scan, each with its number and the count of all, joined
    /// by number in whatever order the file has them.</item>
    /// <item>PNG: the <c>iCCP</c> chunk before the image data, inflated within the budget.</item>
    /// <item>TIFF and the raw formats built on it, DNG among them: tag 0x8773 of the first directory.</item>
    /// <item>WebP: the <c>ICCP</c> chunk.</item>
    /// </list>
    /// </summary>
    /// <param name="file">The whole file.</param>
    /// <param name="limits">
    /// The budgets. Unless given, those of a whole file: <see cref="MetadataLimits.MaxValueBytes"/>
    /// 16,777,216 and <see cref="MetadataLimits.MaxRetainedMetadataBytes"/> 67,108,864, the smaller
    /// of which bounds the profile.
    /// </param>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidIccProfile"/> when the file carries a profile that cannot
    /// be given whole: a JPEG segment is missing, repeated or numbered outside the count, the
    /// segments disagree about the count; the <c>iCCP</c> chunk fails its checksum, has no name or
    /// does not inflate; tag 0x8773 is not of the type BYTE or UNDEFINED or points outside the file;
    /// the header is shorter than 128 bytes, its size field does not fit the bytes the file holds,
    /// or the <c>acsp</c> signature is absent.
    /// <see cref="MetadataErrorCode.AmbiguousMetadata"/> when a PNG or a WebP has two profile chunks.
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the file, the number of its segments,
    /// chunks or directory entries, or the profile exceeds a budget.
    /// </exception>
    public static byte[]? Read(ReadOnlySpan<byte> file, MetadataLimits? limits = null)
    {
        limits = EmbeddedFile.Checked(file, limits);
        return EmbeddedFile.Detect(file) switch
        {
            FileKind.Jpeg => Segments(file, 2, limits),
            FileKind.Exv => Segments(file, 7, limits),
            FileKind.Png => Png(file, limits),
            FileKind.Tiff => Tiff(file, limits),
            FileKind.WebP => WebP(file, limits),
            _ => null,
        };
    }

    internal static MetadataException Invalid(string message, long? offset = null) =>
        new(MetadataErrorCode.InvalidIccProfile, message, offset);

    /// <summary>
    /// The profile at the start of <paramref name="carrier"/>, checked against its own header: at
    /// least 128 bytes, a size field that is at least that and no more than the carrier holds, and
    /// the <c>acsp</c> signature at byte 36. The result is a copy of exactly the declared bytes.
    /// </summary>
    /// <param name="carrier">The bytes the container gives for the profile.</param>
    /// <param name="offset">Where the carrier is in the file, for the error; null when it was assembled.</param>
    internal static byte[] Whole(ReadOnlySpan<byte> carrier, long? offset)
    {
        if (carrier.Length < HeaderBytes)
            throw Invalid("An ICC profile is at least the 128 bytes of its header.", offset);
        uint size = BinaryPrimitives.ReadUInt32BigEndian(carrier);
        if (size < HeaderBytes || size > (uint)carrier.Length)
            throw Invalid("The size field of the ICC profile header does not fit the bytes the file holds.", offset);
        if (!carrier.Slice(36, 4).SequenceEqual("acsp"u8))
            throw Invalid("The ICC profile header lacks the 'acsp' signature.", offset + 36);
        return carrier[..(int)size].ToArray();
    }

    private static MetadataException Ambiguous(string message, long offset) =>
        new(MetadataErrorCode.AmbiguousMetadata, message, offset);

    /// <summary>The length a block may have under the budgets, or a refusal.</summary>
    private static int Bounded(long length, MetadataLimits limits, long? offset)
    {
        if (length > EmbeddedFile.Budget(limits))
            throw EmbeddedFile.Limit("The ICC profile exceeds its budget.", offset);
        return (int)length;
    }

    // ---- JPEG and EXV: APP2 "ICC_PROFILE\0", the number of the segment from one, the count of all ----

    private static byte[]? Segments(ReadOnlySpan<byte> data, int at, MetadataLimits limits)
    {
        (int At, int Length)[]? parts = null;
        int found = 0;
        long total = 0;
        for (int count = 0; EmbeddedFile.Segment(data, ref at, out byte marker, out int payload, out int end); count++)
        {
            if (count == limits.MaxSegments)
                throw EmbeddedFile.Limit("The segment count exceeds its budget.", payload - 4);
            if (marker != 0xE2 || end - payload < 14 || !data.Slice(payload, 12).SequenceEqual("ICC_PROFILE\0"u8)) continue;
            int number = data[payload + 12], declared = data[payload + 13];
            if (declared == 0 || number == 0 || number > declared)
                throw Invalid("An ICC profile segment is numbered outside the count it declares.", payload + 12);
            parts ??= new (int At, int Length)[declared];
            if (parts.Length != declared)
                throw Invalid("The ICC profile segments disagree about their count.", payload + 13);
            if (parts[number - 1].At != 0)
                throw Invalid("An ICC profile segment is repeated.", payload + 12);
            parts[number - 1] = (payload + 14, end - payload - 14);
            total += end - payload - 14;
            found++;
        }
        if (parts is null) return null;
        if (found != parts.Length)
            throw Invalid("An ICC profile segment is missing.");
        byte[] joined = new byte[Bounded(total, limits, null)];
        int to = 0;
        foreach ((int start, int length) in parts)
        {
            data.Slice(start, length).CopyTo(joined.AsSpan(to));
            to += length;
        }
        return Whole(joined, null);
    }

    // ---- PNG: iCCP holds a name of 1 to 79 bytes, a zero, the compression method and a zlib stream ----

    private const uint ProfileChunk = 0x69434350;

    private static byte[]? Png(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        int at = 8;
        byte[]? profile = null;
        for (int count = 0; EmbeddedFile.Chunk(data, ref at, out uint type, out int payload, out int length); count++)
        {
            if (count == limits.MaxSegments)
                throw EmbeddedFile.Limit("The chunk count exceeds its budget.", payload - 8);
            // The profile comes before the image data; a chunk of the name after it is not one.
            if (type is EmbeddedFile.PngImageData or EmbeddedFile.PngEnd) break;
            if (type != ProfileChunk) continue;
            if (profile is not null)
                throw Ambiguous("The PNG has more than one iCCP chunk.", payload - 8);
            if (!EmbeddedFile.ChecksumHolds(data, payload, length))
                throw Invalid("The iCCP chunk fails its checksum.", payload - 8);
            ReadOnlySpan<byte> chunk = data.Slice(payload, length);
            int name = chunk.IndexOf((byte)0);
            if (name is < 1 or > 79 || chunk.Length - name < 2)
                throw Invalid("The iCCP chunk has no profile name or no compression method.", payload);
            if (chunk[name + 1] != 0)
                throw Invalid("The iCCP chunk names a compression method other than deflate.", payload + name + 1);
            byte[] inflated;
            try
            {
                inflated = BoundedZlib.Expand(chunk[(name + 2)..].ToArray(), EmbeddedFile.Budget(limits), default);
            }
            catch (MetadataException failure) when (failure.Code == MetadataErrorCode.InvalidPng)
            {
                throw Invalid("The compressed profile of the iCCP chunk is not a whole zlib stream.", payload + name + 2);
            }
            profile = Whole(inflated, null);
        }
        return profile;
    }

    // ---- TIFF: tag 0x8773 (InterColorProfile) of the first directory ----

    private static byte[]? Tiff(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        bool little = data[0] == 0x49;
        long directory = U32(data, 4, little);
        if (directory < 8 || directory > data.Length - 2) return null;
        int entries = U16(data, (int)directory, little);
        if (entries > limits.MaxEntriesPerDirectory)
            throw EmbeddedFile.Limit("The directory has more entries than its budget.", directory);
        if (12L * entries > data.Length - directory - 2) return null;
        for (int index = 0; index < entries; index++)
        {
            int entry = (int)directory + 2 + 12 * index;
            if (U16(data, entry, little) != 0x8773) continue;
            int type = U16(data, entry + 2, little);
            if (type is not (1 or 7))
                throw Invalid("Tag 0x8773 is not of the type BYTE or UNDEFINED.", entry + 2);
            long count = U32(data, entry + 4, little);
            // Four bytes or fewer are in the entry itself, and are no profile; the header check says so.
            long start = count <= 4 ? entry + 8 : (long)U32(data, entry + 8, little);
            int length = Bounded(count, limits, entry + 4);
            if (start > data.Length - count)
                throw Invalid("The profile of tag 0x8773 lies outside the file.", entry + 8);
            return Whole(data.Slice((int)start, length), start);
        }
        return null;
    }

    private static int U16(ReadOnlySpan<byte> data, int at, bool little) => little ?
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(at, 2)) : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));

    private static uint U32(ReadOnlySpan<byte> data, int at, bool little) => little ?
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at, 4)) : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));

    // ---- WebP: the ICCP chunk of the RIFF file ----

    private const uint RiffProfileChunk = 0x49434350;

    private static byte[]? WebP(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        int at = 12;
        byte[]? profile = null;
        for (int count = 0; EmbeddedFile.RiffChunk(data, ref at, out uint type, out int payload, out int length); count++)
        {
            if (count == limits.MaxSegments)
                throw EmbeddedFile.Limit("The chunk count exceeds its budget.", payload - 8);
            if (type != RiffProfileChunk) continue;
            if (profile is not null)
                throw Ambiguous("The WebP has more than one ICCP chunk.", payload - 8);
            profile = Whole(data.Slice(payload, Bounded(length, limits, payload - 8)), payload);
        }
        return profile;
    }
}
