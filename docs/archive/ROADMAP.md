# Historical roadmap M00–M18 and completion gates

> **Historical, completed baseline.** For current work use
> [Programme 2: independent capability coverage and .NET evolution](roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md)
> and [CURRENT_STATE](CURRENT_STATE.md). Retained in place to preserve contract,
> provenance and historical links; "next" instructions below are not current tasks.

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

## M16 approved write contract — implemented and audited

Developer requested continuing after M15. The [exact M16 proposal](design/STANDALONE_TIFF_EDITING_CONTRACT_PROPOSAL.md)
was explicitly approved on 2026-11-07; implementation/audit passed at 762dc20 (448 tests/platform, policy/references green). Final documentation-head checks and merge receipt are tracked in PR #11.
It specifies addressed baseline
descriptive fields with append-preserve replacement of the active IFD graph,
closed field/pointer schema and source-identity/new-output guarantees. Old metadata
bytes remain physically present: no privacy scrubbing or compaction. Do not start
writer code without the approved schema and predecessor gate.
Use [the recovery checkpoint](CURRENT_STATE.md) for the current next steps. M17/M18 remain the next later units; DNG
implementation is not part of those three remaining roadmap milestones.

## M17–M18 grouped final roadmap audit

The developer explicitly requested both remaining units in one pass/PR on the new
arena/b166bb08-metadatasharp branch (merged and delivered through master; the session
branches are pruned after merge, so that ref no longer exists on the remote). M17 [DNG feasibility](design/DNG_SCOPE.md) and
[proposed follow-up prompts](prompts/followups/DNG_PROPOSED_SEQUENCE.md) are complete;
the proposed future implementation sequence still requires approval. M17 passed
42439542366 before M18 tooling began. M18 [measured/package audit](audits/PERFORMANCE_AND_PACKAGING_AUDIT.md)
passed 42439984148 at 4d89a00 (seven jobs including three-platform package consumers,
448 tests/platform and required references). Final grouped head/merge receipt is
tracked in PR #12 after checks. After that merge M00–M18 are complete within their
bounded definitions; no DNG implementation or package publication is implied.

## Selected next stage: DNG D0 contracts, not implementation

After the completed roadmap/cleanup, the developer explicitly selected DNG
contracts instead of release-candidate hardening. Execute only D0 from
prompts/followups/DNG_PROPOSED_SEQUENCE.md: pin/resolve the narrow candidate's
normative dependencies and propose exact read-only signatures, options, immutable
models, version/role/tag subset, finite budgets, errors, ownership/async semantics,
fixture rights and independent validator strategy. Record open blockers and ask
approval of that exact public contract before any DNG implementation. Existing
TIFF DNG refusal remains unchanged. D1/D2, shipping packages and publication are
not authorized by selecting D0. Release hardening remains deferred, not completed.
