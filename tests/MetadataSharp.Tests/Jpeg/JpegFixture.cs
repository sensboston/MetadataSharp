namespace MetadataSharp.Tests.Jpeg;

/// <summary>Authored 8x8 grayscale baseline JPEG: DC zero, EOB, and one padding byte.</summary>
internal static class JpegFixture
{
    internal static byte[] OrientationTiff => [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0];
    internal static byte[] Exif(byte[]? tiff = null) => [69, 120, 105, 102, 0, 0, .. tiff ?? OrientationTiff];
    internal static byte[] Segment(byte marker, params byte[] payload) => [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];
    internal static byte[] Build(byte[]? prefix = null, byte[]? entropy = null, byte[]? frame = null, byte[]? scan = null,
        bool restart = false, byte frameMarker = 0xC0)
    {
        byte[] quant = new byte[65]; Array.Fill(quant, (byte)1, 1, 64);
        byte[] huffman = new byte[36]; huffman[1] = 1; huffman[18] = 0x10; huffman[19] = 1;
        return [0xFF, 0xD8, .. prefix ?? [], .. Segment(0xDB, quant), .. Segment(0xC4, huffman),
            .. Segment(frameMarker, frame ?? [8, 0, 8, 0, 8, 1, 1, 0x11, 0]),
            .. restart ? Segment(0xDD, 0, 1) : [], .. Segment(0xDA, scan ?? [1, 1, 0, 0, 63, 0]),
            .. entropy ?? [0x3F], 0xFF, 0xD9];
    }
}
