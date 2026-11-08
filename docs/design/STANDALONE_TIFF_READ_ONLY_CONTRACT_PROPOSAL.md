# M15: Read-Only Standalone Classic TIFF Contract Proposal

Status: **APPROVED by the developer on 2026-11-07; implementation in progress.**
Date: 2026-11-07. The developer requested attempting M15 after PNG/WebP. This
proposal makes the previously unspecified public API and safety boundary explicit.
The PNG/WebP approval does not approve these new contracts. Start implementation
only after explicit approval and the M13–M14 final predecessor gate/merge.

## 1. Deliberately separate scope

M15 is an immutable, read-only *image-layout accounting* snapshot, not a TIFF
writer, image decoder, full metadata reader or lossy projection onto ExifDirectoryKind.
Do not reuse the JPEG-sized EXIF reader as a whole-file TIFF parser, lift its
65,527-byte cap, change JPEG/PNG/WebP signatures or introduce a public common base.
No BigTIFF, DNG, GeoTIFF semantics, private-pointer relocation or package release.

A static TiffLayout.Read[Async] operation owns its result, not a retained source.
Path sources close before returning; borrowed streams stay open, final position is
unspecified. A returned snapshot can outlive or be shared independently of the
source. There are no subsequent source-dependent read/save methods, IDisposable,
raw pixel access or implicit EXIF/XMP/IPTC reconciliation. Concurrent mutation of
an input during the read is unsupported; there is no filesystem-lock/checksum
identity guarantee for future editing. M16 needs its own approved source/safety API.

## 2. Exact proposed public surface

Namespace `MetadataSharp.Formats.Tiff`; existing Endianness and MetadataLimits are
from MetadataSharp.Core. Unless shown with a public constructor, model constructors
are internal. Collections are immutable owned/read-only views, never mutable lists
or byte arrays. Descriptors expose ranges and recognized relationships, not copied
arbitrary field payloads. No semantic EXIF values are promised by this interface.

```csharp
public sealed record TiffReadOptions
{
    public MetadataLimits Limits { get; init; } = new();
    public int MaxDirectoryLinks { get; init; } = 4_096;
    public int MaxAccountedRanges { get; init; } = 16_384;
}

public readonly record struct TiffByteRange(long Offset, long Length);
public enum TiffStorageKind { Strips, Tiles }
public enum TiffDirectoryLinkKind { NextIfd, SubIfd, Exif, Gps, Interoperability }
public enum TiffRangeKind { Header, Directory, FieldValue, Strip, Tile, JpegPreview }
public enum TiffLayoutIssueCode
{
    UnsupportedCompression, UnsupportedSampleLayout, UnsupportedPointerLayout,
    UnknownFieldSemantics, MissingImageLayout
}

public sealed class TiffFieldInfo
{
    public ushort TagId { get; }
    public ushort FieldType { get; }
    public uint Count { get; }
    public bool IsInline { get; }
    public TiffByteRange ValueRange { get; }
}
public sealed class TiffDirectoryLink
{
    public TiffDirectoryLinkKind Kind { get; }
    public ushort? TagId { get; } // null for the next-IFD structural pointer.
    public long PointerOffset { get; } // Absolute location of this 4-byte pointer.
    public uint TargetIfdOffset { get; }
}
public sealed class TiffAccountedRange
{
    public TiffRangeKind Kind { get; }
    public uint? DirectoryOffset { get; }
    public ushort? TagId { get; }
    public TiffByteRange Range { get; }
}
public sealed class TiffLayoutIssue
{
    public TiffLayoutIssueCode Code { get; }
    public uint DirectoryOffset { get; }
    public ushort? TagId { get; }
    public string Message { get; } // Diagnostic, not a stable machine discriminator.
}
public sealed class TiffImageLayout
{
    public uint Width { get; }
    public uint Height { get; }
    public ushort Compression { get; }
    public TiffStorageKind Storage { get; }
    public IReadOnlyList<TiffByteRange> Segments { get; } // File order of array entries.
    public TiffByteRange? JpegPreview { get; }
    public bool IsSupported { get; } // Accounting subset, not pixel decodability.
}
public sealed class TiffDirectoryInfo
{
    public uint Offset { get; }
    public TiffByteRange DirectoryRange { get; }
    public IReadOnlyList<TiffFieldInfo> Fields { get; }
    public IReadOnlyList<TiffDirectoryLink> Links { get; }
    public TiffImageLayout? Image { get; } // null for a metadata-only directory.
}
public sealed class TiffLayout
{
    public static TiffLayout Read(string path, TiffReadOptions? options = null);
    public static TiffLayout Read(Stream source, TiffReadOptions? options = null);
    public static Task<TiffLayout> ReadAsync(string path,
        TiffReadOptions? options = null, CancellationToken cancellationToken = default);
    public static Task<TiffLayout> ReadAsync(Stream source,
        TiffReadOptions? options = null, CancellationToken cancellationToken = default);
    public Endianness ByteOrder { get; }
    public long SourceLength { get; }
    public uint FirstIfdOffset { get; }
    public IReadOnlyList<TiffDirectoryInfo> Directories { get; }
    public IReadOnlyList<TiffAccountedRange> Ranges { get; }
    public IReadOnlyList<TiffLayoutIssue> Issues { get; }
    public bool IsSupported { get; } // All reported image layouts supported; no issues.
}
```

Append InvalidTiff to MetadataErrorCode without renumbering existing values.
InvalidTiff: malformed framing, cycles, duplicate tags, conflicting image storage,
out-of-range/overflow, inconsistent pointer/count arrays or forbidden overlaps.
LimitExceeded: configured budgets. UnsupportedFeature: BigTIFF/DNG signatures,
unknown field widths/types or a fundamental structure that cannot be safely framed.
Safely framed but unsupported recognized compression/sample/pointer layouts return
an issue-bearing snapshot, never IsSupported=true. Keep normal argument/I/O and
cancellation exceptions. No partial snapshot is returned after an exception.

## 3. Framing, graph and pointer decisions requiring approval

- Classic TIFF 6.0, both endian orders, magic 42, 8-byte header, aligned nonzero
  first IFD, 32-bit offsets and 12-byte entries. First profile source at most 4 GiB;
  default MaxInputBytes remains 1 GiB. No BigTIFF/magic 43. A DNGVersion tag (50706)
  explicitly refuses DNG rather than treating it as an ordinary supported TIFF.
- Accept classic field types 1–12 only; validate count times width, inline versus
  out-of-line storage, all known extents and required word alignment. Unknown
  types (including extension type 13) refuse rather than guessing byte widths.
  Unsorted entries may be accepted, duplicate tag IDs are ambiguous/malformed.
- Traverse iteratively: root next-IFD chain, SubIFDs (330), Exif (34665), GPS
  (34853), Interoperability (40965), LONG pointers only. Validate scalar/array
  counts and null rules. Exif/GPS/Interop pointers are scalar nonzero when present;
  SubIFD array elements must be nonzero; zero next pointer terminates a chain.
  Expose absolute pointer locations, relationships and target directory offsets.
  If the entire graph has no candidate image layout, report MissingImageLayout;
  metadata-only child directories do not individually require an image.
- Repeated target IFD offsets may form an acyclic shared graph and are parsed once;
  cycles refuse. Limits apply to unique directories, every link and maximum path
  depth, including shared paths. Metadata-only directories need not claim pixels.
- Account header, IFD tables (including next pointers), every out-of-line field
  value and every supported image/preview range. Inline ranges remain inside
  their own entry and are not added as competing independent storage ranges.
- Reject image ranges overlapping headers/directories/field storage, partial
  value aliases and image/image overlaps, including identical duplicated strips.
  Exact shared out-of-line field-value ranges may be retained once only if type,
  count and extent agree. Shared graph targets refer to one directory record.
  These conservative overlap restrictions may refuse otherwise viewable TIFFs.
- Every declared range must fit the actual file; use checked wide arithmetic.
  Do not allocate or read pixel buffers just to account their extents. No claim
  about embedded pointers inside opaque/private values; report unknown semantics.

## 4. Initial support matrix constraining M16

| Layout/field group | M15 proposed handling |
| --- | --- |
| Strips: StripOffsets 273, StripByteCounts 279, RowsPerStrip 278 | SHORT/LONG scalar/arrays as TIFF allows, equal array counts, positive extents; derive expected strips from image/sample layout |
| Tiles: TileWidth 322, TileLength 323, TileOffsets 324, TileByteCounts 325 | Paired arrays and positive tile dimensions/extents; checked grid/plane count; no simultaneous strips |
| Dimensions/samples | ImageWidth 256, ImageLength 257, BitsPerSample 258, PhotometricInterpretation 262, SamplesPerPixel 277, PlanarConfiguration 284, SampleFormat 339 and ExtraSamples 338: decode only values needed to classify/count storage |
| Supported accounting profile | 8/16-bit unsigned grayscale or RGB, optional explicitly identified alpha, chunky or separate planes; strips and tiles; Compression 1 (uncompressed), 5 (LZW), 7 (new-style JPEG), 8/32946 (Deflate), 32773 (PackBits); encoded bytes are never decompressed or proven decodable |
| Subsampled, palette, bilevel, floating-point, signed or unfamiliar sample layout | Safely framed descriptors/ranges plus UnsupportedSampleLayout; no general rendering claim |
| JPEG preview 513/514 | Unique matched offset/length, bounded positive range, plus MaxThumbnailBytes; no JPEG decoding; label as a candidate preview, not proof of valid JPEG |
| JPEGTables 347 | Ordinary bounded field-value range, opaque, not relocated or decoded |
| Old JPEG Compression 6 and JPEGQTables/DCTables/ACTables 519/520/521 | UnsupportedCompression/UnsupportedPointerLayout; retain framed field descriptors, do not infer table lengths or claim complete target-range accounting |
| Unknown/private pointer-bearing fields, GeoTIFF/private extensions | UnknownFieldSemantics, no complete-accounting/rebuild claim; do not guess/follow integer-looking values |
| EXIF MakerNote, XMP/IPTC/ICC payload fields | Bounded opaque field descriptors only; not translated to semantic models or reconciled |
| BigTIFF, DNG | Refused; no implicit fall-through to classic TIFF support |

IsSupported means only the selected *range-accounting* profile, not absence of
all private pointers, image correctness, EXIF conformance or safe relocatability.
Unknown field semantics produce an issue and therefore false. No M15 result by
itself grants M16 write permission. M16 must approve its tag schema, opaque-field
policy, source identity, copy/relocation strategy and affected-offset proof.

## 5. Resource and acceptance gate

Capture/validate options before I/O. Use MetadataLimits for input bytes,
directories/depth/entries/value and retained bookkeeping budgets, combined
strip/tile/preview count via MaxSegments, preview size via MaxThumbnailBytes, and
copy buffers. Count descriptors, link arrays and accounted ranges logically against
MaxRetainedMetadataBytes before growth; describe the formula in implementation
review (not an exact managed-allocation claim). MaxDirectoryLinks and
MaxAccountedRanges are additional positive hard ceilings. Ordinary tests need no
new runtime/codec dependencies.

Before code: review exact TIFF 6.0 sections and applicable approved EXIF pointer
normative sources, record immutable source hashes/sections and resolve discrepancies.
A necessary safety/public change returns for approval, not an implementation guess.
Author synthetic byte fixtures with provenance. Acceptance must include little/big
endian, multi-IFD chains/shared DAGs/cycles, strips/tiles/planes/previews, recognized
metadata pointers, old JPEG/private/unknown sample/compression issue reporting,
zero/out-of-range/overlapping references, declared-count overflow, limits and bounded
mutation tests, owned result stability, short true-async I/O and cancellation.
Require all-platform build/tests and separately classified pinned CLI comparisons
where supported. Finish an implemented support matrix before proposing M16.
