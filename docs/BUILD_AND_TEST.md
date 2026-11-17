# Build and test

## Requirements

.NET SDK 10 (`global.json` pins feature band 10.0.100 with `latestFeature` roll-forward). The libraries
target `net8.0` and `net10.0`; running the `net8.0` tests needs the .NET 8 runtime as well.

## Commands

```sh
dotnet build MetadataSharp.sln --configuration Release
dotnet test tests/MetadataSharp.Tests --configuration Release -f net10.0
dotnet test tests/MetadataSharp.Examples --configuration Release -f net10.0   # the README's "Quick start", run
dotnet test tools/MetadataSharp.Harness.Tests --configuration Release         # the CI gates, offline
```

## CI (`.github/workflows/build-and-test.yml`)

- **build-and-test**: Ubuntu, Windows and macOS × net8.0/net10.0 — build, .NET tests, the tests of the
  CI gates (net10.0 legs), the documentation inventory reports and the authored video fixtures.
  A leg fails on any failing test or on too few tests run (`trx-count`: at least 450, all passed).
- **external-reference** (nightly): `external-reference --required` against exiftool/exiv2/dwebp from apt.
- **performance-package-audit**: `package-audit`, measured workloads and local package inspection
  (nothing is published).
- **differential-parity**: fetches the exiv2 0.28.9 repository at the pinned commit at test time,
  runs `tools/MetadataSharp.Dump` over its `test/data` files and compares with the expected
  `exiv2 -Pkycvt` outputs in `test/data/test_reference_files`; then builds the pinned exiv2 and
  compares on this repository's own generated fixtures. Reports are uploaded as artifacts; parity
  never fails the build, a broken run does. Nothing from exiv2 is vendored into this repository.
- **raw-corpus-parity**: downloads the pinned RAW corpus (below), then runs the `-pt` tables, the
  preview comparison and the MakerNote edit/copy comparison over it against the same pinned 0.28.9
  binary. The corpus is checked against its pins before the job can pass.

## The gate harness

Every gate of the workflows is a command of one C# tool, `tools/MetadataSharp.Harness`; no step runs
Python. The tool references neither library: it runs them through `MetadataSharp.Dump`,
`MetadataSharp.ReferenceFixture` and the fuzz harness, beside the pinned exiv2 binary, as separate
processes:

```sh
dotnet build tools/MetadataSharp.Harness -c Release
HARNESS="dotnet run --project tools/MetadataSharp.Harness -c Release --no-build --"
$HARNESS tag-print --tree artifacts/parity/exiv2 --exiv2 path/to/bin/exiv2 --dump-command "$DUMP" --annotate
$HARNESS previews --tree artifacts/parity/exiv2 --dump-command "$DUMP"
```

| Command | What it gates |
| --- | --- |
| `tag-print` | `-pt` rows of every corpus file against the pinned binary, with the named exceptions |
| `previews` | preview lists against the stored `exiv2 -pp` expectation or the pinned binary, and their source offsets |
| `icc` | ICC profiles against the pinned tests of `exiv2 -pC` |
| `key-catalog` | every key of the pinned reference's tag tables in `tag-catalog-data.json` |
| `sweep` | the ICC and comment readers over every corpus file |
| `video-gate` | key-set and value parity of the video containers |
| `raw-corpus` | download, verify, pin and explain the pinned RAW corpus |
| `makernote-render-gate` | interpreted MakerNote values in a parity report (not a CI step) |
| `range-corpus-gate` | read equivalence and byte budgets (below) |
| `read-cost-table` | the reading-cost report (below) |
| `sync-parity`, `operations-parity`, `tiff-thumbnail-parity`, `makernote-parity`, `charset-parity`, `crw-parity` | what this library converts or writes, against what the pinned binary does or reads back; every difference has to be a documented one |
| `tiff-based-edits`, `embedded-edits`, `png-chunk-edits`, `xmp-edits`, `iptc-edits` | files this library edited still read in the pinned binary, with the edit and every other line of the source |
| `eps-pgf-write-parity`, `psd-jp2-exv-write-parity` | writes in both directions between this library and the pinned binary |
| `jpeg-owned-fixtures`, `png-owned-fixtures`, `tiff-owned-fixtures`, `webp-owned-fixtures`, `xmp-owned-fixtures`, `dng-fixtures`, `dng-census-fixtures`, `eps-pgf-fixtures`, `video-fixtures`, `raw-container-fixtures` | the authored fixtures, written and checked against their pinned manifests |
| `external-reference` | authored files against exiftool, exiv2 0.27.6 and dwebp from apt |
| `package-audit` | the package audit (measurements, packages, offline consumer) |
| `doc-inventory-report` | the documentation inventory of a project, as annotations |
| `fuzz-run`, `fuzz-summary` | the fuzz campaign of one target, and the table of all targets |
| `trx-count`, `trx-failures`, `annotate-tail`, `annotate-chunks` | the test-count gate, the failed tests, the log of a sample that does not build and the reading-cost table, as annotations |

Their arguments, reports, annotations and exit statuses are the ones the Python scripts they replace
had: before a script was deleted, it and its port ran side by side on the same inputs in the runs of one
push (43680412100 for `tools/parity` and `tools/ci`; 43692065262 and 43692065252 for the rest) and
printed, wrote and exited the same. `dotnet run --project tools/MetadataSharp.Harness` lists the commands; those
with options print them with `--help`.

## Fuzzing (`.github/workflows/fuzz.yml`)

`tools/MetadataSharp.Fuzz` fuzzes every parser and writer with SharpFuzz and AFL++, one job per target
(`span`, `stream`, `previews`, `embedded`, `icc`, `comments`, `iptc`, `xmp`, `maker`, `write`): five
minutes of afl-fuzz each on a push or a pull request, thirty every night. Each job first replays the
whole corpus through its target - this repository's fixtures and generated files, the pinned exiv2
test data and the pinned CC0 raw files - so a finding on a real file fails the job before any
mutation. A target either succeeds or is refused with a `MetadataException` that has a defined code;
any other exception, a hang, an out-of-memory failure, a result beyond its `MetadataLimits`, a stream
read that differs from the read in memory, or a written file that does not read back with its change
fails the job. The campaign of each target is `fuzz-run` of the gate harness and the summary job's
table is `fuzz-summary`. Statistics and failures are posted as annotations. The targets are linked into the test
project, which replays the minimized failures under `tests/fixtures/fuzz` on every platform; see
`tools/MetadataSharp.Fuzz/README.md`.

## The pinned RAW corpus

The upstream checkout holds one to three raw files for most raw formats, which cannot show whether a
whole camera family is read. `tools/parity/raw-corpus.json` therefore names a wider corpus of camera
raw files from [raw.pixls.us](https://raw.pixls.us/), where every upload is released under CC0 1.0.
Each entry pins the exact URL, the byte size and the SHA-256, and the camera the sample came from; the
files are downloaded at test time and are never committed.

```sh
$HARNESS raw-corpus list                                     # what the manifest holds
$HARNESS raw-corpus download artifacts/parity/raw-corpus      # fetch and check every pin
$HARNESS raw-corpus verify artifacts/parity/raw-corpus        # refuse a missing, wrong or unpinned file
$HARNESS raw-corpus pin artifacts/parity/raw-corpus --write   # record a measured size and hash
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
and fields, `IptcDatasetNames.Interpret` for IPTC datasets and `XmpTags.Interpret` for XMP properties.
An EXIF, MakerNote or XMP row always states the field, empty where the library makes nothing of the
value, so that a text the reference reads and this library does not is a row the comparison sees; only
the elements of an array nested in a MakerNote sub-directory leave it out.
`tools/MetadataSharp.Parity` compares such dumps with exiv2 references and reports the interpreted
column apart from the raw one. A value we return no interpretation for is `not-interpreted`, counted
beside the rate; the gate fails on a value the reference reads and this library does not, or reads
otherwise. `--gaps` posts the counts of every group and the list of what remains as notices.

## What a file costs to read

Reading takes the byte ranges the parsers ask for: `ImageMetadata.Read(path)` and
`ImageMetadata.Read(stream)` open a source, and every reader reaches back through it for the bytes it
needs. `ImageMetadata.Read(byte[])` still parses bytes already in memory, and writing still reads and
rewrites the whole file, where that is the only correct behaviour.

The measurement is a report, not a gate: it compares this library with itself, whole file against
ranges, and then with the pinned exiv2 binary on the same files.

```sh
dotnet build tools/MetadataSharp.Bench -c Release
METADATASHARP_BENCH_DIR=path/to/files dotnet run --project tools/MetadataSharp.Bench -c Release -- --filter '*'
$HARNESS read-cost-table --results artifacts/bench/BenchmarkDotNet.Artifacts/results \
  --corpus path/to/files --exiv2 path/to/bin/exiv2 --out artifacts/bench/reading-cost.md
```

`METADATASHARP_BENCH_QUICK=1` replaces the short job with a dry one, which is what the CI job uses;
a local run without it gets BenchmarkDotNet's short job and steadier numbers. BenchmarkDotNet is
referenced by `tools/MetadataSharp.Bench` alone and by no library. The `streaming-bench` job runs the
same two commands on six files of the pinned RAW corpus and keeps `reading-cost.md` as an artifact.

The required gates run in both parity jobs. `range-corpus-gate` compares snapshots,
refusals and errors from array, seekable stream, path and non-seekable stream over every input in
pinned `exiv2/test/data` (including the preview directory, excluding reference-output text) and all
23 pinned CC0 RAW files. The only format exception is a signatureless `.tga`, which only the path
can identify by name. The RAW manifest is verified by SHA-256. The gate counts actual underlying
stream reads on named real files; tables with a row per file are in the Actions summary and artifact.

Each named format has its own ceiling: `max(4096, fraction × file size + allowance)` bytes.
The allowance accounts for block-sized reads and overlaps; every RAW sample in the manifest is
checked. HEIC, AVIF, CR3, JPEG, PNG and WebP use named pinned exiv2 samples. A missing sample,
wrong detected format, read failure, mismatch or exceeded ceiling fails the job.

| Format | Fraction | Allowance (bytes) | Format | Fraction | Allowance (bytes) |
| --- | ---: | ---: | --- | ---: | ---: |
| CRW | 5% | 131,072 | CR2 | 7% | 131,072 |
| NEF | 8% | 131,072 | ARW | 5% | 131,072 |
| ORF | 3% | 131,072 | PEF | 12% | 131,072 |
| RW2 | 23% | 131,072 | SRW | 5% | 131,072 |
| DNG | 10% | 131,072 | MRW | 3% | 131,072 |
| RAF | 6% | 131,072 | SR2 | 4% | 131,072 |
| HEIC | 15% | 65,536 | AVIF | 50% | 65,536 |
| CR3 | 60% | 65,536 | WebP | 20% | 16,384 |
| JPEG | 1% | 32,768 | PNG | 10% | 16,384 |

JPEG metadata snapshots stop at the first SOS after validating its header; entropy and later
markers are not checked. Editing documents still scan and hash the whole file. PNG snapshots skip
IDAT payloads and check CRC on metadata chunks, not on non-metadata image chunks. PNG metadata
after IDAT remains discoverable. WebP snapshots skip image payloads. A 4 KiB cache block can
include a small amount of image data adjacent to a required header; source blocks are read once,
and the PNG gate also rejects overlapping underlying reads. Files dominated by metadata can still
require nearly the entire file: the pinned Reagan JPEG contains 1.6 MB of APP2 segments before
SOS, and Reagan PNG contains a 1.15 MB iCCP chunk. The tightened percentage ceilings use pinned
files with substantial image data; they are not universal limits on all files.
Non-seekable streams deliberately buffer the entire file within `MaxInputBytes`.

`tests/MetadataSharp.Tests/Runtime/RangeReadTests.cs` also checks fixtures, a sparse TIFF,
malformed offsets and cycles, and the non-seekable input budget. The byte counter measures the
seekable stream overload; `Read(path)` uses the same range source. Benchmarks are a report, not a
gate: `Read(byte[])` is preloaded parsing only, while `File.ReadAllBytes + Read` includes whole-file
I/O and allocation and is the honest previous-path baseline. The report also times `exiv2 -pa` on
exactly the six chosen files. The CI dry job is one iteration and must not be treated as a precise
performance comparison. `reading-cost.md` in the Actions artifact has the measurements.

The range cache retains blocks until a read ends; scattered directories can allocate more than
preloaded parsing. Writing remains whole-file. No claim is made that parsing is as fast as exiv2.
