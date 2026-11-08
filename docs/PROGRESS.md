# Progress and Approval Record

## Documentation setup

- Established the project policy, replacement roadmap, and execution prompts.
- No library implementation or public API exists yet.
- At documentation setup, no stage contracts had been approved. This historical
  state is superseded by the M00 approval record below.
- The developer agreed to the high-level safety/licensing direction and requested
  bounded milestones; this is not approval of concrete API signatures.
- Validation: local Markdown link targets and roadmap/prompt ID consistency checked
  during documentation setup; no .NET build or tests apply to this docs-only change.

## Stage records

### M00 — 2026-11-07 — Proposal submitted; approval pending

- Authorization: the developer explicitly requested starting M00. This authorizes
  the proposal only, not implementation or approval of its contents.
- Artifact: `docs/design/INITIAL_CONTRACTS.md` proposes concrete core/JPEG signatures,
  the JPEG subset, finite limits, data ownership, edit/preservation rules, exception
  codes, cancellation and stream/path failure semantics, and acceptance examples.
- Proposed conservative tradeoffs: single-scan baseline JPEG; no ordinary rebuild
  with unknown/unclassified tags or MakerNotes; full source digest verification;
  seekable streams; SaveAs only to a new destination; independent raw tag edits.
- Repository review: `git status --short`, `git diff -- METADATASHARP_PROMPT.md`,
  `find docs -type f | sort`, and `git branch --show-current` confirmed existing
  documentation changes and the expected branch. Existing files were preserved.
- Validation: ran an inline `python - <<'PY'` documentation check using pathlib/re:
  local Markdown links and balanced fences in all 25 Markdown files, all eight
  proposal sections, `65535 - 2 - 6 == 65527`, and absence of `.cs`/`.csproj` files.
  Result: PASS. Ran `git diff --check`; exit code 0, no whitespace errors.
- No .NET build, unit tests, external tools or benchmark was run: no implementation
  exists and this unit is documentation only. C# signatures are not compiled.
- Normative sources were not fetched or verified. Candidate editions/topics and
  their required verification gates are explicitly recorded in the proposal.
- Pending gate: developer approval/amendment of the seven decisions in section 8.
  M01 cannot start until this gate is closed and further work is requested.


### M00 approval — 2026-11-07 — Gate closed

- The developer explicitly approved all seven initial-contract decisions and
  requested durable Markdown documentation with descriptive names.
- Recorded the accepted scope, rationale, costs, guarantees and reopening criteria
  in `docs/decisions/INITIAL_RELEASE_ACCEPTED_TRADEOFFS.md`.
- Marked `docs/design/INITIAL_CONTRACTS.md` approved and updated the roadmap and
  project entry point to distinguish approved design from unimplemented behavior.
- Added root `README.md` navigation. Existing proposal-validation history above is
  retained as a historical record, not a current approval blocker.
- Developer authorized committing/pushing the current branch and merging into
  master. This does not authorize implementing later work units automatically.
- Documentation checks: local link targets and balanced fenced blocks verified by
  an inline Python check; `git diff --check` passed. No C# build/tests apply.
- M00 is complete as an approved design stage. M01 is eligible but not started.
- Normative verification, compilation and implementation tests remain future gates;
  approval does not certify the unverified standards assumptions.

### M01 — 2026-11-07 — Skeleton prepared; validation blocked

- Authorization: the developer requested implementation after M00 approval and
  explicitly resumed work in the new active session. Work remains on
  `arena/e42dbe63-metadatasharp`; no switch to master was made.
- Added `MetadataSharp.sln`, Core/Formats library projects and one xUnit test
  project. Formats references Core; tests reference both. No binary primitives,
  metadata API implementations, parsers or serializers were added.
- Added shared net8.0/C# 12.0/nullable settings, unsafe disabled, deterministic
  output, warnings as errors and packaging disabled. `global.json` allows serviced
  stable .NET 8 feature bands without roll-forward to a different major version.
- Test dependencies: Microsoft.NET.Test.Sdk 17.11.1, xunit 2.9.2,
  xunit.runner.visualstudio 2.8.2 (private assets). No library NuGet dependencies.
  Versions are explicit but restore/license/transitive resolution is not claimed
  verified; no generated lock file was fabricated without restore.
- Added a two-case infrastructure theory checking both referenced library assemblies
  can load and declare net8.0. No placeholder public classes were introduced.
- Added `.gitignore` and `docs/BUILD_AND_TEST.md`; updated navigation/current status
  in README, roadmap and project instructions.
- Environment check: `dotnet --info` failed with exit 127 (`dotnet: command not
  found`). `command -v dotnet` found no executable; common SDK directories were
  absent. No SDK was installed or alternate target silently selected.
- Attempted acceptance commands individually; each failed with exit 127:
  - `dotnet restore MetadataSharp.sln`
  - `dotnet build MetadataSharp.sln --configuration Release --no-restore`
  - `dotnet test MetadataSharp.sln --configuration Release --no-build --no-restore`
- Static validation: inline Python parsed SDK JSON and all three project XML files,
  checked shared settings, project/solution reference paths, explicit package
  version syntax, local links/fences in 28 Markdown files, and absence of production
  C# source. PASS. This is not an MSBuild evaluation or C# compilation.
- `git diff --check` passed. `git check-ignore` confirmed representative bin/obj/
  TestResults paths are ignored. No CI or external-tool run was performed.
- Pending gate: successful SDK-enabled restore, Release build and test execution
  with two discovered theory cases. Package availability and runtime loading are
  unverified until then. M02 is blocked; resume M01 validation next.

### M01 SDK bootstrap attempt — 2026-11-07 — Still blocked

- The developer uploaded `dotnet-sdk-8.0.400-linux-x64.tar.gz` (211,858,390 bytes)
  to the repository release `sdk_bootstrap` after direct SDK download hosts failed.
- `gh release view sdk_bootstrap --json tagName,assets` succeeded and confirmed the
  asset. `gh release download sdk_bootstrap --pattern
  dotnet-sdk-8.0.400-linux-x64.tar.gz --dir /home/user/.cache/metadatasharp-sdk`
  failed with exit 1: GitHub redirected the download to
  `release-assets.githubusercontent.com`, and the connection ended with EOF.
- The asset host is outside the environment's stated outbound host allowlist.
  GitHub API access does not establish access to release binary downloads.
- Extraction, SHA-512 calculation and `dotnet --info` did not execute because the
  chained download command failed. No SDK is installed; no new build/test result
  is claimed. No signed download URL or credentials were stored in the repository.
- M01 remains pending. Next options: platform-authorized access to the asset host,
  a supported direct file attachment, or running the documented acceptance commands
  in an SDK-enabled environment. No network-policy bypass is attempted.

### M01 CI validation — 2026-11-07 — Run requested

- The developer proposed GitHub Actions to validate M01 despite the missing local
  SDK. Added a read-only workflow for Linux, Windows and macOS using .NET 8.
- Workflow restores, builds Release, tests, checks TRX counters for at least two
  passing cases, and uploads test results. No SDK archive is added to Git.
- Publishing the current M01 files to the active Arena branch triggers validation;
  no merge or later-stage implementation is part of this execution.
- This entry records setup only. The completion gate remains pending until the
  actual run result is inspected and recorded below.

### M01 CI result — 2026-11-07 — Gate closed

- Commit validated: `e72ce881dd5579b34d1dd36f335df58be8e3f299`.
- Evidence: https://github.com/sensboston/MetadataSharp/actions/runs/42416545213
- `gh run watch 42416545213 --exit-status --interval 15` exited 0.
- Linux, Windows and macOS jobs all succeeded, including SDK setup, environment
  reporting, restore, Release build, Release tests, TRX counter verification and
  artifact upload. The counter step requires at least two passing cases and no
  nonpassing cases; the current suite defines two cases.
- A manual API dispatch attempt returned HTTP 403, but the push-triggered run did
  start successfully. Opened PR #2 for review; it has not been merged.
- Bulk log download via `gh run view 42416545213 --log` was unavailable because
  its redirected log-storage host returned EOF. Job/step status was accessible and
  observed through the watch command. Exact installed SDK patch is not claimed.
- Nonblocking annotations: v4 actions use a deprecated Node runtime that GitHub
  currently upgrades automatically; hosted runner image/capacity notices also
  appeared. Future maintenance should review action versions and runner drift.
- Updated current-status documents to supersede the historical local-SDK blocker.
  Local SDK installation is still unavailable; use CI for actual .NET execution.
- M01 acceptance gate is satisfied. M02 has not started and requires a request.

### M02 — 2026-11-07 — Implementation submitted to CI

- Developer explicitly requested M02, then M03 only after successful CI, and merge
  of completed work. This is a bounded two-stage exception to the default stop rule.
- Added approved Endianness and internal span cursors using BinaryPrimitives;
  Try failures do not advance or modify output. Added checked nonnegative range,
  product and addition helpers, with friend access for the test assembly only.
- Added known-byte tests for both orders/all integer widths, signed extrema, every
  truncated width, exact end, failure recovery, invalid order and overflow bounds.
- Design rationale: `docs/design/BINARY_PRIMITIVES_AND_FAILURE_ATOMICITY.md`.
- No TIFF parsing, allocation abstraction or unapproved public API was added.
- Local `git diff --check` passed; runtime checks require GitHub Actions. M03 has
  not started; the gate remains pending until M02 CI succeeds.

### M02 CI result — 2026-11-07 — Gate closed

- Validated commit: `97f87df97a55caf528c77dcbddeebcc5c447c587`.
- Evidence: https://github.com/sensboston/MetadataSharp/actions/runs/42417202024
- `gh run watch 42417202024 --exit-status --interval 15` exited 0. Linux,
  Windows and macOS all passed restore, Release build, tests and TRX verification.
- Existing nonblocking action-runtime/hosted-runner notices remain. No local SDK
  or downloaded bulk log is claimed. M03 started only after this successful run.

### M03 — 2026-11-07 — Implementation submitted to CI

- Implemented the approved raw type/entry/snapshot model and signed/unsigned
  rational types. Internal constructors do not broaden the public API.
- Added ownership, both-order raw fidelity, twelve width cases, invalid enums,
  count overflow, short/excess buffers, zero counts, opaque IDs, snapshot immutability,
  duplicate keys, NaN-bit preservation, rational extrema/zero denominator/equality tests.
- Rationale and exclusions: `docs/design/RAW_EXIF_VALUE_OWNERSHIP_AND_EQUALITY.md`.
- No tag schema or relocation safety is inferred from raw values. Normative/tag
  constraint verification remains an explicit M04 prerequisite; no format parser,
  editing API, endian transcoding or source document is introduced by M03.
- Local `git diff --check` passed. Runtime gate pending GitHub Actions.

### M03 CI result — 2026-11-07 — Gate closed

- Validated commit: `ca0f991066c0e7322decbe9ee88dd6f743aaa296`.
- Evidence: https://github.com/sensboston/MetadataSharp/actions/runs/42417333330
- `gh run watch 42417333330 --exit-status --interval 15` exited 0. All three
  platforms passed restore, Release build, tests and TRX verification.
- Raised the workflow test floor from two infrastructure cases to all 71 authored
  M01–M03 cases (2 infrastructure + 36 binary + 33 raw/rational). This strengthened
  counter check is to be validated on the final head before merge.
- Updated current status/navigation. No parser, ExifEdit or serializer was added.
  M04 requires a separate request and normative/tag schema verification first.
- Developer requested merge of ready work. PR #2 now includes M01–M03; merge only
  after the final head's CI checks pass. Merge outcome is recorded in GitHub PR
  history rather than predeclared here.

### Sequential authorization and M04 — 2026-11-07 — Submitted to CI

- Developer authorized sequential milestones without per-stage permission, with
  successful CI and merge of completed work. Safety/public-API changes still need
  approval. Updated development policy to record the standing authorization.
- Reviewed TIFF 6.0 Section 2 and final Exif 3.0 classic structure/link/tag-shape
  sections through GitHub-hosted PDF copies. No third-party implementation/tests
  were used. The supposed 2.32 copy proved to be a draft and was explicitly rejected.
- Source hashes, exact sections, small factual tag schema, acceptance restrictions
  and unverified final-2.32 semantic gates are in
  `docs/design/TIFF_EXIF_NORMATIVE_SOURCES_AND_READ_POLICY.md`.
- Implemented internal bounded classic TIFF/EXIF reading, known links, cycle/shared
  pointer detection, overlap/alignment checks, structured failures, finite budgets,
  raw MakerNotes and JPEG-thumbnail range/boundary preservation.
- Added the already approved MetadataLimits/MetadataException contracts in Core.
  No public reader facade, serializer, editing or JPEG container code is added.
- Added independent both-order fixtures, malformed graph/range/type/count tests,
  budgets, thumbnail checks and reproducible mutation smoke tests.
- `git diff --check` passed. Local SDK remains absent; runtime gate awaits CI.

### M04 CI and merge — 2026-11-07 — Gate closed

- Commit `ac9c9e87d21941f66ffa295b85c63eee776eddd3` passed Linux/Windows/macOS
  restore, Release build, tests and TRX checks in run 42418170361.
- Evidence: https://github.com/sensboston/MetadataSharp/actions/runs/42418170361
- `gh run watch 42418170361 --exit-status --interval 15` exited 0.
- PR #3 merged using `gh pr merge 3 --merge`; GitHub confirmed MERGED at commit
  `c450fcaaa9c1d0927311336f72caef2e27d9bc82`. M05 began only after this gate.

### M05 — 2026-11-07 — Submitted to CI

- Added deterministic internal EXIF directory/value/thumbnail planning; no payload
  serialization, file rewriting or public edit API. Source byte order is retained.
- Unknown/opaque source tags block rebuilding even if absent from desired metadata.
  Known structural values and thumbnail entries are protected; pointer offsets are
  recomputed from the planned TIFF base. Rechecked budgets after synthesized links.
- Tests cover boundary/alignment/ordering, exact APP1 maximum, pointer rebasing,
  thumbnail ownership, refusals, all affected budgets and disjoint planned ranges.
- Design: `docs/design/EXIF_LAYOUT_PLANNING_AND_SAFE_REBUILD_REFUSALS.md`.
- `git diff --check` passed. Runtime gate awaits three-platform CI.

### M05 CI and merge — 2026-11-07 — Gate closed

- Commit `976983b9f36fddde9d0ab3eb558d4de9ea65e4e9` passed restore, Release build,
  tests and the 142-case TRX floor on Linux, Windows and macOS in run 42418449461.
- Evidence: https://github.com/sensboston/MetadataSharp/actions/runs/42418449461
- `gh run watch 42418449461 --exit-status --interval 15` exited 0.
- `gh pr merge 4 --merge` succeeded. M06 began after the successful gate and merge.

### M06 — 2026-11-07 — Submitted to CI

- Added internal plan-based TIFF EXIF serializer. Full layout/shape/pointer/thumbnail
  validation precedes all destination modification; short buffers remain untouched.
- Freeze plan collections, zero only the output region, retain suffix/source bytes,
  and preserve raw payloads/thumbnail without interpretation or decoding.
- Added independent II/MM and external-ASCII known bytes, rational/subdirectory
  round trips, thumbnail rebasing, boundaries/padding and forged-plan refusal tests.
- Design: `docs/design/EXIF_SERIALIZATION_PREFLIGHT_AND_OUTPUT_GUARANTEES.md`.
- `git diff --check` passed. No external-tool result or local build is claimed;
  runtime gate awaits the next three-platform CI run.

### M06 CI result — 2026-11-07 — Gate closed

- Commit `067bfed16de2ebd3d8e0d982a73d33e87ea9a706` passed restore, Release build,
  tests and the 155-case TRX floor on Linux, Windows and macOS in run 42418674786.
- Evidence: https://github.com/sensboston/MetadataSharp/actions/runs/42418674786
- `gh run watch 42418674786 --exit-status --interval 15` exited 0.
- Updated README, roadmap, build guide and project instructions to reflect actual
  M04–M06 completion, not the historical M03 checkpoint. No public JPEG facade or
  edit API has been implemented. Final-2.32 semantic verification remains explicit.
- Confirmed M05 PR #4 merged at `4f05d169c3e85c1ed1da8235c5c6400690799383`.
- PR #5 will be merged only after the final documentation head passes CI. Its
  authoritative merge result is in GitHub PR history, not predicted in this record.
- M07 is the next stage under standing authorization; no additional developer
  permission is needed for its already planned scope. It must verify JPEG marker
  rules from normative sources before coding and preserve the strict JPEG subset.

### M07 — 2026-11-07 — Submitted to CI

- Developer approved a combined M07–M09 PR, with per-unit commits and successful CI
  before dependent work. Recorded this batching policy without relaxing stage gates.
- Added internal pooled streaming sync/async baseline JPEG scanner through EOI,
  structural table/frame/scan validation, signatures/ranges and strict refusals.
- Reviewed T.81 Annex B and baseline restrictions plus JFIF reference; document
  hashes, exact subset and non-decoding guarantees are in
  `docs/design/JPEG_MARKER_SCANNING_AND_SUPPORTED_SUBSET.md`.
- Added authored minimal grayscale JPEG fixture and scanner tests for malformed
  inputs, signatures, truncation, deterministic mutations, short reads and real
  sync/async separation. No foreign image fixtures or implementation source.
- `git diff --check` passed. Runtime gate awaits CI; M08 has not started.

- Initial M07 run 42419981805 failed compilation on xUnit2031 (filtered Assert.Single
  analyzer); tests did not run. Corrected the assertion, hardened the async short-
  read test double against MemoryStream fallback dispatch, and disabled context
  capture in byte reads. No analyzer suppression or relaxed CI gate was used.

### M07 CI result — 2026-11-07 — Gate closed

- Corrected commit `3437bb6a80bfa907c7aeed00aa082f8b18a2885e` passed Linux,
  Windows and macOS restore/build/tests and the 176-case TRX floor in run 42420076893.
- Evidence: https://github.com/sensboston/MetadataSharp/actions/runs/42420076893
- `gh run watch 42420076893 --exit-status --interval 15` exited 0.
- PR #6 remains open for the agreed combined M07–M09 group. M08 began only after
  this CI gate; the initial analyzer failure is preserved in the history above.

### M08 — 2026-11-07 — Submitted to CI

- Implemented the approved ExifEdit and JPEG facade, sync/async I/O, source identity
  preflight, safe addressed injection, whole removal and exact no-op copy.
- SaveAs uses a same-directory temporary file and no-overwrite publication; stream
  output failure may be partial. All shared pools returned; no source writes.
- Added preservation, refusal, ownership, cancellation, endian, path race/cleanup
  and output-failure tests. Shape validation does not claim full Exif conformance.
- See `docs/design/JPEG_EXIF_EDITING_OWNERSHIP_AND_FAILURE_SEMANTICS.md`.
- Local `git diff --check` passed; runtime gate awaits CI before M09.

### M08 CI result — 2026-11-07 — Gate closed

- Commit `c209dacaf76ab5b5843fc8575db26c4bb16c3dcf` passed Linux/Windows/macOS
  restore/build/tests and the 191-case TRX floor in run 42420352483.
- Evidence: https://github.com/sensboston/MetadataSharp/actions/runs/42420352483
- `gh run watch 42420352483 --exit-status --interval 15` exited 0.
- M09 began after this gate. No separate merge: PR #6 remains the agreed group.

### M09 — 2026-11-07 — Submitted to CI

- Added a public-API fixture consumer/generator with no metadata tool dependencies,
  and a separate optional Python CLI reference harness. Ordinary tests stay offline.
- Required CI installs standalone tools on ubuntu-24.04 and checks exact upstream
  Exiv2 0.27.6 / ExifTool 12.76; package revisions and command results are recorded.
- Harness compares raw tag values, reverse external edits, no-op/whole-removal and
  encoded-byte preservation; classifies only specifically allowed minimal-EXIF
  missing-default-tag warnings, failing unknown warnings and tool/version errors.
- Generated evidence includes SHA-256 and source/license manifests. Fixtures are
  authored; no GPL test/implementation is copied and no private photo is used.
- `python -m unittest discover -s tests/reference -v`: six offline policy tests pass.
  `git diff --check` passed. Required external run and release audit remain pending.

- Initial required reference run 42420600312 failed in harness normalization:
  Exiv2 `-P ktcv` selected interpreted output rather than the type column. Read
  the job check annotation via the GitHub API; changed flags to `-P kycv` for
  key/type/count/plain-value. No expected value or safety assertion was relaxed.
- Added independent main-path skip/failure/nonzero-exit tests (nine Python tests
  total). Audit also corrected premature EOI classification to InvalidJpeg and
  added its regression case. Generated Python caches are excluded from Git.

### M09 corrected gate and first-release audit

- Commit cbb0a2b passed PR run 42420756667 on Linux/macOS/Windows with a 192-case
  TRX floor and nine Python tests, plus the required pinned reference job.
- Both references read authored values; reverse writes and exact no-op/encoded
  preservation checks passed. ExifTool reported missing IFD0 XResolution, explicitly
  classified as a minimal-EXIF limitation rather than full conformance.
- Added `docs/audits/INITIAL_JPEG_RELEASE_AUDIT.md`: reproducibility, fixture rights,
  GPL black-box boundary, discrepancy disposition, safety/dependency audit and
  publication checklist. Updated current-state docs. Final hardening adds a tenth
  Python test for exact warning tag/name pairs, rejects optimized Python, and
  records source/recipe hashes, installed tool copyright notices and SDK evidence.
- No package publication, broad conformance or measured allocation claim. Final
  head CI remains required before merging the grouped PR; no M10 work started.

### M07–M09 final gate and merge confirmation

- Final head f3a51ae7239a550c0004d26df0a79bf1437009be passed PR run
  42420919484 and push run 42420914771. All three .NET OS jobs and required
  external-reference validation passed; offline Python suite has ten tests.
- PR #6 merged as f10ec6b2d830eb7c919cf758962c26de775894f3 on 2026-11-07.
  This records the completed final gate, superseding the preceding submission note.

### M10–M11 contract proposal (implementation pending)

- Developer requested the next coherent group on 2026-11-07. Active working branch:
  arena/ca9ac984-metadatasharp, based on the merged M07–M09 work. No unrelated changes.
- Read M10/M11 prompts, development policy and existing JPEG/EXIF contracts/API.
- Added `docs/design/XMP_PACKET_SIDECAR_AND_JPEG_CONTRACT_PROPOSAL.md`: exact proposed
  public API, framework-only XML dependency decision, restricted literal/array/lang
  RDF profile, opaque packet behavior, UTF-8 scope, hostile-input budgets, sidecar
  ownership/publication, JPEG signature/duplicate/Extended XMP refusal policy,
  metadata independence and per-unit acceptance gates.
- Located Adobe-hosted repository XMP Part 1/3 PDFs and recorded source revision/blob
  identifiers. Edition/section/content review remains a preimplementation gate;
  no SDK implementation, tests or PDF files imported.
- Contract-only change; no parser/writer implementation, dependency addition or new
  tests executed. M10 cannot begin implementation until the new contracts are
  approved. After approval: normative review, M10 implementation and green CI, then
  M11 implementation; final-head CI before the one grouped PR is merged.

### M10 approval, normative review and implementation submission

- Developer explicitly approved the M10–M11 proposal and continued execution on
  2026-11-07. Marked the contract approved; no implementation preceded approval.
- Reviewed Adobe XMP Part 1 (April 2012) and Part 3 (January 2020); hashes, sections,
  deliberate profile restrictions and JPEG size/placement discrepancies are in
  `docs/design/XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md`.
- Implemented immutable literal/array/language models, bounded XML validation before
  DOM allocation, opaque unsupported RDF, addressed edits, exact no-op, bounded
  serialization preflight, detached stream/path loads and safe new-path saves.
  Added InvalidXmp/XmpTooLarge at the end of the existing error enum; no new package.
- Added independent M10 tests for known XML, namespace identity, qualifiers/opaque
  refusal, Unicode/arrays/languages, ambiguity, XML/encoding attacks and budgets,
  ownership, short actual-async I/O, cancellation, no-op and sidecar publication.
- Local SDK remains unavailable. Submission requires actual three-platform Actions
  validation before M11; no .NET result is claimed from local execution.
- Initial M10 CI 42422121306 failed compilation because the new test file omitted
  its explicit Xunit import. Added the import (no analyzer suppression) and extra
  mid-I/O cancellation/partial failure and post-write publication cleanup tests.
- M10 corrected commit b37ce98 passed PR run 42422179802 on Linux/macOS/Windows
  with a 233-case minimum, ten offline Python tests and the existing required
  external-reference job. This satisfies the predecessor gate before M11 changes.

### M11 implementation submission

- Extended scanner classification by exact standard/extension signatures only;
  kept existing EXIF parsing and JPEG structural acceptance unchanged.
- Added addressed JPEG XMP reads/saves with hash preflight, combined logical
  metadata budgets, unique-packet policy, extension/stale-reference refusal,
  malformed/opaque whole removal and exact no-op/EXIF independence.
- Added JPEG XMP boundary/preservation/ambiguity/async/ownership/failure tests.
  Public fixture consumer/harness now checks external literal reads, rich sidecar
  arrays/Unicode language alternatives, reverse writes/no-op and 65,502–65,504-byte
  boundaries. External outcomes are pending, not assumed from authored tests.
- First M11 commit a1609be passed all three OS builds/tests (253-case minimum),
  but required external validation failed because ExifTool `-lang all` is not a
  language-alternative selector. Corrected the harness to request Title and
  Title-ru explicitly, preserving both value assertions. No library refusal or
  expected metadata value was relaxed. Added late-placement, opaque structure,
  combined budget, EXIF independence and deterministic publication-race tests.
- Audit found an alternative-array semantic risk: sorting language keys could
  change the preferred first item when x-default is absent. Retained original
  nondefault language order (still emitting x-default first) and added an explicit
  unrelated-edit regression. This is a preservation fix, not a contract expansion.

### M11 corrected gate and group audit

- Commit 1e25408 passed PR run 42422570403: Linux/macOS/Windows Release builds,
  a 259-case .NET floor, ten offline Python tests and required external references.
- Both tools read all three 65,502/65,503/65,504-byte boundary packets. Rich sidecar
  subjects/default/Russian title values, externally modified CreatorTool reads,
  exact XMP no-op/removal and encoded-byte checks passed. No public boundary changed
  to accommodate the historical PDF arithmetic discrepancy.
- Added XMP usage/ownership/failure documentation and updated current-state docs.
  Final harness hardening validates XMP JPEG/sidecar/boundary inputs explicitly
  and attributes each warning to its fixture. Required final-head CI is pending
  before merging PR #7. No M12 implementation or package publication.
- Final hardening run 42422760070 kept all OS tests green but found an unclassified
  ExifTool warning: `[minor] XMP is missing xpacket wrapper`. Rather than accept a
  new warning category, emit the standard writable PI pair for newly serialized
  packets (Part 1 section 7.3.2 allows an empty begin value). No-op keeps original
  bytes. Added an independent exact-wrapper regression; boundary fixture sizing is
  based on measured serialization overhead and retains all three target sizes.
- Wrapper-corrected commit 2b760c69bc39d2c0a334d66e131b02ad869c6310 passed PR run
  42422888266: all three OS jobs with a 260-case minimum, ten Python tests and
  the full required external job, including explicit XMP/boundary validation.
  Missing-wrapper warning eliminated; no new validation-warning category accepted.
  Only the previously documented minimal-EXIF missing-default-tag limitation remains.
- Documentation-only final count/evidence update follows; merge still requires its
  final-head checks. M12 contract review is the next eligible unit, not implemented.

### M10–M11 final head and merge confirmation

- Final documentation head 332ede3c500621bc7e67b068e687d6b57912bacc passed PR run
  42423001909 and push run 42422998072: all three OS jobs, 260-case floor, ten Python
  tests and required Exiv2/ExifTool validation. PR #7 merged on 2026-11-07 as
  7dafca53cab4ccf98539034585e5bcef69acdbc1. Verified via gh before starting M12.
  This supersedes the earlier final-head pending submission notes.

### M12 contract proposal (implementation pending)

- Developer requested continued execution. Current clean branch:
  arena/da38f569-metadatasharp, based on merged M10–M11 work.
- Read the M12 prompt, development policy, current limits/errors and predecessor
  status. Added `docs/design/IPTC_IIM_PHOTOSHOP_IRB_AND_JPEG_CONTRACT_PROPOSAL.md`.
- Proposed one M12 PR with separately gated M12a codec/resource framing and M12b
  JPEG integration checkpoints; exact public signatures, raw repetition/order
  preservation, restricted text/encoding policy, extended lengths, Pascal name/data
  padding, unknown-resource preservation, conservative ambiguity rules and existing
  IPTC digest maintenance/removal. No EXIF/XMP synchronization or new dependency.
- Exact normative source review remains a preimplementation gate, not an executed
  result. Contract-only proposal; no production/test implementation changed and no
  new test execution claimed. Requires explicit approval of new public/safety
  contracts before M12a implementation. No package publication.

### M12 approval and M12a submission

- Developer explicitly approved the M12 contracts/checkpoint split on 2026-11-07.
- Reviewed IIM 4.2 (July 2014), Photoshop File Formats (June 2012 HTML mirror) and
  XMP Part 3 (January 2020) storage/digest sections. Source blobs/hashes, six-field
  validation table and deliberate restrictions are in the M12 normative review.
- Implemented owned raw IIM datasets/occurrences, preserved untouched framing,
  bounded normal/extended lengths, charset-aware text helpers and last-wins edits.
  Internal Photoshop framing preserves unknown resources/names/padding and updates
  only IPTC plus an existing digest; no new runtime dependency.
- Added independent known-byte, repetition/encoding/control/budget/length/parity,
  digest vector and APP13-capacity tests. Local dotnet is absent; required three-OS
  CI is pending before M12b, and no local .NET test result is claimed.
- Pre-CI audit corrected two new expected-byte test initializers to C# 12 collection
  expressions (spread syntax is not valid in legacy array initializers). No test
  assertion was removed; corrected-head CI remains required.
- Corrected M12a head f45a96be8bc8399a2c92e33f48581f1ecc3edccc passed PR run
  42425723428: Linux/macOS/Windows Release builds, 288-case floor, ten Python tests
  and required existing Exiv2/ExifTool checks. M12b begins only after this gate.

### M12b implementation submission

- Added exact Photoshop APP13 discovery and independent IPTC Read/Save APIs with
  source hash preflight, combined logical budgets, conservative ambiguity/digest
  policy and safe non-overwriting publication. No EXIF/XMP pipeline behavior changed.
- Added JPEG IPTC preservation, digest, signature, duplicates, malformed removal,
  even-capacity, budget, async ownership/cancellation and path-race/failure tests.
- Extended the owned public-API fixture consumer/reference harness with repetitions,
  Unicode, existing digest maintenance, reverse writes, resource-preserving removal
  and a 65,506-byte IIM/full APP13 boundary. Added independent repeated-row parser
  policy tests. External reference outcomes remain pending.
- First M12b commit 5c2376b passed PR run 42426038001: all three OS jobs, 304-case
  floor, twelve offline Python tests and the full required external-reference job.
  Repeated/Unicode IPTC facts, reverse writes/no-op, digest refresh, exact removal
  preserving a copyright resource, and full-capacity repetitions all passed.
- Audit tightened count/byte checks before allocating new IIM/IRB frames and checks
  cancellation after the final hash read. Added partial-output/cancellation and
  captured-growth regressions plus a deterministic 256-case owned mutation smoke
  (not a general fuzzing or security-certification claim).

### M12 audited gate and documentation

- Audited head f19471ec7d1e048423e8d9d3ed7e217b8832c8e0 passed PR run 42426187721:
  all three OS builds/tests, 309-case floor, twelve offline Python tests and full
  required Exiv2 0.27.6 / ExifTool 12.76 validation. No new warning category accepted;
  the existing minimal-EXIF XResolution limitation remains explicit.
- Added `IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md` with examples, raw/text
  semantics, exact digest/IRB behavior, actual even-capacity limits, budgets and
  path/stream failure rules. Updated current-state docs and preserved historical
  gate counts. No new dependency, package publication or general IIM conformance claim.
- Final documentation head still requires green CI before merging PR #8. M13 (PNG)
  contract review is next eligible; no M13 implementation started.

### M12 final head and merge confirmation

- Final M12 head a0e1ad752359fc5da19827ba2c85e67efb85277e passed PR run
  42426353953 and push run 42426350017: three OS jobs, 309-case floor, twelve
  Python tests and the required external-reference job. PR #8 merged on
  2026-11-07 as fc73316554cffd8a7a95b98a3610aa61427b3164, verified via gh.
  This supersedes the preceding final-documentation pending submission note.

### M13–M14 grouped contract proposal (implementation pending)

- Developer approved grouping PNG/WebP into one PR: shared contract/normative review,
  M13 implementation and green CI, then M14 implementation and final audit/CI.
- Active clean working branch is arena/a6abf89b-metadatasharp. Read M13/M14 prompts,
  development policy, existing limits and the current EXIF core's APP1-sized cap.
- Added `PNG_AND_WEBP_METADATA_CONTRACT_PROPOSAL.md`: exact concrete facades/options,
  static-only scope, strict PNG CRC/critical ordering, eXIf/iTXt identification,
  bounded compressed XMP, unknown-chunk safety, WebP simple-to-VP8X promotion,
  RIFF/flags/padding/alpha policy, independent EXIF/XMP operations and shared
  stream/path failure rules. Proposed retaining the 65,527-byte TIFF core cap
  rather than silently expanding it with the new containers.
- This is a contract-only proposal. No parser/writer/dependency/test changes or new
  test execution. Exact source review and new public/safety contract approval remain
  required before implementing M13. No standalone TIFF/DNG, animation or publication.

### M13 approval and implementation submission

- Developer explicitly approved PNG/WebP contracts and authorized M13 then M14,
  with interest in subsequent M15. M15 still needs separate read-only standalone
  TIFF contracts; no TIFF/DNG implementation is authorized by PNG/WebP signatures.
- Marked the shared proposal approved. Reviewed published PNG Third Edition and
  RFC1950/1951 mechanics; source hashes/decisions are in the normative review.
- Implemented full streaming PNG CRC/structural validation, static-only profile,
  conservative unknown-chunk safety, lazy independent EXIF/XMP snapshots, addressed
  preflighted edits, exact no-op and non-overwriting path publication. Retained the
  approved 65,527-byte EXIF cap and original JPEG behavior.
- Compressed XMP has an independently authored bounded DEFLATE framing walk plus
  framework expansion and Adler verification; reject truncation/trailing streams
  before returning a packet. No reference codec/CRC implementation was copied.
- Added owned one-pixel fixtures and independent CRC, structure, metadata, compression,
  limits, preservation and async/path tests. Local dotnet remains unavailable;
  no .NET success claimed before actual three-platform CI. M14 is gated on M13.

- First submission failed only xUnit blocking-task analyzer checks; tests were made
  properly async rather than suppressing the analyzer. Revised d548ae8 passed all
  .NET platforms (at least 338 cases) but PNG external reverse validation exposed
  opaque RDF after unqualified ExifTool Artist editing. Restrict the operation to
  EXIF:Artist to avoid implicit family synchronization; retain strict XMP profile
  and include raw authored-packet diagnostics if still outside it. No green group
  or M14 start claimed until the reference job also passes. Python policy 12/12 pass.

- d602f1f again passed all .NET platforms. Packet diagnostics established the
  reference failure is specifically ExifTool's PNG `end='r'` read-only wrapper,
  not CRC/compression loss or RDF namespace handling. Retain the approved opaque
  behavior and test no-op/removal/refusal explicitly; record unsupported reverse
  semantic property reads in the reproducibility report. No warning suppression.

### M13 completed checkpoint; M14 eligible

- M13 head c2f3c58ca415bb002071ae7a004f38f1862f462e passed required PR run
  42429048302: Windows/Linux/macOS Release build/tests (338-case minimum), Python
  policy 12/12, pinned Exiv2 0.27.6/ExifTool 12.76 PNG/JPEG reference job green.
- Observed PNG reference limitation is explicit in the report/review: ExifTool
  read-only wrappers remain opaque, exact copy/removal works and semantic edits
  refuse before output. Existing warning classifier was not broadened.
- Starting M14 only after that completed checkpoint. PNG/WebP share approved
  contracts/PR #9; no merge until the complete group passes final head CI.

### M14 implementation submission

- Added independent concrete WebP facade/options and bounded RIFF/header scanner:
  strict size/padding, VP8/VP8L static images, VP8X flags/canvas, ICC/ALPH order,
  simple promotion, no downgrade, raw/legacy EXIF and independent XMP. Unknown
  chunks and animation cannot be edited/flattened. No coded-pixel decoding.
- Normative review records metadata's permitted arbitrary ordering and VP8L alpha
  hint semantics without inventing pixel facts. Fixtures are four owned pixels
  encoded by pinned test-only Pillow/libwebp; recipe/hashes/notices checked in.
- Added offline WebP boundary/preservation/independence and async/publication tests,
  plus required CLI reads/reverse edits and separately pinned dwebp decoded-PAM
  equality. Python policy 12/12 passes; dotnet remains absent locally, so new .NET
  code and reference additions await Actions, not reported as green yet.
- PNG audit additionally fixed missing-property removal with existing EXIF: return
  an exact no-op before planner/unknown-chunk edit restrictions. No contract change.

### M14 completed checkpoint and final group audit submission

- M14 cc8b8700e63cd922f9e30b60db83cd6c534be81f passed both PR run
  42429453288 and push run 42429449491: all three .NET platforms (352-case minimum),
  Python policy 12/12 and pinned reference job including dwebp 1.3.2 green.
- Final audit adds PNG missing-property/no-op regression, exact XMP-keyword near
  match, empty/reserved DEFLATE tests, and corrects compressed iTXt nonzero method
  classification to InvalidPng (no new public behavior/profile). Existing malformed
  compression still refuses before destination writes. Added both-format path races,
  emission-failure temporary cleanup, owned path closure, snapshot stability and
  cancellation after partial caller-stream emission. No rollback promise added.
- Updated user-facing supported-subset docs/README. Captured exact encoder wheel
  URL/digest in fixture provenance; no new production dependency, reference code or
  camera image imported. No full-format, performance or package-publication claim.
- M15 exact standalone read-only TIFF proposal prepared, explicitly NOT approved
  or implemented. It is immutable layout/range accounting, not metadata rewriting,
  BigTIFF, DNG, or a larger-cap reuse of the JPEG EXIF parser. Developer approval
  remains required before code. M16 is not authorized.
- Final audit head CI is still pending. Do not merge based on predecessor results
  alone; require the final head's platforms and reference checks.

- Hostile-compression audit caches immutable fixed Huffman trees instead of
  reconstructing them for each empty fixed block; a 10,000-empty-block owned RFC
  bit fixture and concatenation-refusal test cover exact framing. Adler traversal
  now checks cancellation at bounded checkpoints. No measured allocation claim.

- Added independent ExifTool facts for an explicitly authored legacy WebP EXIF
  wrapper and its canonical raw-TIFF rebuild, rather than relying only on our own
  reader/writer round trip. Final required reference gate includes both variants.

### M13–M14 audited code checkpoint

- Head da259516449cec78d17e3b9f3790a73c3237d4e4 passed required PR run
  42429946046, all four jobs: 361 passing .NET tests on Windows/Linux/macOS,
  Python 12/12, pinned Exiv2/ExifTool/dwebp checks including legacy WebP EXIF.
- Completed the permanent group audit with actual per-unit/run evidence, reference
  discrepancy classification, fixture licensing/provenance, failure guarantees and
  deferred limits. Final documentation head must be revalidated before PR #9 merge;
  actual final-head/merge evidence is posted to PR #9 after completion.
- Next implementation scope: M15 only after explicit approval of its separate exact
  read-only TIFF contract. Proposal is checked in; no TIFF/DNG code was started.

### M15 approval and implementation submission (2026-11-07)

- Developer explicitly approved STANDALONE_TIFF_READ_ONLY_CONTRACT_PROPOSAL.md.
  Predecessor PR #9 merged at 269dc92cb5bf1218df230e1ced3140c95ca7d81c after
  green final runs 42430081717/42430077969. Work is on arena/f0470529-metadatasharp.
- Added separate immutable TiffLayout.Read[Async] facade, approved models/options,
  bounded structural/image range accounting and explicit unsupported-layout issues.
  No TIFF writer, shared facade refactor, EXIF cap expansion, BigTIFF or DNG support.
- Reviewed TIFF6/CIPA normative field prose; finite logical bookkeeping formula,
  unsupported matrix and M16 boundary are recorded in the normative/support doc.
- Added owned II/MM strip/tile fixtures, DAG/cycle/pointers/overlap/planes/preview,
  limit/type/unsupported/mutation and true-async/path tests. Required independent
  ExifTool/Exiv2 reads and ExifTool rewritten-TIFF ingress exercise public API.
- Local dotnet remains unavailable. Python policy and whitespace checks run locally;
  no new .NET or external-reference success claimed before Actions results.

- Initial .NET implementation passed all three platforms (382 tests); external
  reference gate is inspected separately before declaring the unit green. Added
  explicit sample-layout issue, directory/link/value budget, no-image and null/odd
  pointer coverage, plus a 128-KiB TIFF that forbids pixel reads and survives as an
  immutable snapshot. Pointer-array link ceilings now preflight before decoding.
  New audit head still requires full platform/reference CI before merge.

- First submission 3c6e5636ce8a5f4d011fa9c32604b8f29f1aacc5 passed required
  run 42430554406 (all platforms, 382 tests, pinned reference job). Final normative
  audit tightened TIFF6's explicit nonempty-IFD framing requirement and replaced
  empty synthetic graph nodes with a valid one-field metadata directory. Added
  zero-entry refusal regression, without changing the approved public API/scope.

- Final I/O audit preserves physical stream exceptions without reclassifying them
  as semantic TIFF errors; configured input budgets and the 4-GiB profile ceiling
  preflight before reads. Added sync/async failure/borrowed-lifetime and oversized
  sparse-source tests. No source hashing or future editing authority is implied.

- Thumbnail audit covers preview-only metadata IFDs without TIFF dimensions:
  preserve their bounded candidate extent in global ranges and report unsupported
  layout rather than guessing JPEG dimensions or declaring complete image support.
  This uses the approved issue-bearing unsupported-layout path; paired fields,
  budgets and all overlap checks remain mandatory. No pixel/JPEG decoding added.
