namespace MetadataSharp.Core;

/// <summary>An immutable raw tag entry. Instances are supplied by the metadata reader.</summary>
public sealed class ExifEntry
{
    // Pair a valid directory key with its already-owned immutable value; entry snapshots never borrow parser storage.
    internal ExifEntry(ExifTagKey key, ExifValue value)
    {
        if (!Enum.IsDefined(key.Directory))
            throw new ArgumentOutOfRangeException(nameof(key));
        ArgumentNullException.ThrowIfNull(value);
        Key = key;
        Value = value;
    }

    /// <summary>The directory role and tag ID of the entry.</summary>
    public ExifTagKey Key { get; }
    /// <summary>The raw value of the entry.</summary>
    public ExifValue Value { get; }
}
