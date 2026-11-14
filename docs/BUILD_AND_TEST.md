# Build and test

## Requirements

.NET SDK 10 (`global.json` pins feature band 10.0.100 with `latestFeature` roll-forward). The libraries
target `net8.0` and `net10.0`; running the `net8.0` tests needs the .NET 8 runtime as well.

## Commands

```sh
dotnet build MetadataSharp.sln --configuration Release
dotnet test tests/MetadataSharp.Tests --configuration Release -f net10.0
dotnet test tests/MetadataSharp.Examples --configuration Release -f net10.0   # the README's "Quick start", run
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
- **raw-corpus-parity**: downloads the pinned RAW corpus (below), then runs the `-pt` tables, the
  preview comparison and the MakerNote edit/copy comparison over it against the same pinned 0.28.9
  binary. The corpus is checked against its pins before the job can pass.

## The pinned RAW corpus

The upstream checkout holds one to three raw files for most raw formats, which cannot show whether a
whole camera family is read. `tools/parity/raw-corpus.json` therefore names a wider corpus of camera
raw files from [raw.pixls.us](https://raw.pixls.us/), where every upload is released under CC0 1.0.
Each entry pins the exact URL, the byte size and the SHA-256, and the camera the sample came from; the
files are downloaded at test time and are never committed.

```sh
python tools/parity/raw_corpus.py list                                     # what the manifest holds
python tools/parity/raw_corpus.py download artifacts/parity/raw-corpus      # fetch and check every pin
python tools/parity/raw_corpus.py verify artifacts/parity/raw-corpus        # refuse a missing, wrong or unpinned file
python tools/parity/raw_corpus.py pin artifacts/parity/raw-corpus --write   # record a measured size and hash
```

`explain` prints where the first corpus file disagrees with the reference, row by row, and is what the
job emits when one of its comparisons fails. `verify` refuses a file whose size or hash is not the pinned one, and refuses an entry that has no pin
at all, so a green run never rests on bytes nobody measured. A downloaded file that does not match its
pin is deleted instead of passed on to a comparison.

## Parity locally

```sh
dotnet build tools/MetadataSharp.Dump -c Release -f net10.0
dotnet run --project tools/MetadataSharp.Dump -c Release -f net10.0 --no-build -- FILE...
```

Each file prints `FILE`, `TAG <key> <type> <count> <value> [<interpreted>]` and
`STATUS ok|refused|not-attempted|error` records (tab-separated). The sixth field is the library's
interpreted value: `ExifTags.Interpret` for standard EXIF tags, `MakerNote.Interpret` for MakerNote tags
and fields, `IptcDatasetNames.Interpret` for IPTC datasets; XMP records have none.
`tools/MetadataSharp.Parity` compares such dumps with exiv2 references and reports the interpreted
column apart from the raw one. Its rate is over the values both sides interpret; a value we return no
interpretation for is `not-interpreted`, counted beside the rate and never as a difference.
