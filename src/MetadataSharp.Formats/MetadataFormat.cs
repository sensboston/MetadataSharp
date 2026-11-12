namespace MetadataSharp.Formats;

/// <summary>
/// What the first bytes of a file say it is, as <see cref="ImageMetadata.Detect"/> tells it. A
/// member names a signature and so a reader, not a camera or a file extension: where several kinds
/// of file share one signature they share one member.
/// </summary>
public enum MetadataFormat
{
    /// <summary>No signature the metadata readers know.</summary>
    Unknown = 0,

    /// <summary>JPEG: the bytes <c>FF D8</c>.</summary>
    Jpeg,

    /// <summary>PNG: the bytes <c>89 "PNG"</c>.</summary>
    Png,

    /// <summary>WebP: <c>"RIFF"</c>, a length, then <c>"WEBP"</c>.</summary>
    WebP,

    /// <summary>
    /// A TIFF structure: TIFF itself (<c>"II*\0"</c>, <c>"MM\0*"</c>) and every file built as one,
    /// which is DNG and the raw formats CR2, NEF, NRW, ARW, SR2 and PEF, together with ORF
    /// (<c>"IIRO"</c>, <c>"MMOR"</c>) and RW2 (<c>"IIU\0"</c>), which keep the structure under an
    /// identifier of their own. The signature does not tell these apart; the Make and DNGVersion
    /// tags of <see cref="ImageMetadata.Exif"/> do.
    /// </summary>
    Tiff,

    /// <summary>Photoshop PSD: <c>"8BPS"</c>.</summary>
    Psd,

    /// <summary>An Exiv2 metadata file: the bytes <c>FF 01 "Exiv2"</c>.</summary>
    Exv,

    /// <summary>JPEG 2000 in its boxed form: a signature box of type <c>"jP  "</c>.</summary>
    Jp2,

    /// <summary>
    /// An XMP sidecar file: a packet wrapper (<c>&lt;?xpacket</c>), an XML declaration
    /// (<c>&lt;?xml</c>), or the element <c>&lt;x:xmpmeta</c> or <c>&lt;rdf:RDF</c>, after a UTF-8
    /// byte order mark or without one. An XML declaration opens other documents too: whether the
    /// file holds a packet is for the XMP reader to say.
    /// </summary>
    XmpSidecar,

    /// <summary>Canon CR3: an ISO BMFF file whose file type box names the major brand <c>"crx "</c>.</summary>
    Cr3,

    /// <summary>
    /// ISO BMFF with any other major brand: a file type box (<c>"ftyp"</c>) comes first. HEIC, HEIF
    /// and AVIF are read; a file of another brand, a video for one, has the signature all the same.
    /// </summary>
    Bmff,

    /// <summary>
    /// JPEG XL: the signature box of a container, or the bytes <c>FF 0A</c> of a bare codestream,
    /// which holds no metadata.
    /// </summary>
    Jxl,

    /// <summary>Canon CRW (CIFF): the byte order twice, the length of the header and <c>HEAPCCDR</c>.</summary>
    Crw,

    /// <summary>Minolta MRW: the header block <c>\0MRM</c>.</summary>
    Mrw,

    /// <summary>Fujifilm RAF: the text <c>FUJIFILM</c>.</summary>
    Raf
}
