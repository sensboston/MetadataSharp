"""Independently inspect the narrow PSD fixture's bytes; no PSD parser claim."""
import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import b8_psd_fixture as fixture


class B8PsdFixtureTests(unittest.TestCase):
    def test_pinned_hash_and_explicit_header(self):
        entry = fixture.verify()[0]
        data = (fixture.DIRECTORY / entry['file']).read_bytes()
        self.assertEqual(52, len(data))
        self.assertEqual('8903393ec12d1236063cc994add7a87514ddbad49689db5b5501a249933c4c10', hashlib.sha256(data).hexdigest())
        self.assertEqual((b'8BPS', 1, b'\x00'*6, 3, 2, 2, 8, 3),
                         struct.unpack('>4sH6sHIIHH', data[:26]))
        self.assertEqual((0, 0, 0), struct.unpack('>III', data[26:38]))
        self.assertEqual(0, int.from_bytes(data[38:40], 'big'))
        self.assertEqual(bytes([13, 101, 241, 19, 47, 151, 37, 223, 91, 203, 79, 127]), data[40:])
        self.assertEqual('none', entry['metadata'])

    def test_owned_metadata_resource_structure_and_pixels(self):
        import xml.etree.ElementTree as ET
        data = (fixture.DIRECTORY / fixture.RICH_NAME).read_bytes()
        self.assertEqual('fee30fbbe825123fe4fdbe5278c99329cabfa4dfc949a68768e5777526acb493', hashlib.sha256(data).hexdigest())
        self.assertEqual(0, int.from_bytes(data[26:30], 'big'))
        size = int.from_bytes(data[30:34], 'big')
        end, pos, resources = 34 + size, 34, {}
        while pos < end:
            self.assertEqual(b'8BIM', data[pos:pos+4])
            ident = int.from_bytes(data[pos+4:pos+6], 'big')
            self.assertNotIn(ident, resources)
            self.assertEqual(b'\x00\x00', data[pos+6:pos+8])
            length = int.from_bytes(data[pos+8:pos+12], 'big')
            start = pos + 12
            self.assertLessEqual(start + length + length % 2, end)
            resources[ident] = data[start:start+length]
            if length % 2:
                self.assertEqual(0, data[start+length])
            pos = start + length + length % 2
        self.assertEqual(end, pos)
        self.assertEqual({1028, 1058, 1060}, set(resources))
        self.assertEqual(b'II', resources[1058][:2])
        self.assertEqual((42, 8, 1), struct.unpack_from('<HIH', resources[1058], 2))
        self.assertEqual((315, 2, 9, 26), struct.unpack_from('<HHII', resources[1058], 10))
        self.assertEqual(b'B8Artist\x00', resources[1058][26:])
        root = ET.fromstring(resources[1060])
        desc = root.find('.//{http://www.w3.org/1999/02/22-rdf-syntax-ns#}Description')
        self.assertEqual('B8Tool', desc.attrib['{http://ns.adobe.com/xap/1.0/}CreatorTool'])
        self.assertEqual(b'\x1c\x02\x19\x00\x09B8Keyword', resources[1028])
        self.assertEqual(bytes(4), data[end:end+4])  # no layer/mask section
        self.assertEqual(bytes(2), data[end+4:end+6])  # raw planar compression
        self.assertEqual(bytes([13, 101, 241, 19, 47, 151, 37, 223, 91, 203, 79, 127]), data[end+6:])

    def test_mutation_is_rejected_even_with_updated_manifest(self):
        with tempfile.TemporaryDirectory() as temp, patch.object(fixture, 'DIRECTORY', Path(temp)):
            mutated = bytearray(fixture.encode())
            mutated[-1] ^= 1
            (Path(temp) / fixture.NAME).write_bytes(mutated)
            (Path(temp) / fixture.RICH_NAME).write_bytes(fixture.encode_rich())
            (Path(temp) / 'manifest.json').write_text(json.dumps([fixture.entry(mutated)]))
            with self.assertRaises(ValueError):
                fixture.verify()

    def test_truncated_manifest_or_fixture_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp, patch.object(fixture, 'DIRECTORY', Path(temp)):
            (Path(temp) / fixture.NAME).write_bytes(fixture.encode()[:-1])
            (Path(temp) / fixture.RICH_NAME).write_bytes(fixture.encode_rich())
            (Path(temp) / 'manifest.json').write_text('[]')
            with self.assertRaises(ValueError):
                fixture.verify()


if __name__ == '__main__':
    unittest.main()
