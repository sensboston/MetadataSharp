# Current development state and recovery checkpoint

Updated 2026-11-07. Read first; compare with live branch/Git/PR/CI. Current assigned
branch **arena/d0eec35b-metadatasharp**, based on PR #13 merge
820229f57abe6cc10400087ca56ed2bdd0980911. Never use older session branch names.

## Approved new direction and scope

Developer approved a new large programme aiming at practically complete Exiv2
user-visible capability coverage via independent implementation, with improvements
from issues/discussions, idiomatic .NET contracts, **.NET 8 + .NET 10 LTS**, and
Framework 4.8.x ONLY if genuinely cheap. See
roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md for E00–E18 and honest coverage rules.
No C++ ABI/CLI syntax clone, GPL-derived implementation/tests/dictionaries, general
MakerNote relocation, release publication or unseen public APIs approved.

## This submission

- E00 programme documentation recorded; historical M00–M18 entry points marked
  archive-in-place, binding contracts/audits/provenance kept. DOCUMENTATION_INDEX
  distinguishes current work and history. No production source/config changed.
- E01 initial discovery performed: Exiv2 v0.28.9 (2026-08-30), annotated tag
  110cbaef8e75f0e064e5c522846a18982bc4db70 → b0859fc61fea5dedfc898aece6b15893ddf4314e.
  Reviewed README/license/CLI documentation facts, not implementation or tests.
  31 documented format rows/six metadata categories; 35 issue title/label/state
  signals from three bounded search queries and 12 of 37 discussions (metadata
  only in the initial snapshot). Additional selected-body review is recorded in
  research/EXIV2_BODY_REVIEW_SUPPLEMENT.md (no source/patch/attachment review).
  Full E01 operation inventory/CLI build verification and denominator remain
  OPEN, no percentage claim. Sources/sampling limits in docs/research.
- Official dotnet/core lifecycle index blob 66392f8bd6d254f73cf7a2f42113bc4034a20747
  confirms .NET 8 EOL 2026-11-10, .NET 10 EOL 2028-11-14. No retargeting/builds done.
  E02 must prove both TFMs/SDK selection/packages/platforms. Framework is unproven,
  not a promised TFM or a trivial compatibility package. Initial API cost map is now in research/DOTNET_TARGET_FEASIBILITY.md; light
  Framework support has NOT been established and no compatibility build ran.
- E04/D0 exact read-only DNG proposal prepared in
  design/DNG_D0_READ_ONLY_CONTRACT_PROPOSAL.md. **UNAPPROVED, not implemented.**
  Exact APIs/error addition/subset proposed; normative ledger/fixture rights/raw
  oracle/TFM/compatibility gates remain before D1. Existing TIFF DNG refusal stays.
- Local Python policy and document JSON/link/diff checks are the available checks.
  No .NET 10/Framework/Exiv2 0.28.9 binary/DNG decoder run is claimed. No legal
  clean-room certification, exhaustive issue-body review or parity score claimed.

## Next steps and stop boundaries

1. Save/push this documentation/research PR and inspect its actual CI. Prior GitHub
   account billing/spending problem blocked PR #13 jobs. User allowed only that
   documentation PR to merge without green CI; **exception does NOT carry over**.
   Do not merge a new red/unexecuted head without successful checks or new explicit
   per-PR deferral. Do not repeatedly retry billing failures without an account fix.
2. Request explicit D0 contract approval, not generic programme approval. Close its
   normative/independent-oracle prerequisites before D1; do not implement DNG now.
3. Continue full E01 atomic baseline/corpus/reference build reconciliation, with
   rights per fixture and separation of docs claims/observations/test expectations.
   E02 runtime work can be scoped independently when required CI is available.
4. E03 architecture proposal before public API refactor. E06+ umbrella format/vendor
   stages require bounded per-family prompts and new contract gates before code.

## Completed history and tools

M00–M18 complete within original bounded scope. M17 feasibility is not DNG support;
M18 audit is not publication. PR #12 merged b7c9f395; final head 02be218 passed
42440206658/42440203208: 448 tests/platform, Python 12/12, required references and
three platform audits. PR #13 merged 820229f5 under one-off documentation-only CI
exception; ten old proven-merged branches deleted, active session branches kept.
Exact old decisions/receipt links remain PROGRESS/ROADMAP/audits. Old .NET 8 CI
success and pinned Exiv2 0.27.6 are NOT evidence of new runtime/baseline parity.

```sh
git status --short
git branch --show-current
python -m unittest discover -s tests/reference -q
git diff --check
gh pr list --head arena/d0eec35b-metadatasharp --state all
gh run list --branch arena/d0eec35b-metadatasharp --limit 4
```

Local SDK absent; do not repeat failed SDK/Actions blob downloads. Use check-run
annotations for infrastructure/TRX errors. Current pipeline requires seven jobs;
none waived by this submission. Downloaded review material stays ignored artifacts;
only small source metadata/factual research JSON is committed, not GPL source or PDFs.
