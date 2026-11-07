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

The suite now defines 155 cases through M06, covering infrastructure, binary
primitives, raw values, TIFF/EXIF reading, layout and serialization. The workflow
rejects fewer than 155 passing cases or any nonpassing cases. JPEG containers and
file editing are not implemented/tested yet.

## Project Boundaries

- `src/MetadataSharp.Core`: binary/raw primitives and internal EXIF reader/planner/serializer; no runtime NuGet dependencies.
- `src/MetadataSharp.Formats`: empty SDK library referencing Core.
- `tests/MetadataSharp.Tests`: xUnit tests referencing both libraries.
- `Directory.Build.props`: shared language/target/nullable settings, deterministic
  output, warnings as errors, unsafe disabled, and packaging disabled by default.

SDK-generated assembly attributes let the still-empty Formats project compile
without dummy classes. One test project currently suffices; future separation is possible
when there are actual format and external-tool tests. No coverage collector or
external metadata tool is needed for this stage. `bin`, `obj`, test results and
local IDE files are ignored. These projects are not ready for NuGet publication.

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
above. A separate TRX check rejects a green run with no tests or fewer than 155
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
