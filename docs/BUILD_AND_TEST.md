# Building and Testing the M01 Skeleton

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

A successful run should discover two xUnit theory cases that load the Core and
Formats project assemblies and check their net8.0 target framework attributes.
This checks project-reference output availability, test discovery and runtime
loading without inventing a public library API. It does not validate metadata.

## Project Boundaries

- `src/MetadataSharp.Core`: empty SDK library; no runtime NuGet dependencies.
- `src/MetadataSharp.Formats`: empty SDK library referencing Core.
- `tests/MetadataSharp.Tests`: xUnit tests referencing both libraries.
- `Directory.Build.props`: shared language/target/nullable settings, deterministic
  output, warnings as errors, unsafe disabled, and packaging disabled by default.

SDK-generated assembly attributes let empty library projects compile without dummy
classes. One test project is sufficient for M01; future test separation is possible
when there are actual format and external-tool tests. No coverage collector or
external metadata tool is needed for this stage. `bin`, `obj`, test results and
local IDE files are ignored. These projects are not ready for NuGet publication.

## Current Verification Status

The Arena environment used for M01 preparation has no `dotnet` command. Restore,
build and test cannot run there. Project XML, solution/reference paths and settings
have been checked statically only; this does not establish compilation, package
resolution, test discovery or passing tests. M01 remains pending until the commands
above complete successfully in an SDK-enabled environment. Do not start M02 based
on static checks alone. No CI run or platform matrix is claimed.

## GitHub Actions Validation

`.github/workflows/build-and-test.yml` runs on pushes to master/Arena branches and
pull requests targeting master. It installs a serviced .NET 8 SDK on Linux, Windows
and macOS runners, records `dotnet --info`, and runs the restore/build/test commands
above. A separate TRX check rejects a green run with no tests or fewer than two
passing cases. Test results are retained as per-platform artifacts for seven days.
CI needs only read access to repository contents; no SDK archive or release asset
is required. Hosted runners have their own network environment, independent of
Arena's local SDK download restrictions. The run conclusion and logs, not workflow
creation alone, determine whether the M01 gate is satisfied.
