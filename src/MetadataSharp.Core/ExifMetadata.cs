using System.Diagnostics.CodeAnalysis;

namespace MetadataSharp.Core;

/// <summary>An immutable metadata snapshot independent of input buffers and source lifetime.</summary>
public sealed class ExifMetadata
{
    private readonly Dictionary<ExifTagKey, ExifEntry> lookup;

    internal ExifMetadata(Endianness byteOrder, IReadOnlyList<ExifEntry> entries, MakerNote? makerNote,
        bool editable = true)
        : this(byteOrder, entries)
    {
        MakerNote = makerNote;
        IsEditable = editable;
    }

    internal ExifMetadata(Endianness byteOrder, IReadOnlyList<ExifEntry> entries)
    {
        if (byteOrder is not Endianness.LittleEndian and not Endianness.BigEndian)
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        ArgumentNullException.ThrowIfNull(entries);
        var copy = new ExifEntry[entries.Count];
        lookup = new Dictionary<ExifTagKey, ExifEntry>(entries.Count);
        for (int i = 0; i < copy.Length; i++)
        {
            ExifEntry entry = entries[i];
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.Value.ByteOrder != byteOrder)
                throw new ArgumentException("All entries must use the metadata byte order.", nameof(entries));
            if (!lookup.TryAdd(entry.Key, entry))
                throw new ArgumentException("Duplicate tag keys are not supported.", nameof(entries));
            copy[i] = entry;
        }
        ByteOrder = byteOrder;
        Entries = Array.AsReadOnly(copy);
    }

    /// <summary>The byte order of the block and of every value in it.</summary>
    public Endianness ByteOrder { get; }
    /// <summary>Every entry of the block, across its directories.</summary>
    public IReadOnlyList<ExifEntry> Entries { get; }

    /// <summary>
    /// False when the block was read under the read-only rules: it holds a structure the editable
    /// profile refuses (an unknown field type, whose entry is left out; strip, tile or SubIFD tags;
    /// values that share bytes), so a document returns it for reading and refuses to edit it.
    /// </summary>
    public bool IsEditable { get; } = true;

    /// <summary>
    /// A read-only decoding of the MakerNote when its vendor layout is recognised, otherwise null.
    /// Null after an edit: the decoded view describes the bytes as read, not a planned output.
    /// </summary>
    public MakerNote? MakerNote { get; }

    /// <summary>Looks up a directory/tag pair without interpreting the tag ID.</summary>
    public bool TryGetEntry(ExifTagKey key, [NotNullWhen(true)] out ExifEntry? entry) =>
        lookup.TryGetValue(key, out entry);
}
