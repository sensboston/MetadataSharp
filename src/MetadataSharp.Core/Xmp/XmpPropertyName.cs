using System.Xml;

namespace MetadataSharp.Core.Xmp;

/// <summary>An expanded XML property name, independent of its serialization prefix.</summary>
public readonly record struct XmpPropertyName
{
    public XmpPropertyName(string namespaceUri, string localName)
    {
        NamespaceUri = namespaceUri;
        LocalName = localName;
        Validate();
    }
    public string NamespaceUri { get; }
    public string LocalName { get; }

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
