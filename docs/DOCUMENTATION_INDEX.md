# Documentation index: current work vs retained history

## Current entry points

- CURRENT_STATE.md: source-of-truth recovery status, approvals and blockers.
- roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md: new programme E00–E18; user-approved
  direction, not a parity completion claim.
- design/DOTNET_ARCHITECTURE_AND_COMPATIBILITY_CONTRACT_PROPOSAL.md: exact E03
  contracts, APPROVED 2026-11-08 and implemented (additive MetadataCapabilities);
  receipts in PROGRESS.md.
- design/EXIV2_PARITY_MASTER_PLAN.md: how the 2026-11-08 parity directive is
  broken into gated subtasks; sequencing, dependency policy and definition of done.
- research/EXIV2_CAPABILITY_BASELINE.md and EXIV2_REQUIREMENTS_TRIAGE.md: bounded
  first-pass evidence, snapshot JSON, unresolved full E01 inventory work.
- design/DNG_D0_READ_ONLY_CONTRACT_PROPOSAL.md: exact new API/subset proposal,
  explicitly approved 2026-11-07, with normative/oracle/runtime gates before implementation.
- DEVELOPMENT.md: standing safety, licensing, verification and workflow policy.
- BUILD_AND_TEST.md: reproduction and historical observed results.

## Archive-in-place policy

ROADMAP.md and prompts/M00.md–M18.md describe the **completed** original programme.
They are retained as historical records, not current execution instructions. Existing
approved design contracts, normative sources, limitations and audit evidence remain
binding for existing code. Do not delete them just because implementation completed.
PROGRESS.md is chronological evidence; old future-tense entries are superseded by
later checkpoints, not silently rewritten. Repository links to those records remain
valid. No bulk moves, contract deletion, provenance removal or new "clean" history.

This cleanup marks old entry points historical and replaces the active recovery
instructions. Future physical archive moves require a link migration pass; there is
no need to churn dozens of reviewed contracts now. Restricted/GPL source/spec copies,
issue bodies/attachments and downloaded fixtures are not part of the retained docs.

Additional current research (not completion claims):
- research/EXIV2_BODY_REVIEW_SUPPLEMENT.md: selected issue/discussion initial bodies
  reviewed as requirements; complements the earlier title-only snapshot.
- research/DOTNET_TARGET_FEASIBILITY.md: modern multi-target gate and preliminary
  Framework API cost map; no build or compatibility claim.

E02 current support/build evidence:
- audits/DOTNET_8_10_SUPPORT_AND_E02_AUDIT.md and evidence/e02: dual-runtime tests,
  package manifests, matching-SDK consumers and reflected-signature receipts.
- research/EXIV2_OPERATION_SEEDS.json and tools/reference/build_capability_seeds.py:
  reproducible E01 documentation seeds, NOT frozen denominator or parity coverage.

E01 pinned execution (bounded, not E01 closure):
- research/EXIV2_PINNED_BASELINE_EXECUTION.md: exact source/build/fixture scope,
  first failure diagnoses, explicit D001 COM/D002 XMP preservation differences.
- research/evidence/e01-pinned-jpeg: complete successful observation JSON + source-
  bound recovery receipt for 601a20b/run42449222307; no binary/source catalogs.
