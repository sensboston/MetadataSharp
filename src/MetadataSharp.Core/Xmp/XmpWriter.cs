using System.Text;
using System.Xml;

namespace MetadataSharp.Core.Xmp;

/// <summary>
/// Serializes a tree as RDF/XML in one canonical form: one <c>rdf:Description</c> that declares every
/// namespace, every property as an element, structures with <c>rdf:parseType="Resource"</c>, a
/// qualified value as a resource with <c>rdf:value</c>, a URI value as <c>rdf:resource</c>.
/// </summary>
internal static class XmpWriter
{
    private const string XmlUri = "http://www.w3.org/XML/1998/namespace";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>
    /// The packet of a tree. <paramref name="registered"/> maps prefixes to namespaces and is asked
    /// for a namespace no node of the tree brings a prefix for. Without <paramref name="wrapper"/>
    /// the <c>xpacket</c> processing instructions are left out, as in the extension part of
    /// Extended XMP.
    /// </summary>
    internal static byte[] Write(XmpTree tree, XmpLimits limits, IReadOnlyDictionary<string, string>? registered = null, bool wrapper = true)
    {
        Dictionary<string, string> prefixes = Prefixes(tree, registered);
        // Count UTF-8 output in a first pass, without growing an unbounded memory stream.
        using var counter = new BudgetStream(limits.MaxPacketBytes);
        Emit(counter, tree, prefixes, wrapper);
        using var output = new MemoryStream((int)counter.Length);
        Emit(output, tree, prefixes, wrapper);
        return output.ToArray();
    }

    /// <summary>
    /// The packet with whitespace before its closing processing instruction, <paramref name="length"/>
    /// bytes long in all; the packet itself when it is that long already or has no such instruction.
    /// </summary>
    internal static byte[] Pad(byte[] packet, int length)
    {
        ReadOnlySpan<byte> trailer = "<?xpacket end="u8;
        int at = packet.AsSpan().LastIndexOf(trailer);
        if (at < 0 || length <= packet.Length) return packet;
        byte[] padded = new byte[length];
        packet.AsSpan(0, at).CopyTo(padded);
        int fill = length - packet.Length;
        for (int i = 0; i < fill; i++) padded[at + i] = (byte)(i % 100 == 99 || i == fill - 1 ? '\n' : ' ');
        packet.AsSpan(at).CopyTo(padded.AsSpan(at + fill));
        return padded;
    }

    /// <summary>One prefix for each namespace of the tree, in the order the namespaces first occur.</summary>
    private static Dictionary<string, string> Prefixes(XmpTree tree, IReadOnlyDictionary<string, string>? registered)
    {
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal) { "rdf", "xml", "xmlns", "x" };
        foreach (XmpNode property in tree.Properties) Assign(property, prefixes, used, registered);
        return prefixes;
    }

    // Select one collision-free prefix per namespace while preserving a node's prefix where possible.
    private static void Assign(XmpNode node, Dictionary<string, string> prefixes, HashSet<string> used, IReadOnlyDictionary<string, string>? registered)
    {
        string uri = node.Name.NamespaceName;
        if (uri != XmpCodec.RdfUri && !prefixes.ContainsKey(uri))
        {
            string? prefix = null;
            if (Free(node.Prefix, used)) prefix = node.Prefix;
            if (prefix is null && registered is not null)
            {
                foreach (KeyValuePair<string, string> pair in registered)
                    if (pair.Value == uri && Free(pair.Key, used)) { prefix = pair.Key; break; }
            }
            if (prefix is null && XmpNamespaces.TryGetPrefix(uri, out string conventional) && Free(conventional, used)) prefix = conventional;
            for (int i = 1; prefix is null; i++)
            {
                string generated = "ns" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (Free(generated, used)) prefix = generated;
            }
            used.Add(prefix);
            prefixes.Add(uri, prefix);
        }
        foreach (XmpNode child in node.Children) Assign(child, prefixes, used, registered);
        foreach (XmpNode qualifier in node.Qualifiers) Assign(qualifier, prefixes, used, registered);
    }

    private static bool Free(string prefix, HashSet<string> used) => prefix.Length != 0 && !used.Contains(prefix);

    // Emit canonical RDF/XML without indentation so the serialized tree stays within the reader's node budget.
    private static void Emit(Stream stream, XmpTree tree, Dictionary<string, string> prefixes, bool wrapper)
    {
        var settings = new XmlWriterSettings
        {
            // No indentation: every whitespace node counts against the node budget of the reader.
            Encoding = Utf8, OmitXmlDeclaration = true, CloseOutput = false, NewLineHandling = NewLineHandling.Entitize
        };
        using XmlWriter writer = XmlWriter.Create(stream, settings);
        if (wrapper) writer.WriteProcessingInstruction("xpacket", "begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"");
        writer.WriteStartElement("x", "xmpmeta", XmpCodec.MetaUri);
        writer.WriteStartElement("rdf", "RDF", XmpCodec.RdfUri);
        writer.WriteStartElement("rdf", "Description", XmpCodec.RdfUri);
        writer.WriteAttributeString("rdf", "about", XmpCodec.RdfUri, tree.About);
        foreach (KeyValuePair<string, string> pair in prefixes) writer.WriteAttributeString("xmlns", pair.Value, null, pair.Key);
        foreach (XmpNode property in tree.Properties) Named(writer, prefixes, property);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        if (wrapper) writer.WriteProcessingInstruction("xpacket", "end=\"w\"");
    }

    private static void Named(XmlWriter writer, Dictionary<string, string> prefixes, XmpNode node)
    {
        string uri = node.Name.NamespaceName;
        Element(writer, prefixes, node, uri == XmpCodec.RdfUri ? "rdf" : prefixes[uri], node.Name.LocalName, uri);
    }

    private static void Element(XmlWriter writer, Dictionary<string, string> prefixes, XmpNode node, string prefix, string localName, string uri)
    {
        writer.WriteStartElement(prefix, localName, uri);
        if (node.Language is not null) writer.WriteAttributeString("xml", "lang", XmlUri, node.Language);
        if (node.Qualifiers.Count != 0)
        {
            // A qualified value: the value moves into rdf:value and the qualifiers stand beside it.
            writer.WriteAttributeString("rdf", "parseType", XmpCodec.RdfUri, "Resource");
            writer.WriteStartElement("rdf", "value", XmpCodec.RdfUri);
            Content(writer, prefixes, node);
            writer.WriteEndElement();
            foreach (XmpNode qualifier in node.Qualifiers) Named(writer, prefixes, qualifier);
        }
        else Content(writer, prefixes, node);
        writer.WriteEndElement();
    }

    /// <summary>The value of a node on the element that is open: attributes first, then content.</summary>
    private static void Content(XmlWriter writer, Dictionary<string, string> prefixes, XmpNode node)
    {
        switch (node.Kind)
        {
            case XmpEntryKind.Struct:
                writer.WriteAttributeString("rdf", "parseType", XmpCodec.RdfUri, "Resource");
                foreach (XmpNode field in node.Children) Named(writer, prefixes, field);
                break;
            case XmpEntryKind.Bag:
            case XmpEntryKind.Seq:
            case XmpEntryKind.Alt:
                writer.WriteStartElement("rdf", node.Kind == XmpEntryKind.Bag ? "Bag" : node.Kind == XmpEntryKind.Seq ? "Seq" : "Alt", XmpCodec.RdfUri);
                foreach (XmpNode item in node.Children) Element(writer, prefixes, item, "rdf", "li", XmpCodec.RdfUri);
                writer.WriteEndElement();
                break;
            default:
                if (node.IsUri) writer.WriteAttributeString("rdf", "resource", XmpCodec.RdfUri, node.Value);
                else if (node.Value.Length != 0) writer.WriteString(node.Value);
                break;
        }
    }

    /// <summary>Counts encoded output bytes without retaining them and aborts as soon as the packet budget is crossed.</summary>
    private sealed class BudgetStream(int maximum) : Stream
    {
        private long length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => length;
        // The measured byte count is the only position this write-only counting stream supports.
        public override long Position { get => length; set => throw new NotSupportedException(); }
        // Count writes without copying their contents; the second Emit pass writes the actual bytes.
        public override void Write(byte[] buffer, int offset, int count) => Add(count);
        public override void Write(ReadOnlySpan<byte> buffer) => Add(buffer.Length);
        // Keep the first pass bounded even when a hostile tree would serialize beyond the packet limit.
        private void Add(int count) { length += count; if (length > maximum) throw XmpCodec.Limit(); }
        public override void Flush() { }
        // This stream exists only for the counting write pass; read, seek, and resize would break that invariant.
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        // Support relative and absolute seeks while rejecting positions outside the bounded output buffer.
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        // Prevent XML serialization from expanding or truncating beyond the configured packet budget.
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
