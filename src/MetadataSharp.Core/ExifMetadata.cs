using System.Diagnostics.CodeAnalysis;

namespace MetadataSharp.Core;

/// <summary>An immutable metadata snapshot independent of input buffers and source lifetime.</summary>
public sealed partial class ExifMetadata
{
    private readonly Dictionary<ExifTagKey, ExifEntry> lookup;

    // Preserve the parsed MakerNote for read access even when the enclosing block cannot be safely edited.
    internal ExifMetadata(Endianness byteOrder, IReadOnlyList<ExifEntry> entries, MakerNote? makerNote,
        bool editable = true, IReadOnlyList<ExifSubImage>? subImages = null)
        : this(byteOrder, entries)
    {
        MakerNote = makerNote;
        IsEditable = editable;
        if (subImages is null || subImages.Count == 0)
            return;
        var copied = new ExifSubImage[subImages.Count];
        for (int i = 0; i < copied.Length; i++)
            copied[i] = subImages[i] ?? throw new ArgumentException("Sub-image directories cannot be null.", nameof(subImages));
        SubImages = Array.AsReadOnly(copied);
    }

    // Validate before publishing either view so the ordered entries and keyed lookup describe one immutable block.
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

    /// <summary>
    /// The image directories of a whole TIFF-based file that its primary directory names with tag
    /// 0x014A (SubIFDs), in the order that tag lists them, empty when the file has none. Their tags are
    /// listed by <see cref="ExifSubImage.Entries"/> and are not part of <see cref="Entries"/>: they
    /// describe pictures, and neither an edit nor the layout planner reaches them.
    /// </summary>
    public IReadOnlyList<ExifSubImage> SubImages { get; } = Array.Empty<ExifSubImage>();

    /// <summary>Looks up a directory/tag pair without interpreting the tag ID.</summary>
    /// <param name="key">The directory and tag ID to find.</param>
    /// <param name="entry">Receives the matching entry when found; otherwise receives <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the key is present; otherwise <see langword="false"/>.</returns>
    public bool TryGetEntry(ExifTagKey key, [NotNullWhen(true)] out ExifEntry? entry) =>
        lookup.TryGetValue(key, out entry);
}
