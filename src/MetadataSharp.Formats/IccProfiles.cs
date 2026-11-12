using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Png;

namespace MetadataSharp.Formats;

/// <summary>
/// Read-only access to the ICC colour profile a file embeds. The profile is returned as the bytes
/// its header declares and nothing in it is interpreted: the reader checks the size field and the
/// <c>acsp</c> signature of the header, and no more. Nothing is written here: the documents of
/// the formats write a profile (<c>SaveIccProfile</c>).
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
    /// <item>HEIC, HEIF and AVIF: the <c>colr</c> property of the kind <c>prof</c> or <c>rICC</c>
    /// that the item property associations give the primary item.</item>
    /// <item>JPEG 2000: the first <c>colr</c> box of the header box whose method is 2 or 3.</item>
    /// <item>PSD: image resource 0x040F.</item>
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
    /// <see cref="MetadataErrorCode.AmbiguousMetadata"/> when a PNG or a WebP has two profile chunks,
    /// the primary item of a BMFF file two profiles, or a PSD two resources 0x040F.
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the file, the number of its segments,
    /// chunks, boxes, resources, directory entries or property associations, or the profile exceeds
    /// a budget.
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
            FileKind.Bmff => Bmff(file, limits),
            FileKind.Jp2 => Jp2(file, limits),
            FileKind.Psd => Psd(file, limits),
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

    /// <summary>
    /// The check of a profile that is to be written. It is the check of <see cref="Whole"/> and one
    /// more: the size field is the number of bytes given, so that what is written is what the
    /// header declares, without padding.
    /// </summary>
    internal static void Exact(ReadOnlySpan<byte> profile)
    {
        if (profile.Length < HeaderBytes)
            throw Invalid("An ICC profile is at least the 128 bytes of its header.");
        if (BinaryPrimitives.ReadUInt32BigEndian(profile) != (uint)profile.Length)
            throw Invalid("The size field of the ICC profile header is not the number of bytes given.");
        if (!profile.Slice(36, 4).SequenceEqual("acsp"u8))
            throw Invalid("The ICC profile header lacks the 'acsp' signature.");
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

    // ---- BMFF: a colr property of the primary item that holds a profile ----

    private const uint MetaBox = 0x6D657461, PrimaryItemBox = 0x7069746D, PropertiesBox = 0x69707270,
        PropertyContainerBox = 0x6970636F, AssociationBox = 0x69706D61, ColourBox = 0x636F6C72,
        UnrestrictedProfile = 0x70726F66, RestrictedProfile = 0x72494343;

    private static byte[]? Bmff(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        int at = 0, meta = -1, metaEnd = 0;
        for (int count = 0; EmbeddedFile.Box(data, ref at, data.Length, out uint type, out int payload, out int end); count++)
        {
            if (count == limits.MaxSegments)
                throw EmbeddedFile.Limit("The box count exceeds its budget.", payload);
            if (type != MetaBox) continue;
            // A full box: the children follow the version and the flags.
            meta = payload + 4;
            metaEnd = end;
            break;
        }
        if (meta < 0 || meta > metaEnd) return null;

        long primary = -1;
        int container = -1, containerEnd = 0, associations = -1, associationsEnd = 0;
        at = meta;
        for (int count = 0; EmbeddedFile.Box(data, ref at, metaEnd, out uint type, out int payload, out int end); count++)
        {
            if (count == limits.MaxSegments)
                throw EmbeddedFile.Limit("The box count exceeds its budget.", payload);
            if (type == PrimaryItemBox && primary < 0)
            {
                // The item in 16 bits, or in 32 from version 1.
                if (end - payload >= 6 && data[payload] == 0) primary = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(payload + 4, 2));
                else if (end - payload >= 8 && data[payload] != 0) primary = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(payload + 4, 4));
            }
            else if (type == PropertiesBox)
            {
                int child = payload;
                for (int inner = 0; EmbeddedFile.Box(data, ref child, end, out uint kind, out int body, out int bodyEnd); inner++)
                {
                    if (inner == limits.MaxSegments)
                        throw EmbeddedFile.Limit("The box count exceeds its budget.", body);
                    if (kind == PropertyContainerBox && container < 0) { container = body; containerEnd = bodyEnd; }
                    else if (kind == AssociationBox && associations < 0) { associations = body; associationsEnd = bodyEnd; }
                }
            }
        }
        if (primary < 0 || container < 0 || associations < 0) return null;

        HashSet<int> mine = Associated(data.Slice(associations, associationsEnd - associations), (uint)primary, limits, associations);
        byte[]? profile = null;
        at = container;
        // The associations name a property by its place in the container, from one.
        for (int index = 1; EmbeddedFile.Box(data, ref at, containerEnd, out uint type, out int payload, out int end); index++)
        {
            if (index > limits.MaxTotalEntries)
                throw EmbeddedFile.Limit("The item properties exceed their budget.", payload);
            if (type != ColourBox || end - payload < 4 || !mine.Contains(index)) continue;
            uint colour = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(payload, 4));
            if (colour is not (UnrestrictedProfile or RestrictedProfile)) continue;
            if (profile is not null)
                throw Ambiguous("The primary item has more than one ICC profile.", payload);
            profile = Whole(data.Slice(payload + 4, Bounded(end - payload - 4, limits, payload)), payload + 4);
        }
        return profile;
    }

    /// <summary>
    /// The properties the association box gives one item. The box holds a version and flags, a
    /// count of entries, and for each entry the item (in 16 bits, or in 32 from version 1), a count,
    /// and that many associations: a bit that says the property is essential and its number, in one
    /// byte, or in two when bit 0 of the flags is set.
    /// </summary>
    private static HashSet<int> Associated(ReadOnlySpan<byte> box, uint item, MetadataLimits limits, int offset)
    {
        var indices = new HashSet<int>();
        if (box.Length < 8) return indices;
        bool wideItem = box[0] != 0, wideIndex = (box[3] & 1) != 0;
        int itemBytes = wideItem ? 4 : 2, width = wideIndex ? 2 : 1;
        uint entries = BinaryPrimitives.ReadUInt32BigEndian(box.Slice(4, 4));
        int at = 8;
        for (uint entry = 0; entry < entries && box.Length - at > itemBytes; entry++)
        {
            if (entry == (uint)limits.MaxTotalEntries)
                throw EmbeddedFile.Limit("The item property associations exceed their budget.", offset + at);
            uint id = wideItem ? BinaryPrimitives.ReadUInt32BigEndian(box.Slice(at, 4)) : BinaryPrimitives.ReadUInt16BigEndian(box.Slice(at, 2));
            at += itemBytes;
            int count = box[at++];
            if (box.Length - at < count * width) break;
            for (int index = 0; index < count; index++, at += width)
            {
                if (id != item) continue;
                int property = wideIndex ? BinaryPrimitives.ReadUInt16BigEndian(box.Slice(at, 2)) & 0x7FFF : box[at] & 0x7F;
                if (property != 0) indices.Add(property);
            }
        }
        return indices;
    }

    // ---- JPEG 2000: a colr box of the header box; the method, the precedence, the approximation, the profile ----

    private const uint Jp2Header = 0x6A703268;

    private static byte[]? Jp2(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        int at = 0;
        for (int count = 0; EmbeddedFile.Box(data, ref at, data.Length, out uint type, out int payload, out int end); count++)
        {
            if (count == limits.MaxSegments)
                throw EmbeddedFile.Limit("The box count exceeds its budget.", payload);
            if (type != Jp2Header) continue;
            int child = payload;
            for (int inner = 0; EmbeddedFile.Box(data, ref child, end, out uint kind, out int body, out int bodyEnd); inner++)
            {
                if (inner == limits.MaxSegments)
                    throw EmbeddedFile.Limit("The box count exceeds its budget.", body);
                // Method 1 names a colour space by number; 2 (a restricted profile) and 3 (any profile) carry one.
                if (kind != ColourBox || bodyEnd - body < 3 || data[body] is not (2 or 3)) continue;
                return Whole(data.Slice(body + 3, Bounded(bodyEnd - body - 3, limits, body)), body + 3);
            }
            return null;
        }
        return null;
    }

    // ---- PSD: image resource 0x040F ----

    private static byte[]? Psd(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        // A header of 26 bytes, then the colour mode data and the image resources, each behind its length in 32 bits.
        if (data.Length < 34) return null;
        long colour = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(26, 4));
        if (colour > data.Length - 34) return null;
        int at = 30 + (int)colour;
        long section = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
        at += 4;
        if (section > data.Length - at) return null;
        int limit = at + (int)section;
        byte[]? profile = null;
        // A resource: a signature, its number, a name behind a length byte and padded to an even length,
        // the length of the data in 32 bits, and the data, padded the same way.
        for (int count = 0; limit - at >= 12 && IsResource(data.Slice(at, 4)); count++)
        {
            if (count == limits.MaxSegments)
                throw EmbeddedFile.Limit("The resource count exceeds its budget.", at);
            int id = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 4, 2));
            int name = (data[at + 6] + 2) & ~1;
            if (limit - at - 6 - name < 4) break;
            int payload = at + 6 + name + 4;
            long length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(payload - 4, 4));
            if (length > limit - payload) break;
            if (id == 0x040F)
            {
                if (profile is not null)
                    throw Ambiguous("The PSD has more than one ICC profile resource.", at);
                profile = Whole(data.Slice(payload, Bounded(length, limits, payload - 4)), payload);
            }
            at = payload + (int)length + (int)(length & 1);
        }
        return profile;
    }

    /// <summary>The resource signatures Adobe applications write; all frame a resource as 8BIM does.</summary>
    private static bool IsResource(ReadOnlySpan<byte> signature) =>
        signature.SequenceEqual("8BIM"u8) || signature.SequenceEqual("AgHg"u8) || signature.SequenceEqual("DCSR"u8) || signature.SequenceEqual("PHUT"u8);
}
