# Roadmap and Completion Gates

## Execution rules

M00 scope and contracts were approved on 2026-11-07. M01–M12 passed Linux,
Windows and macOS CI, with required pinned external references at M09. See
[progress](PROGRESS.md) and the [initial JPEG audit](audits/INITIAL_JPEG_RELEASE_AUDIT.md)
for evidence, restrictions and remaining publication gates. M07–M09 form one
approved PR with per-unit CI gates. No package publication is implied.
Execute units in the order below. Each unit requires its predecessor's gate;
contract-only proposals pause the unit until explicit approval. A completed gate
requires reviewable changes, relevant passing tests, documented limitations, and
an honest execution report. Unavailable required checks leave the gate pending
unless the developer explicitly accepts a deferral.

The developer authorized sequential execution and merge after successful CI on
2026-11-07. Individual prompt stop rules still apply to blocked gates or changed
public contracts, not to requesting the next already-planned unit.

The first release candidate is M09, not the entire roadmap. M10 onward is a later
expansion and requires a new scope/contract review at the start of each unit.
BigTIFF, general MakerNote relocation, and automatic metadata synchronization are
out of scope unless separately approved.

| Unit | Bounded deliverable | Completion gate |
| --- | --- | --- |
| M00 | Initial release scope, API and safety proposal; no implementation | Developer approves contracts, subset, limits and failure policy |
| M01 | .NET solution, nullable/build settings, xUnit skeleton | Restore/build/test actually run; no parsers |
| M02 | Endian and checked range primitives | Both orders, exact boundaries, overflow/truncation tests |
| M03 | Raw TIFF value model and rational representation | Raw preservation and conversion policies tested |
| M04 | Bounded classic TIFF/EXIF reader | Graph, malformed input and independent known-byte tests |
| M05 | EXIF write planner and safety decisions | Layout validated without emitting output; unsafe relocation refused |
| M06 | EXIF serializer for approved layouts | Known bytes, round trips, thumbnail/offset and refusal tests |
| M07 | JPEG scanner and EXIF discovery | Supported subset enforced, marker bounds and scan handling tested |
| M08 | JPEG EXIF edit/copy pipeline | Addressed changes only; image/unrelated bytes preserved; failure tests |
| M09 | External reference harness and first-release audit | Licensed fixtures, pinned optional tools, discrepancy review and release checklist |
| M10 | XMP packet and sidecar support | Dependency review; secure XML and semantic preservation tests |
| M11 | Standard JPEG XMP integration | APP1 size/duplicates tested; Extended XMP supported or explicitly refused |
| M12 | IPTC IIM and Photoshop IRB | Repetition/encoding and unrelated-resource preservation tests |
| M13 | PNG metadata support | CRC, ordering, text limits, unknown-chunk policy and IDAT preservation |
| M14 | WebP metadata support | RIFF/padding/VP8X consistency and frame preservation |
| M15 | Read-only standalone classic TIFF | Image-range accounting and unsupported-layout reporting |
| M16 | Restricted standalone TIFF editing | Approved strategy; all affected offsets and image preservation tested |
| M17 | DNG feasibility and contracts only | Written subset/risk assessment; developer approves a separate implementation plan |
| M18 | Measured optimization and packaging audit | Baselines, platform evidence, dependency/notice/package inspection |

## State tracking

After actual work, append the unit ID, date, approval reference or explicit
statement that approval is pending, changes, exact checks, and unresolved gates to
`docs/PROGRESS.md`. Do not pre-mark future units as approved. Documentation setup
is not completion of M00. If a step reveals an architectural blocker, record it
and stop rather than bypassing the gate or rewriting the roadmap silently.

## Deferred scope

M17 does not authorize DNG implementation. Vendor-specific MakerNote relocation,
BigTIFF, Extended XMP if deferred at M11, full JPEG variants beyond the approved
subset, and synchronization need their own bounded follow-up prompts. Split such
work only after its contracts and acceptance criteria are reviewed.

## M10–M11 proposal

The developer requested this next group on 2026-11-07. Its contract-only proposal
is [XMP packets, sidecars and JPEG](design/XMP_PACKET_SIDECAR_AND_JPEG_CONTRACT_PROPOSAL.md).
Contracts were explicitly approved on 2026-11-07. M10 and M11 passed their per-unit
CI gates; see PROGRESS.md for evidence. See the XMP usage/normative review documents
for the implemented subset. M12 (IPTC) was subsequently approved and implemented; see its checkpoint record below.

## M12 contract review

The IPTC/Photoshop scope is recorded in
[the M12 contract](design/IPTC_IIM_PHOTOSHOP_IRB_AND_JPEG_CONTRACT_PROPOSAL.md).
Approved on 2026-11-07: one PR with M12a codec/resource and M12b JPEG checkpoints,
each gated by CI. Both implementation checkpoints passed; see PROGRESS.md and the
IPTC usage/normative review documents for evidence and restrictions. M13 (PNG) is
the next scope; its subsequent explicit PNG/WebP approval is recorded below.

## M13–M14 grouped contract review

The developer approved one coherent PNG/WebP PR with separate per-format gates on
2026-11-07. The new [container contracts](design/PNG_AND_WEBP_METADATA_CONTRACT_PROPOSAL.md)
were explicitly approved on 2026-11-07. M13 passed before M14 began; M14 also
passed its implementation CI. Final head passed and PR #9 merged (269dc92cb5bf1218df230e1ced3140c95ca7d81c).
Standalone TIFF contracts are separate; no DNG implementation.

## M15 approved read-only contract

The developer requested attempting M15 next. Its [exact read-only TIFF proposal](design/STANDALONE_TIFF_READ_ONLY_CONTRACT_PROPOSAL.md)
was explicitly approved on 2026-11-07. Implementation/audit passed 392 tests and
required references; final head 812bd76 also passed runs 42431094052/42431090942
and PR #10 merged as 80dc75e62eff242d2b98bc4b14b3090cf4ea60ce. It separates immutable classic TIFF range accounting
from JPEG-sized EXIF parsing, TIFF writing, BigTIFF and DNG. No M16 authorization.

## M16 approved write contract — implementation pending

Developer requested continuing after M15. The [exact M16 proposal](design/STANDALONE_TIFF_EDITING_CONTRACT_PROPOSAL.md)
was explicitly approved on 2026-11-07; it is not implemented at this checkpoint.
It specifies addressed baseline
descriptive fields with append-preserve replacement of the active IFD graph,
closed field/pointer schema and source-identity/new-output guarantees. Old metadata
bytes remain physically present: no privacy scrubbing or compaction. Do not start
writer code without the approved schema and predecessor gate.
Use [the recovery checkpoint](CURRENT_STATE.md) for the current next steps. M17/M18 remain the next later units; DNG
implementation is not part of those three remaining roadmap milestones.
