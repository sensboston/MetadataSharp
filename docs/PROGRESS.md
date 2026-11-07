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
