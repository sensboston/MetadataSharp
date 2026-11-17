namespace MetadataSharp.Harness.Python;

// zlib.error: the message Python prints, "Error N while decompressing data: reason".
internal sealed class ZlibError(string message) : Exception(message);

// zlib.decompress: a zlib stream (RFC 1950) inflated as the reference library inflates it, with its checks
// and its errors: the header check, the window size, a preset dictionary, the Adler-32 trailer and a
// truncated stream all fail; bytes after the end of the stream are ignored.
internal static class Inflate
{
    private static readonly int[] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly int[] LengthExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly int[] DistanceBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static readonly int[] DistanceExtra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
    private static readonly int[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        var reader = new BitReader(data.ToArray());
        int cmf = reader.Byte(), flg = reader.Byte();
        if ((cmf * 256 + flg) % 31 != 0) throw Data("incorrect header check");
        if ((cmf & 0x0F) != 8) throw Data("unknown compression method");
        if ((cmf >> 4) + 8 > 15) throw Data("invalid window size");
        if ((flg & 0x20) != 0)
        {
            reader.Bits(32);
            throw new ZlibError("Error 2 while decompressing data");
        }
        var output = new List<byte>();
        bool last;
        do
        {
            last = reader.Bits(1) == 1;
            switch (reader.Bits(2))
            {
                case 0:
                    reader.AlignToByte();
                    int length = reader.Bits(16), complement = reader.Bits(16);
                    if (length != (~complement & 0xFFFF)) throw Data("invalid stored block lengths");
                    for (int i = 0; i < length; i++) output.Add((byte)reader.Bits(8));
                    break;
                case 1:
                    Codes(reader, output, Fixed.Literals, Fixed.Distances);
                    break;
                case 2:
                    var (literals, distances) = Dynamic(reader);
                    Codes(reader, output, literals, distances);
                    break;
                default:
                    throw Data("invalid block type");
            }
        } while (!last);
        reader.AlignToByte();
        uint expected = (uint)reader.Bits(8) << 24;
        expected |= (uint)reader.Bits(8) << 16;
        expected |= (uint)reader.Bits(8) << 8;
        expected |= (uint)reader.Bits(8);
        byte[] result = output.ToArray();
        if (Zlib.Adler32(result) != expected) throw Data("incorrect data check");
        return result;
    }

    private static ZlibError Data(string reason) => new($"Error -3 while decompressing data: {reason}");

    private static void Codes(BitReader reader, List<byte> output, Huffman literals, Huffman distances)
    {
        while (true)
        {
            int symbol = literals.Decode(reader, "invalid literal/length code");
            if (symbol < 256)
            {
                output.Add((byte)symbol);
                continue;
            }
            if (symbol == 256) return;
            symbol -= 257;
            if (symbol >= 29) throw Data("invalid literal/length code");
            int length = LengthBase[symbol] + reader.Bits(LengthExtra[symbol]);
            int code = distances.Decode(reader, "invalid distance code");
            if (code >= 30) throw Data("invalid distance code");
            int distance = DistanceBase[code] + reader.Bits(DistanceExtra[code]);
            if (distance > output.Count) throw Data("invalid distance too far back");
            int from = output.Count - distance;
            for (int i = 0; i < length; i++) output.Add(output[from + i]);
        }
    }

    private static (Huffman Literals, Huffman Distances) Dynamic(BitReader reader)
    {
        int literalCount = reader.Bits(5) + 257, distanceCount = reader.Bits(5) + 1, codeLengthCount = reader.Bits(4) + 4;
        if (literalCount > 286 || distanceCount > 30) throw Data("too many length or distance symbols");
        var codeLengths = new int[19];
        for (int i = 0; i < codeLengthCount; i++) codeLengths[CodeLengthOrder[i]] = reader.Bits(3);
        var codeLengthCode = Huffman.Build(codeLengths, complete: true) ?? throw Data("invalid code lengths set");
        var lengths = new int[literalCount + distanceCount];
        int index = 0;
        while (index < lengths.Length)
        {
            int symbol = codeLengthCode.Decode(reader, "invalid code lengths set");
            if (symbol < 16)
            {
                lengths[index++] = symbol;
                continue;
            }
            int repeat, value = 0;
            if (symbol == 16)
            {
                if (index == 0) throw Data("invalid bit length repeat");
                value = lengths[index - 1];
                repeat = 3 + reader.Bits(2);
            }
            else if (symbol == 17) repeat = 3 + reader.Bits(3);
            else repeat = 11 + reader.Bits(7);
            if (index + repeat > lengths.Length) throw Data("invalid bit length repeat");
            for (int i = 0; i < repeat; i++) lengths[index++] = value;
        }
        if (lengths[256] == 0) throw Data("invalid code -- missing end-of-block");
        var literals = Huffman.Build(lengths[..literalCount], complete: false) ?? throw Data("invalid literal/lengths set");
        var distances = Huffman.Build(lengths[literalCount..], complete: false) ?? throw Data("invalid distances set");
        return (literals, distances);
    }

    // The fixed codes of block type 1.
    private static class Fixed
    {
        public static readonly Huffman Literals = Huffman.Build(Enumerable.Range(0, 288).Select(n => n < 144 ? 8 : n < 256 ? 9 : n < 280 ? 7 : 8).ToArray(), complete: false)!;
        public static readonly Huffman Distances = Huffman.Build(Enumerable.Repeat(5, 32).ToArray(), complete: false)!;
    }

    // A canonical Huffman code, decoded bit by bit.
    private sealed class Huffman
    {
        private readonly int[] counts = new int[16];
        private readonly int[] symbols;
        private readonly int max;
        private readonly bool codeLengths;

        private Huffman(int[] lengths, int max, bool codeLengths)
        {
            this.max = max;
            this.codeLengths = codeLengths;
            symbols = new int[lengths.Length];
            foreach (int length in lengths) counts[length]++;
            counts[0] = 0;
            var offsets = new int[16];
            for (int length = 1; length < 16; length++) offsets[length] = offsets[length - 1] + counts[length - 1];
            for (int symbol = 0; symbol < lengths.Length; symbol++)
                if (lengths[symbol] != 0) symbols[offsets[lengths[symbol]]++] = symbol;
        }

        // The reference refuses an over-subscribed code, and an incomplete one unless it is a literal or
        // distance code of a single one-bit code. A code without symbols is accepted: decoding from it
        // consumes one bit and fails, except for the code-length code, where it reads a length of zero.
        public static Huffman? Build(int[] lengths, bool complete)
        {
            var counts = new int[16];
            foreach (int length in lengths) counts[length]++;
            int max = 15;
            while (max >= 1 && counts[max] == 0) max--;
            if (max == 0) return new Huffman(lengths, 0, complete);
            int left = 1;
            for (int length = 1; length <= 15; length++)
            {
                left <<= 1;
                left -= counts[length];
                if (left < 0) return null;
            }
            if (left > 0 && (complete || max != 1)) return null;
            return new Huffman(lengths, max, complete);
        }

        public int Decode(BitReader reader, string invalid)
        {
            if (max == 0)
            {
                reader.Bits(1);
                return codeLengths ? 0 : throw Data(invalid);
            }
            int code = 0, first = 0, index = 0;
            for (int length = 1; length <= max; length++)
            {
                code |= reader.Bits(1);
                int count = counts[length];
                if (code - count < first) return symbols[index + (code - first)];
                index += count;
                first += count;
                first <<= 1;
                code <<= 1;
            }
            throw Data(invalid);
        }
    }

    // Least-significant-bit-first reading; running out of input is a truncated stream.
    private sealed class BitReader(byte[] data)
    {
        private int position;
        private int buffer;
        private int available;

        public int Byte()
        {
            AlignToByte();
            return Bits(8);
        }

        public int Bits(int count)
        {
            long value = 0;
            for (int i = 0; i < count; i++)
            {
                if (available == 0)
                {
                    if (position >= data.Length) throw new ZlibError("Error -5 while decompressing data: incomplete or truncated stream");
                    buffer = data[position++];
                    available = 8;
                }
                value |= (long)(buffer & 1) << i;
                buffer >>= 1;
                available--;
            }
            return (int)value;
        }

        public void AlignToByte()
        {
            buffer = 0;
            available = 0;
        }
    }
}
