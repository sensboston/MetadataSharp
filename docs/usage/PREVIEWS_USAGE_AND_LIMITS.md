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
  embedded JPEG and its separate TIFF block; RW2 reads the Panasonic MakerNote preview (not a generic
  linked thumbnail IFD); JP2 scans the Exif UUID box; PGF scans its bounded embedded image; XMP
  sidecars and PNG iTXt packets can expose `xmpGImg`/`xapGImg` JPEG thumbnails and compressed
  ImageMagick Raw EXIF profiles.
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

There is no general-purpose replacement API for every `PreviewImage`. `ExifEdit.SetThumbnail` and
`ExifEdit.RemoveThumbnail` update an existing EXIF thumbnail directory where the format writer supports
it; they do not synthesize a missing directory. `CrwMetadataDocument` also writes/removes the CIFF
thumbnail record, and the complete-set `PsdMetadata.Write` writes/removes PSD thumbnail resources
0x0409/0x040C. These are the explicit preview write paths. Other format-specific preview loaders,
including MRW, RAF, RW2, JP2, EPS and BMFF, are read/extract-only; their writers do not promise to
preserve or rewrite preview bytes.

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
or reader refusal; fixture tests independently pin range offsets. The group-C preview step is a gate:
any unlisted/extra preview, field mismatch, offset mismatch, or reader refusal fails CI. No directory or
extension is globally excluded. The fixture-only exceptions are explicit:

| Fixture | Reason for excluding it from the Exiv2 preview oracle |
| --- | --- |
| `b11eps/dos-eps-xmp.eps` | Exiv2 0.28.9 rejects the explicit page after its implicit page; the DOS TIFF carrier is covered by the EPS unit test. |
| `b11eps/eps-plain.eps` | Metadata-only EPS fixture; Exiv2 0.28.9 rejects its minimal DSC page framing before listing previews. |
| `b11eps/eps-reference-write.eps` | Writer-parity fixture with no preview payload; covered by the dedicated EPS write/read gate. |
| `b11eps/eps-xmp.eps` | XMP metadata-parser fixture with no native preview; Exiv2 0.28.9 rejects its minimal DSC page framing. |
| `b11eps/eps-xmp-31.eps` | Illustrator 3.1/XMP parser variant with no native preview payload. |
| `b11eps/eps-xmp-crlf.eps` | CRLF/XMP parser variant with no native preview payload. |
| `b11eps/eps-xmp-nomain.eps` | NoMain/XMP parser variant with no native preview payload. |
| `b11eps/eps-xmp-readonly.eps` | Read-only XMP parser variant with no native preview payload. |
| `b11eps/eps-xmp-space.eps` | OpenOffice/XMP parser variant with no native preview payload. |
| `b7/owned-codestream.jxl` | Standalone JPEG XL codestream is not recognized as an image type by Exiv2 0.28.9; the container JXL sample is gated. |
| `b8/owned-flat-rgb.psd` | 52-byte minimal PSD parser fixture is not recognized as an image by Exiv2 0.28.9; metadata-bearing PSD remains gated. |
