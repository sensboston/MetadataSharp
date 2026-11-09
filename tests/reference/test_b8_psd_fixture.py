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

    def test_mutation_is_rejected_even_with_updated_manifest(self):
        with tempfile.TemporaryDirectory() as temp, patch.object(fixture, 'DIRECTORY', Path(temp)):
            mutated = bytearray(fixture.encode())
            mutated[-1] ^= 1
            (Path(temp) / fixture.NAME).write_bytes(mutated)
            (Path(temp) / 'manifest.json').write_text(json.dumps([fixture.entry(mutated)]))
            with self.assertRaises(ValueError):
                fixture.verify()

    def test_truncated_manifest_or_fixture_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp, patch.object(fixture, 'DIRECTORY', Path(temp)):
            (Path(temp) / fixture.NAME).write_bytes(fixture.encode()[:-1])
            (Path(temp) / 'manifest.json').write_text('[]')
            with self.assertRaises(ValueError):
                fixture.verify()


if __name__ == '__main__':
    unittest.main()
