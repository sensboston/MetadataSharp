using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Dng;

/// <summary>Dedicated internal DNG framing/profile reader. TIFF's refusal of DNG stays intact; only neutral primitives are shared.</summary>
internal sealed class DngReader(Stream source, DngReadOptions options, bool asynchronous, CancellationToken token)
{
    private const ushort TagDngVersion = 50706, TagDngBackwardVersion = 50707, TagUniqueCameraModel = 50708;
    private const ushort TagColorMatrix1 = 50721, TagCalibrationIlluminant1 = 50778, TagAsShotNeutral = 50728;
    private const ushort TagBlackLevelRepeatDim = 50713, TagBlackLevel = 50714, TagWhiteLevel = 50717;
    private const ushort TagActiveArea = 50829, TagDefaultCropOrigin = 50719, TagDefaultCropSize = 50720;
    private const ushort TagCfaRepeatPatternDim = 33421, TagCfaPattern = 33422, TagCfaPlaneColor = 50710, TagCfaLayout = 50711;

    private static readonly HashSet<ushort> CommonTags = [274, 305, 315];
    private static readonly HashSet<ushort> IdentityTags = [TagDngVersion, TagDngBackwardVersion, TagUniqueCameraModel, TagColorMatrix1, TagCalibrationIlluminant1, TagAsShotNeutral, TagDefaultCropOrigin, TagDefaultCropSize];
    private static readonly HashSet<ushort> RawImageTags = [254, 256, 257, 258, 259, 262, 273, 274, 277, 278, 279, 284, 305, 315, 339, TagCfaRepeatPatternDim, TagCfaPattern, TagCfaPlaneColor, TagCfaLayout, TagBlackLevelRepeatDim, TagBlackLevel, TagWhiteLevel, TagActiveArea];
    private static readonly HashSet<ushort> RawRoleTags = [.. RawImageTags, .. IdentityTags, TagActiveArea];
    private static readonly HashSet<ushort> ThumbnailImageTags = [254, 256, 257, 258, 259, 262, 273, 274, 277, 278, 279, 284, 305, 315, 330, 339];
    private static readonly HashSet<ushort> ThumbnailIdentityTags = [TagDngVersion, TagDngBackwardVersion, TagUniqueCameraModel, TagColorMatrix1, TagCalibrationIlluminant1, TagAsShotNeutral];
    private static readonly HashSet<ushort> ThumbnailRoleTags = [.. ThumbnailImageTags, .. CommonTags, .. ThumbnailIdentityTags];
    private static readonly HashSet<ushort> TileTags = [322, 323, 324, 325];

    private readonly long length = source.Length;
    private readonly List<TiffAccountedRange> ranges = [];
    private readonly List<DngLayoutIssue> issues = [];
    private readonly Dictionary<TiffByteRange, (ushort Type, uint Count)> values = [];
    private readonly Dictionary<uint, List<uint>> edges = [];
    private readonly Queue<uint> pending = [];
    private readonly HashSet<uint> discovered = [];
    private Endianness order;
    private long retained;
    private int entries, links, segments;

    private static MetadataException Bad(string text = "Malformed DNG framing.") => new(MetadataErrorCode.InvalidTiff, text);
    private static MetadataException InvalidDng(string text) => new(MetadataErrorCode.InvalidDng, text);
    // Map DNG accounting exhaustion to one stable LimitExceeded exception.
    private static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "DNG accounting exceeds its configured budget.");
    // Charge retained bytes before keeping parsed TIFF/DNG metadata in memory.
    private void Charge(long bytes) { if (bytes > options.TiffOptions.Limits.MaxRetainedMetadataBytes - retained) throw Limit(); retained += bytes; }
    private ushort U16(byte[] bytes, int at = 0) => order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at));
    private uint U32(byte[] bytes, int at = 0) => order == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));
    private int S32(byte[] bytes, int at = 0) => order == Endianness.LittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)) : BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at));
    // Reject offsets, counts, and alignment that escape the source stream.
    private void Bounds(long start, long count, bool aligned = false)
    { if (start < 0 || count < 0 || start > length || count > length - start || (aligned && (start & 1) != 0)) throw Bad("Invalid DNG range/alignment."); }
    // Read an exact source range after checking bounds and charging the retention budget.
    private async Task<byte[]> At(long start, int count)
    {
        token.ThrowIfCancellationRequested(); Bounds(start, count); source.Position = start;
        byte[] bytes = new byte[count];
        await ContainerIo.Read(source, bytes, asynchronous, token).ConfigureAwait(false);
        return bytes;
    }
    // Record each TIFF range with its owner and tag for later overlap analysis.
    private void Range(TiffRangeKind kind, uint? owner, ushort? tag, long start, long count, ushort type = 0, uint n = 0)
    {
        Bounds(start, count); if (count == 0) return;
        var range = new TiffByteRange(start, count);
        if (kind == TiffRangeKind.FieldValue)
        {
            if (values.TryGetValue(range, out var shape)) { if (shape != (type, n)) throw Bad("Conflicting shared field storage."); return; }
        }
        if (ranges.Count >= options.TiffOptions.MaxAccountedRanges) throw Limit();
        Charge(64);
        if (kind == TiffRangeKind.FieldValue) values.Add(range, (type, n));
        ranges.Add(new(kind, owner, tag, range));
    }
    private void Issue(DngLayoutIssueCode code, uint owner, ushort? tag, string message)
    { Charge(96); issues.Add(new(code, owner, tag, message)); }
    // Traverse linked TIFF directories once, rejecting cycles and out-of-range pointers.
    private void Discover(uint target)
    {
        if (target == 0) throw Bad("Null directory target.");
        Bounds(target, 2, true);
        if (discovered.Contains(target)) return;
        if (discovered.Count >= options.TiffOptions.Limits.MaxDirectories) throw Limit();
        Charge(128); discovered.Add(target); pending.Enqueue(target);
    }
    // Validate and record a directory pointer with its owning tag and link kind.
    private void Link(List<TiffDirectoryLink> result, uint owner, uint target, long pointer, TiffDirectoryLinkKind kind, ushort? tag)
    {
        if (links >= options.TiffOptions.MaxDirectoryLinks) throw Limit();
        links++; Charge(48);
        Discover(target);
        if (kind != TiffDirectoryLinkKind.NextIfd) edges[owner].Add(target);
        result.Add(new(kind, tag, pointer, target));
    }
    // Read the DNG layout asynchronously and preserve the caller’s cancellation semantics.
    internal static async Task<DngLayout> Read(Stream source, DngReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source); token.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek || source.Position != 0) throw new ArgumentException("DNG source must be readable, seekable and at zero.", nameof(source));
        var reader = new DngReader(source, options, asynchronous, token);
        return await reader.Run().ConfigureAwait(false);
    }
    // Walk the reachable IFD graph, decode required DNG tags, and produce an immutable layout.
    private async Task<DngLayout> Run()
    {
        if (length > options.TiffOptions.Limits.MaxInputBytes) throw Limit();
        if (length < 8) throw Bad("Truncated DNG header.");
        if (length > 4294967296L) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Files larger than 4 GiB are outside the classic DNG profile.");
        byte[] header = await At(0, 8).ConfigureAwait(false);
        order = header[0] == 0x49 && header[1] == 0x49 ? Endianness.LittleEndian : header[0] == 0x4d && header[1] == 0x4d ? Endianness.BigEndian : throw Bad("Invalid DNG byte order.");
        ushort magic = U16(header, 2);
        if (magic == 43) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "BigTIFF is unsupported.");
        if (magic != 42) throw Bad("Invalid DNG magic.");
        uint first = U32(header, 4);
        firstOffset = first;
        Range(TiffRangeKind.Header, null, null, 0, 8); Discover(first);
        while (pending.Count != 0) { token.ThrowIfCancellationRequested(); await Directory(pending.Dequeue()).ConfigureAwait(false); }
        Graph(first);
        token.ThrowIfCancellationRequested();
        return await Validate(first).ConfigureAwait(false);
    }
    // Hold one mutable IFD during parsing before publishing its immutable snapshot.
    private sealed class WorkingDirectory
    {
        // Retain Offset state required to parse or write TIFF/DNG data safely.
        public required uint Offset;
        public required bool IsRoot;
        public required List<TiffFieldInfo> Fields;
        public required Dictionary<ushort, TiffFieldInfo> ByTag;
        public required List<TiffDirectoryLink> Links;
        public DngImageLayout? Image;
        public uint? NewSubFileType;
        public bool NewSubFileTypeBadShape;
        // Freeze the completed IFD’s fields and links for the returned DNG layout.
        public DngDirectoryInfo Snapshot() => new(Offset, IsRoot, Image, Fields.AsReadOnly(), Links.AsReadOnly());
    }

    private uint firstOffset;
    private readonly List<WorkingDirectory> working = [];

    private async Task Directory(uint offset)
    {
        edges.Add(offset, []);
        int count = U16(await At(offset, 2).ConfigureAwait(false));
        if (count == 0) throw Bad("DNG requires at least one directory entry.");
        if (count > options.TiffOptions.Limits.MaxEntriesPerDirectory || count > options.TiffOptions.Limits.MaxTotalEntries - entries) throw Limit();
        entries += count;
        long size = 6L + 12L * count; Bounds(offset, size, true);
        Range(TiffRangeKind.Directory, offset, null, offset, size);
        var fields = new List<TiffFieldInfo>(); var byTag = new Dictionary<ushort, TiffFieldInfo>(); var resultLinks = new List<TiffDirectoryLink>();
        for (int i = 0; i < count; i++)
        {
            long start = offset + 2L + 12L * i;
            byte[] raw = await At(start, 12).ConfigureAwait(false);
            ushort tag = U16(raw), type = U16(raw, 2); uint n = U32(raw, 4);
            if (tag is TagDngVersion or TagDngBackwardVersion && (type != 1 || n != 4))
                throw InvalidDng($"Tag {tag} must be BYTE/4.");
            Charge(64 + 4L * n);
            int width = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 => 4, 5 or 10 or 12 => 8, _ => throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unknown DNG field width/type.") };
            long bytes = (long)n * width; if (bytes > options.TiffOptions.Limits.MaxValueBytes) throw Limit();
            bool inline = bytes <= 4; long location = inline ? start + 8 : U32(raw, 8); Bounds(location, bytes, !inline);
            var field = new TiffFieldInfo(tag, type, n, inline, new(location, bytes));
            if (!byTag.TryAdd(tag, field)) throw Bad("Duplicate DNG tag.");
            fields.Add(field);
            if (!inline) Range(TiffRangeKind.FieldValue, offset, tag, location, bytes, type, n);
            if (tag == 330)
            {
                if (type != 4 || n == 0) throw Bad("Invalid SubIFD pointer shape.");
                if (n > options.TiffOptions.MaxDirectoryLinks - links) throw Limit();
                uint[] targets = await Uints(field, 4).ConfigureAwait(false);
                for (int t = 0; t < targets.Length; t++) Link(resultLinks, offset, targets[t], location + 4L * t, TiffDirectoryLinkKind.SubIfd, tag);
            }
            else if (tag is 34665 or 34853 or 40965)
            {
                // Recognized but out of profile: discover and inventory the child safely, then refuse via issues.
                if (type != 4 || n == 0) throw Bad("Invalid child IFD pointer shape.");
                if (n > options.TiffOptions.MaxDirectoryLinks - links) throw Limit();
                uint[] targets = await Uints(field, 4).ConfigureAwait(false);
                var kind = tag switch { 34665 => TiffDirectoryLinkKind.Exif, 34853 => TiffDirectoryLinkKind.Gps, _ => TiffDirectoryLinkKind.Interoperability };
                for (int t = 0; t < targets.Length; t++) Link(resultLinks, offset, targets[t], location + 4L * t, kind, tag);
            }
            else if (TileTags.Contains(tag)) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, offset, tag, "Tiles are outside the accepted DNG profile.");
        }
        long nextLocation = offset + 2L + 12L * count; uint next = U32(await At(nextLocation, 4).ConfigureAwait(false));
        if (next != 0) Link(resultLinks, offset, next, nextLocation, TiffDirectoryLinkKind.NextIfd, null);
        Charge(128);
        var workingDirectory = new WorkingDirectory { Offset = offset, IsRoot = offset == firstOffset, Fields = fields, ByTag = byTag, Links = resultLinks };
        (workingDirectory.NewSubFileType, workingDirectory.NewSubFileTypeBadShape) = await NewSubFileType(workingDirectory).ConfigureAwait(false);
        working.Add(workingDirectory);
    }

    // Decode NewSubFileType only when its TIFF type and element count are supported.
    private async Task<(uint? Value, bool BadShape)> NewSubFileType(WorkingDirectory directory)
    {
        if (!directory.ByTag.TryGetValue(254, out var field)) return (null, false);
        if (field.FieldType != 4 || field.Count != 1) return (null, true);
        return ((await Uints(field, 4).ConfigureAwait(false))[0], false);
    }

    // Follow next-IFD and sub-IFD links while rejecting cycles and repeated directories.
    private void Graph(uint first)
    {
        var degree = discovered.ToDictionary(v => v, _ => 0); var depth = discovered.ToDictionary(v => v, _ => 1);
        foreach (var children in edges.Values) foreach (uint child in children) degree[child]++;
        var ready = new Queue<uint>(degree.Where(p => p.Value == 0).Select(p => p.Key)); int visited = 0;
        while (ready.Count != 0)
        {
            token.ThrowIfCancellationRequested(); uint current = ready.Dequeue(); visited++;
            if (depth[current] > options.TiffOptions.Limits.MaxDirectoryDepth) throw Limit();
            foreach (uint child in edges[current]) { depth[child] = Math.Max(depth[child], depth[current] + 1); if (--degree[child] == 0) ready.Enqueue(child); }
        }
        if (visited != discovered.Count || degree[first] != 0) throw Bad("Cyclic DNG directory graph.");
    }

    // Decode unsigned integer elements only when their stored TIFF type matches the tag contract.
    private async Task<uint[]> Uints(TiffFieldInfo field, int expectedType)
    {
        if (field.FieldType != expectedType || field.Count == 0) throw new MetadataException(MetadataErrorCode.InvalidTiff, $"Invalid numeric field shape for tag {field.TagId}.");
        Charge(4L * field.Count);
        byte[] bytes = await At(field.ValueRange.Offset, checked((int)field.ValueRange.Length)).ConfigureAwait(false);
        uint[] result = new uint[(int)field.Count]; int width = expectedType == 3 ? 2 : 4;
        for (int i = 0; i < result.Length; i++) { if ((i & 1023) == 0) token.ThrowIfCancellationRequested(); result[i] = width == 2 ? U16(bytes, i * width) : U32(bytes, i * width); }
        return result;
    }

    // Decode rational elements with the requested signedness and checked element ranges.
    private async Task<(int Num, int Den)[]> Rationals(TiffFieldInfo field, bool signed)
    {
        int type = signed ? 10 : 5;
        if (field.FieldType != type || field.Count == 0) throw new MetadataException(MetadataErrorCode.InvalidTiff, $"Invalid rational field shape for tag {field.TagId}.");
        Charge(8L * field.Count);
        byte[] bytes = await At(field.ValueRange.Offset, checked((int)field.ValueRange.Length)).ConfigureAwait(false);
        var result = new (int, int)[(int)field.Count];
        for (int i = 0; i < result.Length; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            int num = signed ? S32(bytes, i * 8) : (int)U32(bytes, i * 8); int den = signed ? S32(bytes, i * 8 + 4) : (int)U32(bytes, i * 8 + 4);
            result[i] = (num, den);
        }
        return result;
    }
    private async Task<DngLayout> Validate(uint first)
    {
        // ---------- Hard identity errors (D0 error model) ----------
        var root = working.First(d => d.Offset == first);
        if (!root.ByTag.TryGetValue(TagDngVersion, out var rootVersionField))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Ordinary TIFF without a root DNGVersion; DNG is unsupported.");
        DngVersion version = await IdentityVersion(rootVersionField, root.Offset, TagDngVersion).ConfigureAwait(false);
        if (!version.Equals(new DngVersion(1, 1, 0, 0)))
            Issue(DngLayoutIssueCode.UnsupportedVersion, first, TagDngVersion, "DNGVersion must be exactly 1.1.0.0.");
        foreach (var dir in working)
        {
            if (dir.Offset == first) continue;
            if (dir.ByTag.TryGetValue(TagDngVersion, out var leafVersion)) _ = await IdentityVersion(leafVersion, dir.Offset, TagDngVersion).ConfigureAwait(false);
        }
        DngVersion backward = await BackwardVersion(root, version).ConfigureAwait(false);
        foreach (var dir in working)
        {
            if (dir.Offset == first) continue;
            if (dir.ByTag.TryGetValue(TagDngVersion, out var leafVersion))
            {
                DngVersion leaf = await IdentityVersion(leafVersion, dir.Offset, TagDngVersion).ConfigureAwait(false);
                if (!leaf.Equals(new DngVersion(1, 1, 0, 0))) Issue(DngLayoutIssueCode.UnsupportedVersion, dir.Offset, TagDngVersion, "DNGVersion must be exactly 1.1.0.0.");
                else if (!leaf.Equals(version)) Issue(DngLayoutIssueCode.UnsupportedVersion, dir.Offset, TagDngVersion, "Raw leaf DNGVersion differs from the root value.");
            }
            if (dir.ByTag.TryGetValue(TagDngBackwardVersion, out var leafBackward))
            {
                DngVersion leaf = await IdentityVersion(leafBackward, dir.Offset, TagDngBackwardVersion).ConfigureAwait(false);
                if (Compare(leaf, backward) > 0) Issue(DngLayoutIssueCode.UnsupportedVersion, dir.Offset, TagDngBackwardVersion, "Raw leaf DNGBackwardVersion exceeds the root backward version.");
            }
        }

        // ---------- Roles and shape acceptance ----------
        var roles = working.ToDictionary(d => d.Offset, RoleOf);
        DngImageRole RootRole() => roles[first];
        var expected = new HashSet<uint> { first };
        WorkingDirectory? rawLeaf = null;
        if (RootRole() == DngImageRole.CfaRaw)
        {
            foreach (var extra in working.Where(d => d.Offset != first))
                Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, extra.Offset, null, "Extra directory outside the accepted raw-rooted DNG profile.");
            foreach (var link in root.Links)
                Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, root.Offset, link.TagId, "Raw-rooted profile accepts no links from the root.");
        }
        else if (RootRole() == DngImageRole.RgbThumbnail)
        {
            var subIfdLinks = root.Links.Where(l => l.Kind == TiffDirectoryLinkKind.SubIfd).ToList();
            foreach (var link in root.Links.Where(l => l.Kind != TiffDirectoryLinkKind.SubIfd))
                Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, root.Offset, link.TagId, "RGB thumbnail root accepts only a single SubIFD link.");
            if (subIfdLinks.Count != 1)
                Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, root.Offset, 330, "Accepted profile requires exactly one SubIFD from the RGB thumbnail root.");
            else
            {
                rawLeaf = working.SingleOrDefault(d => d.Offset == subIfdLinks[0].TargetIfdOffset);
                if (rawLeaf is not null)
                {
                    expected.Add(rawLeaf.Offset);
                    if (roles[rawLeaf.Offset] != DngImageRole.CfaRaw)
                        Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, rawLeaf.Offset, 254, "SubIFD target is not a CFA raw directory.");
                    foreach (var link in rawLeaf.Links)
                        Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, rawLeaf.Offset, link.TagId, "Raw leaf carries links outside the accepted DNG profile.");
                }
            }
            foreach (var extra in working.Where(d => !expected.Contains(d.Offset)))
                Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, extra.Offset, null, "Extra directory outside the accepted thumbnail/raw DNG profile.");
        }
        else
        {
            Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, first, root.ByTag.ContainsKey(254) ? (ushort?)254 : null, "Root is neither a CFA raw nor an RGB thumbnail directory.");
        }
        foreach (var dir in working)
            foreach (var link in dir.Links.Where(l => l.Kind == TiffDirectoryLinkKind.NextIfd))
                Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, dir.Offset, null, "Next-IFD chains are outside the accepted DNG profile.");
        foreach (var dir in working)
            if (dir.Offset != first && roles[dir.Offset] == DngImageRole.Unknown)
                Issue(DngLayoutIssueCode.UnsupportedDirectoryRole, dir.Offset, null, "Directory role is not part of the accepted DNG profile.");
        if (root.NewSubFileTypeBadShape)
            Issue(DngLayoutIssueCode.UnsupportedFieldShape, root.Offset, 254, "NewSubFileType must be LONG/1.");

        // ---------- Per-directory profile validation ----------
        foreach (var dir in working)
        {
            DngImageRole role = roles[dir.Offset];
            if (role == DngImageRole.Unknown) continue; // role issue already recorded; geometry not established
            var allowed = role == DngImageRole.CfaRaw ? RawRoleTags : ThumbnailRoleTags;
            foreach (var field in dir.Fields)
                if (!allowed.Contains(field.TagId))
                    Issue(DngLayoutIssueCode.UnsupportedExtension, dir.Offset, field.TagId, "Field is outside the accepted DNG profile whitelist.");
            await ValidateCommon(dir).ConfigureAwait(false);
            if (role == DngImageRole.CfaRaw) _ = await ValidateRaw(dir).ConfigureAwait(false);
            else _ = await ValidateThumbnail(dir).ConfigureAwait(false);
            await ValidateColorIdentity(dir, dir.IsRoot).ConfigureAwait(false);
        }

        var sorted = ranges.OrderBy(r => r.Range.Offset).ThenBy(r => r.Range.Length).ToArray();
        for (int i = 1; i < sorted.Length; i++) { token.ThrowIfCancellationRequested(); if (sorted[i].Range.Offset < sorted[i - 1].Range.Offset + sorted[i - 1].Range.Length) throw Bad("Overlapping DNG ranges."); }
        var snapshots = working.Select(d => d.Snapshot()).ToArray();
        bool isSupported = issues.Count == 0;
        token.ThrowIfCancellationRequested();
        return new DngLayout(order, length, first, version, backward, isSupported, snapshots, sorted, issues.ToArray());

        DngImageRole RoleOf(WorkingDirectory dir)
        {
            if (dir.NewSubFileTypeBadShape) return DngImageRole.Unknown;
            if (dir.NewSubFileType is null) return DngImageRole.Unknown;
            return dir.NewSubFileType switch { 0 => DngImageRole.CfaRaw, 1 => DngImageRole.RgbThumbnail, _ => DngImageRole.Unknown };
        }
    }
    // Compare packed DNG version components numerically rather than as text.
    private static int Compare(DngVersion a, DngVersion b)
    {
        if (a.Major != b.Major) return a.Major - b.Major;
        if (a.Minor != b.Minor) return a.Minor - b.Minor;
        if (a.Patch != b.Patch) return a.Patch - b.Patch;
        return a.Build - b.Build;
    }

    // Read the DNGVersion tag and reject malformed component counts or unsupported encodings.
    private async Task<DngVersion> IdentityVersion(TiffFieldInfo field, uint owner, ushort tag)
    {
        if (field.FieldType != 1 || field.Count != 4)
            throw InvalidDng($"Tag {tag} must be BYTE/4.");
        byte[] bytes = await At(field.ValueRange.Offset, 4).ConfigureAwait(false);
        return new DngVersion(bytes[0], bytes[1], bytes[2], bytes[3]);
    }

    // Read DNGBackwardVersion, defaulting or refusing according to the DNG tag rules.
    private async Task<DngVersion> BackwardVersion(WorkingDirectory dir, DngVersion version)
    {
        if (!dir.ByTag.TryGetValue(TagDngBackwardVersion, out var field))
            return new DngVersion(version.Major, version.Minor, 0, 0);
        if (field.FieldType != 1 || field.Count != 4)
            throw InvalidDng("Tag 50707 must be BYTE/4.");
        byte[] bytes = await At(field.ValueRange.Offset, 4).ConfigureAwait(false);
        var value = new DngVersion(bytes[0], bytes[1], bytes[2], bytes[3]);
        bool allowed = (value.Major, value.Minor, value.Patch, value.Build) is (1, 0, 0, 0) or (1, 1, 0, 0);
        if (!allowed || Compare(value, version) > 0)
            Issue(DngLayoutIssueCode.UnsupportedVersion, dir.Offset, TagDngBackwardVersion, "DNGBackwardVersion is not 1.0.0.0/1.1.0.0 or exceeds DNGVersion.");
        return value;
    }

    // Read a single LONG-valued directory tag and distinguish missing from malformed values.
    private async Task<(bool Ok, uint Value)> LongScalar(WorkingDirectory dir, ushort tag, string name)
    {
        if (!dir.ByTag.TryGetValue(tag, out var field)) { Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, tag, name + " is required."); return (false, 0); }
        if (field.FieldType != 4 || field.Count != 1) { Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, tag, name + " must be LONG/1."); return (false, 0); }
        return (true, (await Uints(field, 4).ConfigureAwait(false))[0]);
    }

    // Read a single SHORT-valued directory tag and distinguish missing from malformed values.
    private async Task<(bool Ok, uint Value)> ShortScalar(WorkingDirectory dir, ushort tag, string name, DngLayoutIssueCode mismatchCode = DngLayoutIssueCode.InvalidSemanticValue)
    {
        if (!dir.ByTag.TryGetValue(tag, out var field)) { Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, tag, name + " is required."); return (false, 0); }
        if (field.FieldType != 3 || field.Count != 1) { Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, tag, name + " must be SHORT/1."); return (false, 0); }
        return (true, (await Uints(field, 3).ConfigureAwait(false))[0]);
    }

    // Read a BYTE array tag only after its type, element count, and byte range are validated.
    private async Task<byte[]?> ByteArray(WorkingDirectory dir, ushort tag, int count)
    {
        if (!dir.ByTag.TryGetValue(tag, out var field)) return null;
        if (field.FieldType != 1 || field.Count != (uint)count) { Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, tag, $"Tag {tag} must be BYTE/{count}."); return null; }
        Charge(count);
        return await At(field.ValueRange.Offset, count).ConfigureAwait(false);
    }

    // Keep UintArray focused on TIFF/DNG and its format-specific validation contract.
    private async Task<uint[]?> UintArray(WorkingDirectory dir, ushort tag, int type, int count)
    {
        if (!dir.ByTag.TryGetValue(tag, out var field)) return null;
        if (field.FieldType != (ushort)type || field.Count != (uint)count) { Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, tag, $"Tag {tag} must be {(type == 3 ? "SHORT" : "LONG")}/{count}."); return null; }
        return await Uints(field, type).ConfigureAwait(false);
    }

    private static bool Printable(byte[] bytes) => bytes.Length > 1 && bytes[^1] == 0 && bytes.Take(bytes.Length - 1).All(b => b >= 0x20 && b <= 0x7E);

    // Accept only bounded ASCII tag text with valid termination and encoding.
    private async Task<bool> AsciiOk(WorkingDirectory dir, TiffFieldInfo field, ushort tag)
    {
        if (field.FieldType != 2 || field.Count < 2) { Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, tag, "ASCII field must be type 2 with at least one character and NUL."); return false; }
        Charge(field.Count);
        byte[] bytes = await At(field.ValueRange.Offset, checked((int)field.ValueRange.Length)).ConfigureAwait(false);
        if (!Printable(bytes)) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, tag, "ASCII field must be nonempty printable bytes with one final NUL."); return false; }
        return true;
    }

    // Check dimensions, sample layout, and strip/tile fields shared by DNG images.
    private async Task ValidateCommon(WorkingDirectory dir)
    {
        if (dir.ByTag.TryGetValue(274, out var orientation))
        {
            if (orientation.FieldType == 3 && orientation.Count == 1)
            {
                uint value = (await Uints(orientation, 3).ConfigureAwait(false))[0];
                if (value != 1) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 274, "Only Orientation 1 is accepted.");
            }
            else Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, 274, "Orientation must be SHORT/1.");
        }
        foreach (ushort tag in new ushort[] { 305, 315 })
            if (dir.ByTag.TryGetValue(tag, out var field))
                await AsciiOk(dir, field, tag).ConfigureAwait(false);
    }

    // Check the color-identity tags and their cross-field shape requirements.
    private async Task ValidateColorIdentity(WorkingDirectory dir, bool required)
    {
        if (dir.ByTag.TryGetValue(TagUniqueCameraModel, out var ucm)) { await AsciiOk(dir, ucm, TagUniqueCameraModel).ConfigureAwait(false); }
        else if (required) Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, TagUniqueCameraModel, "UniqueCameraModel is required.");
        if (dir.ByTag.TryGetValue(TagColorMatrix1, out var cm))
        {
            if (cm.FieldType != 10 || cm.Count != 9) Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, TagColorMatrix1, "ColorMatrix1 must be SRATIONAL/9.");
            else
            {
                var values = await Rationals(cm, true).ConfigureAwait(false);
                if (values.Any(v => v.Den <= 0)) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagColorMatrix1, "ColorMatrix1 denominators must be strictly positive.");
            }
        }
        else if (required) Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, TagColorMatrix1, "ColorMatrix1 is required.");
        if (dir.ByTag.TryGetValue(TagCalibrationIlluminant1, out var ci))
        {
            if (ci.FieldType != 3 || ci.Count != 1) Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, TagCalibrationIlluminant1, "CalibrationIlluminant1 must be SHORT/1.");
            else { uint value = (await Uints(ci, 3).ConfigureAwait(false))[0]; if (value != 21) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagCalibrationIlluminant1, "CalibrationIlluminant1 must be 21 (D65)."); }
        }
        else if (required) Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, TagCalibrationIlluminant1, "CalibrationIlluminant1 is required.");
        if (dir.ByTag.TryGetValue(TagAsShotNeutral, out var asn))
        {
            if (asn.FieldType != 5 || asn.Count != 3) Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, TagAsShotNeutral, "AsShotNeutral must be RATIONAL/3.");
            else
            {
                var values = await Rationals(asn, false).ConfigureAwait(false);
                if (values.Any(v => v.Num <= 0 || v.Den <= 0)) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagAsShotNeutral, "AsShotNeutral numerators and denominators must be strictly positive.");
            }
        }
        else if (required) Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, TagAsShotNeutral, "AsShotNeutral is required.");
    }
    // Count image segments against MaxSegments before recording more strip or tile ranges.
    private void AddSegment() { if (segments >= options.TiffOptions.Limits.MaxSegments) throw Limit(); segments++; }

    // Validate strip offsets and byte counts as paired, in-range image segments.
    private async Task<List<TiffByteRange>?> ValidateStrips(WorkingDirectory dir, uint width, uint height, uint rowsPerStrip, int bytesPerSample, ulong? maxTotalBytes)
    {
        if (!dir.ByTag.TryGetValue(273, out var offField)) { Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, 273, "StripOffsets is required."); return null; }
        if (!dir.ByTag.TryGetValue(279, out var cntField)) { Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, 279, "StripByteCounts is required."); return null; }
        if (offField.FieldType != 4 || offField.Count == 0) { Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, 273, "StripOffsets must be a nonempty LONG array."); return null; }
        if (cntField.FieldType != 4 || cntField.Count != offField.Count) { Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, 279, "StripByteCounts must be a LONG array with the same count as StripOffsets."); return null; }
        ulong expected = ((ulong)height + rowsPerStrip - 1) / rowsPerStrip;
        if (offField.Count != expected) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 273, "Strip array count disagrees with the RowsPerStrip grid."); return null; }
        if (expected > (ulong)(options.TiffOptions.Limits.MaxSegments - segments)) throw Limit();
        uint[] starts = await Uints(offField, 4).ConfigureAwait(false);
        uint[] lengths = await Uints(cntField, 4).ConfigureAwait(false);
        var strips = new List<TiffByteRange>(starts.Length);
        ulong total = 0;
        for (int i = 0; i < starts.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            ulong actualRows = Math.Min((ulong)rowsPerStrip, height - (ulong)i * rowsPerStrip);
            ulong expectedBytes = (ulong)width * actualRows * (ulong)bytesPerSample;
            if (lengths[i] != expectedBytes) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 279, "StripByteCounts disagrees with the uncompressed grid.");
            total += lengths[i];
            if (lengths[i] > 0) { AddSegment(); Charge(16); Range(TiffRangeKind.Strip, dir.Offset, 273, starts[i], lengths[i]); }
            strips.Add(new TiffByteRange(starts[i], lengths[i]));
        }
        if (maxTotalBytes is { } cap && total > cap) throw Limit();
        return strips;
    }

    // Validate the CFA repeat pattern and color-plane description as a consistent pair.
    private async Task ValidateCfa(WorkingDirectory dir)
    {
        uint[]? pattern = await UintArray(dir, TagCfaRepeatPatternDim, 3, 2).ConfigureAwait(false);
        if (pattern is not null && !(pattern[0] == 2 && pattern[1] == 2)) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagCfaRepeatPatternDim, "CFARepeatPatternDim must be [2, 2].");
        else if (!dir.ByTag.ContainsKey(TagCfaRepeatPatternDim)) Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, TagCfaRepeatPatternDim, "CFARepeatPatternDim is required.");
        if (dir.ByTag.ContainsKey(TagCfaPattern))
        {
            byte[]? cfa = await ByteArray(dir, TagCfaPattern, 4).ConfigureAwait(false);
            if (cfa is not null && (cfa.Count(b => b == 0) != 1 || cfa.Count(b => b == 1) != 2 || cfa.Count(b => b == 2) != 1))
                Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagCfaPattern, "CFAPattern must contain one red, two green and one blue element.");
        }
        else Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, TagCfaPattern, "CFAPattern is required.");
        if (dir.ByTag.ContainsKey(TagCfaPlaneColor))
        {
            byte[]? plane = await ByteArray(dir, TagCfaPlaneColor, 3).ConfigureAwait(false);
            if (plane is not null && !(plane[0] == 0 && plane[1] == 1 && plane[2] == 2)) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagCfaPlaneColor, "CFAPlaneColor must be [0, 1, 2].");
        }
        else Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, TagCfaPlaneColor, "CFAPlaneColor is required.");
        (bool clOk, uint layout) = await ShortScalar(dir, TagCfaLayout, "CFALayout").ConfigureAwait(false);
        if (clOk && layout != 1) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagCfaLayout, "CFALayout must be 1 (rectangular).");
    }

    // Validate black-level dimensions and values against the CFA repeat pattern.
    private async Task ValidateBlackLevel(WorkingDirectory dir)
    {
        uint[]? dims = await UintArray(dir, TagBlackLevelRepeatDim, 3, 2).ConfigureAwait(false);
        if (dims is not null && !(dims[0] == 1 && dims[1] == 1)) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagBlackLevelRepeatDim, "BlackLevelRepeatDim must be [1, 1].");
        else if (!dir.ByTag.ContainsKey(TagBlackLevelRepeatDim)) Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, TagBlackLevelRepeatDim, "BlackLevelRepeatDim is required.");
        if (dir.ByTag.TryGetValue(TagBlackLevel, out var field))
        {
            if (field.FieldType != 5 || field.Count != 1) Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, TagBlackLevel, "BlackLevel must be RATIONAL/1.");
            else
            {
                var values = await Rationals(field, false).ConfigureAwait(false);
                if (values[0].Den <= 0) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagBlackLevel, "BlackLevel denominator must be strictly positive.");
                else if (values[0].Num != 0) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagBlackLevel, "BlackLevel must be 0.");
            }
        }
        else Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, TagBlackLevel, "BlackLevel is required.");
    }

    // Validate white-level sample counts and values against the image sample layout.
    private async Task ValidateWhiteLevel(WorkingDirectory dir)
    {
        (bool ok, uint value) = await LongScalar(dir, TagWhiteLevel, "WhiteLevel").ConfigureAwait(false);
        if (ok && value != 65535) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagWhiteLevel, "WhiteLevel must be 65535.");
    }

    // Ensure the active area is ordered and contained within the stored image dimensions.
    private async Task<(uint Left, uint Top, uint Right, uint Bottom)?> ValidateActiveArea(WorkingDirectory dir, (uint Width, uint Height)? dimensions)
    {
        if (!dir.ByTag.TryGetValue(TagActiveArea, out _)) return null;
        uint[]? area = await UintArray(dir, TagActiveArea, 4, 4).ConfigureAwait(false);
        if (area is null) return null;
        (uint top, uint left, uint bottom, uint right) = (area[0], area[1], area[2], area[3]);
        if (!(top < bottom && left < right)) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagActiveArea, "ActiveArea must satisfy top < bottom and left < right."); return null; }
        if (dimensions is { } d && (bottom > d.Height || right > d.Width)) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagActiveArea, "ActiveArea exceeds the raw bounds."); return null; }
        return (left, top, right, bottom);
    }

    // Ensure the default crop origin and size remain inside the active image area.
    private async Task ValidateCrop(WorkingDirectory dir, (uint Width, uint Height)? dimensions, (uint Left, uint Top, uint Right, uint Bottom)? activeArea)
    {
        bool hasOrigin = dir.ByTag.TryGetValue(TagDefaultCropOrigin, out _);
        bool hasSize = dir.ByTag.TryGetValue(TagDefaultCropSize, out _);
        if (hasOrigin != hasSize) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, hasOrigin ? TagDefaultCropSize : TagDefaultCropOrigin, "DefaultCropOrigin and DefaultCropSize must occur together."); return; }
        if (!hasOrigin) return;
        uint[]? origin = await UintArray(dir, TagDefaultCropOrigin, 4, 2).ConfigureAwait(false);
        uint[]? size = await UintArray(dir, TagDefaultCropSize, 4, 2).ConfigureAwait(false);
        if (origin is null || size is null) return;
        if (size[0] == 0 || size[1] == 0) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagDefaultCropSize, "DefaultCropSize must be positive."); return; }
        if (dimensions is null) return;
        uint left = activeArea?.Left ?? 0, top = activeArea?.Top ?? 0, right = activeArea?.Right ?? dimensions.Value.Width, bottom = activeArea?.Bottom ?? dimensions.Value.Height;
        ulong rightEdge = (ulong)origin[0] + size[0];
        ulong bottomEdge = (ulong)origin[1] + size[1];
        if (origin[0] < left || origin[1] < top || rightEdge > right || bottomEdge > bottom)
            Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, TagDefaultCropOrigin, "Default crop exceeds the active area.");
    }
    // Validate raw-image dimensions and required DNG tags before accepting the image layout.
    private async Task<DngImageLayout?> ValidateRaw(WorkingDirectory dir)
    {
        (bool wOk, uint w) = await LongScalar(dir, 256, "ImageWidth").ConfigureAwait(false);
        (bool hOk, uint h) = await LongScalar(dir, 257, "ImageLength").ConfigureAwait(false);
        bool dimsOk = wOk && hOk;
        if (dimsOk)
        {
            if (w == 0 || h == 0) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 256, "Image dimensions must be positive."); dimsOk = false; }
            else
            {
                if ((w & 1) != 0 || (h & 1) != 0) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 256, "Raw image dimensions must be even.");
                if ((ulong)w * h > (ulong)options.MaxRawPixels) throw Limit();
            }
        }
        (bool cOk, uint compression) = await ShortScalar(dir, 259, "Compression").ConfigureAwait(false);
        if (cOk && compression != 1) Issue(DngLayoutIssueCode.UnsupportedCompression, dir.Offset, 259, "Only Compression 1 (uncompressed) is accepted.");
        (bool bOk, uint bits) = await ShortScalar(dir, 258, "BitsPerSample").ConfigureAwait(false);
        if (bOk && bits != 16) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 258, "BitsPerSample must be 16.");
        (bool sOk, uint spp) = await ShortScalar(dir, 277, "SamplesPerPixel").ConfigureAwait(false);
        if (sOk && spp != 1) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 277, "SamplesPerPixel must be 1.");
        (bool pOk, uint photo) = await ShortScalar(dir, 262, "PhotometricInterpretation").ConfigureAwait(false);
        if (pOk && photo != 32803) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 262, "PhotometricInterpretation must be 32803 (CFA).");
        (bool plOk, uint planar) = await ShortScalar(dir, 284, "PlanarConfiguration").ConfigureAwait(false);
        if (plOk && planar != 1) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 284, "PlanarConfiguration must be 1.");
        (bool fOk, uint sampleFormat) = await ShortScalar(dir, 339, "SampleFormat").ConfigureAwait(false);
        if (fOk && sampleFormat != 1) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 339, "SampleFormat must be 1 (unsigned integer).");
        if (dir.ByTag.TryGetValue(254, out var nstf))
        {
            if (nstf.FieldType != 4 || nstf.Count != 1) Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, 254, "NewSubFileType must be LONG/1.");
            else { uint v = (await Uints(nstf, 4).ConfigureAwait(false))[0]; if (v != 0) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 254, "Raw NewSubFileType must be 0."); }
        }
        else Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, 254, "NewSubFileType is required.");
        (bool rOk, uint rps) = await LongScalar(dir, 278, "RowsPerStrip").ConfigureAwait(false);
        if (rOk && rps == 0) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 278, "RowsPerStrip must be positive."); rOk = false; }

        List<TiffByteRange>? strips = null;
        if (dimsOk && rOk) strips = await ValidateStrips(dir, w, h, rps, 2, null).ConfigureAwait(false);

        await ValidateCfa(dir).ConfigureAwait(false);
        await ValidateBlackLevel(dir).ConfigureAwait(false);
        await ValidateWhiteLevel(dir).ConfigureAwait(false);
        (uint Width, uint Height)? dimensions = dimsOk ? (w, h) : null;
        (uint Left, uint Top, uint Right, uint Bottom)? activeArea = await ValidateActiveArea(dir, dimensions).ConfigureAwait(false);
        await ValidateCrop(dir, dimensions, activeArea).ConfigureAwait(false);

        if (!dimsOk || strips is null) { dir.Image = null; return null; }
        Charge(64);
        dir.Image = new DngImageLayout(DngImageRole.CfaRaw, w, h, bOk ? (ushort)bits : (ushort)0, sOk ? (ushort)spp : (ushort)0, strips.AsReadOnly());
        return dir.Image;
    }

    // Validate thumbnail dimensions and ranges before retaining the thumbnail segment.
    private async Task<DngImageLayout?> ValidateThumbnail(WorkingDirectory dir)
    {
        (bool wOk, uint w) = await LongScalar(dir, 256, "ImageWidth").ConfigureAwait(false);
        (bool hOk, uint h) = await LongScalar(dir, 257, "ImageLength").ConfigureAwait(false);
        bool dimsOk = wOk && hOk;
        if (dimsOk)
        {
            if (w == 0 || h == 0) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 256, "Image dimensions must be positive."); dimsOk = false; }
            else
            {
                if ((w & 1) != 0 || (h & 1) != 0) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 256, "Thumbnail dimensions must be even.");
                if ((ulong)w * h > (ulong)options.MaxThumbnailPixels) throw Limit();
            }
        }
        (bool cOk, uint compression) = await ShortScalar(dir, 259, "Compression").ConfigureAwait(false);
        if (cOk && compression != 1) Issue(DngLayoutIssueCode.UnsupportedCompression, dir.Offset, 259, "Only Compression 1 (uncompressed) is accepted.");
        uint[]? bits = await UintArray(dir, 258, 3, 3).ConfigureAwait(false);
        if (bits is not null && !(bits[0] == 8 && bits[1] == 8 && bits[2] == 8)) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 258, "BitsPerSample must be [8, 8, 8].");
        else if (!dir.ByTag.ContainsKey(258)) Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, 258, "BitsPerSample is required.");
        (bool sOk, uint spp) = await ShortScalar(dir, 277, "SamplesPerPixel").ConfigureAwait(false);
        if (sOk && spp != 3) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 277, "SamplesPerPixel must be 3.");
        (bool pOk, uint photo) = await ShortScalar(dir, 262, "PhotometricInterpretation").ConfigureAwait(false);
        if (pOk && photo != 2) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 262, "PhotometricInterpretation must be 2 (RGB).");
        (bool plOk, uint planar) = await ShortScalar(dir, 284, "PlanarConfiguration").ConfigureAwait(false);
        if (plOk && planar != 1) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 284, "PlanarConfiguration must be 1.");
        uint[]? sampleFormat = await UintArray(dir, 339, 3, 3).ConfigureAwait(false);
        if (sampleFormat is not null && !(sampleFormat[0] == 1 && sampleFormat[1] == 1 && sampleFormat[2] == 1)) Issue(DngLayoutIssueCode.UnsupportedSampleLayout, dir.Offset, 339, "SampleFormat must be [1, 1, 1].");
        else if (!dir.ByTag.ContainsKey(339)) Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, 339, "SampleFormat is required.");
        if (dir.ByTag.TryGetValue(254, out var nstf))
        {
            if (nstf.FieldType != 4 || nstf.Count != 1) Issue(DngLayoutIssueCode.UnsupportedFieldShape, dir.Offset, 254, "NewSubFileType must be LONG/1.");
            else { uint v = (await Uints(nstf, 4).ConfigureAwait(false))[0]; if (v != 1) Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 254, "Thumbnail NewSubFileType must be 1."); }
        }
        else Issue(DngLayoutIssueCode.MissingRequiredField, dir.Offset, 254, "NewSubFileType is required.");
        (bool rOk, uint rps) = await LongScalar(dir, 278, "RowsPerStrip").ConfigureAwait(false);
        if (rOk && rps == 0) { Issue(DngLayoutIssueCode.InvalidSemanticValue, dir.Offset, 278, "RowsPerStrip must be positive."); rOk = false; }

        List<TiffByteRange>? strips = null;
        if (dimsOk && rOk) strips = await ValidateStrips(dir, w, h, rps, 3, (ulong)options.MaxThumbnailBytes).ConfigureAwait(false);

        if (!dimsOk || strips is null) { dir.Image = null; return null; }
        Charge(64);
        dir.Image = new DngImageLayout(DngImageRole.RgbThumbnail, w, h, 8, sOk ? (ushort)spp : (ushort)0, strips.AsReadOnly());
        return dir.Image;
    }
}
