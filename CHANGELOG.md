# Changelog

## 1.0.1

- Package icon.

## 1.0.0

First release.

- Reads EXIF with camera MakerNotes, IPTC, XMP, ICC profiles, comments and previews from JPEG, PNG,
  WebP, TIFF, DNG and the TIFF-based raw formats, CRW, MRW, RAF, CR3, HEIC/HEIF/AVIF, JPEG XL,
  JPEG 2000, PSD, EXV, EPS, PGF, XMP sidecars and common video containers.
- Edits EXIF, IPTC, XMP and ICC without decoding or recompressing image data; an edit whose
  safety cannot be established is refused with a `MetadataErrorCode`.
- MakerNotes of Canon, Nikon, Sony, Fujifilm, Pentax, Panasonic, Olympus/OM System and others,
  interpreted and edited tag by tag in place.
- Typed tag catalog with print conversion, EXIF/IPTC/XMP synchronization, the file operations of
  the exiv2 command line, partial reads from streams, bounded parsing of hostile input.
- Measured against exiv2 0.28.9: no differing row in the tag tables of 329 corpus files,
  12,645 of 12,645 interpreted MakerNote values, 46 of 46 preview lists.
