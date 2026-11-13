using System.Xml;

namespace MetadataSharp.Core.Xmp;

internal enum XmpOperationKind { Set, Remove, Append, Insert, SetLanguage, RemoveLanguage }

/// <summary>One change of an edit, captured when it was made.</summary>
internal sealed record XmpOperation(XmpOperationKind Kind, XmpStep[] Path, XmpValue? Value = null, bool Structure = false,
    XmpValueKind ArrayKind = XmpValueKind.Text, int Index = 0, string? Language = null);

/// <summary>
/// Changes to an XMP packet, applied in the order they were made: a later change sees the result of
/// the earlier ones. Whole removal cannot be mixed with other changes.
/// </summary>
/// <remarks>
/// <para>
/// A node is named by its expanded name (<see cref="XmpPropertyName"/>, top-level properties only) or
/// by a path in the syntax of <see cref="XmpEntry.Key"/>: <c>Xmp.dc.title</c>, a structure field
/// <c>Xmp.xmpMM.DerivedFrom/stRef:documentID</c>, an array item counted from one
/// <c>Xmp.dc.subject[2]</c> or <c>Xmp.xmpMM.History[last()]/stEvt:action</c>, a qualifier
/// <c>Xmp.dc.creator[1]/?ns:role</c>, the language qualifier <c>Xmp.ns.note/?xml:lang</c>.
/// </para>
/// <para>
/// A prefix stands for the namespace registered with <see cref="RegisterNamespace"/>, else the one the
/// packet declares under it, else the conventional one (<c>dc</c>, <c>xmp</c>, <c>xmpMM</c>,
/// <c>stEvt</c>, <c>Iptc4xmpCore</c> and the prefixes <see cref="XmpEntry.Prefix"/> reports). Identity is
/// the namespace URI: a packet that declares another prefix for the namespace keeps its prefix.
/// </para>
/// <para>
/// The syntax of a path is checked when the change is made (<see cref="ArgumentException"/>); whether
/// it applies to a packet is checked when the edit is applied
/// (<see cref="MetadataErrorCode.InvalidXmpPath"/>), and then nothing is written.
/// </para>
/// </remarks>
public sealed class XmpEdit
{
    private const int MaxOperations = 8192;
    private readonly List<XmpOperation> operations = [];
    private readonly Dictionary<string, string> namespaces = new(StringComparer.Ordinal);
    private bool removeAll;

    /// <summary>Creates an edit that holds no change.</summary>
    public XmpEdit() { }

    /// <summary>True when the edit holds no change.</summary>
    public bool IsEmpty => !removeAll && operations.Count == 0 && replacement is null;

    internal bool RemovesAll => removeAll;

    private XmpTree? replacement;

    /// <summary>What the packet is to hold in place of everything it holds, or null; the changes of the edit follow it.</summary>
    internal XmpTree? Replacement => replacement;

    /// <summary>
    /// Puts everything <paramref name="packet"/> holds in place of everything the packet of the file
    /// holds: its properties with their structures, qualifiers and prefixes. Changes made to this edit
    /// apply after it. A file without a packet gets one. This is how a sidecar file or the packet of
    /// another file is inserted (<c>exiv2 in</c>).
    /// </summary>
    /// <exception cref="ArgumentException">The edit removes the whole packet.</exception>
    /// <exception cref="MetadataException"><see cref="MetadataErrorCode.UnsupportedFeature"/> when <paramref name="packet"/> cannot be edited (<see cref="XmpPacket.IsEditable"/>).</exception>
    /// <param name="packet">The source packet whose properties replace the current packet's properties.</param>
    public void ReplaceWith(XmpPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (removeAll) throw new ArgumentException("Whole XMP removal cannot be mixed with property changes.");
        if (packet.Tree is null) throw XmpCodec.Unsupported();
        replacement = packet.Tree.Clone();
    }
    internal IReadOnlyList<XmpOperation> Operations => operations;
    internal IReadOnlyDictionary<string, string> Namespaces => namespaces;

    /// <summary>
    /// Registers a prefix for a namespace, for paths of this edit and for the declaration that is
    /// written when the packet does not declare the namespace yet. Registering is not a change.
    /// </summary>
    /// <exception cref="ArgumentException">The prefix is not an XML name or is reserved (<c>xml</c>, <c>xmlns</c>, <c>rdf</c>, <c>x</c>), it is registered for another namespace, or the URI is not absolute or is reserved.</exception>
    /// <param name="prefix">The XML prefix used in paths and serialization.</param>
    /// <param name="namespaceUri">The absolute namespace URI bound to the prefix.</param>
    public void RegisterNamespace(string prefix, string namespaceUri)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(namespaceUri);
        try { XmlConvert.VerifyNCName(prefix); }
        catch (Exception e) when (e is XmlException or ArgumentException) { throw new ArgumentException("The prefix is not an XML name.", nameof(prefix)); }
        if (prefix is "xml" or "xmlns" or "rdf" or "x" || prefix.Length > 1024) throw new ArgumentException("The prefix is reserved or too long.", nameof(prefix));
        // The rules of a property's namespace: absolute, not reserved, within the name budget.
        _ = new XmpPropertyName(namespaceUri, "name");
        if (namespaces.TryGetValue(prefix, out string? registered) && registered != namespaceUri)
            throw new ArgumentException("The prefix is registered for another namespace.", nameof(prefix));
        if (namespaces.Count >= 256 && registered is null) throw new ArgumentException("The edit registers more than 256 namespaces.", nameof(prefix));
        namespaces[prefix] = namespaceUri;
    }

    /// <summary>Sets a top-level property, replacing its value whatever form it had; qualifiers of an existing property stay.</summary>
    /// <exception cref="ArgumentException">The name is not valid, the edit already removes all XMP, or the edit holds more than 8,192 changes.</exception>
    /// <param name="name">The expanded name of the top-level property.</param>
    /// <param name="value">The immutable value to assign.</param>
    public void Set(XmpPropertyName name, XmpValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Add(new(XmpOperationKind.Set, XmpPath.Property(name), value));
    }

    /// <summary>Removes a top-level property; one the packet does not have is no change.</summary>
    /// <exception cref="ArgumentException">As <see cref="Set(XmpPropertyName, XmpValue)"/>.</exception>
    /// <param name="name">The expanded name of the top-level property to remove.</param>
    public void Remove(XmpPropertyName name) => Add(new(XmpOperationKind.Remove, XmpPath.Property(name)));

    /// <summary>
    /// Sets the node at the path to a simple value: a property, a structure field, an array item that
    /// exists, a qualifier, or the language qualifier. Structures on the way that the packet does not
    /// have are created; an array and its items are not (see <see cref="Append"/> and
    /// <see cref="AppendStruct"/>). A node that exists has its value replaced whatever form it had and
    /// keeps its qualifiers.
    /// </summary>
    /// <exception cref="ArgumentException">The path or the text is not valid (see <see cref="XmpValue.CreateText"/>), or as <see cref="Set(XmpPropertyName, XmpValue)"/>.</exception>
    /// <param name="path">The XMP path identifying the node to set.</param>
    /// <param name="text">The text value to assign.</param>
    public void Set(string path, string text) => SetValue(path, XmpValue.CreateText(text));

    /// <summary>As <see cref="Set(string, string)"/>, with a value of any form: text, an array of texts or a language alternative.</summary>
    /// <exception cref="ArgumentException">As <see cref="Set(string, string)"/>; also when an array or a language alternative is given for <c>/?xml:lang</c>.</exception>
    /// <param name="path">The XMP path identifying the node to set.</param>
    /// <param name="value">The immutable value to assign.</param>
    public void SetValue(string path, XmpValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        XmpStep[] steps = XmpPath.Parse(path);
        if (steps[^1].Kind == XmpStepKind.Language && (value.Kind != XmpValueKind.Text || !XmpValue.ValidLanguage(value.Text)))
            throw new ArgumentException("xml:lang takes a language tag.", nameof(value));
        Add(new(XmpOperationKind.Set, steps, value));
    }

    /// <summary>
    /// Removes the node at the path with everything below it: a property, a field, a qualifier, or an
    /// array item, after which the later items count one lower. A node the packet does not have is
    /// no change. A structure or an array left empty stays.
    /// </summary>
    /// <exception cref="ArgumentException">The path is not valid, or as <see cref="Set(XmpPropertyName, XmpValue)"/>.</exception>
    /// <param name="path">The XMP path identifying the node to remove.</param>
    public void RemovePath(string path) => Add(new(XmpOperationKind.Remove, XmpPath.Parse(path)));

    /// <summary>
    /// Adds a simple value as the last item of the array at the path. An array the packet does not
    /// have is created in the given form, with the structures on the way to it; one it has must be of
    /// that form.
    /// </summary>
    /// <exception cref="ArgumentException">The form is not <see cref="XmpValueKind.Bag"/>, <see cref="XmpValueKind.Sequence"/> or <see cref="XmpValueKind.Alternative"/>, or as <see cref="Set(string, string)"/>.</exception>
    /// <param name="path">The path of the array to append to.</param>
    /// <param name="arrayKind">The required form when the array must be created.</param>
    /// <param name="text">The text value for the new final item.</param>
    public void Append(string path, XmpValueKind arrayKind, string text) =>
        Add(new(XmpOperationKind.Append, ArrayPath(path), XmpValue.CreateText(text), ArrayKind: ArrayForm(arrayKind)));

    /// <summary>
    /// As <see cref="Append"/>, adding an empty structure; its fields are then set through
    /// <c>path[last()]/prefix:field</c>.
    /// </summary>
    /// <exception cref="ArgumentException">As <see cref="Append"/>.</exception>
    /// <param name="path">The path of the array to append to.</param>
    /// <param name="arrayKind">The required form when the array must be created.</param>
    public void AppendStruct(string path, XmpValueKind arrayKind) =>
        Add(new(XmpOperationKind.Append, ArrayPath(path), Structure: true, ArrayKind: ArrayForm(arrayKind)));

    /// <summary>
    /// Inserts a simple value into the array at the path, before the item that has the index now;
    /// one more than the number of items appends. The array has to exist.
    /// </summary>
    /// <exception cref="ArgumentException">The index is below one, or as <see cref="Set(string, string)"/>.</exception>
    /// <param name="path">The path of the existing array.</param>
    /// <param name="index">The one-based position before which to insert; the count plus one appends.</param>
    /// <param name="text">The text value for the new item.</param>
    public void Insert(string path, int index, string text) =>
        Add(new(XmpOperationKind.Insert, ArrayPath(path), XmpValue.CreateText(text), Index: Position(index)));

    /// <summary>As <see cref="Insert"/>, inserting an empty structure; its fields are then set through <c>path[index]/prefix:field</c>.</summary>
    /// <exception cref="ArgumentException">As <see cref="Insert"/>.</exception>
    /// <param name="path">The path of the existing array.</param>
    /// <param name="index">The one-based position before which to insert; the count plus one appends.</param>
    public void InsertStruct(string path, int index) =>
        Add(new(XmpOperationKind.Insert, ArrayPath(path), Structure: true, Index: Position(index)));

    /// <summary>
    /// Sets the text of one language in the language alternative at the path, creating the
    /// alternative when the packet does not have it. Languages are compared without case. The
    /// <c>x-default</c> item is kept first; a new item of another language is added last. No item is
    /// derived from another: <c>x-default</c> is written only when it is set.
    /// </summary>
    /// <exception cref="ArgumentException">The language is not a language tag, or as <see cref="Set(string, string)"/>.</exception>
    /// <param name="path">The path of the language alternative.</param>
    /// <param name="language">The BCP 47 language tag of the item to set.</param>
    /// <param name="text">The text for that language item.</param>
    public void SetLanguage(string path, string language, string text)
    {
        if (!XmpValue.ValidLanguage(language)) throw new ArgumentException("Not a language tag.", nameof(language));
        Add(new(XmpOperationKind.SetLanguage, ArrayPath(path), XmpValue.CreateText(text), Language: language));
    }

    /// <summary>
    /// Removes the item of one language from the language alternative at the path; when it was the
    /// only item, the alternative goes with it. An item the packet does not have is no change.
    /// </summary>
    /// <exception cref="ArgumentException">As <see cref="SetLanguage"/>.</exception>
    /// <param name="path">The path of the language alternative.</param>
    /// <param name="language">The BCP 47 language tag of the item to remove.</param>
    public void RemoveLanguage(string path, string language)
    {
        if (!XmpValue.ValidLanguage(language)) throw new ArgumentException("Not a language tag.", nameof(language));
        Add(new(XmpOperationKind.RemoveLanguage, ArrayPath(path), Language: language));
    }

    /// <summary>Removes the whole XMP packet.</summary>
    /// <exception cref="ArgumentException">The edit already holds a property change.</exception>
    public void RemoveAllXmp()
    {
        if (operations.Count != 0 || replacement is not null) throw new ArgumentException("Whole XMP removal cannot be mixed with property changes.");
        removeAll = true;
    }

    // Require array operations to end at a property or field, never at an item or qualifier.
    private static XmpStep[] ArrayPath(string path)
    {
        XmpStep[] steps = XmpPath.Parse(path);
        if (steps[^1].Kind == XmpStepKind.Language) throw new ArgumentException("xml:lang is not an array.", nameof(path));
        return steps;
    }

    private static XmpValueKind ArrayForm(XmpValueKind kind) =>
        kind is XmpValueKind.Bag or XmpValueKind.Sequence or XmpValueKind.Alternative
            ? kind
            : throw new ArgumentException("An array is a Bag, a Sequence or an Alternative; a language alternative is edited with SetLanguage.", nameof(kind));

    private static int Position(int index) =>
        index >= 1 ? index : throw new ArgumentOutOfRangeException(nameof(index), "Array items are counted from one.");

    // Record edits in order; the packet applies this immutable operation snapshot transactionally.
    private void Add(XmpOperation operation)
    {
        if (removeAll) throw new ArgumentException("Whole XMP removal cannot be mixed with property changes.");
        if (operations.Count >= MaxOperations) throw new ArgumentException("Edit exceeds the budget of 8,192 changes.");
        operations.Add(operation);
    }

    /// <summary>A copy that later changes to this edit do not reach.</summary>
    // Freeze operations and namespace registrations so subsequent caller edits cannot alter this batch.
    internal XmpEdit Snapshot()
    {
        var copy = new XmpEdit { removeAll = removeAll, replacement = replacement };
        copy.operations.AddRange(operations);
        foreach (KeyValuePair<string, string> pair in namespaces) copy.namespaces[pair.Key] = pair.Value;
        return copy;
    }
}
