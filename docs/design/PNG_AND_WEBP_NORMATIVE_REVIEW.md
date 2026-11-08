# M13–M14 Normative Review and Profile Decisions

M13 review performed after developer approval on 2026-11-07. Only specifications
were read; no reference implementation/sample CRC code or external tests imported.

PNG Third Edition: W3C Recommendation 24 June 2025, mirrored at
visualcommons/gamut `references/png/png-3.html`, blob
`76f2b0ed06fd2146ae7ce636ba46ecf53f4a6d8f`, SHA-256
`a3ac82bb9eb8664b93961a2a9ff6def3d70e3a9f63d92721143c1f391d04edec`.
Reviewed sections 5.2–5.6 (signature, names, CRC/order), 11.2 (critical chunks),
11.3.3 (iTXt fields/XMP keyword), 11.3.4.5 (eXIf), 11.3.6 (APNG), 14 (editors).
This is a mirror of the published Recommendation, not publisher-authenticated bytes.

The ordering table requires eXIf before IDAT; only iTXt can occupy permitted
post-IDAT locations. Implement that rule, not older permissive eXIf behavior.
Uncompressed iTXt readers must ignore the compression-method byte; new output
uses zero. XMP language/translated fields remain the approved empty-field profile.
Known ancillary data is opaque except checked framing/order/fixed sizes; there is
no full color-profile/text semantic validation or IDAT pixel/deflate validation.
Unknown unsafe chunks block effective edits even where PNG permits more permissive
ancillary-only changes. APNG is explicitly refused, not flattened to a first frame.

Compressed XMP: RFC1950 and RFC1951 (May 1996), text mirrors in madler/zlib
`doc/rfc1950.txt` blob `ce6428a0f2eed45691ce209b1daf36807c29b3e7` and
`doc/rfc1951.txt` blob `403c8c722ff24ca034973876fa819d37715b9b6a`.
SHA-256 respectively `8f0475a5c984657bf26277f73df9456c9b97f175084f0c1748f1eb1f0b9b10b9`
and `5ebf4b5b7fe1c3a0c0ab9aa3ac8c0f3853a7dc484905e76e03b0b0f301350009`.
Reviewed zlib header/dictionary/checksum and DEFLATE sections 3.2.3–3.2.7.
A bounded independently authored framing walk checks stored/fixed/dynamic blocks,
length/distance bounds, final block and exact compressed end before framework
ZLibStream expansion. Check Adler-32 explicitly. This avoids relying on framework
read-ahead/EOF behavior to detect truncated or concatenated/trailing streams.
Preset dictionaries are outside the profile. Both input and expanded output are
capped; no unbounded decompression and no new production dependency.

WebP source located for M14 review, not yet an implemented/validated feature:
RFC9649 (November 2024), mirrored at visualcommons/gamut
`references/webp/rfc/rfc9649.txt`, blob `d49745b1524d0ec61d99a7831e790e5b156deef5`,
SHA-256 `f4d7cd92b015c0b340f10a932bb03e564a758a17df50cbb992c16d8459d60e58`.
Complete M13 CI before M14 implementation. If review requires a public/safety
contract change, obtain approval rather than altering the accepted boundary silently.

## M13 external discrepancy

ExifTool 12.76 writes PNG XMP with `<?xpacket end='r'?>`. The existing approved
XMP profile deliberately treats read-only wrappers as opaque (not invalid). Keep
that contract: verify raw packet ownership/independent XML fact, exact no-op,
property-edit refusal before output and successful whole removal. Do not label
this a semantic reverse-property-read success or widen the XMP parser. Both CLI
readers recognize our authored EXIF/XMP. ExifTool reverse EXIF remains supported.
The required report records this specific unsupported reference operation.

## M14 WebP review after M13 green checkpoint

Reviewed RFC9649 sections 2.3–2.7.1.6 (RIFF/chunks, sizes/padding, static/extended
ordering, VP8X flags/canvas, alpha/ICC/metadata/unknown chunks) and 3.4 (bounded
VP8L signature/dimensions/alpha hint/version). Strict zero reserved bits and no
trailing bytes are approved conservative restrictions where readers may ignore.
Reconstruction/color chunks must remain ordered; metadata MAY occur out of order,
so existing target replacements remain in place. New EXIF precedes post-image XMP
where possible; new XMP is appended. No other-family chunk is relocated.

VP8L alpha_is_used is only an encoder hint: do not require equality with existing
VP8X alpha flags and do not claim transparent-pixel validation. On simple lossless
promotion derive the flag from this hint as approved. Lossy ALPH presence must
match VP8X alpha; ALPH is allowed only before VP8, with supported control bits and
raw byte count; compressed alpha/ICCP data is otherwise opaque. ALPH+VP8L is outside
the supported profile. ICC semantic validity is not asserted.

VP8 bounded frame header review: RFC6386 (November 2011), section 9.1 only, mirror
visualcommons/gamut `references/vp8/rfc6386.txt`, blob
`bb4d70ba562876818d1c365353bd92314d350b95`, SHA-256
`c044997ae5e26f74dc28569f8866937412b2783e887c96e0c09372af78be411c`.
Read only prose/field diagrams, not reference codec code later in the RFC. Validate
key/show frame, versions 0–3, start code, nonzero encoded 14-bit dimensions and
first-partition range. Scaling hints do not alter coded canvas dimensions; no
rendering/entropy/pixel conformance claim. VP8L version must be zero.

Read raw TIFF or legacy Exif-NUL-NUL wrapper; preserve original representation on
no-op and write raw TIFF on effective rebuild. This is compatibility framing, not
EXIF capacity expansion. Exact chunk presence determines metadata flags even for
duplicates; target duplicates remain ambiguous. Unknown chunks block all effective
edits but may survive structural reads/no-op, including unpromoted simple sources.

Test-only valid WebP images were encoded from owned RGB/RGBA pixels using pinned
Pillow 12.0.0/libwebp 1.6.0, not imported codec fixtures. Complete recipe, input/output
hashes and relevant license notices are in tests/fixtures/webp. No encoder dependency
is introduced in production or ordinary tests. Required external checks provision
dwebp 1.3.2 separately and compare PAM decoding before/after all four frame modes.
