# DNG D0: proposed exact read-only inventory contract

Status: **PROPOSED — not approved or implemented.** Programme direction was
approved; these new signatures/subset rules still need explicit approval. This is
E04/D0, not general DNG support. E02/E03 runtime/compatibility decisions and the
normative/fixture-oracle prerequisites below must pass before E05/D1 code.
Existing TiffLayout/TiffMetadataDocument DNG rejection stays unchanged.

## 1. Purpose and evidence boundary

A separate immutable snapshot inventories classic TIFF-framed DNG directories,
recognized raw/thumbnail strips and field extents. No retained source, raw field
value API, pixel reader/decoder, digest validation, editor, Save, disposal contract,
private-data relocation certificate or metadata synchronization. IsSupported means
only the narrow accounting/schema profile below, not render fidelity, camera
support, general DNG conformance or permission for future writing.

Normative starting point: DNG 1.7.1.0 September 2023 prose and exact mirror/hash in
DNG_SCOPE.md (blob 5e4d714a2aa6b935286c6177991a72768fbaec0c; SHA256
9cdfe43d44751b662d1f0ace81fff84f6a927321f5d18d79054ce78dd499abcd). Select a
1.1.0.0 feature subset from that edition; do not infer version bytes from its cover
(the p.24 1.7.0.0 wording is recorded as an editorial inconsistency). TIFF6/CIPA
source records remain applicable to shared framing, not DNG-specific interpretation.
No SDK algorithm/tag database/tests or restricted specification text is imported.

## 2. Exact proposed surface

Namespace MetadataSharp.Formats.Dng; existing Core Endianness/MetadataLimits and
Tiff TiffReadOptions/TiffByteRange/TiffFieldInfo/TiffDirectoryLink/
TiffAccountedRange are reused as neutral primitives only. No public common base.
Except options/version value type, model constructors are internal; all collections
are owned, immutable read-only views. No implementation accompanies these declarations.

```csharp
public sealed record DngReadOptions
{
    public TiffReadOptions TiffOptions { get; init; } = new();
    public long MaxRawPixels { get; init; } = 100_000_000;
    public long MaxThumbnailPixels { get; init; } = 1_048_576;
}
public readonly record struct DngVersion(byte Major, byte Minor, byte Patch, byte Build);
public enum DngImageRole { Unknown, CfaRaw, RgbThumbnail }
public enum DngLayoutIssueCode
{
    UnsupportedVersion, MissingRequiredField, UnsupportedFieldShape,
    InvalidSemanticValue, UnsupportedDirectoryRole, UnsupportedCompression,
    UnsupportedSampleLayout, UnsupportedExtension
}
public sealed class DngLayoutIssue
{
    public DngLayoutIssueCode Code { get; }
    public uint DirectoryOffset { get; }
    public ushort? TagId { get; }
    public string Message { get; }
}
public sealed class DngImageLayout
{
    public DngImageRole Role { get; }
    public uint Width { get; }
    public uint Height { get; }
    public ushort BitsPerSample { get; }
    public ushort SamplesPerPixel { get; }
    public IReadOnlyList<TiffByteRange> Strips { get; }
}
public sealed class DngDirectoryInfo
{
    public uint Offset { get; }
    public bool IsRoot { get; }
    public DngImageLayout? Image { get; }
    public IReadOnlyList<TiffFieldInfo> Fields { get; }
    public IReadOnlyList<TiffDirectoryLink> Links { get; }
}
public sealed class DngLayout
{
    public Endianness ByteOrder { get; }
    public long SourceLength { get; }
    public uint FirstIfdOffset { get; }
    public DngVersion Version { get; }
    public DngVersion BackwardVersion { get; }
    public bool IsSupported { get; }
    public IReadOnlyList<DngDirectoryInfo> Directories { get; }
    public IReadOnlyList<TiffAccountedRange> Ranges { get; }
    public IReadOnlyList<DngLayoutIssue> Issues { get; }
    public static DngLayout Read(Stream source, DngReadOptions? options = null);
    public static DngLayout Read(string path, DngReadOptions? options = null);
    public static Task<DngLayout> ReadAsync(Stream source,
        DngReadOptions? options = null, CancellationToken cancellationToken = default);
    public static Task<DngLayout> ReadAsync(string path,
        DngReadOptions? options = null, CancellationToken cancellationToken = default);
}
```

Proposal also appends **InvalidDng** to MetadataErrorCode (no changes to existing
numeric values). It means malformed DNG identity fields, not all unsupported feature
combinations. All of these additions require approval; no existing type signatures
or enum values are changed in this submission.

Fields expose declared typed-element Count and absolute ValueRange, not copies of
arbitrary raw data. Ranges are global offset-sorted; directory Fields retain source
entry order; directories are root-first discovery order; Links retain pointer-slot
order. Offsets are original-file-relative, never discovery indices. Duplicate tags,
cycles, dangling/out-of-file recognized links and illegal overlaps fail framing.
Compatible exact external field aliases count once in Ranges; partial aliases and
image/structure/value overlaps refuse. Shared graph targets are parsed once, but
are outside the accepted DNG profile, even if safe to inventory. Source tags may be
unsorted without rewriting or claiming baseline ordering conformance.

## 3. Narrow accepted accounting profile

These are deliberate profile restrictions, NOT a transcription of all DNG-required
fields or a claim that other legal DNG variants are invalid. Before implementation,
review every type/count/location/default against the pinned prose and separately
close TIFF/EP normative dependencies. Count/type-safe out-of-profile fields produce
issues; unknown field widths cannot be guessed.

- Classic II/MM, magic 42, source <=4 GiB and captured input limit. Root has
  DNGVersion 50706 BYTE/4 exactly 1.1.0.0. Optional 50707 BYTE/4 is 1.0.0.0 or
  1.1.0.0; if absent its effective value is Version with Patch/Build zero (1.1.0.0).
  Lexicographic backward version must not exceed Version. A safely framed newer
  version is inventoried with UnsupportedVersion, never accepted based only on a
  low DNGBackwardVersion. Present DNGVersion with incorrect type/count throws
  InvalidDng; an absent root DNGVersion is UnsupportedFeature (ordinary TIFF).
  No sniffing by .dng filename.
- Exactly one CFA raw IFD. Either root is raw and sole IFD, or root is one RGB
  thumbnail with a single LONG/1 SubIFD 330 pointing to one raw leaf. No next IFD
  chain, further children, duplicate/shared role, metadata-only EXIF/GPS/Interop
  directory or arbitrary private pointer scheme in the accepted profile. Framed
  extra recognized graph nodes can be returned only with issues/IsSupported=false.
- Raw image: explicit Width/Height LONG/1, positive/even dimensions; BitsPerSample
  SHORT/1=16, SamplesPerPixel SHORT/1=1, Compression SHORT/1=1, PhotometricInterpretation
  SHORT/1=32803 (CFA), PlanarConfiguration SHORT/1=1, SampleFormat SHORT/1=1,
  NewSubFileType LONG/1=0. No tile arrays; RowsPerStrip LONG/1 positive, strip offsets
  and byte counts LONG arrays with count ceil(height/rows). Byte count per strip
  exactly width * actual rows * 2 under checked arithmetic. Numeric strip order
  is retained; physical contiguity is not assumed. No strip bytes are read to decode.
- CFARepeatPatternDim 33421 SHORT/2=[2,2]; CFAPattern 33422 BYTE/4 has one red (0),
  two green (1), one blue (2); CFAPlaneColor 50710 BYTE/3=[0,1,2]; CFALayout 50711
  SHORT/1=1. UniqueCameraModel 50708 at root is nonempty printable ASCII+final NUL.
  Root ColorMatrix1 50721 SRATIONAL/9 with positive denominators;
  CalibrationIlluminant1 50778 SHORT/1=21 (D65). Raw BlackLevelRepeatDim 50713
  SHORT/2=[1,1], BlackLevel 50714 RATIONAL/1=0, WhiteLevel 50717 LONG/1=65535,
  root AsShotNeutral 50728 RATIONAL/3 with positive numerator/denominator. This profile
  requires these explicit shapes rather than silently applying general DNG defaults.
  Counting/denominator checks do not certify calibration/color science validity.
- Optional raw ActiveArea 50829 LONG/4 must fit bounds with top<bottom and left<right;
  DefaultCropOrigin 50719 LONG/2 and DefaultCropSize 50720 LONG/2 must occur together,
  be positive-sized and fit the active area (whole raw bounds if absent). No masked
  area table, linearization, alternate matrices/illuminants or other rendering tags.
- Thumbnail, if present: explicit NewSubFileType LONG/1=1, Photometric SHORT/1=2,
  BitsPerSample SHORT/3=[8,8,8], SamplesPerPixel SHORT/1=3, Compression SHORT/1=1,
  PlanarConfiguration SHORT/1=1, SampleFormat SHORT/3=[1,1,1]; dimensions/strips are
  LONG as above, exact packed RGB byte counts (width * actual rows * 3). It is an
  uncompressed image, not an embedded JPEG candidate or regenerated preview.
- Both roles may contain Orientation SHORT/1=1 and Software/Artist ASCII with one
  final NUL and preceding printable bytes. No date/orientation edits, value renderer
  or normalized metadata projection. No field other than the whitelist in this
  section is accepted without UnsupportedExtension, including harmless-looking
  private tags. Structural next pointers are zero and all recognized references
  remain bounded/accounted before a supported result can be returned.
- Exclude all compression, packed/float/linear raw, alpha, tiles, BigTIFF, MakerNote,
  DNGPrivateData/MakerNoteSafety, raw digests/unique IDs/embedded originals, opcodes,
  extra camera profiles/DCP, XMP/IPTC/ICC, semantic masks, proxy/depth/enhanced images
  and unknown optional extensions. A flag claiming private safety is not a proof.

## 4. Resource, I/O, error and lifetime contract

Capture/validate all options before first await; nested options/limits cannot be
null. Pixel ceilings positive; width*height checked before role model creation;
no pixel-count-sized allocation. Total thumbnail strip bytes also obey captured
MaxThumbnailBytes (default 61,440); uncompressed thumbnail bytes are not staged.
TiffOptions retains existing finite defaults:
1-GiB input, 1-MiB logical metadata, 16 directories/depth 8, 512 entries/IFD,
2048 total entries, 65527 bytes/value, 4096 segments, 4096 links, 16384 accounted
ranges and 64-KiB copy buffer. These can deliberately refuse otherwise valid DNG.
Logical charge before growth: 128/directory + (64+4*Count)/field + 48/link +
16/strip + 64/accounted range + 96/issue + 64/image descriptor. External alias
ranges deduplicate only range charges; each field descriptor remains charged.
String/numeric staging fits captured MaxValueBytes and is charged before allocation.
This is finite logical accounting, not a measured CLR/peak-memory guarantee.

Readable/seekable borrowed stream must start at zero; stays open on success/failure,
final position unspecified. Paths close before return/failure. Async uses actual
async reads with short-read loops and cooperative cancellation, not Task.Run.
No concurrent source mutation or source use; no content checksum, retained stream
or editing-identity promise. Results survive source mutation/disposal; no partial
result on failure. Layout access never performs later I/O. No output/temp path exists.

Argument/option/stream-capability errors are argument exceptions; malformed TIFF
framing/ranges are InvalidTiff; invalid DNG identity fields are InvalidDng;
unknown field widths/BigTIFF/non-DNG input are UnsupportedFeature;
finite bounds are LimitExceeded. Physical I/O exceptions propagate and cancellation
throws OperationCanceledException. A structurally safe but unsupported profile is
an owned layout with issues, IsSupported=false and Image=null where geometry cannot
be safely established. That result does NOT claim every opaque internal pointer
or image dependency is accounted. IsSupported=true iff all required profile rules
hold and Issues is empty. Diagnostic Message is not a stable machine discriminator.

## 5. Gates before approval/code and independent validation

API approval is necessary but not alone sufficient to start D1:

1. Close a field-by-field normative type/count/usage/default ledger, including
   TIFF/EP dependencies and CFA root-vs-child associations. The narrow choices above
   are explicit proposals, not claimed completed conformance review. If review
   changes a public/subset promise, revise/reapprove before code, not during tests.
2. Author both valid shapes, II/MM, from prose with owned synthetic pixels and
   independently computed sample/hash/known-byte directory/range expectations.
   A TIFF fixture with only DNGVersion added is insufficient. Pin minimum two
   complementary tools where feasible (metadata inventory plus raw/conformance
   oracle), versions/builds/licenses/provisioning. No tool or fixture is approved
   merely by naming Exiv2, ExifTool, LibRaw or DNG validator. Report oracle gaps as
   blockers, not skips that count as successful DNG validation.
3. Test every whitelist shape/default/role boundary, version/backward mismatch,
   raw/thumbnail grid/bytecounts, CFA/calibration counts, zero denominators, unknown
   extensions, unsupported codecs, private/opcode/digest presence, cycles/dangling
   links/aliases/overlaps/uint overflow, source-read bans over pixel regions,
   budgets before allocation, async-only short reads, cancellation, owned snapshots
   and physical I/O failures. Required current regressions remain green.
4. E02 net8.0/net10.0 matrix and E03 compatibility decision must be available;
   retain existing JPEG/PNG/WebP/TIFF contracts and no production native/GPL deps.

Approval requested: exact proposed surface, read-only lifetime/error model and this
intentionally narrow profile, subject to the explicit normative/oracle gates above.
**No D1 implementation, D2 write API, digest algorithm or legacy-runtime support is
approved by this document.** Until explicit approval, stop at proposal/research.
