using System.Collections.ObjectModel;

namespace MetadataSharp.Core.Xmp;

/// <summary>The form of one node in the flattened view of a packet.</summary>
public enum XmpEntryKind
{
    /// <summary>A simple value.</summary>
    Text,
    /// <summary>An unordered array.</summary>
    Bag,
    /// <summary>An ordered array.</summary>
    Seq,
    /// <summary>An array of alternatives whose items are not all language-tagged text.</summary>
    Alt,
    /// <summary>An array of alternatives in which every item is text tagged with <c>xml:lang</c>.</summary>
    LangAlt,
    /// <summary>A structure; its fields are the entries that follow it.</summary>
    Struct
}

/// <summary>
/// One node of a packet's property tree in the flattened, read-only view that
/// <see cref="XmpPacket.Entries"/> returns. Values are the packet's text, not interpreted.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Path"/> starts with the local name of a top-level property and continues with one step
/// per level: <c>/prefix:field</c> for a structure field, <c>[n]</c> for the n-th array item counted
/// from one, and <c>/?prefix:qualifier</c> for a qualifier, including <c>/?xml:lang</c> on anything
/// that is not an item of a language alternative. Steps use the prefix the packet declares.
/// </para>
/// <para>
/// An array of plain text items is a single entry holding the items. A structure, and an array with
/// an item that is a structure, an array or a qualified value, holds no values itself: its
/// <see cref="HasChildEntries"/> is true and its fields or its items follow as entries of their own.
/// </para>
/// </remarks>
public sealed class XmpEntry
{
    internal XmpEntry(string path, string namespaceUri, string prefix, XmpEntryKind kind, bool hasChildEntries,
        string[] values, (string Language, string Value)[] languages)
    {
        Path = path;
        NamespaceUri = namespaceUri;
        Prefix = prefix;
        Kind = kind;
        HasChildEntries = hasChildEntries;
        Values = new ReadOnlyCollection<string>(values);
        Languages = new ReadOnlyCollection<(string Language, string Value)>(languages);
    }

    /// <summary>The path of the node below its namespace, for example <c>History[2]/stEvt:action</c>.</summary>
    public string Path { get; }

    /// <summary>The namespace URI of the top-level property the node belongs to.</summary>
    public string NamespaceUri { get; }

    /// <summary>
    /// The prefix of that namespace: the conventional one for a well-known namespace, otherwise the
    /// one the packet declares, and the first in scope when it declares several for one namespace.
    /// </summary>
    public string Prefix { get; }

    /// <summary>The form of the node.</summary>
    public XmpEntryKind Kind { get; }

    /// <summary>
    /// True for a structure, and for an array whose items are reported as the entries
    /// <c>Path[1]</c>, <c>Path[2]</c> and so on rather than in <see cref="Values"/>.
    /// </summary>
    public bool HasChildEntries { get; }

    /// <summary>
    /// The text of a simple value (one item), the items of an array of plain text in packet order, or
    /// the texts of a language alternative. Empty when <see cref="HasChildEntries"/> is true.
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>The items of a language alternative in packet order; empty for every other kind.</summary>
    public IReadOnlyList<(string Language, string Value)> Languages { get; }
}
