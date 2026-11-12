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
    internal static MetadataException Unsupported() => new(MetadataErrorCode.UnsupportedFeature, "The XMP packet holds RDF that cannot be written again without loss; it is copied and listed, not edited.");

    /// <summary>
    /// Reads a packet. <c>Tree</c> is the packet's tree when the packet can be written again from it
    /// without loss, else null; <c>Properties</c> are the top-level properties that are text, arrays of
    /// texts or language alternatives.
    /// </summary>
    internal static (XmpTree? Tree, Dictionary<XmpPropertyName, XmpValue> Properties, bool HasExtendedReference, IReadOnlyList<XmpEntry> Entries) Read(byte[] bytes, XmpLimits limits, CancellationToken token)
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
            XmpTree tree = XmpEntryReader.ReadTree(rdf, token, out bool lossless);
            IReadOnlyList<XmpEntry> entries = XmpEntryReader.Flatten(tree, limits);
            XName reference = XName.Get("HasExtendedXMP", XmpNamespaces.Note);
            bool hasReference = document.Descendants().Any(e => e.Name == reference || e.Attributes().Any(a => a.Name == reference));
            // What stands around the RDF is not kept by a rewrite, so anything there but the usual
            // wrapper makes the packet one that is copied and listed, not edited.
            bool editable = lossless && PlainContainer(rdf) && (root == rdf || (OnlyWhitespace(root!) && root!.Elements().Count() == 1 &&
                root.Attributes().All(a => a.IsNamespaceDeclaration || a.Name == XName.Get("xmptk", MetaUri))));
            var packetPis = document.DescendantNodes().OfType<XProcessingInstruction>().Where(p => p.Target == "xpacket").ToArray();
            if (packetPis.Length != 0)
            {
                string[] headers = ["begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"", "begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"",
                    "begin='' id='W5M0MpCehiHzreSzNTczkc9d'", "begin='\uFEFF' id='W5M0MpCehiHzreSzNTczkc9d'"];
                editable &= packetPis.Length == 2 && headers.Contains(packetPis[0].Data) && packetPis[1].Data is "end=\"w\"" or "end='w'";
            }
            var properties = new Dictionary<XmpPropertyName, XmpValue>();
            int propertyCount = 0, itemCount = 0;
            // A packet of one description is held to unique names and languages, as it always was; in
            // one of several descriptions a repeated name is left to the tree, which then is not lossless.
            bool single = rdf.Elements().Count() == 1;
            var seen = new HashSet<XmpPropertyName>();
            foreach (XElement description in rdf.Elements())
            {
                if (description.Name != Rdf + "Description") continue;
                foreach (XAttribute attribute in description.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name != Rdf + "about"))
                {
                    token.ThrowIfCancellationRequested();
                    if (++propertyCount > limits.MaxProperties) throw Limit();
                    if (!Name(attribute.Name, out var name)) continue;
                    if (!seen.Add(name)) { if (single) throw Duplicate(); continue; }
                    properties.Add(name, XmpValue.CreateText(attribute.Value));
                }
                foreach (XElement element in description.Elements())
                {
                    token.ThrowIfCancellationRequested();
                    if (++propertyCount > limits.MaxProperties) throw Limit();
                    if (!Name(element.Name, out var name)) continue;
                    if (!seen.Add(name)) { if (single) throw Duplicate(); continue; }
                    XmpValue? value;
                    try { value = Value(element, limits, ref itemCount); }
                    catch (MetadataException e) when (!single && e.Code == MetadataErrorCode.AmbiguousMetadata) { value = null; }
                    if (value is not null) properties.Add(name, value);
                }
            }
            return (editable ? tree : null, properties, hasReference, entries);
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
    private static MetadataException Duplicate() => new(MetadataErrorCode.AmbiguousMetadata, "Duplicate expanded XMP property name.");
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
}
