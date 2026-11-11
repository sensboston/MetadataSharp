# M05: EXIF Layout Planning and Safe Rebuild Refusals

## Scope

ExifLayoutPlanner is an internal consumer of the M04 read result and an immutable
same-byte-order desired snapshot. It creates a complete inspectable plan, never an
EXIF output buffer. Public ExifEdit and tag-value semantic editing rules are not
implemented here. No new public signatures are introduced.

Existing source directories, including empty ones, remain present. Added Exif/GPS/
Interop directories receive their known parent links automatically. A source null
optional pointer is retained as null if no corresponding directory exists.
Structural and thumbnail fields cannot be inserted, removed or modified as ordinary
values. The future edit layer will enforce this before invoking the planner too.
A read may hand back a thumbnail IFD without a picture — a thumbnail its validation
policy dropped — and a source like that refuses every rebuild before any planning:
the planner may not discard a picture it never owned, nor write dangling offset and
length entries. Removing the whole payload relocates nothing and remains allowed.

## Deterministic Placement

Header is at zero, IFD0 begins at 8. Directories are placed in fixed order: IFD0,
Exif, GPS, Interop, Thumbnail. Entries sort by numeric tag. Every IFD includes its
count and next pointer, even when empty. Only IFD0's next pointer targets Thumbnail.
Known LONG,1 directory links contain TIFF-relative directory offsets, never source
absolute offsets. Thumbnail offset/length fields are recomputed, not copied raw.

Values of at most four bytes stay inline. Larger values follow directory storage,
word aligned in directory/tag order; the preserved thumbnail follows. Align only
the start of each new block: a final odd-length payload needs no extra pad. This
allows exactly 65,527 TIFF bytes and refuses 65,528. Gaps are implicit zero-padding
for the future serializer, not an invitation to copy source slack. Layout arithmetic
uses long intermediates bounded by the APP1 maximum before narrowing to int.

Plans expose read-only directory/entry collections and immutable value references.
Thumbnail bytes are defensively copied. Scalar pointer overrides are represented
as numbers, not pre-serialized output bytes. M06 must zero its destination region
and emit only these validated planned ranges.

## Refusal Policy

Validate the original as well as desired tags. Removing an unknown source tag must
not circumvent safe preservation. Any MakerNote, unknown ID, unclassified payload
or unsupported shape blocks rebuilding. This also applies to same-length edits
and nonempty edits that happen to produce the same values. Empty-edit byte copying
and explicit complete EXIF removal belong to the future container pipeline and do
not invoke this rebuilding planner.

Only the small schema in the M04 normative review is recognized. Supported ASCII
must be seven-bit and NUL terminated; type/count shapes are checked. This does not
validate dates, rational denominator meanings, coordinate consistency or final-2.32
GPS semantics. Those are not inferred from Exif 3.0. A future editing API cannot
claim full semantic validation based solely on this layout check.

Recheck directory/depth/entry/value budgets after adding structural entries.
Retained-payload accounting conservatively includes source and desired values,
source and copied thumbnail bytes, plus the future staged output length. This can
double-count shared immutable values intentionally; it is not an RSS guarantee.
Opaque payloads are never moved under a claim that BLOB equality makes them safe.

## Evidence

Tests cover 4/5-byte boundaries, odd-sized payloads and alignment gaps, tag ordering,
deterministic plans, known pointer bases, empty directories, protected fields,
thumbnail rebasing/ownership, unknown and MakerNote refusal even if removed, exact
APP1 limit, all affected budgets and independent disjoint/in-bounds range checks.
No production serializer, parser/writer round trip or third-party fixture is used
as evidence for layout correctness.
