# Development Policy

## 1. Working agreement

Execute one roadmap work unit at a time. On 2026-11-07 the developer authorized
continuing sequentially without requesting each next stage, and committing, pushing
and merging completed work after successful CI. The developer subsequently
approved one PR for related M07–M09 work (and similar coherent groups), with a
separate commit/progress record and successful CI gate for every included unit.
Merge only after the entire group and final head pass their required checks. Read the repository and current
diff first; preserve unrelated changes. Never bypass a predecessor gate.
If a unit is too large, propose smaller units and stop before implementing them.

Public contracts require developer approval before implementation. M00 approves
only the initial core/JPEG direction, not every future format API. A later unit
that needs new public contracts must first submit a contract-only proposal and
stop. Resume that same unit only after approval.

Each implementation report must state changed files, commands and actual results,
known limitations, and the next eligible unit. Never describe an unrun test as
passing. A missing SDK or external tool is a recorded blocker or skip, not success.
The standing authorization covers commits, pushes and PR merges for completed
roadmap work, not package publication or changes to approved safety/API contracts.

## 2. Preservation guarantees

Distinguish these independently testable properties:

1. Encoded image data remains byte-identical; no decoding or recompression.
2. Untouched metadata and unrelated container structures are preserved.
3. Metadata meaning remains valid even when its physical layout changes.
4. A byte-identical no-op output is guaranteed only if explicitly supported.

Document accepted format subsets. Reject unsupported structures before publishing
output. Default to refusal when a safe write cannot be established. No generic
"best effort" path may silently weaken these guarantees.

### MakerNotes and opaque offsets

Copying an opaque BLOB and updating its outer pointer does not guarantee safety.
Internal offsets may use the TIFF base, MakerNote base, or vendor-specific bases.
Consider this during initial EXIF layout design, not as a final-stage patch.

Preserve original placement only when all relevant assumptions can be validated.
Permit relocation only for an independently documented supported scheme. Otherwise
refuse edits requiring relocation or other changes that could invalidate the block.
Unknown tags must retain their ID, type, count, and raw payload where interpretable;
unknown field types must not be assigned invented widths. Unknown pointer-bearing
payloads cannot automatically be considered safely relocatable. An explicit future
lossy policy must be separately approved; it is not part of the safe default.

### Container-specific constraints

- JPEG: identify APP contents by signature, not marker number alone. Do not delete
  all APP1 or APP13 segments. Preserve unrelated Photoshop resources. Define
  duplicate handling and APP length limits. A scan stopping at the first SOS must
  not claim complete metadata discovery/deletion. The initial writer must either
  validate the complete structure sufficiently to enforce its documented subset,
  or refuse the operation. Multi-scan support requires marker-aware handling of
  entropy data, stuffing, restart markers, and inter-scan segments.
- PNG: validate chunk bounds, ordering, CRC policy, eXIf representation, iTXt
  fields, and compressed-text expansion limits. Preserve IDAT and unrelated chunks;
  account for unknown-chunk safe-to-copy rules and unsupported extensions.
- WebP: validate RIFF sizes, chunk padding, VP8X metadata flags, and supported
  still/animated layouts. Preserve coded frames and unrelated chunks. Do not add
  metadata without making the container headers consistent.
- TIFF: EXIF serialization is not a general TIFF file rewriter. Strips, tiles,
  previews, SubIFDs, and other offsets need explicit accounting. DNG and BigTIFF
  are separate scope decisions, not implied by TIFF support.

EXIF, IPTC, and XMP are edited independently by default. Automatic synchronization,
privacy scrubbing guarantees, and arbitrary vendor decoding are not initial scope.

## 3. Hostile-input and resource policy

Agree finite defaults for directory depth/count, entry count, metadata bytes,
string/XML size, decompressed text, and temporary memory. Check size products,
offset addition, and narrowing conversions for overflow. Detect pointer cycles and
validate all referenced ranges. Avoid unbounded recursion and decompression.

Specify cancellation, short reads, seek requirements, stream ownership, buffer
lifetimes, and input/output aliasing. XML handling must disable external entity
resolution and DTD processing and apply bounded parsing. Report structured failures
rather than leaking incidental index exceptions for malformed data.

Rational values retain original numerator/denominator, including malformed zero
denominators; numerical conversion and display must have explicit policies. Human
formatting of exposure/GPS is not a prerequisite for binary correctness.

## 4. Output safety

Plan sizes and offsets before writing. Distinguish preflight failures from I/O
failures after writing starts. Caller-owned output streams may contain partial data
on I/O failure; document this rather than promising rollback.

Path-based saves should use a temporary file and replacement where supported.
Document platform/filesystem constraints and cleanup. Do not promise universal
atomicity or crash durability. Never overwrite the source directly by default.

## 5. Licensing and provenance

This is independent standards-based development with a black-box reference. Do
not claim a formally separated clean-room process unless one is actually adopted.

- Do not copy, translate, or adapt Exiv2 implementation or GPL test source into
  MIT source. Write tests independently from specifications and observations.
- Invoke Exiv2 as an optional standalone CLI. Mere separate-process execution
  generally does not impose GPL on independent callers; this is not a blanket
  legal conclusion about every integration or distribution arrangement.
- Do not bundle Exiv2 in library packages. Distribution of tools requires separate
  license compliance. Audit package artifacts and dependencies before release.
- Program output is not automatically GPL. Store only necessary factual results
  from inputs with known rights; output may still contain protected source content.
- A repository's code license does not establish rights to every photograph or
  binary fixture. Review each source. Unknown provenance means do not import.
- Do not assume freely accessible standards, CIPA/ExifTool tables, or tag names in
  bulk can be copied without checking the applicable terms.
- Verify exact versions and licenses of permissive dependencies, including any
  candidate XMP component. Keep required notices.

For each committed fixture or baseline, record source/author, license or permission,
SHA-256, test purpose, generation recipe, and tool version/arguments where relevant.
Prefer small synthetic fixtures generated by our own code. Keep large camera
corpora outside Git with a documented retrieval and rights policy. Do not commit
personal photos, GPS, serial numbers, or other private metadata without review.

Legal uncertainty is a reason to pause and obtain review, not to invent permission.

## 6. Test strategy

### Independent unit and regression tests

Use xUnit for endian primitives, both TIFF byte orders, inline/external values,
round trips, malformed lengths, overflows, cycles, duplicate entries, unknown tags,
limits, write refusal, and no-op behavior. Use independent expected bytes and
known values: parser/writer round trips alone can share the same bug.

Compare encoded-image ranges and untouched structures byte-for-byte or by hashes;
define those ranges per container. Comparing a whole-file hash after an edit is
not an image-preservation test. Include failure-path source preservation tests.

Add bounded generative/property tests and reproducible fuzz seeds incrementally.
A smoke fuzz run is evidence for that run, not proof that parsing is safe.

### Optional differential integration tests

Keep external-tool tests separate from fast unit tests. Pin tool versions in the
integration environment; record version output, arguments, locale, and exit codes.
Treat tool installation and downloads as separate setup, subject to environment
availability and provenance review. No network dependency in ordinary unit tests.

Use independently generated inputs with Exiv2 and ExifTool. Compare tag identifiers,
types, counts, and normalized values, not unstable localized display text. Classify
mismatches as our defect, reference defect, unsupported feature, or formatting
variation, with a minimal fixture and rationale. Never update golden outputs merely
to make tests pass. Neither tool overrides the applicable specification.

Run relevant ExifTool validation and warnings checks, but do not treat a clean
report as proof of pixel preservation or complete format conformance. External
tests skipped locally must be visible; a required integration job cannot pass by
silently skipping its required tools.

### Performance and release evidence

Measure allocations and throughput with reproducible inputs and environments.
Benchmark before optimizing; never relax validation for speed without approval.
Aim for Linux, Windows, and macOS managed test coverage. Describe actual platform
coverage and remaining gaps rather than claiming portability from a single run.
