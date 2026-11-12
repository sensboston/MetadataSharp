"""Offline checks for the rule that the pinned tool prints every XMP property an edit wrote.

The tool itself is not run here: its print-outs are authored strings.
"""
import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "xmp_edits", Path(__file__).parents[2] / "tools" / "reference" / "xmp_edits.py")
x = importlib.util.module_from_spec(spec)
spec.loader.exec_module(x)

PRINTED = (
    "Exif.Image.Make                              Ascii      12  TestCompany\n"
    "Xmp.xmp.Rating                               XmpText     1  4\n"
    "Xmp.dc.subject                               XmpBag      2  harbour, dawn\n"
    "Xmp.xmpMM.History                            XmpText    10  type=\"Seq\"\n"
    "Xmp.xmpMM.History[1]/stEvt:action            XmpText     7  created\n"
    "Xmp.xmpNote.HasExtendedXMP                   XmpText    32  0123456789ABCDEF0123456789ABCDEF\n"
)
EXPECTED = [
    "Xmp.xmp.Rating XmpText 1 4",
    "Xmp.dc.subject XmpBag 2 harbour, dawn",
    "Xmp.xmpMM.History XmpText 10 type=\"Seq\"",
    "Xmp.xmpMM.History[1]/stEvt:action XmpText 7 created",
    "Xmp.xmpNote.HasExtendedXMP XmpText 32 *",
]
PACKET = (
    "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
    "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"\"/>"
    "</rdf:RDF></x:xmpmeta>   \n<?xpacket end=\"w\"?>\n"
)


class CompareTests(unittest.TestCase):
    def test_a_print_out_of_exactly_the_written_properties_passes(self):
        self.assertEqual([], x.compare(PRINTED, EXPECTED))

    def test_a_value_that_differs_is_a_problem(self):
        problems = x.compare(PRINTED.replace("harbour, dawn", "harbour, dusk"), EXPECTED)
        self.assertEqual(["not printed: Xmp.dc.subject XmpBag 2 harbour, dawn"], problems)

    def test_a_property_that_is_missing_is_a_problem(self):
        lines = [line for line in PRINTED.splitlines() if "Rating" not in line]
        self.assertEqual(["not printed: Xmp.xmp.Rating XmpText 1 4"], x.compare("\n".join(lines), EXPECTED))

    def test_a_property_nobody_wrote_is_a_problem(self):
        problems = x.compare(PRINTED + "Xmp.dc.format  XmpText  10  image/jpeg\n", EXPECTED)
        self.assertEqual(["printed, not written: Xmp.dc.format XmpText 10 image/jpeg"], problems)

    def test_any_value_still_needs_the_key_the_type_and_the_count(self):
        lines = [line for line in PRINTED.splitlines() if "xmpNote" not in line]
        self.assertEqual(["not printed: Xmp.xmpNote.HasExtendedXMP XmpText 32 *"], x.compare("\n".join(lines), EXPECTED))

    def test_other_families_are_not_looked_at(self):
        self.assertEqual([], x.compare(PRINTED + "Iptc.Application2.City  String  6  Boston\n", EXPECTED))


class PacketTests(unittest.TestCase):
    def test_a_packet_with_its_wrapper_passes(self):
        self.assertIsNone(x.packet_problem(PACKET))

    def test_a_broken_packet_is_a_problem(self):
        self.assertIn("not well-formed", x.packet_problem(PACKET.replace("</rdf:RDF>", "")))
        self.assertIn("not well-formed", x.packet_problem(""))

    def test_xml_without_rdf_is_a_problem(self):
        self.assertIn("no rdf:RDF", x.packet_problem("<a/>"))


class CheckTests(unittest.TestCase):
    def run_main(self, manifest, runner):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "manifest.tsv").write_text(manifest, encoding="utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                code = x.main([directory, "--exiv2", "exiv2"], runner=runner)
            return code, out.getvalue()

    def test_both_print_outs_are_asked_for_every_file(self):
        asked = []

        def runner(command, path):
            asked.append((command[1], path.name))
            return 0, PACKET if command[1] == "-pX" else PRINTED, ""

        code, out = self.run_main("a.jpg\t" + "|".join(EXPECTED) + "\n", runner)
        self.assertEqual(0, code, out)
        self.assertEqual([("-pa", "a.jpg"), ("-pX", "a.jpg")], asked)
        self.assertIn("1 files", out)

    def test_a_file_the_tool_does_not_read_fails(self):
        code, out = self.run_main("a.jpg\t" + "|".join(EXPECTED) + "\n", lambda command, path: (1, "", "no"))
        self.assertEqual(1, code)
        self.assertIn("-pa: not read (exit 1)", out)
        self.assertIn("-pX: not read (exit 1)", out)

    def test_a_problem_carries_the_print_out(self):
        def runner(command, path):
            return 0, PACKET if command[1] == "-pX" else PRINTED.replace("  4\n", "  5\n"), ""

        code, out = self.run_main("a.jpg\t" + "|".join(EXPECTED) + "\n", runner)
        self.assertEqual(1, code)
        self.assertIn("not printed: Xmp.xmp.Rating XmpText 1 4", out)
        self.assertIn("Xmp.xmp.Rating XmpText 1 5", out)

    def test_a_manifest_without_rows_is_refused(self):
        with self.assertRaises(ValueError):
            x.parse_manifest("\n")
        with self.assertRaises(ValueError):
            x.parse_manifest("only one column\n")


if __name__ == "__main__":
    unittest.main()
