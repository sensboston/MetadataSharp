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

371fd0a/run42448387840 locates INSERT difference: five non-EXIF segments remain;
index1 before is COM (fffe0020 + authored comment), after is DQT (ffdb0043...).
This proves an ordering change, not yet whole-payload equality or corruption.
Before the correction: distinguish exact order from a measured byte-identical
segment permutation. Baseline writes may record that explicit ordering divergence
ONLY if full segment MULTISETS (including multiplicity/content) match. Any payload
change/drop/duplication remains a failure; scan and all other TIFF fields remain
strict. Read/default verifier remains byte/order strict. MetadataSharp preservation
contract stays unchanged; this is baseline observation, not accepted write parity.

043f01e/run42448594200 proves a second, exact INSERT finding: original authored
COM fffe0020 + "MetadataSharp authored fixture" becomes fffe0021 + same bytes +
00, in addition to ordering. All other non-EXIF segment multisets match. This is
byte change, NOT exact preservation, and is not adopted as our writer policy.
Before correction: add narrowly named baseline DIFFERENCE D001 for EXACTLY that
single owned COM replacement, with strict matching of every other segment and its
multiplicity. No generic trailing-NUL normalization or arbitrary COM waiver. Emit
explicit false byte-preservation flags and visible finding ID in each affected row.
Default/read/MetadataSharp verifier stays strict; negative tests must still reject
that change by default and any other payload/loss/duplicate under observation mode.
Successful CI then means the versioned observations (including known differences)
match, NOT successful safe-writing parity. No percentage or exclusion accepted.

6cc9b96/run42448793704 advances through READ and INSERT (D001 explicit), then
REPLACE shows D002: owned single-property XMP APP1 is reserialized (wrapper BOM,
toolkit label, whitespace/padding/namespace syntax) as well as COM D001. No generic
XMP normalization waiver: before further implementation, define independent XML
check ONLY for this authored packet: one xmpmeta/RDF/Description, exactly one
CreatorTool=MetadataSharp simple text property (attribute or child syntax), no
other property/qualifier/structure/DTD. A known D002 may be RECORDED only if both
packets meet that literal expectation and all remaining segments match byte-for-
byte with multiplicity, scan/IFD0 intact. Root toolkit annotation is separately
visible serialization metadata, not claimed byte preservation. Explicitly false
byte flags and both finding IDs; no successful writing parity or production policy
change. Unknown structures/values/additions/drops still fail the observation gate.

2ed0025/run42449006766: READ/INSERT/REPLACE pass independent checks, D001/D002
explicitly recorded; REMOVE passes raw value/IFD0/scan/known-difference checks but
our absent-key filtered print query returns nonzero. Do NOT waive exit failures.
Use documented full EXIF inventory print (-P Ekycv) for removal and independently
require exactly Orientation Short 1 1, no Artist/extra field. All invoked commands
still must return zero; no skipped remove or arbitrary success-exit set. Narrow XML
reader explicitly accepts UTF-8 only, rejects NUL/DTD outside this authored variant.

### First complete E01 pinned observation protocol gate

601a20b0c1e6d6e1a78b489269f687445ca7827b/run42449222307: all 15 jobs SUCCESS.
Retrieved/validated complete source-bound report into research/evidence/e01-pinned-jpeg.
READ exact; INSERT D001 with nonExifBytesAndOrderExact=false/payloadBytesExact=false;
REPLACE/REMOVE D001+D002, same false flags. Independent expected Artist bytes or
absence/remaining orientation inventory, all other TIFF fields and scan checked.
This is a successful bounded observation protocol INCLUDING known differences,
NOT successful safe-write parity, E01 census closure, exclusion or numerator claim.
All original fourteen gates retained/pass, 450 .NET cases per six combos; Python
12 historical +26 discovery cases. No production code/API/hostile-input relaxation.

Full scope/build/license/output-rights caveats, optional feature-build gaps, failures,
classification logic and reproduction are in EXIV2_PINNED_BASELINE_EXECUTION.md.
Final PR #16 documentation/evidence head must pass its OWN fifteen jobs before
merge. Exact final immutable SHA/run/merge receipt posted there. Continue atomic
E01 breadth and E03 exact proposal next; D1 stays blocked, D0 approval not reopened.

### PR #16 merge and post-merge recovery checkpoint — 2026-11-08

- PR #16 merged 2026-11-08T05:10:04Z as merge commit
  75ad10dcf9f96a496be8192c6f8949632c6da1ba; FINAL head
  5b0783bf2a60844e82ab2a61479816d01b15d4b9 passed both required final fifteen-job
  runs (PR42449478931 and push42449474066, 15/15 each, verified via gh before merge).
  Final immutable source/run/merge receipts are posted in PR #16 comments.
- New assigned branch arena/c321dc16-metadatasharp is cut directly from that merge
  commit; no new implementation is part of this checkpoint. Branch references in
  CURRENT_STATE.md were updated; PROGRESS history entries were left as written.
- Bounded E01 pinned black-box CLI foundation is COMPLETE: exact external Exiv2
  0.28.9 source b0859fc61fea5dedfc898aece6b15893ddf4314e, recognized explicit CMake
  features/runtime/dependency identities, four owned JPEG ASCII Artist
  read/insert/replace/remove observations with independent checks, strict default
  verifier and narrow known differences D001 (COM trailing NUL/order) and D002
  (single-property XMP reserialization) explicitly recorded, never normalized away.
  Those flags remain FALSE for all three writes; this is NOT safe-write parity.
- NOT completed and not to be inferred: full E01 census/denominator/percentage,
  E03 approval or any refactor, D1 (normative/oracle/E03 gates), DNG implementation,
  package publication, Framework 4.8.x, or any production DLL/API/parser change.
- 186 seed cells / 348 candidate operations remain unchanged and NOT a frozen
  denominator. Non-JPEG families/formats/variants, video/CLI/tag/encoding/layout
  breadth and fixture rights review remain OPEN.
- Next open units (unstarted): (1) expand the atomic E01 ledger by
  family/format/variant; (2) separately prepare the exact E03 .NET
  architecture/compatibility contracts proposal, which requires explicit developer
  approval before any refactor. D0 approval retained; D1 prerequisites unchanged.
- Validation for this checkpoint: `git status --short`, `git branch --show-current`,
  `gh pr view 16`, link/fence re-check of edited Markdown and `git diff --check`.
  No .NET execution was run locally (no local SDK); no production code changed.

### 2026-11-08 directive, parity master plan and E03 proposal — 2026-11-08

- Developer directive (2026-11-08): bring MetadataSharp functionality up to
  Exiv2 user-visible capability; avoid external dependencies where possible.
  Tasks may be split into subtasks. This authorizes direction, not unseen
  public API approval, exclusions, Framework support or publication.
- Recorded the binding dependency policy (zero third-party runtime deps;
  BCL-only; no analyzers/natives/embedded executables; CI-only external oracles)
  in DEVELOPMENT.md section 7. Measured at proposal time: Core/Formats declare
  no PackageReference; only the test project has (private xUnit assets).
- NEW design/EXIV2_PARITY_MASTER_PLAN.md: honest gap matrix (current scope:
  JPEG/PNG/WebP/TIFF restricted EXIF/IPTC/XMP + sidecars), the E06–E18 split
  into gated subtasks E01-CEN..E18 with dependencies/approvals, required order
  (E03 → E01-CEN → implementation batches → E17 → E18) and an explicit
  definition of done for the parity goal. States clearly: parity is dozens of
  gated units; no percentage exists until the E01 denominator is frozen and
  reviewed.
- NEW design/DOTNET_ARCHITECTURE_AND_COMPATIBILITY_CONTRACT_PROPOSAL.md (E03):
  exact additive surface (`MetadataContainer` 31 baseline rows incl. XmpSidecar,
  `MetadataFamily`, `MetadataOperation`, `CapabilityStatus` fail-closed,
  `CapabilityDescriptor`, static `MetadataCapabilities` with All/TryGet/GetStatus),
  compatibility/migration/TFM/versioning/surface-audit policy, zero-dependency
  reaffirmation, verification plan (incl. test-local PackageReference guard),
  explicit non-goals (no approved-contract changes, no new per-format APIs, no
  InvalidDng duplication, no package publication). PROPOSAL ONLY — nothing
  implemented; explicit developer approval required; open questions listed in
  section 9 of that document.
- No production source, test, CI, audit or dependency changed. Branch
  arena/7a35ad89-metadatasharp cut from master 79992da. Documentation checks
  (links/fences/git diff --check) re-run for edited files.

### E03 architecture/compatibility contracts — approved 2026-11-08 — implemented

- Developer explicitly approved the E03 proposal IN FULL (2026-11-08): the section 4
  additive surface, sections 3/5/7 policies, and the verification plan; the developer
  may adjust later. net8.0 + net10.0 are retained despite net8 end of servicing
  (2026-11-10); .NET Framework remains deferred; no publication authorization.
- Implemented on arena/d4158be1-metadatasharp:
  - NEW src/MetadataSharp.Core/MetadataCapabilities.cs — exact approved surface:
    MetadataContainer (Unknown=0 + 31 documented baseline rows, XMP row as
    XmpSidecar), MetadataFamily (Exif=0, IptcIim, Xmp, Icc, Comment, Thumbnail,
    Preview), MetadataOperation (Read=0, Insert, Replace, Remove, Extract),
    CapabilityStatus (NotSupported=0, Partial, Supported), readonly record struct
    CapabilityDescriptor, static MetadataCapabilities with stable sorted immutable
    All (IReadOnlyList), fail-closed TryGet/GetStatus (missing row or undefined
    enum values => false/NotSupported, never throw).
  - Manifest mirrors shipped, test-evidenced capability only: JPEG/PNG/WebP/sidecar
    EXIF/IPTC/XMP rows (Read Supported; Insert/Replace/Remove Partial), TIFF EXIF
    Read/Insert/Replace Partial (ten descriptive IFD0 tags, append-preserve, old
    bytes retained); XmpSidecar XMP rows; everything else NotSupported (ICC/comment/
    thumbnail/preview rows have no public API and are therefore absent, not faked).
    Notes are compile-time constants, bounded (<=240 chars) and carry no control
    characters; manifest is strictly ordered and validated at static init.
  - NEW tests/MetadataSharp.Tests/Capabilities/MetadataCapabilitiesTests.cs —
    independently authored expectation table (35 rows incl. exact notes), ordering,
    fail-closed and domain-coverage tests.
  - ProjectInfrastructureTests gains SourceProjectsDeclareNoPackageReferences
    (test-local guard; implementation source adds ZERO new dependencies).
- NO approved contract, limit, refusal, writer, parser, dependency or TFM changed.
  Reflection-signature delta is exactly the approved additions; E02 receipts remain
  immutable historical evidence. Capability contents are data pinned by tests and
  grow only with reviewed units.
- Local dotnet unavailable; actual build/test evidence is Actions only. Receipt
  (runs/jobs) is posted on the implementing PR; merge only after final head green.
- NOT authorized by this approval: D1 start (needs explicit go-ahead + normative/
  fixture/oracle prereqs), any E06–E18 format/family unit, exclusions/percentages,
  package publication, Framework work.

### E03 merge receipt — 2026-11-08

- PR #19 MERGED 2026-11-08T17:34:49Z as merge commit
  ea8e0567c92cbd4691faa7c1a027764e3daccc3b. Final head bb473f0bc8d4bcfd3e725eaea32
  00ee2868a053 passed both required fifteen-job runs (push 42505415670, pull_request
  42505425014; 15/15 each, incl. pinned external Exiv2 0.28.9 job, six audits and both
  historical references). Superseded push run a835fa0 was cancelled by the concurrency
  policy when the branch advanced. Receipt posted on PR #19.
- E03 gate discharged. Standing non-goals remain binding: no D1 start (explicit
  go-ahead + normative/fixture/oracle prerequisites still required), no E06–E18
  format work, no exclusions/percentages, no publication, Framework deferred.

### D1 narrow read-only DNG inventory profile — 2026-11-08 (PR #21, MERGED at 0d4a516)

- 2026-11-08: developer directed D1 first, then E01-CEN immediately after the
  D1 PR merges (no fresh confirmation required); documentation of current state
  requested; already-completed stages may be consolidated.
- D1 implemented on arena/cdb43c96-metadatasharp (cut from master 8fe9886, the
  PR #19/#20 merge head):
  - NEW src/MetadataSharp.Formats/Dng/* — dedicated internal reader/models/options
    (DngReader, DngLayout, DngModels, DngReadOptions). TIFF's DNG refusal
    (50706 ⇒ UnsupportedFeature in TiffLayoutReader) is UNCHANGED; only neutral
    Core/Tiff primitives are shared. Profile per D0 §3: classic II/MM magic 42,
    DNGVersion exactly 1.1.0.0 at root, one 16-bit CFA raw IFD, optional RGB
    thumbnail root + single LONG/1 SubIFD 330 raw leaf, uncompressed strips with
    checked `width*rows*bytesPerSample` accounting, whitelist-only fields,
    D0 §4 logical charge model, owned immutable snapshot, IsSupported iff the
    profile holds and Issues is empty.
  - `MetadataErrorCode.InvalidDng` appended; existing numeric values unchanged.
    DNG identity type/count violations throw InvalidDng; absent root DNGVersion
    is UnsupportedFeature; framing is InvalidTiff; bounds are LimitExceeded.
  - Gates (D0 §5): gate 1 closed by docs/design/DNG_D1_NORMATIVE_LEDGER.md
    (field/count/role/constraint ledger incl. root-vs-raw-leaf 50706/50707/
    50708/50721/50728/50778 mirror rules, CFA semantics, error mapping, known
    gaps); gate 2 closed by docs/design/DNG_D1_FIXTURES_AND_ORACLES.md (four
    authored fixtures × II/MM, two independent writers compared byte-for-byte in
    CI, offline Python policy suite, pinned-ExifTool 12.76 inventory assertions,
    explicit blocker-class raw-decode oracle gap — accepted, never a silent
    skip); gate 3: tests/MetadataSharp.Tests/Dng/* (52 tests; both shapes ×
    II/MM; mutation boundaries; known-byte SHA-256; limits; cancellation;
    short async reads; snapshot survival); gate 4 (E02/E03) already available.
  - tools/MetadataSharp.ReferenceFixture gains `inspect-dng`; harness.py builds
    the owned fixtures with both writers and requires byte equality; on any
    mismatch the external-reference job fails.
  - Fixture authoring error caught and fixed before merge: DefaultCropSize is
    now full-frame [4,2] (was [2,4], inconsistent with the 4×2 raw grid);
    committed hashes re-pinned (tests/fixtures/dng/*).
  - TIFF's refusal stays; no D2, no decode/render, no parity percentage,
    no Framework, no publication. D0 is a binding narrow contract; the ledger is
    its binding refinement — public/subset changes require the D0 reapproval path.
- CI hardening landed during review, all on 2026-11-08 before merge:
  - `DngLayout.Ranges` now accounts Strip ranges (they were added during
    validation, after the immutable snapshot of the range list was taken); the
    global sorted overlap check now covers strips/fields/directories.
  - Thumbnail fixture bug: tag 277 SamplesPerPixel was written SHORT/3 [3,3,3]
    in BOTH writers; the normative ledger (§4/§5) and DNG/TIFF require SHORT/1.
    Both writers now emit SHORT/1 [3]; thumbnail fixtures re-pinned — 1034 B,
    IFD0 strip at 420, raw leaf IFD at 444, raw strip at 1018; SHA-256
    dng-thumb-ii `3208c70940419fbd5655d9ed7643702626f4ada7c22d2dcb8346b738c0f11b96`,
    dng-thumb-mm `5d32ea1c44c204fcd3040c1a12204d64dfe84fcb83412a0bd4dcfa9f577be5e9`.
    Raw fixtures unchanged (598 B, `474952a4…` / `64dee070…`).
  - DNG identity tags 50706/50707 with wrong type/count now throw `InvalidDng`
    before any framing/bounds interpretation (ledger: malformed DNG identity
    fields → InvalidDng), so inline-vs-external reinterpretation can no longer
    mask the DNG-specific error.
  - Root DNGVersion must equal exactly `01 01 00 00`; anything else safely
    framed reports an `UnsupportedVersion` issue (`IsSupported=false`) with
    geometry still established (ledger §1); leaf mirrors obey the same rule.
  - Next-IFD links are inventoried and reported (`UnsupportedDirectoryRole`)
    but no longer feed the structural cycle graph, so a self-referencing
    next-IFD chain reports the profile issue instead of throwing InvalidTiff.
  - UniqueCameraModel test NUL-overwrite indexed the payload correctly
    (payload is 19 bytes; overwrite offset 18, not 19).
  - Reference harness: exiftool `-n` DNGVersion compared by digit sequence
    (`1100`); TIFF tag 257 is `ImageHeight` in exiftool (not `ImageLength`);
    exiftool surfaces the DNG raw-SubIFD image tags at top level, so the
    thumbnail fixture's exiftool expectations were aligned (geometry/role are
    asserted by the structural oracle and `inspect-dng` instead).
  - `.NET` job had TreatWarningsAsErrors-driven compile failures fixed earlier
    (duplicate local, null-assert) — no behavioral code involved.
- Final head `7d5fc2d`: all fifteen jobs green in
  [run 42511505561](https://github.com/sensboston/MetadataSharp/actions/runs/42511505561)
  (net8.0/net10.0 × ubuntu/windows/macos, six audits, both References, pinned
  Exiv2 0.28.9 observations). **Merged into master as `0d4a516`
  (2026-11-08T18:46:52Z).** No parity percentage; D2/decode/render/write and
  publication remain unauthorized.

### E01-CEN finite atomic denominator design — 2026-11-08 (PROPOSAL, not approved)

- 2026-11-08: after D1 merged, developer-pre-authorized next unit E01-CEN was
  drafted as docs/design/EXIV2_E01_CENSUS_AND_DENOMINATOR_PROPOSAL.md. It defines:
  the census row schema (schemaVersion 1; EX0289-pinned row IDs; operations
  inventory/read/insert/replace/remove/extract; axes valueType/textEncoding/layout/
  duplicates/sidecar/presence, all bound to a reviewed closed `enumerations` block;
  invariants incl. no self-oracles and refusals-never-support), the fixture
  provenance workflow (authored generators, pinned SHA-256 manifests, mutation
  recipes instead of file copies, rights classes, blocked-rights handling, in-repo
  budget), the pinned-CLI observation machinery (CI-only black box, ExifTool 12.76
  secondary differential reference), the proposed per-format batching B0–B14 of the
  31×6 grid (pilot JPEG first; evidence-rich B0–B6; rights/build-gated B7–B11;
  cross-cutting applicability B12–B14), and the TWO denominators (D_full = every
  reviewed row incl. no-claim/blocked/refused; D_accept = D_full minus signed
  developer exclusions; neither is computed yet; reporting rule defined). It also
  records 71 no-claim + 1 empty cell (EPS comments) as in-denominator candidates and
  9 open review questions for the developer.
- Status: DESIGN REVIEWED AND APPROVED by the developer on 2026-11-08 (six §7
  decisions recorded in the proposal: freeze §6 definitions/reporting as proposed;
  no-claim rows included in D_full with mandatory pinned-CLI observation before any
  freeze; external corpora limited to verified CC0/public-domain with recorded
  license/hashes; BMFF/video census-first + per-format rights review before fixtures;
  sidecar rows presence-only (writes stay E15); ExifTool 12.76 retained as secondary
  differential reference; B13 full workflow census after E07 core read coverage; census
  home docs/research/EXIV2_CENSUS.json + PR-per-batch; B14 kept last). Changes land by
  PR; exclusions NOT granted; no parity percentage exists; E01 gate stays open.

### B0 pilot census generation — 2026-11-08 (JPEG × {exif, comments, icc} + inventory)

- 2026-11-08: design approval recorded; B0 enumeration generated and pinned.
  - NEW tools/reference/build_census.py (generator + --check drift guard) and
    docs/research/EXIV2_CENSUS.json (schemaVersion 1, generatedBy the tool).
    135 rows, all `not-run`/`not-assessed` (baselineObservation/verification), no
    fixtures authored yet, `denominatorFrozen: false`. Enumeration: JPEG EXIF 36 rows
    (4 ops × 3 layouts × 3 duplication states, valueType `any-documented` — B14 splits
    the vocabulary), JPEG comments 78 rows (5 ops; encodings ascii/utf-8/unknown-raw;
    extract fixed to opaque/not-applicable), JPEG ICC 20 rows (5 ops × single/segmented
    APP2 × none/first-wins, opaque bytes), 1 JPEG inventory-track row. 183 documented
    cells remain pending in coverage.pendingSeedIds (incl. JPEG xmp/iptc/thumbnail —
    those are B1). Enumerations block marked reviewed 2026-11-08; axis values closed.
  - NEW tests/discovery/test_census.py — 10 policy tests (determinism, ID format,
    closed-enumeration conformance, extract-only-on-opaque families, pilot honesty
    (all not-run), grid partition seeds↔pending, no-claim absence from pilot, no
    MetadataSharp-as-oracle). CI `Verify discovery seed policy` step now runs
    `python tools/reference/build_census.py --check` alongside the seeds check.
  - Review focus for the next PR: schema ergonomics at scale (B1 will triple the
    corpus), the `inventory` family marker, and whether `any-documented` survives
    B14 or must split now. NO observation has been run yet — zero rows carry evidence.
- No exclusions, no freeze, no percentage; D_full/D_accept are computed only after
  the observation batch and developer review. E01 gate remains OPEN.
### B0 pinned-CLI observation batch — 2026-11-08 (JPEG rows observed)

- 2026-11-08: B0 observation batch executed end-to-end (step 1 of the approved
  1→2→3 order: B0 observations, then B1 JPEG xmp/iptc/thumbnail enumeration,
  then denominators + developer sign-off). No exclusions granted; no freeze; no
  percentage; D2/publication not authorized.
  - NEW tools/reference/jpeg_owned_fixtures.py — deterministic owned synthetic
    JPEG writer (14 fixtures): EXIF IFD0/SubIFD/multi-APP1 with duplicate-tag
    and duplicate-block axes; COM single/multiple/duplicate; minimal synthetic
    ICC profiles (single-segment, duplicate, two-segment); 8x8 single-scan
    baseline body; ICC fragments reassembled exactly. Pinned by SHA-256 in
    tests/fixtures/jpeg/manifest.json + provenance.json (binaries never
    committed; CI regenerates and byte-compares).
  - NEW tools/reference/observe_b0.py — runs the pinned 0.28.9 CLI per applicable
    census row (fixtures regenerated and hash-checked first; pin/upstream
    verified; scan-bytes invariant; outcomes recorded, never gated). CI
    `pinned-exiv2-baseline` job re-runs it and uploads `e01-b0-census-observations`.
  - NEW docs/research/evidence/e01-b0-census/ — committed observations.json +
    README (provenance, reproduction, limits). 97 rows observed-cli (89 accepted,
    8 refused — all ICC duplicate-profile rows: `Not a valid ICC Profile` on
    insert/replace/remove/extract; `-pC` still succeeds), 38 statically
    not-applicable (axis collapses: app1-exif-multiple is itself the
    duplicate-block case; com-single cannot carry duplication). Scan preserved
    in 135/135 rows. Comments store argv bytes + trailing NUL (D001-consistent,
    recorded verbatim); `-e c` emits an exiv2 raw .exv container; in-IFD duplicate
    tags print fully; a second EXIF APP1 block is ignored on print/mutation context.
  - docs/research/EXIV2_CENSUS.json regenerated: all B0 rows observed-cli or
    not-applicable (no not-run remains); fixtures pinned per row; oracles split
    spec-derived vs pinned-cli (record-only rows). Discovery suite: 47/47 local.
  - Verification stays not-assessed everywhere (E07); no denominator computed;
    the exclusions conversation (if any) goes to the developer at step 3.
- E01 gate remains OPEN.
### B1 pinned-CLI observation batch — 2026-11-08 (JPEG × {xmp, iptc, thumbnail})

- 2026-11-08: B1 batch executed end-to-end (step 2 of the approved 1→2→3 order;
  B0 done 2026-11-08, denominators remain step 3). No exclusions granted; no
  freeze; no percentage; D2/publication not authorized.
  - tools/reference/jpeg_owned_fixtures.py extended to 31 fixtures (+17): XMP
    standard/extended (base + 2 extension chunks)/duplicate/non-UTF-8 payload
    variants incl. Extended-XMP duplicate and Extended non-UTF-8 fixtures;
    IPTC Photoshop 8BIM 0x0404 with repeatable 2:25 keywords (single block,
    repeated dataset, two APP13 segments unique and shared); EXIF IFD1
    embedded thumbnails (JPEG-compressed, uncompressed strip) via a proper
    IFD0→IFD1 next-IFD chain with inline-patched strip blob offsets; non-UTF-8
    raw COM fixtures (`com-raw*.jpg`) for the B0 unknown-raw comment READ refresh.
    Pinned manifest/provenance regenerated (14→31 fixtures, byte-deterministic).
  - tools/reference/build_census.py: B1 rows for XMP (4 ops × 2 layouts ×
    duplicates {none, first-wins} × encodings {utf-8, unknown-raw}; the pure
    ASCII cell is byte-identical to utf-8 and omitted), IPTC (4 ops × 8BIM
    layout × {none, first-wins}) and thumbnail (5 ops × JPEG/uncompressed
    layouts; insert on the uncompressed layout is statically
    not-applicable — the pinned CLI exposes no uncompressed-thumbnail write
    path; flagged for developer review at denominators). Evidence now applies
    per batch with per-row batch-prefixed notes; `denominatorFrozen` stays false.
  - tools/reference/observe_b0.py: `--batch {b0|b1|all}` (default all), per-batch
    output `artifacts/e01/<batch>/observations.json`; XMP/IPTC/thumbnail runners
    added (IPTC IIM 2:25 parser and EXIF IFD1 thumbnail tag reader included);
    honest new outcome class `nonzero-exit-artifact-produced` for the CLI's
    nonzero-exit-but-successful thumbnail extract (rc 137/46) instead of
    mislabeling it refused or accepted.
  - docs/research/evidence/e01-b1-census/ — committed observations.json + README.
    57 rows observed-cli: 55 accepted, 2 nonzero-exit-artifact-produced
    (thumbnail extract; the JPEG-compressed extract is byte-exact against the
    authored embedded blob — sha256 verified at observation time; the
    uncompressed extract emits CLI-defined TIFF, record-only). 1 static
    not-applicable (thumbnail insert on the uncompressed layout). Scan preserved
    in all 58 rows. Notable CLI behaviors recorded, not gated: second
    APP13/IPTC block is ignored on print and mutation; repeated 2:25 datasets
    survive `set` (first replaced, remainder kept); EXIF IFD1 thumbnail tags
    print without decoding; Extended XMP prints `HasExtendedXMP` only; non-UTF-8
    XMP fails decode rc=0 with a warning; duplicate APP1 XMP first-wins;
    `del Xmp.xmp.CreatorTool` on an Extended-XMP file is a no-op for the
    extension chunks and leaves `HasExtendedXMP` intact.
  - B0 observations refreshed honestly in the same turn: the four unknown-raw
    comment READ rows now observe the non-UTF-8 COM fixtures; B0 outcome counts
    unchanged (89/8/38); scan preserved 135/135.
  - docs/research/EXIV2_CENSUS.json regenerated: 193 rows (B0 135 + B1 58),
    all observed-cli or not-applicable; fixtures pinned per row; oracles split
    spec-derived vs pinned-cli. Discovery suite: 53/53 local.
  - Verification stays not-assessed everywhere (E07); no denominator computed;
    the exclusions conversation (if any) goes to the developer at step 3
    (denominators + sign-off). E01 gate remains OPEN.
### E01 denominators — pre-freeze review package opened — 2026-11-08

- 2026-11-08: step 3 of the approved order (B0 → B1 → denominators) opened as a
  REVIEW PACKAGE, not a freeze: docs/design/EXIV2_E01_DENOMINATORS_PREFREEZE_REVIEW.md.
  It records the post-B1 state (193 rows: 154 observed-cli / 39 static
  not-applicable; 144 accepted / 8 refused (all ICC duplicate-profile rows) /
  2 nonzero-exit-artifact-produced / 0 verified), restates the approved §6
  denominator definitions and lists what the developer must decide (freeze timing
  Q1; exclusion candidates E-CAND-1 thumbnail-uncompressed-insert NA and the
  reserved BMFF/noDocumentedClaim cells Q2; nonzero-exit-artifact-produced reporting
  Q3; remaining batch order Q4; pre-freeze reporting rule Q5). NO freeze, NO
  percentage, NO exclusion signed, verification stays not-assessed, D2/publication
  NOT authorized, `denominatorFrozen: false`.
- E01 gate remains OPEN until the developer reviews the frozen denominators.
### E01 B2 census batch — PNG + inventory — 2026-11-08

- 2026-11-08: B2 executed in the approved §5 order. Owned synthetic PNG fixtures
  authored by `tools/reference/png_owned_fixtures.py` (stdlib byte-level writer, CRC-32
  per chunk, 8x8 8-bit truecolor baseline; eXIf chunks carry classic II TIFF payloads
  without the JPEG APP1 prefix; iCCP carries zlib-compressed profiles; IPTC rides the
  CLI's own hex-encoded IIM/8BIM "Raw profile type iptc" raw-profile carrier in both
  zTXt (CLI-native) and uncompressed iTXt carriers; XMP in uncompressed iTXt and
  zTXt-compressed carriers; 17 fixtures, SHA-256 pinned in
  tests/fixtures/png/manifest.json, regenerated byte-deterministically in CI).
- tools/reference/observe_b2.py: B2 observer mirroring the B0/B1 design (pinned
  source identity + CMAKE policy checks, fixture regeneration + manifest byte-compare,
  per-row recording, `nonzero-exit-artifact-produced` class, never gated on outcome).
  PNG analog of the scan invariant: **IDAT payload stream preserved in 66/66 observed
  rows**, plus CRC-32 verification of every chunk on the structural parse of mutated
  files. 66 rows observed, 1 static not-applicable (`exif-ifd1-uncompressed` thumbnail
  INSERT — the pinned CLI embeds JPEG thumbnails only; the same CLI limitation as the
  JPEG analog; candidate for denominators, never self-granted). Outcome counts:
  66 accepted. No row refused/unparseable/scan-changed.
  Notable CLI behaviors recorded, not gated: `-pc` prints the Description text;
  `-pa` on eXIf prints EXIF tags; the uncompressed-iTXt raw-profile carrier is
  readable and on write the CLI normalizes carriers to a single zTXt raw profile
  (datasets merged, hex re-wrapped); `del` removes the carrier entirely; duplicated
  iCCP chunks read first-wins (record-only); XMP writes normalize to iTXt;
  second XMP chunk silently ignored (first-wins) on print and mutation; unknown-raw
  XMP prints no tags with rc=0; `-i t` from a thumbnail-free eXIf base embeds a JPEG
  thumbnail (spec-derived, verified); replace on an uncompressed-strip IFD1 yields a
  CLI-defined mixed state (strip + JPEG tags), as on JPEG.
- docs/research/evidence/e01-b2-census/ — committed observations.json + README.
  Census regenerated: 260 rows (B0 135 + B1 58 + B2 67); every B2 row
  observed-cli (66) or statically not-applicable (1); `denominatorFrozen` stays
  false; verification stays not-assessed everywhere; no percentage computed;
  D2/publication NOT authorized; E01 gate remains OPEN. Next batch: B3 (WebP).
- Discovery suite: 65/65 local (13 census + 11 new PNG fixture tests included).
### E01 B2 census batch — PR #29 merged — 2026-11-08

- 2026-11-08: PR #29 merged (`ba52ac1e8322615fd06e3fbea163e14a3c88943c`, run
  42558351437, 15/15 green). The `pinned-exiv2-baseline` job rebuilt the pinned
  CLI, regenerated and byte-compared the 17 owned PNG fixtures against
  tests/fixtures/png/manifest.json, and re-ran B0+B1+B2; artifacts uploaded
  (`e01-b2-census-observations` id 13648024215, digest sha256:ccab5875…,
  `e01-b0/b1-census-observations` refreshed). Receipt committed as
  docs/research/evidence/e01-b2-census/receipt.json (metadata-only digests;
  artifact bytes not retrievable from the sandbox — flagged, not waived).
  Census stays 260 rows; `denominatorFrozen: false`; verification not-assessed;
  no percentage; D2/publication not authorized; E01 gate OPEN. Next: B3 (WebP).
### E01 denominators — developer answers recorded — 2026-11-08

- 2026-11-08: the developer answered the pre-freeze review package five questions
  (PR #27) with binding effect, recorded as proposal §9: Q1=(a) single final freeze
  after B2–B14 (B14 included); Q2 = thumbnail-uncompressed-insert row stays
  not-applicable, NO exclusions signed before freeze, noDocumentedClaim cells need an
  observation attempt or stay `blocked`; Q3 = nonzero-exit-artifact-produced is its own
  reporting class, never a pass; Q4 = master-plan §5 numeric order B2→B14, all in this
  cycle, freeze only after B14; Q5 = no percentages printed before freeze. No percentage,
  no freeze, no exclusion, verification stays not-assessed, D2/publication NOT
  authorized. E01 gate remains OPEN. Next: B2 (PNG + inventory).
### E01 B3 census batch — WebP + inventory — 2026-11-09

- 2026-11-09: B3 executed in the approved §5 numeric order. Owned synthetic WebP fixtures
  authored by `tools/reference/webp_owned_fixtures.py` (stdlib byte-level RIFF writer;
  hand-authored fake VP8 keyframe header as the coded-image analog — not a decodable
  frame; metadata ops never decode it and the pinned CLI rewrites it byte-for-byte;
  VP8X flags/8x8 canvas; EXIF chunks carry classic II TIFF payloads without APP1 prefix;
  ICCP carries raw minimal synthetic RGB profiles; XMP rides the raw 'XMP ' chunk; IPTC
  ('IPTC' chunk, IIM/8BIM payload) and comments ('COM ' chunk) are undocumented carriers
  the CLI does not surface; 12 fixtures, SHA-256 pinned in
  tests/fixtures/webp/census-manifest.json (byte-deterministic regeneration in CI)).
  IMPORTANT: tests/fixtures/webp/provenance.json belongs to the .NET library's
  PNG/WebP-group audit (Pillow-encoded single-pixel fixtures) and was left untouched;
  this batch pins its own census-manifest.json / census-provenance.json pair.
- tools/reference/observe_b3.py: B3 observer mirroring the B0/B1/B2 design (pinned
  source identity + CMAKE policy checks, fixture regeneration + census-manifest byte-compare,
  per-row recording, `nonzero-exit-artifact-produced` class, never gated on outcome).
  WebP analog of the scan invariant: **VP8/VP8L bitstream chunk payload stream preserved
  in 42/42 observed rows** (recorded as `scanPreserved`, the key the census reads), plus
  a strict RIFF/WebP structural parse (RIFF size, chunk tree, even-size padding) of every
  mutated file. 42 rows observed; 1 static not-applicable
  (`exif-ifd1-uncompressed` thumbnail INSERT — the pinned CLI embeds JPEG thumbnails only;
  the same CLI limitation as the JPEG/PNG analogs; candidate for denominators, never
  self-granted). Outcome counts: 41 accepted, 1 accepted-unexpected, 1 static-not-applicable.
  Notable pinned-CLI behaviors recorded, not gated:
  `EX0289-WEBP-XMP-REMOVE-0001` (`accepted-unexpected`): `-M "del Xmp.xmp.CreatorTool"`
  exits rc=0 but NEVER rewrites the raw XMP chunk (PNG B2 analog on the iTXt carrier:
  accepted, property removed; same authored packet bytes). XMP insert of unknown-raw
  bytes from-nothing: rc=0, stderr Invalid-UTF-8 error, no chunk written (record-only).
  Duplicate XMP chunks: `set` collapses to a single chunk (last-wins on print); duplicate
  EXIF chunks: `set` applies to the FIRST chunk only, while `del` removes from BOTH
  (record-only). `-iC -l <dir>` reads `<dir>/<target-stem>.icc`; `-d C` removes ICCP;
  `-pC` dumps raw profile bytes; `-pa` prints both EXIF chunks vs last XMP packet;
  unknown-raw XMP prints no tags with rc=0; `-i t` from a thumbnail-free EXIF base embeds
  a JPEG thumbnail (spec-derived, verified); replace on an uncompressed-strip IFD1 yields
  the CLI-defined mixed state. Both noDocumentedClaim rows (comments/IPTC) observe rc=0
  with NOTHING surfaced; `-M add Iptc.Application2.Keywords` rc=0 yet silently dropped
  without an EXIF chunk (datasets land in the EXIF TIFF when one exists). Nothing
  asserted (cells carry no documented claim); unverified carriers recorded, never claimed.
- docs/research/evidence/e01-b3-census/ — committed observations.json + README.
  Census regenerated: 303 rows (B0 135 + B1 58 + B2 67 + B3 43); every B3 row
  observed-cli (42) or statically not-applicable (1); `denominatorFrozen` stays false;
  verification stays not-assessed everywhere; no percentage computed;
  D2/publication NOT authorized; E01 gate remains OPEN. Next batch: B4 (TIFF + inventory).
- Discovery suite: 79/79 local (13 census + 13 new WebP fixture tests included;
  includes a guard that the .NET audit's tests/fixtures/webp/provenance.json is untouched).
- CI: `pinned-exiv2-baseline` job re-runs all batches (observe_b0.py --batch all,
  observe_b2.py, observe_b3.py) and uploads `e01-b3-census-observations` next to the
  B0/B1/B2 artifacts; the workflow was extended accordingly.
### E01 B3 census batch — PR #31 merged — 2026-11-09

- 2026-11-09: PR #31 merged (`f119a45281542de1d52d207eb8099e6443ca20ce`, run
  42661263750, 15/15 green). The `pinned-exiv2-baseline` job rebuilt the pinned
  CLI, regenerated and byte-compared the 12 owned WebP fixtures against
  tests/fixtures/webp/census-manifest.json, and re-ran B0+B1+B2+B3; artifacts uploaded
  (`e01-b3-census-observations` id 13695650344, digest sha256:992a5af7…,
  `e01-b0/b1/b2-census-observations` refreshed). Receipt committed as
  docs/research/evidence/e01-b3-census/receipt.json (metadata-only digests;
  artifact bytes not retrievable from the sandbox — flagged, not waived).
  Census stays 303 rows; `denominatorFrozen: false`; verification not-assessed;
  no percentage; D2/publication not authorized; E01 gate OPEN. Next: B4 (TIFF + inventory).
### E01 B4 census batch — TIFF + inventory — 2026-11-09

- 2026-11-09: B4 executed in the approved §5 numeric order. Owned synthetic classic
  II/42 TIFF fixtures authored by `tools/reference/tiff_owned_fixtures.py` (stdlib
  byte-level TIFF writer on the shared primitives of tools/reference/jpeg_owned_fixtures.py;
  14 fixtures, SHA-256 pinned in tests/fixtures/tiff/manifest.json, regenerated
  byte-deterministically; IPTC carrier is raw IIM datasets in tag 0x83BB after the
  first rehearsal showed the pinned CLI normalizing the 8BIM-wrapped variant on
  rewrite; duplicate carriers are authored as repeated entries in one IFD0 — an
  invalid-but-authored layout on purpose to record CLI duplicate handling).
- tools/reference/observe_b4.py mirrors the B3 observer (pinned source identity +
  CMAKE policy checks, fixture regeneration + manifest byte-compare, per-row recording,
  `nonzero-exit-artifact-produced` class, never gated on outcome). TIFF analog of the
  scan invariant: **IFD0 strip/tile image payload bytes preserved in 49/49 observed
  rows** (`scanPreserved: true`; IFD1/SubIFD entries deliberately excluded — metadata
  rewrites legitimately rewrite them), plus a strict classic II/42 + IFD structural
  parse of every mutated file (typed via the shared parser, extended with
  UNDEFINED/RATIONAL/SRATIONAL/FLOAT/DOUBLE/IFD type sizes after the CLI rewrote ICC
  tag 0x8773 as type 7 UNDEFINED — the spec-correct type). 49 rows observed; 1 static
  not-applicable (`exif-ifd1-uncompressed` thumbnail INSERT — the pinned CLI has no
  uncompressed-thumbnail write path; the same CLI limitation as the JPEG/PNG/WebP
  analogs; candidate for denominators, never self-granted). Outcome counts:
  49 accepted, 0 accepted-unexpected, 1 static-not-applicable.
  Notable pinned-CLI behaviors recorded, not gated:
  `EX0289-TIFF-THUMBNAIL-REPLACE-0001`: replace on an uncompressed-strip IFD1 yields
  a **mixed state** (strip tags AND JPEG thumbnail tags coexist — CLI-defined,
  record-only, mirroring the B1/B2/B3 findings); `-i t` embeds a JPEG thumbnail only.
  `EX0289-TIFF-IPTC-*-0002` (duplicate `0x83BB` entries, record-only): `add` applies
  to BOTH entries; `del` is a **silent no-op** (rc=0, datasets still present).
  EXIF duplicates (`Artist` authored twice): `set` rewrites the FIRST entry only,
  `del` removes from BOTH. XMP duplicates (`0x02BC` twice, packets A/B): `set`
  rewrites the FIRST packet only while `-pa` prints the LAST (first-wins write,
  last-wins print). ICC duplicates: writes collapse to a single entry; `-d C`
  removes exactly one of the two. XMP `del Xmp.xmp.CreatorTool` removes the WHOLE
  `0x02BC` tag with rc=0 (unlike the WebP B3 silent no-op). IPTC rewrite
  normalization: the CLI rewrites tag `0x83BB` to raw IIM (no 8BIM wrapper).
  `-iC -l <dir>` reads `<dir>/<target-stem>.icc`; `-i t` from a thumbnail-free
  EXIF-only base creates the IFD1 JPEG thumbnail (spec-derived, verified at
  observation time). Both documented-claim IPTC cells and the whole batch observe
  rc=0 across all 49 rows. The one noDocumentedClaim row
  (`EX0289-TIFF-COMMENTS-READ-0001`) observes rc=0 with NOTHING surfaced by `-pc`
  on an `XPComment` (0x9C9C, UTF-16LE) carrier; the carrier tag is recorded in facts,
  nothing asserted.
- docs/research/evidence/e01-b4-census/ — committed observations.json + README.
  Census regenerated: 353 rows (B0 135 + B1 58 + B2 67 + B3 43 + B4 50); every B4
  row observed-cli (49) or statically not-applicable (1); inventory track is now
  JPEG, PNG, WebP, TIFF. `denominatorFrozen` stays false; verification stays
  not-assessed everywhere; no percentage computed; D2/publication NOT authorized;
  E01 gate remains OPEN. Next batch: B5.
- Discovery suite: 94/94 local (13 census + 13 WebP fixture tests + 14 new TIFF
  fixture tests included).
- CI: `pinned-exiv2-baseline` job re-runs all batches (observe_b0.py --batch all,
  observe_b2.py, observe_b3.py, observe_b4.py) and uploads `e01-b4-census-observations`
  next to the B0/B1/B2/B3 artifacts; the workflow was extended accordingly.
### E01 B4 census batch — PR #33 merged — 2026-11-09

- 2026-11-09: PR #33 merged (`1720d7be2c229bd12100b9bfaab0341a53b96d54`,
  run 42674518556, 15/15 green). NOTE: the first two runs for PR #33 (push
  42673623285, pull_request 42673654413, 2026-11-09T18:23Z) failed on the GitHub side
  with 2/0 of 15 jobs started (no job logs; the workflow revision was fully green on
  master 20 minutes earlier); the PR was closed and reopened to fire a fresh
  pull_request run, which went 15/15 green at 18:31Z — no content changes
  accompanied the reopen. The `pinned-exiv2-baseline` job rebuilt the pinned CLI
  (b0859fc61), regenerated and byte-compared the 14 owned TIFF fixtures against
  tests/fixtures/tiff/manifest.json, and re-ran B0+B1+B2+B3+B4; artifacts uploaded
  (`e01-b4-census-observations` id 13698689024, digest sha256:5088f05c…, expires
  2026-12-09; `e01-b0/b1/b2/b3-census-observations` refreshed). Receipt committed as
  docs/research/evidence/e01-b4-census/receipt.json (metadata-only digests;
  artifact bytes not retrievable from the sandbox — flagged, not waived).
  Census stays 353 rows; `denominatorFrozen: false`; verification not-assessed;
  no percentage; D2/publication not authorized; E01 gate OPEN. Next: B5.
### Preliminary status summary + merged-branch cleanup — 2026-11-09

- 2026-11-09: CURRENT_STATE.md consolidated with a preliminary status summary
  (PR #35, merged `68fc4703c9cf03202fd0d6ac12821b98b6bd6b2c`, run 42676746713,
  15/15 green; docs-only) — product state (M00–M18 complete; restricted
  JPEG/PNG/WebP/TIFF editing, XMP sidecars, IPTC IIM, DNG D1 read-only, E03
  additive `MetadataCapabilities`; zero runtime deps; net8.0+net10.0; 548 passed in
  latest CI annotation), programme state (E01 bounded CLI + E02 + E03 merged/approved;
  census B0–B4 DONE: 353 rows, 311 observed-cli + 42 not-applicable, payload
  preserved 284/284, all rows verification not-assessed, denominatorFrozen false,
  freeze only after B14), next batch B5 (XMP packets + sidecars) in §5 order,
  E06–E18 not started, publication NOT authorized, and the GitHub-side 18:23Z
  Actions failure recorded.
- 2026-11-09: Remote-branch cleanup of obsolete merged `arena/*` heads per developer
  request. GitHub availability checked first: `https://www.githubstatus.com/` is NOT
  reachable from this sandbox (http 000); live status verified via
  `https://api.github.com/status` ("GitHub lives!") and observed CI behavior
  (15/15 green runs for PRs #33–#35 after the earlier transient failure). Full
  history fetched (`git fetch --unshallow origin`, 170 commits). No open PRs at
  cleanup time. Each candidate was required to be (a) the head of at least one merged
  PR, (b) an ancestor of `origin/master` (`git merge-base --is-ancestor`, re-verified
  immediately before deletion), (c) not `master`, not the active branch
  `arena/3db41ee3-metadatasharp`. All 18 candidates passed and were deleted via the
  refs API (tips recorded): `08b4bad9`(#25), `1af1ed4a`(#15), `250ddde9`(#28),
  `3c179e97`(#31), `45659746`(#24), `49d49a4f`(#16), `67d6975a`(#32),
  `7706fd42`(#27), `7a35ad89`(#18), `96e3dfa2`(#26), `c08c2ef6`(#23),
  `c321dc16`(#17), `c7e53ae9`(#33,#34), `cdb43c96`(#21,#22), `d0eec35b`(#14),
  `d4158be1`(#19,#20), `d5125ece`(#13), `e082b0ba`(#29,#30). Remote heads are now
  exactly `master` and `arena/3db41ee3-metadatasharp`. No tags deleted
  (`sdk_bootstrap` preserved); no history rewrite; nothing merged out-of-gate.
### E01 B5 census batch — XMP packets + sidecars — 2026-11-09

- 2026-11-09: B5 executed in the approved §5 numeric order (next after B4). Scope: the
  standalone `.xmp` packet document (the sidecar artifact) as a metadata carrier —
  XMP × {inventory, exif/iptc/comments/icc/thumbnail (noDocumentedClaim reads), xmp
  (documented Read/Write)} plus four **sidecar-presence read probes** (JPEG/PNG/WebP/
  TIFF). Owned synthetic XMP packet fixtures authored by `tools/reference/xmp_owned_fixtures.py`
  (stdlib byte-level writer; wrapped layout carrying xpacket PIs + `<x:xmpmeta>` and the
  optional-wrapper-omitted variant per XMP Part 1 §7.3–§7.5, layout per the approved
  encoding/structure cat; UTF-8 plus invalid-UTF-8 `unknown-raw` cells; 6 fixtures,
  SHA-256 pinned in tests/fixtures/xmp/manifest.json, byte-deterministic regeneration;
  rights owned-synthetic, MIT; no fixture binaries committed).
- tools/reference/observe_b5.py mirrors the B3/B4 observer (pinned source identity +
  CMAKE policy checks, fixture regeneration + manifest byte-compare, per-row recording,
  `nonzero-exit-artifact-produced` class, never gated on outcome). The in-image "scan
  preserved" invariant has no image analog here (the packet IS the file), so each row
  records SHA-256/length before/after and the xpacket PI counts; a strict XMP parse
  (well-formedness + UTF-8 check) of every authored/rewritten packet is the structural
  analog. 26 rows observed; 0 static not-applicable. Outcome counts:
  17 accepted, 2 accepted-unexpected, 6 accepted-unparseable, 1 refused,
  0 scan-changed.
- Notable pinned-CLI behaviors recorded, not gated (rehearsal run; CI re-verifies):
  `EX0289-XMP-XMP-REMOVE-0001/0003` (utf-8, both layouts, `accepted-unexpected`):
  `del Xmp.xmp.CreatorTool` exits rc=0 with the file **byte-identical** — the same
  silent no-op class as the B3 WebP analog. UTF-8 READ/INSERT/REPLACE all accepted with
  spec-derived expectations held (insert from nothing creates the property; replace swaps
  Alice→Bob); every successful write **re-serializes** the packet (CLI XMP Core
  4.4.0-Exiv2, attribute-form properties, canonical xpacket id `W5M0MpCehiHzreSzNTczkc9d`
  replaces the authored id; lexical form/attribute order/size NOT preserved; the wrapped
  layout is emitted even for the unwrapped input). The invalid-UTF-8 `unknown-raw`
  READ/REPLACE cells are rc=0 with `XMP Toolkit error` on stderr (`accepted-unparseable`;
  REPLACE rows gain a leading partial canonical packet, xpacket PI counts 1→2).
  `EX0289-XMP-INVENTORY-INVENTORY-0001` (`refused`): `-pS` on a bare `.xmp` prints
  `Image type ... is not supported`, rc=1 — the pinned CLI does not inventory standalone
  XMP packets. The four sidecar-presence read probes record the SIDECARPROBE token
  **absent** from the printed output for all four formats (rc=0; adjacency never
  consulted; the image's own token printed); `xmp-applied` writes stay E15 per the
  approved denominators.
- docs/research/evidence/e01-b5-census/ — committed observations.json (raw JSON as-is) +
  README (provenance, reproduction, limits). Census regenerated: 379 rows
  (B0 135 + B1 58 + B2 67 + B3 43 + B4 50 + B5 26); every B5 row is observed-cli
  (26); inventory track now JPEG, PNG, WebP, TIFF, XMP. `denominatorFrozen` stays false;
  verification stays not-assessed everywhere; no percentage computed; D2/publication NOT
  authorized; E01 gate remains OPEN. Next batch: B6 (DNG) per §5 order.
- Enumerations: the closed per-family layout enums in build_census.py gain
  'xmp-packet' for the comments/exif/icc/iptc/thumbnail families — the XMP-format
  noDocumentedClaim probe cells of B5 ride inside the standalone packet document
  itself. B5-additive extension under the approved batch (reviewedAt re-stamped
  2026-11-09; no row/value repurposed). Discovery suite: 103/103 local;
  reference suite 17/17 local.
- CI: `pinned-exiv2-baseline` job re-runs all batches (observe_b0.py --batch all,
  observe_b2.py, observe_b3.py, observe_b4.py, observe_b5.py) and uploads
  `e01-b5-census-observations` next to the B0–B4 artifacts; the workflow was extended
  accordingly (B5 step + artifact upload). Coverage denominators remain pending
  developer sign-off after the batch set completes (freeze only after B14).
### E01 B5 census batch — PR #37 merged — 2026-11-09

- 2026-11-09: PR #37 merged (`7b4bc97d4e1ba85120aae620aedf480dc3883377`,
  run 42683793923, 15/15 green). NOTE: the first pull_request run for PR #37
  (42683123936, 19:25Z) failed ALL SIX OS/TFM test jobs — a real failure caught by
  CI, not GitHub-side: the closed per-family layout enumerations in
  tools/reference/build_census.py did not list 'xmp-packet' for the
  comments/exif/icc/iptc/thumbnail families used by the B5 noDocumentedClaim probe
  rows. Fixed in 81281d6 (B5-additive enum extension; reviewedAt re-stamped
  2026-11-09; census regenerated; discovery 103/103; reference 17/17). The
  twin push-event run (42683074718) additionally showed three id-less phantom
  failures at 19:25Z (net8/net10 ubuntu, net10 macos) — the same GitHub-side
  phantom-failure class recorded on 2026-11-09T18:23Z for PR #33; no content remedy.
  The re-fired pull_request run went 15/15 green at 19:30Z (the
  `pinned-exiv2-baseline` job rebuilt the pinned CLI, regenerated and byte-compared
  the 6 owned XMP fixtures against tests/fixtures/xmp/manifest.json, and re-ran
  B0+B1+B2+B3+B4+B5); artifacts uploaded (`e01-b5-census-observations` id 13703453617,
  digest sha256:0d6d6230…, size 5628, expires 2026-12-09;
  `e01-b0…b4-census-observations` and `e01-exiv2-0289-observations` refreshed).
  Receipt committed as docs/research/evidence/e01-b5-census/receipt.json
  (metadata-only digests; artifact bytes not retrievable from the sandbox — flagged,
  not waived; committed rehearsal observations are 53522 bytes vs the CI artifact
  zip's 5628 — see the README provenance sections for the reconciliation).
  Census stays 379 rows; denominatorFrozen stays false; verification not-assessed;
  no percentage; D2/publication NOT authorized; E01 gate OPEN. Next: B6 (DNG) per
  §5 order (not started; pending developer «гоу»).

### E01 B6 — local observations, CI pending

- Implemented D1-derived DNG fixture catalog, bounded CFA/IFD0/IFD1 oracles,
  88-row census observer and CI step on `arena/3dc8aab2-metadatasharp`.
  Local rehearsal (observer source b1acd7a, Exiv2 source b0859fc61) recorded
  84 accepted and 4 nonzero-exit-artifact-produced; the latter are matching
  JPEG thumbnail extractions returning 137. CFA strip bytes unchanged in 88/88;
  this does not decode raw pixels. IFD1 variant full DNG conformance is not
  independently established. See `docs/research/evidence/e01-b6-census/` for
  scope, CLI-only semantic checks and local build provenance. B6 requires green
  CI and merge before calling the batch done. Census 467 rows, 425 observed-cli
  and 42 static not-applicable; all verification not-assessed, denominatorFrozen
  false; no percentage. Reference tests 34/34, discovery 104/104, census check
  passed. User authorized subsequent stages without a new confirmation, not
  publication, DNG D2 write or denominator freeze.

### E01 B6 — PR #39 merged; CI observation reconciliation

- PR #39 merged as `e6325beddea4b53f7297eb5f01aac4e769a4f30d` after
  pull-request run 42698556762 went 15/15 green. The B6 job and upload passed;
  CI artifact id 13709699612, digest sha256:c40837cb72be52ba0d1bfd6dfa08f9440aee51e56604c6659406bea4587bf8e7.
  Seven of seven check-run B6 annotations were decoded to `ci-observations.json`;
  all 88 observation objects and the fixture manifest match the local rehearsal
  exactly. Full reports differ in environment/provenance. Receipt records hashes.
  Earlier attempts at annotation recovery exceeded the 10-notice limit or the
  4096-character message limit; the final run used seven 4000-character chunks.
  B6 limitations remain: no raw decoding, unproven full IFD1 DNG conformance,
  record-only IPTC/XMP semantics, denominator unfrozen and verification
  not-assessed. Next B7, no new user confirmation required; rights and oracle
  reviews still apply.

### E01 B7 — preliminary rights and oracle groundwork

- Began per-format rights/source review for AVIF, HEIF, HEIC, CR3 and JXL; no
  B7 fixture is cleared or authored, and no B7 census row is observed. Added
  bounded flat-BMFF/ftyp/JXL-signature helper and toy structural tests (not image
  samples), plus a read-only secondary ExifTool probe pinned to 12.76 and commit
  212f02cf22b9484674752181cad3b73089ce9694. The probe's local smoke on an owned
  D1 DNG returned exit 0 and a JSON object; this is not evidence of B7 support.
  Per-format rights/spec review, owned fixtures, Exiv2 observation, ExifTool
  comparison, CI and review are still pending. User authorized continuing stages
  without further confirmation; separate publication, D2 write and freeze
  restrictions remain. See docs/research/E01_B7_RIGHTS_AND_ORACLE_REVIEW.md.

- B7 groundwork PR #41 merged at `49b7e0d2320c6c46faa3200f33c73c8a27f268b6`,
  run 42701867621, 15/15 green. Subsequent in-progress work adds a bounded generic
  extent accounting helper (not an `iloc` parser) and records preliminary
  AVIF brand/box source review. No B7 image fixture is cleared or authored;
  unresolved normative/rights/decode gaps are not treated as observation.

### E01 B7 — owned probes and local observation; CI pending

- Groundwork PR #41/#42 merged; authored five small 8×8 AVIF, HEVC-in-HEIF,
  HEVC-in-HEIC and JXL container/codestream fixtures using pinned external
  encoders and local input bytes. The two HEVC files carry the same HEIC brand;
  JXL codestream and container are distinct. Per-format rights and codec/decode
  limits are documented; no external sample or production dependency imported.
- Implemented 48-row B7 read-only observer with pinned Exiv2 + ExifTool 12.76
  secondary results. Local rehearsal: 32 accepted record-only, eight refused
  (bare JXL), eight CR3 blocked-rights; all 40 attempted reads left input
  unchanged. HEVC ispe 64×64 vs decoded clean aperture 8×8 is explicit.
  IPTC/ICC/comments/thumbnail absent in fixtures; no successful metadata
  extraction is inferred. CR3 no-claim rows remain unattempted before freeze.
  The local evidence is at docs/research/evidence/e01-b7-census/; CI pending.
  Census now 515 rows (465 observed, 42 static-NA, 8 blocked); all verification
  not-assessed, denominator unfrozen. Local tests: reference 54/54, discovery
  105/105, census check and exact pinned-wheel regeneration passed.

### E01 B7 — bounded probe PR #43 merged; unresolved gaps remain

- PR #43 merged as `514ed0b379750b7c58c73035e49c297826625890`; pull-request
  run 42707052713 passed 15/15, including hash-pinned re-encoding of all five
  fixtures, pinned Exiv2/ExifTool observation and artifact upload. All three
  B7 annotation chunks recovered the CI report. The 40 observations match local
  after normalizing only absolute target paths in stderr; all eight blocked
  records and the fixture manifest match exactly. Secondary ExifTool JSON matches
  after removing `System:*` and SourceFile; full reports still differ in paths,
  timestamps and provenance. Receipt records the hashes and artifact metadata.
- Eight CR3 cases are blocked, not observed, refused, excluded or waived.
  Metadata-present IPTC/ICC/comments/thumbnail and independent JXL decode remain
  gaps. This bounded B7 probe batch is merged, but the user request for full B7
  cannot honestly be described as completely resolved while these gaps remain.
  Do not freeze the denominator or claim verification/parity.
