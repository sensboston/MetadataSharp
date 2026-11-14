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

## Character sets

Three carriers hold text whose character set the file itself does not state: an Exif `UserComment`,
the five Windows XP tags of the primary directory, and the text of an IPTC packet. Each is read and
written in one place - `MetadataText` for the first two, `IptcPacket.GetText` for the third - and
every character set they accept is named by `MetadataCharsets`.

### The names and the code pages behind them

`MetadataCharsets` is the list of character-set names Exiv2's `-n` option accepts, resolved to
encodings the platform itself carries. `System.Text.CodePagesEncodingProvider` is registered once,
from the .NET runtime: the library has no package dependency for this, `ProjectInfrastructureTests`
asserts that the source projects declare no `PackageReference` at all, and the matrix test resolves
every entry through the provider.

```csharp
using MetadataSharp.Core;

MetadataCharset cyrillic = MetadataCharsets.Get("windows-1251"); // "CP1251" and "MS-CYRL" name it too
string text = cyrillic.Encoding.GetString(bytes);                // strict: an unreadable byte raises
```

The matrix holds 38 entries: `US-ASCII`, `UTF-8`, `UTF-16LE` (with the `UCS-2LE`, `UCS-2`,
`unicode` and `unicodeLittle` aliases), `UTF-16BE`, `ISO-8859-1`, `-2`, `-3`, `-4`, `-5`, `-6`, `-7`,
`-8`, `-9`, `-13` and `-15`, `windows-1250` to `windows-1258`, `windows-874`, `CP437`, `CP850`,
`CP852`, `CP866`, `KOI8-R`, `KOI8-U`, `macintosh`, `Shift_JIS`, `EUC-JP`, `ISO-2022-JP`, `GB2312`,
`Big5` and `EUC-KR`, each with the aliases Exiv2 and the platform use for it (`latin1`, `l1`,
`TIS-620`, `SJIS`, `GBK`, `UHC`, ...). `MetadataCharsets.Names`, `.All`, `.TryGet`, `.Get` and
`.TryGetName` are the surface; a lookup ignores case, hyphens, underscores and spaces, and a name
outside the matrix is refused with an `ArgumentException` that lists the names the matrix holds
rather than being guessed at or silently mapped to a nearby code page. `MetadataCharsets.All` is
ordered, and the differential fixture numbers its files from it, so the order is part of the test
surface and not incidental.

### A `UserComment`

`Exif.Photo.UserComment` carries an eight-byte character-set prefix. The reader honours the three
the standard defines - `ASCII`, `UNICODE` and eight zero bytes - and the byte-order mark of a
`UNICODE` body; the writer produces them:

```csharp
using MetadataSharp.Core;

ExifValue comment = MetadataText.UserComment("Grüße aus Köln", UserCommentCharset.Unicode, Endianness.BigEndian);
string? text = MetadataText.UserComment(comment);                            // UCS-2 in the file's byte order
string? other = MetadataText.UserComment(comment, "windows-1251");           // the body is in that code page
```

`MetadataText.UserComment(text, charset, byteOrder)` writes UTF-16 in the byte order of the file
without a byte-order mark for `Unicode`, seven-bit ASCII for `Ascii`, and UTF-8 for `Undefined`. A
`JIS` body - the fourth prefix Exiv2 knows - is deliberately not written and is not decoded as
text: the reference prints such a body's bytes without converting them, so a text produced here
would not be what that tool shows. Those bytes stay available as the raw value, the same way a body
with an unknown prefix does.

Naming a character set for the reader says what the bytes of a `UNICODE` body are written in. That
is exactly what Exiv2's `-n` option states, and in the reference it belongs to one action: `fc`
(`fixcom`) reads the comment in that set, converts it to UCS-2 and writes it back
(`app/actions.cpp`, `FixCom::run`). Printing does not consult `-n` at all - `CommentValue::write`
sniffs a byte-order mark and otherwise assumes the byte order of the file (`src/value.cpp`,
`detectCharset`) - and that sniffing is what this library does when no name is passed. Naming a
character set is a read-side statement: it changes no byte, and nothing this library writes depends
on it.

A comment body that holds no printable character is not interpreted at all: cameras also store
binary structures behind the character code, and only the raw value can describe those bytes. A
body that holds text with a line break or another control character of its own is still interpreted
as text.

### The XP tags

`Exif.Image.XPTitle`, `XPComment`, `XPAuthor`, `XPKeywords` and `XPSubject` are UCS-2
**little-endian** whatever the byte order of the file, NUL-terminated, and are not affected by `-n`.
`MetadataText.IsXpTag` recognises them by directory and tag ID; `ExifTags.Interpret` answers their
text, and `MetadataText.Xp` writes one. The reader drops a trailing odd byte and the trailing
UCS-2 NULs, and returns `null` rather than replacement characters when the bytes are not UCS-2.

### An IPTC packet

IIM records have one character-set designation, the UTF-8 marker of dataset 1:90, and a packet that
carries none says nothing about the code page of its text. `IptcPacket.DetectedTextEncoding` is the
packet-wide display choice: a declared UTF-8 marker selects strict UTF-8, otherwise the packet is
tested for ASCII and valid UTF-8, and ISO-8859-1 supplies a reversible one-byte fallback.
`GetText(index)` uses that choice. `GetText(index, charset)` is for a caller who knows the code page -
from the camera, the agency, or the country of the photographer - and raises
`DecoderFallbackException` instead of replacing a byte it cannot read:

```csharp
using MetadataSharp.Core.Iptc;

IptcPacket packet = IptcPacket.Parse(bytes);
string declared = packet.GetText(0);                       // ASCII, UTF-8 or ISO-8859-1, by detection
string cyrillic = packet.GetText(0, "windows-1251");        // strict, in the named character set
```

The reference has no such option for IPTC text: its `-n` names the character set of a `UNICODE`
user comment for the `fc` action, and nothing else. `GetText(index, charset)` is therefore an
addition to what that tool offers, not a comparison with it.

Neither call changes a byte. Display fallback does not make an edit safe: IPTC edits preserve
original bytes and refuse relabeling retained high-bit values when their prior encoding cannot be
established. See [IPTC usage and ownership limits](IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md).

### What is not converted

Naming a character set affects the reading of these two carriers only. XMP is UTF-8 by its own
specification, an Exif ASCII field is not converted, and a value of any other tag keeps the bytes the
file states. No writer converts an existing value into another character set: an edit either sets a
value this library produced or leaves the bytes as they were, which is why an unknown character set
is refused rather than round-tripped through a guess. (The reference's `fc` action *does* rewrite a
comment; `MetadataOperations.FixUserComment` is the equivalent here and takes the encoding the text
is in.)

## Differential evidence

`tools/reference/run_charset.py` writes one JPEG for every entry of the matrix - a `UNICODE`
`UserComment` whose body is the text in that code page, and an `XPTitle` in text that stresses it -
and asks the pinned Exiv2 to make the same statement the name makes: `exiv2 -v -n <name> fc` on a
copy converts the comment from that code page, and the text the tool says it converted
(`Setting Exif UNICODE user comment to "..."`) has to be the text this library reads. The copy is
thrown away: 0.28.9 writes the converted value in big-endian UCS-2 whatever the file's order is
(`CommentValue::read`, `src/value.cpp`), so its own print of the result is not what is compared. The title is compared through the tool's translated print (`-PEkt`), where
its `printUcs2` runs. A comment or title the tool reads differently fails the job, and so does a text
this library cannot read back with the character set it was written in. One rule is recorded by the
gate rather than by an exception list: for `windows-1258` the tool's text is this library's without
its final character, because glibc's converter for that code page holds the last character until the
conversion is flushed and `convertStringCharsetIconv` stops as soon as the input is consumed. The
rule is by name and by exact shape - the rest of the text must still be equal, and every other
character set of the matrix hands over everything without a flush, which the same run shows. The conversion is iconv, an
optional dependency of the reference: a build without it says so in the gate's notice instead of
passing quietly, and the round trips are still failed. The matrix itself, its aliases, its refusals
and the writers are covered offline by `MetadataTextTests`.

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
