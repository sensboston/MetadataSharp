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

### M15 audited implementation checkpoint

- e806adc0d35913a807e6050632afbb06e3f7e85e passed required PR run 42430917666:
  392 passing .NET tests on each of Windows/Linux/macOS, Python policy 12/12 and
  required pinned CLI references. TIFF fact checks/reverse ingress are explicit,
  not a full TIFF validator or decoded-pixel conformance claim.
- Permanent M15 audit, implemented support matrix and usage/limitations record
  approved behavior, exact evidence, normative source provenance, finite limits,
  unsupported cases and remaining M16 boundary. Final documentation head requires
  its own green CI before PR #10 merge; actual final-head evidence posted there.
- M15 introduces no writer, new production dependency or larger embedded EXIF cap.
  Next implementation unit M16 still needs separate exact public/safety contracts.

### M15 final merge confirmed; M16 contract-only proposal (2026-11-07)

- Verified PR #10 MERGED, merge 80dc75e62eff242d2b98bc4b14b3090cf4ea60ce.
  Final source 812bd76af3241d479e7b9fff07c9dd6a8197a118 passed PR/push runs
  42431094052/42431090942 (392 tests/platform, Python 12/12, required references).
- Developer requested continuing; active work branch is arena/d7ec6276-metadatasharp.
  Prepared exact M16 public/safety proposal, not implementation: addressed baseline
  descriptive fields, source-relative IFD keys, SHA-256 preflight, append-preserve
  graph replacement, unchanged image/value offsets and new-path publication.
- Explicit compromises require approval: obsolete metadata remains physically in
  the original prefix; repeated saves grow files; unknown/private/MakerNote and
  opaque extensions refuse effective edits. No general EXIF/XMP/IPTC TIFF editor,
  compaction, privacy scrubbing, BigTIFF, DNG, new production dependency or EXIF cap
  expansion. TiffLayout.IsSupported alone is not writer permission.
- M16 proposal is PROPOSED, not approved. No new public C# API/parser/writer code,
  normative-write-conformance claim or implementation-test pass is asserted.
  Unit tests already exist and remain mandatory for every future stage; current
  completed checkpoint has 392 .NET cases plus Python policy/reference checks.
- Remaining roadmap units are M16 restricted TIFF editing, M17 DNG feasibility/
  contracts only and M18 measured performance/packaging audit. DNG implementation
  requires a separately approved follow-up, not an assumed part of M17.
- Proposal-only local checks: `python -m unittest discover -s tests/reference -q`
  passed 12/12; `git diff --check` passed. No new .NET implementation/test result
  is claimed by this documentation checkpoint.

### M16 explicit approval and pre-implementation recovery checkpoint (2026-11-07)

- Developer explicitly confirmed the exact M16 contract, including retained obsolete
  bytes, and required repository-accessible continuity documentation BEFORE code.
- Marked the contract approved; added docs/CURRENT_STATE.md with predecessor hashes/
  CI evidence, approved decisions, implementation sequence, file map, environment,
  reproduction commands, safety stop rules and deferred milestones. Linked it from
  README, development policy and project instructions; corrected stale test summary.
- This is a documentation-only checkpoint. No M16 C# source/writer exists yet;
  approval is not an implementation or green M16 test result. Commit/push this
  checkpoint first, then begin normative review and implementation under its plan.

### M16 initial implementation submission after preserved recovery checkpoint

- Recovery checkpoint 4bf56c6 was pushed before any C# edit, as explicitly requested.
  Then reviewed TIFF6 placement/fields/sort/alignment and CIPA pointer mechanics;
  exact blobs/hashes and decisions recorded in the M16 normative-write review.
- Added initial approved API/options/edit schema, finite append-graph planning and
  document source identity/sync/async/new-path pipeline. No embedded EXIF serializer
  reuse, source in-place writes or original-storage relocation. Original metadata
  bytes are retained as explicitly approved; there is no privacy/compaction claim.
- Added initial independent writer tests for II/MM strip/tile, full original prefix
  retention, all directory link kinds/shared targets, no-ops/opaque refusals, last
  field removal, typed keys/output limits, endian conversion and actual async I/O.
- Python policy 12/12 and git diff --check pass locally. .NET source/tests have not
  been built locally (SDK absent); initial Actions results must be inspected.
  M16 external-write references, full acceptance matrix and final audit remain
  PENDING. Updated CURRENT_STATE.md so resuming does not mistake this source for
  either a no-code proposal or a completed/validated writer.
- Initial source ae7a946 builds on Linux/macOS but test execution failed; details
  require inspection before any green claim. Added explicit TRX failure annotations
  so test errors remain accessible even when Actions artifact/log hosts cannot be
  downloaded. No failing test was disabled or assertion weakened. Initial M16 write
  reference generation/fact/pointer checks are now submitted alongside diagnosis.

- Initial failure annotations identified two II/MM graph fixture failures BEFORE
  writer planning: the generic helper put a SubIFD array at 768+256=1024, then
  another synthetic IFD overwrote it. Moved that array to the explicit free 1536
  region and added source-layout validation before testing the writer. No parser
  range/overlap check or writer safety assertion was weakened. Full revised CI
  and initial M16-specific independent write references remain to be inspected.

### M16 first green implementation/reference checkpoint; range-limit audit

- Source 53da0549868e0b4eb2261ca10096216aaac6cb6e passed required PR run
  42435065413: 404 .NET tests per Windows/Linux/macOS, Python 12/12, all required
  references including initial M16 edited TIFF facts, reverse edits and independent
  output-pointer/range/prefix retention checks on four owned image variants.
- Subsequent audit added active output range-count preflight (retained external
  alias deduplication plus each newly staged external block), so a written file
  fits captured MaxAccountedRanges as well as byte ceilings. Regression proves
  external additions fail before output while inline additions reopen at ceiling.
- This newer source requires its own full CI. M16 remains draft/in progress: all
  accepted compression/plane/preview/value-alias/overflow/failure families, final
  usage/support/audit and final-head gates are still outstanding. CURRENT_STATE.md
  records actual evidence and next steps; do not merge or start M17 yet.

### M16 persisted continuation checkpoint — initial writer green, unit incomplete

- Code head 600bbc1eef2b21543ec2e8bfc13c0d99e631dd1d passed PR run
  42435227396: all three Release platform jobs (405 passing .NET cases each),
  Python policy 12/12 and mandatory independent reference job green. This includes
  the output range-ceiling regression and initial M16 edited-file facts/IFD-range
  checks. There is no new local .NET claim; SDK is still absent.
- Recovery documentation is in Git and pushed. docs/CURRENT_STATE.md is the entry
  point with approved scope, exact evidence, file map, failure diagnosis and next
  steps. PR #11 remains DRAFT, not merged or a completed M16 milestone.
- Next work: extend accepted compression/plane/preview/shared-value/sparse-overflow
  tests and write failure/temporary-publication race/cancellation coverage; audit
  resulting pointer/schema/resource proofs; finish usage/support/audit documentation
  and actual final-head gates. Continue M16 without repeating approval. M17/DNG
  code and package publication remain outside the approved implementation scope.

### M16 expanded acceptance matrix submission

- Added accounting-only compression selectors 1/5/7/8/32946/32773 for strip/tile
  preservation (not a claim that the synthetic payload encodes those codecs),
  II/MM separate RGB/explicit-alpha planes, shared RATIONAL alias/storage ceilings,
  candidate preview/odd-prefix alignment, opaque-extension refusals, entry/value
  limits and pure planner 4-GiB edge checks without giant allocations/hashing.
- Added deterministic destination race, temporary cleanup after emission read fault,
  caller-owned partial output, snapshot-before-async-I/O and cancellation after
  emission/temporary creation tests. No safety check or warning classifier relaxed.
- Static test floor raised to 435; actual .NET and required reference results await
  Actions. Local Python policy 12/12 and git diff --check pass. M16 is still draft;
  final audit/documentation and final-head gate remain required.

- Descriptive-target audit fixed DateTime reads/replacement of an existing invalid
  calendar value: it now follows the approved malformed-target refusal, while
  unrelated edits preserve its raw bytes and whole-field removal bypasses content.
  New-value validation rejects incompatible types/counts before copying raw bytes.
  Regression covers invalid target read/set, isolated edit and removal; added a
  128-KiB whole-file/snapshot test so no embedded-EXIF-size limit is implied.
  No public signature or approved write strategy changed. Updated floor 437.
- A documentation-update script accidentally overwrote PROGRESS.md in the preceding
  commit. Restored its full historical content directly from that commit's parent
  and reapplied this entry before continuing. No historical checkpoint was lost;
  source/tests were unaffected. Review diff statistics and document headers as
  well as whitespace checks when writing recovery records.

- A second resource audit now checks the edit-snapshot logical charge BEFORE
  copying its dictionary or starting source identity I/O, rather than only in
  later planning. Added an over-budget regression with a source-I/O trap; the
  captured budget and approved 32/key+value formula are unchanged. Floor 438.

- Final acceptance additions cover white/black gray, RGB, 8/16-bit samples,
  contiguous/separate layouts, both explicit alpha kinds, explicit unsigned sample
  format, plus all ten addressed tags through add/read/exact no-op/removal in II/MM.
  Full floor is 448. Added write usage/ownership/limits and audit/support records;
  mandatory references remain four real owned baseline variants, not falsely
  declared codec-valid synthetic fixtures. Final-head gates still pending.

- Observed required run 42436144783 SUCCESS at audited code
  635f8bb565eeaab72a9147d5e9825fdcde54c215: 438 .NET tests on each platform,
  Python policy 12/12 and mandatory references green. This validates the DateTime
  and snapshot-budget corrections, not the later ten added acceptance cases.
  CURRENT_STATE now identifies only final expanded-head/documentation gates and
  successful PR #11 completion as remaining M16 work.

### M16 completed implementation/acceptance gate

- Full source/test/documentation head **762dc206915e7989721645e0ee3a8adb479310c2**
  passed required PR run **42436352761**: **448 tests on each Windows/Linux/macOS**,
  Python policy **12/12**, mandatory pinned external references green. All ten new
  family/tag-matrix cases passed. No assertions or warning classifiers weakened.
- Implementation, complete accepted-family matrix, usage/support and audit are
  finished within the approved contract. Final documentation-only commit must also
  pass its own required checks before PR #11 is readied/merged. Actual final-head
  SHA/run IDs and merge receipt will be posted on PR #11 after success, avoiding
  circular commit recording. CURRENT_STATE gives a deterministic live-PR recovery
  check. No package publication and no DNG implementation; stop after M16.

## M17–M18 grouped execution authorization and recovery plan

Developer requested completing the remaining two units in one pass and one PR,
then activated arena/b166bb08-metadatasharp from M16 merge d476e37. PR #11 is
confirmed MERGED. M17 remains feasibility/proposed follow-up contracts ONLY;
requesting approval of that future plan does not authorize DNG implementation.
The grouped request authorizes proceeding to the independent bounded M18 audit
without implementing or silently approving the M17 proposal.

Before code: bounded M18 scope is reproducible public-API microbaselines on authored
JPEG/PNG/WebP/TIFF inputs (throughput, process allocations, hashes/environment),
three-platform CI, local-only package candidates/content/license/dependency/docs
inspection and an offline consumer smoke. No production API/safety changes, broad
optimization, new production dependency or publication. Existing 448 tests and
mandatory external-reference checks remain required. Benchmark evidence will be
small JSON files; binaries/packages stay ignored artifacts. Record actual failures
and unrun checks; do not invent results. Final head must pass before one PR merge.

### M17 feasibility deliverable

Reviewed pinned DNG 1.7.1.0 September 2023 prose (126 pages), mirror blob
5e4d714a2aa6b935286c6177991a72768fbaec0c, SHA256
9cdfe43d44751b662d1f0ace81fff84f6a927321f5d18d79054ce78dd499abcd.
DNG_SCOPE records raw/preview/profile/opcode/private/integrity/version families,
required contracts, narrow candidate, rights/oracle strategy and unresolved blockers.
Proposed D0/D1/D2 prompts explicitly require future approval; none implemented.
No SDK/GPL code/tests, PDF or external camera fixtures imported. M17 asks approval
of the follow-up plan, not of proceeding with the separately authorized M18 audit.
Local Python policy 12/12/diff checks pass; .NET/platform/reference gate via CI next.

### M17 gate before M18 tooling

M17 head 1a05ffe23ee6e8b766a6db777705e509755bb79b passed required run
42439542366: 448 .NET tests per platform, Python 12/12, required external references.
Only after that gate, M18 tooling adds synchronous public-API microbaselines and
three-platform local candidate-package/content/license/docs/offline-consumer audit.
Normal projects remain non-packable; opt-in audit candidates version 0.0.0-audit
are ignored, never published. XML docs gaps are reported, not hidden by a full-docs
claim. Measurements/package smoke have not yet run; await this head's CI.

First M18 run 42439656646 compiled/executed baselines but package audit failed on
all platforms; existing 448/platform tests and mandatory references remained green.
Job-log blob download returned EOF. Add bounded command/assertion diagnostics and
small JSON evidence as GitHub check annotations, retaining strict assertions and
artifact upload. No package-audit success is claimed before diagnosis/fix.

M18 diagnosis from check annotation 128606314112: candidate package lacked XML docs,
so the required-content assertion correctly failed. Audit opt-in documentation
properties were set too late in Directory.Build.targets. Move those properties to
Directory.Build.props (before SDK documentation defaults), retaining strict package
XML checks; add explicit SeNS author/copyright metadata and capture full SDK info.
Normal builds remain non-packable. No production source or API changes.

M18 fixed head 13bdcca7a16911b17e55356e756b471472be6bff passed run 42439833467:
all seven jobs including baseline/package/offline-consumer on all three platforms,
448 tests/platform, policy/references. Package content included DLL/XML/MIT/readme;
Core has no package deps, Formats only Core. GitHub truncates individual annotation
messages to 4096 bytes, so add compressed <=2500-byte chunks and a receipt-checked
collector for durable full evidence (blob downloads remain unavailable). No measured
results will be inferred from truncated messages. Reporting fix needs its own gate.

### M18 full measured evidence gate and grouped completion submission

Head 4d89a00e016d32845bd44395bba7f0076dcc1217 passed required 42439984148, all seven
jobs: 448/platform, Python 12/12, references and all three platform audits. Complete
baseline/package JSON recovered via compressed API annotations with checked source,
chunk completeness and receipts under docs/audits/evidence/m18. Eight workloads,
five batches each; runtime 8.0.33, SDK 8.0.427, X64 Linux/Windows and Arm64 macOS.
Candidate package XML counts 45/19, MIT/readme/license and dependency assertions,
isolated offline edit/reopen consumer all passed. Performance/reporting gaps and
unexecuted release checks remain explicit; no optimization/publication/DNG code.

M17 feasibility follow-up asks developer approval, not implicitly approved by the
grouped merge. Final source/documentation/evidence head must pass all seven jobs
before one PR #12 merge; immutable final-head run/merge receipt will be posted there.
After completed merge, all bounded M00–M18 roadmap deliverables are complete. No
further implementation unit starts automatically. Local Python policy/diff pass;
local .NET absent, actual SDK measurements were Actions only.

## Post-roadmap merged-branch cleanup

Developer explicitly requested removing obsolete branches and updating existing
records before selecting the next project stage. Active session branch is
`arena/d5125ece-metadatasharp`; it and `master` are protected and were not deleted.
M17–M18 PR #12 is confirmed MERGED at
`b7c9f3959b289f08c30d209b78d0915003111390`. Its final head 02be218 passed
42440206658/42440203208 (seven jobs); all bounded M00–M18 units are complete.

The initial checkout was shallow (master history count 1), so ancestry checks first
refused every candidate. Fetched full history with `git fetch --unshallow origin`;
no deletion occurred before proof. Verified there were no open PRs, matched each
candidate to merged PRs, required its exact remote tip to be an ancestor of
`origin/master`, then rechecked its live SHA immediately before deleting the GitHub
branch ref through `gh api --method DELETE`. No force-push, source/history removal,
tag/release deletion or unmerged-branch deletion was performed.

| Deleted remote branch | Verified tip retained in master | Merged PRs |
| --- | --- | --- |
| `arena/754dbace-metadatasharp` | `f87c70975ae73c04882692722b194f11dfac9ce9` | #1 |
| `arena/9e197a4e-metadatasharp` | `990ed33c6fcb18f579c6f305edb751dfae21a7d8` | #3, #4, #5 |
| `arena/a6abf89b-metadatasharp` | `84e70789aed436eb9ce124e465f1eb1912eb7e06` | #9 |
| `arena/b166bb08-metadatasharp` | `02be218820c6a6b3abdbd08091db1761c7771f7d` | #12 |
| `arena/c040176d-metadatasharp` | `f3a51ae7239a550c0004d26df0a79bf1437009be` | #6 |
| `arena/ca9ac984-metadatasharp` | `332ede3c500621bc7e67b068e687d6b57912bacc` | #7 |
| `arena/d7ec6276-metadatasharp` | `cbd36078e0f3061a6f21b741fcf7d49886a3c799` | #11 |
| `arena/da38f569-metadatasharp` | `a0e1ad752359fc5da19827ba2c85e67efb85277e` | #8 |
| `arena/e42dbe63-metadatasharp` | `9794b9c00b8530a67a3c8400ff99ea2db6a8b13a` | #2 |
| `arena/f0470529-metadatasharp` | `812bd76af3241d479e7b9fff07c9dd6a8197a118` | #10 |

Deleted **10** obsolete remote branches and pruned their remote-tracking refs.
`git ls-remote --heads origin` subsequently showed only master, before publishing
this active documentation branch. Local branches were already only master and the
active Arena branch; no local branch deletion was necessary. All commits remain
reachable from master and merged PR records retain review/CI evidence. Existing
tags/releases, including sdk_bootstrap, were preserved.

Updated CURRENT_STATE rather than creating a new cleanup document. No production
source, package/audit configuration or public contracts changed. Local Python policy
and whitespace checks run for this documentation change; its own CI gate must pass
before merging the documentation PR. Final head/check/merge receipt is recorded in
that PR after success. Preserve the active branch even after that merge; its deletion
is not part of this cleanup. Next project stage awaits developer selection; DNG
implementation and package publication still require separate approvals.

### Cleanup documentation CI infrastructure blocker

Documentation PR #13: https://github.com/sensboston/MetadataSharp/pull/13.
Head 34f1591684ae59c94ad1b335b4fd8d2cd5713172 runs 42441201239 and 42441196628
reported failures in all seven jobs, but GitHub annotations explicitly state:
"The job was not started because recent account payments have failed or your
spending limit needs to be increased. Please check the 'Billing & plans' section
in your settings" (confirmed check IDs 128610605935/128610605889/128610605873).
No build/test/audit executed in those jobs. This is an account Actions billing/limit
blocker, not a test failure, and predecessor green results do not validate this
head. Local Python policy 12/12 and git diff --check passed.

Branch cleanup itself is complete and remote inventory is exactly master plus
arena/d5125ece-metadatasharp. Documentation is saved/pushed, but PR #13 remains OPEN
until account owner resolves GitHub Actions billing/spending limit and required
checks run successfully. No gate bypass, merge override or repeated retry without
an infrastructure fix. Keep active branch. Next project stage awaits developer
selection; no new feature work began during cleanup.

### Explicit documentation-only merge exception and next-stage selection

Developer selected `dng_contract`: next stage is D0 exact read-only DNG contract
proposal, NOT implementation or release hardening. Added scope to ROADMAP.
Developer also explicitly selected `allow_documentation_only`: merge PR #13 despite
unavailable green CI, solely for documentation of branch cleanup/next-stage scope.
This exception is not transferable to code, API implementation, package changes or
future PRs; workflows/check requirements are not modified and failures are not
reported as passes. Latest head b78323e runs 42441262868/42441258668 failed before
jobs started; a single requested rerun via `gh run rerun 42441262868 --failed` was
rejected with "cannot be rerun; its workflow file may be broken". That generic CLI
message does not establish a YAML defect or successful execution.

This follow-up changes only existing Markdown files. Local policy 12/12 and diff
checks pass. Merge uses the explicit per-PR exception, with the final SHA/receipt
recorded on PR #13 after actual completion. Preserve current active branch. Next
D0 work must stop at its proposal/approval gate; no existing safety contract changes.

## Programme 2 authorization, first discovery and D0 proposal

Developer corrected .NET target to 10, approved independent near-complete Exiv2
capability programme and improvements/idiomatic .NET planning, conditional cheap
Framework support, documentation cleanup and starting work without routine further
questions. Live branch arena/d0eec35b-metadatasharp from PR #13 merge 820229f5.
Programme approval does not approve new DNG/public API contracts. The old one-off
PR #13 CI exception does not transfer to new work.

E00 recorded E00–E18 roadmap, parity denominator discipline, rights/source separation,
.NET 8/10 lifecycle and conditional Framework gate. Marked old roadmap/prompt entry
points historical in place; kept approved contracts/audits/provenance and old links.
E01 began with pinned Exiv2 v0.28.9 tag/commit, README/license/CLI docs (31 type rows,
six categories), 35 unique issue title/label/state signals and 12/37 discussions.
No bodies/code/tests/attachments imported; findings are discovery hypotheses, not
verified defects. Exiv2 0.28.9 CLI not run. Full E01 atomic inventory remains OPEN.

Official dotnet/core metadata blob 66392f8bd6d254f73cf7a2f42113bc4034a20747 confirms
.NET 8 EOL 2026-11-10 and .NET 10 EOL 2028-11-14. Neither SDK retarget nor Framework
support implemented. D0/E04 exact read-only DNG contract prepared, status PROPOSED;
no parser/editor/error enum added to code. Additional normative ledger, rights/oracle
and target compatibility gates explicitly block D1 even after surface approval.
Local checks and actual new PR/CI status recorded subsequently; no new .NET or
reference executable run is claimed in this documentation/research submission.

Programme submission local checks: Python reference policy 12/12; new research
JSON parsed with 31 format rows, 35 issue signals and 12 discussion metadata records;
new/edited local Markdown navigation links exist; git diff --check passed. These
are documentation checks, not .NET, pinned-CLI or DNG conformance results.


### Same-branch integration and additional requirements review

Push of local planning commit 246fb84 was refused (non-fast-forward). Fetched remote
9ce172a on the SAME assigned branch; it already contained PR #14 E00–E18 programme,
initial research and exact D0 proposal. Integrated rather than force-pushing or
discarding remote work. Resolved PROGRESS by preserving the existing research
record. Removed the locally introduced duplicate P-series roadmap; E00–E18 in
docs/roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md is the single active sequence.
Additional review reads selected user issue/discussion bodies as requirements,
not patches/linked source/comments/attachments. Initial title-only snapshot remains
truthful for that earlier pass; the new body-review supplement records its own scope.

### Follow-up research and actual infrastructure evidence

Added selected-body requirements supplement (8 issues, 6 discussions), preserving
initial title-only snapshot provenance and not following linked GPL source/patches
or attachments. Added E02 modern-target matrix/package gate and Framework source
API cost map; no cheap-compatibility or net10 execution claim. Existing code relies
on modern guards, spans/stream async/disposal, crypto and zlib APIs; no shims/TFMs
or new dependencies added. E00 direction stays approved, E01 full gate remains open,
D0 exact contract stays PROPOSED. Historical docs archive in place rather than lose
reviewed contracts/evidence.

Runs 42444022195/42444017972 on integration head d27aaed did not start jobs. Check
128619056830 explicitly reports account payment/spending-limit blocker. PR #14
remains OPEN; no CI exception carried from PR #13. Local policy 12/12, research JSON
parsing, selected Markdown links and git diff --check pass. New .NET/reference/
audit/runtime/DNG conformance checks are not claimed. Remote/local work reconciled
without force push or lost documentation. Final submission requires own green CI
or an explicitly approved per-PR deferral before merge.

### Public visibility and fresh CI attempt

Developer changed repository visibility to public. GitHub API confirms
private=false / visibility=public. This does not waive provenance, safety, contract
approval or CI requirements, and no comprehensive historical secret/rights audit
is claimed. Attempt to rerun latest PR run 42444129097 was rejected by gh with
"cannot be rerun; its workflow file may be broken"; that generic message is not
proof of invalid YAML. Push this documentation checkpoint to trigger fresh checks
on the active branch after the visibility change. Results remain pending until
observed; old billing failures are historical, not evidence of the new attempt.

### Public-repository verification succeeded

Fresh head 849ff0cbee69746318ba39107b12b6c440bde8da passed run
42445226614 after visibility change: all seven jobs SUCCESS, including three-OS
.NET 8 correctness tests, Python policy, pinned external references and all three
performance/package/offline-consumer audits. The former billing blocker did not
prevent this new execution; no CI waiver or workflow alteration was needed.
This does not validate .NET 10/Framework or DNG implementation (none added).
Final documentation head still receives its own checks; immutable final receipt
is recorded on PR #14 after success. D0 contract approval remains pending.

## Explicit DNG D0 contract approval

Developer explicitly stated approval of DNG D0 on 2026-11-07. Recorded approval
of exact proposed signatures, InvalidDng addition, read-only lifetime/error model
and narrow profile in the contract; no code or existing public API changed.
Programme direction alone was not used as approval. E04 contract gate is complete,
but D1's section 5 normative ledger, owned-fixture/independent-oracle and E02/E03
prerequisites remain mandatory. D2/write/digest/legacy-runtime support not approved.
Do not ask the same D0 approval again unless promises change.

Predecessor documentation head 3236ad9 passed 42445322851, all seven jobs, after
public visibility change. This approval-record commit requires its own green CI
before PR #14 merge; final SHA/run/merge receipt will be posted there after success.
E01 full inventory remains open. Local policy/whitespace checks run; no DNG/net10/
Framework implementation or runtime verification claimed by this documentation.

## E02 execution plan after approved D0 documentation merge

Active branch arena/1af1ed4a-metadatasharp starts from PR #14 merge 716c00b.
D0 is approved but D1 remains gated; E02 can proceed independently of open E01.
Implement approved .NET 8/10 direction without production API/algorithm changes:
SDK 10 selection, net8.0/net10.0 library/test/tool assets, six OS/TFM correctness
jobs with explicit TRX framework identity, two required reference jobs, six audit
jobs, per-TFM package groups/XML/DLLs and isolated consumers, and cross-target
public-signature receipts. Keep C# 12 and strict package payload assertions.
No new runtime dependency, Framework target, DNG parser or API refactor. Framework
is deferred as not demonstrated cheap by the recorded API cost map; any actual
legacy spike remains separately bounded follow-up. E01 doc-claim ledger may advance
without being called a frozen/verified full denominator. Preserve all old evidence;
record new target-specific measurements separately. Local SDK absent: actual
runtime execution must come from push CI. Final head must pass before merge.

### E02 initial multi-target submission

Added net8.0/net10.0 library, tests and tool targets with SDK 10 feature-band
selection, retaining C# 12. CI now schedules six explicit OS/TFM test jobs, two
required reference jobs and six audit jobs. A new runtime-identity regression
checks actual host and loaded library TFMs; floor 449 per combination. Audits
require both DLL/XML asset sets and exact per-TFM dependencies, each consumer's
actual runtime, and identical reflected public member signatures on both runtimes.
No production parser/API changes. Signature equality is not full attribute/default/
behavior compatibility proof; correctness/reference tests remain separate.
Local Python policy 12/12, compile checks for audit/harness Python and diff checks
pass. Actual .NET 10 and dual-target package results await this head's own Actions.

Initial E02 run 42445858808 at a055140: all six package/runtime/signature audits,
both reference targets and all net8 tests passed; net10 tests failed three cases.
Two old infrastructure cases hardcoded net8 instead of the executing target. One
empty-zlib fixture relied on framework compressor initialization for zero-length
input (failure at strict RFC header/minimum-length gate). Make the authored empty
stream explicit and portable, with an extra regression rejecting zero/missing-
DEFLATE framing; do NOT relax production BoundedZlib. Correct existing target
expectations, retaining explicit runtime/loaded-asset checks. New floor 450.
No production parsing algorithm/API or safety contract changed.

Strengthened consumer independence: each net8/net10 package consumer now has its
own SDK-major global.json and commands execute from that consumer directory.
Check selected SDK major as well as host runtime. A net8 test under SDK 10 alone
would not prove a net8 SDK consumer can restore/use the dual-target package; both
consumer SDKs are installed by the matrix. Record consumer SDK in package receipt.

### E02 implementation gate and E01 seed submission

Audited code 2949589c1908b9da80289670b89c062d03bc5af0 passed 42446034373, all 14 jobs:
450 cases per six OS/TFM combinations, six audits and both required references.
Collected complete small JSON/receipt in docs/audits/evidence/e02. All consumers
selected actual matching SDKs 8.0.427 / 10.0.403. Public reflected signatures: 633
entries, identical hash across six receipts; not full metadata/behavior proof.
No production algorithm/API changes or parser relaxation. Runtime support policy
and Framework deferral recorded in E02 audit; historical M18 evidence untouched.

E01 advances with 186 visible documentation-cell seeds / 348 inferred candidate
operations and deterministic generator + six policy tests. No-claim cells not
excluded, writes not observed, layouts unsplit, fixture rights unreviewed and
MetadataSharp verification unassessed. Full E01 denominator/CLI/corpus gate remains
OPEN. New generator/policy/documents need final-head CI, not predecessor inference.
No DNG implementation, E03 approval or package publication. Final SHA/runs/merge
receipt posted on PR #15 after all checks pass.

Final consumer audit also asserts loaded Core/Formats TargetFrameworkAttribute,
not only SDK and process runtime: a net10 consumer must not silently pick net8
package assets. Added receipt discriminator offlineConsumerAssets=target-matched.
This strengthens the final-head smoke beyond the earlier 2949589 evidence; it
requires its own final run and does not retroactively change stored receipts.

## E01 pinned black-box execution plan (before implementation)

PR #15 is merged at 7c872466c705c7d6877c8e41018df217d4cbd8db; final 85bd097
passed PR42446407601 and push42446403212, 14/14 each. E02 bounded unit complete.
Current assigned branch is arena/49d49a4f-metadatasharp; old branch references are
historical. Continue E01, not D1 or public API refactor.

Bounded next unit: provision exact Exiv2 v0.28.9 commit as an external executable
in a separate required Ubuntu24.04 CI job; record explicit CMake feature switches,
compiler/dependencies, full verbose runtime version, binary/source/build identities.
Do not read/translate parser implementation, tests or tag dictionaries. No imported
fixtures or GPL code in production/package. Build source and outputs stay ignored.
Use existing independently authored MetadataSharp generator inputs for narrow JPEG
ASCII Artist read/insert/replace/delete observations. Inspect each CLI-written
result with a separately authored tiny fixture-specific byte reader (not Exiv2
pretty-print alone), also verify image scan/non-target metadata preservation.
Record atomic variant IDs and fixture/input/output hashes; valid results apply ONLY
to those exact cases, not whole format cells. Keep 186 seeds/denominator unmodified.

Add offline fail-closed policy tests and durable complete chunked JSON receipts;
wrong version/missing feature/source/failed command/expected result mismatch must
fail rather than skip. Existing required 0.27.6 references remain unchanged. No
new runtime dependency, API, publication, blanket coverage, exclusion or E01 closure.
Finish own full CI and merge only green. E03 remains exact proposal before refactor;
D0 approval retained, normative/oracle/E03 prerequisites still block D1.

Initial E01 harness implemented: explicit recognized typed CMake options PLUS
runtime feature checks (not accepting unused -D cache strings), exact version/pin,
external binary/cache/license/host dependencies, four fixture-specific operation
observations, independent ASCII raw bytes + all non-EXIF segments/scan preservation.
Full verbose version stays an ignored CI artifact; durable receipt filters build
facts so upstream namespace catalogs are not imported into this MIT repository.
Complete chunk collector rejects missing/duplicate/corrupt/mixed-source receipts.
Local Python: 12 historical policies + 21 discovery/observation/collector cases PASS;
no local SDK/CMake execution. Required fifteenth CI job must prove actual build and
observations; no successful execution asserted yet.

Initial E01 run42447844906 at6991c02: all fourteen existing gates PASS; pinned
external configure/build also PASS, but observation step FAILS. Downloading logs
from results-receiver returned EOF (known host restriction); annotations initially
only gave exit1. No cause assumed. Add bounded explicit failure annotation with
traceback/build facts before altering expectations; failed step remains required.

Diagnostic run42448019127 at862fad4 proved exact CLI/source/version and all typed
CMake features; failure was OUR assumed verbose key enable_xmp, not missing XMP.
Actual v0.28.9 reports have_xmptoolkit=1, have_libz=1, have_brotli=1, enable_bmff=1;
recorded cache separately proves PNG/XMP/Brotli switches ON. Match those measured
keys, still reject absent/disabled features; do not infer format support from a
library flag. Pin additional observed functional options explicitly (VIDEO/INIH/
LENSDATA ON, WEBREADY/CURL OFF, dynamic runtime ON; command ON/fuzz tests OFF) so
future source defaults cannot silently define this build. No upstream parser source
read, runtime capability downgraded, or failed observation relabeled as success.

Run42448200101/879cea0: source/version/cache and measured runtime feature checks
PASS, observed write fails STRICT non-EXIF byte comparison. Cause is not guessed or
called an upstream bug. Keep gate strict; add bounded first segment mismatch bytes
(authored fixture only) and operation context to diagnose. Existing fourteen gates
remain green. No production changes or normalization workaround authorized.
