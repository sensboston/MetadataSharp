# E01 B8 — pinned-CLI census observations (PSD, JP2, EPS; PGF/EXV/DCP blocked)

Bounded read-only census evidence for batch **B8**: one pinned Exiv2 0.28.9 CLI
probe per census row against four owned synthetic fixtures, plus explicit
blocked-rights records for the three B8 formats whose fixture and oracle review is
still pending. 53 census rows in total: 31 observed, 22 blocked.

Files here:

- `observations.json` — the CI report, byte-pinned by `receipt.json`. This is the
  only B8 evidence: the recorder ran in CI because this developer sandbox has no
  pinned Exiv2 build and cannot obtain one (no cmake/ninja, no `expat`/`zlib`
  development headers, package installs and the Actions log host are unreachable).
- `receipt.json` — provenance, the annotation-recovery channel and its hashes, the
  baseline digests, the tally and the explicit `notClaimed` list.

Census rows flip from this file through `tools/reference/build_census.py`;
`python tools/reference/build_census.py --check` re-derives them.

## What was run

`tools/reference/observe_b8.py` runs exactly one read-only flag per row
(`-pS` inventory, `-pa` print metadata, `-pc` comment, `-pC` ICC profile,
`-et` extract thumbnail) against a copy of the fixture in a throwaway directory.
The recorder pins the Exiv2 source commit and working-tree cleanliness, the CMake
cache digest, the license digest, the executable digest and the reported
version/runtime features before observing anything, and refuses to emit a partial
report whose chunks exceed the annotation recovery budget.

Observed outcome counts: 18 `accepted`, 13 `refused`, 22 `blocked-rights`,
0 `static-not-applicable`, 0 `scan-changed`; every input file stayed byte-identical
and no command produced an artifact.

## What the observations show

- `owned-metadata-rgb.psd` (2×2, `8BIM` resources 1058/1060/1028): `-pa` exits 0 and
  prints a 206-byte listing containing all three authored tokens (`B8Artist`,
  `B8Tool`, `B8Keyword`); `-pc` exits 0 with one newline byte; `-pC` (ICC) exits 1;
  `-et` exits 0 with nothing printed and no file written; `-pS` exits 1.
- `owned-flat-rgb.psd` (no resources): **all eight** probes exit 1 — this build
  refuses the file outright, so the flat layout is not a "metadata absent" result
  for any family.
- `owned-rgb.jp2`: `-pS` exits 0 with a 379-byte structure print; every metadata
  family probe exits 0 printing nothing (`-pc` prints one newline byte); `-et`
  exits 253 with no artifact.
- `owned-vector.eps`: `-pS` and `-pC` exit 1; `-pa`, `-pc` and `-pC`-less reads
  exit 0 printing nothing (`-pc` one newline byte). No PostScript was executed.

## Limitations that must not be read around

- `-pa` prints the whole metadata set for a file, so a per-family attribution is
  **not** established: the PSD EXIF, IPTC, XMP and thumbnail read rows all share one
  identical 206-byte output. The rows record that this build prints metadata for
  this fixture, not that it reads one family independently.
- An accepted empty probe is not support. JP2 and EPS fixtures carry no metadata at
  all, so their `rc=0` rows cannot show metadata-present reading, and a refusal on
  the flat PSD is not evidence about PSD files in general.
- The PSD fixtures are one authored v1 flat 8-bit RGB layout with up to three
  `8BIM` resources: no ICC (1039) or thumbnail (1036) resource exists, and PSB,
  layers, colour management and codec conformance are untested. The JP2 fixture is a
  Pillow/OpenJPEG boxed encode with no `xml ` box and no `jp2e` box; the EPS fixture
  is a 168-byte ASCII program whose bounding box was never rendered.
- No B8 **write** suboperation is enumerated or observed, because no owned writable
  B8 fixture and no owned ICC/thumbnail payload is cleared. That gap stays open
  rather than being inferred from a refusal.
- PGF, EXV and DCP rows are blocked on rights/oracle review
  (`../E01_B8_RIGHTS_AND_ORACLE_REVIEW.md`), not refused, excluded or
  not-applicable; B7 CR3 stays blocked for its own reasons.
- ExifTool 12.76 secondary observations are recorded per fixture inside
  `observations.json` (`secondaryObservations`) as a second opinion only.
  Verification for every B8 row stays `not-assessed`; no denominator is frozen, no
  coverage percentage exists, and no publication or DNG D2 write is authorized.
