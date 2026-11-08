# Read-only standalone TIFF usage and limitations

```csharp
using MetadataSharp.Formats.Tiff;

TiffLayout layout = await TiffLayout.ReadAsync("input.tif");
foreach (var directory in layout.Directories)
{
    if (directory.Image is { } image)
        foreach (var range in image.Segments)
            Console.WriteLine($"{range.Offset}: {range.Length} bytes");
}
foreach (var issue in layout.Issues)
    Console.WriteLine($"{issue.Code}: {issue.Message}");
```

This API returns an owned immutable layout/accounting snapshot. It does **not**
return decoded pixels, raw metadata values or an EXIF model. It does not write a
TIFF, hold a file handle after returning, validate compressed pixel data, or approve
relocation. Paths close on success/failure; borrowed readable/seekable streams at
zero stay open with unspecified final position. No later source-dependent methods
exist, so document disposal/source-change preflight is not applicable to this model.
Concurrent mutation during a read is unsupported. True async reads and cooperative
cancellation are used, without claiming instantaneous cancellation of CPU sorting.

Check both IsSupported and Issues. A safely framed but unsupported compression,
sample layout or opaque/private field yields explicit issues instead of pretending
to support it. Unknown field widths, BigTIFF and DNGVersion refuse. Supported here
means the selected *range-accounting subset*, not full TIFF/Exif compliance, pixel
decodability, absence of hidden private pointers or suitability for rewriting.
Unreferenced file gaps are not certified safe. Unsupported old JPEG table targets
are not guessed; their descriptors/issue reports are intentionally incomplete.

Strip/tile arrays retain their logical order, even if data is physically scattered.
Global ranges are sorted by offset; exact compatible shared external values are
reported once, with each field still exposing its ValueRange. Shared directory DAGs
are accepted; cycles/partial aliases/image overlaps/duplicates are rejected. There
is no normalization, repair or best-effort mode for invalid framing/ranges.

Finite options apply before descriptor/array growth. Default MaxInputBytes is 1 GiB,
first-profile file maximum 4 GiB, but there is no 65,527-byte *whole-file* EXIF cap.
Existing embedded EXIF readers/writers are unchanged. MaxValueBytes still limits
individual declared fields; default metadata/graph/segment ceilings can deliberately
refuse complex otherwise viewable TIFFs. See the normative/support review for exact
logical charges and supported layouts. Those charges are not managed-memory claims.

M16 writing needs a separate approved source identity, pointer/tag classification
and copy/relocation strategy. No BigTIFF, DNG or GeoTIFF semantics are implied.

A preview-only metadata directory without TIFF dimensions is safely accounted via
global JpegPreview range plus explicit UnsupportedPointerLayout (and unsupported
compression where applicable). Image remains null: dimensions/storage are never
guessed from JPEG bytes. Missing/unpaired preview extent fields still refuse.
