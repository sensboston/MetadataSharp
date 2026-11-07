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
