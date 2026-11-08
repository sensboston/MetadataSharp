# Current development state and recovery checkpoint

Updated 2026-11-07. Read first; verify against live branch/Git/PR/CI. Chronological
approvals/diagnostics remain in PROGRESS; older session instructions there are history.

## Active branch and scope

Assigned branch **arena/49d49a4f-metadatasharp**, from PR #15 merge
7c872466c705c7d6877c8e41018df217d4cbd8db. Never switch to master or another branch.
PR #15 is MERGED; E02 is complete (final 85bd097, PR42446407601 and
push42446403212 both 14/14). Active unit: E01 pinned black-box CLI observations. Standing authorization covers commit/push and completed green PR
merge, not package publication or unseen public contracts. The previous public-
visibility billing blocker is resolved in observed runs; no CI exception applies.

Programme: roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md (E00–E18).
Developer approved independent near-complete Exiv2 functionality, idiomatic .NET,
.NET 8+10, conditional Framework only if cheap. No C++ hierarchy/CLI clone or GPL
implementation/tests/dictionaries imported. Rights/provenance, finite input limits,
unknown relocation refusal and independent metadata families stay binding.

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
  receipts: 633 entries, hash a9cd2ae9fe864fbf39da5bc9fd4bfb32fa720f4afc024ac81b53cd1f3bdb93cb.
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
configuration is unobserved breadth, not an accepted exclusion.

Important: all THREE reference writes have false exact non-EXIF preservation flags:
D001 exact authored COM trailing-NUL addition/order; D002 single-property XMP
reserialization on replace/remove, independently checked against literal property.
These are recorded baseline differences, NOT successful safe-writing parity or
permission to modify our writer. Default/read checker remains strict; only exact
known differences classified, all other segment payloads/multiplicities remain
strict. MetadataSharp status unscored; full E01 census/denominator still OPEN.

Final PR #16 head needs own FIFTEEN-job CI before merge; final immutable source/run/
merge receipt posted there. On recovery check live PR #16. If merged, this bounded
CLI foundation is done; do NOT infer E01/E03/full parity/D1 completed. Next expand
atomic ledger by family/format/variant and separately prepare exact E03 compatibility
proposal. D0 already approved, D1 normative/fixture/oracle/E03 prerequisites remain.
No production DLL/API/parser dependency changes, general format support, exclusion,
Framework or publication authorization introduced in this unit.

## Reproduction / local constraints

```sh
git status --short
git branch --show-current
python -m unittest discover -s tests/reference -q
python -m unittest discover -s tests/discovery -q
python tools/reference/build_capability_seeds.py --check
git diff --check
gh pr view 16 --json state,isDraft,headRefOid,statusCheckRollup,mergeCommit
gh run list --branch arena/49d49a4f-metadatasharp --limit 4
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
own final fifteen-job CI still required.
