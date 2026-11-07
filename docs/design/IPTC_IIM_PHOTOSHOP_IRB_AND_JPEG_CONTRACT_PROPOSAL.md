# M12: IPTC IIM, Photoshop IRB and JPEG Contract Proposal

Status: **APPROVED by the developer on 2026-11-07.**
Date: 2026-11-07. Predecessor: M10–M11 PR #7 merged with final CI green.

## 1. Bounded execution plan

Keep one M12 PR, with two implementation checkpoints:

1. **M12a:** owned raw IIM model, bounded reader/editor/serializer and internal
   Photoshop resource framing. Independent known-byte, hostile-input and preservation
   tests; require green three-platform CI before JPEG integration.
2. **M12b:** addressed JPEG APP13 integration and pinned external-reference checks.
   Require final-head CI before merging the whole PR.

Use managed framework APIs only; no new NuGet/native dependency. Review the exact
IPTC IIM specification edition, Photoshop image-resource specification and relevant
XMP Part 3 digest/storage sections before implementing. Record source, edition,
sections, hashes and discrepancies. No Adobe/Exiv2 implementation, GPL tests, bulk
field database or unreviewed photographs may be imported. This proposal is not a
claim that normative source review has already occurred. Any required contract
change found during review returns for approval rather than being silently applied.

## 2. Raw model and text profile

Preserve the ordered sequence of datasets, including repetitions, unknown dataset
IDs and raw payload bytes. Dataset identity is the record/dataset byte pair.
This is not a full IIM application-profile validator. Addressed editing is limited
to record 2; control datasets (including record 2 dataset 0) cannot be addressed.
Other records are retained, not interpreted or silently removed by property edits.
Unknown record-2 datasets may be replaced explicitly as raw bytes; callers remain
responsible for their field-specific semantics. No arbitrary pointer interpretation.

Text convenience operations cover only these six record-2 datasets: Object Name
(5), Keywords (25), By-line (80), Headline (105), Copyright Notice (116), and
Caption/Abstract (120). Verify exact byte-length/cardinality rules from the reviewed
edition before implementation and record the resulting validation table. Repeated
Keywords/By-line are retained; the convenience API enforces the reviewed cardinality
of each supported field. Raw operations remain explicitly raw, not a conformance
or encoding conversion promise.

Encoding is packet-wide:
- Unique record 1 dataset 90 containing ESC `%` `G` declares UTF-8.
- No declaration is **Unspecified**, never an implicit Latin-1 or Windows code page.
  Text reads/writes permit ASCII only in that case; high-bit text is refused.
- Other declarations are **Unsupported** for text operations. Raw reads/copy and
  addressed raw replacement remain available without changing the declaration.
- Duplicate charset declarations are ambiguous for IPTC parsing/editing. Do not
  choose one silently. Unsupported declarations are retained exactly.

CreateEmpty emits a minimal UTF-8 packet with the reviewed record-2 version and
charset controls; verify their exact bytes in the normative gate. Existing control
bytes remain unchanged. Text edits never introduce a declaration that would
reinterpret untouched legacy bytes. No EXIF/XMP synchronization, trimming, Unicode
normalization, date interpretation or guessed transcoding. Whole removal includes
all records in the targeted IPTC resource, not merely the six text fields.

## 3. Proposed public API

Types below are in `MetadataSharp.Core.Iptc`; namespace imports and bodies omitted.

```csharp
public readonly record struct IptcDatasetKey(byte Record, byte Dataset);
public enum IptcCharacterSet { Unspecified, Utf8, Unsupported }

public sealed class IptcValue
{
    // Factory deeply copies input; no exposed mutable buffers.
    public static IptcValue FromRaw(ReadOnlySpan<byte> bytes);
    public int ByteLength { get; }
    public void CopyRawTo(Span<byte> destination);
}

public sealed class IptcDataset
{
    // Instances are created by packet parsing/apply; no public constructor.
    public IptcDatasetKey Key { get; }
    public IptcValue Value { get; }
}

public sealed record IptcLimits
{
    public int MaxPacketBytes { get; init; } = 1_048_576;
    public int MaxDatasets { get; init; } = 4_096;
    public int MaxValueBytes { get; init; } = 1_048_576;
    public int MaxResources { get; init; } = 1_024;
}

public sealed class IptcReadOptions
{
    public IptcLimits Limits { get; init; } = new();
}

public sealed class IptcEdit
{
    public IptcEdit();
    public bool IsEmpty { get; }
    public void ReplaceRaw(IptcDatasetKey key, IEnumerable<IptcValue> values);
    public void ReplaceText(IptcDatasetKey key, IEnumerable<string> values);
    public void Remove(IptcDatasetKey key);
    public void RemoveAllIptc();
}

public sealed class IptcPacket
{
    public static IptcPacket CreateEmpty(IptcReadOptions? options = null);
    public static IptcPacket Parse(ReadOnlySpan<byte> bytes,
        IptcReadOptions? options = null);
    public IptcCharacterSet CharacterSet { get; }
    public IReadOnlyList<IptcDataset> Datasets { get; }
    public int ByteLength { get; }
    public void CopyRawTo(Span<byte> destination);
    public string GetText(int datasetIndex);
    public IptcPacket? Apply(IptcEdit edit);
}
```

Packets/values are deeply owned immutable snapshots. Limits/options are validated
and captured; nonpositive limits are argument errors. Factory/edit enumeration is
bounded by default aggregate byte/dataset budgets and copies mutable caller data.
A blocking caller enumerator is outside cancellation guarantees. GetText uses the
packet declaration and only the six supported textual fields; other fields refuse
interpretation. Index outside the list is an argument error. Raw payloads can always
be copied from successfully parsed packets, without interpreting text.

ReplaceRaw/ReplaceText replace **all occurrences of one key** in the caller's order,
at the first existing occurrence; a previously absent key is appended. Other
occurrences retain their relative order. Empty replacement means removal. Last
operation per key wins. Edit methods validate before mutation; contextual encoding
checks happen during Apply. ReplaceText does not silently synchronize other fields.
Untouched datasets retain their original framing/bytes, not just their values.
A missing-key removal alone is a byte-identical no-op. Removing the last ordinary
field leaves the retained control records; RemoveAllIptc returns null from Apply.
Whole removal cannot mix with addressed operations and is idempotent by itself.
No standalone path/sidecar API, public IRB editor or resource mutation API in M12.

JPEG additions to `MetadataSharp.Formats.Jpeg.JpegMetadataDocument`:

```csharp
public IptcPacket? ReadIptc(IptcReadOptions? options = null);
public Task<IptcPacket?> ReadIptcAsync(IptcReadOptions? options = null,
    CancellationToken cancellationToken = default);
public void SaveIptc(Stream destination, IptcEdit edit);
public Task SaveIptcAsync(Stream destination, IptcEdit edit,
    CancellationToken cancellationToken = default);
public void SaveIptcAs(string destinationPath, IptcEdit edit);
public Task SaveIptcAsAsync(string destinationPath, IptcEdit edit,
    CancellationToken cancellationToken = default);
```

Append `InvalidIptc`, `InvalidPhotoshopResources` and `IptcTooLarge` to the existing
error enum, without renumbering its current members. Reuse UnsupportedFeature,
LimitExceeded, AmbiguousMetadata and SourceChanged. Malformed framing is not an
incidental indexing/overflow exception. Unsupported text encoding and invalid
encoded text are distinguished: UnsupportedFeature versus InvalidIptc. Caller
argument, cancellation and I/O conventions follow existing EXIF/XMP APIs.

## 4. IIM length and preservation policy

Parse dataset markers, record/dataset IDs and lengths with checked bounds before
allocation. Support normal 15-bit lengths and bounded extended lengths with 1–4
additional big-endian length octets. Zero or impossible length-of-length is malformed;
unsupported widths above four are refused explicitly. Do not guess a value size,
accept truncated lengths or silently consume trailing garbage as padding.

For modified/new datasets emit a normal length when possible, otherwise the
shortest supported extended length. Untouched datasets keep their original encoding,
including nonminimal but supported extended-length framing. Packet/value/count
limits apply before allocation. Exact no-op preserves the entire original packet.

## 5. Photoshop IRB and JPEG scope

Recognize only APP13 payloads beginning with exact `Photoshop 3.0` plus one NUL.
Other APP13 data remains opaque. Inside recognized payloads support sequential
`8BIM` resources with big-endian ID and 32-bit data length, Pascal name padded to
even total name-field size, and resource data padded to even size. Validate all
remaining lengths/padding locations; unknown valid resource IDs are not errors.
Preserve untouched names, resources, ordering and padding bytes exactly, including
nonzero padding. New padding is zero. Unsupported signatures/layouts are refused,
not heuristically scanned for a hidden resource. PSB/8B64 and split resources across
APP13 segments are outside this profile.

IPTC resource ID is provisionally 0x0404; IPTC digest ID is provisionally 0x0425,
subject to exact normative verification. Photoshop XMP resource 0x0424 and unrelated
resources are never reserialized, removed or synchronized by IPTC operations.

For this conservative first profile, IPTC reads/effective edits support **one**
recognized Photoshop APP13 segment containing at most one IPTC resource and at most
one digest resource. Multiple recognized segments or duplicate target/digest resources
are ambiguous; do not pick the first or merge them. Reads return null if a valid
unique Photoshop segment has neither an IPTC resource nor an orphan digest. Unrecognized APP13 blocks do not
count toward that ambiguity. A unique valid segment without IPTC may receive a new
IPTC resource; without a Photoshop segment, insert a new one before SOF, without
reordering existing segments.

When replacing IPTC, recompute an existing valid 16-byte digest over the exact new
IIM resource data using framework MD5. Do not create a digest if none existed.
MD5 here is a compatibility checksum, not a security guarantee. An invalid-length
or orphan digest refuses effective edits/semantic reads rather than inventing its
meaning. A mismatching but well-formed digest does not prevent raw dataset reads;
rewriting establishes the new correct digest. Whole IPTC removal removes its
associated digest too, but no unrelated resources. When no resources remain, remove
the empty Photoshop APP13 segment; otherwise preserve its other resources byte-for-byte.

Malformed recognized IRB framing blocks IPTC reads/effective edits, including whole
removal, because resource boundaries are unknown. Malformed IIM inside uniquely
framed IPTC data may be removed whole, along with its associated valid digest, without
parsing its datasets. Empty edits preserve all bytes even with malformed/ambiguous
IPTC/IRB; they do not assert readability. EXIF/XMP-only operations continue treating
APP13 as opaque and do not start parsing or validating IPTC.

Rebuilt APP13 payload must fit 65,533 bytes **including** Photoshop header, all
resources/names/size fields and padding; otherwise fail IptcTooLarge before writes.
Do not split across markers or drop unrelated resources to make it fit.

## 6. Ownership, failure and acceptance gates

JPEG reads/saves use existing SHA-256/length source preflight and the approved JPEG
structural subset. Async methods use real async I/O and cancellation checks.
ReadIptc snapshots options; SaveIptc uses default IptcLimits plus the document's
MetadataLimits, with smaller applicable budgets winning. Retained EXIF, recorded
standard XMP payload sizes and Photoshop resource working data count toward the
logical metadata budget; this is not an exact managed-allocation measurement.

Borrowed streams stay open. Output must be writable, seekable, empty and at zero;
identical source/output is refused, distinct-wrapper storage aliasing is a caller
obligation. Preflight failure produces no output; later I/O/cancellation can leave
partial caller output. Path saves use CreateNew temporary files and non-overwriting
publication/cleanup rules already established for EXIF/XMP. No in-place overwrite,
implicit source mutation, document rebinding or concurrent-use guarantee.

Required tests: independent IIM/IRB bytes, repeated and unknown datasets, supported
and unsupported encodings, duplicate controls, normal/extended/truncated lengths,
odd names/data and padding, unrelated resource/APP13 identity, digest byte order and
exact-data checksum, duplicate segments/resources, malformed whole-removal policy,
full APP13 boundaries, encoded image preservation, async short I/O, cancellation,
source mutation and path-publication failure/races. Extend the pinned CLI harness
with authored fixtures and exact provenance, normalized text/repetition checks,
reverse writes and discrepancy review. New validation warnings are not automatically
accepted. No package publication, automatic EXIF/IPTC/XMP synchronization or privacy
scrub claim in this group.
