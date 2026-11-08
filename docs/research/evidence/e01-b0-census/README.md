# E01 B0 census observations — pinned Exiv2 0.28.9 / black-box CLI

Scope: the B0 pilot rows of `docs/research/EXIV2_CENSUS.json` — JPEG ×
{EXIF, comments, ICC} plus the JPEG inventory track. Observation only: the
pinned CLI's behavior is **recorded**, never pre-asserted, except for a small
spec-derived set of expectations (marked per row in the census) and the
universal invariant "the coded JPEG scan never changes".

## What was observed

- 97 rows observed with the pinned CLI; 38 rows are statically
  not-applicable (axis collapse, reasons recorded per row in the census):
  the `app1-exif-multiple` layout *is* the duplicate-block case (duplicates
  axis not independent) and `com-single` cannot carry a duplication policy.
- Outcome counts: 89 `accepted` (exit 0), 8 `refused` (exit != 0),
  38 `static-not-applicable`. The 8 refusals are all ICC rows whose fixture
  carries two ICC profiles in one JPEG (`icc-single-dup.jpg`,
  `icc-segmented-dup.jpg`); the CLI aborts with
  `Exiv2 exception ... Not a valid ICC Profile` on insert/replace/remove/extract.
  `-pC` (print) still succeeds on those files. This is a real
  out-of-spec-but-present-in-the-wild scenario; duplicate ICC coverage is a
  refusal at the pinned baseline, recorded with the raw stderr in the rows'
  command transcripts inside `observations.json`.
- JPEG comments: the CLI stores the exactly-bytes payload passed on `-c` plus a
  trailing NUL (consistent with the known D001 baseline difference); the bytes
  are recorded verbatim per encoding (`ascii`, `utf-8`, `unknown-raw`)
  without transcoding claims.
- Comment extraction (`-e c`) writes an exiv2 raw `.exv` container
  (`\xff\x01Exiv2` + segments), not a bare payload; file names/hashes are
  recorded per row.
- EXIF duplicates print as-is (in-IFD duplicate tags are all printed, rc=0);
  a second EXIF APP1 block is silently ignored on print and used as unchanged
  context by mutations (record-only; covered by `exif-multi-app1.jpg`,
  `exif-subifd-multi.jpg`).
- ICC segmented profiles reassemble exactly (extract == authored profile
  bytes, verified for the single-profile segmented fixture).
- The coded scan bytes were preserved in every one of the 135 rows
  (`scanPreserved: true` across the batch).

## Fixtures

All fixtures are owned synthetic JPEGs authored by
`tools/reference/jpeg_owned_fixtures.py`; none are derived from Exiv2,
ExifTool, camera corpus, or any upstream fixture. Pinned by SHA-256 in
`tests/fixtures/jpeg/manifest.json` (regeneration is byte-deterministic and CI
cross-checks it); rights: `owned-synthetic`, MIT. See
`tests/fixtures/jpeg/provenance.json`. No fixture binaries are committed.

## Provenance and reproduction

- Local rehearsal (this directory's `observations.json` as first committed):
  pinned source `b0859fc61fea5dedfc898aece6b15893ddf4314e`, built from that
  source with the same CMAKE options as the CI `pinned-exiv2-baseline` job;
  observations recorded with `E01_SOURCE_COMMIT=<git sha>` of the rehearsal.
  Raw JSON is committed as-is; `receipt.json` pins the CI run that re-produced
  it during the PR.
- CI (authoritative gate): the `pinned-exiv2-baseline` job rebuilds the pinned
  CLI and re-runs `python tools/reference/observe_b0.py --upstream
  artifacts/e01/source --build artifacts/e01/build`, uploading
  `e01-b0-census-observations` (30-day retention). The job never gates on
  observation *outcomes* (a refusal is a finding, not a failure); it gates on
  infrastructure integrity (pin drift, modified upstream, fixture
  regeneration/pin mismatch).
- Reproduce locally: build the pinned CLI as in that job (or use any local
  0.28.9-pinned build), then run the command above; `observe_b0.py` verifies
  the pin, regenerates fixtures and compares their SHA-256 against the
  committed manifest, runs every applicable census row, and writes
  `observations.json`.

## Limits and honest claims

- One owned fixture family per axis point; no MakerNote, thumbnail, extended
  EXIF, IPTC, or XMP content; no multi-image JPEGs; no corrupt/truncated
  inputs. Findings generalize to these fixtures only.
- The census records *baseline behavior*, not support. MetadataSharp
  verification stays `not-assessed` everywhere until E07.
- No denominator is frozen and no coverage percentage is computed or implied.
  Exclusion candidates (if any) will be submitted to the developer for
  sign-off; they are never self-granted.
