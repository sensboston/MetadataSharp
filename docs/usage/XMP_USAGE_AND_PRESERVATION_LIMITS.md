# XMP Usage, Ownership and Preservation Limits

M10–M11 implement the [approved contracts](XMP_PACKET_SIDECAR_AND_JPEG_CONTRACT_PROPOSAL.md).
This is a restricted UTF-8 XMP profile, not complete RDF/XMP support. Normative
sources, deliberate restrictions and the historical JPEG capacity discrepancy are
recorded in [the normative review](XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md).
No new NuGet/native dependency was added.

## Create or edit a sidecar

```csharp
using MetadataSharp.Core.Xmp;

var edit = new XmpEdit();
edit.Set(new XmpPropertyName("http://ns.adobe.com/xap/1.0/", "CreatorTool"),
    XmpValue.CreateText("My application"));
edit.Set(new XmpPropertyName("http://purl.org/dc/elements/1.1/", "subject"),
    XmpValue.CreateArray(XmpValueKind.Bag, new[] { "travel", "landscape" }));

XmpPacket packet = XmpPacket.CreateEmpty().Apply(edit)!;
await packet.SaveAsAsync("new-sidecar.xmp"); // refuses an existing destination
XmpPacket loaded = await XmpPacket.LoadAsync("new-sidecar.xmp");
```

Use `CreateLanguageAlternative` for language-keyed text, including optional
`x-default`. Keys are unique case-insensitively. x-default is emitted first;
otherwise the input preferred language order is retained. Bag multiplicity and
Seq order are preserved. No domain validation, automatic language mirroring,
date/number conversion or EXIF/IPTC synchronization occurs.

The packet deeply owns its bytes and model. Source streams may be nonseekable,
are read from their current position to EOF, and remain open. Path load handles
close before returning. Empty Apply returns the exact snapshot; an effective
property edit emits canonicalized XML, not the original lexical representation.
XML normalization applies when reading literal text. Namespace identity uses URI
and local name, never the prefix. Unknown simple namespaces remain editable.

For well-formed unsupported RDF or read-only packets, `IsEditable` is false:
`Properties` and property-changing Apply throw UnsupportedFeature. Raw copy,
Save and empty Apply retain every original byte. `RemoveAllXmp()` makes Apply
return null (packet absence); it does not delete a sidecar path automatically.
Malformed XML, forbidden DOCTYPE or invalid UTF-8 is InvalidXmp, not an opaque
editable model. Nonpositive limits and invalid caller values are argument errors.

## Read/edit standard JPEG XMP independently

```csharp
using MetadataSharp.Formats.Jpeg;

await using var jpeg = await JpegMetadataDocument.OpenAsync("source.jpg");
XmpPacket? before = await jpeg.ReadXmpAsync();
await jpeg.SaveXmpAsAsync("new-result.jpg", edit);
```

Existing Save/SaveAs methods still take ExifEdit and do not parse/modify XMP.
SaveXmp/SaveXmpAs take XmpEdit and do not rebuild EXIF or touch APP13/IPTC. To apply
both kinds of change, reopen the first result before saving the second; documents
do not rebind after saving. Existing JPEG/EXIF acceptance and safety checks at Open
still apply. No in-place source editing or full metadata/privacy scrub is provided.

XMP reads/saves verify source SHA-256/length before using recorded ranges. The
standard APP1 signature must match exactly, including its NUL. Multiple standard
packets are ambiguous for XMP reads/changes. Any exactly signed Extended XMP
segment, including a truncated header, blocks XMP reads/changes; no incomplete
merged model is returned. A dangling HasExtendedXMP reference also blocks reads
and property edits, and cannot be introduced by Set. Whole standard-packet removal
can remove a dangling reference only if there are no extension segments.

An empty XmpEdit preserves even duplicate, extended or malformed XMP byte-for-byte;
it does not imply those packets are readable. A unique malformed/unsupported
standard packet may be removed whole when no extensions exist. Missing property
removal on an absent packet stays an exact copy. Whole removal cannot mix with
addressed operations, and last operation per expanded name wins.

Standard packets up to the approved binary capacity **65,504 bytes** are supported;
65,502/65,503/65,504 boundaries have authored tests and independent reads by both
pinned tools. Oversized output fails with XmpTooLarge before destination writes.
General XMP budgets can fail with LimitExceeded earlier. JPEG operations also
account for EXIF plus XMP logical payload bytes against MaxRetainedMetadataBytes.
This is not an exact managed-memory accounting or allocation-performance claim.
New insertion precedes SOF. Replacement preserves an existing late packet's physical
location, with the conformance caveat in the normative review.

## Failure and cancellation contract

Borrowed output streams must be writable, seekable, empty and positioned at zero.
They stay open; identical JPEG source/destination streams are refused. Distinct
wrappers aliasing storage remain the caller's responsibility. Source mutation or
unsafe/ambiguous/oversize metadata fails during preflight before writes. Subsequent
I/O failure or cancellation may leave partial borrowed output; no rollback is promised.

Path saves publish a new path through a same-directory CreateNew temporary file,
flush/close and non-overwriting File.Move. Existing/competing targets and sources
remain untouched; owned temporary cleanup is best-effort. Cancellation is checked
before publication, not after a successful commit. No universal atomicity or crash
durability guarantee is implied. Do not concurrently use a document, its borrowed
source, output stream or mutable edit. Async methods use actual async stream I/O;
XML/model processing remains bounded synchronous CPU work with cancellation checks.
