using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Psd;

/// <summary>
/// Read-only inspection of a deliberately restricted PSD v1: 8-bit RGB,
/// three channels, no layers, and uncompressed merged image data. Resource
/// payloads are returned as opaque copies, not interpreted or validated metadata.
/// This API does not decode pixels or write files.
/// </summary>
public sealed class PsdMetadata
{
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxResourceBytes = 4 * 1024 * 1024;
    private const int MaxResources = 1024;
    private readonly Dictionary<ushort, byte[]> resources;

    private PsdMetadata(uint width, uint height, Dictionary<ushort, byte[]> resources)
    {
        Width = width;
        Height = height;
        this.resources = resources;
    }

    /// <summary>Width in pixels from the PSD header.</summary>
    public uint Width { get; }
    /// <summary>Height in pixels from the PSD header.</summary>
    public uint Height { get; }

    /// <summary>
    /// Return an independent copy of a resource payload by its ID, or null if
    /// absent. Names and padding are not included. Payloads may be malformed;
    /// callers must validate them before interpreting them.
    /// </summary>
    public byte[]? GetResource(ushort id) => resources.TryGetValue(id, out byte[]? bytes) ? (byte[])bytes.Clone() : null;

    /// <summary>Inspect a complete PSD buffer, subject to fixed finite limits.</summary>
    public static PsdMetadata Read(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes) throw Limit();
        if (data.Length < 26) throw Bad("Truncated PSD header.");
        if (!data[..4].SequenceEqual("8BPS"u8)) throw Bad("Invalid PSD signature.");
        if (U16(data, 4) != 1) throw Unsupported("Only PSD version 1 is supported, not PSB.");
        for (int i = 6; i < 12; i++) if (data[i] != 0) throw Bad("Reserved header bytes must be zero.");
        uint height = U32(data, 14), width = U32(data, 18);
        if (height == 0 || width == 0 || height > 30000 || width > 30000) throw Bad("Invalid PSD dimensions.");
        if (U16(data, 12) != 3 || U16(data, 22) != 8 || U16(data, 24) != 3)
            throw Unsupported("Only three-channel 8-bit RGB PSD is supported.");
        int position = 26;
        ReadOnlySpan<byte> color = Section(data, ref position);
        if (!color.IsEmpty) throw Unsupported("Color-mode data is outside this profile.");
        ReadOnlySpan<byte> resourceSection = Section(data, ref position);
        if (resourceSection.Length > MaxResourceBytes) throw Limit();
        var resources = ParseResources(resourceSection);
        ReadOnlySpan<byte> layers = Section(data, ref position);
        if (!layers.IsEmpty) throw Unsupported("PSD layer and mask information is outside this profile.");
        if (data.Length - position < 2) throw Bad("Missing PSD compression field.");
        if (U16(data, position) != 0) throw Unsupported("Only uncompressed merged pixel data is supported.");
        position += 2;
        long pixelBytes = (long)width * height * 3;
        if (pixelBytes != data.Length - position) throw Bad("Merged pixel data length mismatch.");
        return new PsdMetadata(width, height, resources);
    }

    private static Dictionary<ushort, byte[]> ParseResources(ReadOnlySpan<byte> data)
    {
        int at = 0;
        var result = new Dictionary<ushort, byte[]>();
        while (at < data.Length)
        {
            if (result.Count == MaxResources) throw Limit();
            if (data.Length - at < 7 || !data.Slice(at, 4).SequenceEqual("8BIM"u8)) throw Bad("Invalid PSD resource header.");
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
    private static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "PSD exceeds a fixed read budget.");
}
