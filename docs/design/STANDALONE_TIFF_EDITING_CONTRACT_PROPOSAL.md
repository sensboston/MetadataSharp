# M16: Restricted Standalone TIFF Editing Contract Proposal

Status: **APPROVED by the developer on 2026-11-07; implementation pending.**
Date: 2026-11-07. Developer authorized continuing after M15. This document asks
for the separate public API, preservation strategy and safety approval required
by M16. The developer subsequently explicitly approved this exact proposal,
including retained obsolete bytes, and required a repository-resident recovery
checkpoint before implementation. See docs/CURRENT_STATE.md.

## 1. Predecessor and bounded scope

M15 PR #10 merged as 80dc75e62eff242d2b98bc4b14b3090cf4ea60ce. Its final head
812bd76af3241d479e7b9fff07c9dd6a8197a118 passed runs 42431094052/42431090942:
392 tests/platform, Python 12/12 and required references. Read the M15 normative
review, support matrix and audit first. TiffLayout.IsSupported is accounting
information, not a write authorization or proof about opaque pointers.

M16 proposes addressed edits to a small set of descriptive TIFF fields. Preserve
coded strips/tiles/planes/previews and the active directory graph without image
decoding. No general EXIF/GPS editor, XMP/IPTC/ICC editor, metadata synchronization,
directory creation/deletion, pixel edits, compression conversion, BigTIFF or DNG.
Existing format APIs, EXIF caps and the standalone read-only facade are unchanged.
Do not treat the embedded EXIF serializer as a standalone TIFF writer.

## 2. Proposed strategy: preserve original storage, append a replacement graph

1. Open with the M15 structural reader and captured finite limits. Retain the source
   in a new editing document and capture whole-source SHA-256 and length. Reading
   a layout alone does not establish editing identity. No concurrent source/edit/
   document operations are supported.
2. Empty edits and provably absent-field removals copy the entire original exactly,
   including for safely framed issue-bearing layouts. Identity preflight still
   runs. Invalid TIFF, BigTIFF/DNG and unknown field widths still refuse Open.
3. For any effective edit, require supported M15 image accounting AND the explicit
   M16 field/pointer schema below. Unknown/private fields, MakerNote, opaque metadata
   extensions and unaccounted pointer/integrity schemes refuse. Do not silently
   drop them or claim that preserving their bytes alone proves safety.
4. Clone every reachable IFD into an aligned appended area. Preserve the original
   endian order; emit new directory entries sorted by tag ID. Map every old IFD
   offset to exactly one new IFD offset; preserve shared DAG topology, next-chain
   and SubIFD array order, and Exif/GPS/Interop link roles. Rewrite every active
   directory pointer, including external SubIFD arrays. No active link may point
   back to an obsolete original IFD. Do not create/delete directories or links.
5. Keep all unchanged external non-directory values, strip/tile offset/count arrays,
   coded data and candidate previews at their original absolute file offsets.
   Copy unchanged inline values into the new entries. Append new/changed external
   descriptive values and rebuilt directory-pointer arrays; do not overwrite old
   value slots or reuse apparently free gaps. Compatible shared original values
   stay shared; rebuilding directory-pointer arrays may split their storage alias,
   but must preserve logical target order/sharing. New padding is zero, word aligned.
6. Only header bytes 4–7 change inside the original source-length prefix: point
   to the appended first IFD. All original bytes [0,4) and [8,sourceLength) remain
   identical. The output then contains alignment padding and the appended graph/
   changed values. Validate all planned extents, links, counts, resulting budgets
   and the 32-bit address space before output; do not discover overflow mid-write.
7. Removing the last field of any IFD would make an invalid empty directory: refuse
   UnsupportedFeature before output. Do not insert a dummy tag or delete that node.

**Required compromise:** obsolete IFDs and old metadata bytes remain physically
in the file, unreachable from the new active header/graph. Removal/replacement is
NOT secure erasure, privacy scrubbing, metadata compaction or a guarantee against
forensic/heuristic discovery. Repeated effective saves grow the file. This is an
append-preserve strategy into a *new output*, never in-place appending to a source.
A future compacting rebuild needs separate opaque-pointer/relocation approval.

## 3. Exact proposed public surface

Namespace MetadataSharp.Formats.Tiff. Reuse Core.ExifValue only as the immutable
raw typed value primitive; do not project arbitrary TIFF directory offsets onto
ExifDirectoryKind or claim general EXIF editing. No public base class/interface.

```csharp
public sealed record TiffEditOptions
{
    public TiffReadOptions ReadOptions { get; init; } = new();
    public long MaxOutputBytes { get; init; } = 1_073_741_824;
}
public readonly record struct TiffFieldKey(uint DirectoryOffset, ushort TagId);
public sealed class TiffMetadataEdit
{
    public TiffMetadataEdit();
    public bool IsEmpty { get; }
    public void Set(TiffFieldKey key, ExifValue value);
    public void Remove(TiffFieldKey key);
}
public sealed class TiffMetadataDocument : IDisposable, IAsyncDisposable
{
    public static TiffMetadataDocument Open(string path, TiffEditOptions? options = null);
    public static TiffMetadataDocument Open(Stream source, TiffEditOptions? options = null);
    public static Task<TiffMetadataDocument> OpenAsync(string path,
        TiffEditOptions? options = null, CancellationToken cancellationToken = default);
    public static Task<TiffMetadataDocument> OpenAsync(Stream source,
        TiffEditOptions? options = null, CancellationToken cancellationToken = default);
    public TiffLayout Layout { get; }
    public ExifValue? ReadValue(TiffFieldKey key);
    public Task<ExifValue?> ReadValueAsync(TiffFieldKey key,
        CancellationToken cancellationToken = default);
    public void Save(Stream destination, TiffMetadataEdit edit);
    public Task SaveAsync(Stream destination, TiffMetadataEdit edit,
        CancellationToken cancellationToken = default);
    public void SaveAs(string destinationPath, TiffMetadataEdit edit);
    public Task SaveAsAsync(string destinationPath, TiffMetadataEdit edit,
        CancellationToken cancellationToken = default);
    public void Dispose();
    public ValueTask DisposeAsync();
}
```

Keys use original-source IFD offsets from Layout, not discovery indexes or output
positions. A key must identify an existing reachable directory; malformed keys,
unknown writable IDs and incompatible Set types/counts are argument errors.
Last operation for a key wins. Remove affects only that field, not child directories.
Snapshot edits before awaiting I/O. There is no RemoveAllExif or whole-IFD removal.

ReadValue is limited to the same descriptive tag IDs below, returns null for a
missing field, validates an existing target's supported shape and returns an owned
value. It is not an arbitrary raw TIFF field reader. Verify source identity before
ReadValue and saves; Layout itself is an immutable historical snapshot and does
not perform I/O. Unsupported write layouts may still expose a supported descriptive
ReadValue. Results/Layout survive disposal. A successful Save does not rebind the
document; reopen the new output to obtain its new IFD offsets.

## 4. Exact addressed tags and preserved schema

Set/Remove/ReadValue permit these TIFF baseline descriptive IDs in any existing
reachable IFD, including image IFDs and metadata-only child IFDs:

| IDs | Shape and new-value rule |
| --- | --- |
| 269 DocumentName, 270 ImageDescription, 271 Make, 272 Model, 285 PageName, 305 Software, 315 Artist, 316 HostComputer | ASCII; count equals byte length, at least one byte; exactly one final NUL, preceding bytes printable ASCII 0x20–0x7e; empty text encoded as one NUL |
| 306 DateTime | ASCII, count 20; new value is a valid Gregorian yyyy:MM:dd HH:mm:ss plus NUL, no timezone conversion |
| 274 Orientation | SHORT, count 1, numeric value 1–8; convert the immutable input value to source byte order |

Existing untouched descriptive fields need supported TIFF type/count framing, not
new-text normalization or calendar reinterpretation. Existing edited/read targets
must meet the supported target shape; malformed target content refuses a property
edit/read, but a whole-field Remove may bypass content decoding after framing is
safe. Unsupported/unknown **other** fields still prevent every effective write.
A Set equivalent in type/count/source-order raw bytes is a provable exact no-op;
do not append/reorder solely to set the same value.

The following is the closed preserved-field schema, in addition to the descriptive
IDs and recognized directory pointers. Validate field type/count before allowing
an effective write; merely passing M15.Known is insufficient:

- 254 LONG/1; 255 SHORT/1; 256/257 SHORT or LONG/1.
- 258 SHORT/SamplesPerPixel; 259/262/266/277/284/296/317 SHORT/1;
  280/281 SHORT/SamplesPerPixel; 297 SHORT/2.
- 282/283/286/287 RATIONAL/1. These have no file pointers; copying their raw values
  is not a claim of general resolution/position semantic validation.
- 273/279 SHORT or LONG arrays with M15-derived strip count; 278 SHORT or LONG/1.
  322/323 SHORT or LONG/1; 324 LONG array; 325 SHORT or LONG array, with the
  M15-derived tile count. No changes to their numeric values or original storage.
- 338 SHORT/extra-sample count consistent with the supported alpha layout;
  339 SHORT/SamplesPerPixel. Apply the M15 sample/plane/compression restrictions.
- 330 LONG/nonempty array; 34665/34853/40965 LONG/1; structural next pointers
  remain four bytes. Rebuild directory links only through the old-to-new map.
- 513/514 LONG/1 matched candidate preview extent, subject to M15 overlap/size
  rules. Preserve both their values and the preview bytes/absolute offset.

Everything else refuses effective edits, including ColorMap 320, JPEGTables 347,
old JPEG 6 and 519/520/521 pointers, unknown/private tags, MakerNote 37500,
XMP 700, IPTC 33723, ICC 34675, DNG/GeoTIFF extensions and checksum/integrity tags.
This deliberately refuses some viewable/M15-accounted TIFFs. New-style JPEG 7
without an external JPEGTables field remains an accounting-only candidate: no
entropy validity or rendering guarantee. A future broader schema needs approval.

## 5. Limits, I/O and failure semantics

Capture/validate options before source I/O. Limits remain finite/positive; inherit
all M15 graph/value/segment/range limits. Individual value caps do not cap the
whole TIFF at 65,527 bytes. Count original bookkeeping plus appended metadata plan,
edit values and pointer maps against MaxRetainedMetadataBytes before allocation/
growth; document a conservative logical formula, not an exact allocation claim.
No image-sized allocation or pixel read is needed for layout planning; whole-source
hashing/copying deliberately streams coded bytes, unlike the read-only M15 reader.

Effective output size must fit all of: MaxOutputBytes, ReadOptions.Limits.MaxInputBytes
(so it can be reopened under captured limits), and the classic 4-GiB profile. Every
emitted pointer must fit uint and every extent fit that output. No silent BigTIFF
promotion. An exact copy must also satisfy the configured output ceiling.

Borrowed source: readable/seekable/at zero, remains open; path-opened source is
owned. Output: distinct, writable/seekable, empty/at zero; remains open. Distinct
wrappers aliasing storage are the caller's responsibility. SHA-256/length preflight
is not a filesystem lock or protection against mutation after preflight. Actual
async stream I/O and bounded cooperative cancellation; no Task.Run facade.

SaveAs: new path only, same-directory CreateNew temporary, flush/close, then move
without overwrite. Preserve source/existing/race-winner target; best-effort cleanup
on ordinary failure/cancellation. No cancellation reported after publication.
Preflight semantic/identity/schema/limit failures before output writes. I/O failure
or cancellation during emission may leave partial caller-owned stream output.
No in-place save, overwrite, universal atomicity or crash-durability guarantee.

Reuse InvalidTiff for malformed source framing; SourceChanged for failed identity;
LimitExceeded for configured/address-space ceilings; UnsupportedFeature for valid
outside-profile layouts/shapes or an operation that empties an IFD; UnsafeRelocation
for opaque/private/unproven pointer schemes. Do not add an enum member unless
review proves necessary and the developer approves it. Preserve ordinary I/O and
cancellation exceptions; duplicate source tags remain M15 InvalidTiff, not a
silently selected target. No partial output published for preflight failures.

## 6. Required review and acceptance before completion

After approval, review exact TIFF6 field/type/directory placement prose and approved
Exif/SubIFD link sources, recording immutable provenance/sections. Resolve any
necessary profile/safety/API change with the developer before implementation.

Author independent synthetic fixtures; no GPL codec/test translation or imported
camera/private corpus. Require both byte orders, strip/tile, chunky/separate planes,
all approved accounting compressions, shared DAGs/next chains/metadata child links,
previews, original unsorted IFDs, shared values, changed inline/external boundaries,
absent/same-value no-ops and source-relative key errors. Validate every new link,
IFD ordering/count, endianness, alignment and output ceiling. Verify the full
original prefix equality except bytes 4–7, plus unchanged coded extents and pointer
values. Demonstrate obsolete removed values remain physically present: no privacy
claim. Test all mandatory schema/private/MakerNote refusals, last-field removal,
resource/overflow boundaries, source mutation, actual short async I/O, cancellation,
partial output, path races and temporary cleanup. Use sparse/synthetic boundary
streams instead of committing multi-gigabyte artifacts.

All-platform xUnit and required independent CLI facts/reverse ingress, plus
independent output-pointer/range checks, must pass on the final head. CLI tools are
black boxes, not absolute truth; classify unsupported operations without widening
warnings to pass. No performance/whole-format conformance or publication claim.
Stop after the contract proposal for approval. M17 remains DNG feasibility/planning
only, not a license to implement DNG or extend this writer.
