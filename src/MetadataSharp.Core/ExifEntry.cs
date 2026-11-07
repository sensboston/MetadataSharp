namespace MetadataSharp.Core;

/// <summary>An immutable raw tag entry. Instances are supplied by the metadata reader.</summary>
public sealed class ExifEntry
{
    internal ExifEntry(ExifTagKey key, ExifValue value)
    {
        if (!Enum.IsDefined(key.Directory))
            throw new ArgumentOutOfRangeException(nameof(key));
        ArgumentNullException.ThrowIfNull(value);
        Key = key;
        Value = value;
    }

    public ExifTagKey Key { get; }
    public ExifValue Value { get; }
}
