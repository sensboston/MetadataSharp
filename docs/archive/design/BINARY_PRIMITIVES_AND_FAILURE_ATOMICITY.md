# M02: Binary Primitives and Failure Atomicity

The approved public Endianness enum is in MetadataSharp.Core. Span-based
EndianReader/EndianWriter and CheckedRange are internal implementation details,
accessible to the test assembly through InternalsVisibleTo. No new public
contract is introduced beyond the approved enum.

Reader/writer ref structs support signed/unsigned 16-, 32- and 64-bit integers
using BinaryPrimitives. They start at position zero. A successful operation
advances exactly its width. A failed Try operation returns false, resets read
output to zero, and leaves the cursor and output buffer unchanged. Failure does
not poison the cursor: a smaller operation may subsequently succeed. Invalid
byte order is a caller argument error. No arrays, pooling or stream abstractions
are allocated by the cursors. Single-byte access needs no endianness conversion
and is not wrapped speculatively.

CheckedRange validates nonnegative additions, count-times-width products bounded
by int.MaxValue, and long-offset/length ranges before narrowing to span indices.
Empty ranges at the end are valid. Failure resets output indices/sizes to zero.
Use subtraction comparisons before addition/multiplication; malicious lengths
must not leak OverflowException or cause wraparound. These helpers return facts;
future parsers translate failure into context-specific metadata errors.

Tests use independently specified byte vectors (including negative extrema),
both byte orders, every truncated width, exact-end access, mixed operations,
unchanged sentinel output and long/int overflow boundaries. They are authored in
this repository; no external fixtures or GPL source/tests are imported.

No TIFF header/tag interpretation is implemented. Integer conversion is a .NET
BinaryPrimitives operation, not an assertion that normative JPEG/TIFF clauses
have been reviewed. The normative verification gates for actual format parsing
remain in force.
