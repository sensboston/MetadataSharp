# Proposed DNG follow-up prompts — approval pending

Historical sequence from M17, refined by Programme 2 E04/E05/E13.
The exact D0 contract in DNG_D0_READ_ONLY_CONTRACT_PROPOSAL.md was explicitly
approved on 2026-11-07; do not ask for that same approval again. D1 still requires
its section 5 normative/oracle and E02/E03 gates. D2 remains unapproved.
These prompts are NOT blanket executable authorization. Read DNG_SCOPE.md
and obtain the developer's approval of the sequence first. Do not change existing
TIFF DNG refusal while performing a proposal-only unit.

## D0 — exact read-only contract proposal, then STOP

Pin the reviewed normative editions and resolve required-tag/version/role questions
for the D1 candidate in DNG_SCOPE. Propose exact signatures, options, immutable
models, finite bounds, errors, ownership/async semantics and supported exclusions.
Review independent validator availability/licenses and owned fixture strategy.
Ask approval and stop; no parser/writer implementation.

## D1 — approved bounded read-only inventory

Only after D0 public contracts are approved: implement exactly that subset, with
independent known bytes, II/MM fixtures, positive/refusal/boundary/hostile tests,
raw sample oracle and required version-pinned references. Keep all current format
regressions green on three platforms. Record unavailable gates as blockers. Stop
at the independently validated read-only gate; no writer.

## D2 — descriptive write feasibility and contract, then STOP

Only after D1 passes: prove affected offset/integrity/render dependencies, evaluate
append-preserve obsolete-byte implications, and propose exact Software/Artist-only
editing contracts and failure rules. Require independently validated prefix/raw/
preview/digest expectations and refusal matrix. Ask approval and stop before code.
No compressed breadth, private data, digest recomputation or privacy claims implied.
