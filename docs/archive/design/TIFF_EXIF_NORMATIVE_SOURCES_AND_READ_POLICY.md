# M04: TIFF/EXIF Normative Sources and Read Policy

## Reviewed Sources and Rejected Reference

The following PDF texts were retrieved through GitHub's blob API on 2026-11-07
and inspected locally using pypdf. No third-party implementation or test source
was used to implement this reader. PDFs and extracted text are temporary reference
material outside the repository; no third-party specification is relicensed MIT.
The mirror is a transport source, not a standards authority or proof of authenticity.

| Document | Mirror blob | SHA-256 | Reviewed sections |
| --- | --- | --- | --- |
| Aldus TIFF Revision 6.0, Final, June 3 1992 | [b487a9b](https://api.github.com/repos/visualcommons/gamut/git/blobs/b487a9b8dbb647b4d3407c72721394cb60112185) | `dbcdf729182937ecff415dfd06806894bf03bfd741291aa3ad7ba45335673def` | Section 2, printed pp.13–16: header, relative offsets, alignment, entries, count, inline values, sort order and twelve field types; Appendix A, printed p.117 onward, for field names |
| CIPA DC-008-Translation-2023, Exif 3.0, revised May 2023 | [83ed267](https://api.github.com/repos/visualcommons/gamut/git/blobs/83ed267755af54358149e8d99c93a89e4c26c72c) | `d1020d7f04b05d635ee1a308330b2494586da24fd8e0bb6cd96214dbf60244e8` | Sections 4.5.8, 4.6.2, 4.6.3.1–3, Tables 6/7/9 and GPS 4.6.7: thumbnail representation, IFD structure/links and tag shapes; Tables 8, 14 and 16 for field names |

Canonical publisher references: TIFF 6.0 at
<https://download.osgeo.org/libtiff/doc/TIFF6.pdf> and CIPA standards at
<https://www.cipa.jp/std/std-sec_e.html>. Direct publisher downloads were not
available in this environment; no byte-for-byte publisher authenticity check is
claimed. PDF title/edition/content, not the mirror README, determined selection.

**Rejected:** the mirror's file named `exif-2.32-dc-008-2019.pdf` actually identifies
itself as **DC-X008**, a draft for comment with an explicit warning not to design
products from it. SHA-256 `5f9b8261f479167df8799f3b871d7e9316b634f9f073cbb80a0a0b478de50954`.
It is NOT treated as verified final Exif 2.32 normative text. Final Exif 3.0 was
used only to verify common classic IFD/link mechanics and the limited type/count
facts below, not to broaden the approved API, enable UTF-8 type 129, new tags or
GPS semantics. General Exif 3.0 support and final-2.32 compliance are not claimed.

## Small Independently Recorded Schema

This table summarizes numeric facts from the reviewed standard. It is not copied
from a GPL dictionary. No type, count or interpretation dictionary and no edit
validation is added in M04; the only vocabulary the library carries is the name
table recorded below. These are candidate write shapes for M05/M06; all ordinary raw reads still
preserve uninterpreted values. Future value-level restrictions need their own tests.

| Directory / tag IDs | Shape in the supported subset | Relocation class |
| --- | --- | --- |
| IFD0 010E,010F,0110,0131,013B,8298 | ASCII, variable positive count incl. NUL | Ordinary text; ASCII-only subset of the final-3.0 ASCII/UTF-8 alternatives |
| IFD0 0112 | SHORT,1 | Ordinary numeric |
| IFD0 0132; Exif 9003,9004 | ASCII,20 incl. NUL | Ordinary timestamp text |
| Exif 829A,829D,920A | RATIONAL,1 | Ordinary numeric pair |
| GPS 0000 | BYTE,4 | Ordinary version bytes |
| GPS 0001,0003 | ASCII,2 incl. NUL | Ordinary reference text |
| GPS 0002,0004 | RATIONAL,3 | Ordinary coordinate pairs |
| GPS 0005 | BYTE,1 | Ordinary numeric; legacy altitude meaning not reinterpreted |
| GPS 0006 | RATIONAL,1 | Ordinary numeric pair |
| IFD0 8769,8825; Exif A005 | LONG,1 | Structural IFD-relative pointer; never a user-editable scalar |
| Thumbnail 0103 | SHORT,1 with value 6 | JPEG representation selector |
| Thumbnail 0201,0202 | LONG,1 | Structural JPEG offset and length |
| Exif 927C | UNDEFINED,any | Opaque MakerNote; no relocation guarantee |
| Any other ID or unclassified UNDEFINED payload | Known TIFF type required for raw reading | Unclassified; future safe rebuild must refuse |

## Tag Vocabulary (roadmap 4.2a and 4.2b)

Roadmap 4.2a added `ExifTagNames` in `MetadataSharp.Core`, which paired each tag ID with the
field name the reviewed publications gave it: Exif 3.0 Table 6 for the 0th IFD, extended by the TIFF 6.0
appendix "TIFF
Tags Sorted by Number" for fields a bare TIFF may carry, Tables 8 and 9 for the Exif sub-IFD,
Table 14 for GPS Info and Table 16 for Interoperability. It was read out of the same two PDFs,
re-fetched on 2026-11-11 and matched against the hashes above, by a throwaway script; each parsed
region was then swept for rows the script had dropped, and the seven it had eaten (`SubjectArea`,
plus TIFF's `ColorMap`, `InkNames`, `DotRange`, `ExtraSamples`, `SMinSampleValue`,
`SMaxSampleValue`, whose count or type columns are expressions) were added from the page text. The
198 rows are the tables' rows: no dictionary, tag list or source of any third-party tool was opened,
and the one disagreement between the two texts (TIFF's appendix misprints 0x0202 as
`JPEGInterchangeFormatLngth`) is settled by the spelling both the TIFF body and Exif use.

Three consequences are deliberate. A name defined only outside these tables - a MakerNote's own
subtags, `PrintImageMatching`, `Rating`, `IPTCNAA`, the DNG tags, the DCF-defined Interoperability
fields - is absent, so such a tag keeps its numeric ID wherever it is printed instead of borrowing a
label. Names resolve per directory, as the tables are written: a file storing `ExposureTime` in its
0th IFD prints `Exif.Image.0x829A`, because no reviewed table calls 0x829A a field of that
directory. And where the text writes a Field Name as a phrase, the table stores it as an identifier:
`ExifIFDPointer`, `GPSInfoIFDPointer` and `InteroperabilityIFDPointer` for "Exif IFD Pointer",
"GPSInfo IFD Pointer" and "Interoperability IFD Pointer".

Roadmap 4.2b folded that name table into `ExifTags`: one `ExifTagDefinition` per tag, holding its name,
the field types and counts the standard allows, the default it records, and for the 27 tags whose standard
enumerates values (124 numbered values in all), those meanings in the standard's words. The one lookup is
`ExifTags.Find(key)`; `Definitions(directory)` returns the same records the table holds, so a name cannot
disagree with a shape. The arrays a definition hands out are immutable, and the fixed count is read
back from the allowed-counts list rather than stated twice, so the table has one home for each fact.
Shapes came from the same two PDFs and a similar throwaway script, a tag's own
section first and its table row second: 154 rows resolved from a section, 43 from the TIFF 6.0 appendix,
and one, `ExtraSamples`, from the body of that publication because its own appendix row contradicts it. All
198 carry a type rule and a count rule, and 0 rows were left unresolved. `FixedCount` holds a number only
where the standard allows exactly one (135 tags); the 62 whose count the standard ties to the image or to
another field carry an empty list, and `SubjectArea` carries its three allowed counts. One row contradicts
its own table, and the section wins: Table 9 gives 0xA407 `GainControl` as RATIONAL while 4.6.6.7.41 gives
SHORT with five named gain steps - and the files in the reference corpus carry SHORT. The same goes for
`ExtraSamples`, whose appendix row says BYTE against the SHORT of the body it prints.
`TiffFieldType` still has no type 129, so the 16 rows the standard writes "ASCII or UTF-8" keep `Ascii` and
say `Utf8TextAlternative = true`; a value that disagrees with its definition stays a value the reader
reports, because no reader, writer or limit consults a definition. Listing a tag is not a claim that a file
containing it is compliant, and printing a decoded value is 4.2c's work, not this table's.

## Reader Contract and Deliberate Restrictions

ExifTiffReader is internal and accepts TIFF bytes after the EXIF signature, not a
JPEG or standalone TIFF image. MetadataLimits and MetadataException implement the
approved shared contracts in MetadataSharp.Core, so future format layers can reuse
them without a reverse dependency. No public read facade is introduced here.

The reader uses a bounded queue, unique offset/role sets and a claimed-range list.
It validates directory/value ranges before slicing or copying; rejects cycles,
shared IFDs, duplicate/unsorted tags, all overlaps of external values/structures,
odd directory/external offsets, BigTIFF, unknown field widths and unsupported
SubIFD/strip/tile families. Identical externally shared value storage is also
rejected conservatively; this reader does not promise general TIFF acceptance.
The full TIFF payload is at most 65,527 bytes; directory/entry/depth/value and
retained-payload budgets are enforced before associated growth/copies. The thumbnail
budget alone never refuses a file; it selects whether the picture is carried.

The retained-byte budget reserves the input payload plus owned raw value and
thumbnail copies, conservatively counting inline values too. It is not a hard RSS
cap. Object growth is separately bounded by entry/directory limits. Parsing is
synchronous and bounded; async/cancellation around source I/O belongs to M07/M08.

IFD0 may link to one thumbnail; Exif/GPS/Interop use LONG,1 known links. Zero optional
links mean absent; missing primary IFD is malformed. Empty directories and zero-count
raw fields remain representable per M00 compatibility decisions, despite TIFF 6.0's
nonempty-directory rule; no writer-conformance claim follows from accepting them.
Unknown tag IDs of known widths are retained, not interpreted as safe to move.

Thumbnail support requires Compression=6 and a LONG,1 JPEG offset/length pair; a
thumbnail that fails any check belonging to the picture itself - a missing pair, non-
JPEG compression, a malformed pair, bytes outside the payload, odd alignment, a short
run, absent SOI/EOI markers, or a size over the thumbnail budget - is dropped to
"no thumbnail" while the read completes. The picture is incidental to the metadata,
and the pinned corpus contains well-formed files with broken thumbnails (Canon EOS
300D and D30 samples) whose tags are worth reading. Two checks stay refusals, because
they say something false about the payload even when only the thumbnail moved: its
bytes overlapping another claimed structure, and the whole-file retained budget.
Nested JPEG marker grammar is never validated or decoded, so nothing here claims a
thumbnail - kept or dropped - decodes correctly. On write, a source whose thumbnail
IFD holds no retained picture refuses the rebuild: a dropped thumbnail must not
silently become a discarded one, and dangling offset/length fields will not be
written. Removal of the whole payload carries nothing and is unaffected.

Errors have stable codes and sourceBase-adjusted offsets when the location lies
within input. Out-of-input offsets have no fabricated source position. Invalid
caller budgets/bases remain argument errors. Read snapshots own their raw bytes;
internal thumbnail bytes are owned by the read result for later preservation.

## Tests and Remaining Gates

Authored literal II/MM Orientation fixtures and a synthetic five-directory layout
exercise inline/external values, MakerNotes and thumbnail preservation independently
of any production writer. Mutation tests cover every truncated prefix, alignment,
invalid types/counts/offsets, cycles/shared links, duplicates, overflow and budgets.
A fixed-seed 256-case mutation smoke run rejects incidental exceptions; it is not
exhaustive fuzzing or proof of full conformance.

Before expanding semantic tag editing, verify final-2.32-specific value semantics
against an actual final publication (the draft is not acceptable). Do not infer
new GPSAltitudeRef values or UTF-8 support from the newer reference. JPEG container
and standalone TIFF/DNG guarantees remain separate future milestones.
