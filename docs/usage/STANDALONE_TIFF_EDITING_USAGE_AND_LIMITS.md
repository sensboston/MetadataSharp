# Restricted standalone TIFF editing: usage and limits

The M16 API is distinct from the M15 `TiffLayout.Read[Async]` accounting facade and
from embedded EXIF serialization. The [approved contract](STANDALONE_TIFF_EDITING_CONTRACT_PROPOSAL.md)
is authoritative; see the [write audit](../audits/STANDALONE_TIFF_EDITING_AUDIT.md)
for actual evidence and residual limitations.

```csharp
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;

await using var document = await TiffMetadataDocument.OpenAsync("input.tif");
var edit = new TiffMetadataEdit();
byte[] software = Encoding.ASCII.GetBytes("MetadataSharp\0");
edit.Set(new TiffFieldKey(document.Layout.FirstIfdOffset, 305),
    ExifValue.FromRaw(TiffFieldType.Ascii, (uint)software.Length,
        document.Layout.ByteOrder, software));
await document.SaveAsAsync("new-output.tif", edit); // Must not already exist.
```

The operation may refuse an otherwise viewable TIFF. `Layout.IsSupported` is
necessary for effective editing, but NOT sufficient: the writer also checks its
closed type/count/pointer schema. No general camera/archival TIFF writer is implied.

## Addressing and supported values

- Keys contain original-source reachable IFD offsets, not discovery indexes or
  destination offsets. The document remains bound to its original source after a
  save. Reopen the output before editing that output; do not reuse its new offsets
  with the old document. Multiple saves on the old document are independent edits
  of its original source, not cumulative transactions.
- Only tags 269/270/271/272/285/305/315/316 (printable ASCII with one final NUL),
  306 (20-byte Gregorian DateTime) and 274 (one SHORT, orientation 1–8) are addressed.
  Empty text is one NUL; no Unicode conversion, timezone conversion, synchronization
  or arbitrary EXIF/GPS/XMP/IPTC/ICC editing is performed. Orientation converts to
  the original TIFF byte order. Count is typed-element count, not always byte count.
- `Set`/`Remove` are last-operation-wins per key. `ReadValue` returns an owned
  immutable `ExifValue`, or null when absent; it is not an arbitrary raw-field API.
  Existing malformed target content refuses read/replacement. Removal bypasses
  content decoding, not safe framing or the other-field write policy. Untouched
  descriptive values remain byte-preserved without new-text/calendar normalization.
- Unsupported field IDs, wrong Set types/counts/content and unreachable directory
  keys are argument errors. Unknown/private/opaque extension fields prevent every
  effective write; MakerNote, XMP, IPTC, ICC, ColorMap, JPEGTables and old JPEG
  schemes are not silently dropped. Emptying an IFD refuses instead of deleting it.
- Empty/absent-removal/equivalent-Set operations copy exactly, including eligible
  safely framed issue-bearing sources. They still verify identity and budgets.
  `IsEmpty` describes recorded operations, not whether a plan will be an exact copy.

## Preservation is not erasure

An effective save copies the complete original prefix and appends a sorted,
word-aligned replacement of every reachable IFD, remapping all directory links.
Within the original length, only header bytes 4–7 change. Shared directory targets
remain shared. Strips, tiles, previews, unchanged external values and image offset/
count arrays retain original bytes and absolute offsets. New values and rebuilt
external directory-pointer arrays append; old slots/gaps are never reused.

**Removed/replaced metadata and obsolete directories remain physically present.**
This is NOT privacy scrubbing, secure erasure, compaction or a guarantee against
forensic discovery. Repeated effective edits grow the file. No in-place save or
source append is available. No compressed-data validation, decoding or recompression
occurs; accepted compression IDs describe accounting, not proof of image validity.

## Ownership, identity, resource and failure boundaries

- Path documents own their handle until disposed. Borrowed readable/seekable sources
  must start at zero and remain open/unchanged; disposal does not close them. Layout
  and returned values survive document/source disposal. Stream final positions are
  unspecified. Concurrent source/edit/document operations are unsupported.
- Open captures length and SHA-256; reads/saves recheck them. `SourceChanged` is not
  an atomic snapshot lock against mutations during later copying. Caller must
  prevent concurrent mutation and destination aliases, including filesystem links.
- Options are captured before I/O; edits before the first save await. Logical edit
  snapshot charge is checked before dictionary copying; original graph and staged
  entries/values/pointers/tail are budgeted before output. This is a finite logical
  accounting model, not a measured CLR allocation ceiling or performance claim.
- Output must fit MaxOutputBytes, captured MaxInputBytes, the classic 4-GiB address
  space and captured entry/value/range limits. Defaults intentionally refuse some
  valid files. The 65,527-byte default MaxValueBytes limits individual values, NOT
  whole TIFF files. No BigTIFF or DNG support is added.
- Stream saves require a writable, empty, seekable, position-zero destination,
  distinct from source. Preflight failures write nothing. Once emission starts,
  I/O failure/cancellation can leave partial caller-owned output; it remains open.
- New-path saves preflight, write a same-directory temporary file, flush/close and
  publish without overwrite. Failure/cancellation/racing target creation cleans the
  temporary file on normal exception paths and preserves an existing target. No
  crash/power-loss durability or hostile filesystem-race/alias defense is promised.
- Async methods use async I/O and cooperative cancellation, not Task.Run wrapping.
  Physical I/O errors remain I/O errors; semantic refusals use MetadataException
  codes InvalidTiff, UnsupportedFeature, UnsafeRelocation, LimitExceeded or
  SourceChanged. A failed operation does not publish a partially prepared model.
