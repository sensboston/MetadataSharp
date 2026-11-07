using System.Buffers;
using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Jpeg;

/// <summary>Bounded full-file structural scan of the approved single-scan baseline subset.</summary>
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
            long insertion = 2;
            bool leadingApp0 = true;
            Frame? frame = null;
            bool[] quant = new bool[4];
            bool[,] huffman = new bool[2, 2];
            ushort restart = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                long start = input.Position;
                if (await input.Byte().ConfigureAwait(false) != 0xFF) throw Error(MetadataErrorCode.InvalidJpeg, "Expected a JPEG marker.", start);
                byte marker;
                do { marker = await input.Byte().ConfigureAwait(false); } while (marker == 0xFF);
                if (marker is 0 or 0xD8 or 0xD9 or >= 0xD0 and <= 0xD7)
                    throw Error(MetadataErrorCode.InvalidJpeg, "Unexpected standalone marker outside scan data.", start);
                if (!(marker is 0xC0 or 0xC4 or 0xDA or 0xDB or 0xDD or 0xFE or >= 0xE0 and <= 0xEF))
                    throw Error(MetadataErrorCode.UnsupportedFeature, "The marker is outside the single-scan baseline subset.", start);
                if (segments.Count >= limits.MaxSegments) throw Error(MetadataErrorCode.LimitExceeded, "The segment budget was exceeded.", start);
                int segmentLength = (await input.Byte().ConfigureAwait(false) << 8) | await input.Byte().ConfigureAwait(false);
                if (segmentLength < 2) throw Error(MetadataErrorCode.InvalidJpeg, "A segment length is less than two.", start);
                int count = segmentLength - 2;
                long payloadAt = input.Position;
                await input.Block(payload, count).ConfigureAwait(false);
                bool isExif = marker == 0xE1 && HasExifSignature(payload, count);
                var segment = new JpegSegment(marker, start, input.Position - start, payloadAt, count, isExif);
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
                    case 0xC0:
                        if (frame is not null) throw Error(MetadataErrorCode.UnsupportedFeature, "Multiple frames are not supported.", start);
                        frame = ReadFrame(payload, count, start);
                        break;
                    case 0xDB: ReadQuant(payload, count, quant, start); break;
                    case 0xC4: ReadHuffman(payload, count, huffman, start); break;
                    case 0xDD:
                        if (count != 2) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid DRI length.", start);
                        restart = BinaryPrimitives.ReadUInt16BigEndian(payload);
                        break;
                    case 0xDA:
                        if (frame is null) throw Error(MetadataErrorCode.InvalidJpeg, "SOS precedes the frame header.", start);
                        ReadScan(payload, count, frame, quant, huffman, start);
                        long entropy = input.Position;
                        long eoi = await ScanEntropy(input, restart, token).ConfigureAwait(false);
                        if (input.Position != length) throw Error(MetadataErrorCode.UnsupportedFeature, "Trailing bytes after EOI are not supported.", input.Position);
                        return new(length, segments.AsReadOnly(), exif, insertion, start, entropy, eoi);
                }
            }
        }
        finally { ArrayPool<byte>.Shared.Return(payload); }
    }

    private static bool HasExifSignature(byte[] data, int count) => count >= 6 &&
        data[0] == 69 && data[1] == 120 && data[2] == 105 && data[3] == 102 && data[4] == 0 && data[5] == 0;

    private sealed record Frame(byte[] Ids, byte[] Quantizers, int Samples);

    private static Frame ReadFrame(byte[] data, int count, long at)
    {
        if (count < 6) throw Error(MetadataErrorCode.InvalidJpeg, "Truncated frame header.", at);
        int components = data[5];
        if (count != 6 + 3 * components) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid frame length.", at);
        if (data[0] != 8 || components is not 1 and not 3)
            throw Error(MetadataErrorCode.UnsupportedFeature, "Only 8-bit one/three-component frames are supported.", at);
        if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1)) == 0 || BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(3)) == 0)
            throw Error(MetadataErrorCode.UnsupportedFeature, "Zero dimensions or DNL are not supported.", at);
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
        return new(ids, selectors, samples);
    }

    private static void ReadScan(byte[] data, int count, Frame frame, bool[] quant, bool[,] huffman, long at)
    {
        if (count < 1 || count != 4 + 2 * data[0]) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid SOS length.", at);
        int components = data[0];
        if (components != frame.Ids.Length) throw Error(MetadataErrorCode.UnsupportedFeature, "All components must occur in the sole scan.", at);
        if (components > 1 && frame.Samples > 10) throw Error(MetadataErrorCode.InvalidJpeg, "The interleaved sampling budget exceeds ten.", at);
        for (int i = 0; i < components; i++)
        {
            int selector = data[2 + 2 * i];
            int dc = selector >> 4, ac = selector & 15;
            if (data[1 + 2 * i] != frame.Ids[i] || dc > 1 || ac > 1)
                throw Error(MetadataErrorCode.InvalidJpeg, "Invalid baseline scan component/table selector.", at);
            if (!quant[frame.Quantizers[i]] || !huffman[0, dc] || !huffman[1, ac])
                throw Error(MetadataErrorCode.InvalidJpeg, "A referenced coding table is missing.", at);
        }
        if (data[count - 3] != 0 || data[count - 2] != 63 || data[count - 1] != 0)
            throw Error(MetadataErrorCode.UnsupportedFeature, "Non-baseline spectral/approximation parameters are not supported.", at);
    }

    private static void ReadQuant(byte[] data, int count, bool[] tables, long at)
    {
        if (count == 0) throw Error(MetadataErrorCode.InvalidJpeg, "Empty DQT segment.", at);
        int p = 0;
        while (p < count)
        {
            int selector = data[p++];
            if ((selector >> 4) != 0) throw Error(MetadataErrorCode.UnsupportedFeature, "Only 8-bit quantization tables are supported.", at);
            int id = selector & 15;
            if (id > 3 || count - p < 64) throw Error(MetadataErrorCode.InvalidJpeg, "Invalid quantization table.", at);
            for (int i = 0; i < 64; i++) if (data[p + i] == 0) throw Error(MetadataErrorCode.InvalidJpeg, "Zero quantization element.", at);
            p += 64; tables[id] = true;
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
            if (kind > 1 || id > 1) throw Error(MetadataErrorCode.UnsupportedFeature, "Unsupported baseline Huffman table selector.", at);
            int symbols = 0, freeCodes = 1;
            for (int i = 0; i < 16; i++) { int codes = data[p++]; symbols += codes; freeCodes = freeCodes * 2 - codes; if (freeCodes < 0) throw Error(MetadataErrorCode.InvalidJpeg, "Oversubscribed Huffman code lengths.", at); }
            if (symbols is < 1 or > 256 || symbols > count - p || freeCodes == 0)
                throw Error(MetadataErrorCode.InvalidJpeg, "Invalid Huffman symbol count or reserved all-ones code.", at);
            p += symbols; tables[kind, id] = true;
        }
    }

    private static async Task<long> ScanEntropy(BufferedInput input, ushort interval, CancellationToken token)
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
            if (b == 0xD9)
            {
                if (!sawData) throw Error(MetadataErrorCode.InvalidJpeg, "The scan contains no encoded bytes.", at);
                return at;
            }
            throw Error(MetadataErrorCode.UnsupportedFeature, "Additional scans or post-scan markers are not supported.", at);
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
