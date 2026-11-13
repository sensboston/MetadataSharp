using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Formats.Psd;

/// <summary>
/// Read-only inspection of a PSD v1 file: its header and its image resources. Resource payloads
/// are returned as opaque copies; <see cref="ReadExif"/>, <see cref="ReadIptc"/> and
/// <see cref="ReadXmp"/> interpret the three that hold metadata. Every section is checked to lie
/// inside the file, and merged image data that is stored raw or run-length encoded must have the
/// length the header implies. Color-mode data and the layer and mask section are length-checked and
/// otherwise not read. <see cref="IsRestrictedProfile"/> tells the narrow profile apart from the rest.
/// This API does not decode pixels; <see cref="Write"/> rewrites the metadata resource section without moving image data.
/// </summary>
public sealed partial class PsdMetadata
{
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxResourceBytes = 4 * 1024 * 1024;
    private const int MaxResources = 1024;
    private readonly Dictionary<ushort, byte[]> resources;

    // Store parsed PSD dimensions, resources and the restricted-layout flag.
    private PsdMetadata(uint width, uint height, Dictionary<ushort, byte[]> resources, bool restricted)
    {
        Width = width;
        Height = height;
        this.resources = resources;
        IsRestrictedProfile = restricted;
    }

    /// <summary>Width in pixels from the PSD header.</summary>
    public uint Width { get; }
    /// <summary>Height in pixels from the PSD header.</summary>
    public uint Height { get; }

    /// <summary>
    /// True for the narrow profile: three-channel 8-bit RGB, no color-mode data, an empty layer and
    /// mask section, raw merged image data, and every resource framed as 8BIM. False for any other
    /// file this reader accepts, of which only the header and the resources were read.
    /// </summary>
    public bool IsRestrictedProfile { get; }

    /// <summary>
    /// Reads the EXIF block of resource 0x0422, or returns null when the file has none. The block is
    /// read as the EXIF block of any other container is, under the same size cap and limits; one that
    /// only the read-only rules accept comes back with <see cref="ExifMetadata.IsEditable"/> false.
    /// </summary>
    /// <param name="limits">Resource budgets applied while parsing PSD; null selects the default limits.</param>
    /// <returns>The parsed EXIF metadata, or null when the file has no EXIF block.</returns>
    public ExifMetadata? ReadExif(MetadataLimits? limits = null) =>
        resources.TryGetValue(0x0422, out byte[]? block) && block.Length != 0 ? ExifTiffReader.Inspect(block, limits) : null;

    /// <summary>Reads the IPTC stream of resource 0x0404, or returns null when the file has none.</summary>
    /// <returns>The parsed IPTC packet, or null when the file has no IPTC data.</returns>
    public IptcPacket? ReadIptc() =>
        resources.TryGetValue(0x0404, out byte[]? stream) && stream.Length != 0
            ? IptcPacket.Inspect(stream).WithDigest(resources.TryGetValue(0x0425, out byte[]? digest) && digest.Length == 16 ? digest : null) : null;

    /// <summary>Reads the XMP packet of resource 0x0424, or returns null when the file has none.</summary>
    /// <returns>The parsed XMP packet, or null when the file has no XMP packet.</returns>
    public XmpPacket? ReadXmp() =>
        resources.TryGetValue(0x0424, out byte[]? packet) && packet.Length != 0 ? XmpPacket.Parse(packet) : null;

    /// <summary>
    /// Return an independent copy of a resource payload by its ID, or null if
    /// absent. Names and padding are not included. Payloads may be malformed;
    /// callers must validate them before interpreting them.
    /// </summary>
    /// <param name="id">Photoshop image-resource ID to look up.</param>
    /// <returns>A copy of the resource payload, or null when no resource with that ID exists.</returns>
    public byte[]? GetResource(ushort id) => resources.TryGetValue(id, out byte[]? bytes) ? (byte[])bytes.Clone() : null;

    /// <summary>Inspect a complete PSD buffer, subject to fixed finite limits.</summary>
    /// <param name="data">Complete PSD file bytes starting at the container signature.</param>
    /// <returns>The parsed PsdMetadata for the complete input file.</returns>
    /// <exception cref="MetadataException">The PSD header or resource section is malformed, unsupported, or exceeds a configured budget.</exception>
    public static PsdMetadata Read(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes) throw Limit();
        if (data.Length < 26) throw Bad("Truncated PSD header.");
        if (!data[..4].SequenceEqual("8BPS"u8)) throw Bad("Invalid PSD signature.");
        if (U16(data, 4) != 1) throw Unsupported("Only PSD version 1 is supported, not PSB.");
        for (int i = 6; i < 12; i++) if (data[i] != 0) throw Bad("Reserved header bytes must be zero.");
        uint height = U32(data, 14), width = U32(data, 18);
        if (height == 0 || width == 0 || height > 30000 || width > 30000) throw Bad("Invalid PSD dimensions.");
        int channels = U16(data, 12), depth = U16(data, 22), mode = U16(data, 24);
        // The values the format defines: 1 to 56 channels, four depths, eight color modes.
        if (channels is < 1 or > 56 || depth is not (1 or 8 or 16 or 32) || mode is not (0 or 1 or 2 or 3 or 4 or 7 or 8 or 9))
            throw Bad("A PSD header field is outside the values the format defines.");
        int position = 26;
        ReadOnlySpan<byte> color = Section(data, ref position);
        ReadOnlySpan<byte> resourceSection = Section(data, ref position);
        if (resourceSection.Length > MaxResourceBytes) throw Limit();
        var resources = ParseResources(resourceSection, out bool plainFraming);
        ReadOnlySpan<byte> layers = Section(data, ref position);
        if (data.Length - position < 2) throw Bad("Missing PSD compression field.");
        int compression = U16(data, position);
        position += 2;
        long rows = (long)channels * height, rowBytes = ((long)width * depth + 7) / 8;
        ReadOnlySpan<byte> image = data[position..];
        switch (compression)
        {
            case 0:
                if (rows * rowBytes != image.Length) throw Bad("Merged pixel data length mismatch.");
                break;
            case 1:
                // One 16-bit byte count per scan line of every channel, then the lines themselves.
                if (rows * 2 > image.Length) throw Bad("Truncated PSD run-length table.");
                long encoded = 0;
                for (int row = 0; row < rows; row++)
                {
                    int count = U16(image, row * 2);
                    if (count < 2) throw Bad("A PSD run-length scan line is too short.");
                    encoded += count;
                }
                if (rows * 2 + encoded != image.Length) throw Bad("Merged pixel data length mismatch.");
                break;
            case 2 or 3:
                // A deflate stream to the end of the file: nothing about its length is known without inflating it.
                if (image.IsEmpty) throw Bad("Missing PSD merged pixel data.");
                break;
            default:
                throw Bad("Unknown PSD compression method.");
        }
        bool restricted = channels == 3 && depth == 8 && mode == 3 && color.IsEmpty && layers.IsEmpty &&
            compression == 0 && plainFraming;
        return new PsdMetadata(width, height, resources, restricted);
    }

    /// <summary>
    /// The resource signatures Adobe applications write: Photoshop's 8BIM, and AgHg, DCSR and PHUT.
    /// The others frame a resource exactly as 8BIM does.
    /// </summary>
    private static bool IsResourceSignature(ReadOnlySpan<byte> signature, out bool plain)
    {
        plain = signature.SequenceEqual("8BIM"u8);
        return plain || signature.SequenceEqual("AgHg"u8) || signature.SequenceEqual("DCSR"u8) || signature.SequenceEqual("PHUT"u8);
    }

    // Read resources from bounded metadata data, validating its declared extent before consuming fields.
    private static Dictionary<ushort, byte[]> ParseResources(ReadOnlySpan<byte> data, out bool plainFraming)
    {
        int at = 0;
        plainFraming = true;
        var result = new Dictionary<ushort, byte[]>();
        while (at < data.Length)
        {
            if (result.Count == MaxResources) throw Limit();
            if (data.Length - at < 7 || !IsResourceSignature(data.Slice(at, 4), out bool plain)) throw Bad("Invalid PSD resource header.");
            plainFraming &= plain;
            ushort id = U16(data, at + 4);
            at += 6;
            int nameLength = data[at];
            int nameFieldLength = (1 + nameLength + 1) & ~1;
            if (nameFieldLength > data.Length - at) throw Bad("Truncated resource name.");
            at += nameFieldLength;
            if (data.Length - at < 4) throw Bad("Truncated resource length.");
            uint length = U32(data, at); at += 4;
            if (length > MaxResourceBytes) throw Limit();
            long paddedLength = (long)length + (length & 1);
            if (paddedLength > data.Length - at) throw Bad("Truncated resource payload or padding.");
            if (result.ContainsKey(id)) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate PSD image resource ID.");
            result.Add(id, data.Slice(at, (int)length).ToArray());
            at += (int)paddedLength;
        }
        return result;
    }

    // Keep Section focused on metadata and its format-specific validation contract.
    private static ReadOnlySpan<byte> Section(ReadOnlySpan<byte> data, ref int at)
    {
        if (data.Length - at < 4) throw Bad("Truncated PSD section length.");
        uint length = U32(data, at); at += 4;
        if (length > data.Length - at) throw Bad("PSD section exceeds input.");
        ReadOnlySpan<byte> result = data.Slice(at, (int)length);
        at += (int)length;
        return result;
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at, 2));
    private static uint U32(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(at, 4));
    private static MetadataException Bad(string message) => new(MetadataErrorCode.InvalidPsd, message);
    private static MetadataException Unsupported(string message) => new(MetadataErrorCode.UnsupportedFeature, message);
    // Reject PSD resource data once the fixed parsing budget is exhausted.
    private static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "PSD exceeds a fixed read budget.");
}
