# Current state — recovery checkpoint

Refreshed 2026-11-10, at the differential parity harness. The only recovery document; the
chronological record is `docs/PROGRESS.md` (one short entry per merged PR), everything else reference.

## How this project is run now (2026-11-10 directive; overrides `EXIV2_PARITY_MASTER_PLAN.md` §5)

- **E01 is frozen where it stands.** No B12/B13/B14, no freeze PR, no second-sample, receipt,
  relabeling or housekeeping-only PRs. `docs/research/EXIV2_CENSUS.json` (823 rows, all
  `verification: not-assessed`, `denominatorFrozen: false`) is a **historical reference inventory,
  not a gate**. Its open gaps (B7/B8 cells, B9's 48 and B10's 32 `blocked-rights` cells, CR3,
  sub-25-byte recognizer ownership) stay listed and stay unworked. PR #57 (B12+B13) is abandoned;
  its commits live on the closed PR only.
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
  restricted XMP read/insert/replace/remove, `Save`/`SaveAs` via temp file + `File.Move`. As of 4.1
  the scanner accepts **every JPEG frame type** (SOF0–3, 5–7, 9–11, 13–15), any number of frames and
  scans with DHT/DAC between them, and copies entropy-coded bytes verbatim, so progressive,
  arithmetic, differential and lossless files round-trip their metadata.
- **PNG** and **WebP** `*MetadataDocument`: EXIF + XMP edit in static files, chunk/RIFF policy.
- **Standalone TIFF** `TiffMetadataDocument`: layout accounting, restricted descriptive editing
  with obsolete bytes retained; **DNG**, **PSD**, **JP2**, **EPS** are bounded read-only.
- **Differential parity harness** (`tools/parity`, `tools/MetadataSharp.Dump`, CI job
  `differential-parity`): our readers versus the `exiv2 -Pkycvt` expectations in the pinned upstream
  test tree, plus our own fixtures against the pinned tool built in the same job. Upstream files are
  fetched at CI time and read as data - never vendored, built or executed, and their `tests/*.py`
  (macro-token expectations) are not interpreted. Rates are reported; only a broken run is gated.
  First green run: 330 corpus + 226 owned files, no structural problem; all 26 and 25 values that
  had a reference counterpart agreed, and key parity (0.5%, 4.0%) is the numeric-name gap 4.2 closes.
- `MetadataCapabilities`: 35 model-driven rows (JPEG 12, PNG 8, WebP 8, TIFF 3, XMP sidecar 4).
- Enforced by tests, not convention: MIT, net8.0+net10.0, zero runtime dependencies, bounded
  hostile-input parsing, refusal instead of unsafe write.
- Suites: 197 `[Fact]` + 105 `[Theory]` (319 `[InlineData]`) in `tests/MetadataSharp.Tests`;
  `tests/reference` (224) and `tests/discovery` (112) cover the E01 tooling and owned fixtures,
  `tests/parity` (45) the harness offline. CI requires ≥450 cases per TFM.
- src is 5.7k lines, C# tests 5.2k. `pip install --break-system-packages pyyaml` before the python
  suites; the sandbox has **no dotnet SDK**, so compile and xunit run only in CI.

## What is next, in order

1. **4.2** Typed EXIF/GPS/Interop vocabulary from CIPA DC-008 (Exif 2.32/3.0) + TIFF 6.0: names,
   types, counts, enum meanings, rationals, date/time, GPS coordinates, human-readable printing.
   Internal; no approval. It also renames harness rows, since the comparison keys are exiv2's.
2. **4.3** One unified read entry point returning a single immutable snapshot (EXIF, IPTC,
   XMP, ICC, comment) per container. **Public API → short proposal in the PR, then approval.**
3. **4.4** MakerNotes read-only for Canon, Nikon, Sony, then Fujifilm, Olympus, Panasonic,
   Pentax — structure from public specs and our own analysis; never copy exiv2 or ExifTool tag
   tables or source; unknown tags surface by numeric ID; on write, MakerNote bytes stay
   unchanged and edits that would require relocating them are refused. The reference prints
   `Exif.MakerNote.Offset`/`ByteOrder` for 221 of the 330 corpus files, so those rows stay open
   until here.
4. **4.5** ISO BMFF read-only (HEIC/HEIF, then AVIF via `iinf`/`iloc`, then CR3, then JXL
   container boxes); **4.6** TIFF-derived RAW read-only (CR2, NEF, ARW, then DNG breadth,
   ORF, RW2, PEF, RAF). 4.5/4.6 also move the corpus's heic/avif/jxl/raw rows out of `not-read`.
5. **4.7** only afterwards: BMFF/RAW writes, previews and thumbnails, workflow helpers
   (timestamp shift, copy between files). Exotic formats (PGF, EXV, DCP, CIFF, PSD breadth) last.

## Known limits

- Accepting a frame type is about *metadata*, not decoding: no Huffman/arithmetic symbol
  decoding, no MCU or restart-interval validation, no promise that the pixel stream renders.
- Refusals stay: trailing bytes after EOI, duplicate EXIF APP1, non-length-bearing or reserved
  markers (including 0xC8), unbacked table references, over-budget input or segment counts.
- No MakerNote interpretation yet; ICC and thumbnails are not modelled; no XMP toolkit
  (restricted packet profile only); Extended XMP is refused, not merged.
- The harness reads no BMFF, video, EXV, CIFF or EPS, never decodes pixels, skips files above
  64 MB, and counts an unread file `not-read` rather than as a disagreement. A reference's decoded
  label is not raw bytes, so `match-key` means "same tag found", not "same meaning printed".
