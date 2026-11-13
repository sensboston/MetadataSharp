using System.Buffers.Binary;
using System.IO.Compression;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Png;

/// <summary>Validates exact RFC1950/1951 framing before bounded framework decompression.</summary>
internal static class BoundedZlib
{
    private static readonly int[] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly Huffman FixedLiteral = BuildFixedLiteral();
    private static readonly Huffman FixedDistance = new(Enumerable.Repeat(5, 32).ToArray());
    // Keep BuildFixedLiteral focused on PNG and its format-specific validation contract.
    private static Huffman BuildFixedLiteral()
    {
        int[] lengths = new int[288];
        for (int i = 0; i < lengths.Length; i++) lengths[i] = i < 144 ? 8 : i < 256 ? 9 : i < 280 ? 7 : 8;
        return new(lengths);
    }
    // Convert budget exhaustion into the format-specific LimitExceeded error.
    internal static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "Expanded data exceeds its budget.");
    private static MetadataException Bad() => PngScanner.Invalid("Invalid or truncated zlib stream.");
    internal static byte[] Expand(byte[] input, int maximum, CancellationToken token)
    {
        if (input.Length < 8 || (input[0] & 15) != 8 || (input[0] >> 4) > 7 || ((input[0] << 8) | input[1]) % 31 != 0) throw Bad();
        if ((input[1] & 32) != 0) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Preset zlib dictionaries are unsupported.");
        int window = 1 << ((input[0] >> 4) + 8);
        var bits = new Bits(input, 2, input.Length - 4); int produced = 0; bool final;
        do
        {
            token.ThrowIfCancellationRequested(); final = bits.Read(1) != 0; int type = bits.Read(2);
            if (type == 0)
            {
                bits.Align(); int length = bits.Read(16), inverse = bits.Read(16);
                if ((length ^ inverse) != 65535) throw Bad();
                if (length > maximum - produced) throw Limit(); produced += length; bits.SkipBytes(length); continue;
            }
            if (type == 3) throw Bad();
            Huffman literal, distance;
            if (type == 1)
            {
                literal = FixedLiteral; distance = FixedDistance;
            }
            else
            {
                int nl = bits.Read(5) + 257, nd = bits.Read(5) + 1, nc = bits.Read(4) + 4;
                if (nl > 286 || nd > 32) throw Bad();
                // RFC1951 code-length permutation (19 symbols).
                int[] order = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
                int[] codeLengths = new int[19]; for (int i = 0; i < nc; i++) codeLengths[order[i]] = bits.Read(3);
                var codes = new Huffman(codeLengths); int[] combined = new int[nl + nd]; int at = 0;
                while (at < combined.Length)
                {
                    int symbol = codes.Decode(bits);
                    if (symbol < 16) { combined[at++] = symbol; continue; }
                    int repeat, value = 0;
                    if (symbol == 16) { if (at == 0) throw Bad(); repeat = bits.Read(2) + 3; value = combined[at - 1]; }
                    else if (symbol == 17) repeat = bits.Read(3) + 3;
                    else if (symbol == 18) repeat = bits.Read(7) + 11;
                    else throw Bad();
                    if (repeat > combined.Length - at) throw Bad();
                    Array.Fill(combined, value, at, repeat); at += repeat;
                }
                if (combined[256] == 0) throw Bad();
                literal = new(combined[..nl]); distance = new(combined[nl..]);
            }
            while (true)
            {
                token.ThrowIfCancellationRequested(); int symbol = literal.Decode(bits);
                if (symbol == 256) break;
                if (symbol < 256) { if (produced == maximum) throw Limit(); produced++; continue; }
                if (symbol > 285) throw Bad();
                int index = symbol - 257;
                int extra = index < 8 || index == 28 ? 0 : (index - 4) / 4;
                int length = LengthBase[index] + bits.Read(extra); int ds = distance.Decode(bits);
                if (ds >= 30) throw Bad();
                int de = ds < 4 ? 0 : ds / 2 - 1;
                int back = ds < 4 ? ds + 1 : ((2 + (ds & 1)) << de) + 1 + bits.Read(de);
                if (back > produced || back > window) throw Bad();
                if (length > maximum - produced) throw Limit(); produced += length;
            }
        } while (!final);
        if (bits.EndByte != input.Length - 4) throw Bad();
        try
        {
            using var compressed = new MemoryStream(input, false); using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            byte[] output = new byte[produced]; int at = 0;
            while (at < output.Length)
            {
                token.ThrowIfCancellationRequested(); int count = zlib.Read(output, at, Math.Min(8192, output.Length - at));
                if (count == 0) throw Bad(); at += count;
            }
            if (zlib.ReadByte() != -1) throw Bad();
            uint a = 1, b = 0;
            for (int i = 0; i < output.Length; i++)
            { if ((i & 8191) == 0) token.ThrowIfCancellationRequested(); a = (a + output[i]) % 65521; b = (b + a) % 65521; }
            if (((b << 16) | a) != BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(input.Length - 4))) throw Bad();
            token.ThrowIfCancellationRequested(); return output;
        }
        catch (InvalidDataException) { throw Bad(); }
    }
    // Represent the Bits state used by PNG parsing and output.
    private sealed class Bits(byte[] data, int start, int end)
    {
        private long position = (long)start * 8;
        internal int EndByte => (int)((position + 7) / 8);
        // Read PNG data only after the input extent and applicable resource budgets are checked.
        internal int Read(int count)
        {
            if (position + count > (long)end * 8) throw Bad(); int value = 0;
            for (int n = 0; n < count; n++, position++) value |= ((data[(int)(position / 8)] >> (int)(position & 7)) & 1) << n;
            return value;
        }
        internal void Align() => position = (position + 7) & ~7L;
        // Keep SkipBytes focused on PNG and its format-specific validation contract.
        internal void SkipBytes(int count) { if (position + count * 8L > end * 8L) throw Bad(); position += count * 8L; }
    }
    // Represent the Huffman state used by PNG parsing and output.
    private sealed class Huffman
    {
        private readonly Dictionary<int, int> symbols = [];
        // Capture validated inputs and stable state for this PNG object.
        internal Huffman(int[] lengths)
        {
            int[] counts = new int[16], next = new int[16];
            foreach (int length in lengths) { if (length is < 0 or > 15) throw Bad(); if (length != 0) counts[length]++; }
            int code = 0, available = 1;
            for (int length = 1; length <= 15; length++)
            {
                available = available * 2 - counts[length]; if (available < 0) throw Bad();
                code = (code + counts[length - 1]) << 1; next[length] = code;
            }
            for (int symbol = 0; symbol < lengths.Length; symbol++)
            {
                int length = lengths[symbol]; if (length == 0) continue;
                int canonical = next[length]++, reversed = 0;
                for (int i = 0; i < length; i++) { reversed = (reversed << 1) | (canonical & 1); canonical >>= 1; }
                symbols.Add((1 << length) | reversed, symbol);
            }
        }
        // Keep Decode focused on PNG and its format-specific validation contract.
        internal int Decode(Bits bits)
        {
            int code = 0;
            for (int length = 1; length <= 15; length++) { code |= bits.Read(1) << (length - 1); if (symbols.TryGetValue((1 << length) | code, out int symbol)) return symbol; }
            throw Bad();
        }
    }
}
