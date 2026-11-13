using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats.Jpeg;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// Read-only IPTC access to a whole TIFF-based file. The IIM stream is the value of the IPTC-NAA tag
/// (0x83BB) of the primary directory; a file without that tag is searched for the IPTC resource
/// (0x0404) in its Photoshop image resources tag (0x8649). This API does not write files.
/// </summary>
public static class TiffIptc
{
    private const ushort IptcNaaTag = 0x83BB;
    private const ushort ImageResourcesTag = 0x8649;

    /// <summary>
    /// Reads the IPTC of a complete TIFF-based file held in memory, or returns null when the file
    /// carries none.
    /// </summary>
    /// <exception cref="MetadataException">
    /// What <see cref="TiffExif.Read"/> reports for the file, or the code the IPTC and the Photoshop
    /// resource readers report for a stream they refuse.
    /// </exception>
    /// <param name="data">Complete input file bytes, beginning at the container signature.</param>
    /// <param name="limits">Resource budgets for parsing; null selects the default limits.</param>
    /// <param name="options">IPTC read settings; null selects the defaults.</param>
    /// <returns>The parsed IptcPacket for the complete input file.</returns>
    public static IptcPacket? Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null, IptcReadOptions? options = null)
    {
        IptcLimits iptcLimits = IptcPacket.Settings(options);
        ExifMetadata metadata = TiffExif.Read(data, limits);
        if (metadata.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, IptcNaaTag), out ExifEntry? naa) && naa.Value.ByteLength != 0)
        {
            ReadOnlySpan<byte> stream = naa.Value.RawBytes;
            // Writers store the stream as LONGs, which rounds its length up to four bytes with zeros.
            if (naa.Value.FieldType == TiffFieldType.Long) stream = stream[..UnpaddedLength(stream)];
            // The digest of a file that keeps the stream in its own tag lies in the Photoshop resources, when it has them.
            byte[]? digest = null;
            if (metadata.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, ImageResourcesTag), out ExifEntry? beside) && beside.Value.ByteLength != 0)
            {
                try { digest = PhotoshopResources.Parse([.. "Photoshop 3.0\0"u8, .. beside.Value.RawBytes], iptcLimits).StoredDigest(); }
                catch (MetadataException) { }
            }
            return IptcPacket.Inspect(stream, options).WithDigest(digest);
        }
        if (metadata.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, ImageResourcesTag), out ExifEntry? block) && block.Value.ByteLength != 0)
        {
            // The tag holds the resource blocks alone; the APP13 form of the same data begins with a signature.
            ReadOnlySpan<byte> signature = "Photoshop 3.0\0"u8;
            byte[] payload = new byte[signature.Length + block.Value.ByteLength];
            signature.CopyTo(payload);
            block.Value.RawBytes.CopyTo(payload.AsSpan(signature.Length));
            PhotoshopResources resources = PhotoshopResources.Parse(payload, iptcLimits);
            if (resources.IptcForReading() is PhotoshopResources.Resource resource)
                return IptcPacket.Inspect(resource.Data.Span, options).WithDigest(resources.StoredDigest());
        }
        return null;
    }

    /// <summary>
    /// The length of the stream without the one to three zero bytes that follow its last dataset.
    /// Anything else, including a stream this walk cannot follow, is left whole for the IPTC reader
    /// to judge.
    /// </summary>
    internal static int UnpaddedLength(ReadOnlySpan<byte> stream)
    {
        int at = 0;
        while (at < stream.Length && stream[at] == 0x1C)
        {
            if (stream.Length - at < 5) return stream.Length;
            long length = BinaryPrimitives.ReadUInt16BigEndian(stream.Slice(at + 3, 2));
            at += 5;
            if ((length & 0x8000) != 0)
            {
                int width = (int)(length & 0x7FFF);
                if (width is 0 or > 4 || stream.Length - at < width) return stream.Length;
                length = 0;
                for (int i = 0; i < width; i++) length = (length << 8) | stream[at++];
            }
            if (length > stream.Length - at) return stream.Length;
            at += (int)length;
        }
        int padding = stream.Length - at;
        if (padding is 0 or > 3) return stream.Length;
        for (int i = at; i < stream.Length; i++)
            if (stream[i] != 0) return stream.Length;
        return at;
    }
}
