using System.Globalization;

namespace MetadataSharp.Core.Xmp;

// The properties an application asks for first, read from Entries: the flat view lists every packet,
// editable or not, so these work where Properties throws. Each is the packet's own text; only Rating is
// parsed, because it is a number by definition.
public sealed partial class XmpPacket
{
    private const string DublinCore = "http://purl.org/dc/elements/1.1/";
    private const string XmpBasic = "http://ns.adobe.com/xap/1.0/";

    /// <summary>
    /// The title: the <c>dc:title</c> language alternative. The text is the <c>x-default</c> item,
    /// otherwise the first item in packet order. Null when the packet has no such property or holds it
    /// in a form that has no text of its own (an array of structures, an empty array).
    /// </summary>
    public string? Title => Alternative(DublinCore, "title");

    /// <summary>The description: <c>dc:description</c>, chosen as <see cref="Title" /> is.</summary>
    public string? Description => Alternative(DublinCore, "description");

    /// <summary>
    /// The creators: the items of the <c>dc:creator</c> sequence, in packet order. Empty when the
    /// packet has no such property or its items are not plain text. A packet that writes the property
    /// as one simple value gives that value as the only item.
    /// </summary>
    public IReadOnlyList<string> Creators => Items(DublinCore, "creator");

    /// <summary>The rights statement: <c>dc:rights</c>, chosen as <see cref="Title" /> is.</summary>
    public string? Rights => Alternative(DublinCore, "rights");

    /// <summary>
    /// The keywords: the items of the <c>dc:subject</c> bag, in packet order, by the rules of
    /// <see cref="Creators" />.
    /// </summary>
    public IReadOnlyList<string> Subjects => Items(DublinCore, "subject");

    /// <summary>
    /// The rating: <c>xmp:Rating</c> as a number. XMP defines it as a real from 0 to 5, with -1 for a
    /// rejected picture; the number is returned as written, in or out of that range. Null when the
    /// packet has no such property or its text is not a plain decimal number.
    /// </summary>
    public double? Rating =>
        Find(XmpBasic, "Rating") is { Kind: XmpEntryKind.Text, Values: [string text] } &&
        double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture, out double rating) && double.IsFinite(rating)
            ? rating
            : null;

    /// <summary>The top-level property of that name: its path is the local name alone.</summary>
    private XmpEntry? Find(string namespaceUri, string localName)
    {
        foreach (XmpEntry entry in Entries)
        {
            if (entry.Path == localName && entry.NamespaceUri == namespaceUri)
                return entry;
        }
        return null;
    }

    private string? Alternative(string namespaceUri, string localName)
    {
        if (Find(namespaceUri, localName) is not XmpEntry entry)
            return null;
        if (entry.Kind == XmpEntryKind.LangAlt)
        {
            foreach ((string language, string value) in entry.Languages)
            {
                if (string.Equals(language, "x-default", StringComparison.OrdinalIgnoreCase))
                    return value;
            }
            return entry.Languages.Count != 0 ? entry.Languages[0].Value : null;
        }
        // A simple value, or alternatives without languages: the first is the default.
        return entry.Kind is XmpEntryKind.Text or XmpEntryKind.Alt && entry.Values.Count != 0 ? entry.Values[0] : null;
    }

    // Return array members in their stored order and distinguish a missing property from an empty array.
    private IReadOnlyList<string> Items(string namespaceUri, string localName)
    {
        if (Find(namespaceUri, localName) is not XmpEntry entry ||
            entry.Kind is not (XmpEntryKind.Seq or XmpEntryKind.Bag or XmpEntryKind.Alt or XmpEntryKind.Text) ||
            entry.Values.Count == 0)
            return [];
        return entry.Values.ToArray();
    }
}
