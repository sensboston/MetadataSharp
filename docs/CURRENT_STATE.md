# Current development state and recovery checkpoint

Updated 2026-11-08. Read first; verify against live branch/Git/PR/CI. Chronological
approvals/diagnostics remain in PROGRESS; older session instructions there are history.

## Active branch and scope

Assigned branch **arena/d4158be1-metadatasharp**, cut from master fa548e92
(PR #18 merge, which carried the parity master plan and E03 proposal). Never
switch to master or another branch. Standing authorization covers commit/push
and completed green PR merge, not package publication or unseen public contracts.
The previous public-visibility billing blocker is resolved in observed runs; no CI
exception applies.

Programme: roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md (E00–E18).
Developer approved independent near-complete Exiv2 functionality, idiomatic .NET,
.NET 8+10, conditional Framework only if cheap. No C++ hierarchy/CLI clone or GPL
implementation/tests/dictionaries imported. Rights/provenance, finite input limits,
unknown relocation refusal and independent metadata families stay binding.
2026-11-08: developer directed full Exiv2-level user-visible capability and
**avoiding external dependencies where possible**; binding dependency policy
(zero third-party runtime deps; BCL-only; no analyzers/natives/embedded tools)
is in DEVELOPMENT.md section 7 and EXIV2_PARITY_MASTER_PLAN.md section 2.

## Current evidence and actual implementation

- E00 and D0/E04 documentation PR #14 merged at 716c00b, final 74bd83e passed
  42445526473/42445522806. D0 exact contract explicitly APPROVED, not implemented.
  No need to ask that same approval again unless its promise changes. D1 still
  requires section 5 normative type/count/location/default ledger, owned valid
  fixtures/complementary independent oracles and E02/E03 compatibility gates.
- E02 code head **2949589c1908b9da80289670b89c062d03bc5af0** passed required
  [42446034373](https://github.com/sensboston/MetadataSharp/actions/runs/42446034373):
  **14 jobs green**: 450 .NET cases per six OS/TFM combinations, two required
  reference jobs and six target-specific baseline/package/consumer audit jobs.
- Core/Formats/tests/reference/audit tools target net8.0;net10.0; root global.json
  selects stable SDK10 feature band, C#12 unchanged. Tests assert actual host major
  and loaded test/library TFM, not merely SDK presence. Packages strictly require
  both DLL/XML sets and exact per-target dependencies. Consumers use isolated
  source/cache and own SDK-major global.json: SDK8.0.427 for net8, SDK10.0.403 for
  net10 observed on all OSes. No production parser/public-member changes.
- Public declared reflection signatures equal between runtimes and all six audit
  receipts (E02 receipt; predates the approved E03 additions implemented on this
  branch): 633 entries, hash a9cd2ae9fe864fbf39da5bc9fd4bfb32fa720f4afc024ac81b53cd1f3bdb93cb.
  NOT full attribute/default/nullable/ABI or behavioral compatibility proof.
  JSON/receipt: audits/evidence/e02; exact limitations/commands:
  audits/DOTNET_8_10_SUPPORT_AND_E02_AUDIT.md. Historical M18 evidence kept separate.
- Initial E02 failures were two old hardcoded-net8 tests and runtime-dependent
  empty-zlib test fixture. Made target expectations match executing framework and
  authored canonical valid empty zlib; extra test still rejects empty/truncated
  framing. No production BoundedZlib relaxation or case skipped. Floor now 450.
- Framework 4.8/4.8.1: defer/do not add; cost map did not establish "easy" support.
  No legacy spike/runtime verification or impossibility claim. No conditional
  parser fork/shims/native bridge/degraded async. Source builds need SDK10; net8
  package consumption with SDK8 is separately proven. .NET8 servicing still ends
  2026-11-10; .NET10 recommended for new consumers. No publication authorization.

## E01 state and next gates

Pinned Exiv2 v0.28.9 / b0859fc61fea5dedfc898aece6b15893ddf4314e remains discovery
baseline; required 0.27.6 CI references are historical regression oracles, NOT
0.28.9 parity proof. Metadata facts/selected issue/discussion requirements and
sources stay in docs/research. No source patches or camera attachments imported.

New E01 seed ledger: 186 documentation cells, 348 **candidate** operations, stable
IDs and deterministic generator. Read/Write does not prove each inferred suboperation;
no-claim cells are retained, not excluded. Every seed marks variants unsplit, CLI
unrun, rights unreviewed and MetadataSharp verification unassessed. Six policy tests
prevent fabricated support/exclusions/percentages. Full denominator/CLI build/
corpus/tag/layout/video/workflow census remains OPEN, no parity percentage claimed.

PR #16 bounded E01 CLI unit: implementation 601a20b passed 42449222307,
15/15 jobs including source-pinned external 0.28.9 build and four owned JPEG ASCII
Artist read/insert/replace/remove observations. Complete raw field/print/scan and
source/build/dependency identities in research/evidence/e01-pinned-jpeg; scope and
findings in research/EXIV2_PINNED_BASELINE_EXECUTION.md. Raw namespace catalogs and
upstream source/executable are NOT committed or distributed. Web/curl OFF in this
configuration is unobserved breadth, not an accepted exclusion. FINAL head
5b0783bf2a60844e82ab2a61479816d01b15d4b9 also passed both required final runs
(PR42449478931 and push42449474066, 15/15 each) and was merged as 75ad10d with the
final immutable source/run/merge receipts posted on PR #16. This bounded CLI
foundation is DONE; do NOT infer E01 census closure, E03 approval, D1 progress or
any parity/percentage from it.

Important: all THREE reference writes have false exact non-EXIF preservation flags:
D001 exact authored COM trailing-NUL addition/order; D002 single-property XMP
reserialization on replace/remove, independently checked against literal property.
These are recorded baseline differences, NOT successful safe-writing parity or
permission to modify our writer. Default/read checker remains strict; only exact
known differences classified, all other segment payloads/multiplicities remain
strict. MetadataSharp status unscored; full E01 census/denominator still OPEN.

Next open units (as of 2026-11-08):
1. **E03 APPROVED AND IMPLEMENTED (this branch)** — capability-discovery surface
   (`MetadataContainer`/`MetadataFamily`/`MetadataOperation`/`CapabilityStatus`/
   `CapabilityDescriptor`/`MetadataCapabilities`), pinned test table, test-local
   zero-`PackageReference` guard; net8.0/net10.0 retained per developer. Awaiting
   this PR's green CI + merge. Discharges only the E03 compatibility gate; it
   authorizes no per-format work, exclusions or publication.
2. **E01-CEN** — finite atomic denominator design and the split of the 31×6 grid
   into per-format execution batches; until the developer reviews the denominator,
   no parity percentage or "complete" claim is possible.
3. Atomic E01 breadth (per-format pinned CLI observations beyond JPEG EXIF Artist)
   may proceed only as batches defined by E01-CEN, with owned fixtures/rights review.
D0 approved; D1 normative/fixture/oracle prerequisites remain (E03 compatibility
gate now discharged pending merge). No other new public contracts, general format
support, exclusion, Framework or publication authorization is implied by the
2026-11-08 directive or this checkpoint. See
design/EXIV2_PARITY_MASTER_PLAN.md for the full gated subtask split.

## Reproduction / local constraints

```sh
git status --short
git branch --show-current
python -m unittest discover -s tests/reference -q
python -m unittest discover -s tests/discovery -q
python tools/reference/build_capability_seeds.py --check
git diff --check
gh pr view --json state,isDraft,headRefOid,statusCheckRollup,mergeCommit
gh run list --branch arena/d4158be1-metadatasharp --limit 4
# With both SDKs/runtimes installed:
dotnet restore MetadataSharp.sln
dotnet build MetadataSharp.sln -c Release --no-restore
dotnet test tests/MetadataSharp.Tests/MetadataSharp.Tests.csproj -c Release -f net8.0 --no-build --no-restore
dotnet test tests/MetadataSharp.Tests/MetadataSharp.Tests.csproj -c Release -f net10.0 --no-build --no-restore
```

Local dotnet absent; actual .NET evidence is Actions only. Do not retry unavailable
SDK/blob downloads. Bounded failed-TRX/command diagnostics and compressed complete
JSON annotations work; tools/audit/collect.py validates run/source/chunk receipts.
Audit AUDIT_TFM and reference METADATASHARP_REFERENCE_TFM default net8.0, accept
net10.0. Tools now need explicit -f for dotnet run; no target may silently fall back.
Python historical policy 12/12 and discovery/observation/collector 26/26 pass locally;
merged final-head CI for this checkpoint is PR42449478931 / push42449474066 (15/15).
