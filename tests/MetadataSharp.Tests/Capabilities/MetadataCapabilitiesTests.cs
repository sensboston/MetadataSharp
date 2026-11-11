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
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved writable tag subset only (descriptive IFD0/EXIF/GPS tags); unsupported tags are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved writable tag subset only (descriptive IFD0/EXIF/GPS tags); unsupported tags are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset; whole-EXIF removal is also supported."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM within the approved IPTC usage contract; declared or UTF-8 encoding; finite packet limits enforced."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved IPTC dataset usage subset; unsupported records are refused, not silently rewritten."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved IPTC dataset usage subset; unsupported records are refused, not silently rewritten."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Bounded restricted XMP profile (UTF-8, finite packet limits); raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal is also supported."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "eXIf chunk in static PNG within the approved chunk policy; unsupported layouts are refused."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "eXIf in static PNG; approved writable tag subset only; unsupported tags are refused."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "eXIf in static PNG; approved writable tag subset only; unsupported tags are refused."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "eXIf in static PNG; per-tag removal within the approved subset; whole-EXIF removal is also supported."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static PNG within the approved chunk/text policy; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static PNG; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static PNG; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static PNG; per-property removal within the restricted profile; whole-XMP removal is also supported."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "EXIF in static (non-animated) WebP within the approved RIFF policy; unsupported layouts are refused."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "EXIF in static WebP; approved writable tag subset only; unsupported tags are refused."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "EXIF in static WebP; approved writable tag subset only; unsupported tags are refused."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "EXIF in static WebP; per-tag removal within the approved subset; whole-EXIF removal is also supported."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static WebP within the approved RIFF policy; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static WebP; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static WebP; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static WebP; per-property removal within the restricted profile; whole-XMP removal is also supported."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "Classic (non-BigTIFF) TIFF: IFD0, EXIF, GPS, Interop and thumbnail directories as raw values (TiffExif), and read-only range accounting (TiffLayout); sub-image directories are not read as entries."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "TiffBasedMetadataDocument: approved IFD0/EXIF/GPS tag subset, appended, image data never moved. TiffMetadataDocument: ten descriptive IFD0 tags via append-preserve rebuild, obsolete bytes retained."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "TiffBasedMetadataDocument: approved IFD0/EXIF/GPS tag subset, in place when the value fits, appended otherwise. TiffMetadataDocument: ten descriptive IFD0 tags via append-preserve rebuild."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset, the directory rewritten in place; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Tiff, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); new data is appended, image data and the MakerNote keep their offsets; refused when the file cannot be appended to."),
        (MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); written in place when the value fits, appended otherwise; image data and the MakerNote keep their offsets."),
        (MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset, the directory rewritten in place; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Dng, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Dng, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); new data is appended, image data and the MakerNote keep their offsets; refused when the file cannot be appended to. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); written in place when the value fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset, the directory rewritten in place; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Arw, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Arw, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); new data is appended, image data and the MakerNote keep their offsets; refused when the file cannot be appended to. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); written in place when the value fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Cr2, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset, the directory rewritten in place; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Cr2, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Cr2, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); new data is appended, image data and the MakerNote keep their offsets; refused when the file cannot be appended to. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); written in place when the value fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Nef, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset, the directory rewritten in place; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Nef, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Nef, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); new data is appended, image data and the MakerNote keep their offsets; refused when the file cannot be appended to. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); written in place when the value fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Orf, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset, the directory rewritten in place; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Orf, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Orf, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); new data is appended, image data and the MakerNote keep their offsets; refused when the file cannot be appended to. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved writable tag subset (descriptive IFD0/EXIF/GPS tags); written in place when the value fits, appended otherwise; image data and the MakerNote keep their offsets. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Pef, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset, the directory rewritten in place; whole-EXIF removal is refused: the directories describe the image."),
        (MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM in tag 0x83BB, or in the Photoshop resources of tag 0x8649; approved IPTC usage contract; finite packet limits enforced."),
        (MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 0x83BB, appended; approved IPTC dataset usage subset; refused when the file keeps its IPTC in Photoshop resources (tag 0x8649) only."),
        (MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 0x83BB, keeping its field type; approved IPTC dataset usage subset; a stream kept in Photoshop resources (tag 0x8649) only is refused."),
        (MetadataContainer.Pef, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset; whole-IPTC removal removes tag 0x83BB."),
        (MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "The packet in tag 700 of the primary directory; bounded restricted XMP profile; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Tag 700 of the primary directory, appended; bounded restricted XMP profile."),
        (MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Tag 700 of the primary directory; bounded restricted XMP profile; a packet outside it is refused."),
        (MetadataContainer.Pef, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal removes tag 700."),
        (MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "IFD0, EXIF, GPS, Interop and thumbnail directories and a recognised MakerNote of a classic (non-BigTIFF) TIFF structure, as raw values; sub-image directories are not read."),
        (MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "In place only: a value of the approved tag subset that fits where the old one is. The raw data runs to the end of the file, so nothing is appended and no tag can be added. Tested on authored layouts, not on camera files."),
        (MetadataContainer.Rw2, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset, the directory rewritten in place; whole-EXIF removal is refused."),
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
    [InlineData(MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Jxl, MetadataFamily.Xmp, MetadataOperation.Read)]
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
