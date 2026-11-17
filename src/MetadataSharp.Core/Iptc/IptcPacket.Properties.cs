namespace MetadataSharp.Core.Iptc;

// Six common text datasets, read by the same packet-wide charset policy as GetText. Invalid text
// declared as UTF-8 is left out of convenience properties; raw dataset bytes always remain available.
public sealed partial class IptcPacket
{
    /// <summary>
    /// The title of the picture: the first ObjectName dataset (2:5) as text. Null when the stream has
    /// none, or when its text cannot be decoded; see <see cref="Keywords" />.
    /// </summary>
    public string? ObjectName => First(new IptcDatasetKey(2, 5));

    /// <summary>
    /// Every Keywords dataset (2:25) as text, in the order of the stream; empty when there is none.
    /// </summary>
    /// <remarks>
    /// The text is decoded as <see cref="GetText(int)"/> decodes it, using the charset selected for the whole
    /// packet (ASCII, valid UTF-8, or ISO-8859-1 fallback). An occurrence invalid under a declared UTF-8
    /// marker is left out. The raw bytes of every occurrence stay in <see cref="Datasets"/>.
    /// </remarks>
    public IReadOnlyList<string> Keywords => All(new IptcDatasetKey(2, 25));

    /// <summary>
    /// The creators of the picture: every Byline dataset (2:80) as text, in the order of the stream;
    /// empty when there is none. Decoded as <see cref="Keywords" /> is.
    /// </summary>
    public IReadOnlyList<string> Bylines => All(new IptcDatasetKey(2, 80));

    /// <summary>
    /// The first Headline dataset (2:105) as text. Null when the stream has none, or when its text
    /// cannot be decoded; see <see cref="Keywords" />.
    /// </summary>
    public string? Headline => First(new IptcDatasetKey(2, 105));

    /// <summary>
    /// The copyright notice: the first Copyright dataset (2:116) as text. Null when the stream has
    /// none, or when its text cannot be decoded; see <see cref="Keywords" />.
    /// </summary>
    public string? Copyright => First(new IptcDatasetKey(2, 116));

    /// <summary>
    /// The description of the picture: the first Caption dataset (2:120) as text. Null when the stream
    /// has none, or when its text cannot be decoded; see <see cref="Keywords" />.
    /// </summary>
    public string? Caption => First(new IptcDatasetKey(2, 120));

    // Read only the first occurrence because these convenience properties model a single-valued field.
    private string? First(IptcDatasetKey key)
    {
        foreach (IptcDataset dataset in Datasets)
        {
            if (dataset.Key == key)
                return TryText(dataset, out string? text) ? text : null;
        }
        return null;
    }

    // Decode every occurrence in wire order for the IPTC properties whose schema permits repetition.
    private IReadOnlyList<string> All(IptcDatasetKey key)
    {
        List<string>? found = null;
        foreach (IptcDataset dataset in Datasets)
        {
            if (dataset.Key == key && TryText(dataset, out string? text))
                (found ??= []).Add(text);
        }
        return found is null ? [] : found.ToArray();
    }

    /// <summary>The conditions of <see cref="GetText(int)" />, answered with false where it throws.</summary>
    private bool TryText(IptcDataset dataset, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        text = null;
        try { text = DecodeText(dataset); return true; }
        catch (MetadataException failure) when (failure.Code == MetadataErrorCode.InvalidIptc) { return false; }
    }
}
