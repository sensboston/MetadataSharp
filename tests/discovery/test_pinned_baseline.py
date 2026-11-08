"""Our fixture-specific byte checks and fail-closed receipt policy; no external CLI needed."""
import base64
import copy
import json
from pathlib import Path
import struct
import sys
import unittest
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import exiv2_baseline as baseline
from collect_baseline import decode_receipt


def segment(marker, payload):
    return bytes([255, marker]) + struct.pack('>H', len(payload)+2) + payload


def fixture(artist=b'Alice\0', orientation=1, xmp=b'owned-non-EXIF-APP1'):
    # Authored one-IFD TIFF structure, not copied from a vendor/test file.
    fields = [(274, 3, 1, struct.pack('<H', orientation))]
    if artist is not None:
        fields.append((315, 2, len(artist), artist))
    end = 8 + 2 + 12 * len(fields) + 4
    entries, tail = b'', b''
    for tag, kind, count, raw in fields:
        value = raw.ljust(4, b'\0') if len(raw) <= 4 else struct.pack('<I', end+len(tail))
        entries += struct.pack('<HHI', tag, kind, count) + value
        if len(raw) > 4:
            tail += raw
    tiff = b'II\x2a\0\x08\0\0\0' + struct.pack('<H', len(fields)) + entries + b'\0'*4 + tail
    # Not a decoder fixture: enough framing to exercise ONLY the bounded reader's guards.
    return b'\xff\xd8' + segment(0xe1, b'Exif\0\0'+tiff) + segment(0xe1, xmp) + segment(0xfe, b'owned-comment') + segment(0xda, b'owned-scan-header') + b'owned-scan\xff\xd9'


class BaselinePolicyTests(unittest.TestCase):
    def test_exact_version_required(self):
        baseline.check_version('exiv2 0.28.9\n')
        for text in ('exiv2 0.27.6', 'exiv2 0.28.90', 'exiv2 0.28.9.1', 'exiv2 0.28.9-dev', ''):
            with self.subTest(text=text), self.assertRaises(ValueError):
                baseline.check_version(text)

    def test_cache_requires_every_explicit_feature(self):
        cache = '\n'.join(f'{k}:{"STRING" if k == "CMAKE_BUILD_TYPE" else "BOOL"}={v}' for k, v in baseline.OPTIONS.items())
        baseline.check_cache(cache)
        for key in baseline.OPTIONS:
            with self.subTest(key=key), self.assertRaises(ValueError):
                baseline.check_cache('\n'.join(s for s in cache.splitlines() if not s.startswith(key+':')))
        with self.assertRaises(ValueError):
            baseline.check_cache(cache.replace('EXIV2_ENABLE_BMFF:BOOL=ON', 'EXIV2_ENABLE_BMFF:BOOL=OFF'))

    def test_cache_retains_unrequested_features(self):
        cache = '\n'.join(f'{k}:{"STRING" if k == "CMAKE_BUILD_TYPE" else "BOOL"}={v}' for k, v in baseline.OPTIONS.items())
        self.assertEqual('OFF', baseline.check_cache(cache+'\nEXIV2_OTHER_FEATURE:BOOL=OFF')['EXIV2_OTHER_FEATURE'])
        with self.assertRaises(ValueError):
            baseline.check_cache(cache+'\nEXIV2_ENABLE_PNG:BOOL=ON')

    def test_unused_cmake_option_does_not_count_as_feature(self):
        cache = '\n'.join(f'{k}:{"STRING" if k == "CMAKE_BUILD_TYPE" else "BOOL"}={v}' for k, v in baseline.OPTIONS.items())
        with self.assertRaises(ValueError):
            baseline.check_cache(cache.replace('EXIV2_ENABLE_PNG:BOOL=', 'EXIV2_ENABLE_PNG:UNINITIALIZED='))

    def test_actual_runtime_features_must_be_enabled(self):
        facts = '\n'.join(f'{k}=1' for k in baseline.RUNTIME_FEATURES)
        baseline.check_runtime_features(facts)
        for key in baseline.RUNTIME_FEATURES:
            for replacement in ('', f'{key}=0'):
                with self.assertRaises(ValueError):
                    baseline.check_runtime_features(facts.replace(f'{key}=1', replacement))

    def test_independent_artist_type_count_and_raw_bytes(self):
        before = baseline.inspect_jpeg(fixture())
        self.assertEqual((2, 6, b'Alice\0'), before['tags'][315])
        for artist in (b'Bob\0', b'LongerName\0', None):
            baseline.verify(before, baseline.inspect_jpeg(fixture(artist)), artist)
        with self.assertRaises(ValueError):
            baseline.verify(before, baseline.inspect_jpeg(fixture(b'Bob\0')), b'Bob')

    def test_other_segments_and_orientation_must_survive(self):
        before = baseline.inspect_jpeg(fixture())
        for data in (fixture(b'Bob\0', orientation=2), fixture(b'Bob\0', xmp=b'changed'), fixture(b'Bob\0').replace(b'owned-scan\xff', b'changed-scan\xff')):
            with self.assertRaises(ValueError):
                baseline.verify(before, baseline.inspect_jpeg(data), b'Bob\0')

    def test_byte_identical_permutation_is_reported_not_exact_preservation(self):
        before = baseline.inspect_jpeg(fixture())
        after = copy.deepcopy(before)
        after['nonExifParts'].reverse()
        after['nonExif'] = b''.join(after['nonExifParts'])
        with self.assertRaises(ValueError):
            baseline.verify(before, after, b'Alice\0')
        self.assertEqual('byte-identical-segment-permutation-NOT-exact-order', baseline.verify(before, after, b'Alice\0', allow_segment_reorder=True))

    def test_permutation_mode_still_rejects_payload_loss_and_duplication(self):
        before = baseline.inspect_jpeg(fixture())
        for parts in (before['nonExifParts'][1:], before['nonExifParts']*2, [s+b'\0' for s in before['nonExifParts']]):
            after = copy.deepcopy(before); after['nonExifParts'] = parts; after['nonExif'] = b''.join(parts)
            with self.assertRaises(ValueError):
                baseline.verify(before, after, b'Alice\0', allow_segment_reorder=True)

    def test_exact_known_comment_difference_is_visible_and_strict_by_default(self):
        before = baseline.inspect_jpeg(fixture())
        before['nonExifParts'].append(baseline.COMMENT_BEFORE)
        before['nonExif'] = b''.join(before['nonExifParts'])
        after = copy.deepcopy(before)
        after['nonExifParts'][-1] = baseline.COMMENT_AFTER
        after['nonExifParts'].reverse(); after['nonExif'] = b''.join(after['nonExifParts'])
        with self.assertRaises(ValueError):
            baseline.verify(before, after, b'Alice\0', allow_segment_reorder=True)
        self.assertEqual(baseline.COMMENT_DIFFERENCE, baseline.verify(before, after, b'Alice\0', observe_known_comment_difference=True))
        for extra in ([baseline.COMMENT_AFTER], [b'changed-XMP']):
            bad = copy.deepcopy(after); bad['nonExifParts'] += extra; bad['nonExif'] = b''.join(bad['nonExifParts'])
            with self.assertRaises(ValueError):
                baseline.verify(before, bad, b'Alice\0', allow_segment_reorder=True, observe_known_comment_difference=True)

    def test_non_exif_app1_is_not_misclassified(self):
        info = baseline.inspect_jpeg(fixture(xmp=b'Exif-not-a-signature'))
        self.assertIn(b'Exif-not-a-signature', info['nonExif'])

    def test_truncated_invalid_and_duplicate_structures_refuse(self):
        good = fixture()
        extra = segment(0xe1, b'Exif\0\0II\x2a\0\x08\0\0\0')
        for data in (b'', good[:-1], good[:5], b'\xff\xd8\xff\xd9', good[:2]+extra+good[2:], good.replace(b'II\x2a', b'MM\x2a', 1)):
            with self.assertRaises(ValueError):
                baseline.inspect_jpeg(data)

    def test_out_of_range_value_offset_refuses(self):
        data = bytearray(fixture())
        # IFD0 second entry is the authored 6-byte ASCII value.
        struct.pack_into('<I', data, 12+8+2+12+8, 0xffffffff)
        with self.assertRaises(ValueError):
            baseline.inspect_jpeg(bytes(data))

    def test_print_must_agree_with_independent_expected(self):
        baseline.parse_artist('Exif.Image.Artist Ascii 6 Alice\n', b'Alice\0')
        baseline.parse_artist('', None)
        for text, expected in [('Exif.Image.Artist Ascii 5 Alice', b'Alice\0'), ('Exif.Image.Artist Ascii 6 Bob', b'Alice\0'), ('Exif.Image.Artist Ascii 6 Alice', None)]:
            with self.assertRaises(ValueError):
                baseline.parse_artist(text, expected)


class ReceiptPolicyTests(unittest.TestCase):
    def setUp(self):
        self.source = 'a'*40
        self.report = dict(sourceCommit=self.source, status='passed', baseline=dict(sourceCommit=baseline.PIN, version=baseline.VERSION),
            observations=[dict(id=str(i), operation=op, baselineObservation='passed-independent-byte-and-print-check') for i, op in enumerate(('read', 'insert', 'replace', 'remove'))])

    def chunks(self, report=None):
        text = base64.b64encode(zlib.compress(json.dumps(report or self.report).encode())).decode()
        parts = [text[i:i+120] for i in range(0, len(text), 120)]
        return [dict(title=f'E01Z observations.json {i}/{len(parts)}', message=p) for i, p in enumerate(parts)]

    def test_complete_reordered_chunks_round_trip(self):
        raw, report = decode_receipt(list(reversed(self.chunks())), self.source)
        self.assertEqual(self.report, report)
        self.assertEqual(report, json.loads(raw))

    def test_missing_duplicate_or_inconsistent_chunks_refuse(self):
        chunks = self.chunks()
        bad = copy.deepcopy(chunks); bad[-1]['title'] = bad[-1]['title'].replace('/'+str(len(chunks)), '/40')
        for annotations in ([], chunks[:-1], chunks+chunks[:1], bad):
            with self.assertRaises(ValueError):
                decode_receipt(annotations, self.source)

    def test_wrong_source_baseline_status_or_missing_operation_refuse(self):
        for key, value in (('sourceCommit', 'b'*40), ('status', 'failed'), ('baseline', dict(sourceCommit='b'*40, version=baseline.VERSION)), ('observations', self.report['observations'][:3])):
            report = copy.deepcopy(self.report); report[key] = value
            with self.assertRaises(ValueError):
                decode_receipt(self.chunks(report), self.source)

    def test_corrupt_or_concatenated_stream_refuses(self):
        for packed in (b'corrupt', zlib.compress(b'{}')+zlib.compress(b'{}')):
            annotations = [dict(title='E01Z observations.json 0/1', message=base64.b64encode(packed).decode())]
            with self.assertRaises((ValueError, zlib.error)):
                decode_receipt(annotations, self.source)
