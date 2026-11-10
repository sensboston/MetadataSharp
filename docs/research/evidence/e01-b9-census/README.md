# E01 B9 — pinned-CLI census observations for TIFF-derived RAW wave 1

Batch B9 records ARW, CR2, NEF, ORF, PEF and SRW against owned synthetic fixtures.
96 census rows: 48 observed through the pinned Exiv2 0.28.9 CLI (`b0859fc61`, built in
CI with the pinned option set) and 48 recorded `blocked` for the vendor private layouts.
Every row keeps `verification: not-assessed`, the denominator stays unfrozen and no
coverage percentage is computed anywhere in this batch.

Files here:

- `observations.json` — the CI report, byte-pinned by `receipt.json`. There is no
  local counterpart: this sandbox has no cmake/ninja or expat/zlib headers, cannot
  install them, and cannot reach the Actions log or artifact-blob hosts, so the CI run
  is the only observation source (unlike B6/B7, which kept a local file plus a `ci-`
  twin).
- `receipt.json` — provenance, the annotation-recovery record and its hashes, the
  baseline digests, the tally, the annotation-budget finding and the `notClaimed` list.
- `README.md` — this file.

## What ran

One read-only command per row, chosen by family (`-pS` inventory, `-pa` exif/iptc/xmp
and thumbnail reads, `-pc` comments, `-pC` icc, `-et` thumbnail extract), against a copy
of the fixture in a scratch directory, then ExifTool 12.76 `-json -G1 -s` as a secondary
reference on the same bytes. PostScript, decoders and pixels are never involved; nothing
is written back to a fixture.

## What the pinned CLI showed

| Fixture | Outcome |
| --- | --- |
| all six (`owned-marker-*`) | `-pa` accepted, printing 4214–4234 bytes that contain both authored EXIF tokens `B9Artist` and `B9Tool`; the four metadata-family read rows on one fixture share **one identical stdout digest**, so no row is an independent per-family claim |
| all six | `-pS` accepted (2655–2681 bytes of structure print), so the header framing is read by the pinned build |
| all six | `-pc` accepted and printed a single byte (a newline): no comment was surfaced, consistent with `amNone` comment mode for these formats in the pinned registry |
| ARW/CR2/NEF/PEF/SRW | `-pC` accepted and printed 192 bytes, the exact authored ICC profile length |
| ORF | `-pC` accepted and printed 202 bytes for that same 192-byte profile: a different print form, recorded rather than diagnosed |
| all six | `-et` exited **137** and wrote exactly one artifact, `case-thumb.jpg`, whose bytes are a **byte-exact copy** of the authored 137-byte owned JPEG (`thumbnailRoundTrip.byteExactCopyProducedByPinnedCli: true`) |

Aggregate: 42 `accepted`, 6 `nonzero-exit-artifact-produced`, 48 `blocked-rights`,
no `refused` row at all, 0 `static-not-applicable`, 0 `scan-changed`; every input byte-identical
afterwards; 6 artifacts produced in scratch space, none in the repository.

Because `-et` exits nonzero while producing an artifact, the recorder classifies it as
`nonzero-exit-artifact-produced` — never as `accepted` and never as a `refusal`. That is
the same rule batch B0 established for the pinned CLI's thumbnail exit codes.

## What is *not* shown

These fixtures are authored header markers with owned metadata carriers. They contain no
vendor private IFD, no MakerNote, no decodable raw data and no camera preview. Therefore:

- an accepted `-pa` here is not evidence that the vendor layout is supported;
- the empty `-pc` is not evidence that a real capture has no comment;
- the 48 blocked cells say the proprietary layouts were never reviewed, not that they
  fail;
- ExifTool's `File:FileType` (ARW/CR2/NEF/ORF/PEF/SRW with vendor MIME types) is a
  secondary observation whose file-type lookup consults the extension, so it is not a
  validity proof for these files either;
- the reference registry marking `arw`/`rw2`/`sr2`/`raf` as `amRead` is a fact about
  this pinned build, not an accepted exclusion and not a MetadataSharp decision.

## Annotation budget: why this batch runs in its own job

The first B9 run (42729776775, head `39ee8c9`) was **green and left no recoverable
evidence**. A check run keeps at most 50 annotations; the B0–B8 steps in the shared
Exiv2 job emit exactly 50, so the B9 chunks were dropped at the cap without any step
failing. The report *was* uploaded as `e01-b9-census-observations`, but its blob host
is unreachable from this environment, so the artifact could not substitute.

Two structural fixes came with that finding: the batch moved into its own
`pinned-exiv2-b9` job, which buys a fresh 50-annotation budget, and the recorder now
emits a trailing `title=B9Z observations.json count::4` marker that recovery must match
against the chunk set before trusting the payload — a lost first chunk is otherwise
invisible to a per-chunk `i/n` check. `receipt.annotationBudget` records the cap, the
10 annotations this run used, and the dropped run.

## Reproduction

The report was recovered twice from the same check run by two separate `gh api` calls
and produced the identical `rawPayloadSha256`
(`5b627e3b27bc76a9…`), so the recovery path is deterministic. A second CI run on a
later head would be the cross-run sample; it is deliberately not asserted here, and no
build-reproducibility claim is made — the reference binary embeds its own build
timestamp, so `baseline.executableSha256` changes per run by construction (batch B8
records the same finding).
