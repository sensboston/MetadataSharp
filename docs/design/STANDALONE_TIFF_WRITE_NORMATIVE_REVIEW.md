# M16 write normative review and implementation checkpoint

Exact contract approved on 2026-11-07. Source review uses specification prose and
field tables only; no reference implementation/test or PDF is redistributed.

TIFF6 Final June 3 1992, visualcommons/gamut mirror blob
b487a9b8dbb647b4d3407c72721394cb60112185, SHA-256
dbcdf729182937ecff415dfd06806894bf03bfd741291aa3ad7ba45335673def.
Reviewed section 2, field descriptions in section 8, page-number/name fields,
TIFF tile/plane descriptions and Appendix A against the approved closed schema.
IFDs/value storage can follow image data; offsets are file-relative; directories
and external values are word aligned; inline data is left justified; counts are
numbers of typed values; active IFDs are sorted and nonempty, next link terminates
with zero. Those rules permit appended graph replacement without moving images.
Old unreachable directories are not erased. ASCII may have multiple strings in
the specification; the approved new-value profile deliberately emits one printable
ASCII string. Existing untouched fields are not normalized. DateTime new values
use the approved strict Gregorian 20-byte form, not a claim about all legacy text.
ExtraSamples prose specifies SHORT; the inconsistent Appendix A BYTE row is not
used to widen the approved M15/M16 SHORT-only policy.

CIPA Exif3 final 2023, visualcommons/gamut mirror blob
83ed267755af54358149e8d99c93a89e4c26c72c, SHA-256
d1020d7f04b05d635ee1a308330b2494586da24fd8e0bb6cd96214dbf60244e8.
Reviewed 4.6.3.1.1/4.6.3.2.1/4.6.3.3.1: Exif/GPS/Interop pointers LONG/1;
metadata directories have TIFF IFD framing, not assumed image semantics. SubIFD
LONG arrays remain the approved M15 extension. No broader Exif3/DNG compliance.
Local PDF inspection used cache-only pypdf/cryptography, not production dependencies;
mirror hashes match M15's permanent record. Publisher authenticity is not claimed.

Initial implementation uses one old-to-new map for every active IFD and all
next/scalar/array directory edges. Unchanged external data and all original bytes
except the first-IFD pointer retain absolute placement. Sorted replacement entries
and changed descriptive storage append; no old gaps or slots are reused. Refuse
unknown/private/opaque data and closed-schema mismatch on effective edits, not on
provable no-op. Source identity is rechecked before saving/read-value operations.

Logical write bookkeeping ceiling (not managed-allocation accounting): original
128/IFD + (64 + 4*count)/field + 48/link + 16/image segment + 64/accounted range +
96/issue; edit snapshot 32/key + raw changed bytes; additional planning 176/IFD,
64/entry plus staged changed bytes and rebuilt pointer-array bytes, then the full
appended tail. Charge before growth. Exact-copy operations do not stage a graph;
output ceiling still applies. Source M15 reader has its own approved finite charges.

This is an INITIAL implementation checkpoint, not a completed M16 audit. Required
reference-output facts/range checks, exhaustive accepted-family/boundary/failure
coverage and final platform/reference gates remain pending; see CURRENT_STATE.md.
