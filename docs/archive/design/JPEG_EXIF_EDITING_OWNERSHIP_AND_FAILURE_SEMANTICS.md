# M08: JPEG EXIF Editing, Ownership and Failure Semantics

## API and Scope

Implemented the M00-approved ExifEdit in MetadataSharp.Core and JpegReadOptions /
JpegMetadataDocument in MetadataSharp.Formats.Jpeg. The facade opens the M07 subset,
parses its sole EXIF block strictly, and exposes an independent immutable snapshot.
No salvage, overwrite, metadata synchronization or privacy-scrubbing API is added.

ExifEdit validates reviewed keys and raw type/count/ASCII constraints before changing
its command set; last Set/Remove wins. Structural/thumbnail and unknown keys are
not editable. Whole EXIF removal cannot mix with tag changes (repeating it alone
is idempotent). Zero-count writes are refused. Missing removal on an image without
EXIF stays byte-identical instead of creating an empty EXIF block.

These are raw tag-shape checks, not date/coordinate/domain semantic validation.
Final-2.32 value semantics remain unverified; do not claim full Exif 2.32 conformance
or enable Exif 3.0 extensions. Numeric endian conversion reverses each numeric
component, including each 32-bit rational member, not the entire rational pair.
Byte/ASCII/Undefined bytes remain unchanged. No normalization of numeric bits.

## Source and Output Lifetime

Borrow stream sources; path sources are owned and disposed on failed open or
explicit document disposal. Metadata remains accessible after disposal; further
Save calls fail. Snapshot options and edit commands; do not use source/document/edit
concurrently. Source and output must be seekable, and output empty at zero. Reject
the identical stream object. Distinct wrappers over aliased storage cannot be
reliably detected and remain forbidden by the caller contract.

Scan all bytes at Open, hash the complete source with SHA-256, then recheck length
and SHA-256 before every save, including empty edits and whole removal. This detects
between-operation changes, not concurrent mutation races. Hash/copy loops are
bounded and handle short reads. Async paths call ReadAsync/WriteAsync; sync paths
use synchronous I/O. No Task.Run. Cancellation is checked before and between
bounded I/O operations, before publication, but never retroactively after a
successful file move. Shared pooled buffers are returned in finally blocks.

## Three Write Paths

- Empty edit: byte-identical copy, bypassing rebuilding even for opaque metadata.
- Explicit RemoveAllExif: remove exactly the recognized EXIF APP1 range; retain
  every other APP1, APP13, COM, coding table and scan byte. It is not a privacy scrub.
- Ordinary edit: apply changes to the original snapshot, run M05 safety planning,
  then M06 serialization before first output write. Unknown source data/MakerNotes
  refuse rebuilding even if a change happens to be same-length or a semantic no-op.

Replace at the old EXIF position; insert after contiguous leading APP0 or SOI.
All other source ranges are copied byte-for-byte in order. Saves do not rebind the
document to output. New EXIF defaults to II; existing byte order is retained.
Budgets cover values/conversions and the staged TIFF output before its allocation.
Format maxima remain binding independently of larger caller budgets.

## Failure Boundaries

Argument, source identity, unsafe edit and oversized layout failures occur before
caller-output writes. Caller streams remain open and may be partial on cancellation
or I/O failure during copying; no rollback/truncation/flush is promised.

SaveAs accepts a new path only in a trusted directory, writes a CreateNew temporary
file in that directory, flushes/closes, then File.Move with overwrite:false. An
existing or racing destination is never deleted/overwritten. Cleanup attempts only
our created temporary file without masking the original I/O/permission error.
Source bytes are never written by the library. Crash durability, universal atomicity,
ACL/timestamp preservation and hostile symlink/directory races are not promised.

## Tests

Insert/replace/remove/no-op tests compare all non-EXIF bytes (which include image
coding tables/SOS/entropy/EOI), not merely parsed values. Tests also cover unknown
and MakerNote refusal versus explicit removal, source hash/length changes, async-only
short reads/writes, pre-cancellation, partial output failure, invalid output/aliasing,
malformed EXIF, exact format overflow, byte-order conversion, path publication,
competing creation and temporary cleanup. All fixtures are authored in this project.
