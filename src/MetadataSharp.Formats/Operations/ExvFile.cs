using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Operations;

/// <summary>Writes the metadata container of exiv2: the signature, JPEG-style segments, and an end marker.</summary>
public static class ExvFile
{
    internal static ReadOnlySpan<byte> Signature => [0xFF, 0x01, (byte)'E', (byte)'x', (byte)'i', (byte)'v', (byte)'2'];
    internal static ReadOnlySpan<byte> ExifHeader => "Exif\0\0"u8;
    internal static ReadOnlySpan<byte> XmpHeader => "http://ns.adobe.com/xap/1.0/\0"u8;
    internal static ReadOnlySpan<byte> PhotoshopHeader => "Photoshop 3.0\0"u8;

    /// <summary>
    /// An EXV file of the metadata given; a part that is null is left out.
    /// </summary>
    /// <remarks>
    /// The EXIF block is built anew, little-endian, from the tags an <see cref="ExifEdit"/> can set
    /// (<see cref="ExifEdit.IsEditable"/>): the MakerNote, the tags of image data and the tags of the
    /// thumbnail directory are not carried, since their offsets belong to the file they were read
    /// from. <paramref name="thumbnail"/> becomes the thumbnail of the new block. Each part has to
    /// fit one segment of 64 KiB.
    /// </remarks>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.ExifTooLarge"/> for an EXIF block over one segment;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> for an IPTC stream, a packet or a comment over one segment.
    /// </exception>
    public static byte[] Create(ExifMetadata? exif, IptcPacket? iptc, XmpPacket? xmp, string? comment = null, ReadOnlySpan<byte> thumbnail = default)
    {
        var output = new MemoryStream();
        output.Write(Signature);
        if (exif is not null || thumbnail.Length != 0)
        {
            var edit = new ExifEdit();
            if (exif is not null) CopyTags(exif, edit, null);
            if (thumbnail.Length != 0) edit.SetThumbnail(thumbnail);
            if (!edit.IsEmpty && ExifBlockEditor.Create(edit.Snapshot(), new MetadataLimits(), ExifTiffReader.SegmentCeiling, out _) is byte[] block)
                Segment(output, 0xE1, ExifHeader, block, "EXIF block");
        }
        if (iptc is not null)
        {
            byte[] stream = new byte[iptc.ByteLength];
            iptc.CopyRawTo(stream);
            // One image resource: the signature, the number of IPTC-NAA, an empty name, the length, the stream, to an even length.
            byte[] resource = new byte[12 + stream.Length + (stream.Length & 1)];
            "8BIM"u8.CopyTo(resource);
            resource[4] = 0x04; resource[5] = 0x04;
            BinaryPrimitives.WriteUInt32BigEndian(resource.AsSpan(8), (uint)stream.Length);
            stream.CopyTo(resource, 12);
            Segment(output, 0xED, PhotoshopHeader, resource, "IPTC stream");
        }
        if (xmp is not null)
        {
            byte[] packet = new byte[xmp.ByteLength];
            xmp.CopyRawTo(packet);
            Segment(output, 0xE1, XmpHeader, packet, "XMP packet");
        }
        if (comment is not null)
            Segment(output, 0xFE, default, Encoding.UTF8.GetBytes(comment), "comment");
        output.Write([0xFF, 0xD9]);
        return output.ToArray();
    }

    /// <summary>
    /// Sets in <paramref name="edit"/> every tag of <paramref name="exif"/> an edit can set, outside
    /// the thumbnail directory; a tag the edit refuses is named in <paramref name="notes"/>.
    /// </summary>
    internal static void CopyTags(ExifMetadata exif, ExifEdit edit, List<string>? notes)
    {
        foreach (ExifEntry entry in exif.Entries)
        {
            if (entry.Key.Directory == ExifDirectoryKind.Thumbnail || !ExifEdit.IsEditable(entry.Key)) continue;
            try { edit.SetUnchecked(entry.Key, entry.Value); }
            catch (ArgumentException refusal)
            {
                notes?.Add(MetadataSharp.Core.Operations.MetadataKeys.Format(entry.Key) + " is not carried: " + refusal.Message);
            }
        }
    }

    private static void Segment(Stream output, byte marker, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, string what)
    {
        int length = 2 + header.Length + payload.Length;
        if (length > 0xFFFF)
            throw new MetadataException(what == "EXIF block" ? MetadataErrorCode.ExifTooLarge : MetadataErrorCode.LimitExceeded,
                "The " + what + " does not fit one segment of an EXV file.");
        output.Write([0xFF, marker, (byte)(length >> 8), (byte)length]);
        output.Write(header);
        output.Write(payload);
    }
}
