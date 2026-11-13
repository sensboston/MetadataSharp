using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.WebP;

// Represent the WebPChunk state used by WebP parsing and output.
internal sealed record WebPChunk(string Type, long Start, long Size)
{
    internal long Payload => Start + 8;
    internal long Length => 8 + Size + (Size & 1);
    internal bool Xmp => Type == "XMP ";
}
internal sealed record WebPScan(long Length, IReadOnlyList<WebPChunk> Chunks, int Width, int Height,
    bool ImageAlphaHint, byte[]? ExtendedPayload, long ImageEnd, bool Unknown);

internal static class WebPScanner
{
    internal static MetadataException Invalid(string message = "Malformed WebP container.") => new(MetadataErrorCode.InvalidWebP, message);
    private static MetadataException Unsupported(string message) => new(MetadataErrorCode.UnsupportedFeature, message);
    internal static int U24(ReadOnlySpan<byte> bytes) => bytes[0] | bytes[1] << 8 | bytes[2] << 16;
    internal static void Put24(Span<byte> bytes, int value) { bytes[0] = (byte)value; bytes[1] = (byte)(value >> 8); bytes[2] = (byte)(value >> 16); }
    // Keep Chunk focused on WebP and its format-specific validation contract.
    internal static byte[] Chunk(string type, byte[] payload)
    {
        byte[] chunk = new byte[checked(8 + payload.Length + (payload.Length & 1))];
        Encoding.ASCII.GetBytes(type, chunk.AsSpan(0, 4)); BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(chunk, 8); return chunk;
    }
    internal static async Task<WebPScan> Scan(Stream source, MetadataLimits limits, bool asynchronous, CancellationToken token)
    {
        if (!source.CanRead || !source.CanSeek || source.Position != 0) throw new ArgumentException("WebP source must be readable, seekable and at zero.", nameof(source));
        long length = source.Length;
        if (length > limits.MaxInputBytes) throw new MetadataException(MetadataErrorCode.LimitExceeded, "Input exceeds its byte budget.");
        if (length < 12 || length > uint.MaxValue - 1L) throw Invalid();
        byte[] riff = new byte[12]; await ContainerIo.Read(source, riff, asynchronous, token).ConfigureAwait(false);
        if (!riff.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !riff.AsSpan(8).SequenceEqual("WEBP"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(riff.AsSpan(4)) != length - 8 || (length & 1) != 0) throw Invalid("Invalid RIFF signature/size.");
        var chunks = new List<WebPChunk>(); byte[]? extended = null;
        bool icc = false, alpha = false, image = false, lossless = false, alphaHint = false, unknown = false;
        int width = 0, height = 0, alphaMode = -1; long alphaSize = 0, imageEnd = 0;
        while (source.Position < length)
        {
            token.ThrowIfCancellationRequested(); long start = source.Position;
            if (length - start < 8) throw Invalid();
            if (chunks.Count >= limits.MaxSegments) throw new MetadataException(MetadataErrorCode.LimitExceeded, "WebP chunk count exceeds its budget.");
            byte[] header = new byte[8]; await ContainerIo.Read(source, header, asynchronous, token).ConfigureAwait(false);
            string type = Encoding.Latin1.GetString(header, 0, 4); long size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            if (size + (size & 1) > length - start - 8) throw Invalid("Invalid WebP chunk range.");
            var chunk = new WebPChunk(type, start, size); byte[] prefix = new byte[(int)Math.Min(size, 10)];
            await ContainerIo.Read(source, prefix, asynchronous, token).ConfigureAwait(false);
            source.Position = start + 8 + size;
            if ((size & 1) != 0)
            { byte[] pad = new byte[1]; await ContainerIo.Read(source, pad, asynchronous, token).ConfigureAwait(false); if (pad[0] != 0) throw Invalid("Nonzero RIFF padding."); }
            switch (type)
            {
                case "VP8X":
                    if (chunks.Count != 0 || size != 10 || extended is not null) throw Invalid("VP8X must be unique, first and 10 bytes.");
                    if ((prefix[0] & 2) != 0) throw Unsupported("Animated WebP is outside the static profile.");
                    if ((prefix[0] & 0xc1) != 0 || prefix[1] != 0 || prefix[2] != 0 || prefix[3] != 0) throw Invalid("Reserved VP8X bits are set.");
                    long canvas = (U24(prefix.AsSpan(4)) + 1L) * (U24(prefix.AsSpan(7)) + 1L);
                    if (canvas > uint.MaxValue) throw Invalid("Invalid WebP canvas area.");
                    extended = prefix; break;
                case "ANIM": case "ANMF": throw Unsupported("Animated WebP is outside the static profile.");
                case "ICCP":
                    if (extended is null || icc || alpha || image) throw Invalid("ICCP ordering/cardinality is invalid.");
                    icc = true; break;
                case "ALPH":
                    if (extended is null || alpha || image || size < 1) throw Invalid("ALPH ordering/cardinality is invalid.");
                    if ((prefix[0] & 0xc0) != 0 || ((prefix[0] >> 4) & 3) > 1 || (prefix[0] & 3) > 1) throw Invalid("Invalid ALPH header.");
                    alpha = true; alphaMode = prefix[0] & 3; alphaSize = size; break;
                case "VP8 ": case "VP8L":
                    if (image) throw Invalid("A static WebP must have exactly one image.");
                    image = true; lossless = type == "VP8L"; imageEnd = start + chunk.Length;
                    if (lossless)
                    {
                        if (size < 5 || prefix[0] != 0x2f || (prefix[4] & 0xe0) != 0) throw Invalid("Invalid VP8L header/version.");
                        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(1));
                        width = (int)(bits & 0x3fff) + 1; height = (int)((bits >> 14) & 0x3fff) + 1; alphaHint = (bits & 0x10000000) != 0;
                        if (alpha) throw Unsupported("ALPH with VP8L is outside the static profile.");
                    }
                    else
                    {
                        if (size < 10 || (prefix[0] & 1) != 0 || (prefix[0] & 0x10) == 0 || !prefix.AsSpan(3, 3).SequenceEqual(new byte[] { 0x9d, 1, 0x2a })) throw Invalid("Invalid static VP8 key-frame header.");
                        if (((prefix[0] >> 1) & 7) > 3) throw Unsupported("Unsupported VP8 version.");
                        int partition = U24(prefix) >> 5;
                        if (partition == 0 || partition > size - 10) throw Invalid("VP8 first partition exceeds its chunk.");
                        width = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(6)) & 0x3fff;
                        height = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(8)) & 0x3fff;
                        if (width == 0 || height == 0) throw Invalid("Invalid VP8 dimensions.");
                    }
                    break;
                case "EXIF": case "XMP ": if (extended is null) throw Invalid("Metadata requires VP8X."); break;
                default: unknown = true; break;
            }
            chunks.Add(chunk);
        }
        if (!image) throw Invalid("Missing WebP image.");
        if (alphaMode == 0 && alphaSize != 1L + (long)width * height) throw Invalid("Raw ALPH size disagrees with canvas.");
        if (extended is not null)
        {
            int flags = extended[0];
            if (U24(extended.AsSpan(4)) + 1 != width || U24(extended.AsSpan(7)) + 1 != height) throw Invalid("Frame and canvas dimensions differ.");
            if (((flags & 0x20) != 0) != icc || ((flags & 8) != 0) != chunks.Any(c => c.Type == "EXIF") ||
                ((flags & 4) != 0) != chunks.Any(c => c.Xmp) || (!lossless && (((flags & 0x10) != 0) != alpha))) throw Invalid("VP8X presence flags disagree with chunks.");
            // VP8L alpha_is_used is only an encoder hint, never evidence about pixels.
        }
        return new(length, chunks.AsReadOnly(), width, height, alphaHint, extended, imageEnd, unknown);
    }
}
