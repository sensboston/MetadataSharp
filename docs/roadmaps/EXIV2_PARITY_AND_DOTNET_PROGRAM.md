# Programme 2: independent Exiv2 capability coverage and .NET evolution

Status: direction APPROVED by developer on 2026-11-07; new public contracts still
need explicit approval before implementation. This is a new programme, not a
renumbering or reopening of completed M00–M18. Active work starts with E00/E01;
DNG D0 is approved contract-only E04 (2026-11-07), not implementation. No package publication is authorized.

## Goal, meaning of parity, and non-negotiable boundaries

Aim for practically complete **user-visible functionality** against a fixed Exiv2
baseline, with justified improvements and idiomatic managed .NET APIs. Not C++ ABI,
class hierarchy, CLI syntax, binary/package or undocumented bug compatibility.
Baseline: Exiv2 **v0.28.9**, released 2026-08-30; annotated tag
110cbaef8e75f0e064e5c522846a18982bc4db70 resolves to commit
b0859fc61fea5dedfc898aece6b15893ddf4314e. Build options/version/CLI provisioning
must also be pinned before differential coverage can be asserted. Existing required
0.27.6 reference tests remain historical regressions, NOT proof of 0.28.9 parity.
Future baseline upgrades are reviewed deltas, not silent denominator changes.

"Practically 100%" is an objective, not a current percentage or unconditional
promise of safe vendor-file writing. E01 freezes a versioned inventory of atomic
behaviors: format × metadata family × operation × feature/encoding/layout variant.
Each row has an ID, normative source, baseline observation/build, owned fixture,
MetadataSharp status, independent expected result, limits and test evidence.
Track read/modify/insert/delete/extract and preservation separately; recognizing a
file or copying a no-op is not metadata support. Read-only baseline handlers do not
create implied write requirements. A refusal is not a successful parity test.

Report implemented-and-verified / all baseline-applicable behaviors and a separate
accepted-target coverage metric if developer-approved exclusions exist. Unknown,
blocked and explicitly excluded rows stay visible; do not inflate percentages by
removing difficult rows, grouping an entire vendor as one item or counting fixture
multiplicity. No percentage is valid until the denominator and acceptance corpus
are reviewed. CLI convenience behavior needs a .NET service equivalent or explicit
waiver, not a silent exclusion. Security fixes may intentionally diverge, with a
reviewed discrepancy and an independent test, never blindly reproduce corruption.

Independent implementation: derive algorithms from lawful specifications, publish
our own contracts/tests and use Exiv2 only as a legally reviewed external black box.
Do not translate/copy GPL implementation, tests, tag dictionaries or issue patches.
README/manual/license review and issue titles identify requirements; user attachments
need item-level rights. Do not claim a formally isolated legal clean-room process
or immunity from copyright/patent obligations. BMFF and vendor formats need separate
rights/specification review. No reference CLI/native dependency in production DLLs.
MakerNote safety, hostile-input limits and independent EXIF/IPTC/XMP remain binding.

## Targets and architecture

Developer explicitly corrected the target to **.NET 10**, alongside .NET 8.
Official dotnet/core lifecycle metadata blob 66392f8bd6d254f73cf7a2f42113bc4034a20747:
.NET 8 LTS maintenance ends **2026-11-10**; .NET 10 LTS support ends
**2028-11-14** (observation 2026-11-07). Retaining net8.0 compatibility does NOT
extend Microsoft's support or promise security fixes for an EOL runtime. Recommend
.NET 10 to new consumers; publish a reviewed compatibility policy before release.
Sources: https://github.com/dotnet/core/blob/main/release-notes/releases-index.json
and pinned blob above. E02 must actually build/test/package/consume both TFMs,
not merely run a net8.0 binary on an installed newer SDK. Pin SDK selection, compiler
language policy and six OS/TFM test combinations; benchmark baselines stay comparable.

.NET Framework 4.8/4.8.1 is conditional, Windows-only feasibility, NOT promised.
Current source uses spans, modern guard helpers, IAsyncDisposable/ValueTask, modern
stream/crypto/compression APIs; C# syntax support is not runtime API availability.
Do not add netstandard2.0/net48 by editing a TFM string or install compatibility
packages without review. E02 produces an API/dependency/cost map and isolated build
spike only if cheap. Acceptance for inclusion: no public safety/async degradation,
no duplicated parser/writer engine, no native bridge, no production conditional
sprawl, and small documented shim/dependency set validated on Windows. Otherwise
record "not cheap; deferred" and keep modern .NET as primary target.

Idiomatic .NET: owned immutable snapshots, explicit capabilities, typed values,
streams/paths, cancellation, stable errors, no global mutable registries, no forced
Exiv2 key syntax/class mirroring. E03 proposes exact changes and migration policy
before refactoring; prefer internal shared mechanisms before new public bases.
No rewrite-for-style or simultaneous parser+API migration without independent gates.

## Bounded milestones and gates

The sequence below is planning authorization, not approval of unseen APIs/formats.
Every implementation unit has proposal → explicit contract approval → implementation
→ platform/reference/rights/preservation gate. E06 onward are umbrella ordering slots:
E01 must split them into finite per-format/per-vendor prompts before any code.
Do not claim an umbrella complete because its first format passes.

| ID | Deliverable | Gate / dependencies |
| --- | --- | --- |
| E00 | Programme, independent-implementation policy, document index | Direction already approved; plan and provenance recorded, no parity claim |
| E01 | Frozen capability inventory and issues/discussions requirements backlog | 31 documented image-type rows reconciled against pinned CLI build; atomic operations plus video/CLI/tag/encoding breadth, rights/corpus gaps and reviewed denominator; initial discovery is not this full gate |
| E02 | .NET 8 + 10 multi-target and Framework feasibility | SDK/TFM/package/dependency policy, actual OS/TFM matrix and isolated consumers; Framework include/defer cost decision; CI must be available |
| E03 | .NET architecture and compatibility contracts | Exact capability discovery/value/metadata/addressing/error APIs, independent snapshots and migration tests; approval BEFORE refactor |
| E04 (D0) | Exact read-only DNG proposal | DNG_D0_READ_ONLY_CONTRACT_PROPOSAL; approval plus closed normative/oracle prerequisites before D1; not write support |
| E05 (D1) | Approved narrow read-only DNG | Own valid II/MM fixtures, role/range/schema refusals, raw-sample oracle and external validators; existing TIFF DNG refusal unchanged |
| E06 | Typed metadata/encoding/tag vocabulary breadth | Independently sourced EXIF/GPS/interop, IPTC encodings/repetitions, XMP structures/qualifiers; provenance for every vocabulary; split by family |
| E07 | Existing-container coverage gaps | JPEG/PNG/WebP/TIFF ICC/comments/thumbs/IPTC/XMP/extended/variant coverage and independent edit policies; split per container+operation |
| E08 | Extra writable non-RAW containers | JP2/PSD/PGF/EPS/EXV/DCP only as documented baseline operations justify; per-format specs, owned fixtures and safety proof |
| E09 | Modern read-only containers | BMFF/HEIF/HEIC/AVIF/CR3 and JXL, including container/codestream distinctions, optional compression/dependencies/patents; separate gated formats |
| E10 | TIFF-derived RAW readers | ARW/CR2/NEF/ORF/PEF/SRW and documented read-only MRW/RAF/RW2/SR2 breadth; per-family format/offset/preview accounting, no inherited write authorization |
| E11 | Legacy RAW/recognition/video inventory | CRW/CIFF; BMP/GIF/TGA recognition/dimensions vs metadata; bounded MOV/MP4/MKV/RIFF/ASF baseline reconciliation, then separate read-only contracts |
| E12 | MakerNote and vendor semantic coverage | Per-vendor/block/version source-rights and offset-base proof; lens/value rendering without imported databases; unknown schemes remain refused |
| E13 | Extended safe editing, including DNG D2 proposal first | Read gates precede writes; per-format graph/integrity/private dependencies, no silent destructive fallback; retain visible blocked parity rows |
| E14 | Preview/ICC/thumbnail extraction and insertion breadth | Size/encoding/ownership/offset policies across applicable containers; extraction/replace/delete separate; no unintended decode/recompression |
| E15 | Workflow/service equivalents | Timestamp adjust, metadata copy, transfer mappings, fix operations, Unicode paths and optional machine-readable CLI; explicit loss/sync/overwrite policies |
| E16 | Selected improvements from evidence backlog | Separate proposals with independent tests; threading/capabilities/transaction failure diagnostics etc., no speculative "better than Exiv2" claim |
| E17 | Full parity reconciliation and adversarial validation | Frozen denominator ledger, two-way reference facts, independently justified differences, fuzz/limits, approved residual gaps and no inflated coverage |
| E18 | Release readiness and measured audit | API docs/examples/versioning/notices/advisories/packages/symbols/reproducibility, six target checks/performance deltas; publication requires separate approval |

E01 precedes wide feature implementation. E04 is produced now to honor the earlier
D0 request, but D1 waits for E02/E03 compatibility decisions and exact-contract
approval. E02 can proceed independently of D0 review once CI/tooling is available.
Grouping related milestones into PRs is allowed only with separate per-unit records
and gates; it never bypasses predecessors or new contract approval.

## Completion, blockers and next actions

This submission completes E00 documentation and initial E01 discovery only. Full
E01 atomic inventory/CLI verification remains open. Read EXIV2_CAPABILITY_BASELINE
and EXIV2_REQUIREMENTS_TRIAGE for source-limited findings. Current CI billing issue
must be rechecked; PR #13's one-off docs exception does not cover this programme.
No DNG parser, runtime retarget, public API refactor, or new format is implemented
by these documents. The developer subsequently explicitly approved the exact D0 contract on
2026-11-07. E04 contract approval is complete; D1 still waits for its normative,
fixture/oracle and E02/E03 prerequisites. No approval of broader APIs is implied.

## Superseding E02 implementation checkpoint

E02 modern target implementation at 2949589 passed 42446034373: six OS/TFM
correctness jobs (450 each), six strict package/matching-SDK consumer/signature
jobs, and two required reference jobs. See DOTNET_8_10_SUPPORT_AND_E02_AUDIT and
small evidence/e02 receipts. Framework 4.8.x is deferred because cheap inclusion
was not established; no legacy spike or compatibility promise. E03 is not approved
by successful cross-target signatures. Final PR #15 head/merge needs its own gate.
E01 operation seeds are now reproducible (186 cells / 348 candidate operations),
not an approved atomic denominator or observed current-Exiv2 coverage. E01/E03 and
D1 normative/oracle prerequisites remain next; no DNG implementation yet.
