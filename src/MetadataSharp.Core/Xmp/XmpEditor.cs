using System.Xml.Linq;

namespace MetadataSharp.Core.Xmp;

/// <summary>
/// Applies the operations of an edit to a copy of a packet's tree. An operation that does not apply
/// to the tree as it then is throws <see cref="MetadataErrorCode.InvalidXmpPath"/>; the caller drops
/// the copy, so a refused edit changes nothing.
/// </summary>
internal sealed class XmpEditor
{
    private static readonly XName Item = XmpCodec.Rdf + "li";
    private readonly XmpTree tree;
    private readonly Dictionary<string, string> uriByPrefix = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> prefixByUri = new(StringComparer.Ordinal);

    private XmpEditor(XmpTree tree, IReadOnlyDictionary<string, string> registered)
    {
        this.tree = tree;
        // What the packet declares; the first declaration of a prefix or a namespace stands.
        foreach (XmpNode property in tree.Properties) Declared(property);
        foreach (KeyValuePair<string, string> pair in tree.Declarations)
        {
            if (pair.Key is "rdf" or "xml" or "x" || pair.Value == XmpCodec.RdfUri || pair.Value == XmpCodec.MetaUri) continue;
            uriByPrefix.TryAdd(pair.Key, pair.Value);
        }
        // A registration overrides the packet for the paths of the edit; the packet keeps its
        // prefix for a namespace it declares.
        foreach (KeyValuePair<string, string> pair in registered)
        {
            uriByPrefix[pair.Key] = pair.Value;
            prefixByUri.TryAdd(pair.Value, pair.Key);
        }
    }

    internal static void Apply(XmpTree tree, XmpEdit edit)
    {
        var editor = new XmpEditor(tree, edit.Namespaces);
        foreach (XmpOperation operation in edit.Operations) editor.Apply(operation);
    }

    private void Declared(XmpNode node)
    {
        if (node.Prefix.Length != 0 && node.Name.Namespace != XmpCodec.Rdf)
        {
            uriByPrefix.TryAdd(node.Prefix, node.Name.NamespaceName);
            prefixByUri.TryAdd(node.Name.NamespaceName, node.Prefix);
        }
        foreach (XmpNode child in node.Children) Declared(child);
        foreach (XmpNode qualifier in node.Qualifiers) Declared(qualifier);
    }

    private static MetadataException Refused(string reason) =>
        new(MetadataErrorCode.InvalidXmpPath, "The XMP edit does not apply to the packet: " + reason + ".");

    /// <summary>The expanded name of a step; a prefix nothing declares is refused.</summary>
    private XName NameOf(XmpStep step)
    {
        if (step.Uri is not null) return XName.Get(step.Name, step.Uri);
        if (step.Prefix == "rdf")
        {
            // rdf:type is the one RDF name that is a qualifier of the XMP data model.
            if (step.Kind == XmpStepKind.Qualifier && step.Name == "type") return XmpCodec.Rdf + "type";
            throw Refused("rdf:" + step.Name + " is part of the RDF syntax, not a property");
        }
        if (uriByPrefix.TryGetValue(step.Prefix, out string? uri) || XmpNamespaces.TryGetUri(step.Prefix, out uri))
            return XName.Get(step.Name, uri);
        throw Refused("no namespace is known for the prefix \"" + step.Prefix + "\"; register it with RegisterNamespace");
    }

    /// <summary>A new node under the prefix the packet has for the namespace, else the one the path used.</summary>
    private XmpNode Create(XmpStep step, XName name)
    {
        string uri = name.NamespaceName;
        if (name.Namespace == XmpCodec.Rdf) return new XmpNode(name, "rdf");
        if (!prefixByUri.TryGetValue(uri, out string? prefix))
        {
            // A path that names the namespace by its listed prefix declares it under the written one.
            prefix = XmpNamespaces.TryGetPrefix(uri, out string conventional) ? conventional : step.Prefix;
            if (prefix.Length != 0)
            {
                prefixByUri[uri] = prefix;
                uriByPrefix.TryAdd(prefix, uri);
            }
        }
        return new XmpNode(name, prefix);
    }

    /// <summary>
    /// The node the first <paramref name="count"/> steps lead to, or null when the packet does not
    /// have it. With <paramref name="create"/>, a property or field that is missing is created as a
    /// structure; an array item or a qualified value cannot be made out of nothing.
    /// </summary>
    private XmpNode? Walk(XmpStep[] path, int count, bool create)
    {
        XmpNode? current = null;
        for (int i = 0; i < count; i++)
        {
            XmpStep step = path[i];
            XmpNode? next;
            switch (step.Kind)
            {
                case XmpStepKind.Property:
                {
                    XName name = NameOf(step);
                    next = tree.Find(name);
                    if (next is null && create)
                    {
                        next = Create(step, name);
                        next.Kind = XmpEntryKind.Struct;
                        tree.Properties.Add(next);
                    }
                    break;
                }
                case XmpStepKind.Field:
                {
                    XName name = NameOf(step);
                    if (current!.Kind != XmpEntryKind.Struct)
                    {
                        if (!create) return null;
                        throw Refused("\"" + step.Prefix + ":" + step.Name + "\" is asked of a node that is not a structure");
                    }
                    next = Find(current.Children, name);
                    if (next is null && create)
                    {
                        next = Create(step, name);
                        next.Kind = XmpEntryKind.Struct;
                        current.Children.Add(next);
                    }
                    break;
                }
                case XmpStepKind.Index:
                case XmpStepKind.Last:
                {
                    if (!current!.IsArray)
                    {
                        if (!create) return null;
                        throw Refused("an index is asked of a node that is not an array; an array is created with Append or AppendStruct");
                    }
                    int index = step.Kind == XmpStepKind.Last ? current.Children.Count : step.Index;
                    if (index < 1 || index > current.Children.Count)
                    {
                        if (!create) return null;
                        throw Refused("the array has " + current.Children.Count + " items and item " + (step.Kind == XmpStepKind.Last ? "last()" : step.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)) + " is asked for");
                    }
                    next = current.Children[index - 1];
                    break;
                }
                case XmpStepKind.Qualifier:
                {
                    XName name = NameOf(step);
                    next = Find(current!.Qualifiers, name);
                    if (next is null && create) throw Refused("the qualifier \"" + step.Prefix + ":" + step.Name + "\" does not exist; set it before what is below it");
                    break;
                }
                default:
                    throw Refused("nothing is below xml:lang");
            }
            if (next is null) return null;
            current = next;
        }
        return current;
    }

    private static XmpNode? Find(List<XmpNode> nodes, XName name)
    {
        foreach (XmpNode node in nodes)
            if (node.Name == name) return node;
        return null;
    }

    private void Apply(XmpOperation operation)
    {
        XmpStep[] path = operation.Path;
        XmpStep last = path[^1];
        switch (operation.Kind)
        {
            case XmpOperationKind.Remove:
                Remove(path, last);
                break;
            case XmpOperationKind.Set:
                if (last.Kind == XmpStepKind.Language)
                {
                    XmpNode owner = Walk(path, path.Length - 1, false) ?? throw Refused("xml:lang is set on a node the packet does not have");
                    owner.Language = operation.Value!.Text;
                }
                else Fill(Target(path, last), operation.Value!);
                break;
            case XmpOperationKind.Append:
            {
                XmpEntryKind kind = Form(operation.ArrayKind);
                XmpNode array = Walk(path, path.Length, false) is XmpNode found ? found : NewArray(path, last, kind);
                if (array.Kind != kind) throw Refused("the node is " + Describe(array) + ", not " + Describe(kind));
                array.Children.Add(NewItem(operation));
                break;
            }
            case XmpOperationKind.Insert:
            {
                XmpNode array = Walk(path, path.Length, false) ?? throw Refused("an item is inserted into an array the packet does not have");
                if (!array.IsArray) throw Refused("the node is " + Describe(array) + ", not an array");
                if (operation.Index > array.Children.Count + 1)
                    throw Refused("the array has " + array.Children.Count + " items and an item is inserted at " + operation.Index);
                array.Children.Insert(operation.Index - 1, NewItem(operation));
                break;
            }
            case XmpOperationKind.SetLanguage:
            {
                XmpNode array = Walk(path, path.Length, false) is XmpNode found ? found : NewArray(path, last, XmpEntryKind.Alt);
                if (array.Kind != XmpEntryKind.Alt) throw Refused("the node is " + Describe(array) + ", not a language alternative");
                string language = operation.Language!;
                XmpNode? item = Localized(array, language);
                if (item is not null)
                {
                    if (item.Kind != XmpEntryKind.Text) throw Refused("the item of that language is not a simple value");
                    item.Value = operation.Value!.Text!;
                    item.IsUri = false;
                }
                else
                {
                    item = new XmpNode(Item, "rdf") { Value = operation.Value!.Text!, Language = language };
                    if (IsDefault(language)) array.Children.Insert(0, item);
                    else array.Children.Add(item);
                }
                break;
            }
            case XmpOperationKind.RemoveLanguage:
            {
                if (Walk(path, path.Length, false) is not XmpNode array || array.Kind != XmpEntryKind.Alt) break;
                if (Localized(array, operation.Language!) is not XmpNode item) break;
                array.Children.Remove(item);
                if (array.Children.Count == 0) Remove(path, last);
                break;
            }
        }
    }

    private static bool IsDefault(string language) => language.Equals("x-default", StringComparison.OrdinalIgnoreCase);

    private static XmpNode? Localized(XmpNode array, string language)
    {
        foreach (XmpNode item in array.Children)
            if (item.Language is not null && item.Language.Equals(language, StringComparison.OrdinalIgnoreCase)) return item;
        return null;
    }

    private static XmpEntryKind Form(XmpValueKind kind) => kind switch
    {
        XmpValueKind.Bag => XmpEntryKind.Bag,
        XmpValueKind.Sequence => XmpEntryKind.Seq,
        _ => XmpEntryKind.Alt
    };

    private static string Describe(XmpEntryKind kind) => kind switch
    {
        XmpEntryKind.Bag => "a bag",
        XmpEntryKind.Seq => "a sequence",
        XmpEntryKind.Alt or XmpEntryKind.LangAlt => "an array of alternatives",
        XmpEntryKind.Struct => "a structure",
        _ => "a simple value"
    };

    private static string Describe(XmpNode node) => Describe(node.Kind);

    private static XmpNode NewItem(XmpOperation operation) =>
        operation.Structure
            ? new XmpNode(Item, "rdf") { Kind = XmpEntryKind.Struct }
            : new XmpNode(Item, "rdf") { Value = operation.Value!.Text! };

    /// <summary>The node a value is set on: the one at the path, or a new one where the path ends.</summary>
    private XmpNode Target(XmpStep[] path, XmpStep last)
    {
        // A structure that is missing is created for a field; a qualifier needs the value it qualifies.
        XmpNode? parent = path.Length == 1 ? null : Walk(path, path.Length - 1, last.Kind == XmpStepKind.Field);
        if (parent is null && last.Kind == XmpStepKind.Qualifier) throw Refused("a qualifier is set on a node the packet does not have");
        switch (last.Kind)
        {
            case XmpStepKind.Property:
            {
                XName name = NameOf(last);
                if (tree.Find(name) is XmpNode existing) return existing;
                XmpNode created = Create(last, name);
                tree.Properties.Add(created);
                return created;
            }
            case XmpStepKind.Field:
            {
                XName name = NameOf(last);
                if (parent!.Kind != XmpEntryKind.Struct)
                    throw Refused("\"" + last.Prefix + ":" + last.Name + "\" is set on a node that is not a structure");
                if (Find(parent.Children, name) is XmpNode existing) return existing;
                XmpNode created = Create(last, name);
                parent.Children.Add(created);
                return created;
            }
            case XmpStepKind.Qualifier:
            {
                XName name = NameOf(last);
                if (Find(parent!.Qualifiers, name) is XmpNode existing) return existing;
                XmpNode created = Create(last, name);
                parent.Qualifiers.Add(created);
                return created;
            }
            default:
                // An item: Walk has refused an index outside the array.
                return Walk(path, path.Length, true)!;
        }
    }

    /// <summary>A new, empty array where the path ends.</summary>
    private XmpNode NewArray(XmpStep[] path, XmpStep last, XmpEntryKind kind)
    {
        if (last.Kind is XmpStepKind.Index or XmpStepKind.Last)
            throw Refused("the array item the path names does not exist");
        XmpNode array = Target(path, last);
        array.Kind = kind;
        return array;
    }

    /// <summary>Replaces the value of a node; its language and qualifiers stay.</summary>
    private static void Fill(XmpNode node, XmpValue value)
    {
        node.Children.Clear();
        node.Value = string.Empty;
        node.IsUri = false;
        switch (value.Kind)
        {
            case XmpValueKind.Text:
                node.Kind = XmpEntryKind.Text;
                node.Value = value.Text!;
                break;
            case XmpValueKind.LanguageAlternative:
                node.Kind = XmpEntryKind.Alt;
                foreach (KeyValuePair<string, string> pair in value.Languages)
                {
                    var item = new XmpNode(Item, "rdf") { Value = pair.Value, Language = pair.Key };
                    if (IsDefault(pair.Key)) node.Children.Insert(0, item);
                    else node.Children.Add(item);
                }
                break;
            default:
                node.Kind = Form(value.Kind);
                foreach (string text in value.Items) node.Children.Add(new XmpNode(Item, "rdf") { Value = text });
                break;
        }
    }

    private void Remove(XmpStep[] path, XmpStep last)
    {
        if (path.Length == 1)
        {
            if (tree.Find(NameOf(last)) is XmpNode property) tree.Properties.Remove(property);
            return;
        }
        if (Walk(path, path.Length - 1, false) is not XmpNode parent) return;
        switch (last.Kind)
        {
            case XmpStepKind.Language:
                parent.Language = null;
                break;
            case XmpStepKind.Field:
                if (parent.Kind == XmpEntryKind.Struct && Find(parent.Children, NameOf(last)) is XmpNode field) parent.Children.Remove(field);
                break;
            case XmpStepKind.Qualifier:
                if (Find(parent.Qualifiers, NameOf(last)) is XmpNode qualifier) parent.Qualifiers.Remove(qualifier);
                break;
            default:
                if (!parent.IsArray) break;
                int index = last.Kind == XmpStepKind.Last ? parent.Children.Count : last.Index;
                if (index >= 1 && index <= parent.Children.Count) parent.Children.RemoveAt(index - 1);
                break;
        }
    }
}
