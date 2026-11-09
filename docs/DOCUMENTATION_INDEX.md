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
- design/EXIV2_E01_DENOMINATORS_PREFREEZE_REVIEW.md: pre-freeze review package
  (counts, freeze timing Q1–Q5); ANSWERED 2026-11-08 — binding answers recorded in the
  census proposal §9; freeze pending B2–B14; denominatorFrozen stays false.
- design/EXIV2_E01_CENSUS_AND_DENOMINATOR_PROPOSAL.md: E01-CEN design (schema,
  fixture provenance, batching, two denominators); APPROVED by developer 2026-11-08
  (decisions in §6/§7). Denominators not frozen yet; no percentage exists.
- research/EXIV2_CENSUS.json: generated census (schemaVersion 1; B0/B1 JPEG + B2 PNG
  + B3 WebP batches so far; 303 rows); regenerate only via
  tools/reference/build_census.py (--check enforced in CI).
- research/EXIV2_CAPABILITY_BASELINE.md and EXIV2_REQUIREMENTS_TRIAGE.md: bounded
  first-pass evidence, snapshot JSON, unresolved full E01 inventory work.
- design/DNG_D0_READ_ONLY_CONTRACT_PROPOSAL.md: exact new API/subset proposal,
  explicitly approved 2026-11-07, with normative/oracle/runtime gates; gates 1–3
  are closed for D1 (see below), implementation merged via PR #21.
- design/DNG_D1_NORMATIVE_LEDGER.md: binding field/count/role/constraint ledger for
  the D1 profile (root-vs-raw-leaf identity mirror rules, error mapping, known gaps).
- design/DNG_D1_FIXTURES_AND_ORACLES.md: owned fixtures provenance (two independent
  writers, CI byte cross-check), structural oracle + pinned ExifTool 12.76 inventory
  assertions, explicit raw-decode oracle gap (blocker-class, accepted).
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
- research/evidence/e01-b0-census: B0 pilot observation JSON (97 observed rows,
  outcome counts) + README (provenance, limits, reproduction); fixtures are
  owned-synthetic, SHA-256 pinned in tests/fixtures/jpeg/manifest.json.
- research/evidence/e01-b1-census: B1 XMP/IPTC/thumbnail observation JSON (57 observed
  rows, outcome counts) + README; same JPEG fixture pin set.
- research/evidence/e01-b2-census: B2 PNG inventory/comments/exif(noDocumentedClaim)/ICC/
  IPTC/thumbnail/XMP observation JSON (66 observed rows, 1 static not-applicable) +
  README; fixtures owned-synthetic, SHA-256 pinned in tests/fixtures/png/manifest.json.
- research/evidence/e01-b3-census: B3 WebP inventory/comments(noDocumentedClaim)/EXIF/ICCP/
  IPTC(noDocumentedClaim)/thumbnail/XMP observation JSON (42 observed rows — 41 accepted +
  1 accepted-unexpected, 1 static not-applicable) + README; fixtures owned-synthetic,
  SHA-256 pinned in tests/fixtures/webp/census-manifest.json (the audit's
  tests/fixtures/webp/provenance.json is deliberately untouched); receipt.json records
  the merge run (metadata-only digests).
- research/evidence/e01-b4-census: B4 TIFF inventory/comments(noDocumentedClaim)/EXIF/ICC/
  IPTC/thumbnail/XMP observation JSON (49 observed rows — 49 accepted, 0 accepted-
  unexpected, 1 static not-applicable) + README; fixtures owned-synthetic,
  SHA-256 pinned in tests/fixtures/tiff/manifest.json; IFD0 strip/tile payload bytes
  preserved 49/49; receipt.json records the merge run (metadata-only digests; notes the
  transient GitHub-side failure of the two first runs and the close/reopen retrigger).
- research/evidence/e01-b5-census: B5 XMP (standalone packet document / sidecar
  artifact) inventory/exif(noDocumentedClaim)/iptc(noDocumentedClaim)/comments
  (noDocumentedClaim)/icc(noDocumentedClaim)/thumbnail(noDocumentedClaim)/xmp
  observation JSON + README; fixtures owned-synthetic, SHA-256 pinned in
  tests/fixtures/xmp/manifest.json; outcome counts 17 accepted, 2 accepted-unexpected
  (REMOVE is a silent no-op in the pinned CLI, both layouts — B3 WEBP analog),
  6 accepted-unparseable (unknown-raw, rc=0 with XMP Toolkit decode errors),
  1 refused (`-pS`: bare .xmp is not a supported image type, rc=1); the four
  sidecar-presence read probes (JPEG/PNG/WebP/TIFF) record SIDECARPROBE token absent
  from the image's printed output (presence never implies application; applied writes
  stay E15); observations recorded not gated; no parity percentage implied; receipt.json
  records the merge run (metadata-only digests; notes the real CI catch on the closed
  layout enumerations in the first PR #37 run and the GitHub-side phantom failures in
  the twin push run).
- research/E01_B7_RIGHTS_AND_ORACLE_REVIEW.md: preliminary B7 format rights and oracle ledger; no fixture clearance.
- research/evidence/e01-b8-psd-preflight: authored PSD local secondary reference and decoder sanity checks; CI preflight pending.
- research/E01_B8_RIGHTS_AND_ORACLE_REVIEW.md: per-format B8 rights and oracle decisions before fixture creation.
- research/evidence/e01-b7-census: B7 Exiv2/ExifTool observations, CI receipt and explicit CR3/metadata/decoder gaps.
- research/evidence/e01-b6-census: 88 local pinned-CLI DNG observations and
  limitations, including the raw-decode gap and structural IFD1 probe caveat;
  PR #39 merged; receipt and annotation-recovered CI observations included;
  not MetadataSharp verification.
- research/evidence/e01-b6-preflight: separate exploratory D1 probes; not census rows.
- research/evidence/e01-pinned-jpeg: complete successful observation JSON + source-
  bound recovery receipt for 601a20b/run42449222307; no binary/source catalogs.
