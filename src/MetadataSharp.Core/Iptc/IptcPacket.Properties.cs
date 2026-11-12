using System.Text.Unicode;

namespace MetadataSharp.Core.Iptc;

// Six common text datasets, read by the rules of GetText and without its exceptions: where
// GetText would refuse an occurrence, the occurrence is left out. Nothing is decoded that GetText does
// not decode, and no character set is guessed.
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
    /// The text is decoded as <see cref="GetText" /> decodes it, and an occurrence that method refuses
    /// is left out instead: one with bytes above 0x7F in a stream that does not declare UTF-8, any in a
    /// stream that declares another character set, and one that is not valid UTF-8. The raw bytes of
    /// every occurrence stay in <see cref="Datasets" />.
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

    private string? First(IptcDatasetKey key)
    {
        foreach (IptcDataset dataset in Datasets)
        {
            if (dataset.Key == key)
                return TryText(dataset, out string? text) ? text : null;
        }
        return null;
    }

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

    /// <summary>The conditions of <see cref="GetText" />, answered with false where it throws.</summary>
    private bool TryText(IptcDataset dataset, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        text = null;
        ReadOnlySpan<byte> bytes = dataset.Value.Span;
        if (CharacterSet == IptcCharacterSet.Unsupported ||
            (CharacterSet == IptcCharacterSet.Unspecified && ContainsHighBit(bytes)) ||
            !Utf8.IsValid(bytes))
            return false;
        text = IptcText.Utf8.GetString(bytes);
        return true;
    }
}
