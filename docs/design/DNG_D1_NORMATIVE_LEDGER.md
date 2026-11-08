# DNG D1 normative field ledger (approved profile)

Status: **Gate 1 of D1 (D0 §5) closed by this document.** Binding for the D1
implementation of the narrow read-only DNG profile. It refines, and never widens,
the DNG D0 contract record
(`docs/design/DNG_D0_READ_ONLY_CONTRACT_PROPOSAL.md`). Any change to a
public/subset/safety promise requires the D0 revision-and-reapproval path
(D0 preamble); editorial clarifications that keep promises identical may be
made in place with a dated note. This ledger is not a conformance certificate
and certifies nothing about calibration, color science or render fidelity.

Normative basis (unchanged from D0 §1): Adobe DNG Specification 1.7.1.0
(September 2023), pinned mirror blob `5e4d714a2aa6b935286c6177991a72768fbaec0c`,
SHA-256 `9cdfe43d44751b662d1f0ace81fff84f6a927321f5d18d79054ce78dd499abcd`
(see `docs/design/DNG_SCOPE.md`). TIFF 6.0 and TIFF/EP apply to the shared
framing only (II/MM byte order, magic 42, IFD record layout, strip model); they
do not license DNG-specific interpretation. The 1.1.0.0 feature subset was
selected from the 1.7.1.0 prose; the cover edition/date wording is recorded in
D0 §1 as an editorial inconsistency and is not authoritative for version bytes.
No SDK, tag database, sample corpus or imported test content was used; review
used prose only. No restricted specification text is reproduced here.

## 1. Version identity fields (root, both shapes)

| Field | Tag | Type/Count | Rule | Violation |
|---|---|---|---|---|
| DNGVersion | 50706 | BYTE/4 | Required on the IFD0 root. Exact `01 01 00 00`. | Present with other type/count, or on a directory that cannot establish framing limits → `InvalidDng`. Wrong value on a safely framed file → `UnsupportedVersion` issue (`IsSupported=false`), never accepted on backward-version evidence. Absent root DNGVersion → `UnsupportedFeature` (ordinary TIFF), no sniffing by extension. |
| DNGBackwardVersion | 50707 | BYTE/4 | Optional on the root. Value must be `01 00 00 00` or `01 01 00 00`; if absent, effective value is DNGVersion with Patch/Build zeroed (`01 01 00 00`). | Present with other type/count → `InvalidDng`. Value outside the set, or lexicographically greater than DNGVersion → `UnsupportedVersion` issue. |
| (mirror rule) | 50706/50707 | — | Shape B raw leaf may duplicate identity fields. Duplicates must satisfy the same type/count/value rules; a value mismatch between root and raw leaf → `UnsupportedVersion` issue. Structural impossibility → `InvalidDng`. |

Lexicographic comparison is byte-wise on the 4 version bytes. A safely framed
newer version (e.g. `01 04 00 00`) is inventoried and reported with
`UnsupportedVersion`; it is never silently accepted as compatible.

## 2. Directory model and roles

Exactly one accepted graph per file, in one of two shapes. Both shapes require
classic framing, source ≤ 4 GiB, and the captured option limits.

| Shape | Root (IFD0) | Raw leaf | Other nodes |
|---|---|---|---|
| A — raw-rooted | The CFA raw IFD itself, and the sole IFD (`nextOffset == 0`) | == root | None. Any SubIFD/next link → profile refused (`UnsupportedDirectoryRole`). |
| B — RGB thumbnail + raw leaf | One 8-bit RGB uncompressed thumbnail (NewSubFileType 1) | Exactly one SubIFD (330, LONG/1) pointing to one raw leaf (NewSubFileType 0) | No other SubIFD, no next-IFD chain, no metadata-only EXIF/GPS/Interop directory, no second/shared raw role. Recognized-but-extra nodes are inventoried with issues and `IsSupported=false`; unrecognized framing nodes fail `InvalidTiff`. |

Both shapes: all structural next-IFD pointers are zero; every recognized link is
bounded and accounted before a supported result can be returned; links retain
pointer-slot order; directories are root-first discovery order; duplicate tags
fail framing; cycles, dangling and out-of-file links fail framing; compatible
external field aliases count once in accounted ranges; partial aliases and
image/structure/value overlaps refuse; shared graph targets are parsed once but
are outside the accepted profile even when safely inventoriable.

## 3. Field ledger — common (any accepted directory)

| Field | Tag | Type/Count | Rule | Violation |
|---|---|---|---|---|
| Orientation | 274 | SHORT/1 | Optional. Only `1` accepted. | Other value → `InvalidSemanticValue` issue |
| Software | 305 | ASCII/n | Optional. Printable ASCII, exactly one trailing NUL. | Violation → `InvalidSemanticValue` issue. Type/count mismatch vs. whitelist → `UnsupportedFieldShape` issue |
| Artist | 315 | ASCII/n | Same rule as Software. | Same |
| (any other tag) | — | — | **Whitelist only.** No field outside this ledger is accepted without `UnsupportedExtension`. | issue `UnsupportedExtension` |

## 4. Field ledger — root identity and color (both shapes)

Required on the IFD0 root in both shapes. For Shape A this is the raw IFD; the
same rules apply to Shape B's RGB root.

| Field | Tag | Type/Count | Rule | Violation |
|---|---|---|---|---|
| DNGVersion | 50706 | BYTE/4 | Exact `01 01 00 00` | See §1 |
| DNGBackwardVersion | 50707 | BYTE/4 | `01 00 00 00` or `01 01 00 00`; effective default = Version with Patch/Build zeroed | See §1 |
| UniqueCameraModel | 50708 | ASCII/n | Required. Nonempty printable ASCII with exactly one final NUL. | Absent → `MissingRequiredField`; empty/unprintable/NUL-missing → `InvalidSemanticValue` |
| ColorMatrix1 | 50721 | SRATIONAL/9 | Required. Exactly 9 values; every denominator strictly positive. Numerators may be negative (SRATIONAL by design); zero denominators forbidden. | Type/count → `UnsupportedFieldShape`; zero denominator → `InvalidSemanticValue` |
| CalibrationIlluminant1 | 50778 | SHORT/1 | Required. Exactly `21` (D65). | Other value → `InvalidSemanticValue` |
| AsShotNeutral | 50728 | RATIONAL/3 | Required. Exactly 3 values; numerator and denominator strictly positive (no zero neutral). | Zero numerator/denominator → `InvalidSemanticValue` |
| DefaultCropOrigin | 50719 | LONG/2 | Optional, raw leaf only (Shape A root == raw leaf; Shape B SubIFD); **paired** with DefaultCropSize on the same directory. Origin values fit the raw bounds. On a thumbnail root the pair is out of whitelist → `UnsupportedExtension`. | Unpaired → `InvalidSemanticValue` |
| DefaultCropSize | 50720 | LONG/2 | Optional, raw leaf only; paired with DefaultCropOrigin. Positive-sized, fits the active area (whole raw bounds if ActiveArea absent). | Unpaired / non-positive / out-of-fit → `InvalidSemanticValue` |

No color-science, calibration or linearization claim follows from these checks;
they establish only structural presence and sign/positivity constraints.

## 5. Field ledger — CFA raw leaf

Applies to Shape A (the root) and to Shape B's SubIFD raw leaf.

| Field | Tag | Type/Count | Rule | Violation |
|---|---|---|---|---|
| NewSubFileType | 254 | LONG/1 | Exactly `0` (full-resolution raw). | Wrong type/count → `UnsupportedFieldShape`; other value → `InvalidSemanticValue` |
| ImageWidth | 256 | LONG/1 | Explicit, positive, even; ≤ `MaxRawPixels` pixels in product with Height. | Other → `InvalidSemanticValue`; overflow/limits → `LimitExceeded` |
| ImageLength | 257 | LONG/1 | Same rule. | Same |
| BitsPerSample | 258 | SHORT/1 | Exactly `16`. | Other → `UnsupportedSampleLayout` |
| Compression | 259 | SHORT/1 | Exactly `1` (no compression). | Other → `UnsupportedCompression` |
| PhotometricInterpretation | 262 | SHORT/1 | Exactly `32803` (CFA). | Other → `UnsupportedSampleLayout` |
| StripOffsets | 273 | LONG/n | n = ceil(Height / RowsPerStrip); LONG arrays (single entry is n=1). Numeric order retained; physical contiguity not assumed. No bytes are staged or decoded. | Wrong type → `UnsupportedFieldShape`; count mismatch → `InvalidSemanticValue`; out-of-file/overlapping accounted regions → `InvalidTiff` throws / profile issues per D0 §4 |
| SamplesPerPixel | 277 | SHORT/1 | Exactly `1`. | Other → `UnsupportedSampleLayout` |
| RowsPerStrip | 278 | LONG/1 | Positive. n derived as above; per-strip bytes exactly `Width * ActualRows * 2` under checked arithmetic. | Wrong type/zero → `InvalidSemanticValue`; arithmetic overflow → `LimitExceeded` |
| StripByteCounts | 279 | LONG/n | Count equals StripOffsets count; each entry exactly `Width * ActualRows * 2`. | Mismatch → `InvalidSemanticValue` |
| PlanarConfiguration | 284 | SHORT/1 | Exactly `1`. | Other → `UnsupportedSampleLayout` |
| SampleFormat | 339 | SHORT/1 | Exactly `1` (unsigned integer). | Other → `UnsupportedSampleLayout` |
| CFARepeatPatternDim | 33421 | SHORT/2 | Exactly `[2, 2]`. | Other → `InvalidSemanticValue` |
| CFAPattern | 33422 | BYTE/4 | Exactly one red (`0`), two green (`1`), one blue (`2`); arrangement free. | Other → `InvalidSemanticValue` |
| CFAPlaneColor | 50710 | BYTE/3 | Exactly `[0, 1, 2]`. | Other → `InvalidSemanticValue` |
| CFALayout | 50711 | SHORT/1 | Exactly `1` (rectangular). | Other → `InvalidSemanticValue` |
| BlackLevelRepeatDim | 50713 | SHORT/2 | Exactly `[1, 1]`. | Other → `InvalidSemanticValue` |
| BlackLevel | 50714 | RATIONAL/1 | Exactly `0` (numerator 0), denominator strictly positive. | Zero denominator → `InvalidSemanticValue`; nonzero → `InvalidSemanticValue` |
| WhiteLevel | 50717 | LONG/1 | Exactly `65535`. | Other → `InvalidSemanticValue` |
| ActiveArea | 50829 | LONG/4 | Optional. `top < bottom`, `left < right`, within raw bounds. | Bounds/order violation → `InvalidSemanticValue` |

No tile arrays (TileWidth/TileLength/TileOffsets/TileByteCounts) are accepted;
their presence → `UnsupportedSampleLayout` issue (recognized but out of profile).
No linearization, masked area, alternate matrices/illuminants, opcode, digest,
private tag or embedded-original is accepted; all → `UnsupportedExtension`.

## 6. Field ledger — RGB thumbnail (Shape B root only)

| Field | Tag | Type/Count | Rule | Violation |
|---|---|---|---|---|
| NewSubFileType | 254 | LONG/1 | Exactly `1`. | Wrong type/count → `UnsupportedFieldShape`; other value → `InvalidSemanticValue` |
| ImageWidth | 256 | LONG/1 | Positive, even; ≤ `MaxThumbnailPixels`. | Other → `InvalidSemanticValue`; overflow → `LimitExceeded` |
| ImageLength | 257 | LONG/1 | Same rule. | Same |
| BitsPerSample | 258 | SHORT/3 | Exactly `[8, 8, 8]`. | Other → `UnsupportedSampleLayout` |
| Compression | 259 | SHORT/1 | Exactly `1`. | Other → `UnsupportedCompression` |
| PhotometricInterpretation | 262 | SHORT/1 | Exactly `2` (RGB). | Other → `UnsupportedSampleLayout` |
| StripOffsets | 273 | LONG/n | n = ceil(Height / RowsPerStrip). | Same as §5 |
| SamplesPerPixel | 277 | SHORT/1 | Exactly `3`. | Other → `UnsupportedSampleLayout` |
| RowsPerStrip | 278 | LONG/1 | Positive. | Same as §5 |
| StripByteCounts | 279 | LONG/n | Each entry exactly `Width * ActualRows * 3`; sum ≤ `MaxThumbnailBytes` (default 61,440). | Mismatch → `InvalidSemanticValue`; sum exceeded → `LimitExceeded` |
| PlanarConfiguration | 284 | SHORT/1 | Exactly `1`. | Other → `UnsupportedSampleLayout` |
| SampleFormat | 339 | SHORT/3 | Exactly `[1, 1, 1]`. | Other → `UnsupportedSampleLayout` |
| SubIFD | 330 | LONG/1 | Exactly one value: the raw leaf offset. Pointer-slot retained in Links. | Other count/type → `UnsupportedFieldShape`; dangling/duplicate/shared → framing `InvalidTiff` |

The thumbnail is an uncompressed image, not an embedded JPEG and not a
regenerated preview; its pixels are never staged or decoded.

## 7. Error model mapping

| Condition | Outcome |
|---|---|
| Argument/option/stream-capability misuse; null nested options; non-positive ceilings | `ArgumentException` family |
| Malformed TIFF framing: bad magic/order, IFD bounds, duplicate tags, cycles, dangling links, partial aliases, overlaps, out-of-file pointers, unknown field widths | `InvalidTiff` throws; unknown field widths/BigTIFF/non-DNG inputs/absent root DNGVersion → `UnsupportedFeature` |
| Malformed DNG identity fields (50706/50707 present with wrong type/count, impossible root) | `InvalidDng` |
| Safely framed but out-of-profile (wrong version, CFA/color/strip/semantic violations, missing required tag, unsupported extension, duplicate roles) | owned layout, `Issues` non-empty, `IsSupported=false`, `Image=null` where geometry is not safely establishable |
| Finite ceilings exceeded (pixels, bytes, directories, ranges, logical budget) | `LimitExceeded` |
| Physical I/O failure | propagated exception |
| Cooperative cancellation | `OperationCanceledException` |
| `IsSupported == true` | iff every required profile rule holds and `Issues` is empty |

## 8. Explicit non-goals of this ledger

No compression of any kind, no packed/float/linear raw, no alpha, no tiles, no
BigTIFF, no MakerNote, no DNGPrivateData/MakerNoteSafety, no raw digests/unique
IDs/embedded originals, no opcodes, no extra camera profiles/DCP, no XMP/IPTC/ICC,
no semantic masks, proxy/depth/enhanced images, no unknown optional extensions,
no rendering or editing. A flag claiming private-data safety proves nothing and
is ignored. No parity or completeness claim is made.

## 9. Root-versus-child association (summary)

Root (both shapes): 50706, 50707 (optional), 50708, 50721, 50728,
50778 + role-specific image fields (Shape A: §5 raw; Shape B: §6 thumbnail) +
common §3. Raw leaf (Shape B only): §5 set, may duplicate 50706/50707/50708/
50721/50728/50778 under the §1 mirror rule; may also carry §3 common fields.
The DefaultCropOrigin/DefaultCropSize pair and ActiveArea are raw-leaf-only;
on a Shape-B thumbnail root they are rejected as `UnsupportedExtension`.
CFA tags and BlackLevel/WhiteLevel live on the raw leaf only. ActiveArea lives
on the raw leaf only. No field moves roles implicitly; an unexpected role for a
§4/§5 field → `UnsupportedDirectoryRole`.

## 10. Known gaps accepted at this gate

- No raw-decode oracle exists for this profile (see DNG_D1_FIXTURES_AND_ORACLES
  §"Oracle gap"): byte accounting is verified; decoding is explicitly out of scope.
- Calibration correctness (ColorMatrix1 fitness, illuminant chromatism) is not
  verifiable by structural checks and is not claimed.
- The profile rejects otherwise-valid DNGs; this is intentional (D0 preamble).
- Real-world camera corpus coverage is not claimed or measured.

## 11. Maintenance

Append discovered-during-review items with date and commit. Do not delete or
rewrite accepted rules; supersede via D0 revision if a public/subset/safety
promise changes. Fixture hashes and pinned tool versions live in
`tests/fixtures/dng/provenance.json` and `tools/reference/harness.py`; this
document never pins mutable data.
