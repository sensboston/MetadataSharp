# Tag Catalog and Charset Usage Limits

The public `MetadataTagCatalog` is a lookup surface for tag identities, not a parser or a complete
translation engine. Its bundled table is pinned to Exiv2 0.28.9 for Exif/TIFF group and numeric tag
identities and for XMP namespace/property names and prefix aliases. It uses a MetadataSharp-owned JSON
shape; it does not ship Exiv2 source code or explanatory prose. Labels and brief descriptions are
written locally from names, with a small set of hand-authored field summaries. The existing C#
interpretation tables are a separate surface and do not yet cover every value shown by Exiv2 `-pt` or
`-pv`. IPTC has its own dataset definitions; coded-value meanings are presently a selected subset.
The audit continues to mark complete descriptive and interpreted-value coverage as partial.

```csharp
using MetadataSharp.Core;

IReadOnlyList<string> groups = MetadataTagCatalog.ExifGroups;
IReadOnlyList<NumberedTagDefinition> imageTags = MetadataTagCatalog.Tags("Image");
if (MetadataTagCatalog.TryGet("Image", 0x0100, out NumberedTagDefinition? width))
    Console.WriteLine($"{width.Name}: {width.Description}");

if (MetadataTagCatalog.TryGetXmp("dc", "title", out XmpTagDefinition? title))
    Console.WriteLine($"{title!.NamespaceUri} {title.Name}");
```

`TryGet` is an exact group/ID lookup; XMP lookup uses a registered prefix alias and a local property
name. An absent entry is not proof that a tag is invalid: vendor extensions, custom XMP namespaces,
and tags unknown to the pinned registry can still occur in files. No metadata bytes are changed by
these lookups. The table does not reproduce upstream descriptions, and its local description text
must not be treated as a normative specification.

## Charset selection for display

IPTC packets retain `CharacterSet`, the literal interpretation of dataset 1:90, separately from
`DetectedTextEncoding`, the packet-wide display choice. In the current policy, a declared UTF-8 marker
selects strict UTF-8. Otherwise the packet is tested for ASCII and valid UTF-8; if neither is valid,
ISO-8859-1 supplies a reversible one-byte fallback. `GetText` and the convenience text properties use
that same packet-wide choice. A malformed value under a UTF-8 marker raises `InvalidIptc`; the raw
values remain available. Display fallback does not make an edit safe: IPTC edits preserve original
bytes and refuse relabeling retained high-bit values when their prior encoding cannot be established.
See [IPTC usage and ownership limits](IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md).

Exif `UserComment` has an explicit encoding prefix. The default interpretation honors its marker and
TIFF byte order. To interpret a `UNICODE`-prefixed body using an application-selected code page, pass
an encoding in `ExifInterpretationOptions`; this is a display-only `-n`-style override and does not
rewrite the `ExifValue` bytes:

```csharp
using System.Text;
using MetadataSharp.Core;

var options = new ExifInterpretationOptions { UserCommentEncoding = Encoding.Latin1 };
string? comment = ExifTags.Interpret(key, value, options);
```

Applications using legacy code pages should register the appropriate .NET encoding provider and
choose the `Encoding` explicitly. If the body is invalid under that encoding, interpretation returns
`null`; the original bytes remain accessible on the value. This option is not a global override for
IPTC or XMP, and it does not change the encoding used by metadata writers.

A comment body that holds no printable character is not interpreted at all: cameras also store binary
structures behind the character code, and only the raw value can describe those bytes. A body that
holds text with a line break or another control character of its own is still interpreted as text.

## Differential evidence

`tools/parity/run_tag_print.py` compares the ordered `MetadataSharp.Dump` tag records with the pinned
Exiv2 0.28.9 `-pt` output for each available upstream corpus file. Synthetic upstream reference names
without source media are individually listed with reasons. The exact `Exif.GPSInfo.GPSAltitudeRef`
translation is also listed as a tag-level exception because the local table follows the expanded Exif
3.0 wording while the pinned Exiv2 release predates that wording. `Exif.Canon.Categories` and
`Exif.Canon.PictureStylePC` are retained as exact local-only MakerNote keys because the pinned Exiv2
0.28.9 table comments them out and omits them from `-pt`. `Exif.Canon.FilterInfo` is another exact
key: MetadataSharp keeps the raw Canon MakerNote entry, while Exiv2 0.28.9 does not surface tag
`0x4024` in the affected `-pt` records; its raw value remains covered by the separate upstream gate.
Exact display rules for
`Exif.Photo.MeteringMode`, `Exif.Photo.ExposureProgram`, `Exif.Photo.SensitivityType`,
`Exif.Image.CalibrationIlluminant1`, and `Exif.Image.CalibrationIlluminant2` are tracked once per key
and applied only to the listed display pairs; any other value for those keys still fails. These rules
preserve the local vocabulary and raw-code policy rather than copying Exiv2 descriptions. As a print
format convention, trailing spaces and NUL padding are ignored only at the end of ASCII fields and
`Exif.Photo.UserComment`; internal whitespace and every non-text value remain exact. For ASCII and
`Exif.Photo.UserComment` text, the comparator also accepts a local value whose text continues after the
exact reference text with a line break, including a value that ends there; the reference stops the text
at the first line break of the value, so the complete local text is retained and no other prefix match
is accepted.
TIFF IFD fields (type 13) are also compared as `Long` when MetadataSharp represents the same
32-bit offset using its public `Long` value type; value and count equality are still exact. When Exiv2
prints a numeric fallback for a catalogued tag, the comparator may match it to the local tag name only when
the EXIF directory and numeric tag ID agree; values, types, and counts must still match. For a
remaining `-pt` value difference, the comparator accepts it as presentation-only only if the pinned
Exiv2 `-Pkycv` raw value exactly equals MetadataSharp's raw value for the same key, type, and count. The
tools' text remains distinct and is reported by key; no Exiv2 wording is copied into the local
catalog. Any difference in raw value or structure still fails. A vendor tag
rendered only as an unknown numeric key (for example, `Exif.Canon.0x0019`) is not a named catalog
entry; if the pinned reference does not print that key, the report lists it under
`raw_only_tag_exceptions` instead of counting its fallback raw value as a named-table mismatch. This does not suppress unknown named keys,
keys printed by both tools, raw-value gates, or the separate MakerNote-render parity gate.
The reference's print columns are read by their widths, so a stored value that begins with spaces and
a MakerNote tag name that contains a space are compared exactly. Structures the comparison used to step
around are read and compared row by row: the image directories a whole TIFF-based file names with tag
0x014A are listed as `Exif.SubImageN.*` (`ExifMetadata.SubImages`), a MakerNote directory that states
one tag ID twice lists both entries, and an entry of an unknown field type is read one byte per element
with the type code the file states. Named reader refusals and the
reference rows the comparison cannot answer are inventoried by exact corpus filename, key or group
prefix and reason in [`TAG_PRINT_REFUSAL_CATALOG.md`](TAG_PRINT_REFUSAL_CATALOG.md); they are reported
apart from the matched rows, and any changed outcome fails. The gate is fail-closed for unlisted missing inputs and
other output mismatches. It is not a claim that this table
or the library currently achieves full parity; the report is the measure of remaining work. The tool also checks that both the source checkout
and binary identify as the pin before comparing output.
