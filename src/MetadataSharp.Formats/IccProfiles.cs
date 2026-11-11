using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;

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
    /// a kind that is not read. A JPEG (and an EXV file, which is framed the same way) keeps the
    /// profile in APP2 segments marked <c>ICC_PROFILE</c>, each with its number and the count of
    /// all; they are joined by number, in whatever order the file has them. The bytes the carrier
    /// holds after the length the profile header declares are padding and are not returned.
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
    /// segments disagree about the count, the header is shorter than 128 bytes, its size field does
    /// not fit the bytes the file holds, or the <c>acsp</c> signature is absent.
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the file, the number of its segments or
    /// the profile exceeds a budget.
    /// </exception>
    public static byte[]? Read(ReadOnlySpan<byte> file, MetadataLimits? limits = null)
    {
        limits = EmbeddedFile.Checked(file, limits);
        return EmbeddedFile.Detect(file) switch
        {
            FileKind.Jpeg => Segments(file, 2, limits),
            FileKind.Exv => Segments(file, 7, limits),
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
}
