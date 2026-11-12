# Sync: usage and limits

`MetadataSharp.Core.Sync.MetadataSync` converts EXIF, IPTC and XMP into each other. It computes
edits and writes nothing; the documents of `MetadataSharp.Formats` write them.

## Conversions

- `ExifToXmp(exif, xmp?, options?)`, `XmpToExif(xmp, exif?, options?)`,
  `IptcToXmp(iptc, xmp?, options?)`, `XmpToIptc(xmp, iptc?, options?)`.
- `SyncOptions.Mode`: overwrite the target (default) or `SyncMode.FillMissing`, which leaves what
  the target holds. `SyncOptions.EraseSource` removes from the source what was converted, and only that.
- The rows are public: `MetadataSync.ExifMappings` and `MetadataSync.IptcMappings`.
- The result is `SyncEdits`: `Exif`, `Iptc`, `Xmp` (edits to apply), `Notes` (a value that was not
  converted, with the reason), `IsEmpty`.

## Reconcile

`MetadataSync.Reconcile(exif, iptc, xmp, digest, options)` and `ImageMetadata.Reconcile(options)`
decide, field by field, which block holds the value and write it to the other blocks that exist.
The fields are the rows of `IptcMappings` and six EXIF tags that say the same thing
(ImageDescription, Artist, Copyright, DateTime, DateTimeOriginal, DateTimeDigitized). EXIF is
taken when it holds the field; otherwise IPTC when XMP lacks the field, or when the IPTC digest is
stale (IPTC was edited after XMP) and XMP says something else; otherwise XMP. The value goes to
XMP always, and to EXIF and IPTC where the file has the block. `SyncEdits.Choices` names the
source of every field. `ReconcileOptions.CreateIptc` / `CreateExif` allow a block that does not
exist to be created; `MirrorAllExif` also copies every other mapped EXIF tag to XMP, as Exiv2 does.
A block that is not editable is not written and is named in `Notes`.

The rules follow the Metadata Working Group guidelines as ExifTool implements them (`MWG.pm`);
the MWG document itself was not available when this was written.

## Writing

`SaveAll(stream, edits)` / `SaveAllAs(path, edits)` on `JpegMetadataDocument`,
`PngMetadataDocument`, `WebPMetadataDocument`, `BmffMetadataDocument` and
`TiffBasedMetadataDocument` write the blocks in one pass, under the same rules as the single-block
saves, and recompute an IPTC digest that exists. A format without a block (IPTC in WebP or BMFF)
refuses a non-empty edit for it.

## Limits

- No async `SaveAll`; none for PSD or sidecars.
- A value whose type or count is not what the tag is defined with is not converted (a note says so).
  A blank text is not converted and not noted.
- Not mapped: CFAPattern, OECF, SpatialFrequencyResponse, DeviceSettingDescription.
- Fractions of a second keep their digits as written (`SubSecTime` `06` is `.06`).
  GPS coordinates from XMP are kept to a millionth of a second; a round trip is exact only for a
  value that seven decimals of a minute can hold.
- XMP text is written to `UserComment`, `GPSProcessingMethod` and `GPSAreaInformation` under the
  ASCII code when it is ASCII, else UNICODE. Text that starts with the marker Exiv2 writes
  (`charset=Ascii `, `charset=Unicode `, `charset=Jis `, `charset=Undefined `, exactly so) loses the
  marker and is written under the named code; JIS is not encoded, and Ascii named for other
  characters cannot hold them, so those two follow the first rule.
- Compared in CI with the conversion of exiv2 0.28.9; every difference is listed with its reason in
  `tools/reference/sync_parity.py`.
