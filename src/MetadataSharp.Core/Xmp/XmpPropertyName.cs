using System.Xml;

namespace MetadataSharp.Core.Xmp;

/// <summary>An expanded XML property name, independent of its serialization prefix.</summary>
public readonly record struct XmpPropertyName
{
    /// <summary>Creates a name from a namespace URI and a local name.</summary>
    /// <param name="namespaceUri">The absolute, nonreserved namespace URI.</param>
    /// <param name="localName">The XML local name within the namespace.</param>
    /// <exception cref="ArgumentException">The URI is not absolute or is reserved (RDF, <c>x:xmpmeta</c>, XML), or the local name is not an XML name.</exception>
    public XmpPropertyName(string namespaceUri, string localName)
    {
        NamespaceUri = namespaceUri;
        LocalName = localName;
        Validate();
    }
    /// <summary>The namespace URI, for example <c>http://purl.org/dc/elements/1.1/</c>.</summary>
    public string NamespaceUri { get; }
    /// <summary>The name inside the namespace, for example <c>title</c>.</summary>
    public string LocalName { get; }

    // Reject invalid namespace URIs, prefixes, and local names before constructing an expanded property name.
    internal void Validate()
    {
        if (string.IsNullOrEmpty(NamespaceUri) || !Uri.TryCreate(NamespaceUri, UriKind.Absolute, out _) ||
            NamespaceUri is XmpCodec.RdfUri or XmpCodec.MetaUri or "http://www.w3.org/XML/1998/namespace" or "http://www.w3.org/2000/xmlns/")
            throw new ArgumentException("A property requires an absolute, nonreserved namespace URI.");
        if (string.IsNullOrEmpty(LocalName) || NamespaceUri.Length > 1024 || LocalName.Length > 1024)
            throw new ArgumentException("A property name is empty or exceeds the default name budget.");
        try { XmlConvert.VerifyNCName(LocalName); XmlConvert.VerifyXmlChars(NamespaceUri); }
        catch (XmlException e) { throw new ArgumentException("Invalid XML property name.", e); }
    }
}
