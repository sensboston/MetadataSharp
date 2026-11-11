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

    /// <summary>Pentax, DNG style (also found in DNGPrivateData): a "PENTAX" signature and two bytes, then an IFD
    /// in the TIFF byte order; offsets relative to the MakerNote.</summary>
    PentaxDng,

    /// <summary>Sony: a "SONY DSC" signature, then an IFD; offsets relative to the TIFF header.</summary>
    Sony1,

    /// <summary>Sony: a bare IFD; offsets relative to the TIFF header.</summary>
    Sony2,

    /// <summary>Panasonic: a signature, then an IFD with no next-IFD pointer; offsets relative to the TIFF header.</summary>
    Panasonic,

    /// <summary>Olympus, older cameras: an "OLYMP" signature, then an IFD; offsets relative to the TIFF header.</summary>
    Olympus,

    /// <summary>Olympus: an "OLYMPUS" signature and a byte order, then an IFD whose offsets, and those of its
    /// sub-directories (reported as the fields of their pointer tags), are relative to the MakerNote.</summary>
    Olympus2,

    /// <summary>OM System: as <see cref="Olympus2"/>, with an "OM SYSTEM" signature.</summary>
    OMSystem,

    /// <summary>Minolta and Konica Minolta: a headerless IFD in the TIFF byte order; offsets relative to the
    /// TIFF header. The camera-settings arrays are big-endian whatever that byte order is.</summary>
    Minolta,

    /// <summary>Casio, early cameras: a headerless IFD in the TIFF byte order; offsets relative to the TIFF header.</summary>
    Casio,

    /// <summary>Casio: a "QVC" signature, then a big-endian IFD; offsets relative to the TIFF header.</summary>
    Casio2,

    /// <summary>Samsung: a headerless IFD in the TIFF byte order; offsets relative to the MakerNote. A Samsung
    /// MakerNote with an "AOC" signature is a <see cref="Pentax"/> one and is reported as such.</summary>
    Samsung2,

    /// <summary>Sigma and Foveon: a "SIGMA" or "FOVEON" signature and two bytes, then an IFD in the TIFF byte
    /// order; offsets relative to the TIFF header.</summary>
    Sigma
}

/// <summary>One named field inside an array-valued MakerNote tag, or one entry of a sub-directory.</summary>
public sealed class MakerNoteField
{
    internal MakerNoteField(ushort index, string name, ExifValue value, IReadOnlyList<MakerNoteField>? fields = null)
    {
        Index = index;
        Name = name;
        Value = value;
        Fields = fields ?? [];
    }

    /// <summary>The position of the field's first element in the parent array; for an entry of a
    /// sub-directory, its tag number.</summary>
    public ushort Index { get; }
    public string Name { get; }
    public ExifValue Value { get; }

    /// <summary>
    /// The named elements of this field when it is an entry of a sub-directory and is itself an array
    /// whose layout this release knows; empty otherwise.
    /// </summary>
    public IReadOnlyList<MakerNoteField> Fields { get; }
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
    /// The named fields of an array-valued tag whose layout this release knows, in element order, or
    /// the entries of the sub-directory a pointer tag leads to; empty otherwise. Elements without a
    /// known meaning are not listed.
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

    /// <summary>
    /// Where the note starts, counted from the first byte of the TIFF header of the EXIF block it was
    /// read from: the value offset of the MakerNote entry, or of DNGPrivateData for a note stored there.
    /// Offsets that a vendor counts from the TIFF header are only meaningful while the note stays here.
    /// </summary>
    public long Offset { get; internal set; }

    /// <summary>Entries in directory order.</summary>
    public IReadOnlyList<MakerNoteEntry> Entries { get; }

    public bool TryGetEntry(ushort tagId, [NotNullWhen(true)] out MakerNoteEntry? entry) =>
        lookup.TryGetValue(tagId, out entry);

    /// <summary>
    /// The human-readable form of the value of one of this note's tags, or null when this release has
    /// none for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An enumerated tag answers with a short label (<c>Adobe RGB</c>, <c>Lossless</c>), a counted one
    /// with its unit (<c>5400 K</c>, <c>6000 x 4000</c>). A number an interpreted tag has no label for
    /// answers as stored, in parentheses: <c>(9)</c> says that this release does not name the number,
    /// not that the vendor gives it no meaning.
    /// </para>
    /// <para>
    /// The vocabulary is small on purpose: Canon, Nikon (the <see cref="MakerNoteFormat.Nikon3" />
    /// layout) and Sony, and of those the tags and numbers this project has checked against real
    /// files. Null means there is nothing to add to the raw value: another vendor, a tag outside the
    /// vocabulary, or a value that does not have the type and count the interpretation needs. The raw
    /// value stays the truth; nothing is ever thrown for its content.
    /// </para>
    /// </remarks>
    /// <param name="entry">A tag of this note's own directory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry" /> is null.</exception>
    public string? Interpret(MakerNoteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return MakerNotes.MakerNoteLabels.Interpret(Format, entry);
    }

    /// <summary>
    /// The human-readable form of one field of an array-valued tag, or of one entry of the
    /// sub-directory a pointer tag leads to; null when this release has none for it. The rules are those
    /// of <see cref="Interpret(MakerNoteEntry)" />.
    /// </summary>
    /// <param name="entry">The tag the field belongs to.</param>
    /// <param name="field">One of the tag's <see cref="MakerNoteEntry.Fields" />.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public string? Interpret(MakerNoteEntry entry, MakerNoteField field)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(field);
        return MakerNotes.MakerNoteLabels.Interpret(Format, entry, field);
    }
}
