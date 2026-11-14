"""Offline rules of the key catalog check: how the reference's tables are read and what is a gap.

No Exiv2 checkout and no external tool is used: the snippets below are the shapes the reader has to
tell apart - a table of a class, a table of a plain function, the end-of-table marker, an entry that
is commented out, and a table that ends without its own line break.
"""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "parity"))

import run_key_catalog  # noqa: E402

TAGS_INT = """\
namespace Exiv2 {
constexpr GroupInfo groupInfo[] = {
    {IfdId::ifdIdNotSet, "Unknown IFD", "Unknown", nullptr},
    {IfdId::ifd0Id, "IFD0", "Image", ifdTagList},
    {IfdId::canonId, "Makernote", "Canon", CanonMakerNote::tagList},
    {IfdId::canonCsId, "Makernote", "CanonCs", CanonMakerNote::tagListCs},
    {IfdId::minoltaId, "Makernote", "Minolta", MinoltaMakerNote::tagList},
    {IfdId::samsung2Id, "Makernote", "Samsung2", Samsung2MakerNote::tagList},
};
const TagInfo* ifdTagList() {
  return ifdTagInfo;
}
}  // namespace Exiv2
"""

IFD = """\
constexpr TagInfo ifdTagInfo[] = {
    {0x0100, "ImageWidth", N_("Image Width"), N_("Width"), IfdId::ifd0Id, SectionId::imageTags,
     unsignedShort, -1, printValue},
    // {0x0023, "Categories", N_("Categories"), N_("Categories"), IfdId::ifd0Id, SectionId::imageTags,
    //  unsignedLong, -1, printValue},
    {0xffff, "(UnknownIfdTag)", "(UnknownIfdTag)", "(UnknownIfdTag)", IfdId::ifd0Id,
     SectionId::imageTags, unsignedByte, -1, printValue}};
"""

CANON = """\
const TagInfo* CanonMakerNote::tagList() {
  return tagInfo_;
}

const TagInfo* CanonMakerNote::tagListCs() {
  return tagInfoCs_;
}

constexpr TagInfo CanonMakerNote::tagInfo_[] = {
    {0x0001, "OwnerName", N_("Owner Name"), N_("Name"), IfdId::canonId, SectionId::makerTags,
     asciiString, -1, printValue},
    {0xffff, "(UnknownCanonMakerNoteTag)", "(UnknownCanonMakerNoteTag)", "(UnknownCanonMakerNoteTag)",
     IfdId::canonId, SectionId::makerTags, unsignedByte, -1, printValue}};

constexpr TagInfo CanonMakerNote::tagInfoCs_[] = {
    {1, "Macro", N_("Macro"), N_("Macro mode"), IfdId::canonCsId, SectionId::makerTags, unsignedShort,
     -1, EXV_PRINT_TAG(canonCsMacro)},
    {0xffff, "(UnknownCanonCsTag)", "(UnknownCanonCsTag)", "(UnknownCanonCsTag)", IfdId::canonCsId,
     SectionId::makerTags, unsignedByte, -1, printValue}};
"""

MINOLTA = """\
const TagInfo* MinoltaMakerNote::tagList() {
  return tagInfo_;
}

constexpr TagInfo MinoltaMakerNote::tagInfo_[] = {
    {0x0001, "CameraSettings", N_("Camera Settings"), N_("Settings"), IfdId::minoltaId,
     SectionId::makerTags, undefined, -1, printValue},
    /* {0x0002, "CommentedOut", N_("Out"), N_("Out"), IfdId::minoltaId, SectionId::makerTags,
       undefined, -1, printValue}, */
    {0xffff, "(UnknownMinoltaMakerNoteTag)", "(UnknownMinoltaMakerNoteTag)",
     "(UnknownMinoltaMakerNoteTag)", IfdId::minoltaId, SectionId::makerTags, unsignedByte, -1,
     printValue}};
"""


class ReferenceKeysTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        (self.root / "src").mkdir()
        (self.root / "src" / "tags_int.cpp").write_text(TAGS_INT, encoding="utf-8")
        (self.root / "src" / "tags_int.cpp").write_text(TAGS_INT + IFD, encoding="utf-8")
        (self.root / "src" / "canonmn_int.cpp").write_text(CANON, encoding="utf-8")
        (self.root / "src" / "minoltamn_int.cpp").write_text(MINOLTA, encoding="utf-8")

    def tearDown(self):
        self.temporary.cleanup()

    def test_every_group_that_names_a_table_contributes_its_entries(self):
        keys, unresolved = run_key_catalog.reference_keys(self.root)
        self.assertFalse([line for line in unresolved if not line.startswith("Samsung2:")])
        self.assertEqual({"Exif.Image.ImageWidth", "Exif.Canon.OwnerName", "Exif.CanonCs.Macro",
                          "Exif.Minolta.CameraSettings"}, set(keys))

    def test_end_of_table_markers_and_commented_out_entries_are_not_keys(self):
        keys, _ = run_key_catalog.reference_keys(self.root)
        self.assertFalse([key for key in keys if "(Unknown" in key])
        self.assertNotIn("Exif.Image.Categories", keys)
        self.assertNotIn("Exif.Minolta.CommentedOut", keys)

    def test_numbers_are_kept_as_the_table_states_them(self):
        keys, _ = run_key_catalog.reference_keys(self.root)
        self.assertEqual("0x0001", keys["Exif.Canon.OwnerName"][0])
        self.assertEqual("1", keys["Exif.CanonCs.Macro"][0])

    def test_a_group_whose_function_is_missing_is_reported_rather_than_skipped(self):
        # Samsung2MakerNote::tagList is named by groupInfo and defined nowhere in the snippets.
        _, unresolved = run_key_catalog.reference_keys(self.root)
        self.assertEqual(["Samsung2: Samsung2MakerNote::tagList returns no table this script could find"],
                         unresolved)


class CatalogTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.catalog = Path(self.temporary.name) / "catalog.json"
        self.catalog.write_text(json.dumps({"exifGroups": [
            {"name": "Image", "tags": [{"id": 256, "names": ["ImageWidth"]}]},
            {"name": "Canon", "tags": [{"id": 9, "names": ["OwnerName"]}]},
            {"name": "CanonCs", "tags": [{"id": 1, "names": ["Macro", "(UnknownCanonCsTag)"]}]},
            {"name": "Canon", "tags": [{"id": 35, "names": ["Categories"]}]},
        ]}), encoding="utf-8")

    def tearDown(self):
        self.temporary.cleanup()

    def test_placeholders_are_not_catalog_keys(self):
        keys = run_key_catalog.catalog_keys(self.catalog)
        self.assertNotIn("Exif.CanonCs.(UnknownCanonCsTag)", keys)
        self.assertIn("Exif.Canon.Categories", keys)

    def test_a_key_the_reference_has_and_the_catalog_lacks_fails_the_run(self):
        source = Path(self.temporary.name) / "source"
        (source / "src").mkdir(parents=True)
        (source / "src" / "tags_int.cpp").write_text(TAGS_INT + IFD, encoding="utf-8")
        (source / "src" / "canonmn_int.cpp").write_text(CANON, encoding="utf-8")
        (source / "src" / "minoltamn_int.cpp").write_text(MINOLTA, encoding="utf-8")
        run_key_catalog.MINIMUM_KEYS = 1
        try:
            self.assertEqual(1, run_key_catalog.main([str(source), "--catalog", str(self.catalog)]))
        finally:
            run_key_catalog.MINIMUM_KEYS = 6000

    def test_a_reference_that_reads_nothing_is_not_a_pass(self):
        source = Path(self.temporary.name) / "empty"
        (source / "src").mkdir(parents=True)
        (source / "src" / "tags_int.cpp").write_text(TAGS_INT + IFD, encoding="utf-8")
        (source / "src" / "canonmn_int.cpp").write_text(CANON, encoding="utf-8")
        (source / "src" / "minoltamn_int.cpp").write_text(MINOLTA, encoding="utf-8")
        run_key_catalog.MINIMUM_KEYS = 1000
        try:
            self.assertEqual(1, run_key_catalog.main([str(source), "--catalog", str(self.catalog)]))
        finally:
            run_key_catalog.MINIMUM_KEYS = 6000


if __name__ == "__main__":
    unittest.main()
