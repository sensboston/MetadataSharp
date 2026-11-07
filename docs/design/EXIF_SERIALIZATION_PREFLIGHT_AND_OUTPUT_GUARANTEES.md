# M06: EXIF Serialization Preflight and Output Guarantees

## Internal API

ExifTiffSerializer.Write accepts a complete M05 plan and a caller-owned output span.
It does not write files, allocate a whole image, implement a public edit API or
change the conservative source-tag refusal policy. No new public types/members.

Write validates the complete plan before checking output capacity and touching the
buffer. Validation rechecks the byte order, size, fixed directory ordering and
contiguous directory ranges, unique/sorted tags, exact inline/external placement,
known type/count shapes, child/parent links, scalar pointer overrides, thumbnail
fields/representation and exact final length. Unknown/opaque tag shapes cannot be
smuggled through an internally forged plan. Invalid, inconsistent or stale offsets
fail before modifying output. External values cannot overlap directories or each
other because their offsets must equal the deterministic aligned cursor.

The plan constructor freezes directory/entry collection structure and copies its
thumbnail; raw values already own immutable bytes. Source value changes therefore
cannot occur through the normal API. There is no source stream associated with a
plan in M06; future source identity/aliasing and cancellation preflight belong to
M08. This is not a promise of thread safety for malicious internal/reflection use.

## Output

After successful validation and capacity checking, clear exactly plan.Length bytes,
write II/MM, TIFF identifier 42, and primary IFD offset 8. Emit directory counts,
entries, known pointer fixups and next-IFD fields using BinaryPrimitives in the
planned byte order. Copy raw numeric/ASCII payloads without reinterpretation or
normalization. Zero fill prevents leaking caller-buffer contents in inline padding
or alignment gaps. Leave any destination suffix untouched.

The largest permitted TIFF payload remains 65,527 bytes. The serializer does not
append an unnecessary trailing alignment byte. Thumbnail bytes are copied exactly
at the planned offset. Only the range and SOI/EOI boundaries are validated, not
thumbnail decodability or full nested JPEG grammar.

Short output causes ArgumentException without changes. Invalid plans cause
structured MetadataException without changes. Once preflight succeeds, all writes
are bounded span operations over an immutable plan, not fallible external I/O.
No stream rollback guarantee or source-file replacement is implied.

## Evidence and Limits

Independent literal II/MM Orientation and external-ASCII expected bytes supplement
reader/writer round trips. Tests cover raw rational zero denominators, preserved
source values, output suffixes, alignment gaps, exact format maximum, thumbnail
rebasing, short output and forged header/offset/link/unknown-tag plans with unchanged
sentinel buffers on refusal. M04/M05 tests retain hostile-input and unsafe-source
coverage. No external metadata tool was run for this stage; independent-tool
validation remains a distinct M09 gate, not inferred from round trips.
