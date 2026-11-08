"""Generate owned synthetic minimal DNG fixtures from the DNG 1.7.1.0 prose.

Our own deterministic stdlib writer — not derived from Adobe SDK code, vendor
samples or any imported corpus. Raw image: 4x2, 16-bit unsigned CFA (photometric
32803), one strip. Thumbnail variant adds a 4x2 8-bit RGB (photometric 2) IFD0
with one SubIFD (330, LONG/1) to the raw leaf; the RGB root also carries the DNG identity tags. Deterministic layout: read by both
this generator and tools/MetadataSharp.ReferenceFixture; CI compares SHA-256.

Usage: dng_fixtures.py generate OUTDIR   # writes four .dng files + manifest.json
"""
import argparse, hashlib, json, struct
from pathlib import Path

T_BYTE, T_ASCII, T_SHORT, T_LONG, T_RATIONAL, T_SRATIONAL = 1, 2, 3, 4, 5, 10
SIZEOF = {T_BYTE: 1, T_ASCII: 1, T_SHORT: 2, T_LONG: 4, T_RATIONAL: 8, T_SRATIONAL: 8}

RAW_PIXELS = [0x0000, 0x1000, 0x2000, 0x3000, 0x4000, 0x5000, 0x6000, 0x7000]
THUMB_PIXELS = list(range(24))
SOFTWARE = b"MetadataSharp D1 fixture\0"   # 25 bytes
ARTIST = b"MetadataSharp\0"                # 14 bytes
UNIQUE_MODEL = b"MetadataSharp DNG1\0"     # 20 bytes


def pack_payload(fmt, type_id, values):
    if type_id == T_BYTE:
        return bytes(values)
    if type_id == T_ASCII:
        return bytes(values)
    if type_id == T_SHORT:
        return struct.pack(f"{fmt}{len(values)}H", *values)
    if type_id == T_LONG:
        return struct.pack(f"{fmt}{len(values)}I", *values)
    if type_id == T_RATIONAL:
        flat = [component for value in values for component in value]
        return struct.pack(f"{fmt}{len(flat)}I", *flat)
    if type_id == T_SRATIONAL:
        flat = [component for value in values for component in value]
        return struct.pack(f"{fmt}{len(flat)}i", *flat)
    raise AssertionError(type_id)


def align2(value):
    return (value + 1) & ~1


def raw_entries():
    return [
        (254, T_LONG, 1, [0]),
        (256, T_LONG, 1, [4]),
        (257, T_LONG, 1, [2]),
        (258, T_SHORT, 1, [16]),
        (259, T_SHORT, 1, [1]),
        (262, T_SHORT, 1, [32803]),
        (273, T_LONG, 1, [0]),  # patched: raw strip offset
        (274, T_SHORT, 1, [1]),
        (277, T_SHORT, 1, [1]),
        (278, T_LONG, 1, [2]),
        (279, T_LONG, 1, [16]),
        (284, T_SHORT, 1, [1]),
        (305, T_ASCII, len(SOFTWARE), SOFTWARE),
        (315, T_ASCII, len(ARTIST), ARTIST),
        (339, T_SHORT, 1, [1]),
        (33421, T_SHORT, 2, [2, 2]),
        (33422, T_BYTE, 4, [0, 1, 1, 2]),
        (50706, T_BYTE, 4, [1, 1, 0, 0]),
        (50707, T_BYTE, 4, [1, 0, 0, 0]),
        (50708, T_ASCII, len(UNIQUE_MODEL), UNIQUE_MODEL),
        (50710, T_BYTE, 3, [0, 1, 2]),
        (50711, T_SHORT, 1, [1]),
        (50713, T_SHORT, 2, [1, 1]),
        (50714, T_RATIONAL, 1, [(0, 1)]),
        (50717, T_LONG, 1, [65535]),
        (50719, T_LONG, 2, [0, 0]),
        (50720, T_LONG, 2, [2, 4]),
        (50721, T_SRATIONAL, 9, [(1, 1), (0, 1), (0, 1), (0, 1), (1, 1), (0, 1), (0, 1), (0, 1), (1, 1)]),
        (50728, T_RATIONAL, 3, [(1, 1), (1, 1), (1, 1)]),
        (50778, T_SHORT, 1, [21]),
        (50829, T_LONG, 4, [0, 0, 2, 4]),
    ]


def thumbnail_entries():
    return [
        (254, T_LONG, 1, [1]),
        (256, T_LONG, 1, [4]),
        (257, T_LONG, 1, [2]),
        (258, T_SHORT, 3, [8, 8, 8]),
        (259, T_SHORT, 1, [1]),
        (262, T_SHORT, 1, [2]),
        (273, T_LONG, 1, [0]),  # patched: thumbnail strip offset
        (274, T_SHORT, 1, [1]),
        (277, T_SHORT, 3, [3, 3, 3]),
        (278, T_LONG, 1, [2]),
        (279, T_LONG, 1, [24]),
        (284, T_SHORT, 1, [1]),
        (305, T_ASCII, len(SOFTWARE), SOFTWARE),
        (330, T_LONG, 1, [0]),  # patched: raw IFD offset
        (339, T_SHORT, 3, [1, 1, 1]),
        (50706, T_BYTE, 4, [1, 1, 0, 0]),
        (50707, T_BYTE, 4, [1, 0, 0, 0]),
        (50708, T_ASCII, len(UNIQUE_MODEL), UNIQUE_MODEL),
        (50721, T_SRATIONAL, 9, [(1, 1), (0, 1), (0, 1), (0, 1), (1, 1), (0, 1), (0, 1), (0, 1), (1, 1)]),
        (50728, T_RATIONAL, 3, [(1, 1), (1, 1), (1, 1)]),
        (50778, T_SHORT, 1, [21]),
    ]


def plan_ifd(fmt, entries, base):
    """Compute absolute external offsets; returns (items, end_offset)."""
    body = 2 + 12 * len(entries) + 4
    ext = align2(base + body)
    items = []
    for tag, type_id, count, values in sorted(entries, key=lambda e: e[0]):
        payload = pack_payload(fmt, type_id, values)
        assert len(payload) == SIZEOF[type_id] * count, (tag, len(payload))
        external = len(payload) > 4
        if external:
            ext = align2(ext)
            items.append([tag, type_id, count, payload, ext])
            ext += len(payload)
        else:
            items.append([tag, type_id, count, payload, None])
    tags = [i[0] for i in items]
    assert tags == sorted(tags) and len(set(tags)) == len(tags)
    return items, align2(ext)


def render_ifd(fmt, items, base, next_offset):
    body = bytearray(2 + 12 * len(items) + 4)
    struct.pack_into(f"{fmt}H", body, 0, len(items))
    extras = []
    for i, (tag, type_id, count, payload, off) in enumerate(items):
        struct.pack_into(f"{fmt}HHI", body, 2 + 12 * i, tag, type_id, count)
        if off is None:
            cell = bytearray(4)
            cell[:len(payload)] = payload
            body[2 + 12 * i + 8: 2 + 12 * i + 12] = cell
        else:
            struct.pack_into(f"{fmt}I", body, 2 + 12 * i + 8, off)
            extras.append((off, payload))
    struct.pack_into(f"{fmt}I", body, 2 + 12 * len(items), next_offset)
    data = bytes(body)
    end = base + len(data)
    blob = bytearray()
    for off, payload in sorted(extras, key=lambda kv: kv[0]):
        blob += b"\0" * (off - (end + len(blob))) + payload
    return data + bytes(blob)


def raw_pixels_bytes(fmt):
    return struct.pack(f"{fmt}{len(RAW_PIXELS)}H", *RAW_PIXELS)


def thumb_pixels_bytes():
    return bytes(THUMB_PIXELS)


def build(order, shape):
    fmt = "<" if order == "II" else ">"
    order_mark = order.encode("ascii")
    magic = struct.pack(f"{fmt}H", 42)
    blocks = {0: order_mark + magic + struct.pack(f"{fmt}I", 8)}

    if shape == "raw":
        raw = [list(e) for e in raw_entries()]
        raw[6][3] = [0]
        items, ext_end = plan_ifd(fmt, raw, 8)
        raw_strip = ext_end
        raw[6][3] = [raw_strip]
        items, _ = plan_ifd(fmt, raw, 8)
        blocks[8] = render_ifd(fmt, items, 8, 0)
        blocks[raw_strip] = raw_pixels_bytes(fmt)
        info = dict(ifd0=8, raw_ifd=8, raw_strip=raw_strip, raw_strip_length=16,
                    thumb_ifd=None, thumb_strip=None)
    else:
        thumb = [list(e) for e in thumbnail_entries()]
        raw = [list(e) for e in raw_entries()]
        thumb[6][3] = [0]
        raw[6][3] = [0]
        _, t_ext_end = plan_ifd(fmt, thumb, 8)
        thumb_strip = t_ext_end
        raw_base = thumb_strip + 24
        thumb[6][3] = [thumb_strip]
        thumb[13] = [330, T_LONG, 1, [raw_base]]
        t_items, _ = plan_ifd(fmt, thumb, 8)
        _, r_ext_end = plan_ifd(fmt, raw, raw_base)
        raw_strip = r_ext_end
        raw[6][3] = [raw_strip]
        r_items, _ = plan_ifd(fmt, raw, raw_base)
        blocks[8] = render_ifd(fmt, t_items, 8, 0)
        blocks[thumb_strip] = thumb_pixels_bytes()
        blocks[raw_base] = render_ifd(fmt, r_items, raw_base, 0)
        blocks[raw_strip] = raw_pixels_bytes(fmt)
        info = dict(ifd0=8, raw_ifd=raw_base, raw_strip=raw_strip, raw_strip_length=16,
                    thumb_ifd=8, thumb_strip=thumb_strip)

    data = bytearray()
    for off in sorted(blocks):
        chunk = blocks[off]
        data += b"\0" * (off - len(data)) + chunk
    return bytes(data), info


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("command", choices=["generate"])
    ap.add_argument("outdir")
    args = ap.parse_args()
    out = Path(args.outdir)
    out.mkdir(parents=True, exist_ok=True)
    manifest = []
    for order in ("II", "MM"):
        for shape in ("raw", "thumb"):
            data, info = build(order, shape)
            name = f"dng-{shape}-{'ii' if order == 'II' else 'mm'}.dng"
            (out / name).write_bytes(data)
            manifest.append({
                "file": name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "length": len(data),
                "byteOrder": order,
                "shape": shape,
                **info,
                "rawStripSha256": hashlib.sha256(raw_pixels_bytes("<" if order == "II" else ">")).hexdigest(),
            })
            print(name, len(data), manifest[-1]["sha256"])
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
