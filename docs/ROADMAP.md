# Roadmap and Completion Gates

## Execution rules

M00 scope and contracts were approved on 2026-11-07. M01 infrastructure files
are prepared, but restore/build/test are blocked by the missing .NET SDK. No
metadata implementation exists. M01 remains pending; M02 is not eligible.
Execute units in the order below. Each unit requires its predecessor's gate;
contract-only proposals pause the unit until explicit approval. A completed gate
requires reviewable changes, relevant passing tests, documented limitations, and
an honest execution report. Unavailable required checks leave the gate pending
unless the developer explicitly accepts a deferral.

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
