# E01 B1 census observations — pinned Exiv2 0.28.9 / black-box CLI

Scope: the B1 rows of `docs/research/EXIV2_CENSUS.json` — JPEG ×
{XMP, IPTC, EXIF IFD1 thumbnail}. Observation only: the pinned CLI's behavior
is **recorded**, never pre-asserted, except for a small spec-derived set of
expectations (marked per row in the census) and the universal invariant "the
coded JPEG scan never changes".

## What was observed

- 57 rows observed with the pinned CLI; 1 row is statically not-applicable:
  `exif-ifd1-uncompressed` thumbnail **insert** — the pinned CLI exposes no
  uncompressed-thumbnail write path (`-i t` embeds a JPEG thumbnail read from
  `<file>-thumb.jpg` only). Flagged for developer review at the denominators
  step; revisit if the CLI grows that capability.
- Outcome counts: 55 `accepted`, 2 `nonzero-exit-artifact-produced`,
  1 `static-not-applicable`. **No row is `refused` and no row changed the
  coded scan** (`scanPreserved: true` in 58/58 rows).
- Thumbnail extract (`-e t`) exits **137** (JPEG-compressed source) and **46**
  (uncompressed source) despite succeeding; the produced artifacts decide
  honesty: for the JPEG-compressed source the extracted file is **byte-exact**
  against the authored embedded thumbnail blob (SHA-256 verified at
  observation time; `expectOk=true`), for the uncompressed source the CLI
  converts the strip to a classic TIFF (`raw-thumb.tif`, CLI-defined bytes;
  record-only). The odd exit codes are recorded verbatim per row (see
  `commands` in `observations.json`) and classified as
  `nonzero-exit-artifact-produced`, never silently accepted or refused.
- XMP: the pinned CLI prints `Xmp.xmp.CreatorTool` for standard packets;
  Extended XMP files print only `Xmp.xmpNote.HasExtendedXMP` (extension chunks
  are not reassembled for print); non-UTF-8 XMP payloads fail decode with
  **rc=0** and `XMLValidator` warnings (no tags printed); a second XMP APP1
  segment is silently ignored (first-wins) on print and on mutation;
  `del Xmp.xmp.CreatorTool` on an Extended-XMP file removes nothing from the
  chunks and leaves `HasExtendedXMP` intact; `-M set` on an Extended-XMP file
  adds the tag to the base packet (verified at observation time).
- IPTC: `-pi` prints all datasets of the **first** APP13 segment only; a
  second APP13 segment is silently ignored on print and on mutation
  (`add`/`del` touch the first block). Repeated 2:25 datasets all print;
  `-M set Iptc.Application2.Keywords` replaces the **first** repeated dataset
  and keeps the rest (recorded; `recordOnly`). `-M add` appends a new dataset
  (spec-derived; verified at observation time).
- EXIF thumbnails: `-pa` prints IFD1 thumbnail tags without decoding the blob;
  `-d t` removes all IFD1 thumbnail tags; `-i t` embeds `<file>-thumb.jpg`
  (JPEG only) starting from a thumbnail-free EXIF base (spec-derived;
  verified at observation time) and also works as an overwrite on both
  thumbnail layouts (record-only — on the uncompressed source the result
  carries both the strip tags and the JPEG tags, a CLI-defined mixed state).
- Fixture for `xmp` reads of unknown-raw payloads: `xmp-nonutf8.jpg`
  (standard layout) / `xmp-extended-nonutf8.jpg` (extended layout).

## Fixtures

All fixtures are owned synthetic JPEGs authored by
`tools/reference/jpeg_owned_fixtures.py`; none are derived from Exiv2,
ExifTool, camera corpus, or any upstream fixture. The pin set grew from 14 to
**31** fixtures in this batch and is pinned by SHA-256 in
`tests/fixtures/jpeg/manifest.json` (regeneration is byte-deterministic and CI
cross-checks it); rights: `owned-synthetic`, MIT. See
`tests/fixtures/jpeg/provenance.json`. No fixture binaries are committed.

## Provenance and reproduction

- Local rehearsal (this directory's `observations.json`): pinned source
  `b0859fc61fea5dedfc898aece6b15893ddf4314e`, built from that source with the
  same CMAKE options as the CI `pinned-exiv2-baseline` job; observations recorded
  with `E01_SOURCE_COMMIT=<rehearsal git sha>`. Raw JSON is committed as-is.
- CI (authoritative gate): the `pinned-exiv2-baseline` job rebuilds the pinned
  CLI, regenerates the fixtures and byte-compares them against the committed
  manifest, and re-runs both batches (`observe_b0.py`, batch `all`) without
  infrastructure failure. The job never gates on observation *outcomes* (a
  refusal is a finding, not a failure); it gates on infrastructure integrity
  (pin drift, modified upstream, fixture regeneration/pin mismatch) and uploads
  `e01-b1-census-observations` (30-day retention) next to
  `e01-b0-census-observations`. Artifact bytes are not retrievable from the
  dev sandbox (host allowlist); the receipt documents the run, and no
  byte-equality between the CI artifact and the committed local rehearsal is
  claimed.
- Reproduce locally: build the pinned CLI as in that job (or use any local
  0.28.9-pinned build), then run `observe_b0.py --batch b1`; it verifies the
  pin, regenerates fixtures and compares their SHA-256 against the committed
  manifest, runs every applicable census row of batch B1, and writes
  `observations.json`.

## Limits and honest claims

- One owned fixture family per axis point; findings generalize to these
  fixtures only. No MakerNotes; no multi-image JPEGs; no corrupt/truncated
  inputs; no large/streaming profile cases; no XMP evaluation of the extension
  chunks beyond chunking structure and SHA-256 pins.
- XMP rows deliberately omit the pure-ASCII encoding cell (byte-identical to
  utf-8 behavior); IPTC and thumbnail rows carry no encoding axis
  (`not-applicable`). Duplicate coverage exists for XMP (APP1 pairs) and IPTC
  (repeated 2:25 dataset and repeated APP13 segment); thumbnail duplication is
  out of scope (EXIF allows at most one IFD1 thumbnail).
- The census records *baseline behavior*, not support. MetadataSharp
  verification stays `not-assessed` everywhere until E07.
- No denominator is frozen and no coverage percentage is computed or implied.
  The single static not-applicable row is an exclusion candidate pending the
  developer's denominators review; exclusions are never self-granted.
