"""Offline authored EPS check; never execute the PostScript body."""
import hashlib
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b8_eps_fixture as fixture


class EpsFixtureTests(unittest.TestCase):
    def test_authored_hash_and_dsc_header(self):
        fixture.verify()
        data = (fixture.DIRECTORY / fixture.FILE).read_bytes()
        self.assertEqual('17a5075bac1eaa58e7d37225db451e23e8ef5c13672eb8f24e7fef3d66a270ba', hashlib.sha256(data).hexdigest())
        lines = data.splitlines()
        self.assertEqual(b'%!PS-Adobe-3.0 EPSF-3.0', lines[0])
        self.assertEqual(b'%%BoundingBox: 0 0 10 12', lines[1])
        self.assertIn(b'%%EndComments', lines)
        self.assertEqual(b'%%EOF', lines[-1])


if __name__ == '__main__':
    unittest.main()
