# XMP Normative Review and Restricted Managed Profile

Reviewed 2026-11-07 after developer approval of M10–M11 contracts. No specification
text, third-party test corpus or SDK implementation is imported into the repository.

## Sources and reviewed sections

Adobe's `XMP-Toolkit-SDK` repository, revision
`7093513bd3caaad29da01db0f275d88a39d6bcc2`, supplies:

| File | Edition | PDF pages | SHA-256 |
| --- | --- | --- | --- |
| `docs/XMPSpecificationPart1.pdf` | April 2012, data model/serialization/core properties | 52 | `e0056b9d5c29d4d21eda1856cf947b54cee3013944638a9871aed7d67a949090` |
| `docs/XMPSpecificationPart3.pdf` | January 2020, storage in files | 79 | `2e80d671f87501c91ab18c0722c233e35a72d1c9245c70accb743917ebe2eb18` |

Part 1 sections 6.2–6.4, 7.1–7.9 and language alternative notes in section 8
informed expanded-name identity, literals, Bag/Seq/Alt, qualifiers and preservation
rules. Sections 7.3–7.5 permit optional wrappers, prefix changes, attribute literal
shorthand and plain text/CDATA values. Section 7.7 defines array containers/items.
Part 3 section 1.1.3 and subsections cover JPEG APP1 identification, placement,
UTF-8, extension identifiers/chunks and HasExtendedXMP references. Reviewed only
these mechanics; no comprehensive ISO/XMP conformance claim.

## Deliberate restrictions and dependency boundary

The approved codec uses framework XML APIs, not an external XMP package. No new
NuGet component/license or native runtime is introduced. PDF copies/extracted text
were kept in an excluded local cache for reading only, not redistributed. Adobe's
SDK source/examples/tests are not implementation inputs.

UTF-8 only, one empty-subject Description and literal values/arrays are intentional
restrictions. Multiple descriptions, structures, URI values, inherited/general
qualifiers and unsupported equivalent RDF remain opaque. Unknown simple property
namespaces do not prevent editing. A read-only wrapper prevents editing by our
conservative contract, although Part 1 permits format-aware processors to ignore
that hint. Writers emit optional xmpmeta but omit optional packet PIs/padding.
Comments/PIs and wrapper toolkit identifiers have no data-model meaning; effective
property edits need not preserve them. Exact no-op retains all original bytes.
Supported text is XML-normalized on read (e.g. literal CRLF becomes LF); serialized
character references preserve any actual CR value in the model.

A forward reader validates the full bounded XML before DOM creation. DTDs and
external resolution are disabled. Input byte/UTF-16-character, depth, node,
attribute and name limits apply even to opaque trees. Property/array-item limits
also apply to the editable model. Factories impose finite default value budgets;
custom read limits do not lift those factory maxima. Serialization is counted
against the byte budget before allocating the output buffer and reparsed against
captured XML/model limits before exposing a changed packet.

## JPEG specification discrepancy to retain at M11

Part 3 Table 6 / section 1.1.3.1 states a standard packet limit of 65,502 bytes,
but its field widths and JPEG's length rule imply **65,504**:
65,535 length field maximum minus 2 length bytes minus 29 signature bytes.
The approved contract uses the arithmetic container capacity, 65,504; do not call
that the document's stated limit. Boundary tests and independent CLI validation
must exercise it in M11. If compatibility requires a stricter write cap, request
approval rather than silently changing the accepted public boundary.

Part 3 requires newly written XMP before SOF, while permitting tolerant readers
between SOF and SOS. New insertion must precede SOF. The approved replacement-in-place
policy preserves an existing late segment's placement; this is a documented strict
preservation choice, not a claim that an existing late file becomes conforming.
Extended XMP is detected but not assembled; XMP operations refuse unsupported
extensions rather than returning a misleading partial property model.
