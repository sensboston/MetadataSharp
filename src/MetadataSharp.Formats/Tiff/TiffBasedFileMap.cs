using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Formats.Tiff;

/// <summary>What a directory of a TIFF-based file is to the writer.</summary>
internal enum TiffBasedRole
{
    /// <summary>The directory the header points at.</summary>
    Primary,
    /// <summary>The target of tag 0x8769 of the primary directory.</summary>
    Exif,
    /// <summary>The target of tag 0x8825 of the primary directory.</summary>
    Gps,
    /// <summary>The target of tag 0xA005 of the Exif directory.</summary>
    Interop,
    /// <summary>Everything else that is reached: chained directories, SubIFDs and what they point at.</summary>
    Other
}

internal enum TiffBasedRangeKind
{
    Directory,
    Value,
    /// <summary>Strips, tiles and JPEG streams addressed by an image directory.</summary>
    ImageData,
    /// <summary>The MakerNote (0x927C) and DNGPrivateData (0xC634) values.</summary>
    MakerNote
}

internal readonly record struct TiffBasedRange(TiffBasedRangeKind Kind, long Start, long Length)
{
    internal long End => Start + Length;
}

/// <summary>
/// One directory entry as it lies in the file. <see cref="ValueOffset"/> is where the value bytes
/// are: the last four bytes of the entry for an inline value, the address in the entry otherwise.
/// </summary>
internal sealed record TiffBasedField(ushort Tag, ushort Type, uint Count, int EntryOffset, bool IsInline,
    long ValueOffset, long ByteLength);

internal sealed class TiffBasedDirectory(TiffBasedRole role, uint offset, List<TiffBasedField> fields, uint next,
    bool sorted)
{
    internal TiffBasedRole Role { get; } = role;
    internal uint Offset { get; } = offset;
    internal IReadOnlyList<TiffBasedField> Fields { get; } = fields;
    internal uint Next { get; } = next;
    /// <summary>Whether the tags ascend strictly, which a directory must do to be rewritten.</summary>
    internal bool IsSorted { get; } = sorted;
    /// <summary>How many pointers of the file lead here.</summary>
    internal int References { get; set; } = 1;
    internal int Length => 2 + 12 * Fields.Count + 4;

    internal TiffBasedField? Find(ushort tag)
    {
        foreach (TiffBasedField field in Fields)
            if (field.Tag == tag) return field;
        return null;
    }
}

/// <summary>
/// The physical layout of a TIFF-based file, as far as the writer has to know it: every directory
/// that is reached from the header, every byte range those directories address, and which of the
/// ranges hold image data or a MakerNote. Nothing is decoded and no value is copied.
/// </summary>
/// <remarks>
/// The walk follows the header, the chain of next-directory links of image directories, SubIFDs
/// (0x014A), and the Exif (0x8769), GPS (0x8825) and Interoperability (0xA005) pointers. A structure
/// it cannot account for is a refusal, not a guess: a directory or pointer outside the file, an
/// unknown field type (its width, and so its range, is unknown), a budget of
/// <see cref="MetadataLimits"/>. A value or image range that reaches past the end of the file is
/// recorded and reported by <see cref="HasRangePastEnd"/>: such a file can be edited in place and
/// nothing may be appended to it, because appended bytes would land inside that range.
/// </remarks>
internal sealed class TiffBasedFileMap
{
    private const ushort BigTiffMagic = 43;
    private const ushort Rw2Magic = 0x0055;

    private readonly byte[] file;
    private readonly MetadataLimits limits;
    private readonly List<TiffBasedDirectory> directories = [];
    private readonly List<TiffBasedRange> ranges = [];
    private readonly Dictionary<uint, TiffBasedDirectory?> seen = [];
    private readonly Dictionary<uint, int> extraReferences = [];
    private readonly Queue<(uint Offset, TiffBasedRole Role)> pending = new();
    private readonly int maxRanges;
    private readonly bool rw2;
    private int totalEntries;

    private TiffBasedFileMap(byte[] file, MetadataLimits limits, int maxRanges)
    {
        this.file = file;
        this.limits = limits;
        this.maxRanges = maxRanges;
        Order = file[0] == 0x49 ? Endianness.LittleEndian : Endianness.BigEndian;
        rw2 = U16(2) == Rw2Magic;
    }

    internal Endianness Order { get; }
    internal int SourceLength => file.Length;
    internal IReadOnlyList<TiffBasedDirectory> Directories => directories;
    internal IReadOnlyList<TiffBasedRange> Ranges => ranges;

    /// <summary>A directory addresses bytes that the file does not have.</summary>
    internal bool HasRangePastEnd { get; private set; }

    /// <summary>
    /// Image data whose length the file does not state, so that it runs to the end of the file: the
    /// raw data of an RW2 addressed by RawDataOffset (0x0118), or strips, tiles or a JPEG stream
    /// without their byte counts. Appending to such a file would make that data longer.
    /// </summary>
    internal bool HasOpenEndedImageData { get; private set; }

    internal static TiffBasedFileMap Read(byte[] file, MetadataLimits limits, int maxRanges)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(limits);
        if (file.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The TIFF file exceeds the input budget.", 0);
        if (!ExifTiffReader.HasFileHeader(file))
            throw Invalid("The data does not begin with a TIFF header.", 0);
        var map = new TiffBasedFileMap(file, limits, maxRanges);
        if (map.U16(2) == BigTiffMagic)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "BigTIFF is not supported.", 2);
        map.Walk();
        return map;
    }

    /// <summary>The primary, Exif or GPS directory, or null when the file has none.</summary>
    internal TiffBasedDirectory? Find(TiffBasedRole role)
    {
        foreach (TiffBasedDirectory directory in directories)
            if (directory.Role == role) return directory;
        return null;
    }

    internal TiffBasedDirectory? FindAt(uint offset)
    {
        foreach (TiffBasedDirectory directory in directories)
            if (directory.Offset == offset) return directory;
        return null;
    }

    /// <summary>
    /// Whether the bytes are inside the file, after the header, and addressed by exactly one of the
    /// ranges of the map. Only such bytes may be overwritten: a second range over them is a second
    /// reader of them.
    /// </summary>
    internal bool IsExclusive(long start, long length)
    {
        if (start < 8 || length <= 0 || start > file.Length || length > file.Length - start)
            return false;
        int owners = 0;
        foreach (TiffBasedRange range in ranges)
            if (start < range.End && range.Start < start + length && ++owners > 1)
                return false;
        return owners == 1;
    }

    private void Walk()
    {
        uint first = U32(4);
        if (first == 0)
            throw Invalid("The primary directory is missing.", 4);
        Schedule(first, TiffBasedRole.Primary, 4);
        while (pending.Count != 0)
        {
            (uint offset, TiffBasedRole role) = pending.Dequeue();
            ReadDirectory(offset, role);
        }
        foreach (KeyValuePair<uint, int> extra in extraReferences)
            if (seen[extra.Key] is TiffBasedDirectory shared)
                shared.References += extra.Value;
    }

    private void Schedule(uint offset, TiffBasedRole role, long pointerAt)
    {
        if (offset == 0)
            throw Invalid("A directory pointer is null.", pointerAt);
        if (seen.ContainsKey(offset))
        {
            // A second way to the same directory: it is not walked again, and it is no longer the
            // property of one parent.
            extraReferences[offset] = extraReferences.GetValueOrDefault(offset) + 1;
            return;
        }
        if (seen.Count >= limits.MaxDirectories)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The directory budget was exceeded.", pointerAt);
        seen.Add(offset, null);
        pending.Enqueue((offset, role));
    }

    private void ReadDirectory(uint offset, TiffBasedRole role)
    {
        if (offset > file.Length - 2L)
            throw Invalid("A directory lies outside the file.", offset);
        int start = (int)offset;
        int count = U16(start);
        if (count > limits.MaxEntriesPerDirectory || count > limits.MaxTotalEntries - totalEntries)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The directory entry budget was exceeded.", offset);
        totalEntries += count;
        int length = 2 + 12 * count + 4;
        if (length > file.Length - start)
            throw Invalid("A directory lies outside the file.", offset);
        Account(new(TiffBasedRangeKind.Directory, start, length), offset);

        var fields = new List<TiffBasedField>(count);
        bool sorted = true;
        for (int i = 0; i < count; i++)
        {
            int p = start + 2 + 12 * i;
            ushort tag = U16(p);
            ushort type = U16(p + 2);
            uint valueCount = U32(p + 4);
            int width = Width(type);
            if (width == 0)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "A field type of unknown width leaves a part of the file unaccounted for.", p + 2);
            long bytes = (long)valueCount * width;
            bool inline = bytes <= 4;
            long valueOffset = inline ? p + 8L : U32(p + 8);
            if (i != 0 && tag <= fields[i - 1].Tag) sorted = false;
            fields.Add(new(tag, type, valueCount, p, inline, valueOffset, bytes));
            bool makerNote = (role == TiffBasedRole.Exif && tag == 0x927C) || (role == TiffBasedRole.Primary && tag == 0xC634);
            if (!inline)
                AddRange(makerNote ? TiffBasedRangeKind.MakerNote : TiffBasedRangeKind.Value, valueOffset, bytes);
        }
        uint next = U32(start + 2 + 12 * count);
        var directory = new TiffBasedDirectory(role, offset, fields, next, sorted);
        directories.Add(directory);
        seen[offset] = directory;

        foreach (TiffBasedField field in fields)
            FollowPointer(directory, field);
        if (role is TiffBasedRole.Primary or TiffBasedRole.Other)
        {
            AddImageData(directory, 0x0111, 0x0117);
            AddImageData(directory, 0x0144, 0x0145);
            AddImageData(directory, 0x0201, 0x0202);
            if (rw2 && role == TiffBasedRole.Primary)
                AddRawDataOffset(directory);
            // Exif, GPS and Interoperability directories have no successor; some cameras leave
            // noise in that field, so it is kept as it is and not followed.
            if (next != 0)
                Schedule(next, TiffBasedRole.Other, start + 2 + 12 * count);
        }
    }

    private void FollowPointer(TiffBasedDirectory directory, TiffBasedField field)
    {
        TiffBasedRole role = directory.Role;
        bool image = role is TiffBasedRole.Primary or TiffBasedRole.Other;
        TiffBasedRole target;
        if (field.Tag == 0x8769 && image) target = role == TiffBasedRole.Primary ? TiffBasedRole.Exif : TiffBasedRole.Other;
        else if (field.Tag == 0x8825 && image) target = role == TiffBasedRole.Primary ? TiffBasedRole.Gps : TiffBasedRole.Other;
        else if (field.Tag == 0xA005 && role is TiffBasedRole.Exif or TiffBasedRole.Other)
            target = role == TiffBasedRole.Exif ? TiffBasedRole.Interop : TiffBasedRole.Other;
        else if (field.Tag == 0x014A && image) target = TiffBasedRole.Other;
        else return;

        // LONG, or the IFD type some writers use for the same thing.
        if (field.Type is not (4 or 13) || field.Count == 0 || (field.Tag != 0x014A && field.Count != 1))
            throw Invalid("A directory pointer has an invalid type or count.", field.EntryOffset);
        if (field.ValueOffset > file.Length - field.ByteLength)
            throw Invalid("A list of directory pointers lies outside the file.", field.EntryOffset);
        for (uint i = 0; i < field.Count; i++)
        {
            long at = field.ValueOffset + 4L * i;
            Schedule(U32((int)at), target, at);
        }
    }

    /// <summary>
    /// Records the image data one pair of tags addresses. With both tags present and equally long,
    /// each range is known. With the offsets alone the data is taken to run to the end of the file.
    /// </summary>
    private void AddImageData(TiffBasedDirectory directory, ushort offsetsTag, ushort countsTag)
    {
        TiffBasedField? offsets = directory.Find(offsetsTag);
        if (offsets is null || offsets.Count == 0) return;
        if (offsets.Type is not (3 or 4 or 13))
            throw Invalid("Image data offsets have an invalid type.", offsets.EntryOffset);
        if (offsets.ValueOffset > file.Length - offsets.ByteLength)
            return; // the list itself is past the end; that was recorded when its range was added
        TiffBasedField? counts = directory.Find(countsTag);
        bool known = counts is not null && counts.Count == offsets.Count && counts.Type is 3 or 4 &&
            counts.ValueOffset <= file.Length - counts.ByteLength;
        for (uint i = 0; i < offsets.Count; i++)
        {
            long at = Element(offsets, i);
            if (known)
            {
                long size = Element(counts!, i);
                if (size != 0) AddImageRange(at, size);
            }
            else if (at < file.Length)
            {
                HasOpenEndedImageData = true;
                AddImageRange(at, file.Length - at);
            }
            else
            {
                HasRangePastEnd = true;
            }
        }
    }

    /// <summary>
    /// An RW2 file of the later kind stores the address of its raw data in tag 0x0118 and no length:
    /// the data runs to the end of the file.
    /// </summary>
    private void AddRawDataOffset(TiffBasedDirectory directory)
    {
        TiffBasedField? field = directory.Find(0x0118);
        if (field is null || field.Type != 4 || field.Count != 1) return;
        long at = U32((int)field.ValueOffset);
        HasOpenEndedImageData = true;
        if (at < file.Length) AddImageRange(at, file.Length - at);
        else HasRangePastEnd = true;
    }

    private void AddImageRange(long start, long length)
    {
        // Strips usually follow one another; joined, a file of many strips is a handful of ranges.
        if (ranges.Count != 0 && ranges[^1] is { Kind: TiffBasedRangeKind.ImageData } last && last.End == start)
        {
            ranges[^1] = last with { Length = last.Length + length };
            if (start + length > file.Length) HasRangePastEnd = true;
            return;
        }
        AddRange(TiffBasedRangeKind.ImageData, start, length);
    }

    private void AddRange(TiffBasedRangeKind kind, long start, long length)
    {
        if (length == 0) return;
        if (start > file.Length || length > file.Length - start) HasRangePastEnd = true;
        Account(new(kind, start, length), start);
    }

    private void Account(TiffBasedRange range, long at)
    {
        if (ranges.Count >= maxRanges)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The accounted range budget was exceeded.", at);
        ranges.Add(range);
    }

    private long Element(TiffBasedField field, uint index) => field.Type == 3
        ? U16((int)(field.ValueOffset + 2L * index))
        : U32((int)(field.ValueOffset + 4L * index));

    /// <summary>The byte width of a field type: the twelve of TIFF 6.0 and IFD (13); zero for any other.</summary>
    internal static int Width(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12 => 8,
        _ => 0
    };

    private ushort U16(int at) => Order == Endianness.LittleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at, 2))
        : BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at, 2));

    private uint U32(int at) => Order == Endianness.LittleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at, 4));

    private static MetadataException Invalid(string message, long at) =>
        new(MetadataErrorCode.InvalidTiff, message, at);
}
