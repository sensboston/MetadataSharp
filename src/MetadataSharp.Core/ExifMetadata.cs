using System.Diagnostics.CodeAnalysis;

namespace MetadataSharp.Core;

/// <summary>An immutable metadata snapshot independent of input buffers and source lifetime.</summary>
public sealed class ExifMetadata
{
    private readonly Dictionary<ExifTagKey, ExifEntry> lookup;

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

    public Endianness ByteOrder { get; }
    public IReadOnlyList<ExifEntry> Entries { get; }

    /// <summary>Looks up a directory/tag pair without interpreting the tag ID.</summary>
    public bool TryGetEntry(ExifTagKey key, [NotNullWhen(true)] out ExifEntry? entry) =>
        lookup.TryGetValue(key, out entry);
}
