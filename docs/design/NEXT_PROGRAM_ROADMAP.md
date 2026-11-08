# P-series: independent metadata capability expansion

Status: program direction explicitly approved by developer; implementation
milestones below require their own predecessor gates and new public contracts
require approval before code. Supersedes the old roadmap as the active program;
M00–M18 remain completed historical records, not deleted or renumbered.

## Approved goals and non-goals

Target near-complete **functional capability**, not source/API/CLI/class-hierarchy
compatibility, relative to a pinned Exiv2 release. Improve identified user workflows
where independently justified. Idiomatic .NET design, explicit ownership, immutable
results, typed values, real async/cancellation and structured failures take priority
over imitating C++. Maintain finite hostile-input limits and conservative write
refusals. New formats/opaque relocation/privacy promises are never implied by parity.

Target .NET 8 **and .NET 10 LTS**. Framework 4.8/4.8.1 is conditional on a cheap
feasibility result, not a commitment. No release/publication authorization. No
wholesale rewrite or production refactor before contract and compatibility review.

"Clean sheet" here means independently implemented from specifications and original
requirements/tests. It is NOT a claim of a formal legally certified clean-room
process. Exiv2 documentation and issue reports are research, its CLI a black box;
never translate GPL implementations/tests/tag tables into MIT. Do not copy issue
attachments, snippets, SDK algorithms or fixtures without separate rights review.
Keep provenance records; legal uncertainty blocks affected work, not all research.

## Definition of near-complete

P00 freezes the feature universe into versioned requirement rows: format × metadata
family × operation × supported layout/variant. Each gets a stable ID, source, status,
acceptance oracle, fixture rights, implementation milestone and disposition. Split
read/write/remove/extract, raw vs interpreted values, previews and unsupported
variants; one supported happy path does not mark a format complete. CLI grammar,
C++ ABI/build tooling are excluded from library parity, but underlying user workflows
must be assigned a managed equivalent or an explicitly approved exclusion.

No percentage until the denominator and exclusions are approved. Publish both raw
coverage (verified requirements / all baseline requirements) and scoped coverage
(verified / approved in-scope requirements), plus exclusions/unknowns. Never inflate
coverage by counting refusals or dropping difficult MakerNotes/raw variants. Exiv2
behavior is evidence, not absolute truth; normative discrepancies are reviewed.

## Milestones and sequencing

| Unit | Bounded deliverable | Gate / dependencies |
| --- | --- | --- |
| P00 | Pin Exiv2 baseline/build options; complete capability ledger and sampled issues/discussions requirements | Approve denominator, exclusions, priority order; no implementation implied |
| P01 | .NET 8/10 build/package/consumer plan and Framework 4.8.x feasibility | Exact TFM/OS/API compatibility decision; test actual runtimes, not just compilation |
| P02 | Idiomatic .NET API/architecture proposal, migration and compatibility plan | Approve exact changed signatures/ownership/errors/options; no broad rewrite |
| P03 | Typed values, tag/schema provenance, interpreted/raw separation and capability discovery | P00/P02; independent tests, no imported dictionaries |
| P04 | EXIF/TIFF metadata breadth, thumbnails and duplicate/unknown handling | P03; address-key/graph/preservation contracts and reference gates |
| P05 | XMP/RDF and namespace breadth | P03; explicit richer model, isolation/concurrency/security and exact-preservation policy |
| P06 | IPTC/IRB, ICC opaque profiles, comments and ancillary metadata | P03; separate family semantics, container size/refusal tests |
| P07 | Existing JPEG/PNG/WebP/TIFF container breadth | Relevant P04–P06 contracts; no decoder/recompression; layout matrix and coded-data identity |
| P08-D0 | Exact bounded read-only DNG proposal | Feasibility M17 exists; proposal may proceed in parallel with P00/P01, STOP before code |
| P08-D1 | Approved DNG read-only candidate | Only after D0 exact contract, spec/fixture/oracle prerequisites; not full raw parity |
| P08-D2 | DNG descriptive-write proposal, then approved implementation in a separate unit | D1 gate, explicit pointer/integrity/retained-byte contract; proposal STOP before code |
| P09 | Other raw families, partitioned per format/vendor | P00 selects actual families; each receives separate contract, rights/spec/offset proof and CI gate |
| P10 | Additional still/container families incl BMFF/JXL candidates | P00 confirms baseline operation support/build options; per-family specs/licensing/compression budgets and gates |
| P11 | MakerNote/vendor interpretation and individually proven write schemes | Independent per-vendor evidence/rights; safe read ≠ relocation proof; no arbitrary relocation |
| P12 | Cross-format workflows, extraction/export/batch/capability diagnostics | P02 and underlying family gates; streaming/concurrency/portable machine-readable contracts |
| P13 | Selected improvements from research backlog | Separate approved requirements, not unlimited scope or silent repair |
| P14 | Final parity reconciliation, performance/security/package/API-doc audit and release proposal | All approved row gates, independent corpus, three OS/TFM consumers; publication needs separate approval |

P09/P10/P11 are **planning envelopes**, not single huge implementation PRs. P00 must
expand them into format/vendor-specific work units before coding and re-estimate.
P00–P02 may share a documentation PR; implementation is sequential in the dependency
graph with a green per-unit checkpoint and final PR gate. No schedule/completeness
promise is made before the feature census. DNG implementation does not jump ahead
of missing public contracts just because its proposal is being written now.

## Current authorized execution

Start P00 research, P01 feasibility and P08-D0 proposal documents. Preserve existing
448-test/reference gates; no code/TFM/API changes in this planning checkpoint.
Account Actions billing previously prevented jobs; PR #13's explicit docs-only
exception applied ONLY to PR #13, not this program. Required checks remain pending
if infrastructure prevents execution. New contract proposals must be explicitly
approved before implementation; ordinary within-contract work can follow standing
sequential authorization after gates. See CURRENT_STATE for live recovery state.
