"""Bounded byte-range accounting for independently supplied BMFF item extents.

This is *not* an iloc parser: callers must obtain offset, length and base from
an independently reviewed source, resolve construction methods, and establish
item ownership. Zero-length ranges and overlapping ranges are reported rather
than silently discarded. No image decoding or metadata semantics are implied.
"""
import hashlib

MAX_INPUT_BYTES = 16 * 1024 * 1024
MAX_EXTENTS = 4096
MAX_OVERLAPS = 4096
MAX_TOTAL_EXTENT_BYTES = 64 * 1024 * 1024


class ExtentError(ValueError):
    pass


def inspect_extents(data, extents):
    """Validate (base, offset, length) triplets and return ordered byte facts.

    Values must be nonnegative integers, and all ranges must lie within `data`.
    Input order and duplicates are preserved. Overlap is a separate fact; the
    caller, not this low-level helper, decides whether an overlap is legal.
    """
    if len(data) > MAX_INPUT_BYTES:
        raise ExtentError('input exceeds size limit')
    if len(extents) > MAX_EXTENTS:
        raise ExtentError('extent count exceeds limit')
    facts = []
    total_length = 0
    for triplet in extents:
        if not isinstance(triplet, (tuple, list)) or len(triplet) != 3:
            raise ExtentError('expected base, offset, length')
        base, offset, length = triplet
        if any(type(n) is not int or n < 0 for n in triplet):
            raise ExtentError('extent values must be nonnegative integers')
        start = base + offset
        end = start + length
        if start > len(data) or end > len(data):
            raise ExtentError('extent outside input')
        total_length += length
        if total_length > MAX_TOTAL_EXTENT_BYTES:
            raise ExtentError('total extent bytes exceed limit')
        payload = data[start:end]
        facts.append({'base': base, 'offset': offset, 'start': start, 'length': length,
                      'sha256': hashlib.sha256(payload).hexdigest()})
    overlaps = []
    for i, a in enumerate(facts):
        for j in range(i + 1, len(facts)):
            b = facts[j]
            if a['length'] and b['length'] and max(a['start'], b['start']) < min(
                    a['start'] + a['length'], b['start'] + b['length']):
                if len(overlaps) >= MAX_OVERLAPS:
                    raise ExtentError('overlap count exceeds limit')
                overlaps.append([i, j])
    return {'extents': facts, 'overlaps': overlaps}
