# M15 read-only standalone TIFF audit (2026-11-07)

## Approval and actual evidence

Developer explicitly approved the exact standalone TIFF contract after M13–M14
merged at 269dc92cb5bf1218df230e1ced3140c95ca7d81c. Implementation is separate from
embedded JPEG/PNG/WebP EXIF. No new production dependency, public common base,
TIFF writing, BigTIFF, DNG or M16 write authorization is included.

Initial source 3c6e5636ce8a5f4d011fa9c32604b8f29f1aacc5 passed required run
[42430554406](https://github.com/sensboston/MetadataSharp/actions/runs/42430554406)
(all platforms, 382 tests and pinned references). Audited source
**e806adc0d35913a807e6050632afbb06e3f7e85e** passed required PR run
[42430917666](https://github.com/sensboston/MetadataSharp/actions/runs/42430917666):
**392 passing tests on Windows/Linux/macOS**, Python policy **12/12**, required
Exiv2/ExifTool/dwebp job green. Existing JPEG/PNG/WebP tests remain in that total.

This final documentation head must itself pass CI before merging
[PR #10](https://github.com/sensboston/MetadataSharp/pull/10). Actual final-head SHA,
run IDs and merge evidence are posted there only after completion. Predecessor
results do not authorize merging a red head. Local dotnet remains unavailable;
.NET evidence above is observed Actions output, not a claimed local build.
Local Python policy and git diff --check pass. No package publication performed.

## Implemented and audited boundaries

- Exact approved TiffLayout.Read[Async] path/stream facade and read-only descriptors.
  Options captured before I/O; no source lifetime retained in the returned snapshot.
  Source paths close before return; borrowed streams stay open. Physical I/O errors
  propagate without semantic reclassification. Failed reads return no partial model.
- II/MM classic TIFF6 header/types/offsets/entry framing, positive entry count,
  unique tag IDs, scalar/pointer shapes and checked range arithmetic. Unsorted tags
  are the explicitly approved compatibility acceptance, not baseline conformance.
- Iterative shared directory DAG parsing, actual pointer locations, all recognized
  next/SubIFD/Exif/GPS/Interop links, cycles refused. Longest-path depth validates
  shared paths, not only their first discovery path. Limits preflight before growth.
- Strip/tile arrays, planes/grid counts, candidate JPEG previews and field extents.
  All image/structure/value overlaps and partial value aliases refuse. Compatible
  exact shared field values are accounted once; inline storage belongs to its entry.
  Global ranges sorted; image array order preserved. Pixel bytes are not read.
- Preview-only metadata directories without TIFF dimensions retain a bounded global
  preview extent and explicit unsupported-layout issue, never guessed dimensions.
- Supported samples/compression are only the approved accounting subset. Unsupported
  layouts/private fields report issues and IsSupported=false. Unknown field widths,
  DNGVersion and BigTIFF refuse. Default resource ceilings can intentionally refuse
  otherwise viewable files; see normative support record for logical charge formula.
- Whole-file TIFF is not JPEG-sized: a 128-KiB source is tested with pixel reads
  forbidden. Individual value limits remain finite. Embedded EXIF caps/API unchanged.
- Immutable snapshots survive source mutation/disposal; returned lists reject edits.
  There is no checksum identity or relocation certificate for future writing.

## Fixtures and independent comparisons

StandaloneTiffFixture authors II/MM 16x16 grayscale strip/tile files with an owned
0–255 pixel ramp. No external image, reference implementation/test or codec binary
was imported. The normative review records TIFF6/CIPA mirror hashes, exact sections,
alignment/type facts and conservative unsupported cases; PDFs are not redistributed.

Required pinned ExifTool 12.76 and Exiv2 0.27.6 facts independently match dimensions,
bit depth/compression and layout results for all four owned variants. TIFFs rewritten
by ExifTool with an addressed Artist change are ingested again. The existing dwebp
pin still validates WebP regressions, not TIFF decoding. The report includes argv,
versions, recipe/source hashes, exit/stdout/stderr and generated fixture hashes.
Ordinary tests remain offline. No warning classifier was broadened.

TIFF comparison is specifically metadata-fact/reference-ingress verification, not
an ExifTool -validate full TIFF conformance certification or a pixel decoder test.
The fixture is deliberately minimal, not evidence of full TIFF/Exif compliance.
No privacy scrubbing, metadata synchronization, private pointer completeness,
performance/allocation improvement or exhaustive security/fuzzing proof is claimed.
Finite mutations, malformed extents, overlaps, DAGs/cycles, limits, async failures
and ownership tests complement rather than replace normative review.

## M16 constraint

The implemented support matrix is STANDALONE_TIFF_NORMATIVE_REVIEW_AND_SUPPORT.md.
Unknown/private values and old JPEG table target lengths remain unproven; even an
IsSupported snapshot cannot authorize relocation. Before M16, separately approve a
source-identity model, supported writable tag/pointer schema, opaque-field policy,
copy/rebuild strategy and image-byte preservation proof. No M16 code was started.
