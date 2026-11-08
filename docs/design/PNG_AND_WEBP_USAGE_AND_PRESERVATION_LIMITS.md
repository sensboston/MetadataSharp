# PNG and WebP Usage and Preservation Limits

See the approved container contracts, normative review and PROGRESS.md for actual
implementation/CI state. M13 is PNG; M14 must pass independently before group merge.
No package publication, full image conformance or performance claim is implied.

## PNG

```csharp
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Png;

await using var document = await PngMetadataDocument.OpenAsync("input.png");
var exif = await document.ReadExifAsync(); // Owned snapshot, or null.
var xmp = await document.ReadXmpAsync();  // May be opaque rather than editable.
var edit = new XmpEdit();
edit.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"),
    XmpValue.CreateText("MetadataSharp"));
await document.SaveXmpAsAsync("new-output.png", edit);
```

EXIF uses independent SaveExif[Async]/SaveExifAs[Async] and ExifEdit operations;
TIFF payloads are raw, without the JPEG Exif signature. The 65,527-byte TIFF cap
remains deliberate. A larger safely framed chunk can only be copied or removed
whole. Unsupported EXIF relocation remains refused by the existing planner.

Every accepted chunk's CRC is checked, including IDAT and unknown chunks. Static
noninterlaced/Adam7 *headers* are accepted, not image pixels. APNG and unknown
critical chunks are refused. eXIf must precede IDAT; contiguous IDAT and a final
empty IEND are required. This is not a general PNG validator: there is no IDAT
inflation, filter-row validation, color-profile validation or image decoding.

XMP uses the exact iTXt keyword XML:com.adobe.xmp, empty language/translated fields,
and UTF-8. Existing compressed packets are bounded in both directions with exact
zlib framing/checksum validation. Changed output is uncompressed, flag/method zero.
Other text and ICC data remain opaque. No generic text editor or IPTC mapping.
PNG MaxCompressedXmpBytes and MaxDecompressedXmpBytes default to 1 MiB. The common
metadata budget also applies to retained/staged representations; it is not a bound
on exact managed allocations. No allocation/performance improvement is claimed.

Only addressed metadata changes. IDAT bytes, chunk boundaries, PLTE, other-family
metadata and safe-to-copy unknown chunks are preserved. Unknown unsafe ancillary
chunks block effective edits; a byte-identical no-op remains allowed. Duplicate
target metadata is ambiguous; it is never silently merged or selected. Malformed
other-family semantics are not decoded as a prerequisite to editing this family.
Whole removal of a uniquely identified malformed target bypasses semantic parsing.

ExifTool 12.76 PNG output uses a read-only xpacket wrapper. The current XMP profile
returns that as an owned opaque packet; Properties/property edits are unsupported.
It can still be copied exactly or removed whole. This reference limitation is
explicitly tested/reported, not resolved by discarding the wrapper or relaxing XML.

## Shared I/O guarantees

Open requires a readable, seekable stream at zero, or a path. Borrowed streams
remain open; path-opened sources belong to the document. Returned metadata owns
its bytes. Options and operation edits are captured. Do not concurrently mutate
or operate on a document/source/edit. Source SHA-256/length is revalidated before
reads and save preflight, including no-op saves; this is not a filesystem lock or
protection against a concurrent mutation after preflight.

Stream outputs must be distinct, writable, seekable, empty and positioned at zero.
Semantic/resource/source/safety failures occur before output. Once emission begins,
I/O failure or cancellation may leave partial caller-owned output. Async APIs use
actual async stream reads/writes and cooperative cancellation; CPU parsing is not
claimed to be parallel. No in-place, append or overwrite operation is provided.

Save*As writes a new same-directory CreateNew temporary file, flushes/closes it,
and publishes with no overwrite. A destination race fails without replacing the
winner. The temporary is cleaned on ordinary failure/cancellation. This is not a
crash-durable transaction, hard-link/symlink identity proof or sandbox boundary.

## WebP

Implementation and validation details are to be recorded after the M13 gate and
M14 work. The approved contract is not itself evidence that WebP support shipped.
