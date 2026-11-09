# E01 B2 census observations — pinned Exiv2 0.28.9 / black-box CLI

Scope: the B2 rows of `docs/research/EXIV2_CENSUS.json` — PNG ×
{inventory, comments (noDocumentedClaim read), exif (noDocumentedClaim read),
icc, iptc, thumbnail, xmp}. Observation only: the pinned CLI's behavior is
**recorded**, never pre-asserted, except for a small spec-derived set of
expectations (marked per row in the census) and the universal invariant "the
coded image data never changes" — for PNG the invariant is the **IDAT payload
stream** (`idatPreserved: true` in 66/66 observed rows), plus chunk CRC-32
verification on the structural parse of every mutated file.

## What was observed

- 66 rows observed with the pinned CLI; 1 row is statically not-applicable:
  `exif-ifd1-uncompressed` thumbnail **insert** — the pinned CLI exposes no
  uncompressed-thumbnail write path (`-i t` embeds a JPEG thumbnail read from
  `<file>-thumb.jpg` only), the same CLI limitation that already makes the
  JPEG analog statically not-applicable. Recorded here as a candidate for the
  developer's denominators review; never self-granted.
- Outcome counts: 66 `accepted`, 1 `static-not-applicable`. **No row is
  `refused`, `accepted-unexpected`, `accepted-unparseable`, or
  `scan-changed`.**
- The two `noDocumentedClaim` rows (PNG claim cells `-`) both observe rc=0
  with content printed: `EX0289-PNG-COMMENTS-READ-0001` (`-pc` prints the
  Description text of the owned text-chunk trio) and `EX0289-PNG-EXIF-READ-0001`
  (`-pa` prints the eXIf chunk's orientation/artist tags). The cells carry no
  documented claim, so nothing is asserted; the undocumented support is
  recorded as observed behavior only.
- IPTC rides the CLI's own raw-profile carrier: `zTXt "Raw profile type
ipc"` with the hex-encoded IIM/8BIM payload (which is what the pinned CLI
  writes). The same raw text in an **uncompressed iTXt** carrier is read
  correctly by the CLI (keywords print, rc=0) — the earlier local probe that
  showed "corrupted image metadata" traced to a malformed hand-built fixture
  (wrong hex-length field), not to the carrier. On **write** the CLI
  normalizes either carrier to a single zTXt raw profile whose hex is
  re-wrapped across lines, with ALL datasets merged (an added keyword is
  appended to the same raw profile; duplicated carriers collapse into one).
  `-M del Iptc.Application2.Keywords` removes the carrier chunk entirely
  (output is byte-identical to the no-IPTC base fixture).
  Repeated-dataset `set` semantics replace the first of the repeated 2:25
  datasets and keep the rest (recorded; `recordOnly`).
- iCCP: `-iC -l <dir>` replaces/sets the single iCCP chunk with the profile
  in `<dir>/<basename>.icc`; `-pC` dumps the decompressed profile to stdout;
  `-d C` removes the iCCP chunk entirely. PNG allows at most one iCCP chunk;
  the byte-identical duplicated iCCP fixture observes first-wins-style
  behavior on read (record-only) and a single merged chunk on write.
- XMP: uncompressed iTXt (`XML:com.adobe.xmp`) and zTXt-compressed carriers
  both read and mutate (`-pa` prints `Xmp.xmp.CreatorTool`; `-M
  set/del Xmp.xmp.CreatorTool` works on both). On write the CLI normalizes
  XMP into an **iTXt** carrier regardless of the source layout. A second XMP
  chunk is silently ignored (first-wins) on print and on mutation (both
  carriers). Non-UTF-8 XMP payloads print no tags (decoder error warning;
  rc=0); `set` with raw bytes is record-only. Unknown-raw READ rows reuse the
  same single-payload fixtures (no separate non-UTF-8 duplicates fixture), as
  in B0/B1 — recorded, not hidden.
- EXIF IFD1 thumbnails: `-pa` prints IFD1 tags without decoding the blob;
  `-i t` embeds `<file>-thumb.jpg` starting from the thumbnail-free
  `png-exif.png` base (spec-derived; verified at observation time: IFD1
  gains `JPEGInterchangeFormat`/`Length`); on a file whose IFD1 holds an
  uncompressed strip, `replace` yields a **mixed state** (strip tags AND
  JPEG thumbnail tags coexist — CLI-defined; recorded), mirroring the JPEG
  B1 finding; `-d t` removes all IFD1 thumbnail tags.
- Inventory: `-pS` on the container fixture rc=0 (recognition only; never
  metadata support).

## Fixtures

All fixtures are owned synthetic PNGs authored by
`tools/reference/png_owned_fixtures.py` (stdlib byte-level writer; CRC-32 per
chunk; IDAT of filter-0 scanlines); none are derived from Exiv2, libpng,
camera corpus, or any upstream fixture. The pin set is **17** fixtures, pinned
by SHA-256 in `tests/fixtures/png/manifest.json` (regeneration is
byte-deterministic and CI cross-checks it); rights: `owned-synthetic`, MIT.
See `tests/fixtures/png/provenance.json`. No fixture binaries are committed.

## Provenance and reproduction

- Local rehearsal (this directory's `observations.json`): pinned source
  `b0859fc61fea5dedfc898aece6b15893ddf4314e`, built from that source with the
  same CMAKE options as the CI `pinned-exiv2-baseline` job; observations
  recorded with `E01_SOURCE_COMMIT=<rehearsal git sha>`. Raw JSON is committed
  as-is.
- CI (authoritative gate): the `pinned-exiv2-baseline` job rebuilds the pinned
  CLI, regenerates the fixtures and byte-compares them against the committed
  manifest, and re-runs all batches (`observe_b0.py --batch all`, then
  `observe_b2.py`) without infrastructure failure. The job never gates on
  observation *outcomes* (a refusal is a finding, not a failure); it gates on
  infrastructure integrity (pin drift, modified upstream, fixture
  regeneration/pin mismatch) and uploads `e01-b2-census-observations`
  (30-day retention) next to `e01-b0-census-observations` and
  `e01-b1-census-observations`. Artifact bytes are not retrievable from the
  dev sandbox (host allowlist); the receipt documents the run, and no
  byte-equality between the CI artifact and the committed local rehearsal is
  claimed.
- Reproduce locally: build the pinned CLI as in that job (or use any local
  0.28.9-pinned build), then run `observe_b2.py`; it verifies the pin,
  regenerates fixtures and compares their SHA-256 against the committed
  manifest, runs every applicable census row of batch B2, and writes
  `observations.json`.

## Limits and honest claims

- One owned fixture family per axis point; findings generalize to these
  fixtures only. No interlaced/animated (APNG) inputs, no 16-bit/palette
  inputs, no corrupt/truncated files, no iCCP method values other than 0, no
  extended-XMP carrier analog (PNG iTXt is not extended-XMP chunking; recorded
  out of scope), no CRC-failure fixtures (the CRC check is a parse-time
  structural invariant, not an axis).
- XMP rows deliberately omit the pure-ASCII encoding cell (byte-identical to
  utf-8 behavior); IPTC and thumbnail rows carry no encoding axis
  (`not-applicable`). Unknown-raw READ rows reuse the single-payload
  fixtures.
- The census records *baseline behavior*, not support. MetadataSharp
  verification stays `not-assessed` everywhere until E07.
- No denominator is frozen and no coverage percentage is computed or implied.
  The single static not-applicable row (`exif-ifd1-uncompressed` thumbnail
  insert) is an exclusion candidate pending the developer's denominators
  review; exclusions are never self-granted.
