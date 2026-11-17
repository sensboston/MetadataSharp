namespace MetadataSharp.Tests.Jpeg;

/// <summary>Authored 8x8 grayscale baseline JPEG: DC zero, EOB, and one padding byte.</summary>
internal static class JpegFixture
{
    internal static byte[] OrientationTiff => [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0];
    internal static byte[] Exif(byte[]? tiff = null) => [69, 120, 105, 102, 0, 0, .. tiff ?? OrientationTiff];
    internal static byte[] Segment(byte marker, params byte[] payload) => [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];

    /// <summary>One 8-bit quantization table of ones (Pq/Tq byte plus 64 elements).</summary>
    private static readonly byte[] Quant = FilledQuantizer();
    /// <summary>A DC table with the zero-length code and an AC table with the EOB code.</summary>
    private static readonly byte[] Huffman = ZeroDcEobAc();
    /// <summary>One arithmetic conditioning table entry: AC class, selector 0, two value bytes.</summary>
    private static readonly byte[] Conditioning = [0x10, 0x21, 0];

    /// <summary>A table a test may redefine between scans or before a frame.</summary>
    internal static byte[] HuffmanTable => Huffman;

    internal static byte[] Build(byte[]? prefix = null, byte[]? entropy = null, byte[]? frame = null, byte[]? scan = null,
        bool restart = false, byte frameMarker = 0xC0) =>
        [0xFF, 0xD8, .. prefix ?? [], .. Segment(0xDB, Quant), .. Segment(0xC4, Huffman),
            .. Segment(frameMarker, frame ?? [8, 0, 8, 0, 8, 1, 1, 0x11, 0]),
            .. restart ? Segment(0xDD, 0, 1) : [], .. Segment(0xDA, scan ?? [1, 1, 0, 0, 63, 0]),
            .. entropy ?? [0x3F], 0xFF, 0xD9];

    /// <summary>
    /// 12-bit progressive frame (SOF2 by default) carried by three scans — DC band, full AC band,
    /// then a DC successive-approximation refinement — with a Huffman redefinition between the first
    /// two scans. Every scan keeps its own encoded bytes so nothing but structure is asserted.
    /// </summary>
    internal static byte[] Progressive(byte frameMarker = 0xC2, byte[]? betweenScans = null, byte[]? prefix = null) =>
        [0xFF, 0xD8, .. prefix ?? [], .. Segment(0xDB, Quant), .. Segment(0xC4, Huffman),
            .. Segment(frameMarker, 12, 0, 8, 0, 8, 1, 1, 0x11, 0),
            .. Segment(0xDA, 1, 1, 0, 0, 0, 0), 0x3F,
            .. betweenScans ?? Segment(0xC4, Huffman),
            .. Segment(0xDA, 1, 1, 0, 1, 63, 0), 0x7E,
            .. Segment(0xDA, 1, 1, 0, 0, 0, 0x10), 0x55, 0xFF, 0xD9];

    /// <summary>
    /// Arithmetic-coded frame (SOF9 by default) whose conditioning tables come from DAC and which
    /// carries no DHT at all, so the scan cannot be validated against Huffman definitions.
    /// </summary>
    internal static byte[] Arithmetic(byte frameMarker = 0xC9, byte[]? prefix = null) =>
        [0xFF, 0xD8, .. prefix ?? [], .. Segment(0xDB, Quant), .. Segment(0xCC, Conditioning),
            .. Segment(frameMarker, 8, 0, 8, 0, 8, 1, 1, 0x11, 0), .. Segment(0xDA, 1, 1, 0, 0, 63, 0),
            0x3F, 0xFF, 0xD9];

    private static byte[] FilledQuantizer()
    {
        byte[] quant = new byte[65];
        Array.Fill(quant, (byte)1, 1, 64);
        return quant;
    }

    private static byte[] ZeroDcEobAc()
    {
        byte[] huffman = new byte[36];
        huffman[1] = 1; huffman[18] = 0x10; huffman[19] = 1;
        return huffman;
    }
}
