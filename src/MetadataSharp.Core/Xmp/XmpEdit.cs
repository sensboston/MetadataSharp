namespace MetadataSharp.Core.Xmp;

/// <summary>Addressed XMP changes. Last operation wins; whole removal cannot be mixed.</summary>
public sealed class XmpEdit
{
    private readonly Dictionary<XmpPropertyName, XmpValue?> changes = [];
    private bool removeAll;
    /// <summary>Creates an edit that holds no change.</summary>
    public XmpEdit() { }
    /// <summary>True when the edit holds no change.</summary>
    public bool IsEmpty => !removeAll && changes.Count == 0;
    /// <summary>Sets a top-level property, replacing its value whatever form it had.</summary>
    /// <exception cref="ArgumentException">The name is not valid, the edit already removes all XMP, or the edit names more than 2,048 properties.</exception>
    public void Set(XmpPropertyName name, XmpValue value)
    {
        Check(name); ArgumentNullException.ThrowIfNull(value); changes[name] = value;
    }
    /// <summary>Removes a top-level property; one the packet does not have is no change.</summary>
    public void Remove(XmpPropertyName name) { Check(name); changes[name] = null; }
    /// <summary>Removes the whole XMP packet.</summary>
    /// <exception cref="ArgumentException">The edit already holds a property change.</exception>
    public void RemoveAllXmp()
    {
        if (changes.Count != 0) throw new ArgumentException("Whole XMP removal cannot be mixed with property changes.");
        removeAll = true;
    }
    private void Check(XmpPropertyName name)
    {
        if (removeAll) throw new ArgumentException("Whole XMP removal cannot be mixed with property changes.");
        name.Validate();
        if (!changes.ContainsKey(name) && changes.Count >= 2048) throw new ArgumentException("Edit exceeds the default property budget.");
    }
    internal (bool RemoveAll, Dictionary<XmpPropertyName, XmpValue?> Changes) Snapshot() => (removeAll, new(changes));
}
