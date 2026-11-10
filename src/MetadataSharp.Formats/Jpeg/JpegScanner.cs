using System.Buffers;
using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Jpeg;

/// <summary>
/// Bounded full-file structural scan of every JPEG process this container can carry.
/// </summary>
/// <remarks>
/// Metadata editing never touches entropy-coded data, so the scan validates *structure* — marker
/// syntax, segment arithmetic, offsets and budgets — and copies compressed scan data through
/// verbatim. Baseline, extended, progressive, lossless and arithmetic frames are therefore all
/// acceptable, as are multiple frames and multiple scans with DHT or DAC definitions between them
/// (ITU-T T.81 B.2.2, B.2.3, F.1.1.3, and Annex A for arithmetic coding). Process-specific decode
/// rules are deliberately not enforced: refusing a file this reader never decodes would only cost
/// its metadata.
/// </remarks>
internal static class JpegScanner
{
    internal static JpegScanResult Scan(Stream source, MetadataLimits limits) =>
        ScanCore(source, limits, false, default).GetAwaiter().GetResult();

    internal static Task<JpegScanResult> ScanAsync(Stream source, MetadataLimits limits, CancellationToken token) =>
        ScanCore(source, limits, true, token);

    private static async Task<JpegScanResult> ScanCore(Stream source, MetadataLimits limits, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        token.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek || source.Position != 0)
            throw new ArgumentException("The source must be readable, seekable and positioned at zero.", nameof(source));
        long length = source.Length;
        if (length > limits.MaxInputBytes) throw Error(MetadataErrorCode.LimitExceeded, "The input exceeds its byte budget.", 0);
        using var input = new BufferedInput(source, limits.CopyBufferBytes, asynchronous, token);
        byte[] payload = ArrayPool<byte>.Shared.Rent(65_533);
        try
        {
            if (await input.Byte().ConfigureAwait(false) != 0xFF || await input.Byte().ConfigureAwait(false) != 0xD8)
                throw Error(MetadataErrorCode.InvalidJpeg, "The source does not start with SOI.", 0);
            var segments = new List<JpegSegment>();
            JpegSegment? exif = null;
            long insertion = 2, scanAt = -1, entropyAt = -1;
            bool leadingApp0 = true;
            Frame? frame = null;
            bool[] quant = new bool[4];
            bool[,] huffman = new bool[2, 4];
            ushort restart = 0;
            int frames = 0, scans = 0;
            bool sawDnl = false;
            // The marker that ended the previous entropy-coded segment is already consumed, so the
            // loop below must be able to take it as its own segment start rather than re-read it.
            int ahead = -1;
            long aheadAt = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                long start;
                byte marker;
                if (ahead >= 0)
                {
                    start = aheadAt; marker = (byte)ahead; ahead = -1;
                }
                else
                {
                    start = input.Position;
                    if (await input.Byte().ConfigureAwait(false) != 0xFF) throw Error(MetadataErrorCode.InvalidJpeg, "Expected a JPEG marker.", start);
                    byte code;
                    do { code = await input.Byte().ConfigureAwait(false); } while (code == 0xFF);
                    marker = code;
                }
                if (marker == 0xD9)
                {
                    if (frames == 0 || scans == 0) throw Error(MetadataErrorCode.InvalidJpeg, "The image holds no frame or no scan.", start);
                    if (frame is { HeightZero: true } && !sawDnl) throw Error(MetadataErrorCode.InvalidJpeg, "A frame of zero height needs DNL.", start);
                    if (input.Position != length) throw Error(MetadataErrorCode.UnsupportedFeature, "Trailing bytes after EOI are not supported.", input.Position);
                    return new(length, segments.AsReadOnly(), exif, insertion, scanAt, entropyAt, start, scans, frames);
                }
                if (marker is 0 or 0xD8 or >= 0xD0 and <= 0xD7)
                    throw Error(MetadataErrorCode.InvalidJpeg, "Unexpected standalone marker outside scan data.", start);
                if (!HasLength(marker))
                    throw Error(MetadataErrorCode.UnsupportedFeature, "The marker is not a length-bearing JPEG segment.", start);
                if (segments.Count >= limits.MaxSegments) throw Error(MetadataErrorCode.LimitExceeded, "The segment budget was exceeded.", start);
                int segmentLength = (await input.Byte().ConfigureAwait(false) << 8) | await input.Byte().ConfigureAwait(false);
                if (segmentLength < 2) throw Error(MetadataErrorCode.InvalidJpeg, "A segment length is less than two.", start);
                int count = segmentLength - 2;
                long payloadAt = input.Position;
                await input.Block(payload, count).ConfigureAwait(false);
                bool isExif = marker == 0xE1 && HasExifSignature(payload, count);
                var segment = new JpegSegment(marker, start, input.Position - start, payloadAt, count, isExif,
                    marker == 0xE1 && payload.AsSpan(0, count).StartsWith("http://ns.adobe.com/xap/1.0/\0"u8),
                    marker == 0xE1 && payload.AsSpan(0, count).StartsWith("http://ns.adobe.com/xmp/extension/\0"u8),
                    marker == 0xED && payload.AsSpan(0, count).StartsWith("Photoshop 3.0\0"u8));
                segments.Add(segment);
                if (isExif)
                {
                    if (exif is not null) throw Error(MetadataErrorCode.AmbiguousMetadata, "Multiple EXIF APP1 segments are not supported.", start);
                    exif = segment;
                }
                if (leadingApp0 && marker == 0xE0) insertion = input.Position;
                else leadingApp0 = false;
                switch (marker)
                {
                    case 0xDB: ReadQuant(payload, count, quant, start); break;
                    case 0xC4: ReadHuffman(payload, count, huffman, start); break;
                    case 0xCC:
                        // Each conditioning table entry is one selector byte plus two value bytes (T.81 A.2.3).
                        if (count is 0 || count % 3 != 0) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid DAC length.", start);
                        for (int i = 0; i < count; i += 3)
                            if ((payload[i] >> 4) > 1 || (payload[i] & 15) > 3)
                                throw Error(MetadataErrorCode.InvalidJpeg, "Invalid DAC table selector.", start);
                        break;
                    case 0xDD:
                        if (count != 2) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid DRI length.", start);
                        restart = BinaryPrimitives.ReadUInt16BigEndian(payload);
                        break;
                    case 0xDC:
                        if (count != 2) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid DNL length.", start);
                        if (frames == 0) throw Error(MetadataErrorCode.InvalidJpeg, "DNL precedes every frame.", start);
                        if (BinaryPrimitives.ReadUInt16BigEndian(payload) == 0) throw Error(MetadataErrorCode.InvalidJpeg, "DNL carries no line count.", start);
                        sawDnl = true;
                        break;
                    case 0xDA:
                        if (frame is null) throw Error(MetadataErrorCode.InvalidJpeg, "SOS precedes the frame header.", start);
                        if (scanAt < 0) scanAt = start;
                        ReadScan(payload, count, frame, quant, huffman, start);
                        scans++;
                        if (entropyAt < 0) entropyAt = input.Position;
                        (aheadAt, ahead) = await SkipEntropy(input, restart, token).ConfigureAwait(false);
                        break;
                    default:
                        if (IsFrame(marker))
                        {
                            frame = ReadFrame(marker, payload, count, start);
                            frames++;
                        }
                        break;
                }
            }
        }
        finally { ArrayPool<byte>.Shared.Return(payload); }
    }

    /// <summary>
    /// Frame markers: the SOF codes of every process, minus DHT (0xC4), the reserved 0xC8 and DAC
    /// (0xCC). 0xC0-0xC3 are sequential and lossless Huffman, 0xC5-0xC7 differential, 0xC9-0xCF the
    /// arithmetic family (T.81 Table 1).
    /// </summary>
    private static bool IsFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF;

    private static bool HasLength(byte marker) =>
        IsFrame(marker) || marker is 0xC4 or 0xCC or 0xDB or 0xDC or 0xDD or 0xDA or 0xFE or >= 0xE0 and <= 0xEF;

    private static bool HasExifSignature(byte[] data, int count) => count >= 6 &&
        data[0] == 69 && data[1] == 120 && data[2] == 105 && data[3] == 102 && data[4] == 0 && data[5] == 0;

    private sealed record Frame(byte[] Ids, byte[] Quantizers, int Samples, bool HeightZero, bool Lossless, bool Arithmetic);

    private static Frame ReadFrame(byte marker, byte[] data, int count, long at)
    {
        if (count < 6) throw Error(MetadataErrorCode.InvalidJpeg, "Truncated frame header.", at);
        int precision = data[0], components = data[5];
        if (components < 1 || count != 6 + 3 * components) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid frame length.", at);
        // Precision is not cross-checked against the process mode: this reader never decodes, and a
        // value a real camera wrote must not cost the file its metadata. Only the standard's own
        // range is enforced.
        if (precision is < 2 or > 16) throw Error(MetadataErrorCode.InvalidJpeg, "Frame precision is outside 2..16.", at);
        ushort height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1)), width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(3));
        if (width == 0) throw Error(MetadataErrorCode.InvalidJpeg, "A frame width of zero is not expressible.", at);
        byte[] ids = new byte[components];
        byte[] selectors = new byte[components];
        int samples = 0;
        for (int i = 0; i < components; i++)
        {
            int p = 6 + i * 3;
            ids[i] = data[p]; selectors[i] = data[p + 2];
            int h = data[p + 1] >> 4, v = data[p + 1] & 15;
            if (ids.Take(i).Contains(ids[i]) || h is < 1 or > 4 || v is < 1 or > 4 || selectors[i] > 3)
                throw Error(MetadataErrorCode.InvalidJpeg, "Invalid frame component parameters.", at);
            samples += h * v;
        }
        // Lossless and differential-lossless processes carry the predictor selector in Ss instead of
        // a spectral start, and arithmetic processes take their tables from DAC (T.81 8.3, A.2).
        return new(ids, selectors, samples, height == 0, marker is 0xC3 or 0xC7 or 0xCB or 0xCF,
            marker >= 0xC9);
    }

    private static void ReadScan(byte[] data, int count, Frame frame, bool[] quant, bool[,] huffman, long at)
    {
        if (count < 1 || count != 4 + 2 * data[0]) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid SOS length.", at);
        int components = data[0];
        int start = data[count - 3], end = data[count - 2], approximating = data[count - 1] >> 4;
        if (frame.Lossless)
        {
            // Ss names the predictor (1..7) and Se is zero; successive prediction uses Ah (0..2).
            if (start is < 1 or > 7 || end != 0 || approximating > 2)
                throw Error(MetadataErrorCode.InvalidJpeg, "Invalid lossless predictor parameters.", at);
        }
        else
        {
            if (start > end || end > 63 || approximating > 3)
                throw Error(MetadataErrorCode.InvalidJpeg, "Invalid spectral or approximation parameters.", at);
            if (components > 1 && (start != 0 || end != 63))
                throw Error(MetadataErrorCode.InvalidJpeg, "An interleaved scan must cover every coefficient.", at);
        }
        for (int i = 0; i < components; i++)
        {
            int id = Array.IndexOf(frame.Ids, data[1 + 2 * i]);
            if (id < 0) throw Error(MetadataErrorCode.InvalidJpeg, "A scan names a component the frame does not define.", at);
            int selector = data[2 + 2 * i], dc = selector >> 4, ac = selector & 15;
            if (dc > 3 || ac > 3) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid scan table selector.", at);
            // Lossless processes carry no quantization table at all, so the reference is only a
            // requirement for the lossy ones.
            if (!frame.Lossless && !quant[frame.Quantizers[id]])
                throw Error(MetadataErrorCode.InvalidJpeg, "A referenced quantization table is missing.", at);
            // Arithmetic processes take their table definitions from DAC, not DHT (T.81 A.2), and a
            // band-restricted scan only uses the tables its bands actually cover.
            bool huff = !frame.Arithmetic;
            if (huff && start == 0 && !huffman[0, dc])
                throw Error(MetadataErrorCode.InvalidJpeg, "A referenced DC table is missing.", at);
            if (huff && end > 0 && !huffman[1, ac])
                throw Error(MetadataErrorCode.InvalidJpeg, "A referenced AC table is missing.", at);
        }
        if (components > 1 && frame.Samples > 10) throw Error(MetadataErrorCode.InvalidJpeg, "The interleaved sampling budget exceeds ten.", at);
    }

    private static void ReadQuant(byte[] data, int count, bool[] tables, long at)
    {
        if (count == 0) throw Error(MetadataErrorCode.InvalidJpeg, "Empty DQT segment.", at);
        int p = 0;
        while (p < count)
        {
            int selector = data[p++];
            int precision = selector >> 4, id = selector & 15;
            int width = precision switch { 0 => 1, 1 => 2, 2 => 4, _ => -1 };
            if (width < 0) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid quantization table precision.", at);
            if (id > 3 || count - p < 64 * width) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid quantization table.", at);
            for (int i = 0; i < 64; i++)
            {
                long element = width switch
                {
                    1 => (long)data[p + i],
                    2 => (long)BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p + i * 2)),
                    _ => (long)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p + i * 4)),
                };
                if (element == 0) throw Error(MetadataErrorCode.InvalidJpeg, "Zero quantization element.", at);
            }
            p += 64 * width; tables[id] = true;
        }
    }

    private static void ReadHuffman(byte[] data, int count, bool[,] tables, long at)
    {
        if (count == 0) throw Error(MetadataErrorCode.InvalidJpeg, "Empty DHT segment.", at);
        int p = 0;
        while (p < count)
        {
            if (count - p < 17) throw Error(MetadataErrorCode.InvalidJpeg, "Truncated Huffman table.", at);
            int selector = data[p++], kind = selector >> 4, id = selector & 15;
            if (kind > 1 || id > 3) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid Huffman table selector.", at);
            int symbols = 0, freeCodes = 1;
            for (int i = 0; i < 16; i++) { int codes = data[p++]; symbols += codes; freeCodes = freeCodes * 2 - codes; if (freeCodes < 0) throw Error(MetadataErrorCode.InvalidJpeg, "Oversubscribed Huffman code lengths.", at); }
            if (symbols is < 1 or > 256 || symbols > count - p || freeCodes == 0)
                throw Error(MetadataErrorCode.InvalidJpeg, "Invalid Huffman symbol count or reserved all-ones code.", at);
            p += symbols; tables[kind, id] = true;
        }
    }

    /// <summary>Walks entropy-coded data without decoding it, and stops at the marker that ends it.</summary>
    private static async Task<(long At, int Marker)> SkipEntropy(BufferedInput input, ushort interval, CancellationToken token)
    {
        int expectedRestart = 0;
        bool sawData = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (input.SkipPlainBytes() > 0) sawData = true;
            long at = input.Position;
            byte b = await input.Byte().ConfigureAwait(false);
            if (b != 0xFF) { sawData = true; continue; }
            int fill = 0;
            do { b = await input.Byte().ConfigureAwait(false); fill++; } while (b == 0xFF);
            if (b == 0)
            {
                if (fill != 1) throw Error(MetadataErrorCode.InvalidJpeg, "Fill bytes cannot precede a stuffed zero.", at);
                sawData = true; continue;
            }
            if (b is >= 0xD0 and <= 0xD7)
            {
                if (interval == 0 || b != 0xD0 + expectedRestart) throw Error(MetadataErrorCode.InvalidJpeg, "Unexpected restart marker or sequence.", at);
                expectedRestart = (expectedRestart + 1) & 7;
                continue;
            }
            if (!sawData) throw Error(MetadataErrorCode.InvalidJpeg, "The scan contains no encoded bytes.", at);
            return (at, b);
        }
    }

    private static MetadataException Error(MetadataErrorCode code, string message, long at) => new(code, message, at);

    private sealed class BufferedInput : IDisposable
    {
        private readonly Stream source;
        private readonly bool asynchronous;
        private readonly CancellationToken token;
        private readonly byte[] buffer;
        private readonly int capacity;
        private int current, end;
        internal long Position { get; private set; }
        internal BufferedInput(Stream source, int capacity, bool asynchronous, CancellationToken token)
        { this.source = source; this.capacity = capacity; this.asynchronous = asynchronous; this.token = token; buffer = ArrayPool<byte>.Shared.Rent(capacity); }
        internal ValueTask<byte> Byte()
        {
            token.ThrowIfCancellationRequested();
            if (current < end) { Position++; return new(buffer[current++]); }
            return SlowByte();
        }
        private async ValueTask<byte> SlowByte()
        {
            await Fill().ConfigureAwait(false);
            Position++; return buffer[current++];
        }
        private async ValueTask Fill()
        {
            token.ThrowIfCancellationRequested();
            current = 0;
            end = asynchronous ? await source.ReadAsync(buffer.AsMemory(0, capacity), token).ConfigureAwait(false) : source.Read(buffer, 0, capacity);
            if (end == 0) throw Error(MetadataErrorCode.InvalidJpeg, "Unexpected end of JPEG input.", Position);
        }
        internal async ValueTask Block(byte[] output, int count)
        {
            int written = 0;
            while (written < count)
            {
                token.ThrowIfCancellationRequested();
                if (current == end) await Fill().ConfigureAwait(false);
                int n = Math.Min(count - written, end - current);
                buffer.AsMemory(current, n).CopyTo(output.AsMemory(written));
                current += n; Position += n; written += n;
            }
        }
        internal int SkipPlainBytes()
        {
            int n = buffer.AsSpan(current, end - current).IndexOf((byte)0xFF);
            if (n < 0) n = end - current;
            current += n; Position += n; return n;
        }
        public void Dispose() => ArrayPool<byte>.Shared.Return(buffer);
    }
}
