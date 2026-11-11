using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Jpeg;

namespace MetadataSharp.Formats.Exv;

/// <summary>
/// Read-only access to an EXV metadata file: the 7-byte signature FF 01 "Exiv2" followed by
/// JPEG-style marker segments (APP1 EXIF, APP1 XMP, APP13 Photoshop IRB with IPTC, comments)
/// and an optional EOI. There is no image data. This API does not write files.
/// </summary>
public sealed class ExvMetadata
{
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxSegments = 4_096;

    private ExvMetadata(ExifMetadata? exif, XmpPacket? xmp, IptcPacket? iptc)
    {
        Exif = exif;
        Xmp = xmp;
        Iptc = iptc;
    }

    /// <summary>The EXIF metadata of the first EXIF APP1 segment, or null when there is none.</summary>
    public ExifMetadata? Exif { get; }

    /// <summary>The standard XMP packet of the first XMP APP1 segment, or null when there is none.</summary>
    public XmpPacket? Xmp { get; }

    /// <summary>The IPTC record of the Photoshop APP13 segment, or null when there is none.</summary>
    public IptcPacket? Iptc { get; }

    /// <summary>Reads a complete EXV buffer. Segments other than EXIF, XMP and Photoshop are skipped.</summary>
    public static ExvMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        if (data.Length > MaxFileBytes || data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EXV file exceeds its byte budget.", 0);
        if (!data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0x01, (byte)'E', (byte)'x', (byte)'i', (byte)'v', (byte)'2']))
            throw Bad("The EXV signature is missing.", 0);
        ExifMetadata? exif = null;
        XmpPacket? xmp = null;
        IptcPacket? iptc = null;
        int position = 7, segments = 0;
        while (position < data.Length)
        {
            if (++segments > Math.Min(MaxSegments, limits.MaxSegments))
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "The segment budget was exceeded.", position);
            if (data.Length - position < 2 || data[position] != 0xFF)
                throw Bad("Expected a marker.", position);
            byte marker = data[position + 1];
            if (marker == 0xD9)
                break; // EOI: anything after it is not part of the metadata.
            if (data.Length - position < 4)
                throw Bad("A segment header is truncated.", position);
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
            if (length < 2 || length > data.Length - position - 2)
                throw Bad("A segment length is invalid.", position);
            int payloadAt = position + 4;
            ReadOnlySpan<byte> payload = data.Slice(payloadAt, length - 2);
            if (marker == 0xE1 && exif is null && payload.StartsWith("Exif\0\0"u8))
                exif = ExifTiffReader.Inspect(payload[6..], limits, payloadAt + 6);
            else if (marker == 0xE1 && xmp is null && payload.StartsWith("http://ns.adobe.com/xap/1.0/\0"u8))
                xmp = XmpPacket.Parse(payload["http://ns.adobe.com/xap/1.0/\0"u8.Length..]);
            else if (marker == 0xED && iptc is null && payload.StartsWith("Photoshop 3.0\0"u8))
            {
                PhotoshopResources resources = PhotoshopResources.Parse(payload.ToArray(), new IptcLimits());
                if (resources.Iptc is PhotoshopResources.Resource resource)
                    iptc = IptcPacket.Parse(resource.Data.Span);
            }
            position += 2 + length;
        }
        return new ExvMetadata(exif, xmp, iptc);
    }

    private static MetadataException Bad(string message, long offset) =>
        new(MetadataErrorCode.InvalidExv, message, offset);
}
