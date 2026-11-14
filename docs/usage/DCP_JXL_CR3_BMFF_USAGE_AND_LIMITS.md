# DCP and embedded IPTC: usage and limits

This note records the read/write boundaries established in the repository for the
Adobe DNG Camera Profile (DCP) TIFF profile and IPTC IIM carried by JPEG XL, Canon
CR3 and HEIF-family BMFF metadata. It is intentionally narrower than a claim of
complete Exiv2 parity. The executable per-operation table is
[`MetadataCapabilities`](../../src/MetadataSharp.Core/MetadataCapabilities.cs),
pinned by [`MetadataCapabilitiesTests`](../../tests/MetadataSharp.Tests/Capabilities/MetadataCapabilitiesTests.cs).

## DCP profile EXIF

`ImageMetadata.Detect` identifies both byte orders of the DCP TIFF signature
(magic `0x4352`) as `MetadataFormat.Dcp`. `ImageMetadata.Read` sends the profile through the bounded TIFF reader. It exposes
raw EXIF values and also reads IPTC (`0x83BB`/Photoshop resources) and XMP (tag 700)
when those fields are present; DNG raw-image validation is not applied. DCP-specific
semantic interpretation of camera-profile tags, profile validation, color
transforming and profile installation into an image are outside this API.

A DCP profile can be edited with `TiffBasedMetadataDocument` and `ExifEdit`:

```csharp
using MetadataSharp.Core;
using System.Text;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Formats.Tiff;

using TiffBasedMetadataDocument profile =
    TiffBasedMetadataDocument.Open("camera-profile.dcp");
ExifMetadata exif = profile.ReadExif();
var edit = new ExifEdit();
byte[] software = Encoding.ASCII.GetBytes("MetadataSharp\0");
edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0131),
    ExifValue.FromRaw(TiffFieldType.Ascii, (uint)software.Length,
        exif.ByteOrder, software));
profile.SaveExifAs("edited-profile.tif", edit);
```

On an effective edit, the writer changes the DCP magic to classic TIFF magic, as
Exiv2 0.28.9 does in `TiffImage::writeMetadata`; an exact no-op save remains a byte-for-byte
copy. The writer uses the same conservative rules as its TIFF-based editor for EXIF, IPTC and XMP: bounded directories and values, no moving image/profile data, no pointer/image-address/MakerNote edits, and refusal when a file cannot be safely accounted for. ICC editing and DCP color-transform operations are not claimed. This does not add BigTIFF support or broaden
the DNG contract. The upstream behavior is visible in [the pinned `TiffImage::writeMetadata`](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/src/tiffimage.cpp) and [TIFF header encoder](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/src/tiffimage_int.cpp). The DCP reader and writer fixtures are
[`DcpProfileUsesTheClassicTiffMetadataReader`](../../tests/MetadataSharp.Tests/Containers/ImageMetadataTests.cs)
and [`DcpProfileWritesExifWithExiv2ClassicTiffOutputMagic`](../../tests/MetadataSharp.Tests/Tiff/TiffBasedWriteTests.cs), plus [`DcpProfileWritesIptcAndXmpWithExiv2ClassicTiffOutputMagic`](../../tests/MetadataSharp.Tests/Tiff/TiffBasedWriteTests.cs).

## IPTC in read-only containers

The following readers expose IPTC IIM when the TIFF IPTC-NAA field (`0x83BB`) is
present in the carrier:

- JXL: the TIFF EXIF box, plain or Brotli-compressed (`brob`).
- CR3: the Canon CMT TIFF directories.
- AVIF, HEIC and HEIF: the TIFF Exif item of the BMFF `meta` box.

A missing carrier field yields `Iptc == null`. Framing and resource limits are
checked by the IPTC and underlying container readers. These are read-only paths:
there is no IPTC write operation for JXL, CR3 or BMFF, and no IPTC capability
row claims one. The BMFF reader follows the pinned Exiv2 behavior: it reads IIM
found by TIFF parsing, but does not provide an IPTC writer. Tests author the
embedded fields directly:

- [`JxlMetadataTests.IptcNaaInTheExifTiffIsRead`](../../tests/MetadataSharp.Tests/Jxl/JxlMetadataTests.cs)
- [`Cr3ExifTests.IptcNaaInThePrimaryCanonDirectoryIsRead`](../../tests/MetadataSharp.Tests/Cr3/Cr3ExifTests.cs)
- [`BmffMetadataTests.IptcNaaInTheExifItemIsRead`](../../tests/MetadataSharp.Tests/Bmff/BmffMetadataTests.cs)

For general IPTC editing in JPEG, PNG, PSD and TIFF-based formats, see
[`IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md`](IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md).
