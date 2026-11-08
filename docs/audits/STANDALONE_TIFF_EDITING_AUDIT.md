# M16 restricted standalone TIFF editing audit (2026-11-07)

## Authorization and execution evidence

The exact [M16 contract](../design/STANDALONE_TIFF_EDITING_CONTRACT_PROPOSAL.md),
including retained obsolete bytes, was explicitly approved after M15 merged.
Recovery checkpoint 4bf56c6 was pushed BEFORE code as separately required.
[Usage and limitations](../design/STANDALONE_TIFF_EDITING_USAGE_AND_LIMITS.md)
spell out the non-erasure/new-output compromise. No new public common abstraction,
production dependency, embedded EXIF behavior, BigTIFF/DNG implementation or
publication authorization is introduced.

Initial range-audited code 600bbc1 passed required run 42435227396: 405 tests per
Windows/Linux/macOS, Python policy 12/12 and mandatory pinned references. Expanded
family/failure code 0dd38366cdc5bac2843541d336abfb4c9837cbd5 passed required
[42435965943](https://github.com/sensboston/MetadataSharp/actions/runs/42435965943)
and push run 42435961970: 435 tests/platform plus policy/references. DateTime/snapshot audit corrections at
635f8bb565eeaab72a9147d5e9825fdcde54c215 passed required
[42436144783](https://github.com/sensboston/MetadataSharp/actions/runs/42436144783):
438/platform, policy 12/12 and references. Full acceptance/documentation head
**762dc206915e7989721645e0ee3a8adb479310c2** passed required
[42436352761](https://github.com/sensboston/MetadataSharp/actions/runs/42436352761):
**448 .NET tests on each Windows/Linux/macOS**, Python **12/12** and mandatory
references. Implementation/acceptance audit is complete. The final documentation
commit must also pass CI before merge; its exact SHA/run/merge receipt is recorded
in PR #11 after success, not preclaimed here.
See CURRENT_STATE/PROGRESS and PR #11 for subsequent exact evidence. Local SDK
is absent; only Python policy and diff checks are local. No benchmark, full fuzz
campaign or package publication is claimed.

## Implementation proof review

| Approved boundary | Implementation and exercised evidence |
| --- | --- |
| Original keys, owned values, captured options/edits, last-wins | TiffMetadataEdit/document; both byte orders, every addressed tag add/read/equivalent-set/remove, old key refusals, snapshot mutation during async hash, result lifetime after disposal |
| Malformed targets and finite validation | Closed TiffEditPolicy; bad text/date read/set refusal, removal bypass, type/count/entry/value/output/range caps, snapshot I/O trap; unknown private extensions refuse |
| Complete active graph replacement | TiffAppendPlanner single old/new map; next, SubIFD arrays, Exif/GPS/Interop, shared DAG nodes, sorted entries, nonempty IFDs; source-relative external storage retained |
| Image and prefix identity | II/MM strip/tile, gray/RGB 8/16-bit, contiguous/separate planes, associated/unassociated alpha, candidate preview, odd source-length padding, exact shared RATIONAL extents; all prefix bytes except 4–7 preserved |
| Compression handling | IDs 1/5/7/8/32946/32773 in strip/tile tests; compressed-selector tests are accounting-only with synthetic bytes, NOT valid compressed-codec fixture claims |
| Address/output accounting | Checked aligned reserves before emission; minimum MaxOutput/MaxInput/4-GiB ceiling; synthetic model planner at exactly 4 GiB and overflowing directory/value cases; no giant source hash/copy claim |
| Source integrity and no-op | Whole length/SHA-256 for reads/saves, changed-source refusal including same length; empty/absent/equivalent exact copies with eligible opaque fields, original namespace never rebound |
| I/O/publication semantics | Async-only/short I/O, preflight empty output, cancellation after partial emission/temp creation, source emission read fault, race-created winner preserved, temporary cleanup, borrowed streams remain open |

Core range arithmetic/framing/graph-cycle/overlap/unknown-width/BigTIFF/DNG and
unsupported accounting regressions remain in M15 tests. Closed preserved schema
was audited against the exact contract, not inferred from M15.Known/IsSupported.
The full source prefix is copied; neither images nor arbitrary gaps are staged as
metadata. Only bounded copy/hash buffers and the charged replacement tail are used.

Logical resource charges are recorded in the [normative write review](../design/STANDALONE_TIFF_WRITE_NORMATIVE_REVIEW.md).
Audits fixed (1) missing output active-range ceiling, preserving deduplication of
unchanged external aliases; (2) malformed Gregorian target date read/replacement;
(3) snapshot dictionary copying preceding its own logical budget check. Regressions
cover all three. These tighten the approved policy, not widen it. Source M15
charges plus planner original/staged charges are not exact CLR allocation evidence.

## Independent tools and fixture rights

Owned public-API generator emits original, edited Software/Artist, exact no-op and
removed TIFF variants for all four II/MM strip/tile baselines. Harness requires
Exiv2 0.27.6 and ExifTool 12.76 facts; reverse ExifTool modifications are reread and
edited through the public API. An independent Python inspector verifies active
sorted entries/types/counts, pointer/range extents, copied prefix and fixed coded
image bytes/positions. Removal tests demonstrate both active absence and physical
retention. No reference warning classifier was weakened to get a green result.
The broader synthetic accounting matrix is unit-tested, not represented as
externally validated compressed images. No camera corpus/external copyrighted
image/GPL implementation or tests were imported. Reference CLIs remain black boxes;
fixture provenance and existing notice policy remain applicable.

## Residual restrictions and next gate

- This is a closed baseline descriptive editor, not full TIFF/Exif compliance or a
  general camera-file relocation engine. Hidden vendor conventions are not certified.
- Old data persists. Repeated edits grow files. All streams/handles/concurrent
  source mutations/filesystem aliases remain subject to the documented boundaries.
- A pure planner address-boundary test is not an end-to-end 4-GiB I/O test. Header-
  mutated codec selectors are not decoder-validity tests. Tests are bounded evidence,
  not proof against every adversarial input, OOM, crash or power loss.
- Final complete-head Windows/Linux/macOS build/test and mandatory reference gates
  are required before readying/merging PR #11. No release/package upload is authorized.
- M17 is DNG feasibility/contracts ONLY; a separate implementation plan needs
  approval. M18 is a measured optimization/packaging audit, not release permission.
