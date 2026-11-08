# Current development state and recovery checkpoint

Updated: 2026-11-07. This is a working handoff, not a substitute for source/Git/CI.
Read this file first after an interruption. Update it at implementation/checkpoint
boundaries; append chronological evidence to PROGRESS.md. No chat history required.

## Current unit and approval

**M16: restricted standalone TIFF editing — exact contract APPROVED; no writer
code has been added at this checkpoint.** The developer explicitly confirmed the
proposal, including physically retained obsolete metadata, then asked for this
repository-resident checkpoint before implementation. Do not ask for that same
approval again. Any actual public/safety change still needs new approval.

Authoritative approved API/schema/strategy:
[STANDALONE_TIFF_EDITING_CONTRACT_PROPOSAL.md](design/STANDALONE_TIFF_EDITING_CONTRACT_PROPOSAL.md).
Read it in full; this summary does not broaden it.

Current session branch: `arena/d7ec6276-metadatasharp`, based on M15 merge
`80dc75e62eff242d2b98bc4b14b3090cf4ea60ce`. Proposal commit `a658220`.
Work only on the branch assigned by the live session; this historical branch name
must not override a later session's branch assignment. Never switch to master.
Changes are committed/pushed under standing developer authorization. Merge only
a completed final-head-green unit; package publication is not authorized.

## Completed predecessor evidence

- M00–M15 complete. M13–M14 PR #9 merge:
  `269dc92cb5bf1218df230e1ced3140c95ca7d81c`.
- M15 PR #10 merge: `80dc75e62eff242d2b98bc4b14b3090cf4ea60ce`.
  Final source `812bd76af3241d479e7b9fff07c9dd6a8197a118`, PR run
  [42431094052](https://github.com/sensboston/MetadataSharp/actions/runs/42431094052),
  push run [42431090942](https://github.com/sensboston/MetadataSharp/actions/runs/42431090942).
  Both green: **392 .NET tests on each Windows/Linux/macOS**, **12 Python policy
  tests**, required pinned external references.
- Permanent evidence/limitations: audits/STANDALONE_TIFF_READ_ONLY_AUDIT.md,
  design/STANDALONE_TIFF_NORMATIVE_REVIEW_AND_SUPPORT.md and PROGRESS.md.
  Predecessor numbers are not results for new M16 code.

## Essential M16 decisions already approved

- New concrete TiffMetadataDocument/TiffMetadataEdit/TiffFieldKey/TiffEditOptions;
  source-relative IFD keys; reuse ExifValue as an immutable primitive only.
  Existing JPEG/PNG/WebP APIs, embedded EXIF cap and TiffLayout remain unchanged.
- Closed descriptive tag set; preserve only the exact schema in the contract.
  M15 IsSupported alone does not authorize a write. Refuse unknown/private tags,
  MakerNotes, opaque XMP/IPTC/ICC/JPEGTables extensions and unproven pointers.
- Preserve original storage; append a complete replacement reachable IFD graph;
  rewrite every active next/SubIFD/Exif/GPS/Interop link through one offset map.
  Shared graph targets remain shared; no directory creation/deletion.
- Inside the original source-length prefix, only header bytes 4–7 may change.
  Images/previews and unchanged external values keep their bytes/absolute offsets.
  New IFDs/values/pointer arrays append with word alignment and checked 32-bit bounds.
- **Old metadata bytes remain physically present. Not privacy scrubbing, secure
  erasure or compaction. Repeated edits grow files.** No in-place source append.
- Empty/absent/same-value edits are provable exact copies; no identity bypass.
  Refuse operations that empty an IFD. Retain source endianness.
- Whole-source SHA-256/length before read/save, owned result snapshots, actual async
  I/O/cancellation, finite original+staged bookkeeping, full preflight before writes.
  New-path temporary publication without overwrite; post-emission stream failures
  may leave caller-owned partial output. No concurrent-use/crash-durability promise.
- No TIFF general EXIF/GPS or XMP/IPTC writer, DNG, BigTIFF, decoding/recompression,
  new production dependency, unrelated refactor or performance claim.

## Next implementation steps, in order

1. Verify `git status`, current assigned branch, approved contract and M15 audit.
   This checkpoint must be committed/pushed before changing C#.
2. Review TIFF6 directory/header/field placement and approved CIPA/SubIFD pointer
   prose for writing. Record immutable source hashes/sections in a new M16 review.
   M15's exact mirror hashes are already in its normative record; sources/caches
   are not persisted deliverables. Do not import/translate GPL code or tests.
3. Implement captured options/edit snapshots and independent finite write planning:
   source identity; exact schema validation; original-relative keys; no-op proof;
   whole graph map/IFD layout; rewritten arrays; appended value staging; all ranges,
   output/read/address-space limits and empty-directory refusal before output.
4. Add sync/true-async document/source lifecycle and stream/new-path emission using
   existing internal I/O/publication helpers only where guarantees match. Do not
   reinterpret the embedded EXIF serializer as general TIFF serialization.
5. Add owned independent fixtures/tests: II/MM, strips/tiles/planes/compressions,
   DAG/next/metadata links/previews, shared values, inline/external transitions,
   raw-prefix/image-offset identity, removed bytes still present, mandatory refusals,
   sparse overflow/limits, source mutation, short async I/O/cancellation, path races
   and partial-output/temporary cleanup. No committed huge buffers/camera corpus.
6. Extend public-API reference generator/harness with independent TIFF edited facts,
   reverse ingress and pointer/range checks. Retain strict warning classification.
   Update test floor to actually added cases, not a guessed success count.
7. Run all required platform/reference checks on the actual final head, inspect
   failures and fix them; complete usage/support/audit docs. Record actual evidence,
   commit/push and merge the explicitly numbered PR only after all gates are green.
8. Update this file/PROGRESS with outcomes and next eligible unit. M17 is DNG
   feasibility/contracts ONLY and requires its own follow-up-plan approval, not DNG
   code. M18 is measured optimization/packaging audit, not publication permission.

## Files to inspect first

- src/MetadataSharp.Formats/Tiff/{TiffLayout,TiffLayoutReader,TiffModels}.cs:
  immutable whole-file accounting, graph, ranges and M15 resource charging.
- src/MetadataSharp.Formats/Internal/ContainerIo.cs: source hash, exact I/O, copy,
  destination validation, temporary publication (not a relocation safety engine).
- src/MetadataSharp.Core/{ExifValue,MetadataLimits,MetadataException}.cs:
  owned typed raw values, limits, existing stable error codes.
- tests/MetadataSharp.Tests/Tiff/: existing standalone fixtures/tests; locate actual
  filenames with `find` rather than inventing helper APIs.
- tools/MetadataSharp.ReferenceFixture/Program.cs; tools/reference/harness.py;
  tests/reference/test_harness.py; .github/workflows/build-and-test.yml.

## Reproduction, environment and recovery

```sh
git status --short
git branch --show-current
git log -5 --oneline
python -m unittest discover -s tests/reference -q
git diff --check
dotnet restore MetadataSharp.sln
dotnet build MetadataSharp.sln --configuration Release --no-restore
dotnet test MetadataSharp.sln --configuration Release --no-build --no-restore
```

At this checkpoint `dotnet` and `pdftotext` are absent locally; no new .NET run is
claimed. Use actual push-triggered GitHub Actions for required three-platform and
reference gates; do not waste work repeatedly downloading the unavailable SDK.
Use `gh run list/view` and check-run annotations when log/artifact downloads fail.
Pinned CI reference tools: Exiv2 0.27.6, ExifTool 12.76, dwebp 1.3.2 (WebP only).
Optional local harness skips are never required-reference passes. Never disable
assertions, weaken warnings or merge on predecessor results.

Before resuming, compare these statements to current files, `git diff`, HEAD and
live PR/check status. Uncommitted/unbuilt implementation must be described as such.
A later checkpoint may supersede the no-code state above; preserve the distinction
between a plan, source implementation and observed passing checks.
