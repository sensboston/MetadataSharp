using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Operations;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Psd;

public sealed partial class PsdMetadata
{
    private static readonly ushort[] WritableResourceIds = [0x0404, 0x040F, 0x0422, 0x0424, 0x040C, 0x0425];

    /// <summary>
    /// Rewrites the metadata resources of a complete PSD v1 file. The supplied metadata is the
    /// complete set to write: null (or an empty thumbnail) removes that resource. The Photoshop
    /// header, color data, layers, merged pixels, and every unrelated resource record are preserved.
    /// </summary>
    /// <remarks>
    /// EXIF is encoded anew from editable tags; MakerNote, image-address and thumbnail-directory
    /// tags are not carried. IPTC and XMP use their packet bytes. The ICC profile must be exact.
    /// The JPEG thumbnail is stored as resource 0x040C with its Photoshop thumbnail header. The
    /// rewritten file is parsed again before it is returned; malformed or unsupported layouts fail
    /// without returning output bytes.
    /// </remarks>
    /// <param name="file">A complete PSD v1 file.</param>
    /// <param name="exif">Editable EXIF metadata to write, or null to remove resource 0x0422.</param>
    /// <param name="iptc">IPTC packet to write, or null to remove resource 0x0404.</param>
    /// <param name="xmp">XMP packet to write, or null to remove resource 0x0424.</param>
    /// <param name="iccProfile">A complete ICC profile to write, or null to remove resource 0x040F.</param>
    /// <param name="jpegThumbnail">JPEG thumbnail bytes to write as resource 0x040C; empty removes thumbnail resources.</param>
    /// <returns>A new PSD file containing the supplied metadata set.</returns>
    /// <exception cref="MetadataException">The PSD is malformed or outside the writer profile, a value exceeds a budget, or the ICC profile is invalid.</exception>
    public static byte[] Write(ReadOnlySpan<byte> file, ExifMetadata? exif = null, IptcPacket? iptc = null,
        XmpPacket? xmp = null, byte[]? iccProfile = null, ReadOnlySpan<byte> jpegThumbnail = default)
    {
        _ = Read(file); // Validate the whole accepted PSD profile before constructing output.
        var replacements = new Dictionary<ushort, byte[]>();
        if (exif is not null)
        {
            var edit = new ExifEdit();
            ExvFile.CopyTags(exif, edit, null);
            if (!edit.IsEmpty && ExifBlockEditor.Create(edit.Snapshot(), new MetadataLimits(), ExifTiffReader.SegmentCeiling, out _) is byte[] block)
                replacements[0x0422] = block;
        }
        if (iptc is { ByteLength: > 0 })
        {
            byte[] stream = new byte[iptc.ByteLength];
            iptc.CopyRawTo(stream);
            replacements[0x0404] = stream;
        }
        if (xmp is { ByteLength: > 0 })
        {
            byte[] packet = new byte[xmp.ByteLength];
            xmp.CopyRawTo(packet);
            replacements[0x0424] = packet;
        }
        if (iccProfile is not null)
        {
            byte[] profile = (byte[])iccProfile.Clone();
            IccProfiles.Exact(profile);
            replacements[0x040F] = profile;
        }
        if (!jpegThumbnail.IsEmpty)
            replacements[0x040C] = Thumbnail(jpegThumbnail);

        int at = 26;
        SkipSection(file, ref at); // Color mode data.
        int resourceLengthAt = at;
        int resourceLength = checked((int)U32(file, at));
        at += 4;
        int resourcesEnd = checked(at + resourceLength);
        using var resources = new MemoryStream(resourceLength + 256);
        var written = new HashSet<ushort>();
        while (at < resourcesEnd)
        {
            int start = at;
            ushort id = U16(file, at + 4);
            at += 6;
            int nameLength = file[at];
            int nameFieldLength = (1 + nameLength + 1) & ~1;
            at += nameFieldLength;
            int sizeAt = at;
            int size = checked((int)U32(file, at));
            at += 4;
            int payloadAt = at;
            int paddedSize = checked(size + (size & 1));
            at += paddedSize;

            if (id == 0x0409 || WritableResourceIds.Contains(id))
            {
                if (id != 0x0409 && replacements.TryGetValue(id, out byte[]? replacement))
                {
                    WriteReplacement(resources, file.Slice(start, payloadAt - start), sizeAt - start, replacement);
                    written.Add(id);
                }
                continue;
            }
            resources.Write(file.Slice(start, at - start));
        }
        foreach ((ushort id, byte[] payload) in replacements.OrderBy(pair => pair.Key))
        {
            if (written.Contains(id)) continue;
            WriteNew(resources, id, payload);
        }
        if (resources.Length > 4 * 1024 * 1024) throw Limit("The PSD resource section exceeds its budget.");
        long finalLength = (long)file.Length + resources.Length - resourceLength;
        if (finalLength > 64 * 1024 * 1024) throw Limit("The rewritten PSD exceeds its file budget.");

        using var output = new MemoryStream((int)finalLength);
        output.Write(file[..resourceLengthAt]);
        WriteU32(output, (uint)resources.Length);
        resources.Position = 0;
        resources.CopyTo(output);
        output.Write(file[resourcesEnd..]);
        if (output.Length > 64 * 1024 * 1024) throw Limit("The rewritten PSD exceeds its file budget.");
        byte[] result = output.ToArray();
        _ = Read(result);
        byte[]? readIcc = IccProfiles.Read(result);
        if (replacements.TryGetValue(0x040F, out byte[]? expectedIcc) &&
            (readIcc is null || !expectedIcc.AsSpan().SequenceEqual(readIcc)))
            throw Bad("The written PSD ICC resource did not read back exactly.");
        if (!replacements.ContainsKey(0x040F) && readIcc is not null)
            throw Bad("The removed PSD ICC resource is still present.");
        return result;
    }

    private static byte[] Thumbnail(ReadOnlySpan<byte> jpeg)
    {
        byte[] image = jpeg.ToArray();
        using var stream = new MemoryStream(image, false);
        MetadataLimits limits = new();
        JpegScanResult scan = JpegScanner.Scan(stream, limits);
        JpegSegment? frame = scan.Segments.FirstOrDefault(segment => segment.Marker is >= 0xC0 and <= 0xCF && segment.Marker is not (0xC4 or 0xC8 or 0xCC));
        if (frame is null || frame.PayloadLength < 5) throw new MetadataException(MetadataErrorCode.InvalidJpeg, "The PSD thumbnail has no valid JPEG frame.");
        stream.Position = frame.PayloadOffset + 1;
        Span<byte> dimensions = stackalloc byte[4];
        stream.ReadExactly(dimensions);
        int height = BinaryPrimitives.ReadUInt16BigEndian(dimensions);
        int width = BinaryPrimitives.ReadUInt16BigEndian(dimensions[2..]);
        if (width == 0 || height == 0) throw new MetadataException(MetadataErrorCode.InvalidJpeg, "The PSD thumbnail has zero dimensions.");
        long rowBytes = (((long)width * 24 + 31) / 32) * 4;
        long total = checked(rowBytes * height);
        if (image.Length > 4 * 1024 * 1024 - 28 || total > uint.MaxValue) throw Limit("The PSD thumbnail exceeds its resource budget.");
        byte[] data = new byte[28 + image.Length];
        BinaryPrimitives.WriteUInt32BigEndian(data, 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)height);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), (uint)rowBytes);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), (uint)total);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), (uint)image.Length);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(24), 24);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(26), 1);
        image.CopyTo(data.AsSpan(28));
        return data;
    }

    private static void WriteReplacement(Stream output, ReadOnlySpan<byte> prefix, int sizeOffset, byte[] payload)
    {
        if (payload.Length > 4 * 1024 * 1024) throw Limit("A PSD resource exceeds its resource budget.");
        byte[] header = prefix.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(sizeOffset, 4), (uint)payload.Length);
        output.Write(header);
        output.Write(payload);
        if ((payload.Length & 1) != 0) output.WriteByte(0);
    }

    private static void WriteNew(Stream output, ushort id, byte[] payload)
    {
        if (payload.Length > 4 * 1024 * 1024) throw Limit("A PSD resource exceeds its resource budget.");
        output.Write("8BIM"u8);
        Span<byte> field = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(field, id);
        output.Write(field[..2]);
        output.Write([0, 0]); // empty Pascal name and even padding
        BinaryPrimitives.WriteUInt32BigEndian(field, (uint)payload.Length);
        output.Write(field);
        output.Write(payload);
        if ((payload.Length & 1) != 0) output.WriteByte(0);
    }

    private static void SkipSection(ReadOnlySpan<byte> file, ref int at)
    {
        if (file.Length - at < 4) throw Bad("Truncated PSD section length.");
        int size = checked((int)U32(file, at));
        at = checked(at + 4 + size);
    }

    private static void WriteU32(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        output.Write(bytes);
    }
    private static MetadataException Limit(string message) => new(MetadataErrorCode.LimitExceeded, message);
}
