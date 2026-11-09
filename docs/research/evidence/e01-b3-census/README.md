# E01 B3 census observations — pinned Exiv2 0.28.9 / black-box CLI

Scope: the B3 rows of `docs/research/EXIV2_CENSUS.json` — WebP ×
{inventory, comments (noDocumentedClaim read), exif, icc, iptc
(noDocumentedClaim read), thumbnail, xmp}. Observation only: the pinned CLI's
behavior is **recorded**, never pre-asserted, except for a small spec-derived
set of expectations (marked per row in the census) and the universal invariant
"the coded image data never changes" — for WebP the invariant is the
**VP8/VP8L bitstream chunk payload stream** (`scanPreserved: true` in all
42 observed rows), plus a strict RIFF/WebP structural parse (RIFF size, chunk
tree, even-size padding) of every mutated file.

The fixtures carry a **hand-authored fake VP8 keyframe header** as the coded
image data (`tools/reference/webp_owned_fixtures.py`): it is not a decodable
frame, metadata operations never decode it, and the pinned CLI rewrites it
byte-for-byte. This is the documented analog of the JPEG coded-scan invariant;
it is an observation-time invariant of this batch, not a claim about VP8
codecs in general.

## What was observed

- 42 rows observed with the pinned CLI; 1 row is statically not-applicable:
  `exif-ifd1-uncompressed` thumbnail **insert** — the pinned CLI exposes no
  uncompressed-thumbnail write path (`-i t` embeds a JPEG thumbnail read from
  `<file>-thumb.jpg` only), the same CLI limitation that already makes the
  B1/B2 analogs statically not-applicable. Recorded here as a candidate for
  the developer's denominators review; never self-granted.
- Outcome counts: 41 `accepted`, 1 `accepted-unexpected`, 1
  `static-not-applicable`. **No row is `refused`, `accepted-unparseable`, or
  `scan-changed`.**
- `EX0289-WEBP-XMP-REMOVE-0001` (`accepted-unexpected`): `-M "del
  Xmp.xmp.CreatorTool"` exits **rc=0 but never rewrites the raw `XMP `
  chunk** — `-pa` still prints `Xmp.xmp.CreatorTool` afterwards and the
  packet bytes are unchanged. The B2 PNG analog (iTXt carrier, byte-identical
  authored packet `0a0d30a7…`) removes the property and exits 0
  (`accepted`). The WebP backend of the pinned CLI therefore silently drops
  XMP-deletion while keeping a green exit, container-dependent. Recorded as a
  pinned-CLI anomaly, flagged for developer review (and for a possible
  upstream report); the row stays `observed-cli` with its exit code and
  before/after digests in `observations.json`. `EX0289-WEBP-XMP-REMOVE-0003`
  (duplicate-chunk variant, record-only) observes the same silent no-op.
- Other record-only behaviors worth flagging (all rows record-only by design,
  exit codes/packet digests in `observations.json`):
  - `EX0289-WEBP-XMP-INSERT-0002` (unknown-raw payload, from-nothing):
    rc=0, stderr `Error: XMP Toolkit error 4: Invalid UTF-8 sequence
    length`, and **no XMP chunk is written** (file byte-unchanged).
  - `EX0289-WEBP-XMP-INSERT-0003` / `EX0289-WEBP-XMP-REPLACE-0003/0004`
    (duplicate `XMP ` chunks): the CLI collapses the duplicate to a single
    chunk and applies the set (`-pa` prints the new value); both chunks share
    a packet, first chunk wins on plain reads.
  - `EX0289-WEBP-EXIF-INSERT-0002` / `-REPLACE-0002` (duplicate EXIF
    chunks): `set Exif.Image.Artist` is applied to the **first** EXIF chunk
    only; the second chunk is untouched.
  - `EX0289-WEBP-EXIF-REMOVE-0002`: `del Exif.Image.Artist` removes the
    Artist entry from **both** EXIF chunks.
  - `EX0289-WEBP-EXIF-INSERT-0001` (from-nothing): the CLI creates the
    `EXIF` chunk on demand in a bare VP8X+VP8 file; RIFF/VP8X sizes are
    rewritten correctly.
- The two `noDocumentedClaim` rows (WebP claim cells `-`) observe rc=0 with
  **nothing surfaced**: `EX0289-WEBP-COMMENTS-READ-0001` (`-pc` on a `COM `
  chunk prints nothing) and `EX0289-WEBP-IPTC-READ-0001` (`-pi` on an `IPTC`
  chunk carrying IIM/8BIM bytes prints nothing — even after `-M
  add Iptc.Application2.Keywords`, which the CLI accepts with rc=0 yet
  stores the datasets inside the EXIF TIFF (growing the `EXIF` chunk) when
  one exists, and **silently drops** them when the file has no EXIF chunk).
  The cells carry no documented claim, so nothing is asserted; the
  undocumented carrier/CLI gap is recorded as observed behavior only.
- Duplicate carriers and reads: `-pa` prints **both** EXIF chunks' tags and
  the **last** XMP chunk's packet (last-wins on print); `-pC` dumps the
  (raw) profile bytes of an ICCP chunk; `-iC -l <dir>` replaces/sets the
  single ICCP chunk with `<dir>/<target-stem>.icc` (verified: container test
  `case.webp` reads `case.icc`); `-d C` removes it. ICCP duplicates are
  byte-identical in the fixture (first-wins-style, record-only).
- Inventory: `-pS` prints the RIFF/WebP structure table, rc=0 (recognition
  only; never metadata support).
- EXIF IFD1 thumbnails: `-pa` prints IFD1 tags without decoding the blob;
  `-i t` embeds `<file>-thumb.jpg` starting from the thumbnail-free
  `webp-exif.webp` base (spec-derived, verified at observation time: IFD1
  gains `JPEGInterchangeFormat`/`Length`); on a file whose IFD1 holds an
  uncompressed strip, `replace` yields a **mixed state** (strip tags AND
  JPEG thumbnail tags coexist — CLI-defined; record-only), mirroring the
  B1/B2 findings; `-d t` removes all IFD1 thumbnail tags.

## Fixtures

All fixtures are owned synthetic WebPs authored by
`tools/reference/webp_owned_fixtures.py` (stdlib byte-level RIFF writer); none
are derived from Exiv2, libwebp, camera corpus, or any upstream fixture. The
pin set is **12** fixtures, pinned by SHA-256 in
`tests/fixtures/webp/census-manifest.json` (regeneration is byte-deterministic
and CI cross-checks it); rights: `owned-synthetic`, MIT. See
`tests/fixtures/webp/census-provenance.json`. **No fixture binaries are
committed.** Note: `tests/fixtures/webp/provenance.json` is **not** this
batch's provenance — it belongs to the .NET library's PNG/WebP-group audit
(Pillow-encoded single-pixel fixtures) and is deliberately left untouched;
this batch's manifest/provenance pair is `census-manifest.json` /
`census-provenance.json` in the same directory.

## Provenance and reproduction

- Local rehearsal (this directory's `observations.json`): pinned source
  `b0859fc61fea5dedfc898aece6b15893ddf4314e`, built from that source with the
  same CMAKE options as the CI `pinned-exiv2-baseline` job (static zlib
  1.3.1 / expat 2.6.4 / brotli 1.1.0 / inih r56, `BUILD_SHARED_LIBS=OFF`,
  `BROTLI ON`, `BMFF ON`, no WebP-specific flag — WebP support is always
  compiled in); observations recorded with
  `E01_SOURCE_COMMIT=<rehearsal git sha>`. Raw JSON is committed as-is.
- CI (authoritative gate): the `pinned-exiv2-baseline` job rebuilds the
  pinned CLI, regenerates the fixtures and byte-compares them against the
  committed census manifest, and re-runs all batches (`observe_b0.py --batch
  all`, `observe_b2.py`, then `observe_b3.py`) without infrastructure
  failure. The job never gates on observation *outcomes* (a refusal is a
  finding, not a failure); it gates on infrastructure integrity (pin drift,
  modified upstream, fixture regeneration/pin mismatch) and uploads
  `e01-b3-census-observations` (30-day retention) next to the B0/B1/B2
  artifacts. Artifact bytes are not retrievable from the dev sandbox (host
  allowlist); the receipt documents the run, and no byte-equality between
  the CI artifact and the committed local rehearsal is claimed.
- Reproduce locally: build the pinned CLI as in that job (or use any local
  0.28.9-pinned build), then run `observe_b3.py`; it verifies the pin,
  regenerates fixtures and compares their SHA-256 against the committed
  census manifest, runs every applicable census row of batch B3, and writes
  `observations.json`.

## Limits and honest claims

- One owned fixture family per axis point; findings generalize to these
  fixtures only. No VP8 lossless (VP8L) or alpha-chunk (ALPH) coded data, no
  animated WebP (ANIM/ANMF), no 16-bit or palette inputs, no corrupt or
  truncated files, no `THUM` (WebP extended-format thumbnail) chunk carrier —
  the pinned CLI's handling of `THUM` is unverified and the chunk is out of
  scope for this batch (recorded here, not observed); EXIF-IFD1 thumbnails
  only.
- XMP rows deliberately omit the pure-ASCII encoding cell (byte-identical to
  utf-8 behavior); IPTC and thumbnail rows carry no encoding axis
  (`not-applicable`). Unknown-raw READ rows reuse the single-payload
  fixtures. Duplicate-carrier rows observe what the pinned CLI does with
  *multiple* chunks even though the WebP spec allows at most one of each —
  the duplicates axis exists to record CLI reality, not to license the
  multi-chunk files.
- The census records *baseline behavior*, not support. MetadataSharp
  verification stays `not-assessed` everywhere until E07.
- No denominator is frozen and no coverage percentage is computed or implied.
  The single static not-applicable row
  (`EX0289-WEBP-THUMBNAIL-INSERT-0002`) is an exclusion candidate pending
  the developer's denominators review; exclusions are never self-granted.
- `denominatorFrozen: false` everywhere; freeze remains a separate,
  developer-authorized step after B14.
