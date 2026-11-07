# M00: Initial Scope and Contracts Proposal

Status: **APPROVED for the initial release scope**. Approved: 2026-11-07.
The developer explicitly approved all seven decisions in section 8. See
[accepted tradeoffs](../decisions/INITIAL_RELEASE_ACCEPTED_TRADEOFFS.md).
This is a design document, not implemented or compiled API. All signatures below
are approved design declarations; bodies and namespace imports are intentionally omitted.
No approval of this document authorizes work beyond the requested next unit.

## 1. Release boundary

Target .NET 8 and C# 12, with no native runtime dependencies. Use two library
projects, MetadataSharp.Core and MetadataSharp.Formats, and xUnit test projects.
Keep binary readers and layout machinery internal; expose the small data model
and JPEG facade described here. Do not expose implementation-specific write plans.

The M09 release candidate reads and edits EXIF in a restricted JPEG subset. It can:

- Read raw classic TIFF/EXIF entries in both byte orders.
- Set or remove approved ordinary tags in IFD0, Exif, GPS and Interop directories.
- Create the required known directory links automatically.
- Preserve an existing supported JPEG thumbnail without editing it.
- Insert EXIF into a supported JPEG that has none.
- Explicitly remove the complete EXIF segment, including its thumbnail/MakerNotes.
- Copy an unchanged supported file byte-for-byte.

It does not edit pixels, XMP, IPTC, MakerNotes, arbitrary pointer tags or thumbnails.
No metadata synchronization, privacy scrub guarantee, in-place overwrite, BigTIFF,
standalone TIFF/DNG editing, Extended XMP editing, or broad vendor compatibility.
Removing EXIF is not removing all metadata: XMP, IPTC, COM and other blocks remain.

### JPEG acceptance subset

Accept only a single image starting at byte zero with SOI, an 8-bit baseline DCT
SOF0 frame with nonzero dimensions and 1 or 3 components, one SOS covering all
frame components, and a terminating EOI at end of input. Parse marker grammar
through EOI, including the entropy-coded portion; never stop validation at SOS.
Validate frame/scan lengths, component references, baseline SOS parameters,
length-bearing segment ranges and the structural ordering used by this subset.
No decoding or validation of Huffman-coded pixel correctness is promised.

During entropy traversal, handle FF00 stuffing, fill bytes and RST0–RST7 markers
without treating embedded bytes as APP markers. Validate restart marker structure
against DRI where possible without decoding; do not claim MCU interval validation.
Reject a second SOS, another SOF, post-scan APP/COM segments, DNL, arithmetic or
hierarchical modes, unsupported standalone markers, missing EOI and trailing bytes.
Only supported table/control markers, APP0–APP15 and COM may occur before SOS;
unsupported marker kinds fail explicitly. Full marker allowlist/ordering tests must
be checked against the normative sources before M07 implementation.

APP data is opaque except a pre-SOS APP1 beginning with exact `Exif\0\0` bytes.
An APP1 without this signature is not EXIF. More than one matching EXIF segment
is an ambiguity error, even if their payloads match. APP lengths include their two
length bytes: maximum payload is 65,533 bytes; maximum TIFF bytes after the
six-byte EXIF signature are **65,527**. Never truncate or split oversized EXIF.

Replacement uses the original EXIF position; removal deletes just that segment.
Insertion is immediately after contiguous leading APP0 segments (or after SOI if
none). Preserve every other source range and its relative ordering.

## 2. Public data model (MetadataSharp.Core)

```csharp
public enum Endianness { LittleEndian, BigEndian }
public enum ExifDirectoryKind { Ifd0, Exif, Gps, Interop, Thumbnail }
public enum TiffFieldType : ushort
{
    Byte = 1, Ascii = 2, Short = 3, Long = 4, Rational = 5,
    SByte = 6, Undefined = 7, SShort = 8, SLong = 9, SRational = 10,
    Float = 11, Double = 12
}
public readonly record struct ExifTagKey(ExifDirectoryKind Directory, ushort TagId);

public readonly record struct UnsignedRational(uint Numerator, uint Denominator)
{
    public bool TryGetDouble(out double value);
}
public readonly record struct SignedRational(int Numerator, int Denominator)
{
    public bool TryGetDouble(out double value);
}

public sealed class ExifValue
{
    public TiffFieldType FieldType { get; }
    public uint Count { get; }
    public Endianness ByteOrder { get; }
    public int ByteLength { get; }
    public static ExifValue FromRaw(TiffFieldType fieldType, uint count,
        Endianness byteOrder, ReadOnlySpan<byte> bytes);
    public void CopyRawTo(Span<byte> destination);
}
public sealed class ExifEntry
{
    public ExifTagKey Key { get; }
    public ExifValue Value { get; }
}
public sealed class ExifMetadata
{
    public Endianness ByteOrder { get; }
    public IReadOnlyList<ExifEntry> Entries { get; }
    public bool TryGetEntry(ExifTagKey key, out ExifEntry? entry);
}
public sealed class ExifEdit
{
    public ExifEdit();
    public bool IsEmpty { get; }
    public void Set(ExifTagKey key, ExifValue value);
    public void Remove(ExifTagKey key);
    public void RemoveAllExif();
}
```

Raw values own copied bytes. No public Memory/Span view exposes mutable backing
storage; CopyRawTo requires adequate destination space and copies exactly
ByteLength bytes. Entries and collection snapshots are immutable. Metadata remains
readable after the source document is disposed. FromRaw verifies known type,
checked count-times-width and exact byte length; it never accepts arbitrary enum
casts. Zero-count fields are retained on read but not accepted for new edits.
Raw data includes tag values, not directory-entry padding or external alignment.

Rationals preserve the original pair without reduction. Zero denominators are
readable; TryGetDouble returns false and sets value to zero, never throws or emits
infinity. Signed extreme pairs are converted without integer division/overflow.
Use two explicit types instead of generic Rational<T> to avoid unsupported generic
numeric combinations; human exposure/GPS formatting is deferred.

Byte order belongs to a value, so raw bytes cannot be accidentally interpreted as
host-endian. Setting a value with a different byte order converts numeric elements
(including each rational component); byte/ASCII/undefined data is copied unchanged.
Float/double values preserve their bit patterns when swapping byte order.
Existing EXIF keeps its order; new EXIF defaults to LittleEndian.

### Graph and tag rules

Read IFD0, Exif, GPS, Interop and one thumbnail IFD through their documented links.
Only one directory of each role is accepted; duplicate tag IDs in one directory,
cycles, shared directory offsets, unsupported links and ambiguous overlaps fail.
Zero optional link offsets mean absent directories. A missing required value range
is malformed. A nonzero next-IFD link beyond the thumbnail is unsupported.

Read TIFF field types 1–12; unknown IDs with known types retain their exact raw
values. An unknown field type fails with UnsupportedFeature (its width cannot be
invented). Read MakerNotes as opaque Undefined data. Supported thumbnail layout is
one JPEG interchange stream with validated offset/length inside the EXIF payload;
no nested thumbnail decoding and no strip/tile thumbnail writing.

The serializer manages Exif/GPS/Interop links and thumbnail offset/length fields.
Set/Remove cannot target these or any Thumbnail directory entry. It may create
missing Exif/GPS/Interop directories and links, but does not prune an existing
empty directory automatically. Retain all unedited ordinary values, directory
roles, and a supported thumbnail; entry ordering/padding may change on rebuild.

M03/M04 must record a small independently sourced tag schema with directory, type,
count constraints and relocation classification. Proposed writable initial set:
IFD0 ImageDescription (0x010E), Make (0x010F), Model (0x0110), Orientation (0x0112),
Software (0x0131), DateTime (0x0132), Artist (0x013B), Copyright (0x8298);
Exif ExposureTime (0x829A), FNumber (0x829D), DateTimeOriginal (0x9003),
DateTimeDigitized (0x9004), FocalLength (0x920A);
GPSVersionID (0x0000), GPSLatitudeRef (0x0001), GPSLatitude (0x0002),
GPSLongitudeRef (0x0003), GPSLongitude (0x0004), GPSAltitudeRef (0x0005),
GPSAltitude (0x0006). Verify exact constraints from CIPA before implementation;
adding a writable tag requires reviewed constraints, not copying a bulk dictionary.
ASCII edits require 7-bit bytes and terminating NUL, without automatic transcoding.
GPS edits are independent raw tag changes, not an automatic coordinate validator.

Known but non-writable ordinary tags may be retained only if their relocatability
has been documented. Unknown IDs, MakerNotes, unsupported pointer families or
unclassified Undefined payloads make a nonempty rebuild unsafe. The initial policy
is deliberately conservative: **refuse the entire rebuild**, even for a same-length
change. Do not implement a fixed-offset patch exception in this release.
Read and byte-identical no-op remain possible. Explicit RemoveAllExif is allowed:
it intentionally discards the entire EXIF block rather than pretending to preserve
it. This exception does not authorize dropping unknown tags during ordinary edits.

Edit mutations are ordered: last Set/Remove wins for the same key. Set validates
writable-key and value constraints; Remove rejects protected structural keys and
unreviewed keys. RemoveAllExif must be the sole edit operation (combining it with
Set/Remove throws ArgumentException without mutating the edit). Removing a missing
ordinary key is idempotent. An empty edit is byte-identical copy; a nonempty edit
whose result happens to match the source need not be treated as empty and still
runs the safety checks. Snapshot edits when save starts; concurrent mutation or
concurrent use of a document is not supported.

## 3. Public facade (MetadataSharp.Formats)

```csharp
public sealed record MetadataLimits
{
    public long MaxInputBytes { get; init; } = 1_073_741_824;
    public int MaxSegments { get; init; } = 4_096;
    public int MaxRetainedMetadataBytes { get; init; } = 1_048_576;
    public int MaxDirectories { get; init; } = 16;
    public int MaxDirectoryDepth { get; init; } = 8;
    public int MaxEntriesPerDirectory { get; init; } = 512;
    public int MaxTotalEntries { get; init; } = 2_048;
    public int MaxValueBytes { get; init; } = 65_527;
    public int MaxThumbnailBytes { get; init; } = 61_440;
    public int CopyBufferBytes { get; init; } = 65_536;
}
public sealed record JpegReadOptions
{
    public MetadataLimits Limits { get; init; } = new();
}
public sealed class JpegMetadataDocument : IDisposable, IAsyncDisposable
{
    public ExifMetadata? Exif { get; }
    public static JpegMetadataDocument Open(string path,
        JpegReadOptions? options = null);
    public static JpegMetadataDocument Open(Stream source,
        JpegReadOptions? options = null);
    public static Task<JpegMetadataDocument> OpenAsync(string path,
        JpegReadOptions? options = null, CancellationToken cancellationToken = default);
    public static Task<JpegMetadataDocument> OpenAsync(Stream source,
        JpegReadOptions? options = null, CancellationToken cancellationToken = default);
    public void Save(Stream destination, ExifEdit edit);
    public Task SaveAsync(Stream destination, ExifEdit edit,
        CancellationToken cancellationToken = default);
    public void SaveAs(string destinationPath, ExifEdit edit);
    public Task SaveAsAsync(string destinationPath, ExifEdit edit,
        CancellationToken cancellationToken = default);
    public void Dispose();
    public ValueTask DisposeAsync();
}
```

Open validates the complete accepted container and parses its EXIF. Malformed or
unsupported EXIF blocks fail Open, including for RemoveAllExif; a recovery/salvage
API is deferred. A successful Open does not guarantee that every edit is safe.

All limits must be positive and validated before I/O. CopyBufferBytes is restricted
to 4 KiB–1 MiB. Checked calculations and format maxima apply regardless of larger
configured limits. MaxRetainedMetadataBytes counts source metadata payload buffers
plus staged EXIF output, not opaque APP payloads streamed unchanged. Entry and
directory limits bound object growth separately. No complete image buffering.
This is not a hard process-RSS cap; pooled capacity and runtime overhead may exceed
requested bytes. No XML/decompression happens in this release: corresponding limits
must be added under M10/M13 contracts, not invented as unused options now.

### Stream ownership and identity

- Source must be readable and seekable; Position must be zero. Length bounds the
  complete image. Nonseekable input is rejected rather than silently spooled.
- A stream overload always borrows the source; Dispose never closes it. A path
  overload owns its opened source and closes it on dispose or failed open.
- Source Position is unspecified after any operation; do not promise restoration.
  The caller must not modify or otherwise use the source until disposal.
- A document holds its source, not a full-image snapshot. Save preflight checks
  length and compares a full-source SHA-256 against the digest captured on Open.
  Cancellation-aware streaming hashing is bounded by input and buffer limits.
- Preflight detects ordinary between-operation modifications, not concurrent
  mutation races. The no-concurrent-mutation rule is a caller precondition; file
  sharing restrictions help on some platforms but do not establish a universal lock.
- Destination stream must be writable, seekable, empty, and at Position zero. It
  remains open; Position is at end after success. Nonseekable output is deferred.
- Reject ReferenceEquals(source, destination). Different wrappers/handles to the
  same underlying storage cannot be detected portably: non-aliasing is an explicit
  caller obligation, not a claimed guarantee. Never truncate an unknown destination.
- New objects receive defensive copies of option values; later option/collection
  mutation cannot alter an open document. Dispose is idempotent. Other operations
  on a disposed document throw ObjectDisposedException.

Save has no persistent mutation effect on the document. Multiple sequential saves
use the original metadata and source. A successful Save does not rebind the document
to its output. Use Open on the output to begin a new edit session.

### Synchronous, asynchronous and cancellation boundaries

Provide sync and async I/O entry points; async uses actual stream ReadAsync/
WriteAsync, not Task.Run wrappers. Pure bounded parsing/planning stays synchronous.
Async operations check cancellation before I/O, between bounded scan/hash/copy
chunks and before path publication. Standard synchronous methods have no token.
A cancellation arriving after successful path publication does not roll it back;
once publication completes, return success. Cancellation cannot undo stream writes.

### Save phases and failure guarantees

1. Validate arguments and destination preconditions without writing.
2. Revalidate source identity, edit snapshot, safety, sizes and layout; prepare
   the bounded EXIF payload. Unsupported or malformed preflight leaves destination
   bytes unchanged (its position and the source position are not guaranteed).
3. Stream original ranges and replacements. I/O failures or cancellation can leave
   partial caller-owned output; do not promise rollback, truncation or durability.

SaveAs creates a unique temporary file in the destination directory, completes and
closes it, then publishes without overwriting an existing destination. Existing
paths are errors; no overwrite option or source replacement in this release. This
also rejects a destination equal to the source path. Publication must fail if a
competing writer creates the destination first. Do not assume normalized paths
identify all hard links; requiring a previously nonexistent destination avoids
intentional same-file overwrites. Concurrent directory/symlink attacks are outside
this managed path contract; callers must use a trusted destination directory.

Attempt temporary-file cleanup on failure without masking the original exception;
cleanup is best effort and leftovers may require caller removal. Never delete a
preexisting destination. Source bytes remain untouched by the library. Same-directory
publication reduces exposure but universal atomicity, filesystem transactions,
fsync/crash durability and preservation of ACLs/timestamps are not promised.

## 4. Failure model

```csharp
public enum MetadataErrorCode
{
    InvalidJpeg, InvalidExif, UnsupportedFeature, LimitExceeded,
    AmbiguousMetadata, UnsafeRelocation, ExifTooLarge, SourceChanged
}
public sealed class MetadataException : Exception
{
    public MetadataErrorCode Code { get; }
    public long? SourceOffset { get; }
}
```

Library-created MetadataException instances carry stable codes and optional
absolute source offsets; consumers do not parse English Message strings. For EXIF
errors translate TIFF-relative offsets to JPEG source offsets. Exceptions while
constructing a new value have no source offset. Keep exception constructors internal
for now; serialization/custom subclassing is not a supported API.

- Bad caller arguments/options/capabilities: ArgumentException (including the
  standard ArgumentNullException/ArgumentOutOfRangeException variants).
- Malformed data: InvalidJpeg or InvalidExif; unsupported legal structures/types:
  UnsupportedFeature; duplicate EXIF/tags: AmbiguousMetadata.
- Resource budget breach: LimitExceeded before allocation/traversal beyond it.
- Unsafe ordinary edit: UnsafeRelocation; oversized APP1 result: ExifTooLarge.
- Source digest/length mismatch at preflight: SourceChanged.
- Operating-system/stream failures: preserve IOException, UnauthorizedAccessException
  and other applicable platform exceptions; cancellation: OperationCanceledException.
- No incidental IndexOutOfRangeException/OverflowException for hostile input.
  Unexpected implementation bugs are not caught and relabeled as successful reads.

No public TryRead or diagnostic collection is proposed for this first release.

## 5. Preservation contract

- Empty edit: entire supported source file is copied byte-for-byte.
- Ordinary accepted edit: every byte outside the addressed EXIF APP1 segment is
  preserved in order. Only its length/content may change; new insertion adds one
  APP1 at the defined location. Encoded scan bytes, SOS/EOI, tables and unrelated
  metadata therefore remain byte-identical.
- Within rebuilt EXIF: preserve unedited supported values, byte order, known
  directory links and thumbnail bytes/meaning, but not original padding, offsets,
  entry order, unreferenced slack or byte-identical TIFF representation.
- Refuse rebuilding opaque/unclassified data rather than claiming that equal bytes
  preserve its meaning. Explicit complete removal has intentionally different semantics.

No pixel decoder participates in production code or preservation checks. Tests
compare scanner-defined source ranges and independent expected bytes, not merely
our parser's successful re-read of our writer's output.

## 6. Acceptance examples for subsequent stages

| Scenario | Required observable outcome |
| --- | --- |
| II and MM EXIF containing Orientation=1 | Same typed/raw value in declared source order; orientation edit to 6 reads correctly in an independent tool |
| Unknown ID with known SHORT type | Raw bytes retained on read; empty edit identical; ordinary rebuild refused |
| Unknown TIFF field type | UnsupportedFeature before guessing payload width |
| MakerNote plus Artist edit, even equal length | UnsafeRelocation; destination still empty |
| Same file with RemoveAllExif only | EXIF APP1 absent; other APP1/APP13, scan and thumbnail-external data unchanged |
| JPEG without EXIF, Set Artist | One new EXIF APP1 at insertion position; untouched ranges identical |
| Two EXIF APP1 segments or duplicate IFD tags | AmbiguousMetadata; no first-wins behavior |
| Progressive JPEG, second scan or post-scan APP1 | UnsupportedFeature; no partial discovery claim |
| FF00 or restart bytes in scan | Not mistaken for a metadata segment; raw scan bytes unchanged |
| Missing EOI, bad segment length or TIFF cycle | Structured InvalidJpeg/InvalidExif failure, bounded work |
| Entry count exceeds configured total or value size product overflows | LimitExceeded or InvalidExif as appropriate, before allocation |
| Rational 1/0 or signed minimum/-1 | Pair preserved; zero-denominator conversion fails predictably; no integer overflow |
| Generated TIFF is 65,527 vs 65,528 bytes | First fits APP1 if otherwise valid; second fails ExifTooLarge without writes |
| Empty edit | Full source/output hashes equal, including EXIF padding |
| Source changes between Open and Save | SourceChanged before output writes |
| Same stream object passed for source/output | ArgumentException without writing |
| Distinct source/output wrappers alias storage | Documented caller-contract violation; no false promise of detection |
| Short reads, cancellation, disk-full simulation | Correct loop behavior; cancellation/I/O surfaced; stream output may be partial |
| Existing SaveAs destination or racing creator | No existing file overwritten; source unchanged |
| EXIF object inspected after document disposal | Metadata values still accessible; Save on disposed document fails |

Use minimal generated fixtures with provenance manifests. Validate numeric outputs
against standalone pinned Exiv2 and ExifTool in optional integration jobs; no GPL
source or unknown-rights fixtures. External tools do not override specifications.

## 7. Normative verification register

No standards were fetched or clause texts verified during M00. The following are
**candidate references to verify**, not citations to reviewed normative text:

| Source/edition candidate | Sections/topics to verify before implementation |
| --- | --- |
| TIFF Revision 6.0 (1992) | Header, IFD entry/value layout, field types, byte order/alignment, next-IFD and JPEGInterchangeFormat/Length; exact section labels pending |
| CIPA DC-008 Exif 2.32 | JPEG APP1 EXIF signature, TIFF base, directory links, tag types/counts, thumbnail rules; exact publication identifier and section numbers pending |
| ITU-T T.81 / ISO/IEC 10918-1 | Annex B marker/scan syntax, Annex F baseline constraints, stuffing, restart/fill rules; edition and clauses pending |
| JFIF specification | Leading APP0 placement and compatibility of EXIF insertion; exact version/sections pending |

The M02–M08 owners must verify relevant normative text before coding assumptions
and record exact editions/sections and lawful source links in the design record.
Where access is unavailable, report a blocked assumption rather than fabricate
citations or use Exiv2 implementation as a substitute. TIFF type support does not
imply every type is valid in every Exif tag.

## 8. Approved decisions

The developer approved these explicit tradeoffs on 2026-11-07:

1. Baseline, single-scan JPEG only; structural full-file scan, not pixel validation.
2. Strict read errors; no salvage API or malformed-EXIF removal path yet.
3. Conservative write refusal for MakerNotes/unknown or unclassified tags, except
   byte-identical empty edits and explicit complete EXIF removal.
4. Seekable sources/destinations, borrowed-stream document lifetime, and a full
   source digest on Open and before Save (extra I/O in exchange for stale-source detection).
5. SaveAs to a new path only; no overwrite/in-place/replace-source API initially.
6. The writable tag subset, raw value model, two rational types, and exception model.
7. The proposed finite defaults and core/formats boundary.

Open technical verification: normative clause details, complete reviewed tag schema
and exact JPEG allowlist remain explicit gates in their owning units, not permission
to implement based on guesses. If normative review contradicts this proposal,
return for approval before changing observable contracts.

**M00 approval gate is closed. M01 is eligible when explicitly requested.**
