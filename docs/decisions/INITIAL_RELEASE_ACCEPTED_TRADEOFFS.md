# Initial Release: Accepted Tradeoffs and Safety Boundaries

Status: **Accepted by the developer on 2026-11-07** (M00).
Implementation status: **not implemented**.

The developer explicitly approved the seven decisions in the
[initial contracts](../design/INITIAL_CONTRACTS.md), section 8. This record makes
those compromises easy to find; exact signatures and operational semantics remain
in the contracts. Acceptance is not evidence of implementation or conformance.

## 1. Baseline Single-Scan JPEG Instead of General JPEG Support

**Decision:** Support only the documented 8-bit SOF0, single-scan subset. Traverse
the entire structure through EOI, including entropy-data marker handling. Reject
progressive, multiple-scan, trailing-data and other excluded layouts.

**Reason:** Avoid claiming safe discovery/removal from a first-SOS-only scan while
keeping the first writer bounded and testable.

**Cost:** Many otherwise valid JPEGs will be rejected. Structural acceptance is not
proof that the compressed image decodes correctly.

**Revisit when:** A separately approved scanner expansion has normative references,
independent multi-scan fixtures, and image-range preservation tests.

## 2. Strict Reads Instead of Metadata Recovery

**Decision:** Malformed or unsupported EXIF fails Open, even if the intended action
would be complete EXIF removal. Reject duplicate EXIF blocks and duplicate IFD tags.

**Reason:** Ambiguous or damaged input must not be silently interpreted as a safe
editable document.

**Cost:** The initial API cannot repair damaged photos or strip arbitrary broken EXIF.

**Revisit when:** A separate recovery contract defines ambiguity, raw-block access,
error reporting, and what destructive actions the caller explicitly authorizes.

## 3. Refuse Opaque Relocation Instead of Risking MakerNotes

**Decision:** Refuse ordinary EXIF rebuilding in the presence of MakerNotes, unknown
tag IDs, unclassified payloads or unsupported pointers. Same-length edits are not
an exception. Permit read access, byte-identical empty edits, and explicit complete
EXIF removal on otherwise accepted files.

**Reason:** Copying a BLOB does not preserve the meaning of internal offsets.

**Cost:** Editing real camera files may often be refused. The supported writable
set is intentionally small and must be checked against normative tag constraints.

**Guarantee boundary:** RemoveAllExif intentionally removes the whole block,
including its MakerNotes and thumbnail. It does not remove XMP/IPTC/COM or guarantee
privacy sanitization. Ordinary edits must not silently drop unknown data.

**Revisit when:** An independently documented relocation scheme or fixed-layout
patch strategy has explicit contracts and vendor-specific regression evidence.

## 4. Seekable Streams and Digest Checks Instead of Hidden Spooling

**Decision:** Require seekable input/output streams. Borrow caller streams, own
path-opened sources, retain the source for later saves, and own copied metadata
values. Hash the complete source at Open and before Save.

**Reason:** Bounded memory, deterministic range copying, and detection of ordinary
between-operation source changes without buffering an entire photograph.

**Cost:** Additional full-file reads; no nonseekable streaming API; the caller must
keep the source alive and immutable until document disposal.

**Guarantee boundary:** Hashing does not prevent concurrent mutation. Distinct
wrappers over aliased storage cannot be detected universally; non-aliasing and
exclusive source use remain caller obligations.

**Revisit when:** A separate source-snapshot/spooling or streaming contract defines
resource budgets, cleanup, identity guarantees, and measurable performance costs.

## 5. New-Path SaveAs Instead of Overwrite or In-Place Editing

**Decision:** SaveAs writes a temporary file in the destination directory and
publishes only to a previously nonexistent destination. No overwrite flag, source
replacement or direct in-place editing in the initial release.

**Reason:** Protect the source and preexisting files while keeping failure semantics
clear. Caller-stream output may be partial after cancellation or I/O failure.

**Cost:** Callers manage replacement workflows themselves. Temporary-file cleanup
is best effort; filesystem atomicity/crash durability is not universally promised.

**Revisit when:** A platform-aware replacement API has approved race, backup,
permissions, cancellation and failure semantics with platform test coverage.

## 6. Small Raw API Instead of Broad Convenience or Synchronization

**Decision:** Expose immutable raw EXIF values, explicit byte order, an ExifEdit
command set, a small reviewed writable tag set, two concrete rational types, and
stable exception codes. Preserve rational pairs, including zero denominators.
Edit EXIF independently of XMP/IPTC. Do not expose internal layout plans.

**Reason:** Establish binary fidelity and predictable failures before convenience
formatting, vendor interpretation or cross-schema policy.

**Cost:** More manual value construction, restricted writable tags, no automatic
GPS consistency or caption/date synchronization, and no generic Rational<T> API.

**Revisit when:** Concrete use cases justify additional helpers without losing raw
fidelity, with independently reviewed tag constraints and public API approval.

## 7. Finite Resource Defaults and Separate Core/Formats Projects

**Decision:** Use the exact finite defaults in the initial contracts: 1 GiB input,
4096 JPEG segments, 1 MiB retained metadata payload buffers, 16 directories, depth 8,
512 entries per directory, 2048 total entries, 65,527-byte values, 60 KiB thumbnails,
and a 64 KiB copy buffer. Keep Core and Formats separate; use .NET 8/C# 12 and xUnit.

**Reason:** Bound hostile-input work while separating reusable TIFF mechanics from
container-specific rules. Format maxima still override larger configured budgets.

**Cost:** Valid but unusually large inputs may be rejected. These budgets are not a
hard process-memory cap; runtime objects and pool capacity add overhead.

**Revisit when:** Reproducible supported workloads justify changed defaults or
project boundaries. XML/decompression limits belong to later format contracts.

## Cross-Cutting Licensing and Test Policy

The developer also accepted the [development policy](../DEVELOPMENT.md): independent
standards-based implementation, no GPL source/test translation, optional standalone
Exiv2/ExifTool comparison, and fixture-by-fixture provenance review. A private
repository does not waive copyright or distribution obligations. Tool output is
not automatically GPL, but may contain protected or private input material.

## Changing an Accepted Decision

Propose the change before implementation, explain the preservation and compatibility
impact, obtain developer approval, and update this record and the initial contracts
together. Keep historical rationale. Do not quietly broaden support or relax a
safety refusal to make tests pass. M00 approval does not verify normative clauses;
the verification register in the contracts remains binding for later units.
