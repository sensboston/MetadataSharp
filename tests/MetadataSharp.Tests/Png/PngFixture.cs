using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MetadataSharp.Tests.Png;

/// <summary>Authored one-pixel PNG; its CRC implementation is independent of the library table.</summary>
internal static class PngFixture
{
    internal static byte[] Chunk(string type, byte[] data)
    {
        byte[] bytes = new byte[data.Length + 12]; BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, bytes.AsSpan(4)); data.CopyTo(bytes, 8); uint crc = uint.MaxValue;
        foreach (byte b in bytes.AsSpan(4, data.Length + 4))
        { crc ^= b; for (int n = 0; n < 8; n++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320); }
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(bytes.Length - 4), ~crc); return bytes;
    }
    internal static byte[] Compress(byte[] bytes, CompressionLevel level = CompressionLevel.Optimal)
    {
        // Independently authored RFC1950 header + final fixed empty block + Adler32.
        // Do not depend on whether a runtime encoder initializes on a zero-length write.
        if (bytes.Length == 0) return [0x78, 0x9c, 3, 0, 0, 0, 0, 1];
        using var result = new MemoryStream(); using (var compressor = new ZLibStream(result, level, true)) compressor.Write(bytes); return result.ToArray();
    }
    internal static byte[] Header(byte color = 2, byte depth = 8, byte interlace = 0) => [0, 0, 0, 1, 0, 0, 0, 1, depth, color, 0, 0, interlace];
    internal static byte[] Build(byte[]? before = null, byte[]? after = null) =>
        [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", Header()), .. before ?? [], .. Chunk("IDAT", Compress([0, 10, 20, 30])), .. after ?? [], .. Chunk("IEND", [])];
    internal static byte[] Xmp(byte[] packet, bool compressed = false, byte[]? language = null, byte method = 0) =>
        Chunk("iTXt", [.. "XML:com.adobe.xmp\0"u8, (byte)(compressed ? 1 : 0), method, .. language ?? [], 0, 0, .. compressed ? Compress(packet) : packet]);
}
