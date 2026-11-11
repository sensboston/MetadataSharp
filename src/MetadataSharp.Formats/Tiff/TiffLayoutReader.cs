using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Tiff;

internal sealed class TiffLayoutReader(Stream source, TiffReadOptions options, bool asynchronous, CancellationToken token)
{
    private readonly long length = source.Length;
    private readonly List<TiffDirectoryInfo> directories = [];
    private readonly List<TiffAccountedRange> ranges = [];
    private readonly List<TiffLayoutIssue> issues = [];
    private readonly Dictionary<TiffByteRange, (ushort Type, uint Count)> values = [];
    private readonly Dictionary<uint, List<uint>> edges = [];
    private readonly Queue<uint> pending = [];
    private readonly HashSet<uint> discovered = [];
    private Endianness order;
    private long retained;
    private int entries, links, segments;
    private static MetadataException Bad(string text = "Malformed standalone TIFF.") => new(MetadataErrorCode.InvalidTiff, text);
    private static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "TIFF accounting exceeds its configured budget.");
    private void Charge(long bytes) { if (bytes > options.Limits.MaxRetainedMetadataBytes - retained) throw Limit(); retained += bytes; }
    private ushort U16(byte[] bytes, int at = 0) => order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at));
    private uint U32(byte[] bytes, int at = 0) => order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));
    private void Bounds(long start, long count, bool aligned = false)
    {
        // TIFF 6.0 recommends word-aligned directories and values; files that ignore it are read as they are,
        // since every write appends its own aligned structures and never moves the source bytes.
        _ = aligned;
        if (start < 0 || count < 0 || start > length || count > length - start) throw Bad("Invalid TIFF range.");
    }
    private async Task<byte[]> At(long start, int count)
    {
        token.ThrowIfCancellationRequested(); Bounds(start, count); source.Position = start;
        byte[] bytes = new byte[count];
        await ContainerIo.Read(source, bytes, asynchronous, token).ConfigureAwait(false);
        return bytes;
    }
    private void Range(TiffRangeKind kind, uint? owner, ushort? tag, long start, long count, ushort type = 0, uint n = 0)
    {
        Bounds(start, count); if (count == 0) return;
        var range = new TiffByteRange(start, count);
        if (kind == TiffRangeKind.FieldValue)
        {
            if (values.TryGetValue(range, out var shape)) { if (shape != (type, n)) throw Bad("Conflicting shared field storage."); return; }

        }
        if (ranges.Count >= options.MaxAccountedRanges) throw Limit(); Charge(64);
        if (kind == TiffRangeKind.FieldValue) values.Add(range, (type, n));
        ranges.Add(new(kind, owner, tag, range));
    }
    private void Issue(TiffLayoutIssueCode code, uint owner, ushort? tag, string message)
    { Charge(96); issues.Add(new(code, owner, tag, message)); }
    private void Discover(uint target)
    {
        if (target == 0) throw Bad("Null directory target."); Bounds(target, 2, true);
        if (discovered.Contains(target)) return;
        if (discovered.Count >= options.Limits.MaxDirectories) throw Limit(); Charge(128);
        discovered.Add(target); pending.Enqueue(target);
    }
    private void Link(List<TiffDirectoryLink> result, uint owner, uint target, long pointer, TiffDirectoryLinkKind kind, ushort? tag)
    {
        if (links >= options.MaxDirectoryLinks) throw Limit(); links++; Charge(48);
        Discover(target); edges[owner].Add(target); result.Add(new(kind, tag, pointer, target));
    }
    internal static async Task<TiffLayout> Read(Stream source, TiffReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source); token.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek || source.Position != 0) throw new ArgumentException("TIFF source must be readable, seekable and at zero.", nameof(source));
        var reader = new TiffLayoutReader(source, options, asynchronous, token);
        return await reader.Run().ConfigureAwait(false);
    }
    private async Task<TiffLayout> Run()
    {
        if (length > options.Limits.MaxInputBytes) throw Limit();
        if (length < 8) throw Bad("Truncated TIFF header.");
        if (length > 4294967296L) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Files larger than 4 GiB are outside the classic TIFF profile.");
        byte[] header = await At(0, 8).ConfigureAwait(false);
        order = header[0] == 0x49 && header[1] == 0x49 ? Endianness.LittleEndian : header[0] == 0x4d && header[1] == 0x4d ? Endianness.BigEndian : throw Bad("Invalid TIFF byte order.");
        ushort magic = U16(header, 2);
        if (magic == 43) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "BigTIFF is unsupported.");
        if (magic != 42) throw Bad("Invalid TIFF magic.");
        uint first = U32(header, 4); Range(TiffRangeKind.Header, null, null, 0, 8); Discover(first);
        while (pending.Count != 0) { token.ThrowIfCancellationRequested(); await Directory(pending.Dequeue()).ConfigureAwait(false); }
        Graph(first);
        var sorted = ranges.OrderBy(r => r.Range.Offset).ThenBy(r => r.Range.Length).ToArray();
        for (int i = 1; i < sorted.Length; i++)
        { token.ThrowIfCancellationRequested(); if (sorted[i].Range.Offset < sorted[i - 1].Range.Offset + sorted[i - 1].Range.Length) throw Bad("Overlapping TIFF ranges."); }
        if (directories.All(d => d.Image is null)) Issue(TiffLayoutIssueCode.MissingImageLayout, first, null, "No candidate image layout found.");
        token.ThrowIfCancellationRequested(); return new(order, length, first, directories.ToArray(), sorted, issues.ToArray());
    }
    private async Task Directory(uint offset)
    {
        edges.Add(offset, []); int count = U16(await At(offset, 2).ConfigureAwait(false));
        if (count == 0) throw Bad("TIFF6 requires at least one directory entry.");
        if (count > options.Limits.MaxEntriesPerDirectory || count > options.Limits.MaxTotalEntries - entries) throw Limit(); entries += count;
        long size = 6L + 12L * count; Bounds(offset, size, true); Range(TiffRangeKind.Directory, offset, null, offset, size);
        var fields = new List<TiffFieldInfo>(); var byTag = new Dictionary<ushort, TiffFieldInfo>(); var resultLinks = new List<TiffDirectoryLink>();
        for (int i = 0; i < count; i++)
        {
            Charge(64); long start = offset + 2L + 12L * i; byte[] raw = await At(start, 12).ConfigureAwait(false);
            ushort tag = U16(raw), type = U16(raw, 2); uint n = U32(raw, 4);
            if (tag == 50706) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "DNG is unsupported.");
            int width = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 => 4, 5 or 10 or 12 => 8, _ => throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unknown TIFF field width/type.") };
            long bytes = (long)n * width; if (bytes > options.Limits.MaxValueBytes) throw Limit();
            bool inline = bytes <= 4; long location = inline ? start + 8 : U32(raw, 8); Bounds(location, bytes, !inline);
            var field = new TiffFieldInfo(tag, type, n, inline, new(location, bytes));
            if (!byTag.TryAdd(tag, field)) throw Bad("Duplicate TIFF tag."); fields.Add(field);
            if (!inline) Range(TiffRangeKind.FieldValue, offset, tag, location, bytes, type, n);
            if (tag is 330 or 34665 or 34853 or 40965)
            {
                if (type != 4 || n == 0 || (tag != 330 && n != 1)) throw Bad("Invalid IFD pointer shape.");
                if (n > options.MaxDirectoryLinks - links) throw Limit();
                uint[] targets = await Numbers(field, false).ConfigureAwait(false);
                var kind = tag switch { 330 => TiffDirectoryLinkKind.SubIfd, 34665 => TiffDirectoryLinkKind.Exif, 34853 => TiffDirectoryLinkKind.Gps, _ => TiffDirectoryLinkKind.Interoperability };
                for (int t = 0; t < targets.Length; t++) Link(resultLinks, offset, targets[t], location + 4L * t, kind, tag);
            }
            else if (tag is 519 or 520 or 521) Issue(TiffLayoutIssueCode.UnsupportedPointerLayout, offset, tag, "Old JPEG table target lengths are not inferred.");
            else if (!Known(tag)) Issue(TiffLayoutIssueCode.UnknownFieldSemantics, offset, tag, "Opaque field semantics; private pointers cannot be ruled out.");
        }
        long nextLocation = offset + 2L + 12L * count; uint next = U32(await At(nextLocation, 4).ConfigureAwait(false));
        if (next != 0) Link(resultLinks, offset, next, nextLocation, TiffDirectoryLinkKind.NextIfd, null);
        TiffImageLayout? image = await Image(offset, byTag).ConfigureAwait(false);
        directories.Add(new(offset, new(offset, size), fields.AsReadOnly(), resultLinks.AsReadOnly(), image));
    }
    // Only numeric TIFF6 storage/scalar fields with reviewed nonprivate semantics.
    private static bool Known(ushort tag) => tag is 254 or 255 or 256 or 257 or 258 or 259 or 262 or 266 or 269 or 270 or 271 or 272 or 273 or 274 or 277 or 278 or 279 or 280 or 281 or 282 or 283 or 284 or 285 or 286 or 287 or 296 or 297 or 305 or 306 or 315 or 316 or 317 or 320 or 322 or 323 or 324 or 325 or 338 or 339 or 347 or 513 or 514;
    private async Task<uint[]> Numbers(TiffFieldInfo field, bool shorts = true)
    {
        if (field.FieldType != 4 && !(shorts && field.FieldType == 3)) throw Bad("Invalid numeric storage field type.");
        Charge(4L * field.Count); byte[] bytes = await At(field.ValueRange.Offset, (int)field.ValueRange.Length).ConfigureAwait(false);
        uint[] result = new uint[(int)field.Count]; int width = field.FieldType == 3 ? 2 : 4;
        for (int i = 0; i < result.Length; i++) { if ((i & 1023) == 0) token.ThrowIfCancellationRequested(); result[i] = width == 2 ? U16(bytes, i * width) : U32(bytes, i * width); }
        return result;
    }
    private async Task<uint> Scalar(Dictionary<ushort, TiffFieldInfo> fields, ushort tag, uint? fallback = null, bool shorts = true)
    {
        if (!fields.TryGetValue(tag, out var field)) return fallback ?? throw Bad("Required image scalar missing.");
        if (field.Count != 1 || (tag is 259 or 262 or 277 or 284 && field.FieldType != 3)) throw Bad("Invalid scalar shape."); return (await Numbers(field, shorts).ConfigureAwait(false))[0];
    }
    private async Task<TiffImageLayout?> Image(uint owner, Dictionary<ushort, TiffFieldInfo> f)
    {
        bool strips = f.ContainsKey(273) || f.ContainsKey(279) || f.ContainsKey(278);
        bool tiles = f.ContainsKey(322) || f.ContainsKey(323) || f.ContainsKey(324) || f.ContainsKey(325);
        bool preview = f.ContainsKey(513) || f.ContainsKey(514);
        if (!strips && !tiles && !preview && !f.ContainsKey(256) && !f.ContainsKey(257)) return null;
        if (strips && tiles) throw Bad("Conflicting strip/tile layouts.");
        TiffByteRange? jpeg = await Preview(owner, f, preview).ConfigureAwait(false);
        if (!strips && !tiles && (!f.ContainsKey(256) || !f.ContainsKey(257)))
        {
            if (f.ContainsKey(259))
            {
                uint c = await Scalar(f, 259).ConfigureAwait(false);
                if (c is not (1 or 5 or 7 or 8 or 32946 or 32773)) Issue(TiffLayoutIssueCode.UnsupportedCompression, owner, 259, "Compression is outside the accounting profile.");
            }
            Issue(TiffLayoutIssueCode.UnsupportedPointerLayout, owner, null, "Preview-only directory has no dimensioned strip/tile layout; extent is accounted globally.");
            return null;
        }
        uint w = await Scalar(f, 256).ConfigureAwait(false), h = await Scalar(f, 257).ConfigureAwait(false);
        if (w == 0 || h == 0) throw Bad("Zero image dimension.");
        uint compression = await Scalar(f, 259, 1).ConfigureAwait(false);
        if (compression > ushort.MaxValue) throw Bad("Invalid compression shape.");
        bool supported = !issues.Any(i => i.DirectoryOffset == owner);
        void Unsupported(TiffLayoutIssueCode code, ushort? tag, string text) { supported = false; Issue(code, owner, tag, text); }
        if (compression is not (1 or 5 or 7 or 8 or 32946 or 32773)) Unsupported(TiffLayoutIssueCode.UnsupportedCompression, 259, "Compression is outside the accounting profile.");
        uint spp = await Scalar(f, 277, 1).ConfigureAwait(false), planar = await Scalar(f, 284, 1).ConfigureAwait(false), photo = await Scalar(f, 262, uint.MaxValue).ConfigureAwait(false);
        if (spp == 0 || spp > ushort.MaxValue) throw Bad("Invalid sample count.");
        bool hasBits = f.TryGetValue(258, out var bf);
        foreach (ushort tag in new ushort[] { 258, 338, 339 }) if (f.TryGetValue(tag, out var field) && field.FieldType != 3) throw Bad("Sample fields require SHORT values.");
        uint[] bits = hasBits ? await Numbers(bf!).ConfigureAwait(false) : [1];
        uint[] sample = f.TryGetValue(339, out var sf) ? await Numbers(sf).ConfigureAwait(false) : [];
        uint[] extra = f.TryGetValue(338, out var ef) ? await Numbers(ef).ConfigureAwait(false) : [];
        if ((hasBits && bits.Length != spp) || (f.ContainsKey(339) && sample.Length != spp)) throw Bad("Sample array counts disagree.");
        uint baseSamples = photo is 0 or 1 ? 1u : photo == 2 ? 3u : 0;
        if (baseSamples == 0 || (spp != baseSamples && !(spp == baseSamples + 1 && extra.Length == 1 && extra[0] is 1 or 2)) ||
            (spp == baseSamples && extra.Length != 0) || bits.Any(b => b is not (8 or 16)) || bits.Distinct().Count() != 1 || sample.Any(s => s != 1) || planar is not (1 or 2) || f.ContainsKey(530))
            Unsupported(TiffLayoutIssueCode.UnsupportedSampleLayout, null, "Sample/photometric layout is outside the accounting profile.");
        var imageRanges = new List<TiffByteRange>();
        if (strips || tiles)
        {
            ushort offsets = (ushort)(tiles ? 324 : 273), counts = (ushort)(tiles ? 325 : 279);
            if (!f.TryGetValue(offsets, out var of) || !f.TryGetValue(counts, out var cf) || of.Count == 0 || of.Count != cf.Count) throw Bad("Unpaired image arrays.");
            if (of.Count > options.Limits.MaxSegments - segments) throw Limit();
            uint[] starts = await Numbers(of, !tiles).ConfigureAwait(false), lengths = await Numbers(cf).ConfigureAwait(false);
            ulong across = 1, down;
            if (tiles)
            {
                uint tw = await Scalar(f, 322).ConfigureAwait(false), th = await Scalar(f, 323).ConfigureAwait(false);
                if (tw == 0 || th == 0) throw Bad("Zero tile dimensions.");
                if (tw % 16 != 0 || th % 16 != 0) Unsupported(TiffLayoutIssueCode.UnsupportedSampleLayout, 322, "TIFF6 tile dimensions must be multiples of sixteen.");
                across = ((ulong)w + tw - 1) / tw; down = ((ulong)h + th - 1) / th;
            }
            else { uint rows = await Scalar(f, 278, uint.MaxValue).ConfigureAwait(false); if (rows == 0) throw Bad("Zero rows per strip."); down = ((ulong)h + rows - 1) / rows; }
            if (planar is 1 or 2)
            {
                ulong expected = across * down, planes = planar == 2 ? spp : 1;
                if (expected > (ulong)options.Limits.MaxSegments / planes) throw Limit();
                if (expected * planes != (ulong)starts.Length) throw Bad("Image segment count disagrees with grid/planes.");
            }
            for (int i = 0; i < starts.Length; i++)
            {
                token.ThrowIfCancellationRequested(); if (starts[i] == 0 || lengths[i] == 0) throw Bad("Empty image segment.");
                AddSegment(); Charge(16); Range(tiles ? TiffRangeKind.Tile : TiffRangeKind.Strip, owner, offsets, starts[i], lengths[i]); imageRanges.Add(new(starts[i], lengths[i]));
            }
        }
        else Unsupported(TiffLayoutIssueCode.UnsupportedPointerLayout, null, "Candidate image has no supported strip/tile arrays.");
        return new(w, h, (ushort)compression, tiles ? TiffStorageKind.Tiles : TiffStorageKind.Strips, imageRanges.AsReadOnly(), jpeg, supported);
    }
    private async Task<TiffByteRange?> Preview(uint owner, Dictionary<ushort, TiffFieldInfo> fields, bool present)
    {
        if (!present) return null;
        uint start = await Scalar(fields, 513, shorts: false).ConfigureAwait(false), bytes = await Scalar(fields, 514, shorts: false).ConfigureAwait(false);
        if (start == 0 || bytes == 0) throw Bad("Invalid JPEG preview extent.");
        if (bytes > options.Limits.MaxThumbnailBytes) throw Limit();
        AddSegment(); Range(TiffRangeKind.JpegPreview, owner, 513, start, bytes); return new(start, bytes);
    }
    private void AddSegment() { if (segments >= options.Limits.MaxSegments) throw Limit(); segments++; }
    private void Graph(uint first)
    {
        var degree = discovered.ToDictionary(v => v, _ => 0); var depth = discovered.ToDictionary(v => v, _ => 1);
        foreach (var children in edges.Values) foreach (uint child in children) degree[child]++;
        var ready = new Queue<uint>(degree.Where(p => p.Value == 0).Select(p => p.Key)); int visited = 0;
        while (ready.Count != 0)
        {
            token.ThrowIfCancellationRequested(); uint current = ready.Dequeue(); visited++;
            if (depth[current] > options.Limits.MaxDirectoryDepth) throw Limit();
            foreach (uint child in edges[current]) { depth[child] = Math.Max(depth[child], depth[current] + 1); if (--degree[child] == 0) ready.Enqueue(child); }
        }
        if (visited != discovered.Count || degree[first] != 0) throw Bad("Cyclic TIFF directory graph.");
    }
}
