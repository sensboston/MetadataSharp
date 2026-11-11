# Build and test

## Requirements

.NET SDK 10 (`global.json` pins feature band 10.0.100 with `latestFeature` roll-forward). The libraries
target `net8.0` and `net10.0`; running the `net8.0` tests needs the .NET 8 runtime as well.

## Commands

```sh
dotnet build MetadataSharp.sln --configuration Release
dotnet test tests/MetadataSharp.Tests --configuration Release -f net10.0
python -m unittest discover -s tests/reference     # reference-harness and fixture-generator checks
python -m unittest discover -s tests/parity        # parity-harness checks (offline)
```

## CI (`.github/workflows/build-and-test.yml`)

- **build-and-test**: Ubuntu, Windows and macOS × net8.0/net10.0 — build, .NET tests, Python checks.
  A leg fails on any failing test or on too few tests run.
- **external-reference**: `tools/reference/harness.py --required` against exiftool/exiv2/dwebp from apt.
- **performance-package-audit**: measured workloads and local package inspection (nothing is published).
- **differential-parity**: fetches the exiv2 0.28.9 repository at the pinned commit at test time,
  runs `tools/MetadataSharp.Dump` over its `test/data` files and compares with the expected
  `exiv2 -Pkycvt` outputs in `test/data/test_reference_files`; then builds the pinned exiv2 and
  compares on this repository's own generated fixtures. Reports are uploaded as artifacts; parity
  never fails the build, a broken run does. Nothing from exiv2 is vendored into this repository.

## Parity locally

```sh
dotnet build tools/MetadataSharp.Dump -c Release -f net10.0
dotnet run --project tools/MetadataSharp.Dump -c Release -f net10.0 --no-build -- FILE...
```

Each file prints `FILE`, `TAG <key> <type> <count> <value> [<interpreted>]` and
`STATUS ok|refused|not-attempted|error` records (tab-separated). The sixth field is `ExifTags.Interpret`
for standard EXIF tags. `tools/parity/run_parity.py` compares such dumps with exiv2 references and
reports the interpreted column apart from the raw one.
