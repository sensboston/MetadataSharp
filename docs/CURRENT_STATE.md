# Current development state and recovery checkpoint

**E01 B8 census batch is MERGED** via PR #48 (master `8825377`; master run
42724819709 green 15/15): 53 rows — 31 `observed-cli` from the pinned CI Exiv2 0.28.9
job plus 22 explicit `blocked` rows for PGF/EXV/DCP, whose fixture and oracle review is
still pending. Census total is **568 rows**, 102 seed cells enumerated, 84 pending,
`denominatorFrozen: false`. Evidence: `docs/research/evidence/e01-b8-census/`
(CI-only, plus a second CI run kept as a cross-run reproduction sample; there is no
local run and none is claimed). E08's bounded read-only PSD/JP2/EPS inspection merged
earlier via PR #47. Still open from B8: every write suboperation, PSD ICC/thumbnail
resources, and the JP2/EPS metadata-present fixtures. CR3 remains blocked; no
denominator freeze, MetadataSharp verification, publication or DNG D2 write is
authorized. Next unit in §5 numeric order: **B9** (RAW wave 1). See the B8 checkpoint
and "B8 census closed" sections below.


B7 bounded census merged via PR #43; receipt in
`docs/research/evidence/e01-b7-census/` (its working branch has since been pruned). Read
first; verify against live branch/Git/PR/CI. Chronological approvals/diagnostics
remain in PROGRESS; older session instructions there are history.

## Preliminary status summary — 2026-11-09 (at master `33e3a88`)

**Product.** MetadataSharp is an MIT-licensed managed .NET 8/10 library for image
metadata editing without pixel decoding or recompression. Implemented, audited and
merged (M00–M18): restricted JPEG EXIF/IPTC/XMP editing; static PNG/WebP EXIF/XMP
editing; standalone TIFF layout accounting + restricted descriptive editing (M15/M16);
XMP sidecars (bounded RDF profile); IPTC IIM codec (M12a/b); narrow read-only DNG
inventory profile (D1, PR #21, TIFF DNG refusal unchanged); additive
`MetadataCapabilities` discovery API (E03, merged `ea8e056`). Zero third-party
runtime dependencies; net8.0+net10.0 dual targeting; Framework 4.8.x deferred;
564 passed in latest green CI (master run 42724819709 at `8825377`, net8.0/macos
annotation; floor 450 per OS/TFM). Packages remain local audit candidates; **publication NOT
authorized**.

**Exiv2 programme (E00–E18; pinned baseline v0.28.9 / b0859fc61).**
- Merged/approved: E00; E01 bounded pinned-CLI foundation (PR #16); E02 dual
  targeting (42446034373); E03 contracts + implementation; E04/D0 exact DNG proposal
  approved (no implementation); E05/D1 narrow read-only DNG (PR #21); E01-CEN census
  design approved 2026-11-08.
- Census (`docs/research/EXIV2_CENSUS.json`, `--check` green): B0–B5 merged;
  B6 merged via PR #39 (`e6325be`, run 42698556762, 15/15 green). B7 bounded evidence merged via PR #43 (`514ed0b`). B8 enumerated
  on the current branch (PR #48): census has 568 rows (B0–B7 515, B8 53). B0–B7 hold 465
  observed-cli, 42 static not-applicable and eight CR3 blocked-on-rights rows. B8 adds 31
  observed-cli read-only probes and 22 blocked-rights rows, with 0 scan-changed and every
  input byte-identical; the resource-bearing PSD's four `-pa` family rows share one
  identical 206-byte print, so no per-family attribution is claimed. B8 evidence is CI-only
  (annotation recovery, three chunks, hashes in `receipt.json`). B6 CFA strip bytes preserved 88/88 in local observations;
  no raw decode or rendered equivalence is established. Four thumbnail reads
  produced matching output but returned 137 and are classified separately.
  All rows `verification: not-assessed`; `denominatorFrozen: false`; no coverage
  percentage. B0–B5 receipts are metadata-only; B6 evidence is under `docs/research/evidence/e01-b6-census/`; the receipt
  recovers CI observations from complete check-run annotation chunks and records
  an exact match of all 88 observation objects and the fixture manifest.
- Enumeration breadth: JPEG, PNG, WebP, TIFF, XMP, DNG, AVIF, HEIF, HEIC,
  CR3, JXL, PSD, JP2, EPS, PGF, EXV and DCP; 102 seed cells enumerated, 84 pending.
  CR3 and the three pending B8 formats are enumerated but
  remain blocked, not observed or excluded. B6 fixture and oracle limitations are recorded in
  its evidence README; no exclusion or DNG write support is implied.
- Next in §5 numeric order: **B9** (RAW wave 1), with the B7 rights/metadata gaps and B8's
  own gaps retained → … → B14, then the final freeze PR
  (developer-authorized). B8's open gaps stay visible: JP2/PSD metadata-present variants,
  PSD ICC/thumbnail resources, every B8 write suboperation and PGF/EXV/DCP fixtures.
  Not started: E06 typed vocabulary
  breadth; E07 container gap closure + MetadataSharp-side verification (until then
  verification stays not-assessed); E08–E12 new format families; E13–E15
  editing/preview/workflow breadth; E16 evidence-backed improvements; E17
  reconciliation (two denominators, two-way reference facts); E18 release audit
  (publication approval separate). D2/public write-up NOT authorized.
- GitHub availability: transient GitHub-side Actions failure 2026-11-09T18:23Z hit
  the two first runs of PR #33 (2/0 of 15 jobs started, no logs); a PR
  close/reopen produced 15/15 green runs with no content changes. Recorded in the
  B4 receipt. B5 merged via PR #37 (`7b4bc97`, run 42683793923, 15/15 green);
  receipt at docs/research/evidence/e01-b5-census/receipt.json.
  githubstatus.com is not reachable from this sandbox; live status was
  verified via api.github.com ("GitHub lives!") and observed CI results.

**Standing refusals/limits.** DNG D2 write: NOT authorized. Package publication:
NOT authorized. Exclusions: none signed. Denoms: frozen only after B14. E01 gate
open. Retained absolutes: own fixtures only; pinned-CLI black-box observation;
committed evidence; green-CI merges; ≥450 TRX six OS/TFM combos; net8.0+net10.0;
no TIFF DNG weakening.

**Branch hygiene.** Completed on 2026-11-10: all **10** remote `arena/*` branches were
deleted after proving each one fully merged — `gh api .../compare/master...<branch>`
returned `ahead_by: 0` for every branch, and each head commit mapped to a merged PR
(#36–#48) via `.../commits/<sha>/pulls`; `gh pr list --state open` was empty, and the
active branch and `master` were excluded. `origin` now carries only `master`. PR pages
and their file lists remain readable after deletion (checked on #48: 16 files), so no
evidence or discussion was lost. Because this clone is **shallow** (`.git/shallow`,
`rev-list --count HEAD` = 1), `git rev-list --count origin/master..<branch>`
over-reports divergence and must not be used as a merged-ness proof; use the compare API
or `git diff --name-only --diff-filter=A origin/master <branch>` (0 files = nothing
unmerged). That check also showed the previous looping session stranded no work.

**Live verification:** `gh pr list`, latest master CI run,
`python3 tools/reference/build_census.py --check`,
`python -m unittest discover -s tests/discovery`,
`python -m unittest discover -s tests/reference`.

## Active branch and scope

Assigned branch **arena/308dfd7b-metadatasharp** (post-B8 housekeeping: merged-branch
prune and recovery-checkpoint refresh; based on master `8825377`, which already contains
B8 via PR #48 and the E08 PSD/JP2/EPS work via PR #47); never switch to master or
another branch. Standing authorization
covers commit/push and completed green PR merge, not package publication or
unseen public contracts. The previous public-visibility billing blocker is resolved
in observed runs; no CI exception applies.

Programme: roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md (E00–E18).
Developer approved independent near-complete Exiv2 functionality, idiomatic .NET,
.NET 8+10, conditional Framework only if cheap. No C++ hierarchy/CLI clone or GPL
implementation/tests/dictionaries imported. Rights/provenance, finite input limits,
unknown relocation refusal and independent metadata families stay binding.
2026-11-08: developer directed full Exiv2-level user-visible capability and
**avoiding external dependencies where possible**; binding dependency policy
(zero third-party runtime deps; BCL-only; no analyzers/natives/embedded tools)
is in DEVELOPMENT.md section 7 and EXIV2_PARITY_MASTER_PLAN.md section 2.

## Current evidence and actual implementation

- B0, B1, B2, B3, B4 and B5 observation batches DONE (2026-11-08/09): docs/research/EXIV2_CENSUS.json
  carries 379 rows (135 B0 + 58 B1 JPEG, 67 B2 PNG, 43 B3 WebP, 50 B4 TIFF, 26 B5 XMP); every B0 row is
  observed-cli (97) or statically not-applicable (38, axis collapses recorded per row) and
  every B1 row is observed-cli (57) or statically not-applicable (1:
  `exif-ifd1-uncompressed` thumbnail insert — the pinned CLI exposes no
  uncompressed-thumbnail write path) and every B3 row is observed-cli (42) or statically
  not-applicable (1: the same axis collapse on the WebP fixtures); every B4 row is
  observed-cli (49) or statically not-applicable (1: the same axis collapse on the TIFF
  fixtures) and every B5 row is observed-cli (26; 0 static not-applicable).
  Fixtures owned-synthetic and SHA-256 pinned (tests/fixtures/jpeg/manifest.json,
  31 fixtures; tests/fixtures/png/manifest.json, 17 fixtures;
  tests/fixtures/webp/census-manifest.json, 12 fixtures — NOTE: tests/fixtures/webp/provenance.json
  is reserved for the .NET library's PNG/WebP-group audit and is untouched;
  tests/fixtures/tiff/manifest.json, 14 fixtures, plain manifest/provenance pair — no .NET audit
  reservation; tests/fixtures/xmp/manifest.json, 6 owned XMP packet fixtures); evidence
  committed at docs/research/evidence/e01-b0-census/,
  docs/research/evidence/e01-b1-census/, docs/research/evidence/e01-b2-census/,
  docs/research/evidence/e01-b3-census/, docs/research/evidence/e01-b4-census/ and
  docs/research/evidence/e01-b5-census/
  (observations.json + README); CI job `pinned-exiv2-baseline` re-runs all batches and
  uploads `e01-b0-census-observations`, `e01-b1-census-observations`,
  `e01-b2-census-observations`, `e01-b3-census-observations`, `e01-b4-census-observations`
  and `e01-b5-census-observations`.
  B5: 17 accepted / 2 accepted-unexpected (REMOVE is a silent no-op in the pinned CLI on
  both layouts — rc=0, file byte-identical; B3 WebP analog) / 6 accepted-unparseable
  (unknown-raw READ/REPLACE: rc=0 with `XMP Toolkit error` on stderr; REPLACE rows gain a
  partial leading canonical packet, xpacket PI counts 1→2) / 1 refused (`-pS` on a bare
  .xmp — `Image type ... is not supported`, rc=1) / 0 not-applicable; the four
  sidecar-presence read probes record SIDECARPROBE token absent from printed output
  (adjacency never consulted; applied writes stay E15); zero scan-changed; B5 packets
  carry no coded payload (SHA-256/length/xpacket facts recorded instead). B4: 49 accepted / 0 accepted-unexpected (all 49 rows rc=0; XMP `del` removes the
  whole 0x02BC tag unlike the WebP silent no-op; duplicate-carrier writes are
  first-wins while `-pa` prints last-wins; IPTC duplicates `del` is a silent no-op;
  ICC writes collapse duplicates to a single entry; ICC tag 0x8773 is rewritten as
  type 7 UNDEFINED) / IFD0 strip/tile payload bytes preserved 49/49. B3: 41 accepted /
  1 accepted-unexpected (`EX0289-WEBP-XMP-REMOVE-0001`: `del Xmp.xmp.CreatorTool` rc=0
  but the raw XMP chunk is never rewritten; PNG B2 analog accepted) /
  VP8-bitstream chunk payload stream preserved 42/42. B3 noDocumentedClaim cells (WebP
  comments/IPTC) observed with rc=0 and nothing surfaced; nothing asserted (cells carry
  no documented claim). B0: 89 accepted / 8 refused (all ICC duplicate-profile
  rows; `Not a valid ICC Profile`). B1: 55 accepted / 2 nonzero-exit-artifact-produced
  (thumbnail extract exits 137/46 by CLI quirk with the artifact produced; JPEG
  thumbnail extract verified byte-exact) / 1 not-applicable. B2: 66 accepted / 1 static.
  Scan preserved 284/284 observed in-image rows (193 B0-B2 + 42 B3 + 49 B4; B5 = packet documents,
  no coded payload). Verification stays not-assessed; NO
  denominator frozen; NO coverage percentage; exclusions (if any) go to the
  developer at the denominators step; D2/publication NOT authorized.
  2026-11-09 (step 3 ANSWERED, freeze pending B2–B14): the developer answered the
  pre-freeze review (Q1=(a) one final freeze after all remaining batches incl. B14;
  Q2 thumbnail-insert stays not-applicable, no exclusions signed; Q3 nonzero-exit-artifact-
  produced its own class, never a pass; Q4 numeric order B2→B14, all in this cycle;
  Q5 no percentages pre-freeze). Recorded in EXIV2_E01_CENSUS_AND_DENOMINATOR_PROPOSAL.md
  §9; freeze happens in its own PR after B14; gate stays OPEN; B4 (TIFF) implemented;
  B5 (XMP) and B6 (DNG) merged; B7 follows.
- B2 merged via PR #29 (`ba52ac1`, run 42558351437, 15/15 green); receipt at
  docs/research/evidence/e01-b2-census/receipt.json (metadata-only; artifact bytes
  not retrievable from the sandbox).
- B3 merged via PR #31 (`f119a45`, run 42661263750, 15/15 green); receipt at
  docs/research/evidence/e01-b3-census/receipt.json (metadata-only; artifact bytes
  not retrievable from the sandbox). B3: 41 accepted / 1 accepted-unexpected
  (`EX0289-WEBP-XMP-REMOVE-0001` — `del Xmp.xmp.CreatorTool` rc=0 but the raw XMP
  chunk is never rewritten) / 1 static not-applicable; VP8-bitstream payload stream
  preserved 42/42; verification stays not-assessed; no percentage; D2 NOT authorized.
- B4 merged via PR #33 (`1720d7b`, run 42674518556, 15/15 green); receipt at
  docs/research/evidence/e01-b4-census/receipt.json (metadata-only; artifact bytes
  not retrievable from the sandbox). NOTE: the two first runs failed on the GitHub
  side with 2/0 of 15 jobs started; PR close/reopen fired the green run, no content
  changes. Census 353 rows; all 49 applicable rows observed-cli (0 accepted-unexpected);
  1 static not-applicable (`EX0289-TIFF-THUMBNAIL-INSERT-0002`); IFD0 strip/tile
  payload bytes preserved 49/49.
- B5 merged via PR #37 (`7b4bc97`, run 42683793923, 15/15 green). The first PR run
  failed all six OS/TFM jobs on the closed layout enumeration (`xmp-packet` missing
  for the comments/exif/icc/iptc/thumbnail families) — a real CI catch, fixed in
  81281d6; the twin push run additionally showed three id-less GitHub-side phantom
  failures (same class as the 18:23Z PR #33 incident). Receipt at
  docs/research/evidence/e01-b5-census/receipt.json. 26 rows,
  outcome counts 17 accepted / 2 accepted-unexpected / 6 accepted-unparseable /
  1 refused / 0 static not-applicable / 0 scan-changed; inventory track extended to
  XMP; sidecar-presence read probes record the sidecar token absent from the
  image's printed output on JPEG/PNG/WebP/TIFF; Census 379 rows; denominatorFrozen
  stays false; no percentage; E01 gate OPEN; B6 in progress.

- E00 and D0/E04 documentation PR #14 merged at 716c00b, final 74bd83e passed
  42445526473/42445522806. D0 exact contract explicitly APPROVED, not implemented.
  No need to ask that same approval again unless its promise changes. D1 still
  requires section 5 normative type/count/location/default ledger, owned valid
  fixtures/complementary independent oracles and E02/E03 compatibility gates.
- E02 code head **2949589c1908b9da80289670b89c062d03bc5af0** passed required
  [42446034373](https://github.com/sensboston/MetadataSharp/actions/runs/42446034373):
  **14 jobs green**: 450 .NET cases per six OS/TFM combinations, two required
  reference jobs and six target-specific baseline/package/consumer audit jobs.
- Core/Formats/tests/reference/audit tools target net8.0;net10.0; root global.json
  selects stable SDK10 feature band, C#12 unchanged. Tests assert actual host major
  and loaded test/library TFM, not merely SDK presence. Packages strictly require
  both DLL/XML sets and exact per-target dependencies. Consumers use isolated
  source/cache and own SDK-major global.json: SDK8.0.427 for net8, SDK10.0.403 for
  net10 observed on all OSes. No production parser/public-member changes.
- Public declared reflection signatures equal between runtimes and all six audit
  receipts (E02 receipt; predates the approved E03 additions implemented on this
  branch): 633 entries, hash a9cd2ae9fe864fbf39da5bc9fd4bfb32fa720f4afc024ac81b53cd1f3bdb93cb.
  NOT full attribute/default/nullable/ABI or behavioral compatibility proof.
  JSON/receipt: audits/evidence/e02; exact limitations/commands:
  audits/DOTNET_8_10_SUPPORT_AND_E02_AUDIT.md. Historical M18 evidence kept separate.
- Initial E02 failures were two old hardcoded-net8 tests and runtime-dependent
  empty-zlib test fixture. Made target expectations match executing framework and
  authored canonical valid empty zlib; extra test still rejects empty/truncated
  framing. No production BoundedZlib relaxation or case skipped. Floor now 450.
- Framework 4.8/4.8.1: defer/do not add; cost map did not establish "easy" support.
  No legacy spike/runtime verification or impossibility claim. No conditional
  parser fork/shims/native bridge/degraded async. Source builds need SDK10; net8
  package consumption with SDK8 is separately proven. .NET8 servicing still ends
  2026-11-10; .NET10 recommended for new consumers. No publication authorization.

## E01 state and next gates

Pinned Exiv2 v0.28.9 / b0859fc61fea5dedfc898aece6b15893ddf4314e remains discovery
baseline; required 0.27.6 CI references are historical regression oracles, NOT
0.28.9 parity proof. Metadata facts/selected issue/discussion requirements and
sources stay in docs/research. No source patches or camera attachments imported.

New E01 seed ledger: 186 documentation cells, 348 **candidate** operations, stable
IDs and deterministic generator. Read/Write does not prove each inferred suboperation;
no-claim cells are retained, not excluded. Every seed marks variants unsplit, CLI
unrun, rights unreviewed and MetadataSharp verification unassessed. Six policy tests
prevent fabricated support/exclusions/percentages. Full denominator/CLI build/
corpus/tag/layout/video/workflow census remains OPEN, no parity percentage claimed.

PR #16 bounded E01 CLI unit: implementation 601a20b passed 42449222307,
15/15 jobs including source-pinned external 0.28.9 build and four owned JPEG ASCII
Artist read/insert/replace/remove observations. Complete raw field/print/scan and
source/build/dependency identities in research/evidence/e01-pinned-jpeg; scope and
findings in research/EXIV2_PINNED_BASELINE_EXECUTION.md. Raw namespace catalogs and
upstream source/executable are NOT committed or distributed. Web/curl OFF in this
configuration is unobserved breadth, not an accepted exclusion. FINAL head
5b0783bf2a60844e82ab2a61479816d01b15d4b9 also passed both required final runs
(PR42449478931 and push42449474066, 15/15 each) and was merged as 75ad10d with the
final immutable source/run/merge receipts posted on PR #16. This bounded CLI
foundation is DONE; do NOT infer E01 census closure, E03 approval, D1 progress or
any parity/percentage from it.

Important: all THREE reference writes have false exact non-EXIF preservation flags:
D001 exact authored COM trailing-NUL addition/order; D002 single-property XMP
reserialization on replace/remove, independently checked against literal property.
These are recorded baseline differences, NOT successful safe-writing parity or
permission to modify our writer. Default/read checker remains strict; only exact
known differences classified, all other segment payloads/multiplicities remain
strict. MetadataSharp status unscored; full E01 census/denominator still OPEN.

Next open units (as of 2026-11-08):
1. **E03 APPROVED AND IMPLEMENTED (this branch)** — capability-discovery surface
   (`MetadataContainer`/`MetadataFamily`/`MetadataOperation`/`CapabilityStatus`/
   `CapabilityDescriptor`/`MetadataCapabilities`), pinned test table, test-local
   zero-`PackageReference` guard; net8.0/net10.0 retained per developer.
   MERGED 2026-11-08T17:34:49Z as ea8e0567c92cbd4691faa7c1a027764e3daccc3b (final head
   bb473f0; push 42505415670 and pull_request 42505425014 both 15/15). Discharges
   only the E03 compatibility gate; it authorizes no per-format work, exclusions or
   publication.
2. **E01-CEN** — finite atomic denominator design; DESIGN APPROVED 2026-11-08
   (decisions in docs/design/EXIV2_E01_CENSUS_AND_DENOMINATOR_PROPOSAL.md §6/§7).
   B0 pilot census generated 2026-11-08: docs/research/EXIV2_CENSUS.json (135 rows,
   all not-run; JPEG × {exif, comments, icc} + inventory; 183 cells pending);
   generator tools/reference/build_census.py (+ --check in CI); policy tests
   tests/discovery/test_census.py. Observations (pinned CLI, no-claim attempts) and
   fixture authoring pending; denominators NOT frozen; no parity percentage or
   "complete" claim is possible until the developer freezes both denominators.
3. Atomic E01 breadth (per-format pinned CLI observations beyond JPEG EXIF Artist)
   may proceed only as batches defined by E01-CEN, with owned fixtures/rights review.
4. **D1 — narrow read-only DNG inventory profile** (2026-11-08 go-ahead):
   **merged 2026-11-08** ([PR #21](https://github.com/sensboston/MetadataSharp/pull/21),
   merge `0d4a516`; final head `7d5fc2d`; all fifteen jobs green in
   [run 42511505561](https://github.com/sensboston/MetadataSharp/actions/runs/42511505561)).
   Gates: 1 (normative ledger, docs/design/DNG_D1_NORMATIVE_LEDGER.md), 2 (owned
   fixtures + structural oracle + pinned ExifTool 12.76 inventory,
   docs/design/DNG_D1_FIXTURES_AND_ORACLES.md; two independent writers byte-compared
   in CI; raw-decode oracle gap recorded as blocker-class, accepted, never a silent
   skip), 3 (52 xunit cases + offline Python policy suite, both shapes × II/MM),
   4 (E02/E03 available). TIFF's DNG refusal is unchanged. No decode/render/write,
   no parity percentage. CI hardening before merge is chronicled in PROGRESS.md
   (strip-range accounting/global overlap check, thumbnail fixture SamplesPerPixel
   re-pinned to normative SHORT/1, InvalidDng/UnsupportedVersion error-model fixes,
   exiftool -n DNGVersion/ImageHeight/SubIFD tag reporting).
   Next unit (developer pre-authorized 2026-11-08): **E01-CEN starts immediately**,
   then documentation consolidation of completed stages. See PROGRESS.md
   "D1 narrow read-only DNG inventory profile".
No other new public contracts, general format support, exclusion, Framework or
publication authorization is implied by the 2026-11-08 directive or this
checkpoint. See design/EXIV2_PARITY_MASTER_PLAN.md for the full gated subtask split.

## Reproduction / local constraints

```sh
git status --short
git branch --show-current
python -m unittest discover -s tests/reference -q
python tools/reference/dng_fixtures.py generate /tmp/dng-check
python -m unittest discover -s tests/discovery -q
python tools/reference/build_capability_seeds.py --check
git diff --check
gh pr view --json state,isDraft,headRefOid,statusCheckRollup,mergeCommit
gh run list --branch master --limit 4
python3 tools/reference/b8_census_fixtures.py verify
python tools/reference/build_census.py --check
# With both SDKs/runtimes installed:
dotnet restore MetadataSharp.sln
dotnet build MetadataSharp.sln -c Release --no-restore
dotnet test tests/MetadataSharp.Tests/MetadataSharp.Tests.csproj -c Release -f net8.0 --no-build --no-restore
dotnet test tests/MetadataSharp.Tests/MetadataSharp.Tests.csproj -c Release -f net10.0 --no-build --no-restore
```

Local dotnet absent; actual .NET evidence is Actions only. Do not retry unavailable
SDK/blob downloads. Bounded failed-TRX/command diagnostics and compressed complete
JSON annotations work; tools/audit/collect.py validates run/source/chunk receipts.
Audit AUDIT_TFM and reference METADATASHARP_REFERENCE_TFM default net8.0, accept
net10.0. Tools now need explicit -f for dotnet run; no target may silently fall back.
Python discovery suite is 103/103 locally at the B5 checkpoint (incl. census-policy and
XMP fixture tests; the closed layout enumerations gained 'xmp-packet' for the
comments/exif/icc/iptc/thumbnail families as a B5-additive extension discovered by CI
in PR #37); B5 observations rehearsed via the pinned CLI, status passed; reference
suite 17/17.
Merged final-head CI for this checkpoint is PR42449478931 / push42449474066 (15/15).

## B6 working checkpoint

B6 merged via PR #39; its receipt is in `docs/research/evidence/e01-b6-census/`
(with a `ci-` twin), recorded on a branch since pruned. The user authorized
continuation to subsequent stages without another confirmation; existing separate
publication, D2 write and denominator-freeze restrictions remain in force.
`tools/reference/dng_raw_oracle.py` adds a bounded classic-TIFF CFA strip-byte
oracle for the owned D1 raw and SubIFD fixtures, with both byte orders and
fail-closed malformed-input tests in `tests/reference/test_dng_raw_oracle.py`.
It is not a raw-image decoder, and does not establish rendered pixel equivalence.
Local reference tests: 34/34; discovery: 104/104; census `--check` passed.
B6 also has 12 owned D1-derived fixture variants with separately pinned manifest
and provenance: both endian orders, raw/preview root, empty/present IFD0 carriers,
and present variants with a chained JPEG IFD1. The preview root is not the IFD1;
the IFD1 variant's full DNG conformance is not independently established. A
bounded IFD0 byte oracle extracts selected metadata carriers without decoding.
A pinned-source local Exiv2 preflight exercised 28 exploratory CLI probes across
four owned D1 fixtures: all commands/readbacks returned zero and CFA strip bytes
were unchanged. The raw facts and build/linkage limitations are recorded in
`docs/research/evidence/e01-b6-preflight/`; these are not census observations
or MetadataSharp verification. B6's implemented observer now records 88 local
rows, integrated into the census and CI workflow; local evidence and limitations
are in `docs/research/evidence/e01-b6-census/`. CI and PR merge completed. CI annotation-recovered observations match all 88
local observation objects and the fixture manifest; see the receipt.
After the receipt PR, continue to B7 without a new user confirmation,
while maintaining format rights/oracle reviews and the separate release limits.

## B7 working checkpoint

B7 groundwork PR #41/#42 is merged. On the B7 working branch (since pruned),
separate format rights reviews now allow narrowly scoped owned-synthetic probes
for AVIF, HEIF, HEIC and JXL; CR3 remains blocked. Five 8×8 files and pinned
encoder recipes are in `tests/fixtures/b7/`; HEIF and HEIC both use HEVC/HEIC
brand; JXL has distinct container and naked codestream. Their complete normative
conformance, codec patent clearance and independent JXL decode are not established.
The 48-row read-only B7 local rehearsal recorded 32 accepted record-only,
8 refused on bare JXL, 8 CR3 blocked. EXIF/XMP are present on four fixtures;
IPTC/ICC/comments/thumbnail are absent, so those probes do not prove support.
ExifTool 12.76 is recorded as the secondary reference. HEIC/HEIF coded ispe
64×64 and clean-aperture display 8×8 must remain distinct. Raw evidence and
limitations: `docs/research/evidence/e01-b7-census/`. PR #43 run 42707052713
was 15/15 green; recovered CI report matches 40 observations after normalizing
target paths in stderr, and five secondary reports after excluding system data.
Local checks: reference 54/54, discovery 105/105, census check and exact
five-fixture regeneration passed. No denominator frozen, verification remains
not-assessed, and no parity percentage or publication is authorized. Even if
this bounded batch is merged, unattempted CR3 no-claim rows cannot be silently
closed before freeze; remaining metadata-present and decoder-oracle gaps persist.

## B8 preliminary checkpoint

B8's separate per-format rights and oracle ledger is
`docs/research/E01_B8_RIGHTS_AND_ORACLE_REVIEW.md`, committed before fixture
authoring. Only a narrow owned PSD probe is cleared so far; JP2, PGF, EPS, EXV
and DCP remain pending review, not refused or excluded. DCP is a camera profile,
not an ordinary TIFF image. The owned 52-byte 2×2 RGB PSD v1 has no image
resources, layers or metadata. `tools/reference/b8_psd_fixture.py` regenerates
the bytes using only the standard library; tests independently check header,
section lengths and planar pixel bytes. Local ImageMagick 6.9.11-60 `identify`
recognized PSD at 2×2 and `convert -depth 8 rgb:-` returned the authored pixels.
That external decoder is a local sanity check, not a pinned-CLI census
observation or proof of full PSD conformance. No B8 rows were observed at that point;
metadata-present cases and other formats still require research and evidence
(superseded for the cleared formats by "B8 census closed" below).

### B8 PSD metadata preflight (current working branch)

After an additional PSD resource review committed before authoring, a second
owned 2×2 PSD adds EXIF Artist B8Artist, XMP CreatorTool B8Tool and IPTC keyword
B8Keyword. Independent tests inspect section sizes, padding, payload contents and
planar pixels. Locally ImageMagick 6.9.11-60 decoded both fixture pixel arrays;
pinned ExifTool 12.76 read all three authored rich metadata values. ExifTool
reports Compression Unknown (12183) on both although the authored compression
field is zero; this is an unresolved discrepancy, not a diagnosed error. See
`docs/research/evidence/e01-b8-psd-preflight/`. New CI preflight runs pinned
Exiv2 and ExifTool and records outcomes. CI run 42710532014 passed 15/15;
its recovered report records flat file five unknown-type refusals and rich
file three printed metadata values, empty/no-thumbnail outcomes and -pS/-pC
not-supported. All ten inputs unchanged. No B8 census rows had been observed at that
point. ICC and thumbnail are absent. JP2, PGF,
EPS, EXV, DCP and B7 CR3 remain unresolved; no denominator freeze.

## B8 census closed — observed in the pinned CI job (PR #48)

B8 is enumerated and recorded: 53 rows over seven layouts, 31 `observed-cli` and 22
`blocked` (PGF/EXV/DCP). `tools/reference/observe_b8.py` runs exactly one read-only
pinned-CLI flag per row and refuses to emit a report that would not fit the
ten-annotation recovery budget; `tools/reference/b8_census_fixtures.py` re-derives the
container framing (PSD sections and `8BIM` accounting, JP2 box walk, bounded EPS DSC
scan that never executes PostScript) with the standard library only, so the catalog
verifies offline. Evidence: `docs/research/evidence/e01-b8-census/`.

Recorded facts worth keeping, because they are what the honesty constraints rest on: the
resource-bearing PSD answers `-pa` with one 206-byte print containing all three authored
tokens, so its four metadata-family read rows share identical bytes and **no per-family
attribution is claimed** (the discovery suite asserts that equality instead of trusting
prose); `-pC` exits 1 and `-et` exits 0 with nothing produced; `-pS` exits 1 on **both**
PSD layouts, which is why the flat file records eight refusals rather than eight
"metadata absent" results. JP2 is recognized (`-pS` exit 0, 379-byte print) but prints
nothing for every family and `-et` exits 253; EPS refuses `-pS`/`-pC` and prints nothing
for `-pa`/`-pc`. Zero `scan-changed`, every input byte-identical before and after, no
artifact produced.

Reproduction: B8 has no local run (no cmake/ninja or expat/zlib headers in this sandbox,
and the Actions log host is unreachable), so the second sample is another CI run —
`observations`, `blocked`, `fixtureManifest` and `outcomeCounts` matched byte-for-byte
between runs, while `sourceCommit`, the ExifTool `System:File*Date` fields,
`baseline.executableSha256` and the `time=` banner line differ (the reference build
embeds its own timestamp). So the conclusion is deliberately narrow: **observations are
reproducible, the compiled binary is not**, and `receipt.json` pins the digest of the
exact build that produced the primary evidence.

Still open, not closed by this batch: every B8 write suboperation (no owned writable
fixture and no owned ICC/thumbnail payload is cleared), PSD ICC (1039) and thumbnail
(1036) resources, PSB/layers/colour management, JP2 and EPS metadata-present fixtures,
PGF/EXV/DCP rights and oracles, and the B7 CR3 block. Verification stays `not-assessed`
on all 568 rows, no denominator is frozen, and publication and DNG D2 write stay
unauthorized. Next unit: **B9** (TIFF-derived RAW wave 1), which needs vendor layout
specs and per-format rights review before any fixture is authored.

## E08 bounded PSD, JP2 and EPS implementation — merged via PR #47

User requested implementation of the remaining formats and maximal work in one
PR. New `PsdMetadata` and `Jp2Metadata` implement bounded synchronous buffer
inspection only: PSD v1 uncompressed flat RGB image resources as opaque copies;
JP2 32-bit boxes, dimensions and opaque top-level XML boxes. No pixel decode,
metadata interpretation, full validation, editing or preservation is claimed.
Unsupported structures fail closed; capabilities for parsed metadata remain
NotSupported. Independent rights review precedes authored JPEG 2000 fixture;
PSD has existing authored fixture and pinned preflight, JP2 pinned preflight is recorded at
`docs/research/evidence/e01-b8-jp2-preflight/`. E01 census is not modified or verified by these
new product tests; no percentages or denominator freeze. Remaining B8 formats
PGF, EPS, EXV and DCP and broad PSD/JP2 variants still require implementation
and rights/oracle work. B7 CR3 stays blocked. Full contract:
`docs/design/E08_PSD_JP2_BOUNDED_READ_CONTRACT.md`.

### EPS header inspection added to the same E08 PR

After a separate EPS rights review, added a small independently authored EPSF
3.0 fixture and `EpsHeader.Read`, a bounded DSC bounding-box scanner. It never
executes, interprets or validates PostScript, nor verifies rendering. It only
accepts immediate four integer coordinates and explicit EndComments. Pinned
reference preflight run 42713667205 passed and is committed in
`docs/research/evidence/e01-b8-eps-preflight/`; six OS/TFM jobs each passed 562
tests. CI after the receipt and additional budget tests is pending. PGF/EXV/DCP
remain unresolved.
