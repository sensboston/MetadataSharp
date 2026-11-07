namespace MetadataSharp.Core.Xmp;

/// <summary>Addressed XMP changes. Last operation wins; whole removal cannot be mixed.</summary>
public sealed class XmpEdit
{
    private readonly Dictionary<XmpPropertyName, XmpValue?> changes = [];
    private bool removeAll;
    public XmpEdit() { }
    public bool IsEmpty => !removeAll && changes.Count == 0;
    public void Set(XmpPropertyName name, XmpValue value)
    {
        Check(name); ArgumentNullException.ThrowIfNull(value); changes[name] = value;
    }
    public void Remove(XmpPropertyName name) { Check(name); changes[name] = null; }
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
