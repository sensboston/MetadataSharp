"""Offline tests for the B8 census fixture catalog and its byte-structure oracle.

No external tool is executed here: the catalog must verify with the standard
library alone so CI and a developer laptop reach the same conclusion.
"""
import json
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b8_census_fixtures as catalog


class CatalogTests(unittest.TestCase):
    def test_manifest_matches_bytes_and_pins_recipes(self):
        manifest = catalog.verify()
        self.assertEqual(sorted(catalog.FIXTURES), sorted(e['layout'] for e in manifest))
        for entry in manifest:
            data = (catalog.DIRECTORY / entry['file']).read_bytes()
            self.assertEqual(len(data), entry['length'])
            self.assertEqual(catalog.digest(data), entry['sha256'])
            self.assertEqual('owned-synthetic', entry['rights'])
            self.assertIn('tools/reference/', entry['recipe'])
            self.assertIn(entry['metadata'], ('none', 'authored-exif-xmp-iptc'))

    def test_only_cleared_formats_are_cataloged(self):
        self.assertEqual({'PSD', 'JP2', 'EPS'}, {fmt for fmt, _ in catalog.FIXTURES.values()})
        # PGF/EXV/DCP have no cleared fixture, so the catalog cannot invent one.
        for pending in ('pgf-unobserved', 'exv-unobserved', 'dcp-unobserved'):
            self.assertNotIn(pending, catalog.FIXTURES)

    def test_psd_sections_are_accounted_and_resources_enumerated(self):
        flat = (catalog.DIRECTORY / 'owned-flat-rgb.psd').read_bytes()
        rich = (catalog.DIRECTORY / 'owned-metadata-rgb.psd').read_bytes()
        flat_facts = catalog.structure('PSD', flat)
        rich_facts = catalog.structure('PSD', rich)
        self.assertEqual([], flat_facts['resources'])
        self.assertEqual(0, flat_facts['resourceLength'])
        self.assertEqual([1028, 1058, 1060], sorted(r['identifier'] for r in rich_facts['resources']))
        for resource in rich_facts['resources']:
            self.assertEqual(64, len(resource['payloadSha256']))
        # Same authored pixels in both layouts, so pixel bytes must agree exactly.
        self.assertEqual(flat_facts['pixelBytesSha256'], rich_facts['pixelBytesSha256'])
        self.assertEqual(12, rich_facts['pixelBytesLength'])
        self.assertEqual(358, len(rich))

    def test_psd_parser_fails_closed_on_truncated_or_foreign_bytes(self):
        with self.assertRaises(ValueError):
            catalog.structure('PSD', b'8BPS' + bytes(20))
        with self.assertRaises(ValueError):
            catalog.structure('PSD', b'PNG' + bytes(60))
        rich = (catalog.DIRECTORY / 'owned-metadata-rgb.psd').read_bytes()
        with self.assertRaises(ValueError):
            catalog.structure('PSD', rich[:len(rich) - 1])
        # A resource payload that overruns the declared section is refused.
        broken = bytearray(rich)
        struct.pack_into('>I', broken, 30, 4000)
        with self.assertRaises(ValueError):
            catalog.structure('PSD', bytes(broken))

    def test_jp2_box_walk_consumes_the_file_exactly(self):
        data = (catalog.DIRECTORY / 'owned-rgb.jp2').read_bytes()
        facts = catalog.structure('JP2', data)
        self.assertEqual(0, facts['boxes'][0]['offset'])
        self.assertEqual(len(data), sum(b['length'] for b in facts['boxes']))
        self.assertEqual(['jP  ', 'ftyp', 'jp2h', 'jp2c'], facts['boxTypes'])
        self.assertFalse(facts['hasXmlBox'], 'the cleared JP2 fixture carries no XMP box')
        self.assertFalse(facts['hasEndBox'])
        self.assertEqual(64, len(facts['codestreamSha256']))
        broken = data + b'\x00\x00\x00\x04junk'
        with self.assertRaises(ValueError):
            catalog.structure('JP2', broken)

    def test_eps_scan_records_framing_without_execution(self):
        data = (catalog.DIRECTORY / 'owned-vector.eps').read_bytes()
        facts = catalog.structure('EPS', data)
        self.assertFalse(facts['postScriptExecuted'])
        self.assertEqual([0, 0, 10, 12], facts['boundingBox'])
        self.assertIn('BoundingBox', facts['dscComments'])
        self.assertIn('EndComments', facts['dscComments'])
        self.assertEqual(len(data), facts['fileLength'])

    def test_eps_scan_refuses_binary_and_missing_markers(self):
        with self.assertRaises(ValueError):
            catalog.structure('EPS', b'%!PS-Adobe-3.0 EPSF-3.0\n\x00\n')
        with self.assertRaises(ValueError):
            catalog.structure('EPS', b'%!PS-Adobe-3.0 EPSF-3.0\n%%EndComments\n%%EOF\n')
        with self.assertRaises(ValueError):
            catalog.structure('EPS', b'%!PS-Adobe-3.0 EPSF-3.0\n'
                                  b'%%BoundingBox: 0 0 ten twelve\n%%EndComments\n%%EOF\n')

    def test_unknown_format_has_no_silent_oracle(self):
        with self.assertRaises(ValueError):
            catalog.structure('CR3', b'')


if __name__ == '__main__':
    unittest.main()
