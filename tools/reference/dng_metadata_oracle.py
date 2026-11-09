"""Bounded byte-level IFD0 metadata extraction for B6 DNG observations.

This only extracts selected classic-TIFF tags as stored. It does not validate
ICC, parse XMP/IIM, traverse Exif IFDs, or interpret the entire DNG. Duplicate
selected tags remain distinct rather than being silently coalesced.
"""


class MetadataOracleError(ValueError):
    pass


def root_values(data, selected):
    """Return selected IFD0 tag records in on-disk order, with exact value bytes."""
    if len(data) < 8 or data[:2] not in (b'II', b'MM'):
        raise MetadataOracleError('invalid TIFF header')
    order = 'little' if data[:2] == b'II' else 'big'

    def number(pos, size):
        if pos < 0 or pos + size > len(data):
            raise MetadataOracleError('out-of-bounds TIFF value')
        return int.from_bytes(data[pos:pos + size], order)

    if number(2, 2) != 42:
        raise MetadataOracleError('not classic TIFF')
    offset = number(4, 4)
    if offset < 8:
        raise MetadataOracleError('invalid IFD0 offset')
    count = number(offset, 2)
    if count > 4096 or offset + 2 + 12 * count + 4 > len(data):
        raise MetadataOracleError('invalid IFD0 table')
    # Exiv2 may serialize IPTC as LONG, so retain raw bytes and type/count.
    sizes = {1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 7: 1, 9: 4, 10: 8}
    result = []
    for i in range(count):
        entry = offset + 2 + 12 * i
        tag = number(entry, 2)
        if tag not in selected:
            continue
        typ, elements = number(entry + 2, 2), number(entry + 4, 4)
        if typ not in sizes or elements == 0 or elements > len(data):
            raise MetadataOracleError('unsupported TIFF type or count')
        length = sizes[typ] * elements
        at = entry + 8 if length <= 4 else number(entry + 8, 4)
        if at + length > len(data):
            raise MetadataOracleError('out-of-bounds TIFF payload')
        result.append({'tag': tag, 'type': typ, 'count': elements, 'bytes': data[at:at + length]})
    return result
