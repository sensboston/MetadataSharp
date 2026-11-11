# M12 Normative Review and Deliberate IPTC/IRB Restrictions

Reviewed 2026-11-07 after explicit contract approval. Only specification text was
read; no implementation/tests/corpus or bulk tag dictionary was imported.

| Source | Edition / repository blob | SHA-256 |
| --- | --- | --- |
| IPTC-NAA IIM | Version 4.2, Rev 2 July 2014; visualcommons/gamut `references/iptc/iim-4.2.pdf`, blob `85bcee29f69042440b035fee7511099e3b63c349` | `d9c74e601daafe121553c4c110126065b02e104ba04cc8f8da1adccaf25af0e3` |
| Adobe Photoshop File Formats HTML mirror | June 2012; ericduran/psdJs `docs/PhotoshopFileFormats.htm`, blob `2f535c7653d22548671c2f503503b2bb6f52266f` | `fe800eab570b181aa25e4eac32f07a6f697f0d6319b3988304614766030137df` |
| Adobe XMP Part 3 | January 2020; adobe/XMP-Toolkit-SDK `docs/XMPSpecificationPart3.pdf`, blob `c7ee90e2b2b60e9ccef42bd0b6d676b21bc6591a` | `2e80d671f87501c91ab18c0722c233e35a72d1c9245c70accb743917ebe2eb18` |

The first two sources are mirrors, not publisher-authenticated downloads. Copies
and extraction tooling are in an excluded local cache; no redistribution is implied.

IIM Part I sections 1.4–1.6 define 0x1c framing, a 15-bit standard data length,
extended length descriptors and packet encoding. Part II dataset 1:90 defines the
encoding declaration; 2:0 is a two-byte record version, currently 4 (big-endian).
The ISO 2022 UTF-8 designation is ESC % G (1b 25 47). New minimal packets include
that declaration and version 4; they are not complete interchange envelopes.
The approved 1–4 extra length-byte cap is deliberately bounded; untouched supported
nonminimal descriptors are retained, not normalized to a different width.

Reviewed six text dataset definitions, not a bulk database:

| Record 2 dataset | Maximum encoded octets | Repeatable |
| --- | ---: | --- |
| 5 Object Name | 64 | no |
| 25 Keywords | 64 | yes |
| 80 By-line | 32 | yes |
| 105 Headline | 256 | no |
| 116 Copyright Notice | 128 | no |
| 120 Caption/Abstract | 2000 | no |

Text writes accept Unicode graphic characters/spaces; control characters are
refused, except CR/LF in Caption. Text reads decode without trimming or applying
write-time length/cardinality repairs. Raw editing deliberately does not enforce a
complete field schema. Missing charset means ASCII-only text, not an assumed code
page; unknown declarations remain raw. Invalid UTF-8 only fails when decoded,
not when unrelated raw datasets are copied. Duplicate charset/version controls are
ambiguous. No automatic repair or transcoding of existing controls occurs.

Photoshop's Image Resource Blocks/IDs sections establish 8BIM, 16-bit IDs, a Pascal
name padded to even size including its length byte, 32-bit data length and separate
even data padding. Padding bytes need not be rewritten when the block is untouched.
Resource 0x0404 is IPTC; 0x0424 is Photoshop XMP (untouched by this feature); 0x0425
is the 16-byte caption/IPTC digest. XMP Part 3 sections 3.3.1–3.3.2, Tables 36 and
49, clarify that the binary MD5 value covers the exact contents of resource 1028,
not its name/header/padding. Digest bytes are the hash output, not a host-endian
integer. This checksum is not a security or authentication mechanism.

The approved profile refuses multiple Photoshop APP13s, duplicate IPTC/digest
resources, orphan/invalid-length digests, unsupported signatures and split resources
for semantic operations. It does not implement the synchronization recommendations
in XMP Part 3: EXIF, XMP and unrelated resources remain independent. No broad IIM,
Photoshop/PSB, full envelope or metadata-synchronization conformance is claimed.
