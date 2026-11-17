using System.Security.Cryptography;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>One TIFF directory entry as the fixture writers author it: tag, type, count, the packed value and,
/// for a SubIFD pointer, the index of the directory it points at.</summary>
internal sealed record TiffEntry(int Tag, int Type, long Count, byte[] Packed, int? SubIfd = null);

/// <summary>One fixture of a generated set: its bytes and the manifest fields beside them.</summary>
internal sealed record OwnedFixture(byte[] Bytes, string Layout, string Duplicates, string Recipe);

// Owned synthetic census JPEG fixtures (B0 observation batch), formerly tools/reference/jpeg_owned_fixtures.py.
//
// Our own deterministic writer; not derived from Exiv2, ExifTool, libjpeg, camera samples, or any imported
// corpus. The 8x8 single-scan baseline body is an independent re-implementation of the authored recipe also
// used by the C# JPEG fixtures (tools/MetadataSharp.ReferenceFixture). EXIF TIFFs are classic II, authored
// here from the TIFF 6.0 prose; ICC profiles are minimal and carry no decodable real-world data (container-level
// observations only). Deterministic; CI regenerates and compares SHA-256 against tests/fixtures/jpeg/manifest.json.
//
// Usage: jpeg-owned-fixtures generate OUTDIR   # write fixtures + manifest.json
//        jpeg-owned-fixtures verify OUTDIR     # fail if manifest pins drift
//        jpeg-owned-fixtures print-manifest    # emit pinned manifest JSON
//        jpeg-owned-fixtures print-provenance  # emit provenance JSON
internal static class JpegOwnedFixtures
{
    public const int TByte = 1, TAscii = 2, TShort = 3, TLong = 4;

    public static readonly IReadOnlyDictionary<int, int> SizeOf = new Dictionary<int, int>
    {
        [TByte] = 1, [TAscii] = 1, [TShort] = 2, [TLong] = 4,
        [5] = 8,   // RATIONAL (two LONGs)
        [7] = 1,   // UNDEFINED (CLI rewrites ICC/XMP payloads into this type)
        [10] = 8,  // SRATIONAL
        [11] = 4,  // FLOAT
        [12] = 8,  // DOUBLE
        [13] = 4,  // IFD
    };

    public static readonly string Manifest = Repo.Path("tests/fixtures/jpeg/manifest.json");
    public static readonly string Provenance = Repo.Path("tests/fixtures/jpeg/provenance.json");

    // ---------------------------------------------------------------- TIFF core
    public static byte[] PackValues(int typeId, long[] values, string fmt = "<") => typeId switch
    {
        TByte => values.Select(value => checked((byte)value)).ToArray(),
        TAscii => values.Select(value => checked((byte)value)).ToArray(),
        TShort => PyStruct.Pack($"{fmt}{values.Length}H", values.Cast<object>().ToArray()),
        TLong => PyStruct.Pack($"{fmt}{values.Length}I", values.Cast<object>().ToArray()),
        _ => throw new PyException("AssertionError", typeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    };

    public static byte[] PackValues(int typeId, byte[] values) => typeId switch
    {
        TByte or TAscii => values.ToArray(),
        _ => PackValues(typeId, values.Select(value => (long)value).ToArray()),
    };

    public static TiffEntry Entry(int tag, int typeId, long[] values) => new(tag, typeId, values.Length, PackValues(typeId, values));

    public static TiffEntry BytesEntry(int tag, int typeId, byte[] values) => new(tag, typeId, values.Length, PackValues(typeId, values));

    /// <summary>Python's list.sort(key=...): stable, in place.</summary>
    public static void StableSort<T>(List<T> items, Func<T, long> key)
    {
        var sorted = items.Select((item, index) => (item, index)).OrderBy(pair => key(pair.item)).ThenBy(pair => pair.index).Select(pair => pair.item).ToList();
        items.Clear();
        items.AddRange(sorted);
    }

    /// <summary>
    /// Classic II TIFF; ifds = list of entry lists; entries (tag, type, count, packed, subifd_index) where
    /// subifd_index points at the IFD holding the EXIF SubIFD (tag 0x8769, LONG, 1, value = that IFD offset).
    /// With chain each IFD's trailing pointer links to the next IFD (IFD0->IFD1, used for EXIF thumbnail
    /// fixtures); otherwise pointers are 0.
    /// </summary>
    public static byte[] BuildTiff(List<List<TiffEntry>> ifds, bool chain = false)
    {
        foreach (var entries in ifds) StableSort(entries, entry => entry.Tag); // stable: duplicate tags keep authored order
        var bodies = ifds.Select(entries => 2 + 12 * entries.Count + 4).ToList();
        var offsets = new List<long>();
        long off = 8;
        foreach (int size in bodies)
        {
            offsets.Add(off);
            off += size;
        }
        long dataBase = off;
        var placements = new Dictionary<(int, int), long>();
        var poolOrder = new List<(int I, int J)>();
        for (int i = 0; i < ifds.Count; i++)
        {
            for (int j = 0; j < ifds[i].Count; j++)
            {
                var entry = ifds[i][j];
                if (entry.SubIfd is not null || entry.Packed.Length <= 4) continue;
                placements[(i, j)] = dataBase;
                poolOrder.Add((i, j));
                dataBase += (entry.Packed.Length + 1) & ~1;
            }
        }
        var output = new List<byte>();
        output.AddRange("II"u8.ToArray());
        output.AddRange(PyStruct.Pack("<H", 42));
        output.AddRange(PyStruct.Pack("<I", offsets[0]));
        for (int i = 0; i < ifds.Count; i++)
        {
            output.AddRange(PyStruct.Pack("<H", ifds[i].Count));
            for (int j = 0; j < ifds[i].Count; j++)
            {
                var entry = ifds[i][j];
                output.AddRange(PyStruct.Pack("<HHI", entry.Tag, entry.Type, entry.Count));
                if (entry.SubIfd is int target) output.AddRange(PyStruct.Pack("<I", offsets[target]));
                else if (entry.Packed.Length <= 4) output.AddRange(LJust(entry.Packed, 4));
                else output.AddRange(PyStruct.Pack("<I", placements[(i, j)]));
            }
            output.AddRange(PyStruct.Pack("<I", chain && i + 1 < ifds.Count ? offsets[i + 1] : 0));
        }
        foreach (var key in poolOrder)
        {
            var packed = ifds[key.I][key.J].Packed;
            if (!(packed.Length > 4 && output.Count == placements[key])) throw new PyException("AssertionError", "");
            output.AddRange(packed);
            if ((output.Count & 1) != 0) output.Add(0);
        }
        return [.. output];
    }

    /// <summary>bytes.ljust(width, b'\0').</summary>
    public static byte[] LJust(byte[] data, int width) => data.Length >= width ? data : [.. data, .. new byte[width - data.Length]];

    /// <summary>One directory parse_tiff found: its entry count, the values of every tag in order, the byte span
    /// of every entry and of the whole body.</summary>
    internal sealed record ParsedIfd(long Count, Dictionary<long, List<(long Type, long Count, byte[] Value)>> Tags, List<(long Start, long End)> Spans,
                                     (long Start, long End) Body);

    /// <summary>
    /// Parse classic II TIFF from EXIF payload; returns list of IFD dicts with tag -> list of (type, count,
    /// value_bytes) and raw spans for byte checks.
    /// </summary>
    public static List<ParsedIfd> ParseTiff(byte[] exif)
    {
        long U16(long p) => PyStruct.U("<H", exif, checked((int)p));
        long U32(long p) => PyStruct.U("<I", exif, checked((int)p));
        if (!exif.AsSpan(0, Math.Min(2, exif.Length)).SequenceEqual("II"u8) || U16(2) != 42)
            throw new PyException("ValueError", "Only authored classic little-endian TIFF is supported");
        var seen = new List<(long Base, ParsedIfd Ifd)>();
        var stack = new List<long> { U32(4) };
        while (stack.Count > 0)
        {
            long @base = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            if (@base == 0 || seen.Any(item => item.Base == @base)) continue;
            long count = U16(@base);
            var tags = new Dictionary<long, List<(long, long, byte[])>>();
            var spans = new List<(long, long)>();
            for (long p = @base + 2; p < @base + 2 + 12 * count; p += 12)
            {
                long tag = U16(p), typeId = U16(p + 2), n = U32(p + 4);
                long size = n * SizeOf[(int)typeId];
                long off = size <= 4 ? p + 8 : U32(p + 8);
                spans.Add((p, p + 12));
                if (!tags.TryGetValue(tag, out var values)) tags[tag] = values = [];
                values.Add((typeId, n, PyBytes.Slice(exif, off, off + size)));
                if (tag == 0x8769) stack.Add(U32(p + 8));
            }
            long nextBase = U32(@base + 2 + 12 * count);
            if (nextBase != 0) stack.Add(nextBase);
            seen.Add((@base, new ParsedIfd(count, tags, spans, (@base, @base + 2 + 12 * count + 4))));
        }
        return seen.OrderBy(item => item.Base).Select(item => item.Ifd).ToList();
    }

    // ---------------------------------------------------------------- JPEG body
    public static byte[] Segment(int marker, byte[] payload) => [0xFF, (byte)marker, .. PyStruct.Pack(">H", payload.Length + 2), .. payload];

    private static byte[] Dqt() => Segment(0xDB, [0, .. Enumerable.Repeat((byte)1, 64)]); // table 0, all-ones (authored)

    private static byte[] Dht()
    {
        byte[] counts = [1, .. new byte[15]];                // 1 symbol of length 1 (DC)
        byte[] symbols = [0x00, 0x10, 0x01, .. new byte[16]]; // authored symbol set
        return Segment(0xC4, [0x00, .. counts, .. symbols]);
    }

    private static byte[] Sof0() => Segment(0xC0, [8, 0, 8, 0, 8, 1, 1, 0x11, 0]);

    private static byte[] Sos() => Segment(0xDA, [1, 1, 0, 0, 63, 0]);

    /// <summary>SOI + authored prefix segments + minimal baseline body + EOI.</summary>
    public static byte[] JpegBuild(byte[] prefix) => [0xFF, 0xD8, .. prefix, .. Dqt(), .. Dht(), .. Sof0(), .. Sos(), 0x3F, 0xFF, 0xD9];

    public static byte[] ExifSegment(byte[] tiff) => Segment(0xE1, [.. "Exif\0\0"u8, .. tiff]);

    public static byte[] CommentSegment(byte[] payload) => Segment(0xFE, payload);

    public static readonly byte[] IccSignature = "ICC_PROFILE\0"u8.ToArray();

    public static byte[] IccSegment(int seq, int count, byte[] chunk) => Segment(0xE2, [.. IccSignature, (byte)seq, (byte)count, .. chunk]);

    // ---------------------------------------------------------------- ICC bodies
    /// <summary>
    /// Minimal synthetic ICC RGB/GRAY display profile: header + 'desc' only. Not a usable color profile; exists
    /// for container-level observations.
    /// </summary>
    public static byte[] BuildIccProfile(byte[] colorSpace, string description)
    {
        byte[] desc = [.. System.Text.Encoding.ASCII.GetBytes(description), 0];
        byte[] descData = [.. "desc"u8, .. PyStruct.Pack(">I", desc.Length), .. desc, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        const int tagCount = 1, recordSize = 12;
        const int tagTableOff = 128;
        int dataOff = tagTableOff + 4 + recordSize * tagCount;
        int size = dataOff + descData.Length;
        var header = new byte[128];
        PyStruct.PackInto(">I", header, 0, size);                             // profile size
        "SOBS"u8.CopyTo(header.AsSpan(4));                                    // preferred CMM (arbitrary)
        PyStruct.PackInto(">I", header, 8, 0x04300000);                       // ICC version 4.3
        "mntr"u8.CopyTo(header.AsSpan(12));                                   // display device class
        colorSpace.CopyTo(header.AsSpan(16));                                 // 'RGB ' / 'GRAY'
        "XYZ "u8.CopyTo(header.AsSpan(20));                                   // PCS
        PyStruct.PackInto(">6H", header, 24, 2026, 11, 8, 0, 0, 0);           // authored date/time
        "acsp"u8.CopyTo(header.AsSpan(36));                                   // profile signature
        "SOBR"u8.CopyTo(header.AsSpan(40));                                   // platform (arbitrary)
        PyStruct.PackInto(">I", header, 64, 0);                               // attributes hi
        PyStruct.PackInto(">I", header, 68, 0);                               // attributes lo
        PyStruct.PackInto(">I", header, 72, 0);                               // rendering intent
        PyStruct.PackInto(">3i", header, 76, 0x0000F6D6, 0x00010000, 0x0000D32D); // D50 illuminant
        "MetadataSharp synthetic ICC authored fixture, not for real color transforms\0\0\0\0"u8[..36].CopyTo(header.AsSpan(88));
        header.AsSpan(124, 4).Clear();
        byte[] output = [.. header, .. PyStruct.Pack(">I", tagCount), .. "desc"u8, .. PyStruct.Pack(">2I", dataOff, descData.Length), .. descData];
        if (output.Length != size) throw new PyException("AssertionError", "");
        return output;
    }

    public static readonly byte[] ProfileA = BuildIccProfile("RGB "u8.ToArray(), "MetadataSharp ICC profile A");
    public static readonly byte[] ProfileB = BuildIccProfile("GRAY"u8.ToArray(), "MetadataSharp ICC profile B");

    // ------------------------------------------------------------- fixture set
    private static List<TiffEntry> Ifd0Common(params TiffEntry[] entries) => [Entry(274, TShort, [1]), .. entries]; // Orientation first under sort

    private static TiffEntry Artist(byte[] text) => BytesEntry(315, TAscii, [.. text, 0]);

    private static TiffEntry SubIfdPtr(int targetIfd) => new(0x8769, TLong, 1, [0, 0, 0, 0], targetIfd);

    private static TiffEntry Iso(long value) => Entry(34855, TShort, [value]);

    public static byte[] ExifIfd0Tiff() => BuildTiff([Ifd0Common(Artist("Alice"u8.ToArray()))]);

    public static byte[] ExifDupTagTiff() => BuildTiff([Ifd0Common(Artist("Alice"u8.ToArray()), Artist("Bob"u8.ToArray()))]);

    private static List<List<TiffEntry>> Block(byte[] artist, bool withSubIfd)
    {
        if (withSubIfd)
        {
            List<TiffEntry> sub = [Iso(200)];
            return [Ifd0Common(Artist(artist), SubIfdPtr(1)), sub];
        }
        return [Ifd0Common(Artist(artist))];
    }

    public static byte[] ExifSubIfdTiff() => BuildTiff(Block("Alice"u8.ToArray(), true));

    // duplicate SHORT tag inside the SubIFD (first-wins scenario, SubIFD level)
    public static byte[] ExifSubIfdDupTagTiff() => BuildTiff([Ifd0Common(Artist("Alice"u8.ToArray()), SubIfdPtr(1)), [Iso(100), Iso(400)]]);

    public static (byte[] Block1, byte[] Block2) ExifSubIfdMultiBlocks() =>
        (BuildTiff(Block("Alice"u8.ToArray(), true)), BuildTiff([Ifd0Common(Artist("Bob"u8.ToArray()))]));

    public static readonly byte[] CommentA = "MetadataSharp B0 comment A"u8.ToArray();
    public static readonly byte[] CommentB = "MetadataSharp B0 comment B"u8.ToArray();

    private static readonly Lazy<SortedDictionary<string, OwnedFixture>> Catalog = new(BuildCatalog);

    public static SortedDictionary<string, OwnedFixture> Fixtures => Catalog.Value;

    private static SortedDictionary<string, OwnedFixture> BuildCatalog()
    {
        var fixtures = new SortedDictionary<string, OwnedFixture>(StringComparer.Ordinal);
        void Register(string name, byte[] data, string layout, string duplicates, string recipe) => fixtures[name] = new(data, layout, duplicates, recipe);
        Register("exif-base.jpg", JpegBuild(ExifSegment(ExifIfd0Tiff())), "app1-exif-ifd0", "none", "jpeg-exif-ifd0-orientation-artist-v1");
        Register("exif-dup-tag.jpg", JpegBuild(ExifSegment(ExifDupTagTiff())), "app1-exif-ifd0", "first-wins", "jpeg-exif-ifd0-duplicate-artist-v1");
        Register("exif-multi-app1.jpg", JpegBuild([.. ExifSegment(BuildTiff([Ifd0Common(Artist("Alice"u8.ToArray()))])),
                                                   .. ExifSegment(BuildTiff([Ifd0Common(Artist("Bob"u8.ToArray()))]))]),
                 "app1-exif-ifd0", "multiple-independent", "jpeg-exif-two-app1-independent-v1");
        Register("exif-subifd.jpg", JpegBuild(ExifSegment(ExifSubIfdTiff())), "app1-exif-ifd0-subifd", "none", "jpeg-exif-subifd-iso-v1");
        Register("exif-subifd-dup-tag.jpg", JpegBuild(ExifSegment(ExifSubIfdDupTagTiff())), "app1-exif-ifd0-subifd", "first-wins", "jpeg-exif-subifd-duplicate-iso-v1");
        var (block1, block2) = ExifSubIfdMultiBlocks();
        Register("exif-subifd-multi.jpg", JpegBuild([.. ExifSegment(block1), .. ExifSegment(block2)]), "app1-exif-ifd0-subifd", "multiple-independent", "jpeg-exif-subifd-two-app1-v1");
        Register("com-none.jpg", JpegBuild([]), "com-single", "none", "jpeg-com-none-v1");
        Register("com-single.jpg", JpegBuild(CommentSegment(CommentA)), "com-single", "none", "jpeg-com-single-a-v1");
        Register("com-multiple.jpg", JpegBuild([.. CommentSegment(CommentA), .. CommentSegment(CommentB)]), "com-multiple", "none", "jpeg-com-multiple-distinct-v1");
        Register("com-multiple-dup.jpg", JpegBuild([.. CommentSegment(CommentA), .. CommentSegment(CommentA)]), "com-multiple", "first-wins", "jpeg-com-multiple-identical-v1");
        int halfA = ProfileA.Length / 2, halfB = ProfileB.Length / 2;
        Register("icc-single.jpg", JpegBuild(IccSegment(1, 1, ProfileA)), "app2-icc-single", "none", "jpeg-icc-single-segment-a-v1");
        Register("icc-single-dup.jpg", JpegBuild([.. IccSegment(1, 1, ProfileA), .. IccSegment(1, 1, ProfileB)]), "app2-icc-single", "first-wins", "jpeg-icc-two-single-segment-profiles-v1");
        Register("icc-segmented.jpg", JpegBuild([.. IccSegment(1, 2, ProfileA[..halfA]), .. IccSegment(2, 2, ProfileA[halfA..])]),
                 "app2-icc-segmented", "none", "jpeg-icc-two-segment-a-v1");
        Register("icc-segmented-dup.jpg", JpegBuild([.. IccSegment(1, 2, ProfileA[..halfA]), .. IccSegment(2, 2, ProfileA[halfA..]),
                                                     .. IccSegment(1, 2, ProfileB[..halfB]), .. IccSegment(2, 2, ProfileB[halfB..])]),
                 "app2-icc-segmented", "first-wins", "jpeg-icc-two-two-segment-profiles-v1");
        Register("xmp-none.jpg", JpegBuild([]), "app1-xmp-standard", "none", "jpeg-xmp-none-v1");
        Register("xmp-standard.jpg", JpegBuild(XmpSegment(XmpPacket())), "app1-xmp-standard", "none", "jpeg-xmp-standard-packet-v1");
        byte[] extDoc = XmpExtendedDoc();
        int half = extDoc.Length / 2;
        Register("xmp-extended.jpg", JpegBuild([.. XmpSegment(XmpBaseWithExtended()), .. XmpExtensionSegment(0, extDoc[..half], extDoc.Length),
                                                .. XmpExtensionSegment(half, extDoc[half..], extDoc.Length)]),
                 "app1-xmp-extended", "none", "jpeg-xmp-extended-base-plus-2-chunks-v1");
        Register("xmp-duplicate.jpg", JpegBuild([.. XmpSegment(Replace(XmpPacket(), "MetadataSharp</xmp:CreatorTool>"u8, "MetadataSharpA</xmp:CreatorTool>"u8)),
                                                 .. XmpSegment(Replace(XmpPacket(), "MetadataSharp</xmp:CreatorTool>"u8, "MetadataSharpB</xmp:CreatorTool>"u8))]),
                 "app1-xmp-standard", "first-wins", "jpeg-xmp-two-app1-different-creators-v1");
        Register("xmp-extended-duplicate.jpg", JpegBuild([.. XmpSegment(XmpBaseWithExtended()), .. XmpSegment(XmpBaseWithExtended()),
                                                          .. XmpExtensionSegment(0, extDoc[..half], extDoc.Length),
                                                          .. XmpExtensionSegment(half, extDoc[half..], extDoc.Length)]),
                 "app1-xmp-extended", "first-wins", "jpeg-xmp-extended-two-base-one-chunk-set-v1");
        Register("xmp-extended-nonutf8.jpg", JpegBuild([.. XmpSegment([0x89, .. "RAW"u8, 0xFF, 0xFE, .. " not parseable xmp"u8]),
                                                        .. XmpExtensionSegment(0, [0x89, .. "EXT-chunk-1"u8, 0xFF, 0xFE], 16),
                                                        .. XmpExtensionSegment(8, [0x89, .. "EXT-chunk-2"u8, 0xFF, 0xFE], 16)]),
                 "app1-xmp-extended", "none", "jpeg-xmp-extended-non-utf8-base-and-chunks-v1");
        Register("xmp-nonutf8.jpg", JpegBuild(XmpSegment([0x89, 0xFE, 0xFF, .. "RAW not xml"u8])), "app1-xmp-standard", "none", "jpeg-xmp-non-utf8-payload-v1");
        Register("iptc-none.jpg", JpegBuild([]), "app13-8bim-iptc", "none", "jpeg-iptc-none-v1");
        Register("iptc-single.jpg", JpegBuild(App13Iptc([[.. IptcAlpha, .. IptcBeta]])), "app13-8bim-iptc", "none", "jpeg-iptc-single-8bim-unique-v1");
        Register("iptc-single-dup-dataset.jpg", JpegBuild(App13Iptc([[.. IptcDup, .. IptcDup]])), "app13-8bim-iptc", "first-wins", "jpeg-iptc-single-8bim-repeated-dataset-v1");
        Register("iptc-multiple.jpg", JpegBuild([.. App13Iptc([IptcAlpha]), .. App13Iptc([IptcBeta])]), "app13-8bim-iptc-multiple", "none", "jpeg-iptc-two-app13-unique-v1");
        Register("iptc-multiple-dup.jpg", JpegBuild([.. App13Iptc([IptcDup]), .. App13Iptc([IptcDup])]), "app13-8bim-iptc-multiple", "first-wins", "jpeg-iptc-two-app13-shared-dataset-v1");
        byte[] rawStrip = Enumerable.Range(0, 192).Select(i => (byte)(i % 256)).ToArray();
        Register("exif-thumb-jpeg.jpg", JpegBuild(ExifSegment(ThumbTiff(ThumbJpegBytes(), true))), "exif-ifd1-jpeg", "none", "jpeg-exif-ifd1-jpeg-thumbnail-v1");
        Register("exif-thumb-raw.jpg", JpegBuild(ExifSegment(ThumbTiff(rawStrip, false))), "exif-ifd1-uncompressed", "none", "jpeg-exif-ifd1-uncompressed-thumbnail-v1");
        Register("com-raw.jpg", JpegBuild(CommentSegment(RawComA)), "com-single", "none", "jpeg-com-raw-high-bytes-v1");
        Register("com-raw-multiple.jpg", JpegBuild([.. CommentSegment(RawComA), .. CommentSegment(RawComB)]), "com-multiple", "none", "jpeg-com-multiple-raw-distinct-v1");
        Register("com-raw-multiple-dup.jpg", JpegBuild([.. CommentSegment(RawComA), .. CommentSegment(RawComA)]), "com-multiple", "first-wins", "jpeg-com-multiple-raw-identical-v1");
        return fixtures;
    }

    /// <summary>bytes.replace(old, new).</summary>
    public static byte[] Replace(byte[] data, ReadOnlySpan<byte> old, ReadOnlySpan<byte> replacement)
    {
        var output = new List<byte>();
        int at = 0;
        while (true)
        {
            int found = data.AsSpan(at).IndexOf(old);
            if (found < 0 || old.Length == 0) break;
            output.AddRange(data.AsSpan(at, found).ToArray());
            output.AddRange(replacement.ToArray());
            at += found + old.Length;
        }
        output.AddRange(data.AsSpan(at).ToArray());
        return [.. output];
    }

    public static byte[] FixtureBytes(string name) => Fixtures[name].Bytes;

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>The manifest entries of a fixture set, in file name order.</summary>
    public static List<object?> ManifestOf(IEnumerable<KeyValuePair<string, OwnedFixture>> fixtures) =>
        fixtures.Select(pair => (object?)PyDict.Of(("file", pair.Key), ("sha256", Sha256(pair.Value.Bytes)), ("length", (long)pair.Value.Bytes.Length),
                                                   ("layout", pair.Value.Layout), ("duplicates", pair.Value.Duplicates), ("recipe", pair.Value.Recipe),
                                                   ("rights", "owned-synthetic"))).ToList();

    public static List<object?> ManifestEntries() => ManifestOf(Fixtures);

    private static PyDict ProvenanceOf(List<object?> fixtures) => PyDict.Of(
        ("encoder", "None (authored byte-level writer; tools/MetadataSharp.Harness/Reference/JpegOwnedFixtures.cs)"),
        ("recipe", "Deterministic classic-II-TIFF/EXIF/COM/APP1-XMP/APP13-IPTC/ICC-in-JPEG writer: "
                   + "SOI + owned segments + 8x8 single-scan baseline body (DQT/DHT/SOF0/SOS, one entropy byte) + EOI; "
                   + "EXIF IFD0 carries Orientation and ASCII Artist; SubIFD fixtures carry tag 0x8769; "
                   + "EXIF IFD1 carries the embedded thumbnail (IFD0->IFD1 next-IFD chain, inline patched "
                   + "strip/JPEG blob offsets); COM payloads are opaque bytes as stored (incl. non-UTF-8 high-byte "
                   + "variants); XMP fixtures carry standard APP1 packets, base+2 extension chunks (Extended XMP) "
                   + "and non-UTF-8/duplicate/undecodable variants; IPTC fixtures carry Photoshop 8BIM 0x0404 "
                   + "resources with repeatable 2:25 keywords (single and two-APP13-segment variants); "
                   + "ICC profiles are minimal synthetic RGB/GRAY display profiles (header + desc tag only), "
                   + "never decodable real profiles. "
                   + "Pinned by tests/fixtures/jpeg/manifest.json; CI regenerates and compares SHA-256."),
        ("inputRights", "MetadataSharp authored synthetic JPEG fixtures, MIT; no imported images, "
                        + "no imported codec code/tests, no upstream fixture corpus; the 8x8 baseline body "
                        + "independently re-implements the authored recipe used by the C# JPEG fixtures."),
        ("fixtures", fixtures));

    public static readonly OwnedFixtureSet Set = new("jpeg-owned-fixtures", Manifest, "*.jpg", Repo.Path("artifacts/owned-jpeg"), () => Fixtures,
                                                      ProvenanceOf);

    public static int Run(string[] argv) => Set.Run(argv);

    // ------------------------------------------------- XMP / IPTC / thumbnail / raw COM
    public static readonly byte[] XmpSignature = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();
    public static readonly byte[] XmpExtSignature = "http://ns.adobe.com/xmp/extension/\0"u8.ToArray();
    public static readonly byte[] XmpExtGuid = "B0B0B0B0000000000000000000000001"u8.ToArray();

    public static byte[] XmpPacket() => Latin1(
        "<?xpacket begin=\"\u00ef\u00bb\u00bf\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
        + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"MetadataSharp\">"
        + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">"
        + "<xmp:CreatorTool>MetadataSharp</xmp:CreatorTool>"
        + "</rdf:Description></rdf:RDF></x:xmpmeta>"
        + "<?xpacket end=\"w\"?>");

    public static byte[] XmpExtendedDoc() => Latin1(
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
        + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" "
        + "xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
        + "<xmp:ModifyDate>2026-11-08T00:00:00</xmp:ModifyDate>"
        + "<dc:creator><rdf:Seq><rdf:li>MetadataSharp</rdf:li></rdf:Seq></dc:creator>"
        + "</rdf:Description></rdf:RDF></x:xmpmeta>");

    public static byte[] XmpBaseWithExtended() =>
    [
        .. Latin1("<?xpacket begin=\"\u00ef\u00bb\u00bf\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
                  + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
                  + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
                  + "<rdf:Description rdf:about=\"\" xmlns:xmpNote=\"http://ns.adobe.com/xmp/extension/\">"
                  + "<xmpNote:HasExtendedXMP>"),
        .. XmpExtGuid,
        .. Latin1("</xmpNote:HasExtendedXMP>"
                  + "</rdf:Description></rdf:RDF></x:xmpmeta>"
                  + "<?xpacket end=\"w\"?>"),
    ];

    /// <summary>A Python bytes literal written as text whose characters are the byte values (b'...').</summary>
    public static byte[] Latin1(string text) => text.Select(c => checked((byte)c)).ToArray();

    public static byte[] XmpSegment(byte[] packet) => Segment(0xE1, [.. XmpSignature, .. packet]);

    public static byte[] XmpExtensionSegment(int offset, byte[] chunk, int fullSize) =>
        Segment(0xE1, [.. XmpExtSignature, .. XmpExtGuid, .. PyStruct.Pack(">II", fullSize, offset), .. chunk]);

    public const int IptcRecord = 2, IptcKeywords = 25; // 2:25 keywords, repeatable dataset (authored)

    public static byte[] IptcDataset(int record, int datasetId, byte[] data)
    {
        if (data.Length >= 0x8000) throw new PyException("AssertionError", "");
        return [0x1C, (byte)record, (byte)datasetId, .. PyStruct.Pack(">H", data.Length), .. data];
    }

    public static byte[] IptcKeyword(string text) => IptcDataset(IptcRecord, IptcKeywords, System.Text.Encoding.ASCII.GetBytes(text));

    public static byte[] BimBlock(int resourceId, byte[] name, byte[] data)
    {
        byte[] pascal = [(byte)name.Length, .. name];
        if (pascal.Length % 2 != 0) pascal = [.. pascal, 0];
        byte[] output = [.. "8BIM"u8, .. PyStruct.Pack(">H", resourceId), .. pascal, .. PyStruct.Pack(">I", data.Length), .. data];
        if (output.Length % 2 != 0) output = [.. output, 0];
        return output;
    }

    public const int IptcResourceId = 0x0404; // IPTC/NAA resource id in Photoshop 8BIM blocks

    public static byte[] App13Iptc(IEnumerable<byte[]> payloads) =>
        Segment(0xED, [.. "Photoshop 3.0\0"u8, .. payloads.SelectMany(payload => BimBlock(IptcResourceId, [], payload))]);

    public static readonly byte[] IptcAlpha = IptcKeyword("MetadataSharpAlpha");
    public static readonly byte[] IptcBeta = IptcKeyword("MetadataSharpBeta");
    public static readonly byte[] IptcDup = IptcKeyword("MetadataSharpDup");

    /// <summary>
    /// IFD0 (Orientation) chained to IFD1 (EXIF thumbnail); pool after bodies; the thumbnail blob is the only
    /// external value (offsets computed exactly).
    /// </summary>
    public static byte[] ThumbTiff(byte[] blob, bool compressed)
    {
        List<TiffEntry> orientation = [Entry(274, TShort, [1])];
        List<TiffEntry> ifd1 = compressed
            ? [Entry(259, TShort, [6]), Entry(262, TShort, [2]), new(513, TLong, 1, [0, 0, 0, 0]), Entry(514, TLong, [blob.Length])]
            : [Entry(256, TShort, [8]), Entry(257, TShort, [8]), Entry(259, TShort, [1]), Entry(262, TShort, [2]), new(273, TLong, 1, [0, 0, 0, 0]),
               Entry(277, TShort, [3]), Entry(278, TShort, [8]), Entry(279, TLong, [blob.Length])];
        int[] bodies = [2 + 12 * orientation.Count + 4, 2 + 12 * ifd1.Count + 4];
        int ifd1Off = 8 + bodies[0];
        int pool = ifd1Off + bodies[1];
        int blobOff = pool;
        int patchTag = compressed ? 513 : 273;
        for (int j = 0; j < ifd1.Count; j++)
        {
            if (ifd1[j].Tag == patchTag) ifd1[j] = ifd1[j] with { Packed = PyStruct.Pack("<I", blobOff), SubIfd = null };
        }
        return [.. BuildTiff([orientation, ifd1], chain: true), .. blob];
    }

    private static readonly Lazy<byte[]> ThumbJpeg = new(() => JpegBuild([])); // reuses the authored baseline body

    public static byte[] ThumbJpegBytes() => ThumbJpeg.Value;

    public static readonly byte[] RawComA = [0x89, 0xFE, 0xFF, .. "RAW high-byte comment A"u8];
    public static readonly byte[] RawComB = [0x89, 0xFE, 0xFF, .. "RAW high-byte comment B"u8];
}
