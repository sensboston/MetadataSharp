# M18: bounded performance and packaging audit

Scope authorized in the grouped M17–M18 request. This is a measurement/inspection
unit, not an optimization, release or publication authorization. Production public
APIs and safety policies are unchanged. Actual CI evidence/observations will be
recorded below after successful runs; predecessor tests do not validate audit tools.

## Reproduction and measurement interpretation

On a .NET 8 SDK machine, from repository root:

```sh
python tools/audit/run.py
python -m unittest discover -s tests/reference -v
dotnet test MetadataSharp.sln --configuration Release
```

`run.py` builds the separate non-packable MetadataSharp.Audit tool, measures eight
public-API workloads, packs two opt-in 0.0.0-audit local candidates, inspects their
ZIP/nuspec/XML contents and runs a separate consumer restored from a cleared-source
local feed with an isolated package cache. No NuGet publish command exists in the
audit. Normal project builds retain IsPackable=false. Packages/binaries/cache/input
files stay in ignored artifacts; small measured JSON is retained as CI artifacts
and check annotations, then selected baseline receipts are committed under evidence.

Inputs link the repository's MIT-authored JPEG/PNG/WebP/TIFF test generators. JPEG
is the small authored baseline grayscale stream; PNG one RGB pixel; WebP the owned
static fixture; TIFF 16x16 uncompressed gray at 4096 bytes, plus a 1-MiB variant
with zero unreferenced padding (not a 1-MiB photograph). XMP constructs a supported
CreatorTool property from an empty model. Report hashes identify exact input bytes.
These are supported representative micro-workloads, NOT a camera corpus, entropy
benchmark, malformed-input campaign or general TIFF/DNG conformance proof.

Each workload has ten warmups after a first checked result, then five batches of
100 operations (20 for the 1-MiB TIFF). Every result length/count is checked for
stability. Stopwatch timestamps measure elapsed time; process-wide
GC.GetTotalAllocatedBytes(true) differences measure allocation including document,
model and output-buffer allocation, not retained heap/peak working set. Collections
between batches reduce uncontrolled carryover but produce an artificial GC regime.
This is not BenchmarkDotNet, a statistically controlled latency study or a universal
allocation/performance guarantee. Tiered JIT, CPU scaling/background runner load and
process-wide counters can affect results. Use medians and sample spread, not one
fastest sample. Runtime/OS/architecture/CPU count/GC/SDK/source SHA are captured.

Ops/s is the primary throughput metric. Nominal input MiB/s divides declared input
size by time, not physical read traffic: source-identity checking and copying can
scan TIFF repeatedly. XMP's empty-model input has zero input throughput by design.
MemoryStream outputs include their allocation/growth costs. No disk, async,
cancellation latency, network, NativeAOT, trimming, single-file, mobile or huge-file
baseline is implied. Hosted OS/CPU architectures differ; cross-platform results
are functional evidence, not an OS performance ranking. No optimization proposed
without a separately reviewed profiling/comparison experiment (at most one follow-up).

## Package/dependency/license/API documentation checks

- Candidate Core contains its managed net8.0 DLL/XML, README and exact MIT LICENSE;
  Forms contains its own DLL/XML/README/LICENSE and only the Core package dependency.
  Nuspec license is MIT expression. Explicit author/copyright follow repository
  license (SeNS). Version 0.0.0-audit is an inspection marker, not release selection.
- Production csproj files contain no external PackageReference. .NET 8 framework
  dependencies remain required; this is not standalone runtime packaging. Tests
  use their existing test-only packages; benchmark links authored generators but
  no test assembly/GPL CLI/codec/PDF/camera fixture is packed. Existing encoder
  fixture notices stay under tests and are not required for library DLL content.
- Strict audit assertions reject missing docs/license/readme, unexpected DLL/native
  executables/source/image/PDF contents or unexpected package dependencies. Isolated
  package-only consumer edits/reopens an owned TIFF; it cannot silently use project
  references or online feeds. It is one smoke test, not exhaustive public API usage.
- XML member counts are evidence that documented summaries ship, NOT percentage
  coverage. Existing public APIs still have documentation gaps (e.g. many concrete
  TiffMetadataDocument methods lack member summaries). CS1591 is suppressed ONLY for
  opt-in audit packaging; full API documentation/review remains a release gate.
- No shipping package configuration or package upload was approved. Before release:
  select versions/TFMs/support policy, finish public XML docs/examples, review release
  notes/notice attribution, inspect symbols/SourceLink and reproducibility/signing/
  SBOM needs, audit dependencies and vulnerabilities against current advisories,
  and obtain explicit publication authorization. No vulnerability scanner, legal
  opinion, SDK patent clearance, bitwise cross-OS reproducibility or signing claim.

## Actual execution record

Validated tooling/evidence head **4d89a00e016d32845bd44395bba7f0076dcc1217** passed
required [42439984148](https://github.com/sensboston/MetadataSharp/actions/runs/42439984148):
all seven jobs green. Correctness **448 tests per Windows/Linux/macOS**, Python
policy **12/12**, mandatory pinned external references, and three platform-specific
baseline/package/offline-consumer jobs passed. Earlier fixed 13bdcca also passed
42439833467. Original failures/diagnosis are preserved in PROGRESS, not counted as
passes. Local SDK is absent; no local .NET execution is claimed.

Full small JSON reports plus retrieval receipt live in [evidence/m18](evidence/m18/).
Collected with `python tools/audit/collect.py 42439984148 docs/audits/evidence/m18`:
GitHub API check IDs, run/head identity, complete chunk sets and source SHA validated;
receipt contains raw compact JSON SHA-256. No logs/artifact binary download was
needed. Individual GitHub annotations truncate at 4096 bytes, so runner uses
zlib/base64 chunks <=2500 bytes; artifacts retain ordinary JSON as well.

All measured processes used .NET **8.0.33**, SDK **8.0.427**, workstation GC. Ubuntu
and Windows were X64; macOS Arm64. Full environment/processor counts and sample
spreads are in JSON. The SDK host can be a newer runtime while the net8.0 workload
runs on 8.0.33; these are different observations, not a portability guarantee.

### Observed medians (five batches; rounded)

| Platform | Workload | Operations/s | Process allocated bytes/op |
| --- | --- | ---: | ---: |
| ubuntu | jpeg-open | 161,499 | 1,402 |
| ubuntu | jpeg-exif-save | 74,102 | 5,285 |
| ubuntu | png-exif-save | 65,651 | 5,973 |
| ubuntu | webp-exif-save | 75,233 | 5,877 |
| ubuntu | tiff-layout-4k | 73,094 | 9,256 |
| ubuntu | tiff-edit-4k | 17,274 | 28,221 |
| ubuntu | tiff-edit-1m | 319 | 4,146,248 |
| ubuntu | xmp-create-apply | 9,920 | 88,836 |
| windows | jpeg-open | 101,595 | 1,394 |
| windows | jpeg-exif-save | 27,832 | 5,269 |
| windows | png-exif-save | 25,477 | 5,957 |
| windows | webp-exif-save | 28,404 | 5,861 |
| windows | tiff-layout-4k | 34,750 | 9,256 |
| windows | tiff-edit-4k | 10,682 | 28,202 |
| windows | tiff-edit-1m | 231 | 4,146,176 |
| windows | xmp-create-apply | 5,637 | 88,832 |
| macos | jpeg-open | 201,005 | 1,389 |
| macos | jpeg-exif-save | 93,336 | 5,256 |
| macos | png-exif-save | 80,775 | 5,944 |
| macos | webp-exif-save | 102,428 | 5,848 |
| macos | tiff-layout-4k | 84,303 | 9,259 |
| macos | tiff-edit-4k | 32,386 | 28,192 |
| macos | tiff-edit-1m | 582 | 4,146,605 |
| macos | xmp-create-apply | 19,384 | 88,858 |

The padded TIFF case allocates about 4.15 MB/op in this open+edit+MemoryStream
workload. XMP create+apply allocates about 89 KB/op. These are observed process
allocation costs, not retained metadata size or evidence of an exploitable leak.
No performance improvement is claimed and no production optimization was made.
A representative real-file/metadata-rich profile is needed before selecting even
one optimization. No IPTC-specific, large XMP, DNG, asynchronous or disk benchmark
is included. These coverage gaps do not become measured results by analogy.

All three package audits reported **45 Core and 19 Formats XML member entries**;
this confirms shipping the existing summaries, not full documentation coverage.
Core has zero package dependencies; Formats has exactly Core 0.0.0-audit. Both
candidate archives contained the required managed DLL/XML/README/exact MIT license
and no CLI/native/image/source/PDF payload. Offline restore/consumer passed on all
three OSes. Per-archive hashes and manifests are retained; cross-platform package
hash equality is neither required nor claimed (pack metadata/build paths can vary).

M18's bounded audit gate is complete; release-hardening/documentation gaps above
remain explicit follow-up work, not permission to publish. Final grouped PR #12
head must pass all seven required jobs before merge; actual final-head run IDs and
merge receipt are recorded in the PR to avoid self-referential commit recording.
