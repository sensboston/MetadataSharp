# Building and Testing MetadataSharp

## Prerequisites and SDK Selection

Install a supported .NET 8 SDK in your development environment. `global.json` sets
8.0.100 as the minimum feature band and allows the latest installed stable .NET 8
feature band/patch (`latestFeature`). It does not roll forward to .NET 9 or 10 and
does not install an SDK. Prefer a serviced .NET 8 SDK; this is not a request to
install the original 8.0.100 release. C# is fixed to version 12.0 and targets net8.0.

Run all commands from the repository root. Initial restore needs access to NuGet
or an appropriately populated local package cache. Dependency versions are explicit,
but no resolved dependency lock file is claimed until a real restore has run.

## Validation Commands

```sh
dotnet --info
dotnet restore MetadataSharp.sln
dotnet build MetadataSharp.sln --configuration Release --no-restore
dotnet test MetadataSharp.sln --configuration Release --no-build --no-restore
```

The last completed M15 gate passed 392 cases per platform (see CURRENT_STATE.md), covering infrastructure, binary
primitives, raw values, TIFF/EXIF reading, layout, serialization, JPEG EXIF/XMP scanning and editing, and XMP packets/sidecars, PNG/WebP and standalone read-only TIFF. The workflow
rejects fewer than 309 passing cases or any nonpassing cases. Twelve independent Python policy/execution tests also run on each OS.

## Project Boundaries

- `src/MetadataSharp.Core`: binary/raw primitives and internal EXIF reader/planner/serializer; no runtime NuGet dependencies.
- `src/MetadataSharp.Formats`: JPEG scanner and EXIF editing facade referencing Core.
- `tests/MetadataSharp.Tests`: xUnit tests referencing both libraries.
- `Directory.Build.props`: shared language/target/nullable settings, deterministic
  output, warnings as errors, unsafe disabled, and packaging disabled by default.

The public-API fixture generator is `tools/MetadataSharp.ReferenceFixture`. Core
and Formats have no runtime NuGet dependencies. Production code does not launch
external tools. Generated `bin`, `obj`, test results, harness output and Python
caches are ignored; packaging is disabled.

## Current Verification Status

The local Arena environment has no `dotnet` command. This no longer blocks the
M01 gate: [GitHub Actions run 42416545213](https://github.com/sensboston/MetadataSharp/actions/runs/42416545213)
validated commit `e72ce881dd5579b34d1dd36f335df58be8e3f299` on 2026-11-07.
Linux, Windows and macOS jobs all passed restore, Release build, tests and the TRX
counter check. The suite contains two infrastructure theory cases. This establishes
only the M01 skeleton, not metadata correctness or untested platform variants.


## GitHub Actions Validation

`.github/workflows/build-and-test.yml` runs on pushes to master/Arena branches and
pull requests targeting master. It installs a serviced .NET 8 SDK on Linux, Windows
and macOS runners, records `dotnet --info`, and runs the restore/build/test commands
above. A separate TRX check rejects a green run with no tests or fewer than 309
passing cases. Test results are retained as per-platform artifacts for seven days.
CI needs only read access to repository contents; no SDK archive or release asset
is required. Hosted runners have their own network environment, independent of
Arena's local SDK download restrictions. The run conclusion and logs, not workflow
creation alone, determine whether the M01 gate is satisfied.

## M02 and M03 Validation

- [M02 run 42417202024](https://github.com/sensboston/MetadataSharp/actions/runs/42417202024)
  validated commit `97f87df97a55caf528c77dcbddeebcc5c447c587`.
- [M03 run 42417333330](https://github.com/sensboston/MetadataSharp/actions/runs/42417333330)
  validated commit `ca0f991066c0e7322decbe9ee88dd6f743aaa296`.

Each passed restore, Release build and tests on Linux, Windows and macOS. The
latest workflow raises the TRX floor to the full authored M01–M03 case count before
merge. These tests validate bounded primitives and raw ownership, not TIFF/JPEG
conformance, vendor metadata relocation or file saving.

## M04–M06 Validation

| Unit | Validated implementation commit | Three-platform PR run |
| --- | --- | --- |
| M04 | `ac9c9e87d21941f66ffa295b85c63eee776eddd3` | [42418170361](https://github.com/sensboston/MetadataSharp/actions/runs/42418170361) |
| M05 | `976983b9f36fddde9d0ab3eb558d4de9ea65e4e9` | [42418449461](https://github.com/sensboston/MetadataSharp/actions/runs/42418449461) |
| M06 | `067bfed16de2ebd3d8e0d982a73d33e87ea9a706` | [42418674786](https://github.com/sensboston/MetadataSharp/actions/runs/42418674786) |

All passed restore, Release build, tests and TRX count checks. No local SDK run,
benchmark, external-tool validation or broader format-conformance claim is implied.

## M07–M09 Validation and External References

M07 passed run 42420076893; M08 passed 42420352483; corrected M09 passed
[42420756667](https://github.com/sensboston/MetadataSharp/actions/runs/42420756667).
The current TRX floor is 309. M09 additionally requires Exiv2 0.27.6 and ExifTool
12.76 in a separate Ubuntu 24.04 job; absent tools cannot silently skip CI.

```sh
python -m unittest discover -s tests/reference -v
dotnet build MetadataSharp.sln --configuration Release
python tools/reference/harness.py --required --output artifacts/reference
```

See the [audit](audits/INITIAL_JPEG_RELEASE_AUDIT.md) for exact provisioning,
provenance, commands/evidence, warning classification and remaining release gates.
Without `--required`, local missing tools produce an explicit skipped report.
The local Arena checkout still has no SDK; actual .NET/reference execution was
performed by Actions, not claimed as a local run. Historical sections above retain
earlier milestone evidence, not current coverage boundaries.

## M10–M11 XMP Validation

M10 commit b37ce98 passed three-platform PR run
[42422179802](https://github.com/sensboston/MetadataSharp/actions/runs/42422179802)
with a 233-case floor. M11 corrected/audited commit 1e25408 passed
[42422570403](https://github.com/sensboston/MetadataSharp/actions/runs/42422570403)
with a 259-case floor and ten Python policy tests. The required external job read
our XMP with both tools, read reverse-modified owned JPEGs, verified exact no-op and
encoded-byte preservation, checked rich sidecar values, and exercised all three
65,502–65,504-byte boundaries. No local SDK run is claimed.

Final harness hardening additionally applies ExifTool validation/warning review to
standard XMP JPEGs, rich sidecars and boundary files, using the same strict existing
warning classification. Evidence manifests now include owned .xmp outputs as well
as JPEGs. See [XMP usage](design/XMP_USAGE_AND_PRESERVATION_LIMITS.md) and the
[normative review](design/XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md).

Wrapper-corrected commit 2b760c6 passed PR run
[42422888266](https://github.com/sensboston/MetadataSharp/actions/runs/42422888266),
including the 260-case floor and full ExifTool validation of XMP JPEG/sidecar/boundary
fixtures. The missing-wrapper warning was eliminated by emitting standard PIs;
no warning whitelist was broadened. The original minimal-EXIF limitation remains.

## M12 IPTC/Photoshop Validation

M12a corrected head f45a96b passed PR run
[42425723428](https://github.com/sensboston/MetadataSharp/actions/runs/42425723428)
with a 288-case floor before M12b began. M12b first head 5c2376b passed
[42426038001](https://github.com/sensboston/MetadataSharp/actions/runs/42426038001)
with a 304-case floor. Audited head f19471e passed
[42426187721](https://github.com/sensboston/MetadataSharp/actions/runs/42426187721)
with a 309-case floor. All included Linux/macOS/Windows Release build/test and
required pinned external-reference checks; Python policy coverage is now twelve tests.

Owned IPTC fixtures exercise UTF-8/repeated datasets, exact no-op, resource-preserving
removal, existing digest refresh, reverse CLI edits and full APP13 capacity. Both
Exiv2 0.27.6 and ExifTool 12.76 passed their factual checks; explicit ExifTool
validation ran on normal and boundary IPTC JPEGs. No new warning whitelist was
introduced. The known initial minimal-EXIF XResolution limitation remains.
A deterministic 256-iteration mutation smoke checked bounded IIM/IRB error paths;
this is not a general fuzzing campaign or proof of security. Local dotnet remains
unavailable; actual .NET/reference execution was in Actions, not locally claimed.

See [IPTC usage](design/IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md) and the
[normative review](design/IPTC_IIM_AND_PHOTOSHOP_NORMATIVE_REVIEW.md).

## M15–M16 standalone TIFF validation

M15 final 812bd76 passed runs 42431094052/42431090942 (392 tests/platform).
M16 range-audited 600bbc1 passed required 42435227396 (405/platform), then expanded
0dd3836 passed required 42435965943 and push 42435961970 (435/platform). All include
Python policy 12/12 and pinned external references; do not substitute predecessor
success for final-head validation. The submitted full M01–M16 floor is now 448.
Actual final-head evidence/merge is recorded in CURRENT_STATE/PROGRESS and PR #11.

The same restore/Release build/test commands apply; required reference generation
now includes standalone TIFF original/edited/no-op/removed II/MM strip/tile files,
reverse ingress/rewrite and independent directory/range/prefix/image identity
checks. No optional-tool skip counts as a required pass. Bounded failed-TRX details
are emitted as Actions annotations if log/artifact downloads are unavailable.
See the [write audit](audits/STANDALONE_TIFF_EDITING_AUDIT.md) for test classifications:
synthetic compression accounting and pure 4-GiB planner boundaries are not claims
of decoded compression validity or full-size source I/O. Local SDK still absent.

Full M16 acceptance/documentation head 762dc206915e7989721645e0ee3a8adb479310c2
passed required run [42436352761](https://github.com/sensboston/MetadataSharp/actions/runs/42436352761):
448 tests on each Windows/Linux/macOS, Python 12/12 and mandatory references.
Final documentation-head checks and merge receipt are recorded on PR #11 after
success. No local .NET, full 4-GiB I/O, benchmark or publication claim is implied.

## M17–M18 feasibility and measured package audit

M17 documentation head 1a05ffe passed required 42439542366 before M18 tooling.
M18 tooling/evidence head 4d89a00 passed required 42439984148: seven jobs, 448 .NET
tests per Windows/Linux/macOS, Python policy 12/12, mandatory references, and
baseline/local-package/offline-consumer audits per OS. No production API changed.

```sh
python tools/audit/run.py
# Recover complete small evidence through configured gh (no blob download):
python tools/audit/collect.py 42439984148 artifacts/recovered-audit
```

See [M18 audit](audits/PERFORMANCE_AND_PACKAGING_AUDIT.md) for methodology, exact
measured JSON, SDK/architecture context, package/license/dependency/API docs gaps
and unexecuted checks. Audit-only opt-in IsPackable/0.0.0-audit properties never
permit publication; normal builds are non-packable. Local SDK is absent. Final PR
#12 documentation head requires its own full CI before merge; receipt goes in PR.

## E02 modern multi-target verification (supersedes net8-only instructions)

Source SDK selection is now 10.0.100/latest stable feature band; retain SDK/runtime
8 and 10 to execute both targets. Libraries/tests/tools target net8.0 and net10.0;
`dotnet run` for those projects now requires `--framework net8.0` or `net10.0`.

Validated 2949589/run42446034373: all 14 jobs, 450 .NET cases per six OS/TFM
combinations, both references and six audits. Exact commands/support/evidence:
[audit](audits/DOTNET_8_10_SUPPORT_AND_E02_AUDIT.md). Historical SDK8-only/single-TFM
commands describe older commits, not current source builds. Reference harness
selects its fixture DLL with METADATASHARP_REFERENCE_TFM (default net8.0); audit
selects with AUDIT_TFM (default net8.0). Normal IsPackable remains false. No publish.
Final discovery/documentation head requires own CI; receipt recorded in PR #15.

## E01 pinned baseline job

CI then had **fifteen** jobs: the fourteen E02 checks, plus required
`Exiv2 0.28.9 / bounded E01 observations`; `differential-parity` at the end of this file is the
sixteenth (matrix rows are counted separately, so `gh pr checks` shows more). It builds exact pinned upstream source
externally, never links native/GPL code into production DLLs/packages, and does not
replace required historical 0.27.6 references. No upstream tests/fixtures imported.
See [scope, commands and preservation findings](research/EXIV2_PINNED_BASELINE_EXECUTION.md).
Successful observation protocol includes visible D001/D002 reference differences;
it is NOT a blanket successful safe-write/parity assertion. Full source-bound small
JSON can be recovered with `python tools/reference/collect_baseline.py RUN_ID DEST`.
Python discovery now runs 26 cases, historical policy 12; .NET floor remains 450/TFM.

## Differential parity job

`differential-parity` (Ubuntu, added with the harness) measures our readers against exiv2 0.28.9's
own expectations: the workflow checks out `b0859fc61fea5dedfc898aece6b15893ddf4314e` into
`artifacts/parity/exiv2` and reads its `test/data` samples plus the `-Pkycvt` reference text beside
them, then runs `tools/parity/run_parity.py`, which reports key and value parity per file, per format
and per tag family. That corpus is CI-time input only - nothing upstream is vendored, translated,
committed or executed in the first phase, and `tools/parity` never interprets upstream's `tests/*.py`
(their expectations are macro tokens their own driver assembles). The second phase builds the pinned
CLI so the fixtures this repository generates get an oracle from the same commit;
`artifacts/parity/*/parity-report.{txt,json}` is uploaded as `differential-parity`.

Parity numbers do not fail a build, by design: `run_parity.py` exits 1 only when the run itself is
unsound - an unreadable dump block, a missing block, a reader that threw instead of refusing, or a
damaged expectation file. The offline rules of the same harness run as a normal gate step
(`python -m unittest discover -s tests/parity`, 45 cases, standard library only). The dump tool
`tools/MetadataSharp.Dump` is restricted to the public API by construction, so what the harness can
see is what a consumer can read; a tag the library cannot name is printed as a numeric key, which is
the gap 4.2 closes.
