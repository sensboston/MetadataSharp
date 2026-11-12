using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Capabilities;

/// <summary>
/// Pins the shipped, test-evidenced capability table. The expectation list below
/// is authored independently of the manifest and must be updated only together
/// with the reviewed contract change that justifies it.
/// </summary>
public sealed class MetadataCapabilitiesTests
{
    private static readonly (MetadataContainer Container, MetadataFamily Family, MetadataOperation Operation, CapabilityStatus Status, string Note)[] Expected =
    [
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "Classic TIFF/EXIF within any JPEG frame type or scan count; unsupported layouts are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within one APP1 segment; no thumbnail directory is created."),
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within one APP1 segment; no thumbnail directory is created."),
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, a directory dropped with its last tag; whole-EXIF removal is also supported."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM within the approved IPTC usage contract; declared or UTF-8 encoding; finite packet limits enforced."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved IPTC dataset usage subset; unsupported records are refused, not silently rewritten."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved IPTC dataset usage subset; unsupported records are refused, not silently rewritten."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Bounded restricted XMP profile (UTF-8, finite packet limits); raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal is also supported."),
        (MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Supported, "JpegMetadataDocument.SaveIccProfile: APP2 ICC_PROFILE chunks of at most 65,519 bytes directly after the leading APP0 and APP1 segments; at most 255 chunks; a profile that fails the header check is refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Supported, "JpegMetadataDocument.SaveIccProfile: every APP2 ICC_PROFILE segment before the first scan is taken out, a broken set too, and the chunks of the new profile written; every other byte is copied."),
        (MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Supported, "JpegMetadataDocument.SaveIccProfile with null: every APP2 ICC_PROFILE segment before the first scan is taken out; every other byte is copied."),
        (MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the APP2 ICC_PROFILE chunks before the first scan, joined by their numbers; the bytes the profile declares, not interpreted; a missing, repeated or miscounted chunk is refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Comment, MetadataOperation.Read, CapabilityStatus.Supported, "Comments.Read: every COM segment before the first scan, in file order; UTF-8 when well-formed, ISO-8859-1 otherwise; ending zero bytes dropped."),
        (MetadataContainer.Jpeg, MetadataFamily.Comment, MetadataOperation.Insert, CapabilityStatus.Supported, "JpegMetadataDocument.SaveComment: one COM segment, UTF-8 of at most 65,533 bytes, directly before the first segment that is not an application segment."),
        (MetadataContainer.Jpeg, MetadataFamily.Comment, MetadataOperation.Replace, CapabilityStatus.Supported, "JpegMetadataDocument.SaveComment: every COM segment before the first scan is taken out and one written where the first stood; every other byte is copied."),
        (MetadataContainer.Jpeg, MetadataFamily.Comment, MetadataOperation.Remove, CapabilityStatus.Supported, "JpegMetadataDocument.SaveComment with null: every COM segment before the first scan is taken out; every other byte is copied."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "eXIf chunk in static PNG within the approved chunk policy; unsupported layouts are refused."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "eXIf in static PNG; any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within 65,527 bytes; no thumbnail directory is created."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "eXIf in static PNG; any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within 65,527 bytes; no thumbnail directory is created."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "eXIf in static PNG; per-tag removal of any editable tag, a directory dropped with its last tag; whole-EXIF removal is also supported."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static PNG within the approved chunk/text policy; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static PNG; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static PNG; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static PNG; per-property removal within the restricted profile; whole-XMP removal is also supported."),
        (MetadataContainer.Png, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Supported, "PngMetadataDocument.SaveIccProfile: an iCCP chunk (name ICC Profile, zlib) directly after IHDR; an sRGB chunk is taken out with it; a profile that fails the header check is refused."),
        (MetadataContainer.Png, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Supported, "PngMetadataDocument.SaveIccProfile: the iCCP chunk is replaced where it stands and an sRGB chunk taken out; a file that holds exactly the profile is copied; every other chunk is copied."),
        (MetadataContainer.Png, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Supported, "PngMetadataDocument.SaveIccProfile with null: the iCCP chunk is taken out; an sRGB chunk and every other chunk are copied."),
        (MetadataContainer.Png, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the iCCP chunk before the image data, inflated within the budget, not interpreted; a failed checksum, a broken stream or a second chunk is refused."),
        (MetadataContainer.Png, MetadataFamily.Comment, MetadataOperation.Read, CapabilityStatus.Partial, "Comments.Read: tEXt, zTXt and iTXt chunks whose keyword is Comment, in file order; other keywords (Description, Title) are not read as comments."),
        (MetadataContainer.Png, MetadataFamily.Comment, MetadataOperation.Insert, CapabilityStatus.Supported, "PngMetadataDocument.SaveComment: one chunk with the keyword Comment before the image data; tEXt when the text is ISO-8859-1 and reads back as itself, else uncompressed iTXt in UTF-8."),
        (MetadataContainer.Png, MetadataFamily.Comment, MetadataOperation.Replace, CapabilityStatus.Supported, "PngMetadataDocument.SaveComment: every tEXt, zTXt and iTXt chunk with the keyword Comment is taken out and one written where the first stood; every other chunk is copied."),
        (MetadataContainer.Png, MetadataFamily.Comment, MetadataOperation.Remove, CapabilityStatus.Supported, "PngMetadataDocument.SaveComment with null: every tEXt, zTXt and iTXt chunk with the keyword Comment is taken out; every other chunk is copied."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "EXIF in static (non-animated) WebP within the approved RIFF policy; unsupported layouts are refused."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "EXIF in static WebP; any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within 65,527 bytes; no thumbnail directory is created."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "EXIF in static WebP; any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); an existing block is edited where it lies, within 65,527 bytes; no thumbnail directory is created."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "EXIF in static WebP; per-tag removal of any editable tag, a directory dropped with its last tag; whole-EXIF removal is also supported."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static WebP within the approved RIFF policy; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static WebP; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static WebP; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static WebP; per-property removal within the restricted profile; whole-XMP removal is also supported."),
        (MetadataContainer.WebP, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Supported, "WebPMetadataDocument.SaveIccProfile: an ICCP chunk directly after VP8X with the ICC flag set; a file in the simple layout is given a VP8X chunk; a profile that fails the header check is refused."),
        (MetadataContainer.WebP, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Supported, "WebPMetadataDocument.SaveIccProfile: the ICCP chunk is replaced where it stands; a file that holds exactly the profile is copied; every other chunk is copied."),
        (MetadataContainer.WebP, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Supported, "WebPMetadataDocument.SaveIccProfile with null: the ICCP chunk is taken out and the ICC flag cleared; the VP8X chunk stays; every other chunk is copied."),
        (MetadataContainer.WebP, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the ICCP chunk, not interpreted; a second one is refused."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "Classic (non-BigTIFF) TIFF: IFD0, EXIF, GPS, Interop and thumbnail directories as raw values (TiffExif), and read-only range accounting (TiffLayout); sub-image directories are not read as entries."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "TiffBasedMetadataDocument: any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable), appended. TiffMetadataDocument: ten descriptive IFD0 tags via append-preserve rebuild, obsolete bytes retained."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "TiffBasedMetadataDocument: any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable), in place if it fits, else appended. TiffMetadataDocument: ten descriptive IFD0 tags via append-preserve rebuild."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Tiff, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Partial, "TiffBasedMetadataDocument.SaveIccProfile: tag 0x8773 of the primary directory, as UNDEFINED, appended with a copy of the directory; no byte of the source moves; a file that cannot be appended to is refused."),
        (MetadataContainer.Tiff, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Partial, "TiffBasedMetadataDocument.SaveIccProfile: the value of tag 0x8773 of the primary directory, of its type BYTE or UNDEFINED; written over the stored one when no longer than it, else appended; a file that holds exactly the profile is copied."),
        (MetadataContainer.Tiff, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Partial, "TiffBasedMetadataDocument.SaveIccProfile with null: tag 0x8773 is taken out of the primary directory, which is rewritten where it is; the bytes of the profile stay in the file, no longer addressed."),
        (MetadataContainer.Tiff, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Partial, "IccProfiles.Read: tag 0x8773 of the first directory only, of type BYTE or UNDEFINED, not interpreted; a value outside the file is refused."),
        (MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets; refused when the file cannot be appended to."),
        (MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); written in place when the value fits, appended otherwise; image data and the MakerNote keep their offsets."),
        (MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "The Exif item of the top-level meta box, with a recognised MakerNote, as raw values; a block longer than a JPEG segment holds is read within the budgets. An item built from other items or stored in another file is refused."),
        (MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); a file without an Exif item gains one for the primary item. New data goes into a media data box of its own; no item data moves."),
        (MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when the item keeps its length, in a new media data box otherwise. No other item moves."),
        (MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; removal of the whole Exif item is refused."),
        (MetadataContainer.Avif, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The application/rdf+xml item of the top-level meta box; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Avif, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "A file without an XMP item gains one (mime, application/rdf+xml) that describes the primary item, appended in a media data box of its own; bounded restricted XMP profile."),
        (MetadataContainer.Avif, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "The packet of the XMP item: in place when it keeps its length, appended in a new media data box otherwise; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Avif, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; removal of the whole XMP item is refused."),
        (MetadataContainer.Avif, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: a colr property of kind prof after the last one, given to the primary item alone; the meta box grows as the last box or a new one is appended; no item moves; refused without a primary item or iprp box."),
        (MetadataContainer.Avif, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: the colr property of kind prof or rICC of the primary item, replaced for every item that shares it; inside the iprp box when it fits, else the meta box changes its length; the same profile is copied."),
        (MetadataContainer.Avif, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile with null: the property becomes a free box of eight bytes that no item is given; the item properties box is rewritten in the bytes it has; nclx colour information stays."),
        (MetadataContainer.Avif, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the colr property of kind prof or rICC that ipma gives the primary item, not interpreted; nclx colour information is not a profile; two profiles are refused."),
        (MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Cr3, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "Cr3Metadata.Read: the CMT1, CMT2 and CMT4 boxes of the Canon box as the IFD0, EXIF and GPS directories of one block, CMT3 as the Canon MakerNote, as raw values; boxes that differ in byte order are refused. Nothing is written."),
        (MetadataContainer.Cr3, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Cr3Metadata.Read: the packet of the XMP box at the top level of the file; bounded restricted XMP profile; raw bytes retained for unmodeled constructs. Nothing is written."),
        (MetadataContainer.Exv, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the APP2 ICC_PROFILE chunks, joined by their numbers as in a JPEG; the bytes the profile declares, not interpreted."),
        (MetadataContainer.Exv, MetadataFamily.Comment, MetadataOperation.Read, CapabilityStatus.Supported, "Comments.Read: every COM segment, in file order, as in a JPEG. Nothing is written."),
        (MetadataContainer.Heic, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "The Exif item of the top-level meta box, with a recognised MakerNote, as raw values; a block longer than a JPEG segment holds is read within the budgets. An item built from other items or stored in another file is refused."),
        (MetadataContainer.Heic, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); a file without an Exif item gains one for the primary item. New data goes into a media data box of its own; no item data moves."),
        (MetadataContainer.Heic, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when the item keeps its length, in a new media data box otherwise. No other item moves."),
        (MetadataContainer.Heic, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; removal of the whole Exif item is refused."),
        (MetadataContainer.Heic, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The application/rdf+xml item of the top-level meta box; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Heic, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "A file without an XMP item gains one (mime, application/rdf+xml) that describes the primary item, appended in a media data box of its own; bounded restricted XMP profile."),
        (MetadataContainer.Heic, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "The packet of the XMP item: in place when it keeps its length, appended in a new media data box otherwise; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Heic, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; removal of the whole XMP item is refused."),
        (MetadataContainer.Heic, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: a colr property of kind prof after the last one, given to the primary item alone; the meta box grows as the last box or a new one is appended; no item moves; refused without a primary item or iprp box."),
        (MetadataContainer.Heic, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: the colr property of kind prof or rICC of the primary item, replaced for every item that shares it; inside the iprp box when it fits, else the meta box changes its length; the same profile is copied."),
        (MetadataContainer.Heic, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile with null: the property becomes a free box of eight bytes that no item is given; the item properties box is rewritten in the bytes it has; nclx colour information stays."),
        (MetadataContainer.Heic, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the colr property of kind prof or rICC that ipma gives the primary item, not interpreted; nclx colour information is not a profile; two profiles are refused."),
        (MetadataContainer.Heif, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "The Exif item of the top-level meta box, with a recognised MakerNote, as raw values; a block longer than a JPEG segment holds is read within the budgets. An item built from other items or stored in another file is refused."),
        (MetadataContainer.Heif, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); a file without an Exif item gains one for the primary item. New data goes into a media data box of its own; no item data moves."),
        (MetadataContainer.Heif, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when the item keeps its length, in a new media data box otherwise. No other item moves."),
        (MetadataContainer.Heif, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; removal of the whole Exif item is refused."),
        (MetadataContainer.Heif, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The application/rdf+xml item of the top-level meta box; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Heif, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "A file without an XMP item gains one (mime, application/rdf+xml) that describes the primary item, appended in a media data box of its own; bounded restricted XMP profile."),
        (MetadataContainer.Heif, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "The packet of the XMP item: in place when it keeps its length, appended in a new media data box otherwise; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Heif, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; removal of the whole XMP item is refused."),
        (MetadataContainer.Heif, MetadataFamily.Icc, MetadataOperation.Insert, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: a colr property of kind prof after the last one, given to the primary item alone; the meta box grows as the last box or a new one is appended; no item moves; refused without a primary item or iprp box."),
        (MetadataContainer.Heif, MetadataFamily.Icc, MetadataOperation.Replace, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile: the colr property of kind prof or rICC of the primary item, replaced for every item that shares it; inside the iprp box when it fits, else the meta box changes its length; the same profile is copied."),
        (MetadataContainer.Heif, MetadataFamily.Icc, MetadataOperation.Remove, CapabilityStatus.Partial, "BmffMetadataDocument.SaveIccProfile with null: the property becomes a free box of eight bytes that no item is given; the item properties box is rewritten in the bytes it has; nclx colour information stays."),
        (MetadataContainer.Heif, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the colr property of kind prof or rICC that ipma gives the primary item, not interpreted; nclx colour information is not a profile; two profiles are refused."),
        (MetadataContainer.Jp2, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: the first colr box of the jp2h header whose method is 2 or 3, not interpreted; a colour space named by number is not a profile."),
        (MetadataContainer.Jxl, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "JxlMetadata.Read: the block of the Exif box, plain or Brotli-compressed in a brob box, with a recognised MakerNote, as raw values; read within the budgets, counted after decompression. Nothing is written."),
        (MetadataContainer.Jxl, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "JxlMetadata.Read: the packet of the xml box, plain or Brotli-compressed in a brob box; bounded restricted XMP profile; raw bytes retained for unmodeled constructs. Nothing is written."),
        (MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); new data is appended, image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Any tag in any field type except pointers, image-data addresses and the MakerNote (TagNotEditable); in place when it fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place and dropped with its last tag; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Psd, MetadataFamily.Icc, MetadataOperation.Extract, CapabilityStatus.Supported, "IccProfiles.Read: image resource 0x040F, not interpreted; a second one is refused."),
        (MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "In place only: a value of any editable tag that fits where the old one is. The raw data runs to the end of the file, so nothing is appended and no tag can be added. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal of any editable tag, the directory rewritten in place; whole-EXIF removal is refused."),
        (MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Load/save of XMP packets and .xmp sidecars within the bounded restricted profile."),
        (MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal is also supported."),
    ];

    [Fact]
    public void All_ContainsExactlyThePinnedEvidencedRows()
    {
        Assert.Equal(Expected.Length, MetadataCapabilities.All.Count);
        for (int i = 0; i < Expected.Length; i++)
        {
            (MetadataContainer container, MetadataFamily family, MetadataOperation operation, CapabilityStatus status, string note) = Expected[i];
            Assert.Equal(new CapabilityDescriptor(container, family, operation, status, note), MetadataCapabilities.All[i]);
        }
    }

    [Fact]
    public void All_IsStableStrictlyOrderedAndNonTrivial()
    {
        Assert.Same(MetadataCapabilities.All, MetadataCapabilities.All);
        Assert.NotEmpty(MetadataCapabilities.All);
        for (int i = 1; i < MetadataCapabilities.All.Count; i++)
        {
            CapabilityDescriptor previous = MetadataCapabilities.All[i - 1];
            CapabilityDescriptor current = MetadataCapabilities.All[i];
            int comparison = previous.Container.CompareTo(current.Container);
            if (comparison == 0) comparison = previous.Family.CompareTo(current.Family);
            if (comparison == 0) comparison = previous.Operation.CompareTo(current.Operation);
            Assert.True(comparison < 0, $"Rows out of order at index {i}.");
        }
    }

    [Fact]
    public void All_ContainsOnlyDefinedNonDefaultRows()
    {
        foreach (CapabilityDescriptor row in MetadataCapabilities.All)
        {
            Assert.True(Enum.IsDefined(row.Container));
            Assert.True(Enum.IsDefined(row.Family));
            Assert.True(Enum.IsDefined(row.Operation));
            Assert.NotEqual(CapabilityStatus.NotSupported, row.Status);
            Assert.True(Enum.IsDefined(row.Status));
            Assert.NotNull(row.Note);
            Assert.InRange(row.Note!.Length, 1, 240);
            Assert.DoesNotContain(row.Note, ch => char.IsControl(ch));
        }
    }

    [Fact]
    public void TryGet_ReturnsExactDescriptorForDeclaredRows()
    {
        foreach ((MetadataContainer container, MetadataFamily family, MetadataOperation operation, CapabilityStatus status, string note) in Expected)
        {
            Assert.True(MetadataCapabilities.TryGet(container, family, operation, out CapabilityDescriptor descriptor));
            Assert.Equal(new CapabilityDescriptor(container, family, operation, status, note), descriptor);
        }
    }

    [Theory]
    [InlineData(MetadataContainer.Tiff, MetadataFamily.Icc, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Jpeg, MetadataFamily.Thumbnail, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Png, MetadataFamily.IptcIim, MetadataOperation.Read)]
    [InlineData(MetadataContainer.WebP, MetadataFamily.IptcIim, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Dng, MetadataFamily.Preview, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Insert)]
    [InlineData(MetadataContainer.Rw2, MetadataFamily.Xmp, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Sr2, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Srw, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.XmpSidecar, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Avif, MetadataFamily.IptcIim, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Heic, MetadataFamily.Preview, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Cr3, MetadataFamily.Exif, MetadataOperation.Replace)]
    [InlineData(MetadataContainer.Cr3, MetadataFamily.IptcIim, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Cr3, MetadataFamily.Preview, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Jxl, MetadataFamily.Xmp, MetadataOperation.Replace)]
    [InlineData(MetadataContainer.Jxl, MetadataFamily.IptcIim, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Jxl, MetadataFamily.Icc, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Dng, MetadataFamily.Icc, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Gif, MetadataFamily.Icc, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Dng, MetadataFamily.Icc, MetadataOperation.Insert)]
    [InlineData(MetadataContainer.Jp2, MetadataFamily.Icc, MetadataOperation.Replace)]
    [InlineData(MetadataContainer.WebP, MetadataFamily.Comment, MetadataOperation.Insert)]
    [InlineData(MetadataContainer.Exv, MetadataFamily.Comment, MetadataOperation.Remove)]
    [InlineData(MetadataContainer.WebP, MetadataFamily.Comment, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Gif, MetadataFamily.Comment, MetadataOperation.Read)]
    public void TryGet_AbsentRow_FailsClosed(MetadataContainer container, MetadataFamily family, MetadataOperation operation)
    {
        Assert.False(MetadataCapabilities.TryGet(container, family, operation, out CapabilityDescriptor descriptor));
        Assert.Equal(default, descriptor);
        Assert.Equal(CapabilityStatus.NotSupported, MetadataCapabilities.GetStatus(container, family, operation));
    }

    [Theory]
    [InlineData((MetadataContainer)999, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Jpeg, (MetadataFamily)999, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Jpeg, MetadataFamily.Exif, (MetadataOperation)999)]
    [InlineData((MetadataContainer)255, (MetadataFamily)255, (MetadataOperation)255)]
    public void UndefinedEnumValues_FailClosedWithoutThrowing(MetadataContainer container, MetadataFamily family, MetadataOperation operation)
    {
        Assert.False(MetadataCapabilities.TryGet(container, family, operation, out CapabilityDescriptor descriptor));
        Assert.Equal(default, descriptor);
        Assert.Equal(CapabilityStatus.NotSupported, MetadataCapabilities.GetStatus(container, family, operation));
    }

    [Fact]
    public void TheTiffBasedWriterHasTheSameRowsForEveryFormatItsLayoutTestsCover()
    {
        // TiffBasedWriteTests runs on a layout of each of these kinds; RW2 is edited in place only.
        MetadataContainer[] appendable = [MetadataContainer.Tiff, MetadataContainer.Dng, MetadataContainer.Arw, MetadataContainer.Cr2,
            MetadataContainer.Nef, MetadataContainer.Orf, MetadataContainer.Pef];
        foreach (MetadataContainer container in appendable)
            foreach (MetadataFamily family in new[] { MetadataFamily.Exif, MetadataFamily.IptcIim, MetadataFamily.Xmp })
            {
                CapabilityStatus read = family == MetadataFamily.Exif ? CapabilityStatus.Partial : CapabilityStatus.Supported;
                Assert.Equal(read, MetadataCapabilities.GetStatus(container, family, MetadataOperation.Read));
                foreach (MetadataOperation write in new[] { MetadataOperation.Insert, MetadataOperation.Replace, MetadataOperation.Remove })
                    Assert.Equal(CapabilityStatus.Partial, MetadataCapabilities.GetStatus(container, family, write));
            }
        Assert.Equal(3, MetadataCapabilities.All.Count(row => row.Container == MetadataContainer.Rw2));
        Assert.Equal(CapabilityStatus.Partial, MetadataCapabilities.GetStatus(MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Replace));
        Assert.Equal(CapabilityStatus.NotSupported, MetadataCapabilities.GetStatus(MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Insert));
    }

    [Fact]
    public void TheBmffWriterHasTheSameRowsForEveryBrandItsLayoutTestsCover()
    {
        // BmffWriteTests runs on a layout of each of these brands. No row says an item is removed.
        foreach (MetadataContainer container in new[] { MetadataContainer.Avif, MetadataContainer.Heic, MetadataContainer.Heif })
        {
            // Eight rows of the EXIF and XMP of the document, three of its ICC profile, and the one of the ICC reader.
            Assert.Equal(12, MetadataCapabilities.All.Count(row => row.Container == container));
            Assert.Equal(CapabilityStatus.Partial, MetadataCapabilities.GetStatus(container, MetadataFamily.Exif, MetadataOperation.Read));
            Assert.Equal(CapabilityStatus.Supported, MetadataCapabilities.GetStatus(container, MetadataFamily.Xmp, MetadataOperation.Read));
            foreach (MetadataFamily family in new[] { MetadataFamily.Exif, MetadataFamily.Xmp })
                foreach (MetadataOperation write in new[] { MetadataOperation.Insert, MetadataOperation.Replace, MetadataOperation.Remove })
                {
                    Assert.True(MetadataCapabilities.TryGet(container, family, write, out CapabilityDescriptor row));
                    Assert.Equal(CapabilityStatus.Partial, row.Status);
                    Assert.Equal(write == MetadataOperation.Remove, row.Note!.Contains("removal of the whole"));
                }
            Assert.Equal(CapabilityStatus.NotSupported, MetadataCapabilities.GetStatus(container, MetadataFamily.IptcIim, MetadataOperation.Read));
        }
    }

    [Fact]
    public void TheCr3ReaderHasItsTwoReadRowsAndNoneThatWrites()
    {
        // Cr3ExifTests and Cr3XmpTests author the layouts; no type writes a CR3 file.
        CapabilityDescriptor[] rows = MetadataCapabilities.All.Where(row => row.Container == MetadataContainer.Cr3).ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal(CapabilityStatus.Partial, MetadataCapabilities.GetStatus(MetadataContainer.Cr3, MetadataFamily.Exif, MetadataOperation.Read));
        Assert.Equal(CapabilityStatus.Supported, MetadataCapabilities.GetStatus(MetadataContainer.Cr3, MetadataFamily.Xmp, MetadataOperation.Read));
        foreach (CapabilityDescriptor row in rows)
        {
            Assert.Equal(MetadataOperation.Read, row.Operation);
            Assert.StartsWith("Cr3Metadata.Read: ", row.Note);
            Assert.EndsWith("Nothing is written.", row.Note);
        }
    }

    [Fact]
    public void TheJxlReaderHasItsTwoReadRowsAndNoneThatWrites()
    {
        // JxlMetadataTests and JxlCompressedTests author the boxes; no type writes a JPEG XL file.
        CapabilityDescriptor[] rows = MetadataCapabilities.All.Where(row => row.Container == MetadataContainer.Jxl).ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal(CapabilityStatus.Partial, MetadataCapabilities.GetStatus(MetadataContainer.Jxl, MetadataFamily.Exif, MetadataOperation.Read));
        Assert.Equal(CapabilityStatus.Supported, MetadataCapabilities.GetStatus(MetadataContainer.Jxl, MetadataFamily.Xmp, MetadataOperation.Read));
        foreach (CapabilityDescriptor row in rows)
        {
            Assert.Equal(MetadataOperation.Read, row.Operation);
            Assert.StartsWith("JxlMetadata.Read: ", row.Note);
            Assert.Contains("Brotli-compressed in a brob box", row.Note);
            Assert.EndsWith("Nothing is written.", row.Note);
        }
    }

    [Fact]
    public void TheIccAndCommentReadersHaveARowForEveryKindTheirTestsCover()
    {
        // IccProfileTests and CommentTests author a file of each of these kinds. The profile is
        // returned as bytes, which is Extract; of a TIFF only the first directory is looked at.
        MetadataContainer[] profiles = [MetadataContainer.Jpeg, MetadataContainer.Png, MetadataContainer.WebP, MetadataContainer.Tiff,
            MetadataContainer.Avif, MetadataContainer.Exv, MetadataContainer.Heic, MetadataContainer.Heif, MetadataContainer.Jp2, MetadataContainer.Psd];
        MetadataContainer[] comments = [MetadataContainer.Jpeg, MetadataContainer.Png, MetadataContainer.Exv];
        CapabilityDescriptor[] extracted = MetadataCapabilities.All.Where(row => row.Operation == MetadataOperation.Extract).ToArray();
        CapabilityDescriptor[] listed = MetadataCapabilities.All
            .Where(row => row.Family == MetadataFamily.Comment && row.Operation == MetadataOperation.Read).ToArray();

        Assert.Equal(profiles, extracted.Select(row => row.Container));
        Assert.Equal(comments, listed.Select(row => row.Container));
        foreach (CapabilityDescriptor row in extracted)
        {
            // Nothing but the profile is extracted through this table.
            Assert.Equal(MetadataFamily.Icc, row.Family);
            Assert.Equal(row.Container == MetadataContainer.Tiff ? CapabilityStatus.Partial : CapabilityStatus.Supported, row.Status);
            Assert.StartsWith("IccProfiles.Read: ", row.Note);
            Assert.Contains("not interpreted", row.Note);
        }
        foreach (CapabilityDescriptor row in listed)
        {
            // Of a PNG only the keyword Comment is read.
            Assert.Equal(row.Container == MetadataContainer.Png ? CapabilityStatus.Partial : CapabilityStatus.Supported, row.Status);
            Assert.StartsWith("Comments.Read: ", row.Note);
        }
    }

    [Fact]
    public void TheProfileAndTheCommentAreWrittenOnlyWhereAWriterAndItsTestsExist()
    {
        // The tests named <Format>IccProfileTests and <Format>CommentTests insert, replace and remove; no other container has a writer.
        // A TIFF and the BMFF brands are written by the documents that edit their EXIF, which refuse the layouts they cannot
        // account for: their rows are partial, as the other rows of those documents are. DNG and the raw formats have no row:
        // the primary directory of such a file is commonly that of a reduced image.
        MetadataOperation[] writes = [MetadataOperation.Insert, MetadataOperation.Replace, MetadataOperation.Remove];
        (MetadataContainer Container, string Document, CapabilityStatus Status)[] profiles =
        [
            (MetadataContainer.Jpeg, "JpegMetadataDocument", CapabilityStatus.Supported),
            (MetadataContainer.Png, "PngMetadataDocument", CapabilityStatus.Supported),
            (MetadataContainer.WebP, "WebPMetadataDocument", CapabilityStatus.Supported),
            (MetadataContainer.Tiff, "TiffBasedMetadataDocument", CapabilityStatus.Partial),
            (MetadataContainer.Avif, "BmffMetadataDocument", CapabilityStatus.Partial),
            (MetadataContainer.Heic, "BmffMetadataDocument", CapabilityStatus.Partial),
            (MetadataContainer.Heif, "BmffMetadataDocument", CapabilityStatus.Partial),
        ];
        (MetadataContainer Container, string Document, CapabilityStatus Status)[] comments =
        [
            (MetadataContainer.Jpeg, "JpegMetadataDocument", CapabilityStatus.Supported),
            (MetadataContainer.Png, "PngMetadataDocument", CapabilityStatus.Supported),
        ];
        (MetadataFamily Family, string Save, (MetadataContainer Container, string Document, CapabilityStatus Status)[] Writers)[] families =
        [
            (MetadataFamily.Icc, "SaveIccProfile", profiles),
            (MetadataFamily.Comment, "SaveComment", comments),
        ];
        foreach ((MetadataFamily family, string save, (MetadataContainer Container, string Document, CapabilityStatus Status)[] writers) in families)
        {
            CapabilityDescriptor[] rows = MetadataCapabilities.All.Where(row => row.Family == family && writes.Contains(row.Operation)).ToArray();

            Assert.Equal(writers.Select(writer => writer.Container), rows.Select(row => row.Container).Distinct());
            foreach ((MetadataContainer container, string document, CapabilityStatus status) in writers)
            {
                CapabilityDescriptor[] own = rows.Where(row => row.Container == container).ToArray();
                string api = document + "." + save;

                Assert.Equal(writes, own.Select(row => row.Operation));
                Assert.All(own, row =>
                {
                    Assert.Equal(status, row.Status);
                    Assert.StartsWith(api, row.Note);
                });
                Assert.StartsWith(api + " with null: ", own[2].Note);
            }
        }
    }

    [Fact]
    public void EnumDomains_CoverTheDocumentedBaseline()
    {
        Assert.Equal(32, Enum.GetValues<MetadataContainer>().Length); // Unknown + the 31 documented baseline rows (XMP represented as XmpSidecar)
        Assert.Equal(7, Enum.GetValues<MetadataFamily>().Length);
        Assert.Equal(5, Enum.GetValues<MetadataOperation>().Length);
        Assert.Equal(0, (int)MetadataContainer.Unknown);
        Assert.Equal(0, (int)MetadataFamily.Exif);
        Assert.Equal(0, (int)MetadataOperation.Read);
        Assert.Equal(0, (int)CapabilityStatus.NotSupported);
    }
}
