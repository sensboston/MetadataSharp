"""Bounded extractor for an authored JPEG thumbnail in the first chained IFD."""


class ThumbnailOracleError(ValueError):
    pass


def ifd1_jpeg(data):
    if len(data) < 8 or data[:2] not in (b'II', b'MM'):
        raise ThumbnailOracleError('invalid TIFF header')
    order = 'little' if data[:2] == b'II' else 'big'

    def num(pos, size):
        if pos < 0 or pos + size > len(data):
            raise ThumbnailOracleError('out-of-bounds value')
        return int.from_bytes(data[pos:pos + size], order)

    def table(offset):
        if offset < 8:
            raise ThumbnailOracleError('invalid IFD offset')
        count = num(offset, 2)
        end = offset + 2 + count * 12
        if count > 4096 or end + 4 > len(data):
            raise ThumbnailOracleError('invalid IFD')
        return count, end

    if num(2, 2) != 42:
        raise ThumbnailOracleError('not classic TIFF')
    _, end = table(num(4, 4))
    ifd1 = num(end, 4)
    if ifd1 == 0:
        return None
    count, _ = table(ifd1)
    fields = {}
    for i in range(count):
        entry = ifd1 + 2 + i * 12
        tag = num(entry, 2)
        if tag not in (259, 513, 514):
            continue
        typ, n = num(entry + 2, 2), num(entry + 4, 4)
        if tag in fields or typ not in (3, 4) or n != 1:
            raise ThumbnailOracleError('unsupported or duplicate thumbnail field')
        fields[tag] = num(entry + 8, 2 if typ == 3 else 4)
    if not (513 in fields or 514 in fields):
        return None
    if fields.get(259) != 6 or 513 not in fields or 514 not in fields:
        raise ThumbnailOracleError('incomplete JPEG thumbnail')
    start, size = fields[513], fields[514]
    if not size or start + size > len(data):
        raise ThumbnailOracleError('out-of-bounds JPEG thumbnail')
    return data[start:start + size]
