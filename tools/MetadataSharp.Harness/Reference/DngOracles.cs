using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>Input lies outside this oracle's supported, bounded profile.</summary>
internal sealed class DngOracleError(string message) : PyException("DngOracleError", message);

internal sealed class ThumbnailOracleError(string message) : PyException("ThumbnailOracleError", message);

internal sealed class MetadataOracleError(string message) : PyException("MetadataOracleError", message);

/// <summary>Classic TIFF integers of either byte order, read with the bounds check each oracle states in its own
/// words.</summary>
internal sealed class TiffReader(byte[] data, Func<string, Exception> fail, string outOfBounds)
{
    public bool Little { get; } = data.Length >= 2 && data[0] == 'I';

    public long Number(long at, int size)
    {
        if (at < 0 || at + size > data.Length) throw fail(outOfBounds);
        long value = 0;
        for (int i = 0; i < size; i++)
        {
            long b = data[at + (Little ? i : size - 1 - i)];
            value |= b << (8 * i);
        }
        return value;
    }

    public static bool HasHeader(byte[] data) => data.Length >= 8 && ((data[0] == 'I' && data[1] == 'I') || (data[0] == 'M' && data[1] == 'M'));
}

// Bounded raw-strip byte oracle for the owned D1 classic-TIFF DNG fixtures, formerly tools/reference/dng_raw_oracle.py.
//
// This is not a DNG decoder or a general TIFF validator. It follows IFD next links and SubIFDs, selects CFA IFDs by
// PhotometricInterpretation, and extracts their strip byte ranges. It fails closed on malformed or unsupported
// structures rather than treating an unreadable image as evidence of preserved raw pixels.
internal static class DngRawOracle
{
    /// <summary>Return CFA strip payloads in IFD traversal / strip order. Supports classic TIFF, either byte order, and
    /// SHORT/LONG integer arrays. Limits intentionally bound work even when given an Exiv2-rewritten file.</summary>
    public static List<byte[]> RawStrips(byte[] data)
    {
        if (!TiffReader.HasHeader(data)) throw new DngOracleError("invalid TIFF header");
        var reader = new TiffReader(data, message => new DngOracleError(message), "out-of-bounds TIFF value");
        long Number(long at, int size) => reader.Number(at, size);
        if (Number(2, 2) != 42) throw new DngOracleError("not classic TIFF");

        List<long> Values(long entry)
        {
            long typ = Number(entry + 2, 2);
            long count = Number(entry + 4, 4);
            int size = typ switch { 3 => 2, 4 => 4, _ => 0 };
            if (size == 0 || count == 0 || count > 4096) throw new DngOracleError("unsupported integer field");
            long length = count * size;
            long at = length <= 4 ? entry + 8 : Number(entry + 8, 4);
            if (at + length > data.Length) throw new DngOracleError("out-of-bounds integer array");
            var values = new List<long>();
            for (long i = 0; i < count; i++) values.Add(Number(at + i * size, size));
            return values;
        }

        long first = Number(4, 4);
        if (first == 0) throw new DngOracleError("missing IFD");
        var pending = new List<long> { first };
        var visited = new HashSet<long>();
        var result = new List<byte[]>();
        while (pending.Count > 0)
        {
            long offset = pending[^1];
            pending.RemoveAt(pending.Count - 1);
            if (visited.Contains(offset) || visited.Count >= 128) throw new DngOracleError("cyclic or excessive IFD graph");
            visited.Add(offset);
            long count = Number(offset, 2);
            if (count > 4096 || offset + 2 + 12 * count + 4 > data.Length) throw new DngOracleError("invalid IFD table");
            var fields = new Dictionary<long, List<long>>();
            for (long i = 0; i < count; i++)
            {
                long entry = offset + 2 + 12 * i;
                long tag = Number(entry, 2);
                if (tag is 262 or 273 or 279 or 330)
                {
                    if (fields.ContainsKey(tag)) throw new DngOracleError("duplicate relevant TIFF tag");
                    fields[tag] = Values(entry);
                }
            }
            long nextIfd = Number(offset + 2 + 12 * count, 4);
            if (nextIfd != 0) pending.Add(nextIfd);
            foreach (long sub in Enumerable.Reverse(fields.GetValueOrDefault(330) ?? []))
            {
                if (sub == 0) throw new DngOracleError("null SubIFD");
                pending.Add(sub);
            }
            var photo = fields.GetValueOrDefault(262) ?? [];
            if (photo.Count == 1 && photo[0] == 32803)
            {
                var offsets = fields.GetValueOrDefault(273) ?? [];
                var lengths = fields.GetValueOrDefault(279) ?? [];
                if (offsets.Count == 0 || offsets.Count != lengths.Count) throw new DngOracleError("missing or mismatched raw strips");
                foreach (var (start, length) in offsets.Zip(lengths))
                {
                    if (length == 0 || start + length > data.Length) throw new DngOracleError("invalid raw strip range");
                    result.Add(PyBytes.Slice(data, start, start + length));
                }
            }
            else if (photo.Contains(32803))
            {
                throw new DngOracleError("ambiguous photometric interpretation");
            }
        }
        if (result.Count == 0) throw new DngOracleError("no CFA raw strips found");
        return result;
    }

    /// <summary>Return ordered lengths and SHA-256 hashes without conflating strip boundaries.</summary>
    public static List<object?> RawStripFingerprints(byte[] data) =>
        RawStrips(data).Select(part => (object?)PyDict.Of(("length", (long)part.Length), ("sha256", JpegOwnedFixtures.Sha256(part)))).ToList();
}

// Bounded extractor for an authored JPEG thumbnail in the first chained IFD, formerly
// tools/reference/dng_thumbnail_oracle.py.
internal static class DngThumbnailOracle
{
    public static byte[]? Ifd1Jpeg(byte[] data)
    {
        if (!TiffReader.HasHeader(data)) throw new ThumbnailOracleError("invalid TIFF header");
        var reader = new TiffReader(data, message => new ThumbnailOracleError(message), "out-of-bounds value");
        long Num(long pos, int size) => reader.Number(pos, size);

        (long Count, long End) Table(long offset)
        {
            if (offset < 8) throw new ThumbnailOracleError("invalid IFD offset");
            long count = Num(offset, 2);
            long end = offset + 2 + count * 12;
            if (count > 4096 || end + 4 > data.Length) throw new ThumbnailOracleError("invalid IFD");
            return (count, end);
        }

        if (Num(2, 2) != 42) throw new ThumbnailOracleError("not classic TIFF");
        var (_, end) = Table(Num(4, 4));
        long ifd1 = Num(end, 4);
        if (ifd1 == 0) return null;
        var (count, _) = Table(ifd1);
        var fields = new Dictionary<long, long>();
        for (long i = 0; i < count; i++)
        {
            long entry = ifd1 + 2 + i * 12;
            long tag = Num(entry, 2);
            if (tag is not (259 or 513 or 514)) continue;
            long typ = Num(entry + 2, 2), n = Num(entry + 4, 4);
            if (fields.ContainsKey(tag) || typ is not (3 or 4) || n != 1) throw new ThumbnailOracleError("unsupported or duplicate thumbnail field");
            fields[tag] = Num(entry + 8, typ == 3 ? 2 : 4);
        }
        if (!(fields.ContainsKey(513) || fields.ContainsKey(514))) return null;
        if (!fields.TryGetValue(259, out long compression) || compression != 6 || !fields.ContainsKey(513) || !fields.ContainsKey(514))
            throw new ThumbnailOracleError("incomplete JPEG thumbnail");
        long start = fields[513], size = fields[514];
        if (size == 0 || start + size > data.Length) throw new ThumbnailOracleError("out-of-bounds JPEG thumbnail");
        return PyBytes.Slice(data, start, start + size);
    }
}

// Bounded byte-level IFD0 metadata extraction for B6 DNG observations, formerly tools/reference/dng_metadata_oracle.py.
//
// This only extracts selected classic-TIFF tags as stored. It does not validate ICC, parse XMP/IIM, traverse Exif
// IFDs, or interpret the entire DNG. Duplicate selected tags remain distinct rather than being silently coalesced.
internal static class DngMetadataOracle
{
    // Exiv2 may serialize IPTC as LONG, so retain raw bytes and type/count.
    private static readonly Dictionary<long, long> Sizes = new() { [1] = 1, [2] = 1, [3] = 2, [4] = 4, [5] = 8, [7] = 1, [9] = 4, [10] = 8 };

    /// <summary>Return selected IFD0 tag records in on-disk order, with exact value bytes.</summary>
    public static List<PyDict> RootValues(byte[] data, IReadOnlyCollection<long> selected)
    {
        if (!TiffReader.HasHeader(data)) throw new MetadataOracleError("invalid TIFF header");
        var reader = new TiffReader(data, message => new MetadataOracleError(message), "out-of-bounds TIFF value");
        long Number(long pos, int size) => reader.Number(pos, size);
        if (Number(2, 2) != 42) throw new MetadataOracleError("not classic TIFF");
        long offset = Number(4, 4);
        if (offset < 8) throw new MetadataOracleError("invalid IFD0 offset");
        long count = Number(offset, 2);
        if (count > 4096 || offset + 2 + 12 * count + 4 > data.Length) throw new MetadataOracleError("invalid IFD0 table");
        var result = new List<PyDict>();
        for (long i = 0; i < count; i++)
        {
            long entry = offset + 2 + 12 * i;
            long tag = Number(entry, 2);
            if (!selected.Contains(tag)) continue;
            long typ = Number(entry + 2, 2), elements = Number(entry + 4, 4);
            if (!Sizes.TryGetValue(typ, out long unit) || elements == 0 || elements > data.Length) throw new MetadataOracleError("unsupported TIFF type or count");
            long length = unit * elements;
            long at = length <= 4 ? entry + 8 : Number(entry + 8, 4);
            if (at + length > data.Length) throw new MetadataOracleError("out-of-bounds TIFF payload");
            result.Add(PyDict.Of(("tag", tag), ("type", typ), ("count", elements), ("bytes", PyBytes.Slice(data, at, at + length))));
        }
        return result;
    }
}
