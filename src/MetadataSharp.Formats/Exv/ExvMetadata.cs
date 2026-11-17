using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Internal;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;
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

    // Capture the metadata packets extracted from the EXV payload.
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

    /// <summary>
    /// The IPTC record of the Photoshop APP13 segments, or null when there is none. Photoshop splits one
    /// block of resources across consecutive segments; the segments are joined as a JPEG file's are,
    /// and a record joined from several is for reading only.
    /// </summary>
    public IptcPacket? Iptc { get; }

    /// <summary>Reads a complete EXV buffer. Segments other than EXIF, XMP and Photoshop are skipped.</summary>
    /// <param name="data">Complete EXV file bytes starting at the container signature.</param>
    /// <param name="limits">Resource budgets applied while parsing EXV; null selects the default limits.</param>
    /// <returns>The parsed ExvMetadata for the complete input file.</returns>
    public static ExvMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null) =>
        ReadView(ByteView.Over(data), limits, null);

    /// <summary>The same read of a file the caller already holds.</summary>
    /// <param name="data">The file.</param>
    /// <param name="limits">The budgets, or null for the defaults.</param>
    /// <param name="refusals">Where a kind its reader refused is noted.</param>
    /// <returns>The parsed metadata.</returns>
    internal static ExvMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits, KindRefusals? refusals) =>
        ReadView(ByteView.Over(data), limits, refusals);

    /// <summary>
    /// Reads the segments of a file held as a view: the signature, then one segment at a time, and
    /// only the payload of a segment this reader takes in. With <paramref name="refusals"/> the
    /// segment of a kind its reader refuses is noted there and the walk goes on, so that the other
    /// kinds are still read; a later segment of that kind is not taken in its place.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="limits">The budgets, or null for the defaults.</param>
    /// <param name="refusals">Where a kind its reader refused is noted.</param>
    /// <returns>The parsed metadata.</returns>
    internal static ExvMetadata ReadView(ByteView data, MetadataLimits? limits, KindRefusals? refusals)
    {
        limits ??= new();
        limits.Validate();
        if (data.Length > MaxFileBytes || data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EXV file exceeds its byte budget.", 0);
        if (data.Length < 7 || !data.Slice(0, 7).SequenceEqual((ReadOnlySpan<byte>)[0xFF, 0x01, (byte)'E', (byte)'x', (byte)'i', (byte)'v', (byte)'2']))
            throw Bad("The EXV signature is missing.", 0);
        ExifMetadata? exif = null;
        XmpPacket? xmp = null;
        IptcPacket? iptc = null;
        var photoshop = new List<(int At, int Length)>();
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
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 2, 2));
            if (length < 2 || length > data.Length - position - 2)
                throw Bad("A segment length is invalid.", position);
            int payloadAt = position + 4;
            ReadOnlySpan<byte> payload = data.Slice(payloadAt, length - 2);
            if (marker == 0xE1 && exif is null && refusals?.Exif is null && payload.StartsWith("Exif\0\0"u8))
            {
                try { exif = ExifTiffReader.Inspect(payload[6..], limits, payloadAt + 6); }
                catch (MetadataException refusal) when (refusals is not null) { refusals.Exif = refusal; }
            }
            else if (marker == 0xE1 && xmp is null && refusals?.Xmp is null && payload.StartsWith("http://ns.adobe.com/xap/1.0/\0"u8))
            {
                try { xmp = XmpPacket.Parse(payload["http://ns.adobe.com/xap/1.0/\0"u8.Length..]); }
                catch (MetadataException refusal) when (refusals is not null) { refusals.Xmp = refusal; }
            }
            else if (marker == 0xED && payload.StartsWith(PhotoshopSignature))
            {
                photoshop.Add((payloadAt, length - 2));
            }
            position += 2 + length;
        }
        if (photoshop.Count != 0)
        {
            try { iptc = JoinedIptc(data, photoshop); }
            catch (MetadataException refusal) when (refusals is not null) { refusals.Iptc = refusal; }
        }
        return new ExvMetadata(exif, xmp, iptc);
    }

    private static ReadOnlySpan<byte> PhotoshopSignature => "Photoshop 3.0\0"u8;

    /// <summary>
    /// The IPTC record of the Photoshop segments, joined in their order as the JPEG reader joins them:
    /// the signature once, then the resource data of each segment. An EXV file holds what the JPEG
    /// file it was extracted from held, and a block split across segments is whole only when joined.
    /// </summary>
    private static IptcPacket? JoinedIptc(ByteView data, List<(int At, int Length)> segments)
    {
        var settings = new IptcLimits();
        int signature = PhotoshopSignature.Length;
        long total = signature + segments.Sum(segment => (long)segment.Length - signature);
        if (total > settings.MaxPacketBytes) throw IptcPacket.Limit();
        byte[] joined = new byte[total];
        PhotoshopSignature.CopyTo(joined);
        int at = signature;
        foreach ((int start, int length) in segments)
        {
            data.Slice(start + signature, length - signature).CopyTo(joined.AsSpan(at));
            at += length - signature;
        }
        PhotoshopResources resources = PhotoshopResources.Parse(joined, settings);
        if (resources.IptcForReading() is not PhotoshopResources.Resource resource) return null;
        // As in a JPEG file: a record joined from several segments cannot be edited where it lies.
        MetadataException? refusal = segments.Count > 1
            ? new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Multiple Photoshop APP13 segments are unsupported for IPTC operations.")
            : null;
        return IptcPacket.Inspect(resource.Data.Span, null, refusal).WithDigest(resources.StoredDigest());
    }

    // Report invalid EXV framing at the offending input offset.
    private static MetadataException Bad(string message, long offset) =>
        new(MetadataErrorCode.InvalidExv, message, offset);
}
