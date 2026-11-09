"""Bounded BMFF box-header inspection for B7 reference work.

This tool only accounts for a selected flat byte range and optionally reads an
ftyp box. It does not recursively infer container types, parse item locations,
validate AVIF/HEIF/HEIC/CR3/JXL, or decode images. Malformed input is an error,
not an absent box or a successful recognition. All offsets are absolute.
"""
from dataclasses import dataclass

MAX_INPUT_BYTES = 16 * 1024 * 1024
MAX_BOXES = 4096


class BoxError(ValueError):
    """The selected box stream is malformed or exceeds the probe's limits."""


@dataclass(frozen=True)
class Box:
    kind: bytes
    start: int
    size: int
    header_size: int
    user_type: bytes | None = None

    @property
    def payload_start(self):
        return self.start + self.header_size

    @property
    def end(self):
        return self.start + self.size


def boxes(data, start=0, end=None):
    """Parse one flat range; no recursion or semantic interpretation is implied."""
    if len(data) > MAX_INPUT_BYTES:
        raise BoxError('input exceeds size limit')
    if end is None:
        end = len(data)
    if not (0 <= start <= end <= len(data)):
        raise BoxError('invalid box range')
    result = []
    pos = start
    while pos < end:
        if len(result) >= MAX_BOXES:
            raise BoxError('box count exceeds limit')
        if end - pos < 8:
            raise BoxError('truncated box header')
        size32 = int.from_bytes(data[pos:pos + 4], 'big')
        kind = bytes(data[pos + 4:pos + 8])
        header = 8
        if size32 == 1:
            if end - pos < 16:
                raise BoxError('truncated extended box size')
            size = int.from_bytes(data[pos + 8:pos + 16], 'big')
            header = 16
        elif size32 == 0:
            size = end - pos
        else:
            size = size32
        user_type = None
        if kind == b'uuid':
            if end - pos < header + 16:
                raise BoxError('truncated UUID box header')
            user_type = bytes(data[pos + header:pos + header + 16])
            header += 16
        if size < header or size > end - pos:
            raise BoxError('invalid box size')
        result.append(Box(kind=kind, start=pos, size=size, header_size=header, user_type=user_type))
        pos += size
    return result


def ftyp(data, box):
    """Return raw major/compatible brands and minor version; not format validation."""
    if box.kind != b'ftyp':
        raise BoxError('not an ftyp box')
    # Recheck a caller-supplied box; do not trust manufactured Box instances.
    if box.start < 0 or box.header_size < 8 or box.payload_start > box.end or box.end > len(data):
        raise BoxError('invalid ftyp bounds')
    payload = data[box.payload_start:box.end]
    if len(payload) < 8 or len(payload) % 4:
        raise BoxError('invalid ftyp payload')
    return {'majorBrand': bytes(payload[:4]),
            'minorVersion': int.from_bytes(payload[4:8], 'big'),
            'compatibleBrands': [bytes(payload[i:i + 4]) for i in range(8, len(payload), 4)]}


def jxl_signature(data):
    """Report signature shape only; never assert validity, decodability, or metadata."""
    if data.startswith(b'\x00\x00\x00\x0cJXL \x0d\x0a\x87\x0a'):
        return 'container-signature'
    if data.startswith(b'\xff\x0a'):
        return 'codestream-signature'
    return 'unknown'
