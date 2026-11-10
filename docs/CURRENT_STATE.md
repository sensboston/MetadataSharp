# Current state — recovery checkpoint

Refreshed 2026-11-10, at the JPEG frame-type unit (4.1). This is the only recovery document:
what works, what is next, what is deliberately not being done. The chronological record is
`docs/PROGRESS.md` (one short entry per merged PR); everything else is reference.

## How this project is run now (2026-11-10 directive; overrides `EXIV2_PARITY_MASTER_PLAN.md` §5)

- **E01 is frozen where it stands.** No B12/B13/B14, no freeze PR, no second-sample,
  receipt, relabeling or housekeeping-only PRs. `docs/research/EXIV2_CENSUS.json`
  (823 rows, all `verification: not-assessed`, `denominatorFrozen: false`) is a
  **historical reference inventory, not a gate**. Its open gaps (B7/B8 cells, B9's 48 and
  B10's 32 `blocked-rights` cells, CR3, sub-25-byte recognizer ownership) stay listed and
  stay unworked. PR #57 (B12+B13) is abandoned; its commits live on the closed PR only.
- **Approval is required only for new or changed public API.** Put the proposed signatures
  in the PR description as a short block and ask in Russian. Internal parsers, readers,
  tables and refactors go ahead without asking.
- **One feature per PR; merge when CI is green on ubuntu/windows/macos for net8.0+net10.0**
  — no extra sign-off between units. Report 3–5 lines in Russian after each merge.
- **Docs budget:** the docs delta stays well under src+tests (guide: <20%). Update only
  this file, the affected format's `docs/design/*_USAGE_AND_LIMITS.md` (or its implementation
  note) briefly, and `docs/PROGRESS.md`. No new design/normative/rights document per feature.
  A **rights review** is needed only for importing third-party fixtures or vendor MakerNote
  knowledge, and lives as a short section in that PR.

## What works (shipped, test-evidenced)

- **JPEG** `JpegMetadataDocument`: EXIF read/insert/replace/remove, IIM/APP13 read and edit,
  restricted XMP read/insert/replace/remove, `Save`/`SaveAs` via temp file + `File.Move`.
  As of 4.1 the scanner accepts **every JPEG frame type** (SOF0–3, 5–7, 9–11, 13–15), multiple
  frames and any number of scans with DHT/DAC between them, and copies entropy-coded data
  verbatim, so a progressive, arithmetic, differential or lossless file's metadata round-trips.
- **PNG** and **WebP** `*MetadataDocument`: EXIF + XMP edit in static files, chunk/RIFF policy.
- **Standalone TIFF** `TiffMetadataDocument`: layout accounting, restricted descriptive editing
  with obsolete bytes retained; **DNG**, **PSD**, **JP2**, **EPS** are bounded read-only.
- `MetadataCapabilities`: 35 model-driven rows (JPEG 12, PNG 8, WebP 8, TIFF 3, XMP sidecar 4).
- Constraints enforced by tests, not by convention: MIT, net8.0+net10.0, zero runtime
  dependencies, bounded hostile-input parsing, refusal instead of unsafe write.
- Suites: 197 `[Fact]` + 105 `[Theory]` (319 `[InlineData]` rows) in `tests/MetadataSharp.Tests`;
  `tests/reference` (224) + `tests/discovery` (112) cover the frozen E01 tooling and the owned
  synthetic fixtures. CI requires ≥450 executed cases per TFM.
- src is 5.7k lines, C# tests 5.2k. `pip install --break-system-packages pyyaml` before the
  python suites; the sandbox has **no dotnet SDK**, so compile and xunit run only in CI.

## What is next, in order

1. **4.1** JPEG all frame types — this PR.
2. **Differential harness** (replaces the census as the parity metric): owned/generated
   fixtures plus a small CI-downloaded permissively licensed corpus (one manifest records
   source and license; uncommitted files where the license asks for it). A CI job compares
   pinned exiv2 0.28.9 (`-pa`/`-pv`, `-pX` for XMP) against a MetadataSharp dump after
   normalization, per tag and per file grouped by format, with every discrepancy in one
   generated report classified ours/theirs/formatting/unsupported **only when investigated**.
   That pass percentage is the only parity number reported. The pinned-build recipe already
   in `.github/workflows/build-and-test.yml` (`pinned-exiv2-baseline`) and
   `tools/reference/harness.py` are the starting points.
3. **4.2** Typed EXIF/GPS/Interop vocabulary from CIPA DC-008 (Exif 2.32/3.0) + TIFF 6.0:
   names, types, counts, enum meanings, rationals, date/time, GPS coordinates, human-readable
   printing. Internal; no approval needed.
4. **4.3** One unified read entry point returning a single immutable snapshot (EXIF, IPTC,
   XMP, ICC, comment) per container. **Public API → short proposal in the PR, then approval.**
5. **4.4** MakerNotes read-only for Canon, Nikon, Sony, then Fujifilm, Olympus, Panasonic,
   Pentax — structure from public specs and our own analysis; never copy exiv2 or ExifTool tag
   tables or source; unknown tags surface by numeric ID; on write, MakerNote bytes stay
   unchanged and edits that would require relocating them are refused.
6. **4.5** ISO BMFF read-only (HEIC/HEIF, then AVIF via `iinf`/`iloc`, then CR3, then JXL
   container boxes); **4.6** TIFF-derived RAW read-only (CR2, NEF, ARW, then DNG breadth,
   ORF, RW2, PEF, RAF).
7. **4.7** only afterwards: BMFF/RAW writes, previews and thumbnails, workflow helpers
   (timestamp shift, copy between files). Exotic formats (PGF, EXV, DCP, CIFF, EPS/PSD
   breadth) come last.

## Known limits

- Accepting a frame type is about *metadata*, not decoding: no Huffman/arithmetic symbol
  decoding, no MCU or restart-interval validation, no promise that the pixel stream renders.
- Refusals stay: trailing bytes after EOI, duplicate EXIF APP1, non-length-bearing or reserved
  markers (including 0xC8), unbacked table references, over-budget input or segment counts.
- No MakerNote interpretation yet; ICC and thumbnails are not modelled; no XMP toolkit
  (restricted packet profile only); Extended XMP is refused, not merged.
