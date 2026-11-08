# E01 denominators — pre-freeze review package (2026-11-08)

Status: **NOT a freeze, NOT a percentage, NOT a signed exclusion list.** This is the
review material for the developer's denominators decision in the approved order
(B0 → B1 → denominators → cleanup → public write-up). Nothing here changes
`denominatorFrozen` (stays `false`), nothing is computed for publication, and
D2/publication remains NOT authorized.

## 1. Where the program actually stands

Committed, merged and CI-verified (PR #24, PR #25, PR #26):

- Census artifact `docs/research/EXIV2_CENSUS.json` — **193 rows**, JPEG only:
  B0 135 (exif 36, comments 78, icc 20, inventory 1), B1 58 (xmp 32, iptc 16, thumbnail 10).
- `observed-cli` 154 / `not-applicable` 39 (38 B0 reviewed axis collapses + 1 B1:
  `exif-ifd1-uncompressed` thumbnail insert — the pinned CLI exposes no
  uncompressed-thumbnail write path).
- Outcomes (pinned Exiv2 0.28.9 black-box CLI): **144 accepted**, **8 refused**
  (all ICC duplicate-profile mutation/extract rows; `Not a valid ICC Profile`),
  **2 `nonzero-exit-artifact-produced`** (thumbnail extract exits 137/46 despite
  succeeding; JPEG-compressed extract verified byte-exact vs the authored blob),
  **39 static not-applicable**. Zero scan-byte changes in 193/193 rows.
- `verification = not-assessed` everywhere (E07); `noDocumentedClaim = false` on all
  193 rows (JPEG cells are documented-claim cells).
- Evidence: `docs/research/evidence/e01-b0-census/`, `e01-b1-census/` (committed
  local rehearsals + receipts; CI is the authoritative re-runner, artifact bytes
  unreachable from the dev sandbox — no byte-equality claimed).
- Pending: **180 of 186 documented seed cells** (all families of the other 30 formats;
  JPEG is fully enumerated). The 72 no-documented-claim/empty cells (71 `-` + 1 empty
  EPS comments) will materialize as census rows only when their batches enumerate;
  they are visible today solely in `EXIV2_OPERATION_SEEDS.json`.

## 2. What the denominators will count (approved §6 definitions)

- **`D_full`**: every census row generated from the 31×6 grid × operations × reviewed
  enumerations — including `-` cells (71) and the 1 empty cell, rights-blocked rows,
  refused rows and not-yet-observed rows. Today that is 193 rows; the final count is
  only knowable after the remaining batches generate their rows.
- **`D_accept`**: `D_full` minus developer-approved exclusions, each with ID, reason,
  reviewer, date, recorded in the census and PROGRESS.md. **No exclusions exist today.**
- **Reporting rule (post-freeze)**: headline `verified / D_accept`; secondary
  `verified / D_full`; `refused`, `blocked`, `unknown`, `noDocumentedClaim-pending`
  always printed alongside. A refused row is never a pass; a blocked row is never dropped.

## 3. If a freeze happened against today's enumeration (informational only — NOT proposed)

- `D_full` = 193; exclusions so far = 0 ⇒ `D_accept` = 193.
- `verified` = 0 (nothing is verification-assessed; E07 pending) ⇒ reports would read
  **0 / 193** and **0 / 193** regardless of which denominator is shown.
- This would freeze against a 1-of-31-format enumeration and would silently shrink the
  denominator every time a later batch adds rows. It is presented only to show the
  mechanics; it is **not** proposed for approval.

## 4. Decisions needed from the developer

**Q1 — Freeze timing.** (a) One final freeze after all remaining format batches are
enumerated and observed (only option compatible with the approved §6 definitions);
(b) additionally keep publishing provisional per-batch `D_full` counts (clearly labeled
"provisional, excludes pending batches") for progress visibility; (c) other.

**Q2 — Exclusion candidates (identification only; none is approved):**
1. `EX0289-JPEG-THUMBNAIL-INSERT-0002` — static not-applicable: no uncompressed-thumbnail
   write path in the pinned CLI. Options: keep as `not-applicable` in both denominators
   (recommended; reported, never a pass) or grant a signed exclusion.
2. The 8 ICC duplicate-profile refusals and the 2 `nonzero-exit-artifact-produced` rows
   are **not** exclusion candidates (refused/artifact-produced rows stay in both
   denominators per §6.4 and Q3 below). No other candidates are identified at this time;
   future candidates arise per batch and are never self-granted.
3. Video/BMFF rows (B7+) remain reserved for their per-format rights review — not
   signable now.
4. `noDocumentedClaim` rows (71 `-` + 1 empty) must be attempted via pinned-CLI
   observation before the final freeze per the approved §6(ii); confirm this holds for
   the chosen freeze point, including any corpus-gated cells (then the cell stays in
   `D_full` as `blocked`, never silently dropped).

**Q3 — New outcome class.** Proposed treatment for `nonzero-exit-artifact-produced`
(2 rows): reported as its own class alongside accepted/refused/blocked/…; **never a
parity pass**; no exclusion proposed; no MetadataSharp mapping promised. Confirm.

**Q4 — Remaining batch sequence.** The master plan batches B2–B14 (PNG, TIFF/DNG-refusal
handling, RAW families, WebP/HEIC, BMFF video, PSD/EPS, GIF, XMP-sidecar-presence,
inventory/B13, B14 vocabulary) remain. Confirm the order to execute next (proposal §5)
and whether every batch before the final denominators freeze is in the current cycle,
and whether B14 (EXIF valueType split `any-documented`) is in scope before the freeze.

**Q5 — Reporting during the gap.** Until the freeze, EXIV2_CENSUS.json stays
`denominatorFrozen: false`; every PR keeps the per-batch observation reports. Confirm no
percentage is to be printed anywhere before the freeze (recommended).

## 5. What this package does not do

No freeze, no percentage, no exclusion signed, no verification assessed, no E07 claim,
no scope change, no D2/publication authorization. The single static not-applicable row
and the two nonzero-exit rows are flagged here exactly so the developer can rule on them —
not to decide them.

## 6. Next step after answers

Implement the decision mechanically (freeze tooling + PROGRESS delta report if Q1=(a);
provisional labeled counts if Q1=(b)) in a dedicated PR; remaining format batches proceed
per the approved order. E01 gate stays OPEN until the developer reviews the frozen
denominators — not before, and not by this document.
