"""B8 census fixture catalog: pinned manifest plus independent byte structure.

Standard library only. This module joins the independently authored B8 research
fixtures (two PSD layouts, one boxed JP2, one ASCII EPS) to the layout names used
by census rows, and re-derives the structural facts each observation is compared
against from the bytes themselves. It never runs the pinned Pillow/OpenJPEG
encoder and never executes PostScript, so verification works offline.

PGF, EXV and DCP have no cleared fixture (docs/research/E01_B8_RIGHTS_AND_ORACLE_REVIEW.md),
so they are deliberately absent here: their census rows stay blocked-rights rows
rather than being dropped or called not-applicable.

Structure facts are a specification-derived oracle for the container framing only.
They do not establish codec conformance, pixel correctness, metadata support or
rendering.

Usage: b8_census_fixtures.py verify    # offline, no encoders
       b8_census_fixtures.py generate # rewrite the census manifest
"""
import hashlib
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / 'tests/fixtures/b8'
MANIFEST = DIRECTORY / 'census-manifest.json'

# layout -> (format, file). The layout strings are the census axis values, so the
# manifest entry, the census row and the observation always name the same cell.
FIXTURES = {
    'flat-rgb-v1-no-resources': ('PSD', 'owned-flat-rgb.psd'),
    'flat-rgb-v1-metadata': ('PSD', 'owned-metadata-rgb.psd'),
    'jp2-boxed-rgb': ('JP2', 'owned-rgb.jp2'),
    'eps-dsc-bounding-box': ('EPS', 'owned-vector.eps'),
}
RIGHTS = 'owned-synthetic'
RECIPE = {
    'PSD': 'tools/reference/b8_psd_fixture.py; authored 2x2 RGB, 8-bit planar, v1',
    'JP2': 'tools/reference/b8_jp2_fixture.py; Pillow 11.3.0 / OpenJPEG 2.5.3, 8x8 RGB',
    'EPS': 'tools/reference/b8_eps_fixture.py; authored ASCII EPSF 3.0 single page',
}
METADATA = {
    'owned-flat-rgb.psd': 'none',
    'owned-metadata-rgb.psd': 'authored-exif-xmp-iptc',
    'owned-rgb.jp2': 'none',
    'owned-vector.eps': 'none',
}
AUTHORED_TOKENS = ('B8Artist', 'B8Tool', 'B8Keyword')
EPS_SCAN_LIMIT = 4096


def digest(data):
    return hashlib.sha256(data).hexdigest()


def entries():
    result = []
    for layout in sorted(FIXTURES):
        fmt, name = FIXTURES[layout]
        data = (DIRECTORY / name).read_bytes()
        result.append({'file': name, 'format': fmt, 'layout': layout, 'length': len(data),
                       'sha256': digest(data), 'rights': RIGHTS, 'recipe': RECIPE[fmt],
                       'metadata': METADATA[name]})
    return result


def psd_resources(data):
    """Independent PSD framing: 26-byte header, four sections in file order, 8BIM blocks.

    Section order and lengths are read sequentially, so the resource section is never
    assumed to be empty and the layer length is never read from the wrong offset.
    """
    if len(data) < 40 or data[:4] != b'8BPS':
        raise ValueError('missing PSD signature')
    version, channels, height, width, depth, color_mode = struct.unpack_from('>H6xHIIHH', data, 4)
    if version != 1:
        raise ValueError(f'only PSD v1 is authored here, found version {version}')
    if depth != 8 or channels != 3 or color_mode != 3:
        raise ValueError('authored PSD is 8-bit RGB (color mode 3), three channels')
    offset = 26
    lengths = {}
    for section in ('colorMode', 'imageResources', 'layerAndMask'):
        if offset + 4 > len(data):
            raise ValueError(f'truncated PSD {section} length field')
        lengths[section] = struct.unpack_from('>I', data, offset)[0]
        offset += 4 + lengths[section]
    resource_start = 34 + lengths['colorMode']
    resource_end = resource_start + lengths['imageResources']
    if offset > len(data) or resource_end > len(data):
        raise ValueError('a PSD section length exceeds the file')
    if lengths['colorMode'] != 0:
        raise ValueError('authored PSD keeps the color mode data section empty')
    resources, cursor = [], resource_start
    while cursor < resource_end:
        if data[cursor:cursor + 4] != b'8BIM' or cursor + 12 > resource_end:
            raise ValueError('unexpected bytes inside the image resource section')
        identifier, name_length = struct.unpack_from('>HB', data, cursor + 4)
        padded_name = (1 + name_length + 1) & ~1
        size = struct.unpack_from('>I', data, cursor + 6 + padded_name)[0]
        payload_start = cursor + 10 + padded_name
        payload_end = payload_start + size
        if payload_end > resource_end:
            raise ValueError('resource payload length exceeds the image resource section')
        resources.append({'identifier': identifier, 'nameLength': name_length,
                          'payloadLength': size, 'payloadSha256': digest(data[payload_start:payload_end])})
        cursor = payload_end + (payload_end & 1)
    if offset + 2 > len(data):
        raise ValueError('truncated PSD image compression field')
    compression = struct.unpack_from('>H', data, offset)[0]
    if compression != 0:
        raise ValueError('authored PSD image data is uncompressed')
    pixel_start = offset + 2
    planes = height * width * channels * (depth // 8)
    if pixel_start + planes != len(data):
        raise ValueError(f'uncompressed planar pixel data length mismatch: expected {planes} bytes')
    return {'signature': '8BPS', 'version': version, 'channels': channels, 'height': height,
            'width': width, 'depth': depth, 'colorMode': color_mode,
            'colorModeLength': lengths['colorMode'], 'resourceLength': lengths['imageResources'],
            'layerLength': lengths['layerAndMask'], 'resources': resources,
            'pixelBytesSha256': digest(data[pixel_start:]), 'pixelBytesLength': planes}


def jp2_boxes(data):
    """Independent top-level JP2 box walk (32-bit lengths only, as authored).

    The authored fixture is the Pillow/OpenJPEG output: signature, ftyp, jp2h, jp2c.
    It carries no 'xml ' box and no jp2e end box, and that absence is part of the
    recorded structure rather than an assumed conformance property.
    """
    boxes, offset = [], 0
    while offset < len(data):
        if offset + 8 > len(data):
            raise ValueError('truncated JP2 box header')
        length = struct.unpack_from('>I', data, offset)[0]
        kind = data[offset + 4:offset + 8].decode('latin-1')
        if length == 1:
            raise ValueError('64-bit box length is not authored in this fixture')
        if length < 8 or offset + length > len(data):
            raise ValueError(f'invalid JP2 box length {length} for box {kind!r}')
        boxes.append({'type': kind, 'offset': offset, 'length': length,
                      'payloadSha256': digest(data[offset + 8:offset + length])})
        offset += length
    types = [b['type'] for b in boxes]
    if types[:1] != ['jP  ']:
        raise ValueError('JP2 fixture must start with the signature box')
    if data[8:12] != b'\x0d\x0a\x87\x0a':
        raise ValueError('JP2 signature box carries the wrong four-byte signature')
    if 'jp2h' not in types:
        raise ValueError('missing the JP2 header superbox')
    if types[-1] != 'jp2c':
        raise ValueError('authored JP2 keeps the contiguous codestream as its last box')
    stream = boxes[-1]
    return {'boxTypes': types, 'boxes': boxes, 'fileLength': len(data),
            'headerSuperbox': True, 'hasEndBox': 'jp2e' in types, 'hasXmlBox': 'xml ' in types,
            'codestreamOffset': stream['offset'], 'codestreamLength': stream['length'],
            'codestreamSha256': stream['payloadSha256']}


def eps_structure(data):
    """Bounded DSC comment scan of the header region. PostScript is never executed."""
    head = data[:EPS_SCAN_LIMIT]
    if b'\x00' in data:
        raise ValueError('authored EPS is ASCII with no NUL bytes')
    if not head.startswith(b'%!PS-Adobe-3.0 EPSF-3.0\n'):
        raise ValueError('missing the authored EPSF 3.0 identification line')
    comments, box, creator = [], None, None
    for line in head.split(b'\n'):
        if line.startswith(b'%%'):
            keyword, _, value = line[2:].partition(b':')
            comments.append(keyword.decode('latin-1'))
            if keyword == b'BoundingBox' and box is None:
                parts = value.split()
                if len(parts) != 4 or any(not p.lstrip(b'-').isdigit() for p in parts):
                    raise ValueError('BoundingBox must carry four immediate integers')
                box = [int(p) for p in parts]
            if keyword == b'Creator' and creator is None:
                creator = value.strip().decode('latin-1')
    if 'BoundingBox' not in comments or 'EndComments' not in comments:
        raise ValueError('authored EPS requires BoundingBox and EndComments')
    if not head.rstrip().endswith(b'%%EOF'):
        raise ValueError('missing the EPS end-of-file comment')
    return {'headerBytes': len(head), 'dscComments': comments, 'boundingBox': box,
            'creator': creator, 'postScriptExecuted': False, 'fileLength': len(data)}


def structure(fmt, data):
    if fmt == 'PSD':
        return psd_resources(data)
    if fmt == 'JP2':
        return jp2_boxes(data)
    if fmt == 'EPS':
        return eps_structure(data)
    raise ValueError(f'no B8 structure oracle for {fmt}')


def verify():
    """Check every fixture against its own authoring module and the census manifest."""
    import b8_eps_fixture as eps
    import b8_jp2_fixture as jp2
    import b8_psd_fixture as psd
    for module in (psd, jp2, eps):
        if not module.verify():
            raise ValueError(f'{module.__name__} reported no B8 fixture manifest')
    if (DIRECTORY / psd.NAME).read_bytes() != psd.encode():
        raise ValueError('PSD flat fixture differs from its authored bytes')
    if (DIRECTORY / psd.RICH_NAME).read_bytes() != psd.encode_rich():
        raise ValueError('PSD metadata fixture differs from its authored bytes')
    if (DIRECTORY / eps.FILE).read_bytes() != eps.DATA:
        raise ValueError('EPS fixture differs from its authored bytes')
    for fmt, name in FIXTURES.values():
        data = (DIRECTORY / name).read_bytes()
        facts = structure(fmt, data)
        if fmt == 'PSD' and facts['resourceLength'] == 0 and facts['resources']:
            raise ValueError('PSD resource accounting disagrees with the empty section')
        if fmt == 'PSD' and name == psd.RICH_NAME:
            identifiers = sorted(r['identifier'] for r in facts['resources'])
            if identifiers != [1028, 1058, 1060]:
                raise ValueError(f'PSD metadata fixture lost a resource: {identifiers}')
        if fmt == 'EPS' and facts['boundingBox'] != [0, 0, 10, 12]:
            raise ValueError('EPS bounding box differs from the authored values')
    recorded = json.loads(MANIFEST.read_text(encoding='utf-8')) if MANIFEST.exists() else None
    if recorded != entries():
        raise ValueError('B8 census fixture manifest differs from the fixture bytes')
    return recorded


def main():
    if sys.argv[1:] == ['generate']:
        DIRECTORY.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_text(json.dumps(entries(), indent=2) + '\n', encoding='utf-8')
        print(f'wrote {MANIFEST.relative_to(ROOT)}')
    elif sys.argv[1:] != ['verify']:
        raise SystemExit('expected verify or generate')
    manifest = verify()
    print(f'B8 census fixture catalog verified: {len(manifest)} fixtures')


if __name__ == '__main__':
    main()
