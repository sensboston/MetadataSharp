# M13–M14: PNG and WebP Metadata Contract Proposal

Status: **APPROVED by the developer on 2026-11-07.**
Date: 2026-11-07. The developer approved grouping PNG and WebP into one PR, with
separate implementation and successful CI gates, then explicitly approved the
signatures/safety profile below. Normative review remains required.

## 1. Scope, dependencies and implementation gates

1. Approve these contracts and review exact normative PNG/WebP editions/sections;
   record source/hash/provenance and reconcile any specification discrepancies.
   If review requires changing these contracts, request approval before implementation.
2. M13: PNG structure/CRC and addressed eXIf/XMP iTXt implementation, independent
   tests and required cross-platform CI. Do not start M14 before that gate passes.
3. M14: static WebP RIFF/VP8X EXIF/XMP implementation, independent tests and required
   cross-platform/external validation. Final group audit/head CI before merging.

No IPTC mapping, text-field editor, image codec, metadata synchronization, standalone
TIFF or DNG implementation. No APNG or animated WebP in this first profile.
Use framework primitives, XML/XMP, cryptography and zlib support; no new production
NuGet/native dependency. Existing external Exiv2/ExifTool pins remain independent
black-box checks, not implementation material. Review support per tool/container;
record unsupported reference operations rather than pretending they ran.

Synthetic PNG pixels/PPM inputs can be authored. For valid WebP test data, a separately
provisioned/pinned cwebp CLI may encode those owned pixels. Before use, record exact
version, distribution license/notices, command and input/output hashes; output rights
come from owned inputs, not an assumption about arbitrary bundled examples. Do not
import third-party codec source/tests or camera images. Ordinary tests remain offline;
required integration cannot silently skip missing tools. No package publication.

## 2. Shared EXIF/XMP boundary and ownership

Reuse current ExifEdit, XmpEdit, raw models, conservative TIFF rebuild policy and
XMP UTF-8/RDF subset. Do not broaden their writable schema, relocation guarantees
or supported encodings. **Keep the current 65,527-byte TIFF reader/rebuild cap**
for both new containers in this group, even though their physical limits are larger.
This deliberate first-profile restriction avoids silently expanding the JPEG-era
core. Larger uniquely identified EXIF can be copied or removed whole when container
structure is safe, but ReadExif/property edits fail ExifTooLarge. A future larger
EXIF core needs its own contract/performance review.

All new EXIF serialization starts at the TIFF byte-order marker, not Exif-NUL-NUL.
PNG eXIf and canonical WebP EXIF output use raw classic TIFF. For WebP input only,
also accept exactly one legacy leading `Exif\0\0` wrapper when followed by a valid
TIFF header; retain original bytes on no-op and strip that wrapper on an effective
EXIF rewrite. Do not heuristically search for embedded TIFF. Normative/independent
reference review must confirm this compatibility policy before implementation.

Open validates complete container structure to EOF, not metadata semantics. EXIF
and XMP are parsed only by their explicit Read/property-edit operations; malformed
or unsupported metadata does not block unrelated-family edits or exact no-op.
A unique malformed target may be removed whole without parsing it. Duplicate target
chunks are AmbiguousMetadata for reads/effective edits, including whole removal;
other-family edits/no-op preserve their original bytes. No arbitrary duplicate merging.

Read methods perform source SHA-256/length preflight then return owned snapshots.
Snapshots survive document disposal; documents do not rebind after save. Empty edit
and missing-key removal alone are exact copies, but only after the structural
validation required by Open. Whole removal bypasses semantic parsing, not CRC,
framing, unknown-chunk safety or duplicate checks. Family edits do not synchronize
EXIF/IPTC/XMP or promise complete privacy scrubbing.

Borrowed source must be readable/seekable at zero and stays open. Path sources are
owned and disposed. Output must be distinct, writable/seekable, empty and at zero;
borrowed output stays open. Distinct wrappers aliasing storage are a caller obligation.
No concurrent use of a document, its streams or mutable edit. Snapshot edits before
awaiting I/O. Async methods use actual async stream I/O and bounded cancellation
checks; metadata processing is bounded CPU work, not an instant-cancellation promise.

Preflight validates source identity, target safety, sizes and resulting metadata
before output writes. Subsequent I/O/cancellation may leave partial caller output.
SaveAs uses a same-directory CreateNew temporary, flush/close and non-overwriting
publication. Preserve existing/competing targets and source, cleanup owned temporaries
best-effort, and never report cancellation after successful publication. No universal
atomicity/crash-durability or zero-allocation guarantee.

## 3. Proposed public API

In `MetadataSharp.Formats.Png`:

```csharp
public sealed record PngReadOptions
{
    public MetadataLimits Limits { get; init; } = new();
    public int MaxCompressedXmpBytes { get; init; } = 1_048_576;
    public int MaxDecompressedXmpBytes { get; init; } = 1_048_576;
}

public sealed class PngMetadataDocument : IDisposable, IAsyncDisposable
{
    public static PngMetadataDocument Open(string path, PngReadOptions? options = null);
    public static PngMetadataDocument Open(Stream source, PngReadOptions? options = null);
    public static Task<PngMetadataDocument> OpenAsync(string path,
        PngReadOptions? options = null, CancellationToken cancellationToken = default);
    public static Task<PngMetadataDocument> OpenAsync(Stream source,
        PngReadOptions? options = null, CancellationToken cancellationToken = default);
    public ExifMetadata? ReadExif();
    public Task<ExifMetadata?> ReadExifAsync(CancellationToken cancellationToken = default);
    public XmpPacket? ReadXmp(XmpReadOptions? options = null);
    public Task<XmpPacket?> ReadXmpAsync(XmpReadOptions? options = null,
        CancellationToken cancellationToken = default);
    public void SaveExif(Stream destination, ExifEdit edit);
    public Task SaveExifAsync(Stream destination, ExifEdit edit,
        CancellationToken cancellationToken = default);
    public void SaveExifAs(string destinationPath, ExifEdit edit);
    public Task SaveExifAsAsync(string destinationPath, ExifEdit edit,
        CancellationToken cancellationToken = default);
    public void SaveXmp(Stream destination, XmpEdit edit);
    public Task SaveXmpAsync(Stream destination, XmpEdit edit,
        CancellationToken cancellationToken = default);
    public void SaveXmpAs(string destinationPath, XmpEdit edit);
    public Task SaveXmpAsAsync(string destinationPath, XmpEdit edit,
        CancellationToken cancellationToken = default);
    public void Dispose();
    public ValueTask DisposeAsync();
}
```

In `MetadataSharp.Formats.WebP`, add `public sealed record WebPReadOptions` with
`public MetadataLimits Limits { get; init; } = new();` and
`public sealed class WebPMetadataDocument : IDisposable, IAsyncDisposable`.
Its exact public method set is the block above with every `PngMetadataDocument`
replaced by `WebPMetadataDocument` and every `PngReadOptions` by `WebPReadOptions`.
There are no compressed-XMP options on WebP. This substitution is documentation
notation, not a new generic base class or public interface. Existing JPEG signatures
remain unchanged; no incidental API unification/refactor in this group.

Capture/validate options; reject nonpositive settings. MetadataLimits.MaxSegments
counts PNG/RIFF chunks, MaxInputBytes bounds the whole file and existing copy/EXIF
budgets still apply. XmpReadOptions constrain explicit XMP reads; save uses default
XmpLimits plus captured container budgets, with smaller applicable limits winning.
Apply MaxRetainedMetadataBytes to logical retained EXIF/XMP and staged metadata,
including decompressed XMP, before exposing output. It is not exact managed-memory
accounting. Do not allocate a declared chunk length merely to skip/copy/check CRC.

Append InvalidPng and InvalidWebP to MetadataErrorCode, without renumbering members.
Use LimitExceeded for configured budgets, ExifTooLarge for the deliberate EXIF cap,
AmbiguousMetadata for target duplicates, UnsupportedFeature for outside-profile
structures/unsafe copies and SourceChanged for failed identity preflight. Reuse
InvalidExif/InvalidXmp for semantic payload errors. Invalid compression/framing of
XMP iTXt is InvalidPng; well-framed unsupported XMP XML remains opaque as today.
Keep argument/cancellation/ordinary I/O exception conventions.

## 4. PNG accepted structural profile

Validate PNG signature, checked chunk lengths/ranges and names/reserved bits,
**every chunk CRC** (including IDAT and unknown chunks), and complete EOF traversal.
No ignore-CRC/best-effort option. IHDR first/unique with 13 bytes; legal nonzero
sizes, color-type/bit-depth combinations, compression/filter/interlace values.
Accept noninterlaced and Adam7 headers without decoding pixels. Enforce PLTE
presence/size/order constraints for the declared color type, one or more contiguous
IDAT chunks, and unique empty final IEND with no trailing bytes. Check known
critical ordering/cardinality; do not claim IDAT zlib, filter-row or pixel correctness.

Reject APNG acTL/fcTL/fdAT explicitly rather than mislabeling it static PNG.
Unknown critical chunks refuse Open. Unknown ancillary chunks can be read/copied:
- safe-to-copy bit set: preserve entire original chunk on metadata edits;
- unsafe-to-copy bit clear: refuse any effective edit, including whole metadata
  removal, rather than dropping/repositioning it. No-op remains byte-identical.
This is deliberately more conservative than rules that permit some ancillary-only
changes. Maintain a reviewed explicit known-chunk allowlist; unfamiliar ancillary
extensions are not automatically declared safe. No color-profile/text reconciliation.

Identify eXIf by chunk type and XMP iTXt by exact Latin-1 keyword
`XML:com.adobe.xmp` followed by NUL. eXIf content is classic TIFF without a JPEG
signature. Read accepted pre/post-IDAT metadata positions only where the reviewed
PNG specification permits; no metadata between contiguous IDAT chunks.
New insertion is before first IDAT (after any required PLTE); replacement stays at
its original permitted position. Only addressed chunk bytes/CRC change. Preserve
all IDAT payloads, their boundaries/order and other permitted chunks exactly.

For XMP reads/property edits, validate iTXt keyword, compression flag/method,
NUL-terminated language/translated-keyword fields and text bounds. First-profile
XMP language and translated-keyword fields must be empty; nonempty fields are
UnsupportedFeature, not silently discarded. Accept uncompressed UTF-8 XMP and
zlib-compressed XMP with method zero under independent compressed-input and
expanded-output caps. Refuse invalid/truncated streams; stop on the first byte over
the expansion budget. Exact compressed-stream termination/trailing-data checks
must be resolved against framework decoder behavior before claiming that guarantee.

New/changed XMP is emitted as **uncompressed** UTF-8 iTXt, compression flag/method
zero and empty language/translated keyword, with a newly computed CRC. Empty edit
preserves existing compression and every byte. Unrelated tEXt/zTXt/iTXt/iCCP data is
preserved opaque; do not decompress it merely to copy it. A uniquely identified XMP
chunk can be removed whole even if its remaining iTXt fields/text are malformed.
No general PNG text or ICC-profile editor in M13.

## 5. WebP accepted structural profile

Validate RIFF/WEBP signatures, exact little-endian RIFF size to EOF, chunk bounds,
zero odd-byte padding and checked sizes. Accept static lossy VP8 and lossless VP8L;
read only the bounded image headers necessary for dimensions/layout conversion.
Do not decode entropy, validate all coded pixels or re-encode a frame.

Simple layout: exactly one VP8 or VP8L image chunk. Metadata insertion promotes it
to extended layout by adding VP8X derived from validated image-header dimensions,
lossless alpha hint where applicable, and required metadata feature flags. Invalid
or unsupported headers fail before output; do not guess dimensions or transparency.
Removing metadata from existing extended layout keeps VP8X; no implicit downgrade.

Extended layout: unique 10-byte VP8X first, reserved bits/bytes validated, valid
canvas/area and bounded frame header dimensions consistent with that canvas.
Validate ICCP/EXIF/XMP presence flags against chunk presence; require supported
ordering, one still image and at most one ICCP/ALPH. Support ALPH only in its
reviewed lossy-still position and preserve its payload. For lossless alpha, treat
the header as a structural hint, not proof of actual transparent pixels; any
additional consistency restriction must be recorded during normative review.

Animation flag or ANIM/ANMF chunks refuse Open. No partial first-frame editor.
Unknown well-framed chunks may survive reads/no-op, but refuse effective edits
because WebP has no PNG-style safe-to-copy bit and this first profile cannot prove
unknown private-data relocation safety. Never drop them. Simple layout plus
unrecognized extra chunks is outside the simple-to-extended conversion profile.

Metadata chunks are exact `EXIF` and `XMP ` FourCCs. Duplicate metadata chunks are
handled per-family as above; presence flags count presence, not a choice of duplicate.
On effective edits update RIFF size, addressed chunk length/padding and only relevant
VP8X metadata bits. Insert/move only the addressed metadata as required by reviewed
canonical ordering; never silently reorder coded image/alpha/ICCP chunks. Rebuild
chunk padding as zero for changed/new metadata. Preserve existing VP8/VP8L/ALPH/ICCP
chunks byte-for-byte, including their boundaries; do not add PNG iTXt or JPEG APP1
wrappers to WebP XMP. XMP is the existing bounded UTF-8 packet format.

## 6. Required acceptance and audit record

PNG: independent known-byte files and CRC vectors, corrupt CRC in every chunk class,
truncated/overflow lengths, IHDR/PLTE/IDAT/IEND ordering, metadata duplicates, APNG
refusal, unknown safe/unsafe chunks, raw eXIf representation/cap, all iTXt fields,
compressed-input/output bounds and IDAT/chunk byte preservation.
WebP: known RIFF bytes, size/padding/reserved/flag/canvas failures, VP8/VP8L headers,
ALPH/ICCP ordering and preservation, simple promotion, existing extended edits,
legacy EXIF wrapper read/canonical output, duplicates, unknown-chunk and animation
refusals, and complete coded-frame-range identity.
Both: empty/missing-removal exact copies, malformed whole-removal policy, family
independence, ownership/short actual-async I/O, mutation/aliasing/cancellation,
partial-output errors and temporary-publication failure/races. Independent fixtures
and pinned CLI factual validation/reverse edits where supported. Record discrepancies,
unsupported tool behavior and remaining release gates without widening warning
allowances just to pass CI. No performance/full-format conformance/publication claim.
