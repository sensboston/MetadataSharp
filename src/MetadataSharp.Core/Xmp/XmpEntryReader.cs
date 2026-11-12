using System.Globalization;
using System.Xml.Linq;

namespace MetadataSharp.Core.Xmp;

/// <summary>
/// Reads the RDF of a parsed packet into a tree of <see cref="XmpNode"/> and flattens that tree into
/// <see cref="XmpEntry"/> rows. It reads every packet and never refuses one: a property whose RDF
/// it cannot interpret contributes no node, and the tree is then reported as not lossless, which
/// is what keeps such a packet from being rewritten. The document was bounded before it was loaded,
/// so the recursion here is at most <see cref="XmpLimits.MaxDepth"/> deep and the rows are at most
/// one per node or attribute; the one thing that can grow beyond the input is the text of the
/// paths, which is budgeted.
/// </summary>
internal static class XmpEntryReader
{
    private static readonly XNamespace Rdf = XmpCodec.Rdf;
    private static readonly XName Language = XNamespace.Xml + "lang";

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

    /// <summary>
    /// The tree of a packet. <paramref name="lossless"/> is false when the packet holds anything the
    /// tree does not: RDF that is not a form XMP defines, a property that occurs twice, node
    /// identifiers, datatypes, descriptions about different subjects.
    /// </summary>
    internal static XmpTree ReadTree(XElement rdf, CancellationToken token, out bool lossless)
    {
        var parser = new Parser();
        var tree = new XmpTree();
        string? about = null;
        // A property that appears twice is reported once, as its first readable occurrence.
        var seen = new HashSet<XName>();
        foreach (XElement description in rdf.Elements())
        {
            if (description.Name != Rdf + "Description") { parser.Lossy = true; continue; }
            if (!OnlyWhitespace(description)) parser.Lossy = true;
            string subject = string.Empty;
            foreach (XAttribute attribute in description.Attributes())
            {
                token.ThrowIfCancellationRequested();
                if (attribute.IsNamespaceDeclaration) continue;
                if (attribute.Name == Rdf + "about") { subject = attribute.Value; continue; }
                if (!IsPropertyName(attribute.Name)) { parser.Lossy = true; continue; }
                if (Named(attribute.Name, description) is not XmpNode node || !seen.Add(node.Name)) { parser.Lossy = true; continue; }
                node.Value = attribute.Value;
                tree.Properties.Add(node);
            }
            if (about is null) about = subject;
            else if (!string.Equals(about, subject, StringComparison.Ordinal)) parser.Lossy = true;
            foreach (XElement element in description.Elements())
            {
                token.ThrowIfCancellationRequested();
                if (!IsPropertyName(element.Name)) { parser.Lossy = true; continue; }
                if (parser.Property(element) is not XmpNode node || !seen.Add(node.Name)) { parser.Lossy = true; continue; }
                tree.Properties.Add(node);
            }
        }
        tree.About = about ?? string.Empty;
        foreach (XElement element in rdf.AncestorsAndSelf()) Declarations(tree, element);
        foreach (XElement element in rdf.Descendants()) Declarations(tree, element);
        lossless = !parser.Lossy;
        return tree;
    }

    private static void Declarations(XmpTree tree, XElement element)
    {
        foreach (XAttribute attribute in element.Attributes())
            if (attribute.IsNamespaceDeclaration && attribute.Name.Namespace == XNamespace.Xmlns)
                tree.Declarations.TryAdd(attribute.Name.LocalName, attribute.Value);
    }

    internal static IReadOnlyList<XmpEntry> Flatten(XmpTree tree, XmpLimits limits)
    {
        var output = new Output(limits.MaxXmlCharacters);
        foreach (XmpNode property in tree.Properties) Flatten(output, property);
        return output.Entries.AsReadOnly();
    }

    private static bool IsPropertyName(XName name) =>
        name.Namespace != XNamespace.None && name.Namespace != Rdf && name.Namespace != XNamespace.Xml &&
        name.Namespace != XNamespace.Xmlns;

    /// <summary>A node for a name, or null when no prefix is in scope for its namespace.</summary>
    private static XmpNode? Named(XName name, XElement scope)
    {
        string? prefix = scope.GetPrefixOfNamespace(name.Namespace);
        return string.IsNullOrEmpty(prefix) ? null : new XmpNode(name, prefix);
    }

    private static bool OnlyWhitespace(XElement element)
    {
        foreach (XNode node in element.Nodes())
            if (node is XText text && !string.IsNullOrWhiteSpace(text.Value)) return false;
        return true;
    }

    private sealed class Parser
    {
        /// <summary>Set when something in the packet is left out of the tree.</summary>
        internal bool Lossy { get; set; }

        /// <summary>
        /// Reads one property element: a top-level property, a structure field or an array item. Null
        /// means the RDF is not a form XMP defines.
        /// </summary>
        internal XmpNode? Property(XElement element)
        {
            if (Named(element.Name, element) is not XmpNode node) return null;
            string? parseType = null;
            bool valueAttributes = false;
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration) continue;
                if (attribute.Name == Language) node.Language = attribute.Value;
                else if (attribute.Name == Rdf + "parseType") parseType = attribute.Value;
                else if (attribute.Name != Rdf + "ID" && attribute.Name != Rdf + "datatype") valueAttributes = true;
                else Lossy = true;
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
                    if (item.Name != Rdf + "li" || Property(item) is not XmpNode value) return null;
                    node.Children.Add(value);
                }
                return node;
            }
            node.Kind = XmpEntryKind.Struct;
            if (child.Name != Rdf + "Description")
            {
                // A typed node: the element name stands for an rdf:type of the structure.
                if (child.Name.Namespace == Rdf || child.Name.Namespace == XNamespace.None) return null;
                if (Named(Rdf + "type", child) is not XmpNode type) return null;
                type.Value = child.Name.NamespaceName + child.Name.LocalName;
                type.IsUri = true;
                node.Qualifiers.Add(type);
            }
            foreach (XAttribute attribute in child.Attributes())
            {
                if (attribute.IsNamespaceDeclaration) continue;
                if (attribute.Name.Namespace == XNamespace.Xml) { Lossy = true; continue; }
                if (attribute.Name.Namespace == Rdf)
                {
                    if (attribute.Name.LocalName is "about" or "ID" or "nodeID") { Lossy = true; continue; }
                    return null;
                }
                if (Named(attribute.Name, child) is not XmpNode field) return null;
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
        private XmpNode? Shorthand(XElement element, XmpNode node)
        {
            if (element.HasElements || !OnlyWhitespace(element)) return null;
            string? value = null, resource = null;
            var others = new List<XmpNode>();
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || attribute.Name == Language) continue;
                if (attribute.Name.Namespace == Rdf)
                {
                    switch (attribute.Name.LocalName)
                    {
                        case "value": value = attribute.Value; break;
                        case "resource": resource = attribute.Value; break;
                        case "ID" or "nodeID" or "datatype": Lossy = true; break;
                        default: return null;
                    }
                    continue;
                }
                if (attribute.Name.Namespace == XNamespace.Xml || Named(attribute.Name, element) is not XmpNode other) return null;
                other.Value = attribute.Value;
                others.Add(other);
            }
            if (value is not null || resource is not null)
            {
                node.Value = value ?? resource!;
                node.IsUri = value is null;
                if (value is not null && resource is not null && Named(Rdf + "resource", element) is XmpNode target)
                {
                    // A literal and a resource at once is listed, and is not a form that is written back.
                    Lossy = true;
                    target.Value = resource;
                    node.Qualifiers.Add(target);
                }
                node.Qualifiers.AddRange(others);
                return node;
            }
            if (others.Count != 0)
            {
                node.Kind = XmpEntryKind.Struct;
                foreach (XmpNode other in others)
                    if (!node.AddField(other)) return null;
            }
            return node;
        }

        private bool Fields(XmpNode node, XElement container)
        {
            if (!OnlyWhitespace(container)) return false;
            foreach (XElement element in container.Elements())
            {
                if (element.Name.Namespace == Rdf && element.Name.LocalName is not ("value" or "type")) return false;
                if (Property(element) is not XmpNode field || !node.AddField(field)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// A structure with an <c>rdf:value</c> field is that value, and its other fields are qualifiers.
    /// </summary>
    private static XmpNode Resolve(XmpNode node)
    {
        XmpNode? value = null;
        foreach (XmpNode field in node.Children)
            if (field.Name == Rdf + "value") { value = field; break; }
        if (value is null) return node;
        var resolved = new XmpNode(node.Name, node.Prefix)
        {
            Kind = value.Kind,
            Value = value.Value,
            IsUri = value.IsUri,
            Language = value.Language ?? node.Language
        };
        resolved.Children.AddRange(value.Children);
        resolved.Qualifiers.AddRange(node.Qualifiers);
        resolved.Qualifiers.AddRange(value.Qualifiers);
        foreach (XmpNode field in node.Children)
            if (!ReferenceEquals(field, value)) resolved.Qualifiers.Add(field);
        return resolved;
    }

    private static void Flatten(Output output, XmpNode property)
    {
        string uri = property.Name.NamespaceName;
        string prefix = XmpNamespaces.ListedPrefixes.TryGetValue(uri, out string? known) ? known : property.Prefix;
        Flatten(output, uri, prefix, property.Name.LocalName, property);
    }

    private static void Flatten(Output output, string uri, string prefix, string path, XmpNode node)
    {
        if (node.Kind is XmpEntryKind.Bag or XmpEntryKind.Seq or XmpEntryKind.Alt)
        {
            List<XmpNode> items = node.Children;
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
            foreach (XmpNode field in node.Children)
                Flatten(output, uri, prefix, path + "/" + Step(field), field);
            return;
        }
        output.Add(path, uri, prefix, XmpEntryKind.Text, false, [node.Value], []);
        Qualifiers(output, uri, prefix, path, node);
    }

    private static void Qualifiers(Output output, string uri, string prefix, string path, XmpNode node)
    {
        if (node.Language is not null)
            output.Add(path + "/?xml:lang", uri, prefix, XmpEntryKind.Text, false, [node.Language], []);
        foreach (XmpNode qualifier in node.Qualifiers)
            Flatten(output, uri, prefix, path + "/?" + Step(qualifier), qualifier);
    }

    private static string Step(XmpNode node) => node.Prefix + ":" + node.Name.LocalName;
}
