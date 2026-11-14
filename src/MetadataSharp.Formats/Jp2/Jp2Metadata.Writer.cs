using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Operations;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Jp2;

public sealed partial class Jp2Metadata
{
    private const uint HeaderBox = 0x6A703268, ColorBox = 0x636F6C72, UuidBox = 0x75756964;
    private static ReadOnlySpan<byte> ExifUuid => "JpgTiffExif->JP2"u8;
    private static ReadOnlySpan<byte> IptcUuid => [0x33, 0xC7, 0xA4, 0xD2, 0xB8, 0x1D, 0x47, 0x23, 0xA0, 0xBA, 0xF1, 0xA3, 0xE0, 0x97, 0xAD, 0x38];
    private static ReadOnlySpan<byte> XmpUuid => [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];

    /// <summary>
    /// Rewrites the metadata boxes of a complete boxed JP2 file. The arguments are the complete
    /// target set: null metadata removes its UUID box, and a null ICC profile removes embedded
    /// restricted-profile color boxes where a non-profile color specification remains.
    /// </summary>
    /// <remarks>
    /// EXIF is re-encoded from editable tags; MakerNote, image-address tags and the EXIF thumbnail
    /// directory are not carried. IPTC and XMP use their packet bytes. Unknown boxes and the
    /// codestream are copied byte for byte. The output is parsed again before it is returned.
    /// A profile is stored in a method-2 <c>colr</c> child of <c>jp2h</c>; removal is refused if
    /// it would leave no color specification.
    /// </remarks>
    /// <param name="file">A complete boxed JP2 file in the supported profile.</param>
    /// <param name="exif">Editable EXIF metadata to write, or null to remove its UUID box.</param>
    /// <param name="iptc">IPTC packet to write, or null to remove its UUID box.</param>
    /// <param name="xmp">XMP packet to write, or null to remove its UUID box and recognized XML XMP packet.</param>
    /// <param name="iccProfile">An exact ICC profile to write, or null to remove embedded profiles.</param>
    /// <returns>A JP2 file with the supplied metadata set.</returns>
    /// <exception cref="MetadataException">The JP2 structure is malformed or unsupported, a value exceeds a finite budget, the ICC profile is invalid, or ICC removal would leave no color specification.</exception>
    public static byte[] Write(ReadOnlySpan<byte> file, ExifMetadata? exif = null, IptcPacket? iptc = null,
        XmpPacket? xmp = null, byte[]? iccProfile = null)
    {
        _ = Read(file);
        var replacements = new List<(byte[] Uuid, byte[] Payload)>();
        if (exif is not null)
        {
            var edit = new ExifEdit();
            ExvFile.CopyTags(exif, edit, null);
            if (!edit.IsEmpty && ExifBlockEditor.Create(edit.Snapshot(), new MetadataLimits(), ExifTiffReader.NoCeiling, out _) is byte[] block)
                replacements.Add((ExifUuid.ToArray(), block));
        }
        if (iptc is { ByteLength: > 0 })
        {
            byte[] bytes = new byte[iptc.ByteLength];
            iptc.CopyRawTo(bytes);
            replacements.Add((IptcUuid.ToArray(), bytes));
        }
        if (xmp is { ByteLength: > 0 })
        {
            byte[] bytes = new byte[xmp.ByteLength];
            xmp.CopyRawTo(bytes);
            replacements.Add((XmpUuid.ToArray(), bytes));
        }
        byte[]? profile = iccProfile is null ? null : (byte[])iccProfile.Clone();
        if (profile is not null)
        {
            IccProfiles.Exact(profile);
            if (profile.Length > new MetadataLimits().MaxValueBytes) throw Limit();
        }

        var output = new MemoryStream(file.Length + 128);
        int at = 0, boxes = 0;
        bool headerWritten = false;
        while (at < file.Length)
        {
            Box box = Next(file, ref at, ref boxes);
            int start = box.Payload - 8;
            if (box.Type == HeaderBox)
            {
                byte[] rewritten = RewriteHeader(file, box, ref boxes, profile);
                output.Write(rewritten);
                headerWritten = true;
                foreach ((byte[] uuid, byte[] payload) in replacements) WriteUuid(output, uuid, payload);
                continue;
            }
            if (box.Type == UuidBox && IsMetadataUuid(file.Slice(box.Payload, box.End - box.Payload))) continue;
            if (box.Type == 0x786D6C20 && IsXmpXml(file.Slice(box.Payload, box.End - box.Payload))) continue;
            output.Write(file.Slice(start, box.End - start));
        }
        if (!headerWritten) throw Bad("JP2 header box was not found.");
        if (output.Length > MaxFileBytes) throw Limit();
        byte[] result = output.ToArray();
        _ = Read(result);
        byte[]? readProfile = IccProfiles.Read(result);
        if (profile is not null && (readProfile is null || !profile.AsSpan().SequenceEqual(readProfile)))
            throw Bad("The written JP2 ICC profile did not read back exactly.");
        if (profile is null && readProfile is not null)
            throw Bad("A JP2 ICC profile remains after removal.");
        return result;
    }

    private static byte[] RewriteHeader(ReadOnlySpan<byte> source, Box header, ref int boxes, byte[]? profile)
    {
        using var children = new MemoryStream(header.Length);
        int at = header.Payload;
        bool hasColorSpecification = false, hasProfile = false, wroteProfile = false;
        while (at < header.End)
        {
            Box child = Next(source[..header.End], ref at, ref boxes);
            int start = child.Payload - 8;
            if (child.Type != ColorBox)
            {
                children.Write(source.Slice(start, child.End - start));
                continue;
            }
            if (child.End - child.Payload < 3) throw Bad("Truncated JP2 color specification box.");
            byte method = source[child.Payload];
            if (method is 2 or 3)
            {
                hasProfile = true;
                if (profile is not null && !wroteProfile)
                {
                    WriteColorProfile(children, profile);
                    wroteProfile = true;
                    hasColorSpecification = true;
                }
                continue;
            }
            hasColorSpecification = true;
            children.Write(source.Slice(start, child.End - start));
        }
        if (profile is not null && !wroteProfile)
        {
            WriteColorProfile(children, profile);
            hasColorSpecification = true;
        }
        else if (profile is null && hasProfile && !hasColorSpecification)
        {
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "Removing the JP2 ICC profile would leave the header without a color specification.");
        }
        if (!hasColorSpecification) throw Bad("The JP2 header has no color specification.");
        long length = 8 + children.Length;
        if (length > MaxFileBytes || length > uint.MaxValue) throw Limit();
        using var output = new MemoryStream((int)length);
        WriteBoxHeader(output, (uint)length, HeaderBox);
        children.Position = 0;
        children.CopyTo(output);
        return output.ToArray();
    }

    private static void WriteColorProfile(Stream output, byte[] profile)
    {
        long length = 11L + profile.Length;
        if (length > uint.MaxValue) throw Limit();
        WriteBoxHeader(output, (uint)length, ColorBox);
        output.Write([2, 0, 0]); // Restricted ICC profile, precedence zero, approximation zero.
        output.Write(profile);
    }

    private static ReadOnlySpan<byte> ExifTiff(byte[] payload)
    {
        ReadOnlySpan<byte> bytes = payload;
        if (bytes.Length >= 4 && (bytes[..4].SequenceEqual(new byte[] { 0x49, 0x49, 42, 0 }) ||
            bytes[..4].SequenceEqual(new byte[] { 0x4D, 0x4D, 0, 42 }))) return bytes;
        int found = bytes.IndexOf("Exif\0\0"u8);
        if (found >= 0 && found + 6 < bytes.Length) return bytes[(found + 6)..];
        throw new MetadataException(MetadataErrorCode.InvalidExif, "The JP2 Exif UUID box has no TIFF header.");
    }

    private static bool IsMetadataUuid(ReadOnlySpan<byte> payload) => payload.Length >= 16 &&
        (payload[..16].SequenceEqual(ExifUuid) || payload[..16].SequenceEqual(IptcUuid) || payload[..16].SequenceEqual(XmpUuid));

    private static bool IsXmpXml(ReadOnlySpan<byte> packet)
    {
        try { _ = XmpPacket.Parse(packet); return true; }
        catch (MetadataException) { return false; }
    }

    private static void WriteUuid(Stream output, byte[] uuid, byte[] payload)
    {
        long length = 24L + payload.Length;
        if (length > uint.MaxValue) throw Limit();
        WriteBoxHeader(output, (uint)length, UuidBox);
        output.Write(uuid);
        output.Write(payload);
    }

    private static void WriteBoxHeader(Stream output, uint length, uint type)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, length);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], type);
        output.Write(header);
    }

}
