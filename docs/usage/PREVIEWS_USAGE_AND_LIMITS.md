# Preview and thumbnail listing, extraction, and editing

`Previews.List` reports bounded preview candidates in ascending pixel area, with each `PreviewImage`
carrying the MIME type, dimensions when known, file-relative byte offset, byte length, and source
pointer. It does not decode pixels. `Previews.Extract` returns JPEG bytes, native preview bytes
(including TIFF, WMF, PNM, HEVC and AV1 payloads), re-encoded TIFF bytes for reduced uncompressed
images, or generated PNM/JPEG bytes for the EPS AI7 and XMP thumbnail forms described below. BMFF HEVC/AV1 bytes remain encoded and are
not decoded or transcoded.

```csharp
using MetadataSharp.Formats;

byte[] file = File.ReadAllBytes("picture.heic");
IReadOnlyList<PreviewImage> previews = Previews.List(file);
foreach (PreviewImage preview in previews)
{
    Console.WriteLine($"{preview.MimeType} {preview.Width}x{preview.Height} " +
                      $"offset={preview.Offset} length={preview.Length} ({preview.Source})");
}
if (previews.Count != 0)
    File.WriteAllBytes("preview.bin", Previews.Extract(file, previews[0]));
```

## Loaders and byte semantics

- JPEG and EXV segment readers, TIFF-based raw files, PNG, WebP, PSD and BMFF retain the file ranges
  they can prove are bounded. JPEG APP1, TIFF IFD and MakerNote ranges report absolute offsets in the
  complete input. BMFF item extents include the container's item-location calculations.
- CRW uses CIFF preview and thumbnail records; MRW scans its bounded TTW TIFF block; RAF scans the
  embedded JPEG and its separate TIFF block; JP2 scans the Exif UUID box; PGF scans its bounded
  embedded image; XMP sidecars and PNG iTXt packets can expose `xmpGImg`/`xapGImg` JPEG thumbnails and compressed ImageMagick Raw EXIF profiles.
  EPS exposes DOS EPS WMF/TIFF previews and decodes AI7 hex thumbnails to binary P6 PNM. EPS
  PostScript is never interpreted or executed.
- JPEG extraction checks SOI/EOI markers. TIFF, WMF, PNM, HEVC and AV1 extraction is byte-preserving
  and does not decode the media; reduced uncompressed TIFF images are re-encoded as standalone TIFF
  thumbnails. EPS AI7, XMP thumbnails and generated TIFF previews are transformed, so their `Offset`
  identifies the source payload (or first strip) while `Length` is the generated output length; use
  `Extract` rather than slicing the file at that range for those generated previews.
- Malformed, unsupported, overlapping or out-of-file pointer layouts are omitted; configured
  `MetadataLimits` bound the input, directory/segment walks, and the maximum preview count. A listed
  media type that has no extractor throws `UnsupportedFeature`; invalid selected byte ranges fail
  with an offset-aware metadata error.

## Editing

There is no general-purpose preview replacement API. `ExifEdit.SetThumbnail` and
`ExifEdit.RemoveThumbnail` update an existing EXIF thumbnail directory; they do not synthesize a
missing directory. PSD's complete-set `PsdMetadata.Write` can write its JPEG thumbnail resource.
Other preview-loader formats, including CRW, MRW, RAF, JP2, EPS and BMFF, remain read/extract-only
through this preview API. Their container writers do not promise to preserve or rewrite a preview
unless their format-specific writer contract says so.

## Tests and differential checks

The synthetic range/offset tests are in
[`PreviewTests.cs`](https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Tests/Containers/PreviewTests.cs); they pin CRW/CIFF,
MRW/TTW, RAF embedded-JPEG and TIFF, JP2 UUID, EPS, XMP and BMFF handling alongside existing
JPEG/TIFF/PSD coverage. The pinned Exiv2 0.28.9 comparison is
[`run_previews.py`](https://github.com/sensboston/MetadataSharp/blob/HEAD/tools/parity/run_previews.py), with offline parser/gate tests in
[`test_preview_parity.py`](https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/parity/test_preview_parity.py). `MetadataSharp.Dump --previews`
emits each preview's dimensions, MIME type, byte length, absolute offset and source. Since `exiv2 -pp`
does not print offsets, the dynamic parity pass runs pinned `exiv2 -ep`, reverses direct, EPS-hex,
XMP-base64, compressed PNG Raw EXIF and generated-TIFF strip output bytes to their source positions,
and requires our reported offset to be one of those oracle positions. The gate fails on an unprovable offset, any type/dimension/length/offset mismatch,
or reader refusal; fixture tests independently pin range offsets.
