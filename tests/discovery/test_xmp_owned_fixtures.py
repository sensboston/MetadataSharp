import hashlib
import importlib.util
import json
import sys
import tempfile
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
spec = importlib.util.spec_from_file_location('owned_xmp_fixtures', ROOT / 'tools/reference/xmp_owned_fixtures.py')
owned = importlib.util.module_from_spec(spec); spec.loader.exec_module(owned)

MANIFEST = Path(ROOT / 'tests/fixtures/xmp/manifest.json')


class OwnedXmpFixtureTests(unittest.TestCase):
    def setUp(self):
        self.pinned = {e['file']: e for e in json.loads(MANIFEST.read_text())}

    def test_manifest_pins_every_fixture(self):
        self.assertEqual(6, len(self.pinned))
        for name, entry in self.pinned.items():
            self.assertEqual('owned-synthetic', entry['rights'])
            self.assertRegex(entry['sha256'], r'^[0-9a-f]{64}$')

    def test_regeneration_is_byte_deterministic(self):
        with tempfile.TemporaryDirectory() as tmp:
            owned.generate(tmp)
            for name, entry in self.pinned.items():
                data = (Path(tmp) / name).read_bytes()
                self.assertEqual(entry['sha256'], hashlib.sha256(data).hexdigest())
                self.assertEqual(entry['length'], len(data))

    def test_wrapped_and_unwrapped_layouts_parse_in_the_authored_profile(self):
        for name in ('xmp-base.xmp', 'xmp-no-props.xmp', 'xmp-unwrapped.xmp', 'xmp-unwrapped-no-props.xmp'):
            self.assertIsNone(owned.strict_parse_xmp(owned.fixture_bytes(name)), name)
        self.assertIn(b'<x:xmpmeta', owned.fixture_bytes('xmp-base.xmp'))
        self.assertNotIn(b'<x:xmpmeta', owned.fixture_bytes('xmp-unwrapped.xmp'))
        self.assertIn(b'<rdf:RDF', owned.fixture_bytes('xmp-unwrapped.xmp'))

    def test_nonutf8_variants_are_rejected_by_the_profile_check(self):
        for name in ('xmp-nonutf8.xmp', 'xmp-unwrapped-nonutf8.xmp'):
            self.assertEqual('not utf-8', owned.strict_parse_xmp(owned.fixture_bytes(name)), name)

    def test_leading_raw_bom_is_rejected(self):
        data = owned.fixture_bytes('xmp-base.xmp')
        self.assertEqual('leading raw BOM outside the xpacket PI (non-goal of this batch)',
                         owned.strict_parse_xmp(b'\xef\xbb\xbf' + data))

    def test_base_packet_carries_the_single_simple_property(self):
        for name in ('xmp-base.xmp', 'xmp-unwrapped.xmp'):
            self.assertIn(b'<xmp:CreatorTool>MetadataSharp</xmp:CreatorTool>',
                          owned.fixture_bytes(name), name)

    def test_empty_description_variants_have_no_properties(self):
        for name in ('xmp-no-props.xmp', 'xmp-unwrapped-no-props.xmp'):
            data = owned.fixture_bytes(name)
            self.assertNotIn(b'CreatorTool', data, name)
            self.assertIsNone(owned.strict_parse_xmp(data), name)

    def test_provenance_matches_manifest(self):
        provenance = json.loads((ROOT / 'tests/fixtures/xmp/provenance.json').read_text())
        self.assertEqual({e['file'] for e in self.pinned.values()},
                         {e['file'] for e in provenance['fixtures']})


if __name__ == '__main__':
    unittest.main()
