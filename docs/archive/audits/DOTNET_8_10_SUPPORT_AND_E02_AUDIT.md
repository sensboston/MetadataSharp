# E02: .NET 8 and .NET 10 support and verification

## Decision and boundaries

Developer approved .NET 8 plus .NET 10 LTS programme direction. Core, Formats,
tests and authored fixture/audit tools now target **net8.0;net10.0**, built with
SDK 10 (global.json minimum 10.0.100, latest installed stable feature band) and
C# 12. No production parser, writer, public member contract, native/runtime package
dependency or safety policy changed. Source builds need SDK 10; a net8.0 packaged
consumer can still build with SDK 8, actually checked below. Normal projects remain
non-packable; 0.0.0-audit candidates are local inspection artifacts, not publication.

.NET 8 servicing ends 2026-11-10; .NET 10 LTS support ends 2028-11-14 per pinned
official lifecycle metadata in the programme. A net8.0 asset does not extend
Microsoft runtime security servicing. Recommend .NET 10 for new deployments.
No support for every runtime OS/distro/architecture is inferred from hosted CI:
Windows/Linux X64 and macOS Arm64 in these runs are observed functional evidence.
No NativeAOT, trimming, mobile, WASM, Linux Arm64 or Framework runtime claim.

Framework 4.8/4.8.1 decision for this unit: **defer / do not add**. The recorded
source-level cost map (DOTNET_TARGET_FEASIBILITY.md) does not meet the developer's
"only if easy" condition: modern guard, span/stream, disposal/async, crypto and zlib
surfaces need explicit compatibility work. No legacy compiler/runtime spike was
run, so this is not a technical impossibility claim. No duplicated engine, native
bridge, unreviewed shims or degraded async/safety fallback. Further legacy work
would need a separately bounded justification; it must not block modern targets.

## Actual code/target verification

Validated implementation head **2949589c1908b9da80289670b89c062d03bc5af0** passed
required [42446034373](https://github.com/sensboston/MetadataSharp/actions/runs/42446034373):
**all 14 jobs green**:

- 450 .NET cases in **each** Windows/Linux/macOS × net8.0/net10.0 combination
  (2700 executions, not 2700 distinct authored tests). Actual runtime major and
  test/Core/Formats TargetFrameworkAttribute are checked, not inferred from SDK.
- Required pinned Exiv2 0.27.6 / ExifTool 12.76 / dwebp reference facts under BOTH
  fixture-generator TFMs. These are existing regression oracles, not Exiv2 0.28.9
  parity verification or a complete new-format corpus.
- Six OS/TFM audit jobs run target-specific authored workloads and strict package
  inspection. Both Core/Formats packages must contain exactly their two expected
  DLL assets, XML/readme/exact MIT license and exact target dependency groups.
  Core has zero package dependencies per TFM; Formats only Core 0.0.0-audit per TFM.
- Every isolated package-only consumer uses a cleared local feed/cache and its own
  global.json. net8 consumer selected SDK **8.0.427**, net10 SDK **10.0.403** in all
  three OS runs; actual runtime major checked in the edit/reopen consumer. Package
  restore cannot silently use source-project references or public package feeds.
- Both runtimes enumerate declared public type/member signatures. All six receipts
  have **633 entries**, SHA-256
  `a9cd2ae9fe864fbf39da5bc9fd4bfb32fa720f4afc024ac81b53cd1f3bdb93cb`.
  This is signature equality, NOT a full metadata/nullable/default-value/attribute,
  ABI, behavioral compatibility or precompiled-client certification. Existing
  independent behavior/refusal/preservation tests remain mandatory separately.

Full target-specific JSON and validated retrieval receipt are in
[evidence/e02](evidence/e02/). Collector supports historical M18 single-target
receipts and current six-target receipts; complete annotation chunks/run/source
identity are verified. No artifact binary downloads or test/reference code imported.
Measurements are a NEW baseline, not claimed performance improvements over M18:
runner load/CPU/SDK/native compression and generated input hashes can differ.
No optimization/retained-heap/zero-allocation/security proof claimed.

## Diagnosed first-run failures (not hidden as passes)

Initial a055140/run 42445858808 passed audits/references and net8 tests, but net10
failed two old tests hardcoding net8 target identity plus one empty zlib fixture.
The fixture depended on runtime compressor initialization for a zero-length write.
Authored an explicit valid empty RFC1950/1951 stream; added a regression that missing
DEFLATE framing / zero bytes still refuse. Changed infrastructure target expectation
to the actual test framework, retaining explicit host/asset identity checks.
**No production BoundedZlib relaxation, skipped case or weaker error expectation.**
Floor is now 450 per target; Python reference policy remains 12/12. Additional six
E01 seed-policy tests are added in the final submission and require that head's CI.
Local SDK absent; all .NET/runtime/package execution above is observed Actions,
not a local build. Local Python/diff checks pass. Final PR #15 head requires its own
14-job CI; exact final SHA/run/merge receipt is posted there after success.

## Reproduction

Install stable SDKs and runtimes for BOTH .NET 8 and .NET 10. global.json selects
SDK 10 for source; package consumers deliberately select the matching SDK separately.

```sh
dotnet restore MetadataSharp.sln
dotnet build MetadataSharp.sln -c Release --no-restore
dotnet test tests/MetadataSharp.Tests/MetadataSharp.Tests.csproj -c Release -f net8.0 --no-build --no-restore
dotnet test tests/MetadataSharp.Tests/MetadataSharp.Tests.csproj -c Release -f net10.0 --no-build --no-restore
dotnet run --project tools/MetadataSharp.ReferenceFixture -c Release -f net8.0 -- generate artifacts/fixtures
python -m unittest discover -s tests/reference -v
python -m unittest discover -s tests/discovery -v
python tools/reference/build_capability_seeds.py --check
# Audit defaults to net8.0; set AUDIT_TFM=net10.0 for the second run using your shell:
python tools/audit/run.py
# Reference harness defaults to net8.0; set METADATASHARP_REFERENCE_TFM=net10.0 for the second run:
python tools/reference/harness.py --required
```

Audits write artifacts/audit/<tfm>/ and emit complete compressed JSON annotations.
`python tools/audit/collect.py RUN_ID DESTINATION` recovers small receipts. Historical
M18 evidence must not be overwritten/reinterpreted as .NET 10 data. D0 remains
approved contract-only; E03 compatibility proposal and D1 normative/oracle gates
still not complete. E02 runtime support does not authorize a new public API/refactor.
