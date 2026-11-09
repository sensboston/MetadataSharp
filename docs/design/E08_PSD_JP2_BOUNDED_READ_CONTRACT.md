# E08 — PSD and JP2 narrowly bounded read-only contracts

The user requested implementation of the remaining formats, grouped into one
larger PR. This is an initial **subset**, not a claim that all requested formats
are implemented or that the E01 denominator is closed. The independent per-format
fixture rights decisions precede fixture authoring. Product code uses only .NET
BCL, not Exiv2, ExifTool, Pillow, OpenJPEG or other runtime libraries.

## PSD

`PsdMetadata.Read(ReadOnlySpan<byte>)` inspects PSD v1 only: three-channel 8-bit
RGB, zero color-mode and layer/mask sections, uncompressed merged pixels of the
exact expected byte length. It validates bounded image-resource block framing,
refuses duplicate resource IDs, and returns opaque defensive copies by numeric ID.
It does not validate the contents as EXIF/XMP/IPTC, decode pixels, support PSB,
compressed or layered files, modify input, or write metadata. Data outside this
narrow profile is refused; a valid non-profile PSD is not necessarily malformed.
Finite budgets: 64 MiB file, 4 MiB resource section, 1024 resources.

## JP2

`Jp2Metadata.Read(ReadOnlySpan<byte>)` inspects bounded ordinary 32-bit-length
boxes for a boxed JP2 signature, `jp2 ` brand/compatibility, image header and
one contiguous codestream in order. It returns declared dimensions and opaque
copies of top-level XML box payloads, not parsed XMP. Extended-length and
open-ended boxes, unsupported header/brand variants and duplicate critical boxes
are refused. The codestream is not decoded or validated: reported dimensions are
header declarations, not a conformance or safe-rendering guarantee. Raw J2K,
JPX, ICC semantics, metadata editing and full JPEG 2000 support remain outside
this unit. Budgets are fixed and documented in the API. Both readers are
synchronous buffer APIs with caller-owned input; they do not do file I/O.

## Limits and verification

PSD fixtures are independently authored; JP2 uses independently authored pixel
values encoded by pinned research-only Pillow/OpenJPEG. Exact fixture bytes are
hashed and regenerated in CI. Structural checks and local independent-ish
ImageMagick decode are separate from the encoders; where ImageMagick uses the same
codec family, it is not a fully independent implementation oracle. PSD pinned
Exiv2/ExifTool preflight is already recorded; JP2 evidence remains pending until
actually observed. Tests cover valid files, bounds, malformed and unsupported
cases, duplicate/padding and defensive-copy behavior. No publication, DNG D2
write, denominator freeze, coverage percentage or unverified general support is
implied. Other B8 formats, B7 CR3 and broad real-world interoperability remain open.
