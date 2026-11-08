# E01-CEN — finite atomic denominator design (proposal)

Status: **DESIGN REVIEWED AND APPROVED 2026-11-08 (developer; six §7 decisions recorded
in §7 below). B0 pilot execution opened 2026-11-08; observations and fixture authoring
are pending. Denominators remain UNFROZEN until the census executes and no-claim cells
are observed.** It defines the census row schema, the fixture provenance workflow and
the proposed per-format batching of the 31×6 capability grid so that E01 can eventually
freeze a denominator. Until the denominators are computed and reviewed, **no parity
percentage and no "complete" claim exists**, and nothing in this document authorizes
implementation, exclusions, publication or any new public contract. Baseline pins: Exiv2 **v0.28.9**, annotated tag
`110cbaef8e75f0e064e5c522846a18982bc4db70` → commit
`b0859fc61fea5dedfc898aece6b15893ddf4314e`; documentation blob
`149f53b809c182bb4145567e28c466965f7f6505`. Pinned CLI v0.28.9 build/run remains a
black-box oracle only; ExifTool 12.76 remains a secondary differential reference.
E01 seeds (186 cells / 348 candidate operations) are a starting ledger, **not** the
denominator; `denominatorFrozen` stays `false` until review closes.

## 1. Why a census before any implementation batch

The parity directive (2026-11-08) is user-visible coverage, not implementation or binary
parity. A percentage is only honest if the denominator is finite, reviewed and frozen.
Appendix A restates the discovery grid; Appendix B shows one illustrative expansion.
This design produces that denominator by enumeration and review — never by counting what
we happen to implement.

## 2. Census row schema (`schemaVersion: 1`)

Artifact (future): `docs/research/EXIV2_CENSUS.json` (name is part of this proposal).
Header block pins: `schemaVersion`, `baseline` (version/commit/tag/documentation blob),
`generatedBy` (generator name + commit), `created`, `denominatorFrozen: false` until
review, and an `enumerations` block (see 2.4) that is the ONLY legal source of axis
values.

Each row is one atomic, independently verifiable behavior:

| Field | Meaning | Rule |
| --- | --- | --- |
| `rowId` | `EX0289-<FORMAT>-<FAMILY>-<OP>-<NNNN>` | Unique; prefix encodes the baseline pin (`EX0289`). Baseline upgrades mint a new prefix; IDs are never reused. |
| `format` | One of 31 documented format rows (exact manual spelling: ARW…XMP, see A) | Closed enum; new format ⇒ new census, not a drive-by row. |
| `family` | `exif` \| `iptc` \| `xmp` \| `comments` \| `icc` \| `thumbnail` | Closed enum, matches the manual's six categories. |
| `operation` | `inventory` \| `read` \| `insert` \| `replace` \| `remove` \| `extract` | `inventory` = recognize/count/dimensions/MIME without decoding (own track, §5.4). `extract` = copy an opaque blob out (ICC profile, thumbnail, comment payload) without decoding. Preservation is a **test dimension** on mutation operations, never an operation. |
| `valueType` | Scalar shape: e.g. `short-int`, `long-int`, `unsigned-rational`, `signed-rational`, `ascii`, `undefined-bytes`, `datetime`, `enum`, `xmp-structure`, `opaque` | Open list bounded by `enumerations`; every value used must be listed there. |
| `textEncoding` | Character encoding of textual payloads: `ascii`, `utf-8`, `utf-16le/be`, `utf-32`, `cp1252`, `unknown/raw` | Same bound-by-enumerations rule. |
| `layout` | Container layout variant: e.g. `strips-inline`, `strips-external`, `tiles`, `ifd0-exif-subifd` nesting, `extended-xmp-guid-parts`, `bmff-item-extents`, `jxl-container-vs-codestream`, `png-chunk-placement`, `webp-vp8x-chunks`, `sidecar-adjacent` | Per-format enumeration table, grown per batch via PR + review (§2.4). |
| `duplicates` | `none` \| `first-wins` \| `multiple-independent` \| `refused-ambiguous` \| `unknown` | Applies to tags/datasets/IFDs/XMP paths/thumbnails alike. |
| `sidecar` | `none` \| `xmp-present` \| `xmp-applied` | Presence is in scope; sidecar **writes** belong to E15 workflows and are tracked there, not here. |
| `presence` | `{icc: bool\|unknown, comment: bool\|unknown, preview: bool\|unknown}` | Applicability guard; a mutation row may legitimately exist only when its presence combination can occur. |
| `noDocumentedClaim` | `true` when the source cell is `-` (71) or empty (1, EPS comments) | Such rows live in the full-baseline denominator as `not-run/no-documented-claim` until observed or reviewed; they are **never** auto-excluded. |
| `fixture` | `{ id, path, sha256, length, rights, mutationRecipeRef \| null }` or `null` with `not-run` reason | See §3. `rights` ∈ `owned-synthetic` \| `cc0-verified` \| `public-domain-verified` \| `licensed-embedded` \| `blocked`. A `blocked` fixture cannot yield `verification: tested`. |
| `oracle` | `{ type: specification\|pinned-cli\|exiftool\|independent, detail }` | At least one non-self oracle per expected result. `metadataSharp→metadataSharp` is never an oracle. |
| `baselineObservation` | `not-run` \| `observed-documented` \| `observed-cli` \| `not-applicable` \| `blocked` | `observed-cli` records tool version, date, build flags, exit code and the exact invocation class (no imported expected outputs as tests). |
| `verification` | `not-assessed` \| `tested` \| `refused` \| `accepted-difference` \| `failed` | Refusal never counts as support. `accepted-difference` requires a documented, independently justified reason + developer review. |
| `batch` | Target execution batch (§5) | Assigned at census time; a batch PR moves rows out of `not-run`. |
| `limits` | Reference to applicable finite limits (D0/E02 caps or batch-proposal caps) | New caps require their own reviewed proposal; no silent unlimited acceptance. |
| `notes` | Free text, required when any axis is `unknown` | Unknown axes stay visible, never silently resolved. |

### 2.1 Invariants

1. `rowId` unique; header baseline matches every row's implied pin.
2. `verification=tested` ⇒ `fixture.rights` is an owned/verified class **and** `oracle.type ≠ metadataSharp`.
3. `documentedClaim='-'`/empty ⇒ `noDocumentedClaim=true` ⇒ row present in `D_full` (§6), status may still become `not-applicable` or `blocked`, never deleted.
4. Refusal ⇒ row remains in both denominators, `verification=refused`, reported separately; it is not a parity pass.
5. No row may be added without a matching enumeration entry (2.4) or an explicit `unlisted-axis-value` review note.
6. Duplicate rows (same axes, same fixture, same operation) are merged; near-duplicates stay separate and visible.

### 2.2 Expansion model (formula, not count)

`candidateRows(cell) = |operations(cell)| × |variants(cell)|`, where operations(cell) are:
`insert/replace/remove` + `read` for `Read/Write` cells (4), `read` for `Read` cells (1),
`read` for `-`/empty cells (1), plus `extract` on `icc`/`thumbnail` cells with any claim
(12+15 R/W + 9 Read thumbnails = 36). Appendix A arithmetic: 348 documented candidates
→ 384 with `extract`; `inventory` is a separate 31-row track (§5.4). Variant split
multiplies each cell by its applicable variant count `v(cell)` (layout × duplicates ×
presence × encodings actually possible for that format/family). The frozen denominator is
`Σ rows` across the reviewed enumeration — an empirical number produced by the pilot and
review, deliberately **not** precomputed or promised here.

### 2.3 Versioning

`schemaVersion` increments on any field/enum change; `baseline` changes mint new row-ID
prefixes; census diffs are reviewed PRs; the previous frozen census is archived
(in-place policy, like PROGRESS.md) and never silently rewritten.

### 2.4 Enumerations block

`EXIV2_CENSUS.json` carries `enumerations`: per format, the legal sets for `layout`,
`valueType`, `textEncoding`, `duplicates` values, with a normative source pointer
(specification section or manual page) per entry, and a `reviewStatus`
(`proposed` → `reviewed`) per entry. Axes may not be invented inside rows; they are
added here by PR with review. This is the mechanism that keeps the census finite.

## 3. Fixture provenance workflow

1. **Authored or nothing.** Fixtures are produced by our own generators (pattern:
   `tools/reference/dng_fixtures.py` + C# counterpart, pinned SHA-256 in
   `tests/fixtures/<format>/manifest.json`, provenance in `provenance.json`), or are
   unambiguously licensed external samples (CC0/public domain/explicit embed license)
   with recorded fetch recipe + hash + license text reference. **Never** Exiv2's test
   corpus, issue/discussion attachments, or vendor sample packs without item-level
   rights. No "downloaded to inspect" material enters the repo.
2. **Mutation recipes, not file copies.** Layout/encoding/duplicate variants are
   declared as (base fixture, byte-patch list) in `provenance.json` and re-applied by
   the generator; pinned hashes make the result immutable. Hostile/boundary samples are
   authored the same way and classified by risk in the manifest.
3. **Real-capture gap.** Where sensor-realistic vendor RAW data is required and cannot
   be synthesized, the cell is `blocked-rights` until a verified-license corpus is
   secured (recorded license, fetch hash, redistribution terms). Expected results for
   such fixtures come from the normative spec + pinned-CLI observation, never from
   reading Exiv2's implementation.
4. **Review checklist (per PR touching fixtures):** generator reproducible; both writers
   agree byte-for-byte (where a second writer exists); hashes pinned; provenance
   complete; rights classification explicit; expected-result oracle independent; no PII
   or owner-camera originals unless independently licensed.
5. **Storage.** Small owned fixtures stay in-repo (< 1 MB each unless reviewed); large
   corpora live outside git with manifest hashes + retrieval notes. No git-LFS default.
6. **CI.** Fixture regeneration is cross-checked in CI (as the DNG fixtures are); any
   drift fails the job. Fixture bytes are data, not code, but are immutable once pinned.

## 4. Observation and verification machinery

- **Observation** runs only in CI against the pinned build (source-pinned Exiv2 0.28.9,
  exact tag/commit, recorded build options), plus ExifTool 12.76 as secondary
  differential reference. Observations assert *observed* behavior; they do not patch it,
  and they are not imported as test oracles wholesale — each row's expected result is
  justified independently (spec or CLI observation recorded in the row).
- **Verification** happens in the owning implementation batch (E06–E15), tests committed
  with the unit, CI green across the six OS/TFM jobs, differential reference jobs only
  where applicable.
- **Two-way facts** are gathered at E17; differences are individually classified
  (our defect / reference defect / unsupported-by-us / formatting variation), never
  silently aligned.

## 5. Proposed per-format execution batching of the 31×6 grid

Census execution order, not implementation order (implementation follows §5-order of the
master plan and its approvals). Each batch is one or more PRs, each moving rows from
`not-run` to observed/not-applicable/blocked/refused. A batch is done when its rows are
filled, both denominators are updated and the delta report is attached to PROGRESS.md.
No batch approves exclusions; it only records them as candidates for §6 review.

| Batch | Scope (rows) | Rationale / inputs | Rights & oracle risks |
| --- | --- | --- | --- |
| B0 pilot | JPEG × {exif, comments, icc} full variant split + inventory | Proves schema, generators, pinned-CLI observation flow and report format on the evidence-richest container; reuses owned JPEG fixtures | Low; EXIF oracle = spec + CLI; ICC expected results from ICC spec |
| B1 | JPEG × {iptc, xmp, thumbnail} | Completes JPEG; Extended XMP variants exercised | Low-medium; XMP packet layouts enumeration needed |
| B2 | PNG (+inventory) | eXIf/XMP chunk placement; static vs animation out of scope (recorded) | Low |
| B3 | WebP (+inventory) | VP8X chunk layout; lossy/lossless irrelevant to metadata | Low |
| B4 | TIFF (+inventory) | Strips/tiles, inline/external, IFD nesting; feeds E10 families later | Low |
| B5 | XMP packets + sidecars (+inventory) | RDF structures/qualifiers enumeration; sidecar presence/applied distinction | Medium; XMP spec revision pinning required |
| B6 | DNG (+inventory) | **Reuses D1 ledger/fixtures/oracles** — census rows already 80% closed by D1 work | Low; raw-decode oracle gap recorded (kind: blocker-class, accepted) |
| B7 | BMFF read-only probe set: AVIF/HEIF/HEIC/CR3/JXL (+inventory) | Optional-build pinned observations FIRST; JXL container-vs-codestream split | **High**: rights review per format; build options may make rows `not-applicable`; needs developer sign-off on review scope |
| B8 | JP2/PSD/PGF/EPS/EXV/DCP (+inventory) | E08 inputs; DCP not ordinary TIFF (recorded) | Medium-high; per-format specs |
| B9 | TIFF-derived RAW wave 1: ARW/CR2/NEF/ORF/PEF/SRW (+inventory) | feeds E10; authored synthetic RAW fixtures via TIFF writer extensions | **High**: vendor layout specs needed; no real captures imported; rows may be `blocked-rights` |
| B10 | Wave 2: MRW/RW2/SR2/RAF (+inventory) | RAF documented as partial in baseline (recorded) | High (same) |
| B11 | Legacy/recognition/video: CRW/BMP/GIF/TGA + MOV/MP4/MKV/AVI/WAV/ASF (+inventory) | Inventory-only reconciliation for video; recognition rows only, no metadata editing | **High**: video build-dependence; census records applicability, E11 proposes contracts separately |
| B12 | MakerNotes applicability overlay (all RAW/JPEG/TIFF rows) | Applicability only; implementation is E12 with per-vendor provenance | High; rights per vendor |
| B13 | Workflow/transfer census: timestamp/copy/transfer/fix applicability (E15) | Explicit loss/sync/overwrite policy inputs; not CLI-flag cloning | Medium |
| B14 | Vocabulary census (E06): tag/encoding/unit breadth across existing formats | Cross-format; enumerates valueType/textEncoding axes systematically | Medium |

### 5.4 Inventory track

`inventory` rows for all 31 formats (recognize, dimensions, MIME/container sniffing,
multi-image count where applicable) are one cross-format batch (B0-pilot includes JPEG;
remainder in B1–B4), 31 rows total. Recognition is never metadata support; it closes
nothing else.

### 5.5 Ordering logic

Evidence-rich owned-fixture formats first (B0–B6), rights/build-gated formats next with
their reviews (B7–B11), cross-cutting applicability last (B12–B14). Vocabulary (B14) is
also a dependency of rich value assertions in B1+ and may be promoted earlier by
developer decision — that is an ordering improvement, not a scope change.

## 6. The two denominators — APPROVED 2026-11-08 (decisions recorded)

The developer approved this section as proposed on 2026-11-08: the denominator
definitions and the reporting rule are frozen as written; the exclusion-candidate list
remains identification only (no exclusions signed). Additional binding decisions from the
same review: (i) no-claim rows are included in `D_full` and MUST be attempted via pinned
CLI observation before any freeze; (ii) external corpora are limited to verified
CC0/public-domain samples with recorded license/hashes (small fixtures in-repo, larger
external corpora via manifest) — no other licensing; (iii) BMFF/video rows proceed
census-first with a separate per-format rights review before any fixture is authored,
no write support promised; (iv) sidecar rows are census presence rows only
(`xmp-applied` writes stay E15); (v) ExifTool 12.76 remains the secondary differential
reference alongside pinned Exiv2 CLI for new observations. Both denominators are
published together, always, with counts by status. Neither is computed yet.

- **`D_full` (full-baseline denominator):** every census row generated from the 31×6
  grid × operations × reviewed enumerations — including rows whose source cell is `-`
  (71 cells) or empty (1), rows blocked on rights, rows refused by design and rows not
  yet observed. This is "everything the pinned baseline does or might do in this grid".
- **`D_accept` (accepted-target denominator):** `D_full` minus rows the developer
  explicitly approves as exclusions, each with an ID, reason, reviewer and date, recorded
  in the census and in PROGRESS.md. **No exclusions are granted by this proposal.**
  Candidate exclusions to review (identification only, not approval):
  1. Video-container rows where the pinned build lacks the required optional support,
     or where no ownership-safe corpus exists (expect many B11 rows);
  2. `noDocumentedClaim` rows that cannot be observed and lack a normative source —
     e.g. the EPS comments cell;
  3. CLI convenience behaviors with no .NET service equivalent where the developer
     grants an explicit written waiver instead of a .NET mapping (master-plan §E15 rule);
  4. Rows diverging intentionally for security fixes, counted as `accepted-difference`
     but still rows; they belong in `D_accept`, reported as their own class unless the
     developer removes them by signed exclusion, which is discouraged.
- **Reporting rule (post-freeze):** headline coverage = `verified / D_accept`; secondary
  = `verified / D_full`; with `refused`, `blocked`, `unknown` and
  `noDocumentedClaim-pending` counts always printed alongside both. A refused row is
  never a pass; a blocked row is never dropped.

## 7. What this document is not / review questions — ANSWERED 2026-11-08

Not approval of any format, operation, exclusion, API or timeline; not a schedule; not a
parity claim. Developer answers, 2026-11-08 (PR #22/#23 review round):

1. **Denominators:** freeze §6 as proposed (definitions and reporting rule). Exclusion
   candidates remain unsigned; no exclusion exists yet.
2. **No-claim cells:** include in `D_full`, with a mandatory pinned-CLI observation
   attempt before the freeze.
3. **External corpora:** verified CC0/public-domain only, license and hashes recorded;
   small fixtures in-repo, larger corpora external with manifest. No other licensing.
4. **BMFF (B7):** continue census-first with a separate per-format rights review before
   any fixture is authored; no write support promised or implied.
5. **Sidecar:** census presence rows only; sidecar writes remain E15 scope.
6. **B13 scope:** not blocked on core read coverage; full workflow census runs after
   E07 core read coverage lands (applied default, override by PR).
7. **Secondary oracle:** ExifTool 12.76 retained alongside pinned Exiv2 CLI for new
   observations (rows may be confirmed by both).
8. **Census artifact home:** `docs/research/EXIV2_CENSUS.json` + PR-per-batch review
   cadence (applied default, override by PR).
9. **B14 timing:** kept last; promote earlier only by developer decision (applied
   default, override by PR).

Any change to these answers lands by PR with developer approval, then flows into
PROGRESS.md and CURRENT_STATE.md.

B0 pilot observation completed 2026-11-08: all applicable rows observed with the
pinned CLI (evidence e01-b0-census; 89 accepted / 8 refused — ICC
duplicate-profile rows refuse mutations with `Not a valid ICC Profile`; 38 rows
statically not-applicable by reviewed axis collapses: the `app1-exif-multiple`
layout is itself the duplicate-block case, `com-single` cannot carry duplication).
B1 observation completed 2026-11-08: 57 applicable rows observed (55 accepted;
2 nonzero-exit-artifact-produced — thumbnail extract exits 137/46 in the pinned
CLI with the artifact produced; the JPEG-compressed thumbnail extract is
byte-exact against the authored embedded blob) plus 1 statically not-applicable
row (`exif-ifd1-uncompressed` thumbnail insert: the pinned CLI has no
uncompressed-thumbnail write path; flagged for developer review at denominators).
B1 notable CLI behaviors recorded, not gated: second APP13/IPTC block ignored on
print and mutation context, EXIF IFD1 thumbnail tags print without decoding,
Extended XMP's base packet prints `HasExtendedXMP` only, non-UTF-8 XMP packets
fail decode with rc=0 and a warning, duplicate APP1 XMP first-wins. The coded
scan bytes were preserved in all 193 rows. No denominator is computed here; the
developer freezes both denominators after the full batch sequence per the approved
order (B0 → B1 → denominators).

## 8. Review protocol and exit — CLOSED 2026-11-08 (design); B0 observed

Developer reviewed §2 (schema), §3 (provenance), §5 (batching), §6 (denominators/
reporting) and the §7 answers on 2026-11-08; this document is APPROVED as the design
contract for the census. Changes still land by PR to this document (and the master plan
link). `EXIV2_OPERATION_SEEDS.json` remains `denominatorFrozen: false` until the census
is generated, observed and the developer freezes both denominators. B0 pilot enumeration
generation opened 2026-11-08 (tools/reference/build_census.py); observation and fixture
authoring follow per §3–§5. E01's gate closes only when the developer reviews the frozen
denominator — not before, and not by this document.

## Appendix A — discovery grid (restated, no new claims)

31 formats × 6 families = 186 cells; claims: **78 Read/Write, 36 Read, 71 no-claim (`-`),
1 empty (EPS comments)**. Candidate operations from seeds: 348 (read+insert+replace+remove
for R/W; read for the rest); +36 `extract` candidates on icc/thumbnail cells (27 R/W + 9
Read-thumbnail) ⇒ 384 documented candidates pre-variant-split, **not** a denominator.
Read/Write cells per family: exif 16, xmp 17, iptc 14, thumbnail 15, icc 12, comments 4.
No-claim cells concentrate in recognition-only/legacy/video/DCP/XMP (BMP/GIF/TGA 6 each;
DCP 5; XMP 5) and long-tail (2 each for 17 more). Formats with zero no-claim cells:
JPEG and EXV. Zero-claim ≠ impossible; it is unknown until observed.
Seeds live in `EXIV2_OPERATION_SEEDS.json`; facts in
`EXIV2_DOCUMENTED_FORMAT_FACTS.json`; generation rules in `build_capability_seeds.py`
(`--check` guards drift). All remain `needs-reconciliation` until batches fill them.

## Appendix B — worked example (illustrative, not frozen)

Expanding seed `EX0289-JPEG-EXIF` (`documentedClaim: Read/Write`) illustrates, does not
establish, the enumeration. Variants for JPEG/EXIF (enumeration table `proposed`, excerpt):

| axis | values (proposed) |
| --- | --- |
| layout | `ifd0-inline`, `ifd0-exif-subifd`, `multiple-exif-blocks` |
| duplicates | `none`, `first-wins`, `multiple-independent` |
| textEncoding | `ascii`, `utf-8`, `utf-16le` (for ASCII-ish payloads) |
| valueType | `short-int`, `long-int`, `unsigned-rational`, `datetime`, `undefined-bytes`, `ascii` |
| sidecar | `none`, `xmp-present` |
| presence | `{icc, comment, preview} ∈ {0,1}³` — applicability guard, not 8 mandatory rows |

Operations: `read`, `insert`, `replace`, `remove` (+ `inventory` in its own track).
Sketch: 4 operations × layout(3) × duplicates(3) ≈ 36 base rows before encoding/presence
pruning and before merging equivalents — every pruning step is recorded in the census with
a reason, and prunings are part of review. Expected artifacts: per-row
`baselineObservation`/`verification` slots, one pinned JPEG fixture per mutation-class
variant, oracle pointers, batch `B0`. B0 rows now exist and are observed; nothing here is a
denominator until the pilot and review produce and approve them.

## Appendix C — risks

1. **Rights reality:** real vendor RAWs and video corpora are the highest risk; B9–B11 may
   fill slowly or block; plan honesty requires those rows stay `blocked` visibly.
2. **Build dependence:** optional Exiv2 features (BMFF, WebP/curl, etc.) must be pinned per
   observation; if absent, rows are `not-applicable (build option absent)` — visible,
   never silently dropped or counted as supported.
3. **Axis creep:** enumerations must be closed and reviewed per batch; otherwise the
   "finite" denominator is fiction. The `enumerations` block is the enforcement.
4. **Self-oracle drift:** passing own tests on own fixtures is not coverage; the
   `verification=tested` invariant (§2.1) forbids it.
5. **Scope pull:** B5/B13 pull toward E06/E15 decisions; census records presence and
   applicability only, design decisions stay in their owning proposals.
