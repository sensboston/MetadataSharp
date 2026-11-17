using System.Buffers.Binary;

namespace MetadataSharp.Harness.Python;

// The zlib stream the reference harness writes into its owned PNG fixtures and into every compressed
// annotation. The fixtures are pinned by SHA-256 (tests/fixtures/png/manifest.json), so the deflate
// stream has to be the one zlib writes, bit for bit: the runtime's own deflater (zlib-ng since .NET 9)
// chooses other matches and blocks. This is zlib's own algorithm (deflate_slow, levels 4 to 9, the
// default strategy, a 32 KiB window and memory level 8: what zlib.compress uses), written from the
// published format and the reference implementation's documented behaviour. Decompression is in
// Inflate.cs, with the reference's errors.
internal static class Zlib
{
    private const int MinMatch = 3, MaxMatch = 258, MinLookahead = MaxMatch + MinMatch + 1, TooFar = 4096;
    private const int WBits = 15, WSize = 1 << WBits, WMask = WSize - 1, MaxDist = WSize - MinLookahead;
    private const int HashBits = 8 + 7, HashSize = 1 << HashBits, HashMask = HashSize - 1, HashShift = (HashBits + MinMatch - 1) / MinMatch;
    private const int LitBufSize = 1 << (8 + 6), SymEnd = (LitBufSize - 1) * 3, WindowSize = 2 * WSize, WinInit = MaxMatch;
    private const int MaxBits = 15, MaxBlBits = 7, EndBlock = 256, Rep3To6 = 16, RepZ3To10 = 17, RepZ11To138 = 18;
    private const int LengthCodes = 29, Literals = 256, LCodes = Literals + 1 + LengthCodes, DCodes = 30, BlCodes = 19;
    private const int HeapSize = 2 * LCodes + 1;

    // good_length, max_lazy, nice_length, max_chain for each level that uses lazy matching.
    private static readonly int[,] Configuration =
    {
        { 4, 4, 16, 16 }, { 8, 16, 32, 32 }, { 8, 16, 128, 128 }, { 8, 32, 128, 256 }, { 32, 128, 258, 1024 }, { 32, 258, 258, 4096 },
    };

    private static readonly int[] ExtraLBits = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly int[] ExtraDBits = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
    private static readonly int[] ExtraBlBits = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 7];
    private static readonly int[] BlOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    private static readonly int[] BaseLength = new int[LengthCodes];
    private static readonly byte[] LengthCode = new byte[MaxMatch - MinMatch + 1];
    private static readonly int[] BaseDist = new int[DCodes];
    private static readonly byte[] DistCode = new byte[512];
    private static readonly int[] StaticLTreeCode = new int[LCodes + 2], StaticLTreeLen = new int[LCodes + 2];
    private static readonly int[] StaticDTreeCode = new int[DCodes], StaticDTreeLen = new int[DCodes];
    private static readonly uint[] CrcTable = BuildCrcTable();

    // Build the fixed length, distance and code tables once, as the reference does on first use.
    static Zlib()
    {
        int length = 0, code;
        for (code = 0; code < LengthCodes - 1; code++)
        {
            BaseLength[code] = length;
            for (int n = 0; n < 1 << ExtraLBits[code]; n++) LengthCode[length++] = (byte)code;
        }
        LengthCode[length - 1] = (byte)code;
        int dist = 0;
        for (code = 0; code < 16; code++)
        {
            BaseDist[code] = dist;
            for (int n = 0; n < 1 << ExtraDBits[code]; n++) DistCode[dist++] = (byte)code;
        }
        dist >>= 7;
        for (; code < DCodes; code++)
        {
            BaseDist[code] = dist << 7;
            for (int n = 0; n < 1 << (ExtraDBits[code] - 7); n++) DistCode[256 + dist++] = (byte)code;
        }
        var count = new int[MaxBits + 1];
        int index = 0;
        while (index <= 143) { StaticLTreeLen[index++] = 8; count[8]++; }
        while (index <= 255) { StaticLTreeLen[index++] = 9; count[9]++; }
        while (index <= 279) { StaticLTreeLen[index++] = 7; count[7]++; }
        while (index <= 287) { StaticLTreeLen[index++] = 8; count[8]++; }
        GenerateCodes(StaticLTreeLen, StaticLTreeCode, LCodes + 1, count);
        for (int n = 0; n < DCodes; n++)
        {
            StaticDTreeLen[n] = 5;
            StaticDTreeCode[n] = Reverse(n, 5);
        }
    }

    /// <summary>zlib.compress(data, level): a zlib stream (RFC 1950) with the reference's deflate output.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> data, int level = -1)
    {
        if (level == -1) level = 6;
        if (level < 4 || level > 9) throw new ArgumentOutOfRangeException(nameof(level), "only the lazy-matching levels 4 to 9 are implemented");
        var state = new Deflater(level, data);
        return state.Run();
    }

    /// <summary>zlib.decompress(data): the inflated bytes of a zlib stream.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> data) => Inflate.Decompress(data);

    /// <summary>zlib.crc32(data) as an unsigned value.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data, uint crc = 0)
    {
        crc ^= 0xFFFFFFFFu;
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>zlib.adler32(data).</summary>
    public static uint Adler32(ReadOnlySpan<byte> data, uint adler = 1)
    {
        uint a = adler & 0xFFFF, b = adler >> 16;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    // The standard reflected CRC-32 table (polynomial 0xEDB88320).
    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static int Reverse(int code, int length)
    {
        int result = 0;
        do
        {
            result |= code & 1;
            code >>= 1;
            result <<= 1;
        } while (--length > 0);
        return result >> 1;
    }

    // Assign the canonical Huffman codes of a tree from its code lengths.
    private static void GenerateCodes(int[] len, int[] code, int maxCode, int[] blCount)
    {
        var next = new int[MaxBits + 1];
        int value = 0;
        for (int bits = 1; bits <= MaxBits; bits++)
        {
            value = (value + blCount[bits - 1]) << 1;
            next[bits] = value;
        }
        for (int n = 0; n <= maxCode; n++)
        {
            int length = len[n];
            if (length == 0) continue;
            code[n] = Reverse(next[length]++, length);
        }
    }

    private static int DCode(int dist) => dist < 256 ? DistCode[dist] : DistCode[256 + (dist >> 7)];

    // One dynamic tree: frequency/code and parent/length share storage exactly as the reference's unions do.
    private sealed class Tree(int size, int elements, int[]? staticLen, int[] extraBits, int extraBase, int maxLength)
    {
        public readonly int[] Fc = new int[size];
        public readonly int[] Dl = new int[size];
        public readonly int Elements = elements;
        public readonly int[]? StaticLen = staticLen;
        public readonly int[] ExtraBits = extraBits;
        public readonly int ExtraBase = extraBase;
        public readonly int MaxLength = maxLength;
        public int MaxCode;
    }

    // The state of one compression: window, hash chains, symbol buffer, trees and the bit writer.
    private sealed class Deflater
    {
        private readonly byte[] window = new byte[WindowSize];
        private readonly int[] prev = new int[WSize];
        private readonly int[] head = new int[HashSize];
        private readonly byte[] symBuf = new byte[LitBufSize * 3];
        private readonly Tree lTree = new(HeapSize, LCodes, StaticLTreeLen, ExtraLBits, Literals + 1, MaxBits);
        private readonly Tree dTree = new(2 * DCodes + 1, DCodes, StaticDTreeLen, ExtraDBits, 0, MaxBits);
        private readonly Tree blTree = new(2 * BlCodes + 1, BlCodes, null, ExtraBlBits, 0, MaxBlBits);
        private readonly int[] blCount = new int[MaxBits + 1];
        private readonly int[] heap = new int[2 * LCodes + 1];
        private readonly int[] depth = new int[2 * LCodes + 1];
        private readonly MemoryStream output = new();
        private readonly byte[] input;
        private readonly int level, goodMatch, maxLazy, niceMatch, maxChain;
        private int inputAt, heapLen, heapMax, symNext;
        private long optLen, staticLen;
        private int strStart, blockStart, lookahead, insert, matchLength, prevLength, matchStart, prevMatch, insH;
        private bool matchAvailable;
        private long highWater;
        private ulong bitBuffer;
        private int bitCount;

        public Deflater(int level, ReadOnlySpan<byte> data)
        {
            this.level = level;
            input = data.ToArray();
            goodMatch = Configuration[level - 4, 0];
            maxLazy = Configuration[level - 4, 1];
            niceMatch = Configuration[level - 4, 2];
            maxChain = Configuration[level - 4, 3];
            matchLength = prevLength = MinMatch - 1;
            InitBlock();
        }

        public byte[] Run()
        {
            int header = (8 + ((WBits - 8) << 4)) << 8;
            int levelFlags = level < 6 ? 1 : level == 6 ? 2 : 3;
            header |= levelFlags << 6;
            header += 31 - header % 31;
            output.WriteByte((byte)(header >> 8));
            output.WriteByte((byte)header);
            DeflateSlow();
            Span<byte> trailer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(trailer, Adler32(input));
            output.Write(trailer);
            return output.ToArray();
        }

        private void InitBlock()
        {
            Array.Clear(lTree.Fc, 0, LCodes);
            Array.Clear(dTree.Fc, 0, DCodes);
            Array.Clear(blTree.Fc, 0, BlCodes);
            lTree.Fc[EndBlock] = 1;
            optLen = staticLen = 0;
            symNext = 0;
        }

        private void UpdateHash(int c) => insH = ((insH << HashShift) ^ c) & HashMask;

        private int InsertString(int str)
        {
            UpdateHash(window[str + MinMatch - 1]);
            int matchHead = prev[str & WMask] = head[insH];
            head[insH] = str;
            return matchHead;
        }

        private void FillWindow()
        {
            do
            {
                int more = WindowSize - lookahead - strStart;
                if (strStart >= WSize + MaxDist)
                {
                    Array.Copy(window, WSize, window, 0, WSize - more);
                    matchStart -= WSize;
                    strStart -= WSize;
                    blockStart -= WSize;
                    if (insert > strStart) insert = strStart;
                    SlideHash();
                    more += WSize;
                }
                if (inputAt == input.Length) break;
                int n = Math.Min(more, input.Length - inputAt);
                Array.Copy(input, inputAt, window, strStart + lookahead, n);
                inputAt += n;
                lookahead += n;
                if (lookahead + insert >= MinMatch)
                {
                    int str = strStart - insert;
                    insH = window[str];
                    UpdateHash(window[str + 1]);
                    while (insert != 0)
                    {
                        UpdateHash(window[str + MinMatch - 1]);
                        prev[str & WMask] = head[insH];
                        head[insH] = str;
                        str++;
                        insert--;
                        if (lookahead + insert < MinMatch) break;
                    }
                }
            } while (lookahead < MinLookahead && inputAt != input.Length);

            // Bytes past the input that a match may read are zero, as the reference guarantees.
            if (highWater < WindowSize)
            {
                long current = strStart + (long)lookahead;
                if (highWater < current)
                {
                    long init = Math.Min(WindowSize - current, WinInit);
                    Array.Clear(window, (int)current, (int)init);
                    highWater = current + init;
                }
                else if (highWater < current + WinInit)
                {
                    long init = Math.Min(current + WinInit - highWater, WindowSize - highWater);
                    Array.Clear(window, (int)highWater, (int)init);
                    highWater += init;
                }
            }
        }

        private void SlideHash()
        {
            for (int n = 0; n < HashSize; n++) head[n] = head[n] >= WSize ? head[n] - WSize : 0;
            for (int n = 0; n < WSize; n++) prev[n] = prev[n] >= WSize ? prev[n] - WSize : 0;
        }

        private int LongestMatch(int curMatch)
        {
            int chainLength = maxChain;
            int scan = strStart;
            int bestLen = prevLength;
            int nice = niceMatch;
            int limit = strStart > MaxDist ? strStart - MaxDist : 0;
            int strEnd = strStart + MaxMatch;
            byte scanEnd1 = window[scan + bestLen - 1];
            byte scanEnd = window[scan + bestLen];
            if (prevLength >= goodMatch) chainLength >>= 2;
            if (nice > lookahead) nice = lookahead;
            do
            {
                int match = curMatch;
                if (window[match + bestLen] != scanEnd || window[match + bestLen - 1] != scanEnd1
                    || window[match] != window[scan] || window[match + 1] != window[scan + 1]) continue;
                int s = scan + 2, m = match + 2;
                // The reference compares eight bytes per step and may look one byte past the longest match.
                do
                {
                } while (window[++s] == window[++m] && window[++s] == window[++m] && window[++s] == window[++m]
                         && window[++s] == window[++m] && window[++s] == window[++m] && window[++s] == window[++m]
                         && window[++s] == window[++m] && window[++s] == window[++m] && s < strEnd);
                int len = MaxMatch - (strEnd - s);
                if (len > bestLen)
                {
                    matchStart = curMatch;
                    bestLen = len;
                    if (len >= nice) break;
                    scanEnd1 = window[scan + bestLen - 1];
                    scanEnd = window[scan + bestLen];
                }
            } while ((curMatch = prev[curMatch & WMask]) > limit && --chainLength != 0);
            return bestLen <= lookahead ? bestLen : lookahead;
        }

        private bool TallyLiteral(byte c)
        {
            symBuf[symNext++] = 0;
            symBuf[symNext++] = 0;
            symBuf[symNext++] = c;
            lTree.Fc[c]++;
            return symNext == SymEnd;
        }

        private bool TallyDistance(int distance, int length)
        {
            symBuf[symNext++] = (byte)distance;
            symBuf[symNext++] = (byte)(distance >> 8);
            symBuf[symNext++] = (byte)length;
            distance--;
            lTree.Fc[LengthCode[length] + Literals + 1]++;
            dTree.Fc[DCode(distance)]++;
            return symNext == SymEnd;
        }

        private void FlushBlock(bool last)
        {
            FlushBlockCore(blockStart >= 0 ? blockStart : -1, strStart - blockStart, last);
            blockStart = strStart;
        }

        private void DeflateSlow()
        {
            for (;;)
            {
                if (lookahead < MinLookahead)
                {
                    FillWindow();
                    if (lookahead == 0) break;
                }
                int hashHead = 0;
                if (lookahead >= MinMatch) hashHead = InsertString(strStart);
                prevLength = matchLength;
                prevMatch = matchStart;
                matchLength = MinMatch - 1;
                if (hashHead != 0 && prevLength < maxLazy && strStart - hashHead <= MaxDist)
                {
                    matchLength = LongestMatch(hashHead);
                    if (matchLength <= 5 && matchLength == MinMatch && strStart - matchStart > TooFar) matchLength = MinMatch - 1;
                }
                if (prevLength >= MinMatch && matchLength <= prevLength)
                {
                    int maxInsert = strStart + lookahead - MinMatch;
                    bool flush = TallyDistance(strStart - 1 - prevMatch, prevLength - MinMatch);
                    lookahead -= prevLength - 1;
                    prevLength -= 2;
                    do
                    {
                        if (++strStart <= maxInsert) InsertString(strStart);
                    } while (--prevLength != 0);
                    matchAvailable = false;
                    matchLength = MinMatch - 1;
                    strStart++;
                    if (flush) FlushBlock(false);
                }
                else if (matchAvailable)
                {
                    if (TallyLiteral(window[strStart - 1])) FlushBlock(false);
                    strStart++;
                    lookahead--;
                }
                else
                {
                    matchAvailable = true;
                    strStart++;
                    lookahead--;
                }
            }
            if (matchAvailable)
            {
                TallyLiteral(window[strStart - 1]);
                matchAvailable = false;
            }
            insert = strStart < MinMatch - 1 ? strStart : MinMatch - 1;
            FlushBlock(true);
        }

        // Choose the stored, fixed or dynamic encoding of a block by its exact length, as the reference does.
        private void FlushBlockCore(int bufferAt, int storedLength, bool last)
        {
            BuildTree(lTree);
            BuildTree(dTree);
            int maxBlIndex = BuildBlTree();
            long optLenB = (optLen + 3 + 7) >> 3;
            long staticLenB = (staticLen + 3 + 7) >> 3;
            if (staticLenB <= optLenB) optLenB = staticLenB;
            if (storedLength + 4 <= optLenB && bufferAt != -1)
            {
                SendBits(last ? 1 : 0, 3);
                WindUp();
                WriteShortLe(storedLength);
                WriteShortLe(~storedLength);
                output.Write(window, bufferAt, storedLength);
            }
            else if (staticLenB == optLenB)
            {
                SendBits((1 << 1) + (last ? 1 : 0), 3);
                CompressBlock(StaticLTreeCode, StaticLTreeLen, StaticDTreeCode, StaticDTreeLen);
            }
            else
            {
                SendBits((2 << 1) + (last ? 1 : 0), 3);
                SendAllTrees(lTree.MaxCode + 1, dTree.MaxCode + 1, maxBlIndex + 1);
                CompressBlock(lTree.Fc, lTree.Dl, dTree.Fc, dTree.Dl);
            }
            InitBlock();
            if (last) WindUp();
        }

        private bool Smaller(Tree tree, int n, int m) =>
            tree.Fc[n] < tree.Fc[m] || (tree.Fc[n] == tree.Fc[m] && depth[n] <= depth[m]);

        private void DownHeap(Tree tree, int k)
        {
            int v = heap[k];
            int j = k << 1;
            while (j <= heapLen)
            {
                if (j < heapLen && Smaller(tree, heap[j + 1], heap[j])) j++;
                if (Smaller(tree, v, heap[j])) break;
                heap[k] = heap[j];
                k = j;
                j <<= 1;
            }
            heap[k] = v;
        }

        private void BuildTree(Tree tree)
        {
            int maxCode = -1, node;
            heapLen = 0;
            heapMax = HeapSize;
            for (int n = 0; n < tree.Elements; n++)
            {
                if (tree.Fc[n] != 0)
                {
                    heap[++heapLen] = maxCode = n;
                    depth[n] = 0;
                }
                else tree.Dl[n] = 0;
            }
            while (heapLen < 2)
            {
                node = heap[++heapLen] = maxCode < 2 ? ++maxCode : 0;
                tree.Fc[node] = 1;
                depth[node] = 0;
                optLen--;
                if (tree.StaticLen is not null) staticLen -= tree.StaticLen[node];
            }
            tree.MaxCode = maxCode;
            for (int n = heapLen / 2; n >= 1; n--) DownHeap(tree, n);
            node = tree.Elements;
            do
            {
                int n = heap[1];
                heap[1] = heap[heapLen--];
                DownHeap(tree, 1);
                int m = heap[1];
                heap[--heapMax] = n;
                heap[--heapMax] = m;
                tree.Fc[node] = tree.Fc[n] + tree.Fc[m];
                depth[node] = (byte)((depth[n] >= depth[m] ? depth[n] : depth[m]) + 1);
                tree.Dl[n] = tree.Dl[m] = node;
                heap[1] = node++;
                DownHeap(tree, 1);
            } while (heapLen >= 2);
            heap[--heapMax] = heap[1];
            GenerateBitLengths(tree);
            GenerateCodes(tree.Dl, tree.Fc, maxCode, blCount);
        }

        private void GenerateBitLengths(Tree tree)
        {
            int h, overflow = 0;
            Array.Clear(blCount);
            tree.Dl[heap[heapMax]] = 0;
            for (h = heapMax + 1; h < HeapSize; h++)
            {
                int n = heap[h];
                int bits = tree.Dl[tree.Dl[n]] + 1;
                if (bits > tree.MaxLength)
                {
                    bits = tree.MaxLength;
                    overflow++;
                }
                tree.Dl[n] = bits;
                if (n > tree.MaxCode) continue;
                blCount[bits]++;
                int xbits = n >= tree.ExtraBase ? tree.ExtraBits[n - tree.ExtraBase] : 0;
                long f = tree.Fc[n];
                optLen += f * (bits + xbits);
                if (tree.StaticLen is not null) staticLen += f * (tree.StaticLen[n] + xbits);
            }
            if (overflow == 0) return;
            do
            {
                int bits = tree.MaxLength - 1;
                while (blCount[bits] == 0) bits--;
                blCount[bits]--;
                blCount[bits + 1] += 2;
                blCount[tree.MaxLength]--;
                overflow -= 2;
            } while (overflow > 0);
            for (int bits = tree.MaxLength; bits != 0; bits--)
            {
                int n = blCount[bits];
                while (n != 0)
                {
                    int m = heap[--h];
                    if (m > tree.MaxCode) continue;
                    if (tree.Dl[m] != bits)
                    {
                        optLen += ((long)bits - tree.Dl[m]) * tree.Fc[m];
                        tree.Dl[m] = bits;
                    }
                    n--;
                }
            }
        }

        private void ScanTree(Tree tree, int maxCode)
        {
            int prevLen = -1, nextLen = tree.Dl[0], count = 0, maxCount = 7, minCount = 4;
            if (nextLen == 0) { maxCount = 138; minCount = 3; }
            tree.Dl[maxCode + 1] = 0xFFFF;
            for (int n = 0; n <= maxCode; n++)
            {
                int curLen = nextLen;
                nextLen = tree.Dl[n + 1];
                if (++count < maxCount && curLen == nextLen) continue;
                if (count < minCount) blTree.Fc[curLen] += count;
                else if (curLen != 0)
                {
                    if (curLen != prevLen) blTree.Fc[curLen]++;
                    blTree.Fc[Rep3To6]++;
                }
                else if (count <= 10) blTree.Fc[RepZ3To10]++;
                else blTree.Fc[RepZ11To138]++;
                count = 0;
                prevLen = curLen;
                if (nextLen == 0) { maxCount = 138; minCount = 3; }
                else if (curLen == nextLen) { maxCount = 6; minCount = 3; }
                else { maxCount = 7; minCount = 4; }
            }
        }

        private void SendTree(Tree tree, int maxCode)
        {
            int prevLen = -1, nextLen = tree.Dl[0], count = 0, maxCount = 7, minCount = 4;
            if (nextLen == 0) { maxCount = 138; minCount = 3; }
            for (int n = 0; n <= maxCode; n++)
            {
                int curLen = nextLen;
                nextLen = tree.Dl[n + 1];
                if (++count < maxCount && curLen == nextLen) continue;
                if (count < minCount)
                {
                    do SendCode(curLen, blTree.Fc, blTree.Dl); while (--count != 0);
                }
                else if (curLen != 0)
                {
                    if (curLen != prevLen)
                    {
                        SendCode(curLen, blTree.Fc, blTree.Dl);
                        count--;
                    }
                    SendCode(Rep3To6, blTree.Fc, blTree.Dl);
                    SendBits(count - 3, 2);
                }
                else if (count <= 10)
                {
                    SendCode(RepZ3To10, blTree.Fc, blTree.Dl);
                    SendBits(count - 3, 3);
                }
                else
                {
                    SendCode(RepZ11To138, blTree.Fc, blTree.Dl);
                    SendBits(count - 11, 7);
                }
                count = 0;
                prevLen = curLen;
                if (nextLen == 0) { maxCount = 138; minCount = 3; }
                else if (curLen == nextLen) { maxCount = 6; minCount = 3; }
                else { maxCount = 7; minCount = 4; }
            }
        }

        private int BuildBlTree()
        {
            ScanTree(lTree, lTree.MaxCode);
            ScanTree(dTree, dTree.MaxCode);
            BuildTree(blTree);
            int maxBlIndex;
            for (maxBlIndex = BlCodes - 1; maxBlIndex >= 3; maxBlIndex--)
            {
                if (blTree.Dl[BlOrder[maxBlIndex]] != 0) break;
            }
            optLen += 3 * ((long)maxBlIndex + 1) + 5 + 5 + 4;
            return maxBlIndex;
        }

        private void SendAllTrees(int lCodes, int dCodes, int blCodes)
        {
            SendBits(lCodes - 257, 5);
            SendBits(dCodes - 1, 5);
            SendBits(blCodes - 4, 4);
            for (int rank = 0; rank < blCodes; rank++) SendBits(blTree.Dl[BlOrder[rank]], 3);
            SendTree(lTree, lCodes - 1);
            SendTree(dTree, dCodes - 1);
        }

        private void CompressBlock(int[] lCode, int[] lLen, int[] dCode, int[] dLen)
        {
            int sx = 0;
            while (sx < symNext)
            {
                int dist = symBuf[sx++];
                dist += symBuf[sx++] << 8;
                int lc = symBuf[sx++];
                if (dist == 0) SendCode(lc, lCode, lLen);
                else
                {
                    int code = LengthCode[lc];
                    SendCode(code + Literals + 1, lCode, lLen);
                    int extra = ExtraLBits[code];
                    if (extra != 0) SendBits(lc - BaseLength[code], extra);
                    dist--;
                    code = DCode(dist);
                    SendCode(code, dCode, dLen);
                    extra = ExtraDBits[code];
                    if (extra != 0) SendBits(dist - BaseDist[code], extra);
                }
            }
            SendCode(EndBlock, lCode, lLen);
        }

        private void SendCode(int c, int[] code, int[] len) => SendBits(code[c], len[c]);

        private void SendBits(int value, int length)
        {
            bitBuffer |= ((ulong)(uint)value & ((1UL << length) - 1)) << bitCount;
            bitCount += length;
            while (bitCount >= 8)
            {
                output.WriteByte((byte)bitBuffer);
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }

        private void WindUp()
        {
            if (bitCount > 0) output.WriteByte((byte)bitBuffer);
            bitBuffer = 0;
            bitCount = 0;
        }

        private void WriteShortLe(int value)
        {
            output.WriteByte((byte)value);
            output.WriteByte((byte)(value >> 8));
        }
    }
}
