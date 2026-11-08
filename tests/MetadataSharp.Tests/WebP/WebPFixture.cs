using System.Buffers.Binary;
using System.Text;

namespace MetadataSharp.Tests.WebP;

/// <summary>Owned one-pixel inputs encoded by pinned test-only tools; see fixture provenance.</summary>
internal static class WebPFixture
{
    internal static byte[] Image(int kind) => Convert.FromHexString(kind switch
    {
        0 => "524946462c0000005745425056503820200000007001009d012a010001000200342594027401400000fefa79509bb0edc9f85400",
        1 => "524946461e000000574542505650384c110000002f0000000007508a2ad4a3ff8188e87f0000",
        2 => "524946464800000057454250565038580a00000010000000000000000000414c504802000000008056503820200000007001009d012a010001000200342594027401400000fefa79509bb0edc9f85400",
        3 => "524946461e000000574542505650384c110000002f0000001007508a2ad4a3808188e87f0000",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    });
    internal static byte[] Chunk(string kind, byte[] payload)
    {
        byte[] data = new byte[8 + payload.Length + (payload.Length % 2)]; Encoding.ASCII.GetBytes(kind, data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)payload.Length); payload.CopyTo(data, 8); return data;
    }
    internal static byte[] Riff(params byte[][] chunks)
    {
        byte[] data = new byte[12 + chunks.Sum(c => c.Length)]; "RIFF"u8.CopyTo(data); "WEBP"u8.CopyTo(data.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)data.Length - 8);
        int at = 12; foreach (byte[] chunk in chunks) { chunk.CopyTo(data, at); at += chunk.Length; } return data;
    }
    internal static byte[] Extended(byte flags, params byte[][] chunks) => Riff([Chunk("VP8X", [flags, 0, 0, 0, 0, 0, 0, 0, 0, 0]), .. chunks]);
}
