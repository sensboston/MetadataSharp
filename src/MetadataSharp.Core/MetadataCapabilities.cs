using System.Collections.ObjectModel;

namespace MetadataSharp.Core;

/// <summary>Container/file categories for capability discovery. Unknown = 0.
/// Members cover the 31 documented v0.28.9 baseline category rows; the XMP
/// row is represented as XmpSidecar. Every documented category is present so
/// "not supported" is an explicit answer, not an absence of information.</summary>
public enum MetadataContainer
{
    /// <summary>No container information; never assumed supported.</summary>
    Unknown = 0,
    /// <summary>JPEG (JFIF/Exif in APP segments).</summary>
    Jpeg,
    /// <summary>PNG (static images only in this build).</summary>
    Png,
    /// <summary>WebP (static images only in this build).</summary>
    WebP,
    /// <summary>Standalone classic TIFF (baseline II/MM, non-BigTIFF).</summary>
    Tiff,
    /// <summary>Digital Negative (DNG).</summary>
    Dng,
    /// <summary>Sony ARW.</summary>
    Arw,
    /// <summary>AVIF (BMFF-based).</summary>
    Avif,
    /// <summary>BMP bitmap.</summary>
    Bmp,
    /// <summary>Canon CR2 (TIFF-derived RAW).</summary>
    Cr2,
    /// <summary>Canon CR3 (BMFF-based RAW).</summary>
    Cr3,
    /// <summary>Canon CRW/CIFF (legacy RAW).</summary>
    Crw,
    /// <summary>DNG Camera Profile (DCP).</summary>
    Dcp,
    /// <summary>Encapsulated PostScript.</summary>
    Eps,
    /// <summary>Adobe EXV.</summary>
    Exv,
    /// <summary>CompuServe GIF.</summary>
    Gif,
    /// <summary>High Efficiency Image Coding (HEIC).</summary>
    Heic,
    /// <summary>High Efficiency Image File (HEIF).</summary>
    Heif,
    /// <summary>JPEG 2000.</summary>
    Jp2,
    /// <summary>JPEG XL.</summary>
    Jxl,
    /// <summary>Minolta MRW.</summary>
    Mrw,
    /// <summary>Nikon NEF (TIFF-derived RAW).</summary>
    Nef,
    /// <summary>Olympus ORF (TIFF-derived RAW).</summary>
    Orf,
    /// <summary>Pentax PEF (TIFF-derived RAW).</summary>
    Pef,
    /// <summary>Photoshop PGF.</summary>
    Pgf,
    /// <summary>Photoshop PSD/PSB.</summary>
    Psd,
    /// <summary>Fujifilm RAF.</summary>
    Raf,
    /// <summary>Panasonic RW2 (TIFF-derived RAW).</summary>
    Rw2,
    /// <summary>Sony SR2 (TIFF-derived RAW).</summary>
    Sr2,
    /// <summary>Samsung SRW (TIFF-derived RAW).</summary>
    Srw,
    /// <summary>Targa TGA.</summary>
    Tga,
    /// <summary>XMP packet or .xmp sidecar (not a pixel container).</summary>
    XmpSidecar
}

/// <summary>Metadata family granularity used for capability rows.</summary>
public enum MetadataFamily
{
    /// <summary>EXIF (including GPS/Interop sub-IFDs).</summary>
    Exif = 0,
    /// <summary>IPTC Information Interchange Model (IIM).</summary>
    IptcIim,
    /// <summary>XMP packet (RDF) representation.</summary>
    Xmp,
    /// <summary>ICC colour profile: returned as its bytes by <c>IccProfiles.Read</c> (operation <see cref="MetadataOperation.Extract"/>) and written by <c>SaveIccProfile</c> of the JPEG, PNG, WebP, TIFF-based and BMFF documents; not interpreted.</summary>
    Icc,
    /// <summary>Free-text comment (a JPEG COM segment, a PNG text chunk with the keyword Comment): listed by <c>Comments.Read</c>; written by <c>SaveComment</c> of the JPEG and PNG documents.</summary>
    Comment,
    /// <summary>Embedded thumbnail; no general public API in this build.</summary>
    Thumbnail,
    /// <summary>Embedded preview; no general public API in this build.</summary>
    Preview
}

/// <summary>User-visible operation classes; mirrors the E01 ledger axes.</summary>
public enum MetadataOperation
{
    /// <summary>Read/inspect only; no modification.</summary>
    Read = 0,
    /// <summary>Add metadata that was absent.</summary>
    Insert,
    /// <summary>Replace existing metadata values.</summary>
    Replace,
    /// <summary>Remove metadata (per item where applicable, or whole family via the approved whole-removal APIs).</summary>
    Remove,
    /// <summary>Return an embedded resource as its bytes, without interpreting it. Rows exist for the ICC profile (<c>IccProfiles.Read</c>); previews and thumbnails are extracted by <c>Previews</c> and have no rows.</summary>
    Extract
}

/// <summary>Fail-closed support status for one capability row.</summary>
public enum CapabilityStatus
{
    /// <summary>Not available; this is the default and the answer for absent rows.</summary>
    NotSupported = 0,
    /// <summary>Implemented for a restricted subset; see the row note and the shipped contracts.</summary>
    Partial,
    /// <summary>Implemented for the documented family within finite limits; unsupported structures are refused.</summary>
    Supported
}

/// <summary>Immutable description of one (container, family, operation) row.
/// Note is a bounded, compile-time constant explaining partiality or scope;
/// null when nothing needs qualifying.</summary>
public readonly record struct CapabilityDescriptor(
    MetadataContainer Container,
    MetadataFamily Family,
    MetadataOperation Operation,
    CapabilityStatus Status,
    string? Note);

/// <summary>Model-driven capability table. Static, immutable, deterministic;
/// no reflection probing, no machine-dependent behavior, no I/O. A row is
/// Supported/Partial only with test evidence in this repository; anything
/// else is NotSupported. Missing rows are NotSupported by contract.
/// Rows are declared in stable sorted (Container, Family, Operation) order.</summary>
public static class MetadataCapabilities
{
    private const int MaxNoteLength = 240;

    // Data, not code: mirror of shipped, test-evidenced capability. Change only
    // together with the tests that pin it, in the same reviewed unit.
    private static readonly CapabilityDescriptor[] manifest =
    [
        // JPEG: every frame type and scan count; classic II/MM TIFF/EXIF.
        new(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "Classic TIFF/EXIF within any JPEG frame type or scan count; unsupported layouts are refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within one APP1 segment; no thumbnail directory is created."),
        new(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within one APP1 segment; no thumbnail directory is created."),
        new(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, a directory dropped with its last tag; whole-EXIF removal is also supported."),
        new(MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM within the approved IPTC usage contract; declared or UTF-8 encoding; finite packet limits enforced."),
        new(MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Photoshop APP13 resource 0x0404; every IIM 4.2 dataset of records 1 and 2, validated, or raw bytes; other records kept verbatim; UTF-8 declared when text needs it; digest 0x0425 never added."),
        new(MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Resource 0x0404 in place, every other resource byte for byte; occurrences appended, inserted, removed or replaced; digest 0x0425 left stale unless IptcWriteOptions.UpdateDigest; mixed encodings refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset and per-occurrence removal in records 1 and 2; whole-IPTC removal takes resource 0x0404 and its digest 0x0425 out."),
        new(MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Every XMP form within finite packet limits (UTF-8); Extended XMP is read as one packet with its standard packet; a packet that cannot be rewritten without loss is listed and kept as raw bytes."),
        new(MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Any property by path: simple values, arrays, language alternatives, structures and qualifiers in any namespace; a packet over 65,504 bytes is written as Extended XMP; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Any property by path: simple values, arrays, language alternatives, structures and qualifiers in any namespace; a packet over 65,504 bytes is written as Extended XMP; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; whole-XMP removal is also supported."),
        new(MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Supported, "JpegMetadataDocument.SaveIccProfile: APP2 ICC_PROFILE chunks of at most 65,519 bytes directly after the leading APP0 and APP1 segments; at most 255 chunks; a profile that fails the header check is refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Supported, "JpegMetadataDocument.SaveIccProfile: every APP2 ICC_PROFILE segment before the first scan is taken out, a broken set too, and the chunks of the new profile written; every other byte is copied."),
        new(MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Supported, "JpegMetadataDocument.SaveIccProfile with null: every APP2 ICC_PROFILE segment before the first scan is taken out; every other byte is copied."),
        new(MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the APP2 ICC_PROFILE chunks before the first scan, joined by their numbers; the bytes the profile declares, not interpreted; a missing, repeated or miscounted chunk is refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Comment, MetadataOperation.Read, CapabilityStatus.Supported, "Comments.Read: every COM segment before the first scan, in file order; UTF-8 when well-formed, ISO-8859-1 otherwise; ending zero bytes dropped."),
        new(MetadataContainer.Jpeg, MetadataFamily.Comment, MetadataOperation.Insert, CapabilityStatus.Supported, "JpegMetadataDocument.SaveComment: one COM segment, UTF-8 of at most 65,533 bytes, directly before the first segment that is not an application segment."),
        new(MetadataContainer.Jpeg, MetadataFamily.Comment, MetadataOperation.Replace, CapabilityStatus.Supported, "JpegMetadataDocument.SaveComment: every COM segment before the first scan is taken out and one written where the first stood; every other byte is copied."),
        new(MetadataContainer.Jpeg, MetadataFamily.Comment, MetadataOperation.Remove, CapabilityStatus.Supported, "JpegMetadataDocument.SaveComment with null: every COM segment before the first scan is taken out; every other byte is copied."),
        // PNG: static images only; CRC/chunk policy enforced per the approved contract.
        new(MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "eXIf chunk in static PNG within the approved chunk policy; unsupported layouts are refused."),
        new(MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "eXIf in static PNG; any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within 65,527 bytes; no thumbnail directory is created."),
        new(MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "eXIf in static PNG; any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within 65,527 bytes; no thumbnail directory is created."),
        new(MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "eXIf in static PNG; per-tag removal of any editable tag, a directory dropped with its last tag; whole-EXIF removal is also supported."),
        new(MetadataContainer.Png, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in the Raw profile type iptc text chunk (tEXt or zTXt), bare or inside Photoshop resources; finite packet limits enforced."),
        new(MetadataContainer.Png, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "PngMetadataDocument.SaveIptc: a zTXt chunk Raw profile type iptc before the image data, Photoshop resource 0x0404 as exiv2 writes it; every IIM 4.2 dataset of records 1 and 2."),
        new(MetadataContainer.Png, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "The Raw profile type iptc chunk replaced where it stands, keeping its chunk type and profile form; other resources byte for byte; digest 0x0425 left stale unless IptcWriteOptions.UpdateDigest."),
        new(MetadataContainer.Png, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset and per-occurrence removal; whole-IPTC removal takes the stream out, and the chunk with it when nothing else is left in the profile."),
        new(MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static PNG within the approved chunk/text policy; raw bytes retained for unmodeled constructs."),
        new(MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static PNG; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static PNG; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static PNG; removal of any property, item, field or qualifier by path; whole-XMP removal is also supported."),
        new(MetadataContainer.Png, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Supported, "PngMetadataDocument.SaveIccProfile: an iCCP chunk (name ICC Profile, zlib) directly after IHDR; an sRGB chunk is taken out with it; a profile that fails the header check is refused."),
        new(MetadataContainer.Png, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Supported, "PngMetadataDocument.SaveIccProfile: the iCCP chunk is replaced where it stands and an sRGB chunk taken out; a file that holds exactly the profile is copied; every other chunk is copied."),
        new(MetadataContainer.Png, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Supported, "PngMetadataDocument.SaveIccProfile with null: the iCCP chunk is taken out; an sRGB chunk and every other chunk are copied."),
        new(MetadataContainer.Png, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the iCCP chunk before the image data, inflated within the budget, not interpreted; a failed checksum, a broken stream or a second chunk is refused."),
        new(MetadataContainer.Png, MetadataFamily.Comment, MetadataOperation.Read, CapabilityStatus.Partial, "Comments.Read: tEXt, zTXt and iTXt chunks whose keyword is Comment, in file order; other keywords (Description, Title) are not read as comments."),
        new(MetadataContainer.Png, MetadataFamily.Comment, MetadataOperation.Insert, CapabilityStatus.Supported, "PngMetadataDocument.SaveComment: one chunk with the keyword Comment before the image data; tEXt when the text is ISO-8859-1 and reads back as itself, else uncompressed iTXt in UTF-8."),
        new(MetadataContainer.Png, MetadataFamily.Comment, MetadataOperation.Replace, CapabilityStatus.Supported, "PngMetadataDocument.SaveComment: every tEXt, zTXt and iTXt chunk with the keyword Comment is taken out and one written where the first stood; every other chunk is copied."),
        new(MetadataContainer.Png, MetadataFamily.Comment, MetadataOperation.Remove, CapabilityStatus.Supported, "PngMetadataDocument.SaveComment with null: every tEXt, zTXt and iTXt chunk with the keyword Comment is taken out; every other chunk is copied."),
        // WebP: static images only.
        new(MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "EXIF in static (non-animated) WebP within the approved RIFF policy; unsupported layouts are refused."),
        new(MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "EXIF in static WebP; any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within 65,527 bytes; no thumbnail directory is created."),
        new(MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "EXIF in static WebP; any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within 65,527 bytes; no thumbnail directory is created."),
        new(MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "EXIF in static WebP; per-tag removal of any editable tag, a directory dropped with its last tag; whole-EXIF removal is also supported."),
        new(MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static WebP within the approved RIFF policy; raw bytes retained for unmodeled constructs."),
        new(MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static WebP; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static WebP; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static WebP; removal of any property, item, field or qualifier by path; whole-XMP removal is also supported."),
        new(MetadataContainer.WebP, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Supported, "WebPMetadataDocument.SaveIccProfile: an ICCP chunk directly after VP8X with the ICC flag set; a file in the simple layout is given a VP8X chunk; a profile that fails the header check is refused."),
        new(MetadataContainer.WebP, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Supported, "WebPMetadataDocument.SaveIccProfile: the ICCP chunk is replaced where it stands; a file that holds exactly the profile is copied; every other chunk is copied."),
        new(MetadataContainer.WebP, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Supported, "WebPMetadataDocument.SaveIccProfile with null: the ICCP chunk is taken out and the ICC flag cleared; the VP8X chunk stays; every other chunk is copied."),
        new(MetadataContainer.WebP, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the ICCP chunk, not interpreted; a second one is refused."),
        // TIFF-based files: TiffBasedMetadataDocument edits without moving image data or the MakerNote;
        // for standalone TIFF also range accounting and the older append-preserve editor.
        new(MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "Classic (non-BigTIFF) TIFF: IFD0, EXIF, GPS, Interop and thumbnail directories as raw values (TiffExif), and read-only range accounting (TiffLayout); sub-image directories are not read as entries."),
        new(MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "TiffBasedMetadataDocument: any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable), appended. TiffMetadataDocument: ten descriptive IFD0 tags via append-preserve rebuild, obsolete bytes retained."),
        new(MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "TiffBasedMetadataDocument: any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable), in place if it fits, else appended. TiffMetadataDocument: ten descriptive IFD0 tags via append-preserve rebuild."),
        new(MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        new(MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        new(MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; every IIM 4.2 dataset of records 1 and 2, validated, or raw bytes; a file that keeps its IPTC in Photoshop resources (tag 0x8649) is edited there."),
        new(MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type, or resource 0x0404 of tag 0x8649 with every other resource byte for byte; digest 0x0425 left stale unless IptcWriteOptions.UpdateDigest."),
        new(MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset and per-occurrence removal in records 1 and 2; whole-IPTC removal removes tag 0x83BB, or resource 0x0404 and its digest from tag 0x8649."),
        new(MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; any property by path, in any namespace."),
        new(MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; whole-XMP removal removes tag 700."),
        new(MetadataContainer.Tiff, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Partial, "TiffBasedMetadataDocument.SaveIccProfile: tag 0x8773 of the primary directory, as UNDEFINED, appended with a copy of the directory; no byte of the source moves; a file that cannot be appended to is refused."),
        new(MetadataContainer.Tiff, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Partial, "TiffBasedMetadataDocument.SaveIccProfile: the value of tag 0x8773 of the primary directory, of its type BYTE or UNDEFINED; written over the stored one when no longer than it, else appended; a file that holds exactly the profile is copied."),
        new(MetadataContainer.Tiff, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Partial, "TiffBasedMetadataDocument.SaveIccProfile with null: tag 0x8773 is taken out of the primary directory, which is rewritten where it is; the bytes of the profile stay in the file, no longer addressed."),
        new(MetadataContainer.Tiff, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Partial, "IccProfiles.Read: tag 0x8773 of the first directory only, of type BYTE or UNDEFINED, not interpreted; a value outside the file is refused."),
        // DNG and the TIFF-based raw formats, in the order of the enumeration. A raw format has a row
        // only where a layout of its kind is tested; RW2 is edited in place only.
        new(MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        new(MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets; refused when the file cannot be appended to."),
        new(MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); written in place when the value fits, appended otherwise; image data and the MakerNote keep their offsets."),
        new(MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        new(MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        new(MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; every IIM 4.2 dataset of records 1 and 2, validated, or raw bytes; a file that keeps its IPTC in Photoshop resources (tag 0x8649) is edited there."),
        new(MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type, or resource 0x0404 of tag 0x8649 with every other resource byte for byte; digest 0x0425 left stale unless IptcWriteOptions.UpdateDigest."),
        new(MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset and per-occurrence removal in records 1 and 2; whole-IPTC removal removes tag 0x83BB, or resource 0x0404 and its digest from tag 0x8649."),
        new(MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; any property by path, in any namespace."),
        new(MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; whole-XMP removal removes tag 700."),
        new(MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        new(MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        new(MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        new(MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; every IIM 4.2 dataset of records 1 and 2, validated, or raw bytes; a file that keeps its IPTC in Photoshop resources (tag 0x8649) is edited there."),
        new(MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type, or resource 0x0404 of tag 0x8649 with every other resource byte for byte; digest 0x0425 left stale unless IptcWriteOptions.UpdateDigest."),
        new(MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset and per-occurrence removal in records 1 and 2; whole-IPTC removal removes tag 0x83BB, or resource 0x0404 and its digest from tag 0x8649."),
        new(MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; any property by path, in any namespace."),
        new(MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; whole-XMP removal removes tag 700."),
        // HEIC, HEIF and AVIF: BmffMetadataDocument edits the Exif and XMP items without moving the data of
        // any item; each has rows because a layout of its brand is tested.
        new(MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "The Exif item of the top-level meta box, with a recognised MakerNote, as raw values; a block longer than a JPEG segment holds is read within the budgets. An item built from other items or stored in another file is refused."),
        new(MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); a file without an Exif item gains one for the primary item. New data goes into a media data box of its own; no item data moves."),
        new(MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when the item keeps its length, in a new media data box otherwise. No other item moves."),
        new(MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; removal of the whole Exif item is refused."),
        new(MetadataContainer.Avif, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The application/rdf+xml item of the top-level meta box; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Avif, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "A file without an XMP item gains one (mime, application/rdf+xml) that describes the primary item, appended in a media data box of its own; any property by path, in any namespace."),
        new(MetadataContainer.Avif, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "The packet of the XMP item: in place when it keeps its length, appended in a new media data box otherwise; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Avif, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; removal of the whole XMP item is refused."),
        new(MetadataContainer.Avif, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: a colr property of kind prof after the last one, given to the primary item alone; the meta box grows as the last box or a new one is appended; no item moves; refused without a primary item or iprp box."),
        new(MetadataContainer.Avif, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: the colr property of kind prof or rICC of the primary item, replaced for every item that shares it; inside the iprp box when it fits, else the meta box changes its length; the same profile is copied."),
        new(MetadataContainer.Avif, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile with null: the property becomes a free box of eight bytes that no item is given; the item properties box is rewritten in the bytes it has; nclx colour information stays."),
        new(MetadataContainer.Avif, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the colr property of kind prof or rICC that ipma gives the primary item, not interpreted; nclx colour information is not a profile; two profiles are refused."),
        new(MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        new(MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        new(MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        new(MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; every IIM 4.2 dataset of records 1 and 2, validated, or raw bytes; a file that keeps its IPTC in Photoshop resources (tag 0x8649) is edited there."),
        new(MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type, or resource 0x0404 of tag 0x8649 with every other resource byte for byte; digest 0x0425 left stale unless IptcWriteOptions.UpdateDigest."),
        new(MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset and per-occurrence removal in records 1 and 2; whole-IPTC removal removes tag 0x83BB, or resource 0x0404 and its digest from tag 0x8649."),
        new(MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; any property by path, in any namespace."),
        new(MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; whole-XMP removal removes tag 700."),
        // CR3 has no document type either: Cr3Metadata reads, and nothing writes a CR3 file.
        new(MetadataContainer.Cr3, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "Cr3Metadata.Read: the CMT1, CMT2 and CMT4 boxes of the Canon box as the IFD0, EXIF and GPS directories of one block, CMT3 as the Canon MakerNote, as raw values; boxes that differ in byte order are refused. Nothing is written."),
        new(MetadataContainer.Cr3, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Cr3Metadata.Read: the packet of the XMP box at the top level of the file; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss. Nothing is written."),
        // CRW has no EXIF block and no document type: CrwMetadata gives the records of the heap as tags.
        new(MetadataContainer.Crw, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "CrwMetadata.Read: the records of the CIFF heap that Exiv2 0.28.9 maps to EXIF tags, as those tags, the Canon ones as a Canon MakerNote; the preview and the thumbnail are reported by where they lie. Nothing is written."),
        new(MetadataContainer.Crw, MetadataFamily.Comment, MetadataOperation.Read, CapabilityStatus.Supported, "CrwMetadata.Read: the user comment record, without the zero bytes that end it. Nothing is written."),
        // EXV, JPEG 2000 and PSD have no document type: what the static readers return is all that is claimed.
        new(MetadataContainer.Exv, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the APP2 ICC_PROFILE chunks, joined by their numbers as in a JPEG; the bytes the profile declares, not interpreted."),
        new(MetadataContainer.Exv, MetadataFamily.Comment, MetadataOperation.Read, CapabilityStatus.Supported, "Comments.Read: every COM segment, in file order, as in a JPEG. Nothing is written."),
        // HEIC and HEIF: as AVIF above.
        new(MetadataContainer.Heic, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "The Exif item of the top-level meta box, with a recognised MakerNote, as raw values; a block longer than a JPEG segment holds is read within the budgets. An item built from other items or stored in another file is refused."),
        new(MetadataContainer.Heic, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); a file without an Exif item gains one for the primary item. New data goes into a media data box of its own; no item data moves."),
        new(MetadataContainer.Heic, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when the item keeps its length, in a new media data box otherwise. No other item moves."),
        new(MetadataContainer.Heic, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; removal of the whole Exif item is refused."),
        new(MetadataContainer.Heic, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The application/rdf+xml item of the top-level meta box; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Heic, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "A file without an XMP item gains one (mime, application/rdf+xml) that describes the primary item, appended in a media data box of its own; any property by path, in any namespace."),
        new(MetadataContainer.Heic, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "The packet of the XMP item: in place when it keeps its length, appended in a new media data box otherwise; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Heic, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; removal of the whole XMP item is refused."),
        new(MetadataContainer.Heic, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: a colr property of kind prof after the last one, given to the primary item alone; the meta box grows as the last box or a new one is appended; no item moves; refused without a primary item or iprp box."),
        new(MetadataContainer.Heic, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: the colr property of kind prof or rICC of the primary item, replaced for every item that shares it; inside the iprp box when it fits, else the meta box changes its length; the same profile is copied."),
        new(MetadataContainer.Heic, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile with null: the property becomes a free box of eight bytes that no item is given; the item properties box is rewritten in the bytes it has; nclx colour information stays."),
        new(MetadataContainer.Heic, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the colr property of kind prof or rICC that ipma gives the primary item, not interpreted; nclx colour information is not a profile; two profiles are refused."),
        new(MetadataContainer.Heif, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "The Exif item of the top-level meta box, with a recognised MakerNote, as raw values; a block longer than a JPEG segment holds is read within the budgets. An item built from other items or stored in another file is refused."),
        new(MetadataContainer.Heif, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); a file without an Exif item gains one for the primary item. New data goes into a media data box of its own; no item data moves."),
        new(MetadataContainer.Heif, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when the item keeps its length, in a new media data box otherwise. No other item moves."),
        new(MetadataContainer.Heif, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; removal of the whole Exif item is refused."),
        new(MetadataContainer.Heif, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The application/rdf+xml item of the top-level meta box; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Heif, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "A file without an XMP item gains one (mime, application/rdf+xml) that describes the primary item, appended in a media data box of its own; any property by path, in any namespace."),
        new(MetadataContainer.Heif, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "The packet of the XMP item: in place when it keeps its length, appended in a new media data box otherwise; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Heif, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; removal of the whole XMP item is refused."),
        new(MetadataContainer.Heif, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: a colr property of kind prof after the last one, given to the primary item alone; the meta box grows as the last box or a new one is appended; no item moves; refused without a primary item or iprp box."),
        new(MetadataContainer.Heif, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: the colr property of kind prof or rICC of the primary item, replaced for every item that shares it; inside the iprp box when it fits, else the meta box changes its length; the same profile is copied."),
        new(MetadataContainer.Heif, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile with null: the property becomes a free box of eight bytes that no item is given; the item properties box is rewritten in the bytes it has; nclx colour information stays."),
        new(MetadataContainer.Heif, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the colr property of kind prof or rICC that ipma gives the primary item, not interpreted; nclx colour information is not a profile; two profiles are refused."),
        new(MetadataContainer.Jp2, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the first colr box of the jp2h header whose method is 2 or 3, not interpreted; a colour space named by number is not a profile."),
        // JPEG XL has no document type either: JxlMetadata reads, and nothing writes a JPEG XL file. A bare
        // codestream holds no metadata; a jumb box is reported by JxlMetadata.HasJumbf and not read.
        new(MetadataContainer.Jxl, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "JxlMetadata.Read: the block of the Exif box, plain or Brotli-compressed in a brob box, with a recognised MakerNote, as raw values; read within the budgets, counted after decompression. Nothing is written."),
        new(MetadataContainer.Jxl, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "JxlMetadata.Read: the packet of the xml box, plain or Brotli-compressed in a brob box; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss. Nothing is written."),
        // MRW has no document type: MrwMetadata reads the TIFF structure of the TTW block.
        new(MetadataContainer.Mrw, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "MrwMetadata.Read: the directories of the TTW block and a recognised MakerNote, as raw values, read as a TIFF file is; tested on authored files, not on camera files. Nothing is written."),
        new(MetadataContainer.Mrw, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Partial, "MrwMetadata.Read: IIM in tag 0x83BB of the TTW block, or in the Photoshop resources of tag 0x8649; tested on authored files, not on camera files. Nothing is written."),
        new(MetadataContainer.Mrw, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Partial, "MrwMetadata.Read: the packet of tag 700 of the primary directory of the TTW block; tested on authored files, not on camera files. Nothing is written."),
        new(MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        new(MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        new(MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        new(MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; every IIM 4.2 dataset of records 1 and 2, validated, or raw bytes; a file that keeps its IPTC in Photoshop resources (tag 0x8649) is edited there."),
        new(MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type, or resource 0x0404 of tag 0x8649 with every other resource byte for byte; digest 0x0425 left stale unless IptcWriteOptions.UpdateDigest."),
        new(MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset and per-occurrence removal in records 1 and 2; whole-IPTC removal removes tag 0x83BB, or resource 0x0404 and its digest from tag 0x8649."),
        new(MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; any property by path, in any namespace."),
        new(MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; whole-XMP removal removes tag 700."),
        new(MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        new(MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        new(MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        new(MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; every IIM 4.2 dataset of records 1 and 2, validated, or raw bytes; a file that keeps its IPTC in Photoshop resources (tag 0x8649) is edited there."),
        new(MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type, or resource 0x0404 of tag 0x8649 with every other resource byte for byte; digest 0x0425 left stale unless IptcWriteOptions.UpdateDigest."),
        new(MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset and per-occurrence removal in records 1 and 2; whole-IPTC removal removes tag 0x83BB, or resource 0x0404 and its digest from tag 0x8649."),
        new(MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; any property by path, in any namespace."),
        new(MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; whole-XMP removal removes tag 700."),
        new(MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        new(MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        new(MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        new(MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; every IIM 4.2 dataset of records 1 and 2, validated, or raw bytes; a file that keeps its IPTC in Photoshop resources (tag 0x8649) is edited there."),
        new(MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type, or resource 0x0404 of tag 0x8649 with every other resource byte for byte; digest 0x0425 left stale unless IptcWriteOptions.UpdateDigest."),
        new(MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset and per-occurrence removal in records 1 and 2; whole-IPTC removal removes tag 0x83BB, or resource 0x0404 and its digest from tag 0x8649."),
        new(MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; every XMP form within finite packet limits; raw bytes retained for a packet that cannot be rewritten without loss."),
        new(MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; any property by path, in any namespace."),
        new(MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; any property by path; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; whole-XMP removal removes tag 700."),
        new(MetadataContainer.Psd, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: image resource 0x040F, not interpreted; a second one is refused."),
        // RAF has no document type: RafMetadata reads the embedded JPEG file and the TIFF structure.
        new(MetadataContainer.Raf, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "RafMetadata.Read: the EXIF block of the embedded JPEG file, and the tags only the primary directory of the TIFF structure has; its Fujifilm directory (tag 0xF000) is not read; tested on authored files. Nothing is written."),
        new(MetadataContainer.Raf, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Partial, "RafMetadata.Read: the IPTC datasets of the embedded JPEG file; tested on authored files, not on camera files. Nothing is written."),
        new(MetadataContainer.Raf, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Partial, "RafMetadata.Read: the packet of the embedded JPEG file; tested on authored files, not on camera files. Nothing is written."),
        new(MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        new(MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "In place only: a value of any editable tag that fits where the old one is. The raw data runs to the end of the file, so nothing is appended and no tag can be added. Tested on authored layouts, not on camera files."),
        new(MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place; whole-EXIF removal is refused."),
        // XMP packet/sidecar: no container framing.
        new(MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Load/save of XMP packets and .xmp sidecars; every XMP form within finite packet limits."),
        new(MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Any property by path: simple values, arrays, language alternatives, structures and qualifiers in any namespace; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Any property by path: simple values, arrays, language alternatives, structures and qualifiers in any namespace; a packet that cannot be rewritten without loss is refused."),
        new(MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Removal of any property, item, field or qualifier by path; whole-XMP removal is also supported."),
    ];

    /// <summary>All declared (non-NotSupported) capability rows in stable sorted
    /// (Container, Family, Operation) order. Absent rows are NotSupported.</summary>
    public static IReadOnlyList<CapabilityDescriptor> All { get; } = Array.AsReadOnly(manifest);

    private static readonly Dictionary<(MetadataContainer Container, MetadataFamily Family, MetadataOperation Operation), CapabilityDescriptor> index = CreateIndex();

    /// <summary>Fail-closed lookup; false when the row is absent. Undefined enum
    /// values are reported as not supported rather than rejected.</summary>
    public static bool TryGet(MetadataContainer container, MetadataFamily family,
        MetadataOperation operation, out CapabilityDescriptor descriptor)
    {
        if (index.TryGetValue((container, family, operation), out CapabilityDescriptor found))
        {
            descriptor = found;
            return true;
        }
        descriptor = default;
        return false;
    }

    /// <summary>Convenience; returns NotSupported when the row is absent. Undefined
    /// enum values are reported as NotSupported rather than rejected.</summary>
    public static CapabilityStatus GetStatus(MetadataContainer container,
        MetadataFamily family, MetadataOperation operation) =>
        index.TryGetValue((container, family, operation), out CapabilityDescriptor descriptor)
            ? descriptor.Status
            : CapabilityStatus.NotSupported;

    private static Dictionary<(MetadataContainer Container, MetadataFamily Family, MetadataOperation Operation), CapabilityDescriptor> CreateIndex()
    {
        var map = new Dictionary<(MetadataContainer Container, MetadataFamily Family, MetadataOperation Operation), CapabilityDescriptor>(manifest.Length);
        for (int i = 0; i < manifest.Length; i++)
        {
            CapabilityDescriptor row = manifest[i];
            if (row.Status == CapabilityStatus.NotSupported)
                throw new InvalidOperationException("NotSupported rows must be omitted from the manifest; absence already means NotSupported.");
            if (!Enum.IsDefined(row.Container) || !Enum.IsDefined(row.Family) || !Enum.IsDefined(row.Operation))
                throw new InvalidOperationException("The capability manifest contains an undefined enum value.");
            if (row.Note is { Length: > MaxNoteLength })
                throw new InvalidOperationException("Capability notes must be at most 240 characters.");
            if (i > 0 && Compare(manifest[i - 1], row) >= 0)
                throw new InvalidOperationException("The capability manifest must be strictly ordered by (Container, Family, Operation).");
            map.Add((row.Container, row.Family, row.Operation), row);
        }
        return map;
    }

    private static int Compare(in CapabilityDescriptor left, in CapabilityDescriptor right)
    {
        int result = left.Container.CompareTo(right.Container);
        if (result != 0) return result;
        result = left.Family.CompareTo(right.Family);
        if (result != 0) return result;
        return left.Operation.CompareTo(right.Operation);
    }
}
