using System.Globalization;
using System.Xml.Linq;

namespace MetadataSharp.Core.Xmp;

/// <summary>
/// Flattens the RDF of a parsed packet into <see cref="XmpEntry"/> rows. It reads every packet, not
/// only the editable profile, and it never refuses one: a property whose RDF it cannot interpret
/// contributes no rows. The document was bounded before it was loaded, so the recursion here is at
/// most <see cref="XmpLimits.MaxDepth"/> deep and the rows are at most one per node or attribute;
/// the one thing that can grow beyond the input is the text of the paths, which is budgeted.
/// </summary>
internal static class XmpEntryReader
{
    private static readonly XNamespace Rdf = XmpCodec.Rdf;
    private static readonly XName Language = XNamespace.Xml + "lang";

    /// <summary>
    /// Namespaces with a conventional prefix that is used whatever the packet declares: older
    /// packets declare <c>xap</c>, <c>xapMM</c> and <c>xapRights</c>, the IPTC schemas are declared
    /// under their long names, and this library's own writer declares generated prefixes.
    /// </summary>
    private static readonly Dictionary<string, string> WellKnownPrefixes = new(StringComparer.Ordinal)
    {
        ["http://purl.org/dc/elements/1.1/"] = "dc",
        ["http://ns.adobe.com/xap/1.0/"] = "xmp",
        ["http://ns.adobe.com/xap/1.0/rights/"] = "xmpRights",
        ["http://ns.adobe.com/xap/1.0/mm/"] = "xmpMM",
        ["http://ns.adobe.com/xap/1.0/bj/"] = "xmpBJ",
        ["http://ns.adobe.com/xmp/1.0/DynamicMedia/"] = "xmpDM",
        ["http://ns.adobe.com/xmp/note/"] = "xmpNote",
        ["http://ns.adobe.com/pdf/1.3/"] = "pdf",
        ["http://ns.adobe.com/photoshop/1.0/"] = "photoshop",
        ["http://ns.adobe.com/camera-raw-settings/1.0/"] = "crs",
        ["http://ns.adobe.com/tiff/1.0/"] = "tiff",
        ["http://ns.adobe.com/exif/1.0/"] = "exif",
        ["http://ns.adobe.com/exif/1.0/aux/"] = "aux",
        ["http://cipa.jp/exif/1.0/"] = "exifEX",
        ["http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/"] = "iptc",
        ["http://iptc.org/std/Iptc4xmpExt/2008-02-29/"] = "iptcExt",
        ["http://ns.microsoft.com/photo/1.0/"] = "MicrosoftPhoto",
    };

    /// <summary>A property, field, item or qualifier before it is flattened.</summary>
    private sealed class Node(XName name, string prefix)
    {
        private HashSet<XName>? fieldNames;
        internal XName Name { get; } = name;
        internal string Prefix { get; } = prefix;
        internal XmpEntryKind Kind { get; set; } = XmpEntryKind.Text;
        internal string Value { get; set; } = string.Empty;
        internal string? Language { get; set; }
        /// <summary>The fields of a structure or the items of an array.</summary>
        internal List<Node> Children { get; } = [];
        internal List<Node> Qualifiers { get; } = [];

        /// <summary>Adds a structure field; false when the structure already has one of that name.</summary>
        internal bool AddField(Node field)
        {
            fieldNames ??= new HashSet<XName>();
            if (!fieldNames.Add(field.Name)) return false;
            Children.Add(field);
            return true;
        }
    }

    /// <summary>The rows being collected and what is left of the budget for the text of their paths.</summary>
    private sealed class Output(int pathCharacters)
    {
        private long remaining = pathCharacters;
        internal List<XmpEntry> Entries { get; } = [];

        internal void Add(string path, string namespaceUri, string prefix, XmpEntryKind kind, bool hasChildEntries,
            string[] values, (string Language, string Value)[] languages)
        {
            remaining -= path.Length;
            if (remaining < 0) throw XmpCodec.Limit();
            Entries.Add(new XmpEntry(path, namespaceUri, prefix, kind, hasChildEntries, values, languages));
        }
    }

    internal static IReadOnlyList<XmpEntry> Read(XElement rdf, XmpLimits limits, CancellationToken token)
    {
        var output = new Output(limits.MaxXmlCharacters);
        // A property that appears twice is reported once, as its first readable occurrence.
        var seen = new HashSet<XName>();
        foreach (XElement description in rdf.Elements())
        {
            if (description.Name != Rdf + "Description") continue;
            foreach (XAttribute attribute in description.Attributes())
            {
                token.ThrowIfCancellationRequested();
                if (attribute.IsNamespaceDeclaration || !IsPropertyName(attribute.Name)) continue;
                if (Named(attribute.Name, description) is not Node node || !seen.Add(node.Name)) continue;
                node.Value = attribute.Value;
                Flatten(output, node);
            }
            foreach (XElement element in description.Elements())
            {
                token.ThrowIfCancellationRequested();
                if (!IsPropertyName(element.Name)) continue;
                if (Property(element) is not Node node || !seen.Add(node.Name)) continue;
                Flatten(output, node);
            }
        }
        return output.Entries.AsReadOnly();
    }

    private static bool IsPropertyName(XName name) =>
        name.Namespace != XNamespace.None && name.Namespace != Rdf && name.Namespace != XNamespace.Xml &&
        name.Namespace != XNamespace.Xmlns;

    /// <summary>A node for a name, or null when no prefix is in scope for its namespace.</summary>
    private static Node? Named(XName name, XElement scope)
    {
        string? prefix = scope.GetPrefixOfNamespace(name.Namespace);
        return string.IsNullOrEmpty(prefix) ? null : new Node(name, prefix);
    }

    private static bool OnlyWhitespace(XElement element)
    {
        foreach (XNode node in element.Nodes())
            if (node is XText text && !string.IsNullOrWhiteSpace(text.Value)) return false;
        return true;
    }

    /// <summary>
    /// Reads one property element: a top-level property, a structure field or an array item. Null
    /// means the RDF is not a form XMP defines.
    /// </summary>
    private static Node? Property(XElement element)
    {
        if (Named(element.Name, element) is not Node node) return null;
        string? parseType = null;
        bool valueAttributes = false;
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;
            if (attribute.Name == Language) node.Language = attribute.Value;
            else if (attribute.Name == Rdf + "parseType") parseType = attribute.Value;
            else if (attribute.Name != Rdf + "ID" && attribute.Name != Rdf + "datatype") valueAttributes = true;
        }
        if (parseType is not null)
        {
            // Literal and Collection content has no XMP meaning; Resource makes the element a structure.
            if (parseType != "Resource" || valueAttributes) return null;
            node.Kind = XmpEntryKind.Struct;
            return Fields(node, element) ? Resolve(node) : null;
        }
        if (valueAttributes) return Shorthand(element, node);
        if (!element.HasElements)
        {
            node.Value = element.Value;
            return node;
        }
        if (!OnlyWhitespace(element)) return null;
        XElement? child = null;
        foreach (XElement candidate in element.Elements())
        {
            if (child is not null) return null;
            child = candidate;
        }
        if (child is null) return null;
        if (child.Name == Rdf + "Bag" || child.Name == Rdf + "Seq" || child.Name == Rdf + "Alt")
        {
            node.Kind = child.Name.LocalName switch
            {
                "Bag" => XmpEntryKind.Bag,
                "Seq" => XmpEntryKind.Seq,
                _ => XmpEntryKind.Alt
            };
            if (!OnlyWhitespace(child)) return null;
            foreach (XAttribute attribute in child.Attributes())
                if (!attribute.IsNamespaceDeclaration) return null;
            foreach (XElement item in child.Elements())
            {
                if (item.Name != Rdf + "li" || Property(item) is not Node value) return null;
                node.Children.Add(value);
            }
            return node;
        }
        node.Kind = XmpEntryKind.Struct;
        if (child.Name != Rdf + "Description")
        {
            // A typed node: the element name stands for an rdf:type of the structure.
            if (child.Name.Namespace == Rdf || child.Name.Namespace == XNamespace.None) return null;
            if (Named(Rdf + "type", child) is not Node type) return null;
            type.Value = child.Name.NamespaceName + child.Name.LocalName;
            node.Qualifiers.Add(type);
        }
        foreach (XAttribute attribute in child.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace == XNamespace.Xml) continue;
            if (attribute.Name.Namespace == Rdf)
            {
                if (attribute.Name.LocalName is "about" or "ID" or "nodeID") continue;
                return null;
            }
            if (Named(attribute.Name, child) is not Node field) return null;
            field.Value = attribute.Value;
            if (!node.AddField(field)) return null;
        }
        return Fields(node, child) ? Resolve(node) : null;
    }

    /// <summary>
    /// An element that carries its content in attributes: <c>rdf:value</c> or <c>rdf:resource</c>
    /// gives a simple value and the other attributes qualify it; without either, the attributes
    /// are the fields of a structure.
    /// </summary>
    private static Node? Shorthand(XElement element, Node node)
    {
        if (element.HasElements || !OnlyWhitespace(element)) return null;
        string? value = null, resource = null;
        var others = new List<Node>();
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name == Language) continue;
            if (attribute.Name.Namespace == Rdf)
            {
                switch (attribute.Name.LocalName)
                {
                    case "value": value = attribute.Value; break;
                    case "resource": resource = attribute.Value; break;
                    case "ID" or "nodeID" or "datatype": break;
                    default: return null;
                }
                continue;
            }
            if (attribute.Name.Namespace == XNamespace.Xml || Named(attribute.Name, element) is not Node other) return null;
            other.Value = attribute.Value;
            others.Add(other);
        }
        if (value is not null || resource is not null)
        {
            node.Value = value ?? resource!;
            if (value is not null && resource is not null && Named(Rdf + "resource", element) is Node target)
            {
                target.Value = resource;
                node.Qualifiers.Add(target);
            }
            node.Qualifiers.AddRange(others);
            return node;
        }
        if (others.Count != 0)
        {
            node.Kind = XmpEntryKind.Struct;
            foreach (Node other in others)
                if (!node.AddField(other)) return null;
        }
        return node;
    }

    private static bool Fields(Node node, XElement container)
    {
        if (!OnlyWhitespace(container)) return false;
        foreach (XElement element in container.Elements())
        {
            if (element.Name.Namespace == Rdf && element.Name.LocalName is not ("value" or "type")) return false;
            if (Property(element) is not Node field || !node.AddField(field)) return false;
        }
        return true;
    }

    /// <summary>
    /// A structure with an <c>rdf:value</c> field is that value, and its other fields are qualifiers.
    /// </summary>
    private static Node Resolve(Node node)
    {
        Node? value = null;
        foreach (Node field in node.Children)
            if (field.Name == Rdf + "value") { value = field; break; }
        if (value is null) return node;
        var resolved = new Node(node.Name, node.Prefix)
        {
            Kind = value.Kind,
            Value = value.Value,
            Language = value.Language ?? node.Language
        };
        resolved.Children.AddRange(value.Children);
        resolved.Qualifiers.AddRange(node.Qualifiers);
        resolved.Qualifiers.AddRange(value.Qualifiers);
        foreach (Node field in node.Children)
            if (!ReferenceEquals(field, value)) resolved.Qualifiers.Add(field);
        return resolved;
    }

    private static void Flatten(Output output, Node property)
    {
        string uri = property.Name.NamespaceName;
        string prefix = WellKnownPrefixes.TryGetValue(uri, out string? known) ? known : property.Prefix;
        Flatten(output, uri, prefix, property.Name.LocalName, property);
    }

    private static void Flatten(Output output, string uri, string prefix, string path, Node node)
    {
        if (node.Kind is XmpEntryKind.Bag or XmpEntryKind.Seq or XmpEntryKind.Alt)
        {
            List<Node> items = node.Children;
            bool qualified = node.Language is not null || node.Qualifiers.Count != 0;
            if (!qualified && node.Kind == XmpEntryKind.Alt && items.Count != 0 &&
                items.TrueForAll(item => item.Kind == XmpEntryKind.Text && item.Language is not null && item.Qualifiers.Count == 0))
            {
                var texts = new string[items.Count];
                var languages = new (string Language, string Value)[items.Count];
                for (int i = 0; i < items.Count; i++)
                {
                    texts[i] = items[i].Value;
                    languages[i] = (items[i].Language!, items[i].Value);
                }
                output.Add(path, uri, prefix, XmpEntryKind.LangAlt, false, texts, languages);
                return;
            }
            if (!qualified && items.TrueForAll(item => item.Kind == XmpEntryKind.Text && item.Language is null && item.Qualifiers.Count == 0))
            {
                var texts = new string[items.Count];
                for (int i = 0; i < items.Count; i++) texts[i] = items[i].Value;
                output.Add(path, uri, prefix, node.Kind, false, texts, []);
                return;
            }
            output.Add(path, uri, prefix, node.Kind, true, [], []);
            Qualifiers(output, uri, prefix, path, node);
            for (int i = 0; i < items.Count; i++)
                Flatten(output, uri, prefix, path + "[" + (i + 1).ToString(CultureInfo.InvariantCulture) + "]", items[i]);
            return;
        }
        if (node.Kind == XmpEntryKind.Struct)
        {
            output.Add(path, uri, prefix, XmpEntryKind.Struct, true, [], []);
            Qualifiers(output, uri, prefix, path, node);
            foreach (Node field in node.Children)
                Flatten(output, uri, prefix, path + "/" + Step(field), field);
            return;
        }
        output.Add(path, uri, prefix, XmpEntryKind.Text, false, [node.Value], []);
        Qualifiers(output, uri, prefix, path, node);
    }

    private static void Qualifiers(Output output, string uri, string prefix, string path, Node node)
    {
        if (node.Language is not null)
            output.Add(path + "/?xml:lang", uri, prefix, XmpEntryKind.Text, false, [node.Language], []);
        foreach (Node qualifier in node.Qualifiers)
            Flatten(output, uri, prefix, path + "/?" + Step(qualifier), qualifier);
    }

    private static string Step(Node node) => node.Prefix + ":" + node.Name.LocalName;
}
