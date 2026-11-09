# E01 B8 — individual format rights and oracle review

B8 covers JP2, PSD, PGF, EPS, EXV and DCP. This ledger records source and
fixture decisions separately, before any fixture authoring. It is research
only: no product implementation, write support, exclusion, coverage
percentage, denominator freeze, or package publication is authorized here.
No external sample, Exiv2 code/tests/tag dictionary, or vendor camera data is
cleared for copying. Referencing a specification is not permission to copy a
fixture or a patent clearance. Exiv2 0.28.9 is the pinned external black-box
reference; ExifTool 12.76 is secondary, not a normative oracle.

## Format-by-format decisions

| Format | Source reviewed / decision | Limitations and next steps |
| --- | --- | --- |
| PSD | Adobe's [Photoshop File Formats Specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) describes the 26-byte header, v1 PSD vs v2 PSB, big-endian lengths and planar uncompressed merged image data. **Permit** an independently authored, tiny uncompressed RGB v1 PSD with no resources and no layers, from explicit bytes and pixel values. The source is used as a format reference, not copied code, images or text. | This is only a simple flattened layout. Independently validate exact header, section lengths, plane contents and decoding; metadata resources, layers, PSB and color management remain untested. A recognized PSD is not metadata support. |
| JP2 | [DGIWG JPEG2000 profile](https://portal.dgiwg.org/files/12178), appendix C, describes the signature and boxes for its own JPX profile. JP2 is distinct from raw JPEG 2000 codestream and JPX. | No fixture cleared yet. Identify an independently authored, pinned encoder and its provenance/rights; inspect actual JP2 structure and decode independently. An ftyp/signature alone is not a valid image. ISO/IEC 15444-1 conformance and patent clearance are not established. |
| PGF | No sufficient independent normative specification or encoder rights review completed. | No fixture cleared. Do not synthesize a magic-byte stub and call it a valid image. Avoid importing GPL/LGPL implementation or samples. |
| EPS | Preliminary DSC/EPS sources describe a single-page PostScript program and bounding box, but not yet reviewed enough to establish a safe fixture and independent oracle. | No fixture cleared yet. EPS is executable PostScript: do not execute untrusted files or assume a header proves safety/conformance; preview and XMP forms need separate checks. |
| EXV | No independent layout and sample-rights review completed. It is an Exiv2-associated metadata format, not necessarily a normal raster image. | No fixture cleared yet. Do not copy Exiv2 implementation, tests, dictionaries or output as a normative oracle. |
| DCP | Adobe [Digital Negative Specification 1.7.1.0](https://helpx.adobe.com/content/dam/help/en/camera-raw/digital-negative/jcr_content/root/content/flex/items/position/position-par/download_section_733958301/download-1/DNG_Spec_1_7_1_0.pdf) describes camera profile IFDs and a `0x4352` magic in the ExtraCameraProfiles discussion. | DCP is a camera profile, not an ordinary TIFF image. No camera profile fixture cleared yet: establish the applicable standalone profile layout and required tags, independent oracle and rights before authoring. Do not fabricate camera data or infer standalone validity from the embedded-IFD description. |

## PSD image-resource extension — reviewed before authoring

Adobe's November 2019 specification, Image Resource Blocks and Image Resource
IDs sections, describes `8BIM` blocks with a two-byte ID, even-padded Pascal
name, four-byte data length and even-padded data. It identifies 1028 as IPTC,
1058 as EXIF data 1, and 1060 as XMP. Permit a second independently authored
PSD v1 using the same owned pixels and only independently authored values in
those three resources. Record exact byte lengths/padding, payloads and
independent extraction checks. This does not clear ICC or thumbnail resources,
PSB, layer structures, copied samples, or any other format; Adobe's overview of
EXIF does not by itself resolve every EXIF prefix/encoding ambiguity, so both
external references' actual behavior and independent parsing must be recorded.

## JP2 owned-fixture decision — reviewed before authoring

The [Library of Congress JPEG 2000 profile](https://www.loc.gov/ndnp/guidelines/docs/NDNP_JP2HistNewsProfile.pdf), appendix A, describes JP2 signature and file-type box order, the JP2 header superbox and contiguous codestream; it is a profile/overview, not a full ISO/IEC 15444-1 conformance oracle. [Pillow's JPEG 2000 documentation](https://pillow.readthedocs.io/en/stable/handbook/image-file-formats.html) distinguishes boxed JP2 from bare codestream and describes its OpenJPEG encoder. Permit only an independently authored RGB pixel array encoded to boxed JP2 using pinned research-only Pillow/OpenJPEG, with wheel hashes, independently checked box framing, deterministic regeneration and decoder sanity check. No external images, Exiv2 implementation or samples are copied; no patent clearance or full normative conformance is implied. Metadata-present JP2 requires its own later review.

## EPS owned-fixture decision — reviewed before authoring

Adobe's [EPSF 3.0 specification](https://printtechnologies.org/standards/files/epsf_spec_v3_0.pdf) describes required identifying and BoundingBox DSC comments and the single-page enclosing rectangle. Permit only a small independently authored ASCII EPS with a simple geometrical drawing, no external data, no binaries, no fonts, no preview and no copied examples. A bounded header scanner must never execute PostScript; checking a bounding box does not prove EPS or PostScript conformance, safe execution, rendering, metadata support or that the box actually encloses the marks. Other EPS variants, deferred bounding boxes and previews are not cleared by this decision.

## General gate

For each later format decision, document the specific source, rights for the
actual authored or individually licensed bytes, recipe and immutable hashes,
independent structural expectations, decoder limits and metadata presence.
No B8 rows have yet been observed. Unknowns are pending, not refusals or
not-applicable; when evidence is unavailable, record blocking honestly. Metadata
absent in an image cannot prove metadata-present reading/extraction. DNG D2
write remains outside authorization. B7 CR3 and its other gaps stay open.
