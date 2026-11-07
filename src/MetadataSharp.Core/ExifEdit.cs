using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Tiff.Serialization;

namespace MetadataSharp.Core;

/// <summary>A mutable set of addressed EXIF changes. Last operation for a key wins.</summary>
public sealed class ExifEdit
{
    private readonly Dictionary<ExifTagKey, ExifValue?> changes = [];
    private bool removeAll;
    public ExifEdit() { }
    public bool IsEmpty => !removeAll && changes.Count == 0;

    public void Set(ExifTagKey key, ExifValue value)
    {
        CheckKey(key);
        ArgumentNullException.ThrowIfNull(value);
        try { ExifLayoutPlanner.ValidateShape(new ExifEntry(key, value)); }
        catch (MetadataException e) { throw new ArgumentException("The value is outside the supported tag shape.", nameof(value), e); }
        changes[key] = value;
    }

    public void Remove(ExifTagKey key)
    {
        CheckKey(key);
        changes[key] = null;
    }

    public void RemoveAllExif()
    {
        if (changes.Count != 0) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        removeAll = true;
    }

    private void CheckKey(ExifTagKey key)
    {
        if (removeAll) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        bool known = (key.Directory, key.TagId) switch
        {
            (ExifDirectoryKind.Ifd0, 0x010E or 0x010F or 0x0110 or 0x0112 or 0x0131 or 0x0132 or 0x013B or 0x8298) => true,
            (ExifDirectoryKind.Exif, 0x829A or 0x829D or 0x9003 or 0x9004 or 0x920A) => true,
            (ExifDirectoryKind.Gps, <= 6) => true,
            _ => false
        };
        if (!known) throw new ArgumentException("The key is unknown, structural or outside the writable tag subset.", nameof(key));
    }

    internal EditSnapshot Snapshot() => new(removeAll, new Dictionary<ExifTagKey, ExifValue?>(changes));

    internal sealed record EditSnapshot(bool RemoveAll, IReadOnlyDictionary<ExifTagKey, ExifValue?> Changes)
    {
        internal bool IsEmpty => !RemoveAll && Changes.Count == 0;
        internal ExifMetadata Apply(TiffReadResult? source, Endianness order, MetadataLimits limits)
        {
            var entries = source?.Metadata.Entries.ToDictionary(e => e.Key, e => e.Value) ?? [];
            foreach (var change in Changes)
            {
                if (change.Value is null) entries.Remove(change.Key);
                else entries[change.Key] = change.Value;
            }
            if (entries.Count > limits.MaxTotalEntries)
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "The edited entry count exceeds its budget.");
            long bytes = source?.Metadata.Entries.Sum(e => (long)e.Value.ByteLength) ?? 0;
            foreach (ExifValue value in entries.Values)
            {
                bytes += value.ByteLength;
                if (value.ByteLength > limits.MaxValueBytes || bytes > limits.MaxRetainedMetadataBytes)
                    throw new MetadataException(MetadataErrorCode.LimitExceeded, "The edited values exceed the metadata budget.");
            }
            return new ExifMetadata(order, entries.Select(e => new ExifEntry(e.Key, ConvertOrder(e.Value, order))).ToArray());
        }
    }

    internal static ExifValue ConvertOrder(ExifValue value, Endianness order)
    {
        if (value.ByteOrder == order) return value;
        byte[] bytes = value.RawBytes.ToArray();
        int width = value.FieldType is TiffFieldType.Rational or TiffFieldType.SRational ? 4 : ExifValue.GetWidth(value.FieldType);
        if (width > 1) for (int i = 0; i < bytes.Length; i += width) bytes.AsSpan(i, width).Reverse();
        return ExifValue.FromRaw(value.FieldType, value.Count, order, bytes);
    }
}
