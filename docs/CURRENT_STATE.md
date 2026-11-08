# Current development state and recovery checkpoint

Updated: 2026-11-07. Read first after interruption; compare with source/Git/live CI.
This record supersedes earlier initial/no-code handoffs. PROGRESS.md retains history.

## Current unit and exact authorization

**M16 restricted standalone TIFF editing: implemented and audited; final expanded
448-case/head gates pending. PR #11 is still draft and unmerged.**

Exact approved public/safety contract:
[STANDALONE_TIFF_EDITING_CONTRACT_PROPOSAL.md](design/STANDALONE_TIFF_EDITING_CONTRACT_PROPOSAL.md).
Developer explicitly approved the retained-obsolete-bytes compromise. Recovery
checkpoint **4bf56c6 was pushed before code**, satisfying the separate prerequisite;
do not repeat approval or that prerequisite. Actual public/safety changes still
need approval. Usage and final source audit:

- design/STANDALONE_TIFF_EDITING_USAGE_AND_LIMITS.md
- audits/STANDALONE_TIFF_EDITING_AUDIT.md
- design/STANDALONE_TIFF_WRITE_NORMATIVE_REVIEW.md

Current session branch `arena/d7ec6276-metadatasharp`, based on M15 merge
80dc75e62eff242d2b98bc4b14b3090cf4ea60ce. Work only on the live assigned Arena branch;
this historical name must not override a later session assignment. Never switch to
master. Saving/pushing and merging a completed green unit are authorized; package
publication is not. Draft PR: https://github.com/sensboston/MetadataSharp/pull/11.

## Observed checkpoints and remaining work

- M00–M15 merged. M15 final 812bd76 passed required 42431094052 and push 42431090942:
  392 .NET tests/platform, Python 12/12 and pinned references. PR #10 merge above.
- Initial M16 range-audited 600bbc1 passed 42435227396: 405/platform plus required
  references. Then 0dd38366cdc5bac2843541d336abfb4c9837cbd5 passed 42435965943 and
  42435961970: 435/platform, policy/references. These are not final-head results.
- Latest validated source **635f8bb565eeaab72a9147d5e9825fdcde54c215** passed required
  [42436144783](https://github.com/sensboston/MetadataSharp/actions/runs/42436144783):
  **438 .NET tests on each Windows/Linux/macOS**, Python **12/12**, mandatory pinned
  external references. This includes malformed-date policy and snapshot-budget fixes.
- Full acceptance/documentation submission adds ten cases (448 total floor): all
  accepted gray/RGB 8/16-bit/contiguous/separate/alpha families and every addressed
  tag add/read/equivalent-copy/remove in both byte orders. Its own CI is required.
  Compression selector cases are accounting-only, not valid codec payload claims;
  4-GiB boundary cases use a pure planner model, not giant source I/O. No large
  fixture/cache/SDK artifacts committed. Complete usage/support/audit documents exist.
- Remaining: push/review final submission, observe required final-head all-platform
  and reference gates; fix failures without weakening policy. Record actual results,
  complete final status/docs, run final documentation-head gates, ready/merge PR #11
  only after success. Do not invent results or merge on predecessor evidence.
- No further M16 public API/strategy expansion is planned. After completed merge,
  M17 is DNG feasibility/contracts ONLY, not implementation. Its separate future
  implementation plan needs approval. M18 is measured optimization/packaging audit,
  not publication permission. The M16 prompt stops at its completed unit.

## Essential implementation and safety facts

- TiffMetadataDocument retains source identity/ownership; Layout is historical and
  source-relative keys never rebind. ReadValue/Set/Remove only the closed baseline
  descriptive subset. ExifValue remains the immutable primitive, not a general
  TIFF/Exif projection. Existing JPEG/PNG/WebP/embedded EXIF behavior is unchanged.
- Effective edits append a complete sorted active IFD graph through one old/new
  map. Remap next/SubIFD/Exif/GPS/Interop edges; shared targets stay shared. Copy
  original bytes except header 4–7 exactly, retaining original image/preview/value
  positions. Original opaque/private extensions refuse effective writes even if
  M15 accounting is supported. No directory creation/deletion or empty IFD output.
- **Old metadata remains physically present. No privacy scrubbing, erasure or
  compaction. Repeated edits grow files. New output only, never in-place append.**
- Whole length/SHA-256 before reads/saves; no-op still verifies identity. Captured
  finite budgets cover edit snapshot before copying plus original/staged graph,
  changed values/pointer arrays/tail, address/entry/value/active-range ceilings.
  Compatible unchanged external aliases count once. Logical charges are not CLR
  allocation measurements. Existing invalid target dates refuse read/replacement;
  unrelated edits preserve them, removal bypasses content but not framing.
- Async-only/short I/O, cancellation, source changes, partial output, temporary
  cleanup and race-created destination tests exist. Streams stay caller-owned;
  no concurrent mutation, filesystem alias defense or crash durability is promised.
- Mandatory reference harness exists and passed: four owned II/MM strip/tile
  original/edited/no-op/removed cases; Exiv2/ExifTool changed facts, reverse ingress
  and public-API rewrite, independent active IFD/range/prefix/image identity checks.
  It is not a broader codec decoder/conformance suite. No GPL source/tests imported.

## Files and recovery commands

Source: src/MetadataSharp.Formats/Tiff/{TiffMetadataEdit,TiffMetadataDocument,
TiffAppendPlanner,TiffLayout,TiffLayoutReader,TiffModels}.cs; shared ContainerIo.
Tests: tests/MetadataSharp.Tests/Tiff/{TiffEditingTests,TiffWriteAcceptanceTests,
StandaloneTiffTests,StandaloneTiffFixture}.cs. References: tools/MetadataSharp.ReferenceFixture/Program.cs,
tools/reference/harness.py, tests/reference/test_harness.py. Workflow emits bounded
failed-TRX annotations and enforces actual executed count, currently 448.

```sh
git status --short
git branch --show-current
git log -6 --oneline
python -m unittest discover -s tests/reference -q
git diff --check
gh run list --branch arena/d7ec6276-metadatasharp --limit 6
gh pr view 11 --json state,isDraft,headRefOid,statusCheckRollup
# On a machine with .NET 8:
dotnet restore MetadataSharp.sln
dotnet build MetadataSharp.sln --configuration Release --no-restore
dotnet test MetadataSharp.sln --configuration Release --no-build --no-restore
```

Local dotnet is absent. Use actual push-triggered Actions, not another SDK download
attempt. `gh workflow dispatch` was 403; push works. Remote logs/artifacts can EOF;
use check-run annotations, including the workflow's bounded TRX failure report.
Pinned Exiv2 0.27.6/ExifTool 12.76/dwebp 1.3.2 remain required; optional local skips
are not passes. Do not weaken assertions/warnings. Historical fixture collision
was fixed (external SubIFD array moved from 1024 to 1536), not a parser exception.
A PROGRESS update-script overwrite was fully restored from Git before continuation;
check document headers and diff statistics as well as whitespace on future updates.
