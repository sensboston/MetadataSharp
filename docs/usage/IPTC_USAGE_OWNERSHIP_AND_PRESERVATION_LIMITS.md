# IPTC IIM Usage, Ownership and Preservation Limits

M12 implements the [approved contract](IPTC_IIM_PHOTOSHOP_IRB_AND_JPEG_CONTRACT_PROPOSAL.md)
in two gated checkpoints. The [normative review](IPTC_IIM_AND_PHOTOSHOP_NORMATIVE_REVIEW.md)
records editions, source blobs/hashes, exact field limits and deliberate restrictions.
This is a raw IIM/8BIM subset, not a complete interchange-envelope validator,
Photoshop/PSB editor or metadata synchronization engine. No new runtime dependency.

## Independent JPEG IPTC edits

```csharp
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats.Jpeg;

await using var image = await JpegMetadataDocument.OpenAsync("source.jpg");
IptcPacket? before = await image.ReadIptcAsync();

var edit = new IptcEdit();
edit.ReplaceText(new IptcDatasetKey(2, 25), new[] { "travel", "landscape", "travel" });
edit.ReplaceText(new IptcDatasetKey(2, 120), new[] { "A caption" });
await image.SaveIptcAsAsync("new-result.jpg", edit);
```

ReplaceText replaces all occurrences of its key in supplied order, without
collapsing duplicates. Known text fields enforce their reviewed encoded-byte and
cardinality limits. Invalid Unicode, overlong text and forbidden controls fail
before edit mutation. Caption permits CR/LF; other supported text fields do not.
A property replacement occupies the first original occurrence; absent keys append.
Other datasets retain relative order and untouched framing, including supported
nonminimal extended-length descriptors. Last operation per key wins. Empty
replacement means removal. Missing-key removal alone is an exact no-op.

Text encoding is never guessed. A unique ESC % G declaration means UTF-8; absence
permits ASCII-only convenience reads/writes, not implicit Latin-1. Unknown declarations
refuse text operations, even for ASCII. Existing charset/version controls are not
silently repaired, added or changed; explicit transcoding is outside this scope.
New IPTC packets use a UTF-8 declaration and record-2 version 4. GetText decodes
without trimming or write-time field repairs; malformed UTF-8 is InvalidIptc.
Raw fields may be read/copied even when their text cannot be interpreted.

## Owned raw packets and repetitions

```csharp
IptcPacket packet = IptcPacket.CreateEmpty();
var rawEdit = new IptcEdit();
rawEdit.ReplaceRaw(new IptcDatasetKey(2, 250),
    new[] { IptcValue.FromRaw(new byte[] { 1, 2, 3 }) });
IptcPacket changed = packet.Apply(rawEdit)!;
byte[] iim = new byte[changed.ByteLength];
changed.CopyRawTo(iim);
IptcPacket reparsed = IptcPacket.Parse(iim);
```

Raw operations explicitly allow unknown ordinary record-2 keys and arbitrary binary
values. They are not a guarantee that the caller's field semantics, declared
encoding or lengths are standards-conforming. They do not guess widths or pointers
inside opaque data. Record-1 controls and record-2 version cannot be addressed.
Other records and unknown keys remain in the owned ordered Datasets list.

Packets/values deeply own their data and expose no mutable buffers/collections.
ReadIptc returns a snapshot that remains usable after disposing its JPEG document.
Empty Apply returns the original packet. RemoveAllIptc cannot mix with addressed
operations and makes Apply return null (absence), not an empty byte array. In JPEG
it removes the whole IPTC resource and associated digest, not other resources.
Removing ordinary fields alone retains controls. There is no standalone path or
sidecar API for IIM and no public general Photoshop resource editor.

## Photoshop framing, digest and refusal policy

Only an exact `Photoshop 3.0` plus NUL APP13 signature is interpreted. Other APP13
blocks are opaque. The internal reader bounds 8BIM resource IDs, Pascal name fields,
32-bit data lengths and independent even name/data padding before reading ranges.
Untouched unknown resources, names, bytes, order and nonzero padding remain exact.
Only updated IPTC/digest framing/data may change; new padding is zero.

One recognized Photoshop APP13, at most one IPTC resource (0x0404) and at most one
digest (0x0425) are supported for semantic reads/effective edits. Multiple signed
segments or duplicate target/digest resources are ambiguous. Other signatures,
split resources, orphan digests and digest lengths other than 16 are refused.
Malformed IRB cannot be removed selectively because resource boundaries are unsafe.
A uniquely framed malformed IIM payload may be removed whole with its valid digest.

The existing digest, if present, is recomputed over **only the new IIM data bytes**,
not the resource header/name/padding. No digest is introduced when absent. A stale
but well-formed digest does not block raw reads or byte-identical no-op; an effective
write refreshes it. MD5 is a legacy compatibility checksum, not authentication.
Whole IPTC removal also removes that digest, and removes the Photoshop APP13 only
if no resources remain. Photoshop XMP resource 0x0424 and all unrelated resources
are preserved, never synchronized or interpreted by this feature.

The APP13 payload cap is 65,533 bytes including all headers/resources/padding.
Because this framing has even total sizes, the largest emitted payload is 65,532
bytes; an otherwise empty segment can hold 65,506 IIM bytes. No segment splitting,
dropping unrelated resources or automatic lossy fallback. IptcTooLarge is reported
before output writes. General byte/count/resource budgets can fail earlier with
LimitExceeded. Default logical limits are 1 MiB packet/value, 4096 datasets and 1024
resources. The Photoshop working payload also respects the packet-byte budget.
JPEG metadata budgets count EXIF, recorded standard-XMP payload sizes and Photoshop
working data. This is not exact process-memory accounting or an allocation claim.

## Safety, independence and failure semantics

Existing EXIF/XMP APIs still treat APP13 as opaque, including malformed/duplicate
IPTC. Empty IptcEdit verifies source identity then copies the supported JPEG exactly,
without asserting IPTC readability. IPTC operations do not repair malformed EXIF or
expand the approved JPEG structural subset: Open retains its original checks.
Encoded JPEG data and unrelated metadata are byte-preserved. Removing IPTC is not
removing EXIF/XMP, legacy captions elsewhere or all private metadata.

Read/save operations verify source length/SHA-256 before using recorded ranges.
Async methods use actual async I/O and bounded cancellation checks. Borrowed streams
remain open; output must be writable, seekable, empty and at zero. Identical source
and output objects are refused; aliasing through distinct wrappers is a caller
obligation. Preflight failure produces no output; later I/O failure/cancellation may
leave partial borrowed output. Do not concurrently use documents, their streams or
mutable edits. Documents do not rebind after saves; reopen intermediate output when
editing EXIF, IPTC and XMP sequentially.

Path saves create a same-directory CreateNew temporary file, flush/close and publish
without overwriting any existing/competing target. Source bytes are never written.
Temporary cleanup is best-effort; no universal atomicity or crash durability claim.
Cancellation is checked before publication, never falsely reported after commit.
No package publication or broad vendor-compatibility/security certification follows
from this milestone.
