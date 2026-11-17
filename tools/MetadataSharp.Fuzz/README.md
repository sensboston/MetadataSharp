# MetadataSharp.Fuzz

Coverage-guided fuzzing of every parser and writer of the two libraries with
[SharpFuzz](https://github.com/Metalnem/sharpfuzz) 2.2.0 and AFL++. SharpFuzz is a dependency of this
tool only; `MetadataSharp.Core` and `MetadataSharp.Formats` stay without package references
(`ProjectInfrastructureTests` holds them to that).

## The contract

A target takes arbitrary bytes and either succeeds or is refused with a `MetadataException` whose
code is defined. Where an API documents that it refuses less, the target holds it to that:
`ImageMetadata.Read` refuses nothing about the content of a file (what a reader refuses is in
`Errors`), `Previews.List` and `EmbeddedImageMetadata.Read` refuse only a budget or the malformed
container of a CRW, MRW or RAF file. Everything else is a bug:

- any other exception, an out-of-memory failure (the GC heap is limited to 512 MiB), a stack overflow
  (it kills the process, which the engine sees), and an execution longer than 5 s under afl-fuzz or
  10 s + 2 s per MiB in the replay;
- a result that exceeds the `MetadataLimits` it was read under: a value longer than `MaxValueBytes`,
  more entries in a directory or a block than `MaxEntriesPerDirectory` and `MaxTotalEntries`
  allow, an ICC profile or comments beyond their documented budgets, an XMP or IPTC packet beyond
  the packet budget of its reader;
- a read through a stream that differs from the read of the same bytes in memory;
- a written file that is refused when read again, changes format, gains an error the source did not
  have, or does not read back with its change.

The targets are in `FuzzTargets.cs`, which `tests/MetadataSharp.Tests` compiles too: the ordinary test
run puts every file of `tests/fixtures`, the minimized failures of `tests/fixtures/fuzz` among them,
through every target.

| Target | What one input goes through |
| --- | --- |
| `span` | `ImageMetadata.Read(span)` with explicit default limits, every accessor of the snapshot (EXIF values and interpretations, MakerNote entries, fields and keys, IPTC text, XMP entries, the derived properties), `Reconcile`, the limit checks |
| `stream` | `ImageMetadata.Read` from memory, from a seekable stream and from a forward-only stream of short reads, with the readers' own defaults; the three snapshots must be equal |
| `previews` | `Previews.List`, then `Previews.Extract` of each preview: a listed preview lies in the file, an extracted JPEG preview runs from SOI to EOI |
| `embedded` | `EmbeddedImageMetadata.Read` of the file and of each listed preview |
| `icc` | `IccProfiles.Read`: a returned profile has the declared size, the `acsp` signature and fits the budget |
| `comments` | `Comments.Read`: no empty comment, no trailing zero, within the budgets |
| `iptc` | `IptcPacket.Parse` and the lenient reader of the containers; an edit of the caption keeps every other dataset and parses again |
| `xmp` | `XmpPacket.Parse` and `Load` (which must agree); an edit is saved and parses again with the change |
| `maker` | the internal MakerNote readers: the first eleven bytes choose the Make (every vendor branch), the byte order, the note range and the length of the Model that follows; the rest is the TIFF block |
| `write` | every writer of the format: an EXIF edit, a combined EXIF/XMP/IPTC save, a comment, an XMP sidecar insertion, deletions, the PSD, JPEG 2000 and PGF rewrites, `CopyMetadata` in both directions, `ExtractExv`/`InsertExv`, `ExtractXmpSidecar`, `ExtractThumbnail`; each output is read again |

## Running it

`.github/workflows/fuzz.yml` runs every target on each push and pull request (5 minutes of afl-fuzz
each) and every night (30 minutes each). A job first replays the whole corpus through its target -
the repository's fixtures, the files `tools/MetadataSharp.ReferenceFixture` generates, the exiv2 test
data at the commit the parity jobs pin, and the CC0 raw files of `tools/parity/raw-corpus.json` -
then fuzzes two seeds per source and extension (the smallest and the median, cut to AFL's 1 MiB),
and the IPTC streams, XMP packets and MakerNotes extracted from the corpus for the packet targets.
Every crash and hang afl-fuzz saves is then replayed for its stack trace, the failures are grouped
by exception and library frames, and the first input of each group is minimized with `afl-tmin`.
A saved input that passes when it is replayed alone is reported all the same, as
`crash-not-reproduced` or `hang-not-reproduced`, and fails the job like any other finding -
except a crash whose death was signalled by SIGINT or SIGTERM: the runner's asynchronous interrupt
reaches the process group and kills the forkserver child of afl-fuzz, .NET code does not raise those
signals, and the input passes on replay, so it is a warning annotation with the input kept as an
artifact (`reproductions/` and a `not_reproduced_signals` entry in `report.json`), not a finding.
A crash that reproduces, or dies by any other signal, fails the job as before.
Statistics and every distinct failure, with its input when it is small, are posted as annotations;
`report.json`, the logs, the minimized failures and the first five inputs of each failure (renamed
under `reproductions/`, as upload-artifact refuses the colons of afl-fuzz's names) are kept as an
artifact.

The campaign is run by `fuzz-run` of the CI gate harness (`tools/MetadataSharp.Harness/Fuzz/FuzzRun.cs`).
Locally, on Linux with AFL++ (`afl-fuzz`, `afl-tmin`) and the .NET 10 SDK:

```sh
dotnet build tools/MetadataSharp.Fuzz -c Release -o artifacts/fuzz-bin
dotnet build tools/MetadataSharp.Harness -c Release
dotnet tool install --tool-path artifacts/fuzz-tools SharpFuzz.CommandLine --version 2.2.0
HARNESS="dotnet run --project tools/MetadataSharp.Harness -c Release --no-build --"
$HARNESS fuzz-run extract --dll artifacts/fuzz-bin/MetadataSharp.Fuzz.dll \
  --source fixtures=tests/fixtures --output artifacts/extracted
artifacts/fuzz-tools/sharpfuzz artifacts/fuzz-bin/MetadataSharp.Core.dll
artifacts/fuzz-tools/sharpfuzz artifacts/fuzz-bin/MetadataSharp.Formats.dll
$HARNESS fuzz-run span --dll artifacts/fuzz-bin/MetadataSharp.Fuzz.dll \
  --source fixtures=tests/fixtures --extracted artifacts/extracted \
  --dictionary tools/MetadataSharp.Fuzz/dictionaries/files.dict --output artifacts/fuzz-span --seconds 300
```

`dotnet artifacts/fuzz-bin/MetadataSharp.Fuzz.dll --replay TARGET FILE...` runs inputs once each and
prints the stack trace of a failure; it works on the instrumented build too. The dictionaries hold the
signatures, markers, chunk and box names, RDF syntax, IPTC tags, vendor signatures and camera models
the targets meet.

## A failure

Reproduce it with `--replay`, fix it in `src`, add the minimized input under
`tests/fixtures/fuzz/TARGET/` with a line in that directory's README, and, where the input does not
make the cause plain, a unit test next to the code it concerns.
