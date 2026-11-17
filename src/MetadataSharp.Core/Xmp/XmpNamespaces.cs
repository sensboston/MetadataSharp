namespace MetadataSharp.Core.Xmp;

/// <summary>
/// Namespaces known by a conventional prefix. The URIs and prefixes are those the XMP, IPTC, PLUS
/// and MWG specifications and the applications that own the other schemas publish.
/// </summary>
internal static class XmpNamespaces
{
    internal const string Note = "http://ns.adobe.com/xmp/note/";

    /// <summary>
    /// Namespaces whose top-level properties are listed under one prefix whatever the packet
    /// declares: older packets declare <c>xap</c>, <c>xapMM</c> and <c>xapRights</c>, the IPTC schemas
    /// are declared under their long names, and earlier versions of this library declared generated
    /// prefixes.
    /// </summary>
    internal static readonly Dictionary<string, string> ListedPrefixes = new(StringComparer.Ordinal)
    {
        ["http://purl.org/dc/elements/1.1/"] = "dc",
        ["http://ns.adobe.com/xap/1.0/"] = "xmp",
        ["http://ns.adobe.com/xap/1.0/rights/"] = "xmpRights",
        ["http://ns.adobe.com/xap/1.0/mm/"] = "xmpMM",
        ["http://ns.adobe.com/xap/1.0/bj/"] = "xmpBJ",
        ["http://ns.adobe.com/xmp/1.0/DynamicMedia/"] = "xmpDM",
        [Note] = "xmpNote",
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

    /// <summary>The prefix a packet conventionally declares for a namespace, where it differs from the listed one or the namespace is not listed.</summary>
    private static readonly Dictionary<string, string> WrittenPrefixes = new(StringComparer.Ordinal)
    {
        ["http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/"] = "Iptc4xmpCore",
        ["http://iptc.org/std/Iptc4xmpExt/2008-02-29/"] = "Iptc4xmpExt",
        ["http://ns.adobe.com/xap/1.0/sType/ResourceEvent#"] = "stEvt",
        ["http://ns.adobe.com/xap/1.0/sType/ResourceRef#"] = "stRef",
        ["http://ns.adobe.com/xap/1.0/sType/Dimensions#"] = "stDim",
        ["http://ns.adobe.com/xap/1.0/sType/Version#"] = "stVer",
        ["http://ns.adobe.com/xap/1.0/sType/Job#"] = "stJob",
        ["http://ns.adobe.com/xap/1.0/sType/Font#"] = "stFnt",
        ["http://ns.adobe.com/xap/1.0/sType/ManifestItem#"] = "stMfs",
        ["http://ns.adobe.com/xmp/sType/Area#"] = "stArea",
        ["http://ns.adobe.com/xap/1.0/g/"] = "xmpG",
        ["http://ns.adobe.com/xap/1.0/g/img/"] = "xmpGImg",
        ["http://ns.adobe.com/xap/1.0/t/pg/"] = "xmpTPg",
        ["http://ns.adobe.com/xmp/Identifier/qual/1.0/"] = "xmpidq",
        ["http://ns.adobe.com/camera-raw-saved-settings/1.0/"] = "crss",
        ["http://ns.adobe.com/lightroom/1.0/"] = "lr",
        ["http://ns.adobe.com/pdfx/1.3/"] = "pdfx",
        ["http://www.aiim.org/pdfa/ns/id/"] = "pdfaid",
        ["http://ns.useplus.org/ldf/xmp/1.0/"] = "plus",
        ["http://www.metadataworkinggroup.com/schemas/regions/"] = "mwg-rs",
        ["http://www.metadataworkinggroup.com/schemas/keywords/"] = "mwg-kw",
        ["http://www.metadataworkinggroup.com/schemas/collections/"] = "mwg-coll",
        ["http://ns.google.com/photos/1.0/panorama/"] = "GPano",
        ["http://purl.org/dc/terms/"] = "dcterms",
        ["http://creativecommons.org/ns#"] = "cc",
        ["http://www.digikam.org/ns/1.0/"] = "digiKam",
        ["http://ns.acdsee.com/iptc/1.0/"] = "acdsee",
        ["http://ns.microsoft.com/photo/1.2/"] = "MP",
        ["http://ns.microsoft.com/photo/1.2/t/RegionInfo#"] = "MPRI",
        ["http://ns.microsoft.com/photo/1.2/t/Region#"] = "MPReg",
    };

    private static readonly Dictionary<string, string> UriByPrefix = BuildUriByPrefix();

    // Build the reverse built-in namespace map and fail if aliases collide with different URIs.
    private static Dictionary<string, string> BuildUriByPrefix()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in ListedPrefixes) map[pair.Value] = pair.Key;
        foreach (KeyValuePair<string, string> pair in WrittenPrefixes) map[pair.Value] = pair.Key;
        return map;
    }

    /// <summary>The namespace a conventional prefix stands for: either the listed or the written prefix.</summary>
    internal static bool TryGetUri(string prefix, out string uri)
    {
        bool found = UriByPrefix.TryGetValue(prefix, out string? value);
        uri = value ?? string.Empty;
        return found;
    }

    /// <summary>The prefix a new declaration of the namespace is written with.</summary>
    internal static bool TryGetPrefix(string uri, out string prefix)
    {
        bool found = WrittenPrefixes.TryGetValue(uri, out string? value) || ListedPrefixes.TryGetValue(uri, out value);
        prefix = value ?? string.Empty;
        return found;
    }
}
