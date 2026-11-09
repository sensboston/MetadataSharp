# E01 B5 census observations — pinned Exiv2 0.28.9 / black-box CLI

Scope: the B5 rows of `docs/research/EXIV2_CENSUS.json` — XMP ×
{inventory, exif (noDocumentedClaim read), iptc (noDocumentedClaim read),
comments (noDocumentedClaim read), icc (noDocumentedClaim read), thumbnail
(noDocumentedClaim read), xmp (documented Read/Write)}, plus the four
B5 **sidecar-presence read probes** (JPEG/PNG/WebP/TIFF). A standalone
`.xmp` file **is** an XMP packet document (the sidecar artifact), so the
bytes rewritten by metadata operations are the whole file; there is no
coded payload to preserve and the invariant of this batch is recorded as
SHA-256/length/xpacket-PI-count facts instead. Observation only: the
pinned CLI's behavior is **recorded**, never pre-asserted, except for the
spec-derived XMP expectations on the utf-8 rows and the documented
noDocumentedClaim probes (nothing asserted).

The wrapped layout carries xpacket begin/end PIs + `<x:xmpmeta>` (the same
shape the in-image XMP fixtures of B1–B4 carry); the unwrapped layout omits
the optional wrapper per XMP Part 1 §7.3–§7.5 (editions/blobs pinned in
`docs/design/XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md`).

## What was observed

- 26 rows observed with the pinned CLI; 0 static not-applicable.
- Outcome counts: 17 `accepted`, 2 `accepted-unexpected`,
  6 `accepted-unparseable`, 1 `refused`, 0 `scan-changed`,
  0 `static-not-applicable`.
- Documented-claim utf-8 rows with spec-derived expectations, all held:
  - READ on both layouts prints `Xmp.xmp.CreatorTool XmpText 13
    MetadataSharp` (rc=0);
  - INSERT from nothing (empty `rdf:Description`, both layouts) creates the
    property (rc=0, `-pa` afterwards prints the added value; bytes
    287→357 wrapped, 215→357 unwrapped);
  - REPLACE swaps to `Bob` (rc=0; bytes 352→337 wrapped, 280→337 unwrapped).
  - REMOVE is the anomaly below.
- **REMOVE is a silent no-op in the pinned CLI** (both layouts, utf-8):
  `del Xmp.xmp.CreatorTool` exits rc=0, the file is **byte-identical**
  before/after and `-pa` afterwards still prints `CreatorTool
  MetadataSharp` (`EX0289-XMP-XMP-REMOVE-0001`, `EX0289-XMP-XMP-REMOVE-0003`:
  outcome `accepted-unexpected`). The same CLI no-op class as B3's
  `EX0289-WEBP-XMP-REMOVE-0001`; the JPEG/PNG/TIFF in-image analogs remove
  the packet/tag correctly, the standalone packet file does not. Recorded
  as observed behavior; never a pass; no MetadataSharp mapping promised.
- **Writes normalize the packet**: on every successful write the CLI
  re-serializes with its own XMP Core (`x:xmptk="XMP Core 4.4.0-Exiv2"`),
  attribute-form property serialization, and the **canonical xpacket id**
  `W5M0MpCehiHzreSzNTczkc9d` (the authored id `W5M0MpCehiHzreSzkc9d` is
  replaced). Lexical form, attribute order and packet size are not
  preserved; the wrapped layout is emitted even for the unwrapped input
  (unwrapped insert from nothing: 215→357 bytes, i.e. the canonical wrapped
  form).
- **Raw-XMP failure mode prepends a partial canonical packet**: with an
  invalid-UTF-8 value in argv (`unknown-raw` insert/replace rows,
  record-only), the CLI exits rc=0 with stderr `XMP Toolkit error 4:
  Invalid UTF-8 sequence length` / `Failed to encode XMP metadata`, and the
  output file gains a leading partial packet: `xmpBeginPICount 1→2`, bytes
  287→360 (INSERT-0002), 215→288 (INSERT-0004), 353→426 (REPLACE-0002),
  281→354 (REPLACE-0004). The doubled begin/end PIs are visible in
  `facts.xmpBeginPICountAfter`; the packets remain well-formed XML with a
  stray prolog PI. Recorded, not asserted.
- unknown-raw READ/REMOVE rows (`READ-0002/0004`, `REMOVE-0002/0004`):
  rc=0 with stderr `XMP Toolkit error 201: Error in XMLValidator` /
  `Failed to decode XMP metadata` / `Invalid UTF-8 sequence length`; the
  file is not rewritten (READ untouched) or not rewritten by the remove
  path; strict parse fails with `not utf-8` → `accepted-unparseable`
  (rc=0 with unparseable output is never reported as accepted).
- **Inventory is refused**: `-pS` on a bare `.xmp` prints
  `Image type /…/xmp-base.xmp is not supported` (rc=1). The pinned CLI does
  not inventory standalone XMP packets. Recorded as `refused`, never a
  parity pass. (`-pa` reads the embedded XMP fine; see the noDocumentedClaim
  rows.)
- The five noDocumentedClaim rows (exif/iptc/comments/icc/thumbnail, claim
  `-`) observe rc=0 with `-pa` printing the embedded `Xmp.xmp.CreatorTool`;
  the cells carry no documented claim, so nothing is asserted.
- Sidecar-presence read probes (JPEG `app1-xmp-standard`, PNG `itxt-xmp`,
  WEBP `webp-xmp`, TIFF `tiff-xmp`): each copies the pinned image fixture
  next to an authored `<stem>.xmp` carrying `CreatorTool=SIDECARPROBE`; the
  pinned CLI opens the image only — `SIDECARPROBE` never appears in the
  printed output for any of the four formats (rc=0, image's own token
  printed; `facts.sidecarTokenInStdout: false`). The presence/applied
  distinction is the observation; `xmp-applied` writes stay E15 per the
  approved denominators; sidecar **writes** are not exercised here.
- All observations: zero `scan-changed`; image fixtures in probe rows are
  byte-identical (read-only).

## Fixtures

All packet fixtures are owned synthetic XMP packets authored by
`tools/reference/xmp_owned_fixtures.py` (stdlib writer, same shape family as
the in-image packets of the JPEG/PNG/WebP/TIFF batches). The pin set is
**6** fixtures, pinned by SHA-256 in `tests/fixtures/xmp/manifest.json`
(regeneration is byte-deterministic; CI byte-compares); rights:
`owned-synthetic`, MIT. See `tests/fixtures/xmp/provenance.json`. **No
fixture binaries are committed.** The sidecar probes reuse the already-pinned
JPEG/PNG/WebP/TIFF image fixtures (validated against those manifests at run
time; the sidecar itself is an ephemeral work-tree artifact, never a pinned
fixture).

## Provenance and reproduction

- Rehearsal used for this directory's `observations.json`: pinned source
  `b0859fc61fea5dedfc898aece6b15893ddf4314e`, built from that source with
  the same CMAKE options as the CI `pinned-exiv2-baseline` job, on a
  Debian (bookworm-class) sandbox with hand-built static dependencies —
  zlib v1.3.1, expat R_2_6_2, brotli v1.1.0, inih r59 (static), CMake
  3.28.4, GCC 12.2.0, `BUILD_SHARED_LIBS=OFF`, `BROTLI ON`, `BMFF ON` —
  with `CMAKE_PREFIX_PATH` pointing at the dependency prefix. The pin is
  the source commit and the option set, not the machine; the committed
  `observations.json` records `baseline.executableSha256`
  (`640b410e…`), `cmakeCacheSha256` and the license SHA-256.
- CI (authoritative gate): the `pinned-exiv2-baseline` job rebuilds the
  pinned CLI, regenerates the XMP fixtures and byte-compares them against
  the committed manifest, and re-runs all batches (`observe_b0.py
  --batch all`, `observe_b2.py`, `observe_b3.py`, `observe_b4.py`, then
  `observe_b5.py`). The job never gates on observation *outcomes* (a
  refusal is a finding, not a failure); it gates on infrastructure
  integrity (pin drift, modified upstream, fixture regeneration/pin
  mismatch) and uploads `e01-b5-census-observations` (30-day retention).
  Artifact bytes are not retrievable from the dev sandbox (host allowlist);
  the receipt documents the run, and no byte-equality between the CI
  artifact and the committed rehearsal is claimed.
- Reproduce locally: build the pinned CLI as in that job (or any
  locally built b0859fc61-pinned equivalent), then run `observe_b5.py
  --upstream artifacts/e01/source --build artifacts/e01/build`; it
  verifies the pin, regenerates fixtures and compares SHA-256 against the
  committed manifest, runs every applicable census row of batch B5, and
  writes `observations.json`.

## Limits and honest claims

- One owned fixture family per axis point; findings generalize to these
  fixtures only.
- Layout scope: wrapped vs optional-wrapper-omitted packets (XMP Part 1
  §7.3–§7.5), UTF-8 only. **Deliberate non-goals of this batch:** leading
  raw BOMs outside the xpacket PI; double/concatenated packets authored on
  purpose; Extended-XMP segmented layouts (a bare packet file has no
  segments — for in-image Extended XMP see B1); UTF-16/32 packets;
  RDF structures/qualifiers/LangAlt enumeration (that is E06 vocabulary
  breadth; census rows here treat packets as `text-bytes`); a property
  dictionary of any kind.
- `unknown-raw` cells use invalid-UTF-8 payloads (record-only); a pure
  ASCII packet is deliberately not a separate cell (byte-identical to the
  utf-8 behavior, same pruning as B1–B4).
- Duplicate carriers (`first-wins`) are deliberately not enumerated for the
  standalone format: a packet file carries a single packet; the
  duplicate-container axis is exercised by the in-image carrier batches
  B1–B4. Recorded pruning.
- The census records *baseline behavior*, not support. MetadataSharp
  verification stays `not-assessed` everywhere until E07.
- No denominator is frozen and no coverage percentage is computed or
  implied. `denominatorFrozen: false` everywhere; the freeze remains a
  separate, developer-authorized step after B14.
