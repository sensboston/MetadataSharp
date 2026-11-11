# M03: Raw EXIF Value Ownership and Equality

## Approved Public Surface

Implemented in MetadataSharp.Core: ExifDirectoryKind, ExifTagKey, TiffFieldType,
ExifValue, ExifEntry, ExifMetadata, SignedRational and UnsignedRational. Names and
signatures follow M00. Entry/snapshot constructors are internal: future readers
supply them; callers currently construct raw values but cannot forge read results.
TryGetEntry has a nullable-flow annotation, not a different runtime signature.
Directory roles are represented by tag keys; a physical IFD graph belongs to M04.
No ExifEdit, parser, writer or tag interpretation is implemented here.

## Ownership and Raw Fidelity

FromRaw validates the byte order, one of the twelve approved TIFF type codes,
checked count-times-width, and exact length before copying. Widths in this raw
model follow the approved M00 declarations: 1-byte byte/ASCII/signed-byte/undefined,
2-byte shorts, 4-byte longs/float, 8-byte rational pairs/double. Unsupported type
codes have no inferred width. A known type does not establish validity for a tag.

CopyRawTo copies only the payload. Short output is rejected before modification;
extra suffix bytes remain untouched. No mutable view into owned storage escapes.
Float/double bit patterns, arbitrary ASCII bytes and zero denominators are not
normalized at raw-value construction. Tag-specific editing validation is separate.
Zero-count raw values may represent read data; future edits must reject them.
Limits from a reader must be enforced before calling the raw factory: the factory
checks representability, not the future configurable metadata allocation budget.

Metadata snapshots copy the entry list, expose a read-only collection and retain
immutable entries/values. Internal construction rejects null entries, duplicate
keys, invalid directory roles and inconsistent byte order. It preserves input
ordering and unknown numeric tag IDs. Same tag IDs in different directories are
not duplicates. Tests prove independence from input arrays and caller collections;
source-document disposal tests require the later source-document implementation.

## Equality and Conversion

Tag keys and rational records have structural field equality. Rational pairs are
not reduced: 1/2 and 2/4 differ, as do 1/2 and -1/-2. Conversion casts the numerator
to double before division, avoiding int.MinValue / -1 overflow. Zero denominator
returns false with zero output. No exposure/GPS display policy is introduced.

ExifValue, ExifEntry and ExifMetadata are sealed reference types, not records.
They retain normal reference equality; byte-wise or interpreted semantic equality
is not silently invented. Compare raw bytes explicitly when needed. Byte-order
conversion during editing remains deferred with ExifEdit, not performed by FromRaw.

## Provenance and Deferred Normative Gates

All tests use small authored inline bytes; no external corpus or GPL tests.
This step implements the already approved raw type contract, not a tag dictionary
or normative format parser. No claim is made that a CIPA tag schema was verified.
The small writable-tag schema, type/count constraints, relocation classification
and normative clause verification required by M00 remain M04 prerequisites for
interpreting tags. They must be reviewed before parsing/editing semantics are
implemented; unknown IDs in this model are never classified as relocatable.
