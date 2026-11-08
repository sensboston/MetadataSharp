# M15 normative review, support matrix and resource accounting

Developer approved the exact standalone read-only contract on 2026-11-07, after
M13–M14 PR #9 merged (269dc92cb5bf1218df230e1ced3140c95ca7d81c).
No EXIF/JPEG parser cap or writer contract is changed.

Reviewed TIFF 6.0 Final, June 3 1992: GitHub visualcommons/gamut mirror blob
b487a9b8dbb647b4d3407c72721394cb60112185, SHA256
dbcdf729182937ecff415dfd06806894bf03bfd741291aa3ad7ba45335673def.
Sections 2 (framing/types/alignment), 3–6 and field reference (grayscale/RGB,
strip ordering and counts, default rows per strip), 15 (tiles/planes), 19
(SampleFormat), 21 (alpha), 22 (old JPEG pointers/previews) were inspected as prose,
not reference implementation code. TIFF6 tile dimensions must be multiples of 16;
positive nonmultiples are safely accounted but reported UnsupportedSampleLayout,
not mislabeled baseline compliant. TileOffsets is LONG; strip offsets/counts and
tile byte counts allow SHORT/LONG. Sample and compression selectors require SHORT.

CIPA Exif 3.0 final 2023 pointer mechanics: mirror blob
83ed267755af54358149e8d99c93a89e4c26c72c, SHA256
d1020d7f04b05d635ee1a308330b2494586da24fd8e0bb6cd96214dbf60244e8;
section 4.6.3 pointer fields LONG,1, IDs 34665/34853/40965. No Exif3 full-schema
claim. SubIFD 330 LONG arrays are the explicitly approved extension, not a claim
that this extension is defined by the TIFF6 baseline. Publisher authenticity of
mirrored bytes is not claimed. No standards PDF, GPL code or external tests copied.

## Implemented accounting matrix

- Classic II/MM, 32-bit offsets, types 1–12, bounded values/entries, unsorted tags;
  duplicate tags, cycles, partial aliases and all image/structural overlaps refuse.
- Unique root/next/SubIFD/Exif/GPS/Interop graph: shared DAG targets parsed once;
  topological longest-path depth checks every shared path, not just discovery path.
- Strips, tiles and separate planes: paired positive extents and derived grid counts;
  supported sample subset unsigned 8/16-bit grayscale/RGB with optional explicit
  associated/unassociated alpha; compressed bytes are never decoded.
- Accounting compression codes 1/5/7/8/32946/32773 only. Old JPEG 6/table pointers,
  unsupported sample layouts and unknown semantics report explicit issues.
- Preview 513/514: bounded candidate JPEG extent, not a decoded image. JPEGTables
  field bounded opaque. MakerNote/XMP/IPTC/ICC are opaque descriptors, not semantic
  metadata. No inferred private pointers or inferred old JPEG table lengths.
- Exact shared external values permitted only with equal type/count/range, stored
  once; every field still exposes its own range descriptor. Inline ranges belong
  to their entry, not independent conflicting storage. Unreferenced gaps are not
  interpreted or certified safe for writing.
- BigTIFF, DNGVersion and unknown field widths refuse; no writer or general TIFF,
  DNG/GeoTIFF, entropy validation, relocatability or performance claim.

Logical bookkeeping charges (not exact managed allocation): 128 bytes/discovered
IFD, 64/field, 48/link, 64/unique accounted range, 96/issue, 4/decoded numeric value,
16/image segment descriptor. Charges precede growth. MaxDirectories/depth/entries,
MaxDirectoryLinks/MaxAccountedRanges, MaxSegments (combined strips/tiles/previews),
MaxThumbnailBytes and MaxValueBytes are independent finite ceilings. No pixel data
allocation occurs merely to account extents. Return models use owned read-only
collections; paths close before return and borrowed streams stay open.

M16 remains unapproved: IsSupported is only this selected range-accounting subset,
never authorization to relocate an opaque value or rebuild a TIFF. A separate
source-identity, tag/pointer schema and write strategy must be approved first.

Normative audit: TIFF6 section 2 explicitly requires at least one entry per IFD.
Zero-entry directories are InvalidTiff, including metadata-only graph nodes; a
metadata-only directory can instead contain an ordinary Software field. This is
framing validation, not a restriction on metadata-only child roles.
