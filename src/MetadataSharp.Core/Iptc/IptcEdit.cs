namespace MetadataSharp.Core.Iptc;

/// <summary>Last-wins replacement of all occurrences of an addressed record-2 dataset.</summary>
public sealed class IptcEdit
{
    internal sealed record Replacement(bool Text, IptcValue[] Values);
    private readonly Dictionary<IptcDatasetKey, Replacement> changes = [];
    private bool removeAll;
    public IptcEdit() { }
    public bool IsEmpty => !removeAll && changes.Count == 0;
    public void ReplaceRaw(IptcDatasetKey key, IEnumerable<IptcValue> values)
    {
        Check(key); ArgumentNullException.ThrowIfNull(values);
        var copy = new List<IptcValue>(); long bytes = 0;
        foreach (IptcValue value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (copy.Count >= 4096 || (bytes += value.ByteLength + 9L) > 1_048_576)
                throw new ArgumentException("Replacement exceeds the default count or byte budget.", nameof(values));
            copy.Add(value);
        }
        Commit(key, new(false, copy.ToArray()));
    }
    public void ReplaceText(IptcDatasetKey key, IEnumerable<string> values)
    {
        Check(key); ArgumentNullException.ThrowIfNull(values);
        var rule = IptcText.Rule(key); var copy = new List<IptcValue>(); long bytes = 0;
        foreach (string value in values)
        {
            if (copy.Count >= (rule.Repeated ? 4096 : 1)) throw new ArgumentException("The text field cardinality was exceeded.", nameof(values));
            byte[] encoded = IptcText.Encode(key, value);
            if ((bytes += encoded.Length + 9L) > 1_048_576) throw new ArgumentException("Replacement exceeds its default byte budget.", nameof(values));
            copy.Add(new(encoded));
        }
        Commit(key, new(true, copy.ToArray()));
    }
    public void Remove(IptcDatasetKey key) { Check(key); Commit(key, new(false, [])); }
    public void RemoveAllIptc()
    {
        if (changes.Count != 0) throw new ArgumentException("Whole IPTC removal cannot be mixed with dataset edits.");
        removeAll = true;
    }
    private void Check(IptcDatasetKey key)
    {
        if (removeAll || key.Record != 2 || key.Dataset == 0)
            throw new ArgumentException("Only ordinary record-2 datasets can be addressed, without whole removal.", nameof(key));
    }
    private void Commit(IptcDatasetKey key, Replacement replacement)
    {
        long bytes = 0; int count = 0;
        foreach (var change in changes.Where(c => c.Key != key).Select(c => c.Value).Append(replacement))
        {
            count += change.Values.Length;
            foreach (var value in change.Values) bytes += value.ByteLength + 9L;
        }
        if (count > 4096 || bytes > 1_048_576 || (!changes.ContainsKey(key) && changes.Count >= 4096))
            throw new ArgumentException("The edit exceeds its aggregate default budget.");
        changes[key] = replacement;
    }
    internal (bool RemoveAll, Dictionary<IptcDatasetKey, Replacement> Changes) Snapshot() => (removeAll, new(changes));
}
