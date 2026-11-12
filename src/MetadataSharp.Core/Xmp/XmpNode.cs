using System.Xml.Linq;

namespace MetadataSharp.Core.Xmp;

/// <summary>
/// A property, structure field, array item or qualifier of a packet: the model the reader fills, the
/// flattened view is listed from and an edit changes. A packet's own tree is never changed; an edit
/// works on a copy.
/// </summary>
internal sealed class XmpNode(XName name, string prefix)
{
    private HashSet<XName>? fieldNames;

    /// <summary>The expanded name; <c>rdf:li</c> for an array item.</summary>
    internal XName Name { get; } = name;

    /// <summary>The prefix the packet declares for the namespace, or empty for a node an edit named by URI alone.</summary>
    internal string Prefix { get; } = prefix;

    /// <summary>Text, Bag, Seq, Alt or Struct; never LangAlt, which is a view of an Alt.</summary>
    internal XmpEntryKind Kind { get; set; } = XmpEntryKind.Text;

    /// <summary>The text of a simple value.</summary>
    internal string Value { get; set; } = string.Empty;

    /// <summary>True when the simple value is a URI the packet writes as <c>rdf:resource</c>.</summary>
    internal bool IsUri { get; set; }

    /// <summary>The <c>xml:lang</c> qualifier.</summary>
    internal string? Language { get; set; }

    /// <summary>The fields of a structure or the items of an array.</summary>
    internal List<XmpNode> Children { get; } = [];

    /// <summary>The qualifiers other than <c>xml:lang</c>.</summary>
    internal List<XmpNode> Qualifiers { get; } = [];

    internal bool IsArray => Kind is XmpEntryKind.Bag or XmpEntryKind.Seq or XmpEntryKind.Alt;

    /// <summary>Adds a structure field while reading; false when the structure already has one of that name.</summary>
    internal bool AddField(XmpNode field)
    {
        fieldNames ??= new HashSet<XName>();
        if (!fieldNames.Add(field.Name)) return false;
        Children.Add(field);
        return true;
    }

    internal XmpNode Clone()
    {
        var copy = new XmpNode(Name, Prefix) { Kind = Kind, Value = Value, IsUri = IsUri, Language = Language };
        foreach (XmpNode child in Children) copy.Children.Add(child.Clone());
        foreach (XmpNode qualifier in Qualifiers) copy.Qualifiers.Add(qualifier.Clone());
        return copy;
    }

    /// <summary>Equal names, forms, values, languages, children and qualifiers, in order; prefixes are serialization.</summary>
    internal static bool Same(XmpNode left, XmpNode right)
    {
        if (left.Name != right.Name || left.Kind != right.Kind || left.IsUri != right.IsUri ||
            !string.Equals(left.Value, right.Value, StringComparison.Ordinal) ||
            !string.Equals(left.Language, right.Language, StringComparison.Ordinal))
            return false;
        return Same(left.Children, right.Children) && Same(left.Qualifiers, right.Qualifiers);
    }

    internal static bool Same(List<XmpNode> left, List<XmpNode> right)
    {
        if (left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
            if (!Same(left[i], right[i])) return false;
        return true;
    }
}

/// <summary>The properties of a packet in packet order and the subject its descriptions are about.</summary>
internal sealed class XmpTree
{
    /// <summary>The value of <c>rdf:about</c>, empty in nearly every packet.</summary>
    internal string About { get; set; } = string.Empty;

    internal List<XmpNode> Properties { get; } = [];

    /// <summary>
    /// The namespaces the packet declares, by prefix, the first declaration of a prefix first. They
    /// tell an edit what a prefix of the packet stands for; they are not content and are not compared.
    /// </summary>
    internal Dictionary<string, string> Declarations { get; } = new(StringComparer.Ordinal);

    internal XmpTree Clone()
    {
        var copy = new XmpTree { About = About };
        foreach (KeyValuePair<string, string> pair in Declarations) copy.Declarations[pair.Key] = pair.Value;
        foreach (XmpNode property in Properties) copy.Properties.Add(property.Clone());
        return copy;
    }

    internal static bool Same(XmpTree left, XmpTree right) =>
        string.Equals(left.About, right.About, StringComparison.Ordinal) && XmpNode.Same(left.Properties, right.Properties);

    internal XmpNode? Find(XName name)
    {
        foreach (XmpNode property in Properties)
            if (property.Name == name) return property;
        return null;
    }
}
