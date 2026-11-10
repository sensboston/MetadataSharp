using System.Diagnostics.CodeAnalysis;

namespace MetadataSharp.Core;

/// <summary>The vendor layout a decoded MakerNote was read with.</summary>
public enum MakerNoteFormat
{
    /// <summary>Canon: a headerless IFD whose value offsets are relative to the TIFF header.</summary>
    Canon,

    /// <summary>Nikon, early Coolpix: a headerless IFD whose value offsets are relative to the TIFF header.</summary>
    Nikon1,

    /// <summary>Nikon, Coolpix: an 8-byte "Nikon" signature, then an IFD whose value offsets are relative to the TIFF header.</summary>
    Nikon2,

    /// <summary>Nikon, most cameras: a "Nikon" signature and a version, then an embedded TIFF header (with its own
    /// byte order) that the IFD and its value offsets are relative to.</summary>
    Nikon3,

    /// <summary>Fujifilm: a signature and the IFD offset; little-endian, offsets relative to the MakerNote.</summary>
    Fujifilm,

    /// <summary>Pentax: an "AOC" signature and a byte order, then an IFD; offsets relative to the TIFF header.</summary>
    Pentax,

    /// <summary>Pentax, DNG style: a "PENTAX" signature and a byte order, then an IFD; offsets relative to the MakerNote.</summary>
    PentaxDng,

    /// <summary>Sony: a "SONY DSC" signature, then an IFD; offsets relative to the TIFF header.</summary>
    Sony1,

    /// <summary>Sony: a bare IFD; offsets relative to the TIFF header.</summary>
    Sony2,

    /// <summary>Panasonic: a signature, then an IFD with no next-IFD pointer; offsets relative to the TIFF header.</summary>
    Panasonic
}

/// <summary>One named field inside an array-valued MakerNote tag.</summary>
public sealed class MakerNoteField
{
    internal MakerNoteField(ushort index, string name, ExifValue value)
    {
        Index = index;
        Name = name;
        Value = value;
    }

    /// <summary>The position of the field's first element in the parent array.</summary>
    public ushort Index { get; }
    public string Name { get; }
    public ExifValue Value { get; }
}

/// <summary>One tag of a decoded MakerNote directory.</summary>
public sealed class MakerNoteEntry
{
    internal MakerNoteEntry(ushort tagId, string? name, ExifValue value, IReadOnlyList<MakerNoteField> fields)
    {
        TagId = tagId;
        Name = name;
        Value = value;
        Fields = fields;
    }

    public ushort TagId { get; }

    /// <summary>The tag's name, or null when this release does not know the tag.</summary>
    public string? Name { get; }

    /// <summary>The whole raw value, also for tags that are split into <see cref="Fields"/>.</summary>
    public ExifValue Value { get; }

    /// <summary>
    /// The named fields of an array-valued tag whose layout this release knows, in element order;
    /// empty otherwise. Elements without a known meaning are not listed.
    /// </summary>
    public IReadOnlyList<MakerNoteField> Fields { get; }
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
