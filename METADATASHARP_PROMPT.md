# MetadataSharp: rules for contributors and coding agents

Read this file and `docs/CURRENT_STATE.md` before starting. Nothing else is required reading.

## Goal

An MIT-licensed managed .NET 8/10 library with exiv2-level metadata functionality: read, write and
remove EXIF, IPTC, XMP, ICC, comments and previews across the formats exiv2 supports, without
decoding pixels. Independent implementation, zero runtime dependencies.

## Rules

1. **Code and tests are the deliverable.** A PR is mostly `src/` and `tests/`. Do not create new
   documents. The PR description is the write-up.
2. **Docs budget.** `docs/CURRENT_STATE.md`: update only its "Done" and "Next" lists, at most
   10 lines per PR. `docs/archive/` is history: never edit it. Do not rewrite or delete existing docs.
3. **One task per PR.** Branch from current `master`; keep the PR to the task you were given.
4. **"Done" means green CI.** Never report tests as passing before CI is green on all legs. If the
   sandbox has no .NET SDK, say so and let CI compile.
5. **Public API:** show new or changed public signatures as a short block in the PR description and
   wait for review. Internal code needs no approval.
6. **Independent implementation.** exiv2/ExifTool sources may be read as a reference for facts
   (tag IDs, offsets, layouts, value meanings). Never translate their code, copy their descriptive
   text, or vendor their tests or data files (CI fetches the exiv2 corpus at test time only).
7. **Safety.** Bounds-check every length, offset and count before access; budget everything through
   `MetadataLimits`; a write whose safety is not established is refused, never best-effort.
8. **Do not touch `src/MetadataSharp.Core/MakerNotes/`** unless the task is about MakerNotes.
9. Code, identifiers, comments and commit messages in English; reports to the developer in Russian,
   five lines at most.

## Build

`dotnet build MetadataSharp.sln` and `dotnet test tests/MetadataSharp.Tests -f net10.0`.
Parity measurement: `tools/MetadataSharp.Dump` + `tools/parity` (see `docs/BUILD_AND_TEST.md`).
