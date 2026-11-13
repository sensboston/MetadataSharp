using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Jp2;

/// <summary>
/// Narrow read-only boxed JP2 header inspection. Dimensions are declarations,
/// not decoded or validated image pixels. XML box payloads are opaque, not
/// necessarily XMP. This does not validate the JPEG 2000 codestream.
/// </summary>
public sealed class Jp2Metadata
{
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxXmlBytes = 4 * 1024 * 1024;
    private const int MaxBoxes = 1024;
    private readonly byte[][] xmlBoxes;

    private Jp2Metadata(uint width, uint height, List<byte[]> xml)
    {
        Width = width; Height = height; xmlBoxes = xml.ToArray();
    }
    /// <summary>Declared image width.</summary>
    public uint Width { get; }
    /// <summary>Declared image height.</summary>
    public uint Height { get; }
    /// <summary>Number of top-level XML boxes, irrespective of their content.</summary>
    public int XmlBoxCount => xmlBoxes.Length;
    /// <summary>Return an independent copy of an opaque XML box payload.</summary>
    /// <param name="index">Zero-based index of the XML box in the order returned by the reader.</param>
    /// <returns>An independent copy of the indexed XML box payload.</returns>
    public byte[] GetXmlBox(int index) => (byte[])xmlBoxes[index].Clone();

    /// <summary>Inspect one complete boxed JP2 buffer with finite limits.</summary>
    /// <param name="data">Complete JPEG 2000 file bytes starting at the container signature.</param>
    /// <returns>The parsed Jp2Metadata for the complete input file.</returns>
    /// <exception cref="MetadataException">The JP2 structure is malformed, unsupported, or exceeds a configured read budget.</exception>
    public static Jp2Metadata Read(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes) throw Limit();
        int at = 0, boxes = 0, xmlBytes = 0;
        Box signature = Next(data, ref at, ref boxes);
        if (signature.Type != 0x6a502020 || signature.Length != 12 ||
            !data.Slice(signature.Payload, 4).SequenceEqual(new byte[] { 13, 10, 135, 10 }))
            throw Bad("Invalid JP2 signature box.");
        Box fileType = Next(data, ref at, ref boxes);
        if (fileType.Type != 0x66747970 || fileType.Length < 20 || (fileType.Length - 16) % 4 != 0)
            throw Bad("Invalid JP2 file type box.");
        if (U32(data, fileType.Payload) != 0x6a703220 || U32(data, fileType.Payload + 4) != 0)
            throw Unsupported("Only the JP2 brand and version zero are supported.");
        bool compatible = false;
        for (int p = fileType.Payload + 8; p < fileType.End; p += 4)
            compatible |= U32(data, p) == 0x6a703220;
        if (!compatible) throw Unsupported("JP2 compatibility is required.");
        bool seenHeader = false, seenCodestream = false;
        uint width = 0, height = 0;
        var xml = new List<byte[]>();
        while (at < data.Length)
        {
            Box box = Next(data, ref at, ref boxes);
            switch (box.Type)
            {
                case 0x6a502020: case 0x66747970:
                    throw Bad("Duplicate or misplaced JP2 signature/file type box.");
                case 0x6a703268: // jp2h
                    if (seenHeader || seenCodestream) throw Bad("Duplicate or misplaced JP2 header.");
                    seenHeader = true;
                    ReadHeader(data, box, ref boxes, out width, out height);
                    break;
                case 0x6a703263: // jp2c
                    if (!seenHeader || seenCodestream || box.Length == 8) throw Bad("Duplicate, empty or misplaced codestream.");
                    seenCodestream = true;
                    break;
                case 0x786d6c20: // xml (not necessarily XMP)
                    int size = box.Length - 8;
                    if (size > MaxXmlBytes - xmlBytes) throw Limit();
                    xmlBytes += size;
                    xml.Add(data.Slice(box.Payload, size).ToArray());
                    break;
            }
        }
        if (!seenHeader || !seenCodestream) throw Bad("Missing JP2 header or codestream.");
        return new Jp2Metadata(width, height, xml);
    }

    // Read header from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadHeader(ReadOnlySpan<byte> data, Box parent, ref int boxes, out uint width, out uint height)
    {
        int at = parent.Payload;
        Box header = Next(data[..parent.End], ref at, ref boxes);
        if (header.Type != 0x69686472 || header.Length != 22) throw Bad("JP2 image header missing or invalid.");
        height = U32(data, header.Payload); width = U32(data, header.Payload + 4);
        if (width == 0 || height == 0 || U16(data, header.Payload + 8) == 0) throw Bad("Invalid JP2 dimensions or components.");
        if (data[header.Payload + 11] != 7) throw Unsupported("Only JPEG 2000 compression type is supported.");
        bool color = false;
        while (at < parent.End)
        {
            Box child = Next(data[..parent.End], ref at, ref boxes);
            if (child.Type == 0x69686472) throw Bad("Duplicate JP2 image header.");
            if (child.Type == 0x636f6c72) color = true;
        }
        if (!color) throw Bad("Missing JP2 color specification box.");
    }

    // Represent the Box state used by metadata parsing and output.
    private readonly record struct Box(uint Type, int Payload, int End, int Length);
    // Keep Next focused on metadata and its format-specific validation contract.
    private static Box Next(ReadOnlySpan<byte> data, ref int at, ref int count)
    {
        if (++count > MaxBoxes) throw Limit();
        if (at < 0 || data.Length - at < 8) throw Bad("Truncated JP2 box header.");
        uint length = U32(data, at), type = U32(data, at + 4);
        if (length is 0 or 1) throw Unsupported("Open-ended and extended-length boxes are outside this profile.");
        if (length < 8 || length > data.Length - at) throw Bad("Invalid JP2 box length.");
        int start = at; at += (int)length;
        return new Box(type, start + 8, at, (int)length);
    }
    private static ushort U16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));
    private static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
    private static MetadataException Bad(string message) => new(MetadataErrorCode.InvalidJp2, message);
    private static MetadataException Unsupported(string message) => new(MetadataErrorCode.UnsupportedFeature, message);
    // Report a JP2 box or file-size budget violation.
    private static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "JP2 exceeds a fixed read budget.");
}
