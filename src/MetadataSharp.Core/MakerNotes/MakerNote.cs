using System.Diagnostics.CodeAnalysis;

namespace MetadataSharp.Core;

/// <summary>The vendor layout a decoded MakerNote was read with.</summary>
public enum MakerNoteFormat
{
    /// <summary>Canon: a headerless IFD whose value offsets are relative to the TIFF header.</summary>
    Canon
}

/// <summary>One tag of a decoded MakerNote directory.</summary>
public sealed class MakerNoteEntry
{
    internal MakerNoteEntry(ushort tagId, string? name, ExifValue value)
    {
        TagId = tagId;
        Name = name;
        Value = value;
    }

    public ushort TagId { get; }

    /// <summary>The tag's name, or null when this release does not know the tag.</summary>
    public string? Name { get; }

    public ExifValue Value { get; }
}

/// <summary>
/// A read-only view of a recognised MakerNote. The raw MakerNote bytes remain the authoritative
/// value of the EXIF entry; this view is never written back and does not make MakerNote editing safe.
/// </summary>
public sealed class MakerNote
{
    private readonly Dictionary<ushort, MakerNoteEntry> lookup;

    internal MakerNote(MakerNoteFormat format, Endianness byteOrder, IReadOnlyList<MakerNoteEntry> entries)
    {
        Format = format;
        ByteOrder = byteOrder;
        var copy = new MakerNoteEntry[entries.Count];
        lookup = new Dictionary<ushort, MakerNoteEntry>(entries.Count);
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = entries[i];
            lookup.TryAdd(entries[i].TagId, entries[i]);
        }
        Entries = Array.AsReadOnly(copy);
    }

    public MakerNoteFormat Format { get; }
    public Endianness ByteOrder { get; }

    /// <summary>Entries in directory order.</summary>
    public IReadOnlyList<MakerNoteEntry> Entries { get; }

    public bool TryGetEntry(ushort tagId, [NotNullWhen(true)] out MakerNoteEntry? entry) =>
        lookup.TryGetValue(tagId, out entry);
}
