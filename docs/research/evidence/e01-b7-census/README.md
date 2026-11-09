# E01 B7 — read-only pinned observations and explicit gaps

This is a **bounded local rehearsal**, not a complete capability claim or a
MetadataSharp verification. `observations.json` was produced by
`tools/reference/observe_b7.py` at repository commit
`1a6d93201ead56586daa5ed0dc7068f7d0962866`, against Exiv2 0.28.9 pinned source
`b0859fc61fea5dedfc898aece6b15893ddf4314e`, and ExifTool 12.76 pinned source
`212f02cf22b9484674752181cad3b73089ce9694`. SHA-256 of the committed local
observation JSON: `08e52b583e1d188b73c9e9dce65c12e4857db17033d72de906adca0fa22823ce`.
The local Exiv2 build's effective CMake options, verbose version, executable
hash, individual commands, return codes, stderr, and secondary results are in
that JSON. CI reruns both references; local results are not assumed identical
until the CI output is recovered and compared (see receipt).

## CI reconciliation

PR #43 merged as `514ed0b`; pull-request run 42707052713 passed 15/15 and
regenerated all five fixture bytes. The complete report was recovered from three
check-run annotation chunks and preserved as `ci-observations.json`. All 40
observation objects match after replacing absolute target paths in stderr; the
blocked rows and fixture manifest match exactly. ExifTool JSON matches after
excluding environment-dependent System fields and SourceFile. See `receipt.json`
for exact hashes and artifact metadata. The ZIP artifact bytes were not retrieved.

## Scope

48 census rows: inventory, six read-family probes, and thumbnail extraction
for each of AVIF, HEIF, HEIC, CR3 and the two JXL layouts. The owned small
fixture catalog, per-format rights review, inputs, pinned external encoder
wheels and recipe are in `tests/fixtures/b7/` and
`docs/research/E01_B7_RIGHTS_AND_ORACLE_REVIEW.md`.

- AVIF, HEIF, HEIC and JXL container have authored EXIF Artist `B7Artist` and
  XMP CreatorTool `B7Tool`; both Exiv2 and ExifTool printed them. The two HEIF
  and HEIC files are separately encoded inputs but **both carry HEIC/HEVC**;
  this is not a claim of independent generic-HEIF codec coverage.
- IPTC, ICC, comments and thumbnails were **not authored** into these files.
  Those reads/extraction probe absent carriers only. A zero exit or empty
  output on an absent carrier is not a successful metadata extraction or
  evidence of support. The naked JXL fixture contains no metadata boxes.
- The JXL codestream and container are separate. On the naked codestream,
  Exiv2 returned 1 and unknown image type for all eight operations; ExifTool
  identified JXL Codestream and reported 8×8. On the container both tools
  identified/read the authored metadata. The JXL encoder's own libjxl decoder
  reproduced the input pixels, but is not an independent decoding oracle.
- Exiv2 and ExifTool reported 64×64 for the HEIC/HEIF coded spatial extent;
  the local libheif-based display decode reported 8×8. The encoder supplied an
  8×8 input and an 8×8 clean aperture. Coded canvas and displayed size are
  distinct; neither observation should silently overwrite the other.
- The 40 runnable read-only attempts left input bytes unchanged: 32 returned
  zero (`accepted`, **record-only**, including empty/no-thumbnail cases), and
  8 naked-JXL operations returned nonzero (`refused`). Eight CR3 rows are
  `blocked-rights`: no safe source/fixture was established; no CLI attempt is
  falsely claimed. No exclusion is signed and no blocked row is deleted.
- AVIF/HEIF/HEIC decoded at 8×8 using locally available ImageMagick/libheif;
  HEIF/HEIC used another libheif version for encoding, so this is not a fully
  independent codec-family oracle. No full ISO/IEC conformance, EXIF/IPTC/XMP
  extent proof, HEVC patent clearance, full JXL metadata coverage, valid CR3
  recognition, coded payload preservation under writes, or broad format support
  is claimed. MetadataSharp verification remains `not-assessed`.

The helper's top-level box/bitstream hashes and `inputUnchanged` are not a
complete ISO-BMFF item-reference oracle. `noDocumentedClaim` cases remain
visible; CR3's no-claim attempts remain **unperformed** while rights-blocked.
The census denominator is not frozen, and no coverage percentage is implied.
