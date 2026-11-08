# Current development state and recovery checkpoint

Updated: 2026-11-07. Read first after interruption; verify against source/Git/live CI.
PROGRESS.md preserves chronological approvals, diagnostics and immutable evidence.

## Current grouped unit

**M17 DNG feasibility deliverable COMPLETE; future DNG implementation plan remains
UNAPPROVED. M18 bounded measured performance/package audit COMPLETE.** Developer
requested both remaining roadmap units in one pass/one PR. No public production API
or safety changes, DNG implementation, broad optimization or publication authorized.

Active assigned branch: `arena/b166bb08-metadatasharp`, based on M16 merge
`d476e37ef1b848df4556b7346f9858310470f4ee`. Work only on live assigned Arena branch;
never switch to master or reuse a merged historical session branch. One grouped
PR: https://github.com/sensboston/MetadataSharp/pull/12. Standing authorization covers
commits/push and merge after the whole final-head gate, not package publication.

## Durable deliverables and observed gates

- M00–M16 merged. M16 PR #11 confirmed merged at d476e37; final cbd3607 passed
  42436474402/42436471157 with 448 tests/platform, policy/references.
- M17: docs/design/DNG_SCOPE.md and docs/prompts/followups/DNG_PROPOSED_SEQUENCE.md.
  Source DNG 1.7.1.0 September 2023 mirror/hash pinned; dependency/risk/required
  contracts/exclusions/fixture rights/reference questions recorded. D0 → D1 → D2
  is proposed future work requiring approval, not code or an approved public API.
  Current TIFF DNG refusal remains unchanged. Exact M17 head
  1a05ffe23ee6e8b766a6db777705e509755bb79b passed 42439542366 BEFORE M18 tooling.
- M18: docs/audits/PERFORMANCE_AND_PACKAGING_AUDIT.md, evidence/m18 JSON/receipt,
  tools/MetadataSharp.Audit, tools/audit/run.py and collect.py. Eight authored-input
  public-API workloads, five measured batches, throughput/process-wide allocations,
  hashes/environment; opt-in local 0.0.0-audit packages, manifests/XML/MIT/deps and
  isolated offline consumer on Linux/Windows/macOS. Normal IsPackable stays false.
- Audited/recovered evidence head **4d89a00e016d32845bd44395bba7f0076dcc1217** passed
  required [42439984148](https://github.com/sensboston/MetadataSharp/actions/runs/42439984148):
  **all seven jobs green**, including 448 .NET tests per OS, Python policy 12/12,
  required pinned references and all three measurement/package/consumer jobs.
- Package XML member counts 45 Core / 19 Formats are NOT full API documentation
  coverage. No external production package deps except Formats → Core; framework
  runtime still needed. No shipping CLI/native/fixture/PDF/GPL payloads. No broad
  conformance, full-size I/O, universal speed, zero-allocation or release claim.
- Final documentation/evidence commit still must pass its own seven-job CI before
  readying/merging PR #12. Record immutable final SHA/run IDs/merge receipt in PR
  after actual success (avoid circular self-recording). On recovery check live PR:
  if MERGED, all M00–M18 roadmap units are complete within their bounded definitions;
  if OPEN, finish only remaining required gates/merge or fix actual failures.

## Next eligible work after grouped merge

No roadmap implementation milestone remains. Ask developer to approve proposed DNG
D0/D1/D2 sequence before any future DNG work; exact contracts need separate approval
before code. Release hardening (API docs, version/support/signing/SourceLink/SBOM/
advisory checks and publication decision) remains separate. Do not publish packages
or implement DNG merely because M17/M18 or their PR completed. M16 still retains
obsolete bytes: no erasure/privacy/compaction, no generic opaque-pointer safety.

## Recovery commands and environment

```sh
git status --short
git branch --show-current
git log -6 --oneline
python -m unittest discover -s tests/reference -q
python -m py_compile tools/audit/*.py
git diff --check
gh pr view 12 --json state,isDraft,headRefOid,statusCheckRollup,mergeCommit
gh run list --branch arena/b166bb08-metadatasharp --limit 6
# On .NET 8 SDK machine (absent in local Arena):
python tools/audit/run.py
dotnet test MetadataSharp.sln --configuration Release
```

Do not retry unavailable SDK/blob downloads or workflow dispatch (403 previously).
Push-triggered Actions work. Failed TRX and bounded command/assert diagnostics are
check annotations. Audit JSON annotations use compressed <=2500-byte chunks because
GitHub truncates messages at 4096; collector validates complete sets/run/source.
First audit package assertion correctly caught absent XML due late MSBuild property
setup; fixed by early opt-in props, not weakening checks. All artifacts stay ignored;
only small measured JSON/receipt belong in Git. Required references use Exiv2 0.27.6,
ExifTool 12.76 and dwebp 1.3.2; warnings/skips must not be weakened to obtain success.
