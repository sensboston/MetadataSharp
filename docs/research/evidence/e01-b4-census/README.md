# E01 B4 census observations — pinned Exiv2 0.28.9 / black-box CLI

Scope: the B4 rows of `docs/research/EXIV2_CENSUS.json` — TIFF ×
{inventory, comments (noDocumentedClaim read), exif, icc, iptc
(documented Read/Write), thumbnail, xmp}. Observation only: the pinned
CLI's behavior is **recorded**, never pre-asserted, except for a small
spec-derived set of expectations (marked per row in the census) and the
universal invariant "the coded image data never changes" — for TIFF the
invariant is the **IFD0 strip/tile image payload bytes**
(`scanPreserved: true` in all 49 observed rows), plus a strict classic
II/42 + IFD structural parse (magic, entry counts, tag order, sequential
offsets) of every authored/rewritten file via the shared authored TIFF
parser. IFD1 (thumbnail) and SubIFD entries are deliberately excluded
from the invariant because metadata rewrites legitimately rewrite them.

## What was observed

- 49 rows observed with the pinned CLI; 1 row is statically not-applicable:
  `exif-ifd1-uncompressed` thumbnail **insert** — the pinned CLI exposes no
  uncompressed-thumbnail write path (`-i t` embeds a JPEG thumbnail read
  from `<file>-thumb.jpg` only; observed on the raw-strip fixture: instead of
  an uncompressed thumbnail, the IFD1 is rewritten with **both** the old
  strip tags and a JPEG thumbnail (`JPEGInterchangeFormat`) — a mixed state,
  CLI-defined, record-only), the same CLI limitation that already makes the
  B1/B2/B3 analogs statically not-applicable. Recorded here as a candidate
  for the developer's denominators review; never self-granted.
- Outcome counts: 49 `accepted`, 0 `accepted-unexpected`, 0 `refused`,
  0 `accepted-unparseable`, 0 `scan-changed`, 1 `static-not-applicable`.
- The `noDocumentedClaim` row (TIFF claim cell `-`) observes rc=0 with
  **nothing surfaced**: `EX0289-TIFF-COMMENTS-READ-0001` (`-pc` on a file
  carrying `XPComment` tag `0x9C9C`, UTF-16LE, prints nothing). The carrier
  tag is present in the file (recorded in facts), the cell carries no
  documented claim, so nothing is asserted; the undocumented carrier/CLI
  gap is recorded as observed behavior only.
- Documented-claim rows with spec-derived expectations, all held:
  - EXIF read on `tiff-exif.tif` surfaces `Artist=Alice` (rc=0);
  - EXIF insert from nothing (`tiff-base.tif` + `set Exif.Image.Artist`)
    **creates the entry** (rc=0, `-pa` prints the added value);
  - EXIF replace swaps `Alice` → the replacement value;
  - EXIF remove deletes the entry; duplicate-carrier fixture removes from
    **both** entries;
  - ICC insert from nothing (`tiff-base.tif` + `-iC -l <dir>`) **creates
    tag `0x8773`** with the `<target-stem>.icc` bytes (rc=0, profile
    byte-verified equal to the authored profile); remove (`-d C`) deletes it;
  - ICC replace/insert on the carrier is accepted; on the duplicate
    (byte-identical twice in IFD0) the CLI collapses to a **single** entry on
    write (`EX0289-TIFF-ICC-INSERT-0002`: 2 present in fixture, 1 after
    replace; record-only) and `-d C` removes exactly one of the two
    duplicates (`-pC` still prints one profile afterwards; record-only);
  - IPTC (documented Read/Write): read surfaces
    `Iptc.Application2.Keywords` Alpha/Beta (raw `1C 02 19` datasets in
    tag `0x83BB`); insert **from nothing** (`tiff-base.tif` + `-M "add
    Iptc.Application2.Keywords"`) **creates tag `0x83BB`** holding the new
    raw IIM dataset; replace swaps Alpha→replacement, Beta kept; remove
    (`del Iptc.Application2.Keywords`) empties the dataset list; on the
    duplicate carrier (`0x83BB` authored twice, record-only) `add` appends to
    **both**; `del` is a **silent no-op** (rc=0, datasets still present in
    both entries);
  - XMP read/insert/replace/remove on the single-packet carrier all
    accepted (delete removes the whole `0x02BC` tag — `-pa` prints nothing
    afterwards, unlike the WebP B3 analog where deletion was a silent
    no-op);
  - XMP insert/replace from nothing/on-carrier with `unknown-raw` payloads
    are record-only: the pinned CLI's write path does not surface the raw
    value and the resulting packet bytes differ per case (digests in
    `observations.json`);
  - Thumbnail insert from nothing on `tiff-exif.tif` (IFD0 only, no IFD1)
    creates the IFD1 JPEG thumbnail (`JPEGInterchangeFormat`/`Length`,
    spec-derived, verified at observation time); replace on the
    uncompressed-strip variant yields the same **mixed state** as B1/B2/B3
    (strip tags AND JPEG thumbnail tags coexist — CLI-defined; record-only);
    `-d t` removes all IFD1 thumbnail tags.
- Duplicate carriers and writes (record-only by design, exit codes/digests
  in `observations.json`):
  - EXIF duplicates (`Artist` authored twice in IFD0): `set` rewrites the
    **first** entry only (`MetadataSharpAdded`, `Bob` untouched); `del`
    removes from **both**; `-pa` prints both.
  - XMP duplicates (`0x02BC` twice, packets A/B): `set` rewrites the
    **first** packet only; `-pa` prints the **last** packet (`MetadataSharpB`).
  - IPTC duplicates: `add` applies to both entries; `del` is a silent
    no-op (above).
  - ICC duplicates: writes collapse to one entry; `-d C` removes one entry.
  - Reads print all carriers' tags (EXIF), the last XMP packet, or both
    IPTC dataset lists (`Alpha, Beta, Alpha, Beta` on the duplicated read).
- IPTC rewrite normalization: the pinned CLI rewrites tag `0x83BB` to **raw
  IIM** (`1C 02 19 …` datasets, no 8BIM wrapper, ASCII keywords as type
  BYTE/Long as applicable). The authored fixtures carry the same raw-IIM
  layout (the earlier 8BIM-wrapped variant was re-authored after the first
  rehearsal showed the CLI normalizing it; fixtures regenerated and
  re-pinned). `-pi` reads both layouts; only raw IIM survives a rewrite.
- ICC insert/replace **rewrites tag `0x8773` as type 7 (UNDEFINED)** — the
  spec-correct type for ICC profiles on TIFF; the shared authored parser was
  extended accordingly (UNDEFINED/RATIONAL/SRATIONAL/FLOAT/DOUBLE/IFD type
  sizes) so rewritten files remain strictly parseable.
- Inventory: `-pS` prints the TIFF structure table, rc=0 (recognition only;
  never metadata support); `tiff-tiles.tif` (16×16 tiled image) exercises
  the tile path (`TileWidth/TileLength/TileOffsets/TileByteCounts`).
- All 49 operations exit rc=0.

## Fixtures

All fixtures are owned synthetic classic II/42 TIFFs authored by
`tools/reference/tiff_owned_fixtures.py` (stdlib byte-level TIFF writer on
the shared primitives of `tools/reference/jpeg_owned_fixtures.py`); none are
derived from Exiv2, libtiff, camera samples, or any imported corpus. The
pin set is **14** fixtures, pinned by SHA-256 in
`tests/fixtures/tiff/manifest.json` (regeneration is byte-deterministic and
CI cross-checks it); rights: `owned-synthetic`, MIT. See
`tests/fixtures/tiff/provenance.json`. **No fixture binaries are
committed.** `tests/fixtures/tiff/` has no .NET-audit reservation and uses
the plain `manifest.json`/`provenance.json` pair (unlike WebP's
`census-manifest.json`).

## Provenance and reproduction

- Local rehearsal (this directory's `observations.json`): pinned source
  `b0859fc61fea5dedfc898aece6b15893ddf4314e`, built from that source with
  the same CMAKE options as the CI `pinned-exiv2-baseline` job (static
  zlib 1.3.1 / expat 2.6.4 / brotli 1.1.0 / inih r56, `BUILD_SHARED_LIBS=OFF`,
  `BROTLI ON`, `BMFF ON`); observations recorded with
  `E01_SOURCE_COMMIT=<rehearsal git sha>`. Raw JSON is committed as-is.
- CI (authoritative gate): the `pinned-exiv2-baseline` job rebuilds the
  pinned CLI, regenerates the fixtures and byte-compares them against the
  committed manifest, and re-runs all batches (`observe_b0.py --batch all`,
  `observe_b2.py`, `observe_b3.py`, then `observe_b4.py`) without
  infrastructure failure. The job never gates on observation *outcomes* (a
  refusal is a finding, not a failure); it gates on infrastructure
  integrity (pin drift, modified upstream, fixture regeneration/pin
  mismatch) and uploads `e01-b4-census-observations` (30-day retention)
  next to the B0/B1/B2/B3 artifacts. Artifact bytes are not retrievable
  from the dev sandbox (host allowlist); the receipt documents the run, and
  no byte-equality between the CI artifact and the committed local
  rehearsal is claimed.
- Reproduce locally: build the pinned CLI as in that job (or use any local
  0.28.9-pinned build), then run `observe_b4.py`; it verifies the pin,
  regenerates fixtures and compares their SHA-256 against the committed
  manifest, runs every applicable census row of batch B4, and writes
  `observations.json`.

## Limits and honest claims

- One owned fixture family per axis point; findings generalize to these
  fixtures only. Classic little-endian II/42 TIFF only: no BigTIFF, no
  MM (big-endian) byte order, no multi-page chains beyond the IFD0→IFD1
  thumbnail chain, no compressed (JPEG/PackBits/LZW) image data, no
  multi-strips beyond one, no planar-separated samples, no SubIFD carriers
  (duplicate carriers are authored as **repeated entries in one IFD0**, an
  invalid-but-authored layout on purpose — the CLI's duplicate handling is
  what is being recorded), no corrupt/truncated files.
- XMP rows deliberately omit the pure-ASCII encoding cell (byte-identical
  to utf-8 behavior); IPTC and thumbnail rows carry no encoding axis
  (`not-applicable`). Unknown-raw READ rows reuse the single-payload
  fixture; unknown-raw WRITE rows are record-only.
- The census records *baseline behavior*, not support. MetadataSharp
  verification stays `not-assessed` everywhere until E07.
- No denominator is frozen and no coverage percentage is computed or
  implied. The single static not-applicable row
  (`EX0289-TIFF-THUMBNAIL-INSERT-0002`) is an exclusion candidate pending
  the developer's denominators review; exclusions are never self-granted.
- `denominatorFrozen: false` everywhere; freeze remains a separate,
  developer-authorized step after B14.
