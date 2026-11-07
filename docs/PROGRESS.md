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
