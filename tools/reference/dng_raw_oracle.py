"""Bounded raw-strip byte oracle for the owned D1 classic-TIFF DNG fixtures.

This is not a DNG decoder or a general TIFF validator. It follows IFD next links
and SubIFDs, selects CFA IFDs by PhotometricInterpretation, and extracts their
strip byte ranges. It fails closed on malformed or unsupported structures rather
than treating an unreadable image as evidence of preserved raw pixels.
"""
import hashlib


class DngOracleError(ValueError):
    """Input lies outside this oracle's supported, bounded profile."""


def raw_strips(data):
    """Return CFA strip payloads in IFD traversal / strip order.

    Supports classic TIFF, either byte order, and SHORT/LONG integer arrays.
    Limits intentionally bound work even when given an Exiv2-rewritten file.
    """
    if len(data) < 8 or data[:2] not in (b'II', b'MM'):
        raise DngOracleError('invalid TIFF header')
    byteorder = 'little' if data[:2] == b'II' else 'big'

    def number(at, size):
        if at < 0 or at + size > len(data):
            raise DngOracleError('out-of-bounds TIFF value')
        return int.from_bytes(data[at:at + size], byteorder)

    if number(2, 2) != 42:
        raise DngOracleError('not classic TIFF')

    def values(entry):
        typ = number(entry + 2, 2)
        count = number(entry + 4, 4)
        size = {3: 2, 4: 4}.get(typ)
        if size is None or count == 0 or count > 4096:
            raise DngOracleError('unsupported integer field')
        length = count * size
        at = entry + 8 if length <= 4 else number(entry + 8, 4)
        if at + length > len(data):
            raise DngOracleError('out-of-bounds integer array')
        return [number(at + i * size, size) for i in range(count)]

    first = number(4, 4)
    if not first:
        raise DngOracleError('missing IFD')
    pending = [first]
    visited = set()
    result = []
    while pending:
        offset = pending.pop()
        if offset in visited or len(visited) >= 128:
            raise DngOracleError('cyclic or excessive IFD graph')
        visited.add(offset)
        count = number(offset, 2)
        if count > 4096 or offset + 2 + 12 * count + 4 > len(data):
            raise DngOracleError('invalid IFD table')
        fields = {}
        for i in range(count):
            entry = offset + 2 + 12 * i
            tag = number(entry, 2)
            if tag in (262, 273, 279, 330):
                if tag in fields:
                    raise DngOracleError('duplicate relevant TIFF tag')
                fields[tag] = values(entry)
        next_ifd = number(offset + 2 + 12 * count, 4)
        if next_ifd:
            pending.append(next_ifd)
        for sub in reversed(fields.get(330, [])):
            if not sub:
                raise DngOracleError('null SubIFD')
            pending.append(sub)
        photo = fields.get(262, [])
        if photo == [32803]:
            offsets, lengths = fields.get(273, []), fields.get(279, [])
            if not offsets or len(offsets) != len(lengths):
                raise DngOracleError('missing or mismatched raw strips')
            for start, length in zip(offsets, lengths):
                if not length or start + length > len(data):
                    raise DngOracleError('invalid raw strip range')
                result.append(data[start:start + length])
        elif 32803 in photo:
            raise DngOracleError('ambiguous photometric interpretation')
    if not result:
        raise DngOracleError('no CFA raw strips found')
    return result


def raw_strip_fingerprints(data):
    """Return ordered lengths and SHA-256 hashes without conflating strip boundaries."""
    return [{'length': len(part), 'sha256': hashlib.sha256(part).hexdigest()}
            for part in raw_strips(data)]
