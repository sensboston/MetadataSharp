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

/// <summary>
/// Where an entry of a MakerNote directory lies in the TIFF bytes it was read from: what a writer
/// needs to change the value where it is.
/// </summary>
/// <param name="Directory">The offset of the directory (its entry count).</param>
/// <param name="Entry">The offset of the twelve bytes of the entry.</param>
/// <param name="Value">The offset of the value: in the entry, or where its offset leads.</param>
/// <param name="ByteLength">The length of the value.</param>
/// <param name="Little">True when the directory is little-endian.</param>
/// <param name="RegionEnd">The end of the MakerNote (of the TIFF bytes for a directory outside it).</param>
/// <param name="OffsetBase">What the offsets of values count from: 0 for the TIFF header, else an offset inside the MakerNote.</param>
/// <param name="Delta">The correction the reader adds to a stored offset (Canon's footer).</param>
internal sealed record MakerNoteSlot(int Directory, int Entry, int Value, int ByteLength, bool Little, int RegionEnd, int OffsetBase, long Delta);

/// <summary>One named field inside an array-valued MakerNote tag, or one entry of a sub-directory.</summary>
public sealed class MakerNoteField
{
    // Capture the vendor, raw value, decoded fields, and source offset for one parsed MakerNote.
    internal MakerNoteField(ushort index, string name, ExifValue value, IReadOnlyList<MakerNoteField>? fields = null, int offset = -1)
    {
        Index = index;
        Name = name;
        Value = value;
        Fields = fields ?? [];
        Offset = offset;
    }

    /// <summary>Where the field starts in the bytes of the array it was split from, deciphered; -1 for a field that is an entry of a directory.</summary>
    internal int Offset { get; }

    /// <summary>Where the entry of a field of a sub-directory lies; null for a field of an array.</summary>
    internal MakerNoteSlot? Slot { get; init; }

    /// <summary>The position of the field's first element in the parent array; for an entry of a
    /// sub-directory, its tag number.</summary>
    public ushort Index { get; }
    /// <summary>The name of the field.</summary>
    public string Name { get; }
    /// <summary>The raw value of the field.</summary>
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
    // Capture the vendor, raw value, decoded fields, and source offset for one parsed MakerNote.
    internal MakerNoteEntry(ushort tagId, string? name, ExifValue value, IReadOnlyList<MakerNoteField> fields)
    {
        TagId = tagId;
        Name = name;
        Value = value;
        Fields = fields;
    }

    /// <summary>Where the entry lies in the bytes it was read from.</summary>
    internal MakerNoteSlot? Slot { get; init; }

    /// <summary>The stored bytes of an enciphered array made plain, and plain bytes made the stored ones; null for a value stored in the clear.</summary>
    internal Func<byte[], byte[]>? Decode { get; init; }

    /// <inheritdoc cref="Decode"/>
    internal Func<byte[], byte[]>? Encode { get; init; }

    /// <summary>The entry with other fields, where it lies and how it is enciphered kept.</summary>
    // Replace decoded fields while preserving slot and codec metadata needed by a later copy operation.
    internal MakerNoteEntry With(IReadOnlyList<MakerNoteField> fields, Func<byte[], byte[]>? decode = null, Func<byte[], byte[]>? encode = null) =>
        new(TagId, Name, Value, fields) { Slot = Slot, Decode = decode ?? Decode, Encode = encode ?? Encode };

    /// <summary>The tag ID in the MakerNote directory.</summary>
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
/// value of the EXIF entry; this view is never written back. A tag of the note is changed through
/// <see cref="ExifEdit.SetByKey(string, ExifValue, bool)"/>, where it lies in the file.
/// </summary>
public sealed class MakerNote
{
    private readonly Dictionary<ushort, MakerNoteEntry> lookup;

    // Snapshot directory order and retain the first duplicate tag for deterministic lookup.
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

    /// <summary>The vendor layout the note was recognised as.</summary>
    public MakerNoteFormat Format { get; }
    /// <summary>The byte order the note was read in.</summary>
    public Endianness ByteOrder { get; }

    /// <summary>
    /// Where the note starts, counted from the first byte of the TIFF header of the EXIF block it was
    /// read from: the value offset of the MakerNote entry, or of DNGPrivateData for a note stored there.
    /// Offsets that a vendor counts from the TIFF header are only meaningful while the note stays here.
    /// </summary>
    public long Offset { get; internal set; }

    /// <summary>Entries in directory order.</summary>
    public IReadOnlyList<MakerNoteEntry> Entries { get; }

    /// <summary>Looks up an entry by tag ID; the first one when the directory repeats a tag.</summary>
    /// <param name="tagId">The numeric MakerNote tag ID.</param>
    /// <param name="entry">Receives the first matching entry, or <see langword="null"/> when none exists.</param>
    /// <returns><see langword="true"/> when the tag is present; otherwise, <see langword="false"/>.</returns>
    public bool TryGetEntry(ushort tagId, [NotNullWhen(true)] out MakerNoteEntry? entry) =>
        lookup.TryGetValue(tagId, out entry);

    /// <summary>
    /// The key of one of this note's tags, as Exiv2 names it and <see cref="ExifEdit.SetByKey(string, ExifValue, bool)"/>
    /// takes it: <c>Exif.Canon.OwnerName</c>; <c>Exif.Fujifilm.0x1234</c> for a tag without a name.
    /// </summary>
    /// <exception cref="ArgumentException">The entry is not one of this note.</exception>
    /// <param name="entry">An entry in this note's top-level directory.</param>
    /// <returns>The dotted EXIF key for the entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public string GetKey(MakerNoteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        foreach (MakerNotes.MakerNoteTarget target in MakerNotes.MakerNoteKeys.Enumerate(this))
            if (ReferenceEquals(target.Entry, entry) && target.Field is null) return target.Key;
        throw new ArgumentException("The entry is not one of this MakerNote.", nameof(entry));
    }

    /// <summary>
    /// The key of a field of an array-valued tag, or of an entry of the sub-directory a pointer tag
    /// leads to: <c>Exif.CanonCs.Quality</c>, <c>Exif.OlympusCs.ExposureMode</c>. For one of the
    /// <see cref="MakerNoteField.Fields"/> of such an entry, pass that element too.
    /// </summary>
    /// <exception cref="ArgumentException">The field is not one of the entry, or the entry not one of this note.</exception>
    /// <param name="entry">The top-level tag or subdirectory entry containing the field.</param>
    /// <param name="field">The array field or subdirectory tag.</param>
    /// <param name="element">The field within an array field, or null when <paramref name="field"/> is itself the target.</param>
    /// <returns>The dotted EXIF key for the field or entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> or <paramref name="field"/> is null.</exception>
    public string GetKey(MakerNoteEntry entry, MakerNoteField field, MakerNoteField? element = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(field);
        foreach (MakerNotes.MakerNoteTarget target in MakerNotes.MakerNoteKeys.Enumerate(this))
            if (ReferenceEquals(target.Entry, entry) && ReferenceEquals(target.Field, field) && ReferenceEquals(target.Element, element)) return target.Key;
        throw new ArgumentException("The field is not one of this MakerNote.", nameof(field));
    }

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
    /// The vocabulary is deliberately limited to checked fields of Canon, the Nikon 1, Nikon 2 and Nikon 3
    /// layouts, Sony and Pentax notes. Null means there is
    /// nothing to add to the raw value: another vendor, a tag outside the vocabulary, or a value that
    /// does not have the type and count the interpretation needs. The raw
    /// value stays the truth; nothing is ever thrown for its content.
    /// </para>
    /// </remarks>
    /// <param name="entry">A tag of this note's own directory.</param>
    /// <returns>The human-readable value, or <see langword="null"/> when no interpretation is available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry" /> is null.</exception>
    public string? Interpret(MakerNoteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return MakerNotes.MakerNoteLabels.Interpret(this, entry);
    }

    /// <summary>Reads an entry, supplying the enclosing image model when a vendor needs it.</summary>
    /// <param name="entry">A tag of this note's own directory.</param>
    /// <param name="cameraModel">The enclosing EXIF image model, if known.</param>
    /// <returns>The human-readable value, or <see langword="null"/> when no interpretation is available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry" /> is null.</exception>
    public string? InterpretForModel(MakerNoteEntry entry, string? cameraModel)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return MakerNotes.MakerNoteLabels.Interpret(this, entry, cameraModel);
    }

    /// <summary>
    /// The human-readable form of one field of an array-valued tag, or of one entry of the
    /// sub-directory a pointer tag leads to; null when this release has none for it. The rules are those
    /// of <see cref="Interpret(MakerNoteEntry)" />.
    /// </summary>
    /// <param name="entry">The tag the field belongs to.</param>
    /// <param name="field">One of the tag's <see cref="MakerNoteEntry.Fields" />.</param>
    /// <returns>The human-readable value, or <see langword="null"/> when no interpretation is available.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public string? Interpret(MakerNoteEntry entry, MakerNoteField field)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(field);
        return MakerNotes.MakerNoteLabels.Interpret(this, entry, field);
    }

    /// <summary>Reads a field, supplying the enclosing image model when a vendor needs it.</summary>
    /// <param name="entry">The tag the field belongs to.</param>
    /// <param name="field">One of the tag's <see cref="MakerNoteEntry.Fields" />.</param>
    /// <param name="cameraModel">The enclosing EXIF image model, if known.</param>
    /// <returns>The human-readable value, or <see langword="null"/> when no interpretation is available.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public string? InterpretFieldForModel(MakerNoteEntry entry, MakerNoteField field, string? cameraModel)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(field);
        return MakerNotes.MakerNoteLabels.Interpret(this, entry, field, cameraModel);
    }
}
