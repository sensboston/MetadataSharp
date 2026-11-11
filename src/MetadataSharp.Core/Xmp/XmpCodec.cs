using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MetadataSharp.Core.Xmp;

internal static class XmpCodec
{
    internal const string RdfUri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal const string MetaUri = "adobe:ns:meta/";
    internal static readonly XNamespace Rdf = RdfUri;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "XMP exceeds a resource budget.");
    internal static MetadataException Unsupported() => new(MetadataErrorCode.UnsupportedFeature, "The XMP packet is outside the editable profile.");

    internal static (bool Editable, Dictionary<XmpPropertyName, XmpValue> Properties, bool HasExtendedReference, IReadOnlyList<XmpEntry> Entries) Read(byte[] bytes, XmpLimits limits, CancellationToken token)
    {
        if (bytes.Length > limits.MaxPacketBytes) throw Limit();
        try
        {
            string xml = Utf8.GetString(bytes);
            if (xml.StartsWith('\uFEFF')) xml = xml[1..];
            // Containers that store a packet in a fixed or aligned slot pad it with NUL bytes after the
            // closing processing instruction. They are not part of the document; the bytes stay as read.
            if (xml.EndsWith('\0')) xml = xml.TrimEnd('\0');
            if (xml.Length > limits.MaxXmlCharacters) throw Limit();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 0, IgnoreWhitespace = false };
            // Bound the full XML tree before allocating its DOM, including opaque RDF.
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                int nodes = 0, attributes = 0;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (++nodes > limits.MaxNodes || reader.Depth + 1 > limits.MaxDepth) throw Limit();
                    if (reader.LocalName.Length > limits.MaxNameCharacters || reader.NamespaceURI.Length > limits.MaxNameCharacters || reader.Prefix.Length > limits.MaxNameCharacters) throw Limit();
                    if (reader.NodeType == XmlNodeType.XmlDeclaration)
                    {
                        string? encoding = reader.GetAttribute("encoding");
                        if (encoding is not null && !encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                            throw new MetadataException(MetadataErrorCode.InvalidXmp, "Only UTF-8 XMP is supported.");
                    }
                    if (reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            if (++attributes > limits.MaxAttributes || reader.LocalName.Length > limits.MaxNameCharacters || reader.NamespaceURI.Length > limits.MaxNameCharacters ||
                                ((reader.Prefix == "xmlns" || reader.Name == "xmlns") && reader.Value.Length > limits.MaxNameCharacters)) throw Limit();
                        } while (reader.MoveToNextAttribute());
                        reader.MoveToElement();
                    }
                }
            }
            using var input = XmlReader.Create(new StringReader(xml), settings);
            XDocument document = XDocument.Load(input, LoadOptions.PreserveWhitespace);
            XElement? root = document.Root;
            XElement? rdf = root?.Name == Rdf + "RDF" ? root : root?.Name == XName.Get("xmpmeta", MetaUri) ? root.Elements(Rdf + "RDF").FirstOrDefault() : null;
            if (rdf is null) throw new MetadataException(MetadataErrorCode.InvalidXmp, "An XMP packet requires an RDF root.");
            IReadOnlyList<XmpEntry> entries = XmpEntryReader.Read(rdf, limits, token);
            XName reference = XName.Get("HasExtendedXMP", "http://ns.adobe.com/xmp/note/");
            bool hasReference = document.Descendants().Any(e => e.Name == reference || e.Attributes().Any(a => a.Name == reference));
            bool editable = PlainContainer(rdf) && (root == rdf || (OnlyWhitespace(root!) && root!.Elements().Count() == 1 &&
                root.Attributes().All(a => a.IsNamespaceDeclaration || a.Name == XName.Get("xmptk", MetaUri))));
            var packetPis = document.DescendantNodes().OfType<XProcessingInstruction>().Where(p => p.Target == "xpacket").ToArray();
            if (packetPis.Length != 0)
            {
                string[] headers = ["begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"", "begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"",
                    "begin='' id='W5M0MpCehiHzreSzNTczkc9d'", "begin='\uFEFF' id='W5M0MpCehiHzreSzNTczkc9d'"];
                editable &= packetPis.Length == 2 && headers.Contains(packetPis[0].Data) && packetPis[1].Data is "end=\"w\"" or "end='w'";
            }
            XElement[] descriptions = rdf.Elements().ToArray();
            var properties = new Dictionary<XmpPropertyName, XmpValue>();
            if (descriptions.Length != 1 || descriptions[0].Name != Rdf + "Description") return (false, properties, hasReference, entries);
            XElement description = descriptions[0];
            editable &= OnlyWhitespace(description) && description.Attribute(Rdf + "about")?.Value == "";
            int propertyCount = 0, itemCount = 0;
            var seen = new HashSet<XmpPropertyName>();
            foreach (XAttribute attribute in description.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name != Rdf + "about"))
            {
                token.ThrowIfCancellationRequested();
                if (++propertyCount > limits.MaxProperties) throw Limit();
                if (!Name(attribute.Name, out var name)) { editable = false; continue; }
                Duplicate(seen, name); properties.Add(name, XmpValue.CreateText(attribute.Value));
            }
            foreach (XElement element in description.Elements())
            {
                token.ThrowIfCancellationRequested();
                if (++propertyCount > limits.MaxProperties) throw Limit();
                if (!Name(element.Name, out var name)) { editable = false; continue; }
                Duplicate(seen, name);
                XmpValue? value = Value(element, limits, ref itemCount);
                if (value is null) editable = false; else properties.Add(name, value);
            }
            return (editable, properties, hasReference, entries);
        }
        catch (DecoderFallbackException e) { throw new MetadataException(MetadataErrorCode.InvalidXmp, "Invalid UTF-8 XMP: " + e.Message); }
        catch (ArgumentException) { throw Limit(); }
        catch (XmlException e) { throw new MetadataException(MetadataErrorCode.InvalidXmp, "Invalid or prohibited XML: " + e.Message); }
    }
    private static bool Name(XName source, out XmpPropertyName name)
    {
        try { name = new(source.NamespaceName, source.LocalName); return true; }
        catch (ArgumentException) { name = default; return false; }
    }
    private static void Duplicate(HashSet<XmpPropertyName> seen, XmpPropertyName name)
    {
        if (!seen.Add(name)) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate expanded XMP property name.");
    }
    private static bool OnlyWhitespace(XElement e) => !e.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value));
    private static bool PlainContainer(XElement e) => OnlyWhitespace(e) && e.Attributes().All(a => a.IsNamespaceDeclaration);
    private static bool NoQualifiers(XElement e) => e.Attributes().All(a => a.IsNamespaceDeclaration);
    private static XmpValue? Value(XElement element, XmpLimits limits, ref int itemCount)
    {
        if (!NoQualifiers(element)) return null;
        if (!element.HasElements) return XmpValue.CreateText(element.Value);
        if (!OnlyWhitespace(element) || element.Elements().Count() != 1) return null;
        XElement array = element.Elements().Single();
        if (array.Name != Rdf + "Bag" && array.Name != Rdf + "Seq" && array.Name != Rdf + "Alt") return null;
        if (!PlainContainer(array)) return null;
        var texts = new List<string>(); var languages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement item in array.Elements())
        {
            if (++itemCount > limits.MaxArrayItems) throw Limit();
            if (item.Name != Rdf + "li" || item.HasElements) return null;
            if (array.Name == Rdf + "Alt")
            {
                string? language = item.Attribute(XNamespace.Xml + "lang")?.Value;
                if (!XmpValue.ValidLanguage(language) || item.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != XNamespace.Xml + "lang")) return null;
                if (!languages.TryAdd(language!, item.Value)) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate XMP language.");
            }
            else { if (!NoQualifiers(item)) return null; texts.Add(item.Value); }
        }
        return array.Name == Rdf + "Alt" ? XmpValue.CreateLanguageAlternative(languages) : XmpValue.CreateArray(array.Name == Rdf + "Bag" ? XmpValueKind.Bag : XmpValueKind.Sequence, texts);
    }

    internal static byte[] Write(IReadOnlyDictionary<XmpPropertyName, XmpValue> properties, XmpLimits limits)
    {
        // Count UTF-8 output in a first pass, without growing an unbounded memory stream.
        using var counter = new BudgetStream(limits.MaxPacketBytes);
        Emit(counter, properties);
        using var output = new MemoryStream((int)counter.Length);
        Emit(output, properties);
        return output.ToArray();
    }
    private static void Emit(Stream stream, IReadOnlyDictionary<XmpPropertyName, XmpValue> properties)
    {
        using XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = Utf8, OmitXmlDeclaration = true, CloseOutput = false, NewLineHandling = NewLineHandling.Entitize });
        writer.WriteProcessingInstruction("xpacket", "begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"");
        writer.WriteStartElement("x", "xmpmeta", MetaUri);
        writer.WriteStartElement("rdf", "RDF", RdfUri);
        writer.WriteStartElement("rdf", "Description", RdfUri);
        writer.WriteAttributeString("rdf", "about", RdfUri, "");
        string[] namespaces = properties.Keys.Select(k => k.NamespaceUri).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        for (int i = 0; i < namespaces.Length; i++) writer.WriteAttributeString("xmlns", "p" + i, null, namespaces[i]);
        foreach (var pair in properties.OrderBy(p => p.Key.NamespaceUri, StringComparer.Ordinal).ThenBy(p => p.Key.LocalName, StringComparer.Ordinal))
        {
            writer.WriteStartElement("p" + Array.IndexOf(namespaces, pair.Key.NamespaceUri), pair.Key.LocalName, pair.Key.NamespaceUri);
            XmpValue value = pair.Value;
            if (value.Kind == XmpValueKind.Text) writer.WriteString(value.Text);
            else
            {
                writer.WriteStartElement("rdf", value.Kind == XmpValueKind.Bag ? "Bag" : value.Kind == XmpValueKind.Sequence ? "Seq" : "Alt", RdfUri);
                if (value.Kind == XmpValueKind.LanguageAlternative)
                {
                    foreach (var language in value.Languages.OrderBy(p => p.Key.Equals("x-default", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                    {
                        writer.WriteStartElement("rdf", "li", RdfUri);
                        writer.WriteAttributeString("xml", "lang", XNamespace.Xml.NamespaceName, language.Key);
                        writer.WriteString(language.Value); writer.WriteEndElement();
                    }
                }
                else foreach (string text in value.Items) { writer.WriteStartElement("rdf", "li", RdfUri); writer.WriteString(text); writer.WriteEndElement(); }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteProcessingInstruction("xpacket", "end=\"w\"");
    }
    private sealed class BudgetStream(int maximum) : Stream
    {
        private long length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => length;
        public override long Position { get => length; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Add(count);
        public override void Write(ReadOnlySpan<byte> buffer) => Add(buffer.Length);
        private void Add(int count) { length += count; if (length > maximum) throw Limit(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
