using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Tiff;

internal sealed record TiffAppendPlan(byte[] Root, byte[] Tail);

/// <summary>Plans a replacement active graph without moving any original storage.</summary>
internal sealed class TiffAppendPlanner(TiffLayout layout, TiffEditOptions options,
    Func<long, int, Task<byte[]>> read, CancellationToken token)
{
    // Track the next output offset while planning appended TIFF ranges.
    private readonly Endianness order = layout.ByteOrder;
    private long retained;
    private long cursor = layout.SourceLength;
    private readonly Dictionary<uint, uint> map = [];
    private sealed record Field(ushort Tag, byte[] Entry, byte[]? External);
    private sealed record Node(TiffDirectoryInfo Source, List<Field> Fields);
    // Convert budget exhaustion into the format-specific LimitExceeded error.
    private static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "TIFF write plan exceeds its finite budget/address space.");
    private static MetadataException Unsupported() => new(MetadataErrorCode.UnsupportedFeature, "TIFF field/layout is outside the closed write schema.");
    // Enforce the configured TIFF/DNG count, depth, or retained-byte budget before keeping more data.
    private void Charge(long bytes) { if (bytes > options.ReadOptions.Limits.MaxRetainedMetadataBytes - retained) throw Limit(); retained += bytes; }
    private ushort U16(byte[] b) => order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);
    private void Put16(byte[] b, int at, ushort n) { if (order == Endianness.LittleEndian) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), n); else BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), n); }
    private void Put32(byte[] b, int at, uint n) { if (order == Endianness.LittleEndian) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), n); else BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), n); }
    private byte[] Entry(ushort tag, ushort type, uint count)
    { byte[] bytes = new byte[12]; Put16(bytes, 0, tag); Put16(bytes, 2, type); Put32(bytes, 4, count); return bytes; }
    private long Ceiling => Math.Min(4294967296L, Math.Min(options.MaxOutputBytes, options.ReadOptions.Limits.MaxInputBytes));
    // Keep Reserve focused on TIFF/DNG and its format-specific validation contract.
    private uint Reserve(long bytes)
    {
        cursor = (cursor + 1) & ~1L;
        if (cursor > uint.MaxValue || bytes > Ceiling - cursor) throw Limit();
        uint start = (uint)cursor; cursor += bytes; return start;
    }
    internal async Task<TiffAppendPlan?> Create(Dictionary<TiffFieldKey, ExifValue?> changes)
    {
        if (layout.SourceLength > Ceiling) throw Limit();
        if (changes.Count == 0) return null;
        var effective = new Dictionary<TiffFieldKey, ExifValue?>();
        foreach (var pair in changes)
        {
            token.ThrowIfCancellationRequested();
            TiffDirectoryInfo d = layout.Directories.FirstOrDefault(d => d.Offset == pair.Key.DirectoryOffset) ?? throw new ArgumentException("Edit key does not identify a reachable source IFD.");
            TiffFieldInfo? f = d.Fields.FirstOrDefault(f => f.TagId == pair.Key.TagId);
            Charge(32L + (pair.Value?.ByteLength ?? 0));
            if (pair.Value is null) { if (f is not null) effective.Add(pair.Key, null); continue; }
            if (pair.Value.ByteLength > options.ReadOptions.Limits.MaxValueBytes) throw Limit();
            if (f is not null)
            {
                if (!TiffEditPolicy.Shape(f, 1, 0, 0)) throw Unsupported();
                byte[] old = await read(f.ValueRange.Offset, (int)f.ValueRange.Length).ConfigureAwait(false);
                ExifValue oldValue = ExifValue.FromRaw((TiffFieldType)f.FieldType, f.Count, order, old);
                if (!TiffEditPolicy.Value(f.TagId, oldValue)) throw Unsupported();
                if (f.FieldType == (ushort)pair.Value.FieldType && f.Count == pair.Value.Count && old.AsSpan().SequenceEqual(TiffEditPolicy.Bytes(pair.Value, order))) continue;
            }
            effective.Add(pair.Key, pair.Value);
        }
        if (effective.Count == 0) return null;
        Charge(layout.Directories.Sum(d => 128L + d.Fields.Sum(f => 64L + 4L * f.Count) + 48L * d.Links.Count + 16L * (d.Image?.Segments.Count ?? 0)) + 64L * layout.Ranges.Count + 96L * layout.Issues.Count);
        if (layout.Issues.Any(i => i.Code is TiffLayoutIssueCode.UnknownFieldSemantics or TiffLayoutIssueCode.UnsupportedPointerLayout))
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "Opaque/private pointers prevent graph replacement.");
        if (!layout.IsSupported) throw Unsupported();
        var nodes = new List<Node>(); int total = 0;
        foreach (var d in layout.Directories)
        {
            token.ThrowIfCancellationRequested(); Charge(176);
            var fields = new List<Field>();
            TiffFieldInfo? spp = d.Fields.FirstOrDefault(f => f.TagId == 277); uint samples = 1;
            if (spp is not null)
            { if (spp.FieldType != 3 || spp.Count != 1) throw Unsupported(); samples = U16(await read(spp.ValueRange.Offset, 2).ConfigureAwait(false)); }
            uint extras = d.Image is null ? 0 : d.Fields.FirstOrDefault(f => f.TagId == 338)?.Count ?? 0;
            for (int i = 0; i < d.Fields.Count; i++)
            {
                token.ThrowIfCancellationRequested(); TiffFieldInfo f = d.Fields[i]; var key = new TiffFieldKey(d.Offset, f.TagId);
                if (effective.TryGetValue(key, out var replacement))
                { if (replacement is not null) fields.Add(Changed(key.TagId, replacement)); continue; }
                if (!TiffEditPolicy.Shape(f, samples, extras, d.Image?.Segments.Count ?? 0))
                {
                    if (f.TagId is 320 or 347) throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "Opaque extension is outside the write schema.");
                    throw Unsupported();
                }
                Charge(64);
                byte[] entry = await read(d.Offset + 2L + 12L * i, 12).ConfigureAwait(false);
                byte[]? external = null;
                if (TiffEditPolicy.Pointer(f.TagId))
                {
                    Charge(f.ValueRange.Length); byte[] targets = new byte[(int)f.ValueRange.Length];
                    if (targets.Length <= 4) entry.AsSpan(8, 4).Clear(); else external = targets;
                }
                fields.Add(new(f.TagId, entry, external));
            }
            foreach (var pair in effective.Where(p => p.Key.DirectoryOffset == d.Offset && p.Value is not null && !d.Fields.Any(f => f.TagId == p.Key.TagId))) fields.Add(Changed(pair.Key.TagId, pair.Value!));
            if (fields.Count == 0) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Editing would empty an IFD; directory deletion is unsupported.");
            if (fields.Count > ushort.MaxValue || fields.Count > options.ReadOptions.Limits.MaxEntriesPerDirectory || fields.Count > options.ReadOptions.Limits.MaxTotalEntries - total) throw Limit();
            total += fields.Count; fields.Sort((a, b) => a.Tag.CompareTo(b.Tag));
            map.Add(d.Offset, Reserve(6L + 12L * fields.Count)); nodes.Add(new(d, fields));
        }
        foreach (var node in nodes)
        {
            foreach (Field f in node.Fields)
            {
                token.ThrowIfCancellationRequested();
                if (TiffEditPolicy.Pointer(f.Tag))
                {
                    var links = node.Source.Links.Where(l => l.TagId == f.Tag).OrderBy(l => l.PointerOffset).ToArray();
                    if (f.External is null) Put32(f.Entry, 8, map[links.Single().TargetIfdOffset]);
                    else for (int i = 0; i < links.Length; i++) Put32(f.External, i * 4, map[links[i].TargetIfdOffset]);
                }
                if (f.External is not null) Put32(f.Entry, 8, Reserve(f.External.Length));
            }
        }
        // Reopenability includes the active range ceiling, not just byte length.
        var originalValues = new HashSet<TiffByteRange>(); long externalValues = 0;
        foreach (var node in nodes) foreach (Field f in node.Fields)
        {
            if (f.External is not null) { externalValues++; continue; }
            if (TiffEditPolicy.Pointer(f.Tag) || effective.ContainsKey(new(node.Source.Offset, f.Tag))) continue;
            TiffFieldInfo original = node.Source.Fields.First(field => field.TagId == f.Tag);
            if (!original.IsInline) originalValues.Add(original.ValueRange);
        }
        long rangeCount = 1L + nodes.Count + externalValues + originalValues.Count + layout.Ranges.Count(r => r.Kind is TiffRangeKind.Strip or TiffRangeKind.Tile or TiffRangeKind.JpegPreview);
        if (rangeCount > options.ReadOptions.MaxAccountedRanges) throw Limit();
        long size = cursor - layout.SourceLength; Charge(size);
        byte[] tail = new byte[(int)size];
        foreach (var node in nodes)
        {
            token.ThrowIfCancellationRequested(); int at = (int)(map[node.Source.Offset] - layout.SourceLength); Put16(tail, at, (ushort)node.Fields.Count);
            for (int i = 0; i < node.Fields.Count; i++)
            {
                Field f = node.Fields[i]; f.Entry.CopyTo(tail, at + 2 + 12 * i);
                if (f.External is not null)
                {
                    uint position = order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(f.Entry.AsSpan(8)) : BinaryPrimitives.ReadUInt32BigEndian(f.Entry.AsSpan(8));
                    f.External.CopyTo(tail, (int)(position - layout.SourceLength));
                }
            }
            TiffDirectoryLink? next = node.Source.Links.FirstOrDefault(l => l.Kind == TiffDirectoryLinkKind.NextIfd);
            Put32(tail, at + 2 + 12 * node.Fields.Count, next is null ? 0 : map[next.TargetIfdOffset]);
        }
        byte[] root = new byte[4]; Put32(root, 0, map[layout.FirstIfdOffset]); token.ThrowIfCancellationRequested(); return new(root, tail);
    }
    // Keep Changed focused on TIFF/DNG and its format-specific validation contract.
    private Field Changed(ushort tag, ExifValue value)
    {
        Charge(64L + value.ByteLength); byte[] bytes = TiffEditPolicy.Bytes(value, order);
        byte[] entry = Entry(tag, (ushort)value.FieldType, value.Count);
        if (bytes.Length <= 4) { bytes.CopyTo(entry, 8); return new(tag, entry, null); }
        return new(tag, entry, bytes);
    }
}
