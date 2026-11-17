using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>A DNG directory entry before planning: tag, type, count and the values (numbers, rational pairs or
/// bytes).</summary>
internal sealed class DngEntry(int tag, int type, long count, object values)
{
    public int Tag { get; set; } = tag;

    public int Type { get; set; } = type;

    public long Count { get; set; } = count;

    /// <summary>long[] for BYTE/SHORT/LONG, byte[] for an ASCII or BYTE payload, (long, long)[] for (S)RATIONAL.</summary>
    public object Values { get; set; } = values;

    public DngEntry Clone() => new(Tag, Type, Count, Values);
}

/// <summary>A planned entry: the packed payload and, for a payload over four bytes, its absolute offset.</summary>
internal sealed record PlannedEntry(int Tag, int Type, long Count, byte[] Payload, long? Offset);

// Owned synthetic minimal DNG fixtures from the DNG 1.7.1.0 prose, formerly tools/reference/dng_fixtures.py.
//
// Our own deterministic writer - not derived from Adobe SDK code, vendor samples or any imported corpus. Raw image:
// 4x2, 16-bit unsigned CFA (photometric 32803), one strip. Thumbnail variant adds a 4x2 8-bit RGB (photometric 2)
// IFD0 with one SubIFD (330, LONG/1) to the raw leaf; the RGB root also carries the DNG identity tags.
// Deterministic layout: read by both this generator and tools/MetadataSharp.ReferenceFixture; CI compares SHA-256.
//
// Usage: dng-fixtures generate OUTDIR   # writes four .dng files + manifest.json
internal static class DngFixtures
{
    public const int TByte = 1, TAscii = 2, TShort = 3, TLong = 4, TRational = 5, TSRational = 10;

    public static readonly IReadOnlyDictionary<int, int> SizeOf = new Dictionary<int, int>
    {
        [TByte] = 1, [TAscii] = 1, [TShort] = 2, [TLong] = 4, [TRational] = 8, [TSRational] = 8,
    };

    public static readonly long[] RawPixels = [0x0000, 0x1000, 0x2000, 0x3000, 0x4000, 0x5000, 0x6000, 0x7000];
    public static readonly long[] ThumbPixels = Enumerable.Range(0, 24).Select(i => (long)i).ToArray();
    public static readonly byte[] Software = "MetadataSharp D1 fixture\0"u8.ToArray();   // 25 bytes
    public static readonly byte[] Artist = "MetadataSharp\0"u8.ToArray();               // 14 bytes
    public static readonly byte[] UniqueModel = "MetadataSharp DNG1\0"u8.ToArray();     // 19 bytes

    public static byte[] PackPayload(string fmt, int typeId, object values)
    {
        switch (typeId)
        {
            case TByte or TAscii:
                return values is byte[] bytes ? bytes.ToArray() : ((long[])values).Select(value => checked((byte)value)).ToArray();
            case TShort:
            {
                var numbers = Numbers(values);
                return PyStruct.Pack($"{fmt}{numbers.Length}H", numbers.Cast<object>().ToArray());
            }
            case TLong:
            {
                var numbers = Numbers(values);
                return PyStruct.Pack($"{fmt}{numbers.Length}I", numbers.Cast<object>().ToArray());
            }
            case TRational or TSRational:
            {
                var flat = ((ValueTuple<long, long>[])values).SelectMany(value => new object[] { value.Item1, value.Item2 }).ToArray();
                return PyStruct.Pack($"{fmt}{flat.Length}{(typeId == TRational ? 'I' : 'i')}", flat);
            }
        }
        throw new PyException("AssertionError", typeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static long[] Numbers(object values) => values is byte[] bytes ? bytes.Select(b => (long)b).ToArray() : (long[])values;

    public static long Align2(long value) => (value + 1) & ~1L;

    private static DngEntry E(int tag, int type, long count, params long[] values) => new(tag, type, count, values);

    private static DngEntry B(int tag, int type, byte[] values) => new(tag, type, values.Length, values);

    private static DngEntry R(int tag, int type, params (long, long)[] values) => new(tag, type, values.Length, values);

    private static readonly (long, long)[] Identity = [(1, 1), (0, 1), (0, 1), (0, 1), (1, 1), (0, 1), (0, 1), (0, 1), (1, 1)];

    public static List<DngEntry> RawEntries() =>
    [
        E(254, TLong, 1, 0),
        E(256, TLong, 1, 4),
        E(257, TLong, 1, 2),
        E(258, TShort, 1, 16),
        E(259, TShort, 1, 1),
        E(262, TShort, 1, 32803),
        E(273, TLong, 1, 0),  // patched: raw strip offset
        E(274, TShort, 1, 1),
        E(277, TShort, 1, 1),
        E(278, TLong, 1, 2),
        E(279, TLong, 1, 16),
        E(284, TShort, 1, 1),
        B(305, TAscii, Software),
        B(315, TAscii, Artist),
        E(339, TShort, 1, 1),
        E(33421, TShort, 2, 2, 2),
        E(33422, TByte, 4, 0, 1, 1, 2),
        E(50706, TByte, 4, 1, 1, 0, 0),
        E(50707, TByte, 4, 1, 0, 0, 0),
        B(50708, TAscii, UniqueModel),
        E(50710, TByte, 3, 0, 1, 2),
        E(50711, TShort, 1, 1),
        E(50713, TShort, 2, 1, 1),
        R(50714, TRational, (0, 1)),
        E(50717, TLong, 1, 65535),
        E(50719, TLong, 2, 0, 0),
        E(50720, TLong, 2, 4, 2),
        R(50721, TSRational, Identity),
        R(50728, TRational, (1, 1), (1, 1), (1, 1)),
        E(50778, TShort, 1, 21),
        E(50829, TLong, 4, 0, 0, 2, 4),
    ];

    public static List<DngEntry> ThumbnailEntries() =>
    [
        E(254, TLong, 1, 1),
        E(256, TLong, 1, 4),
        E(257, TLong, 1, 2),
        E(258, TShort, 3, 8, 8, 8),
        E(259, TShort, 1, 1),
        E(262, TShort, 1, 2),
        E(273, TLong, 1, 0),  // patched: thumbnail strip offset
        E(274, TShort, 1, 1),
        E(277, TShort, 1, 3),
        E(278, TLong, 1, 2),
        E(279, TLong, 1, 24),
        E(284, TShort, 1, 1),
        B(305, TAscii, Software),
        E(330, TLong, 1, 0),  // patched: raw IFD offset
        E(339, TShort, 3, 1, 1, 1),
        E(50706, TByte, 4, 1, 1, 0, 0),
        E(50707, TByte, 4, 1, 0, 0, 0),
        B(50708, TAscii, UniqueModel),
        R(50721, TSRational, Identity),
        R(50728, TRational, (1, 1), (1, 1), (1, 1)),
        E(50778, TShort, 1, 21),
    ];

    /// <summary>Compute absolute external offsets; returns (items, end_offset).</summary>
    public static (List<PlannedEntry> Items, long End) PlanIfd(string fmt, IReadOnlyList<DngEntry> entries, long @base)
    {
        long body = 2 + 12 * entries.Count + 4;
        long ext = Align2(@base + body);
        var items = new List<PlannedEntry>();
        foreach (var entry in entries.Select((entry, index) => (entry, index)).OrderBy(pair => pair.entry.Tag).ThenBy(pair => pair.index).Select(pair => pair.entry))
        {
            byte[] payload = PackPayload(fmt, entry.Type, entry.Values);
            if (payload.Length != SizeOf[entry.Type] * entry.Count)
                throw new PyException("AssertionError", $"({entry.Tag}, {payload.Length})");
            if (payload.Length > 4)
            {
                ext = Align2(ext);
                items.Add(new(entry.Tag, entry.Type, entry.Count, payload, ext));
                ext += payload.Length;
            }
            else
            {
                items.Add(new(entry.Tag, entry.Type, entry.Count, payload, null));
            }
        }
        var tags = items.Select(item => item.Tag).ToList();
        if (!tags.SequenceEqual(tags.Order()) || tags.Distinct().Count() != tags.Count) throw new PyException("AssertionError", "");
        return (items, Align2(ext));
    }

    public static byte[] RenderIfd(string fmt, IReadOnlyList<PlannedEntry> items, long @base, long nextOffset)
    {
        var body = new byte[2 + 12 * items.Count + 4];
        PyStruct.PackInto($"{fmt}H", body, 0, items.Count);
        var extras = new List<(long Offset, byte[] Payload)>();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            PyStruct.PackInto($"{fmt}HHI", body, 2 + 12 * i, item.Tag, item.Type, item.Count);
            if (item.Offset is not long offset)
            {
                var cell = new byte[4];
                item.Payload.CopyTo(cell, 0);
                cell.CopyTo(body, 2 + 12 * i + 8);
            }
            else
            {
                PyStruct.PackInto($"{fmt}I", body, 2 + 12 * i + 8, offset);
                extras.Add((offset, item.Payload));
            }
        }
        PyStruct.PackInto($"{fmt}I", body, 2 + 12 * items.Count, nextOffset);
        long end = @base + body.Length;
        var blob = new List<byte>();
        foreach (var (offset, payload) in extras.Select((extra, index) => (extra, index)).OrderBy(pair => pair.extra.Offset).ThenBy(pair => pair.index).Select(pair => pair.extra))
        {
            long gap = offset - (end + blob.Count);
            if (gap > 0) blob.AddRange(new byte[gap]);
            blob.AddRange(payload);
        }
        return [.. body, .. blob];
    }

    public static byte[] RawPixelsBytes(string fmt) => PyStruct.Pack($"{fmt}{RawPixels.Length}H", RawPixels.Cast<object>().ToArray());

    public static byte[] ThumbPixelsBytes() => ThumbPixels.Select(value => (byte)value).ToArray();

    /// <summary>Lay the blocks out at their offsets, zero-filling the gaps (a block before the end is pasted after
    /// it, as bytes * negative is empty in Python).</summary>
    public static byte[] Assemble(SortedDictionary<long, byte[]> blocks)
    {
        var data = new List<byte>();
        foreach (var (offset, chunk) in blocks)
        {
            long gap = offset - data.Count;
            if (gap > 0) data.AddRange(new byte[gap]);
            data.AddRange(chunk);
        }
        return [.. data];
    }

    public static (byte[] Data, PyDict Info) Build(string order, string shape)
    {
        string fmt = order == "II" ? "<" : ">";
        byte[] orderMark = System.Text.Encoding.ASCII.GetBytes(order);
        byte[] magic = PyStruct.Pack($"{fmt}H", 42);
        var blocks = new SortedDictionary<long, byte[]> { [0] = [.. orderMark, .. magic, .. PyStruct.Pack($"{fmt}I", 8)] };
        PyDict info;
        if (shape == "raw")
        {
            var raw = RawEntries();
            raw[6].Values = new long[] { 0 };
            var (_, extEnd) = PlanIfd(fmt, raw, 8);
            long rawStrip = extEnd;
            raw[6].Values = new long[] { rawStrip };
            var (items, _) = PlanIfd(fmt, raw, 8);
            blocks[8] = RenderIfd(fmt, items, 8, 0);
            blocks[rawStrip] = RawPixelsBytes(fmt);
            info = PyDict.Of(("ifd0", 8L), ("raw_ifd", 8L), ("raw_strip", rawStrip), ("raw_strip_length", 16L), ("thumb_ifd", null), ("thumb_strip", null));
        }
        else
        {
            var thumb = ThumbnailEntries();
            var raw = RawEntries();
            thumb[6].Values = new long[] { 0 };
            raw[6].Values = new long[] { 0 };
            var (_, tExtEnd) = PlanIfd(fmt, thumb, 8);
            long thumbStrip = tExtEnd;
            long rawBase = thumbStrip + 24;
            thumb[6].Values = new long[] { thumbStrip };
            thumb[13] = E(330, TLong, 1, rawBase);
            var (tItems, _) = PlanIfd(fmt, thumb, 8);
            var (_, rExtEnd) = PlanIfd(fmt, raw, rawBase);
            long rawStrip = rExtEnd;
            raw[6].Values = new long[] { rawStrip };
            var (rItems, _) = PlanIfd(fmt, raw, rawBase);
            blocks[8] = RenderIfd(fmt, tItems, 8, 0);
            blocks[thumbStrip] = ThumbPixelsBytes();
            blocks[rawBase] = RenderIfd(fmt, rItems, rawBase, 0);
            blocks[rawStrip] = RawPixelsBytes(fmt);
            info = PyDict.Of(("ifd0", 8L), ("raw_ifd", rawBase), ("raw_strip", rawStrip), ("raw_strip_length", 16L), ("thumb_ifd", 8L), ("thumb_strip", thumbStrip));
        }
        return (Assemble(blocks), info);
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("dng-fixtures").Add("command", choices: ["generate"]).Add("outdir");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string outdir = args.Str("outdir");
        PyFile.MakeDirs(outdir);
        var manifest = new List<object?>();
        foreach (string order in new[] { "II", "MM" })
        {
            foreach (string shape in new[] { "raw", "thumb" })
            {
                var (data, info) = Build(order, shape);
                string name = $"dng-{shape}-{(order == "II" ? "ii" : "mm")}.dng";
                PyFile.WriteBytes(PyPath.Join(outdir, name), data);
                var entry = PyDict.Of(("file", name), ("sha256", JpegOwnedFixtures.Sha256(data)), ("length", (long)data.Length), ("byteOrder", order), ("shape", shape));
                foreach (var (key, value) in info) entry[key] = value;
                entry["rawStripSha256"] = JpegOwnedFixtures.Sha256(RawPixelsBytes(order == "II" ? "<" : ">"));
                manifest.Add(entry);
                Out.Print($"{name} {data.Length} {entry.Str("sha256")}");
            }
        }
        PyFile.WriteText(PyPath.Join(outdir, "manifest.json"), PyJson.Dumps(manifest, indent: 2) + "\n");
        return 0;
    }
}
