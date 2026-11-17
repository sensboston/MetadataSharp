using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// A structural failure in the tag-print oracle or dump protocol.
internal sealed class TagPrintError(string message) : Exception(message);

// One reviewed refusal: the outcome observed for an exact pinned input, which is required to stay the same.
internal sealed record TagPrintRefusal(string Kind, object Status, string? Detail, string Reason);

// Fail-closed differential gate for the Exiv2 -pt tag listing (formerly tools/parity/run_tag_print.py).
internal static class TagPrint
{
    public const string Title = "PARITY tag print";
    public const string PinnedExiv2Commit = "b0859fc61fea5dedfc898aece6b15893ddf4314e";

    private static readonly Lazy<Dictionary<string, long>> CatalogIds = new(LoadCatalogKeyIds);

    /// <summary>Every EXIF key of the local catalog with its tag number.</summary>
    public static Dictionary<string, long> CatalogKeyIds => CatalogIds.Value;

    /// <summary>The EXIF keys of the local catalog.</summary>
    public static IReadOnlyCollection<string> CatalogKeys => CatalogIds.Value.Keys;

    private static readonly Regex NumericKey = new(@"^(Exif\..+)\.0[xX]([0-9A-Fa-f]+)\z", RegexOptions.CultureInvariant);
    private static readonly Regex RawNumericTag = new(@"\.0[xX][0-9A-Fa-f]+$", RegexOptions.CultureInvariant);
    private static readonly Regex DigitRun = new(@"^\d+(?:" + PyRe.Space + @"+\d+)+\z", RegexOptions.CultureInvariant);

    // Exact EXIF keys omitted from the tag-table comparison for a reviewed, key-specific compatibility
    // reason. Entries remain visible in the report and are not general value/description wildcards.
    public static readonly OrderedDictionary<string, string> KeyExceptions = new()
    {
        ["Exif.GPSInfo.GPSAltitudeRef"] = "MetadataSharp follows the expanded Exif 3.0 ellipsoidal/sea-level definitions, while the pinned Exiv2 0.28.9 print table exposes only its earlier above/below sea-level labels. The raw field and numeric code remain covered by the other tag/value gates; this exact key's translated wording differs by revision.",
        ["Exif.Canon.PictureStylePC"] = "The pinned Exiv2 0.28.9 Canon tag table leaves this PC-picture-style entry commented out and does not include it in -pt; MetadataSharp retains its local MakerNote decoder. This exact local-only tag is excluded from the Exiv2 tag-table comparison.",
        ["Exif.Canon.Categories"] = "The pinned Exiv2 0.28.9 Canon tag table leaves this Categories entry commented out and does not include it in -pt; MetadataSharp retains its local MakerNote decoder. This exact local-only tag is excluded from the Exiv2 tag-table comparison.",
        ["Exif.Canon.FilterInfo"] = "MetadataSharp retains the raw Canon FilterInfo MakerNote entry, while Exiv2 0.28.9 does not surface tag 0x4024 in these -pt records. Its exact raw key remains covered by the separate upstream parity gate.",
    };

    // Exact display aliases, not descriptions copied from Exiv2. These pairs represent the same assigned
    // values in the local Exif vocabulary and Exiv2 0.28.9's shorter/older print terminology. Any other
    // value for these keys still fails comparison.
    public static readonly OrderedDictionary<string, OrderedDictionary<string, string>> DisplayAliases = new()
    {
        ["Exif.Photo.MeteringMode"] = new() { ["Multi-segment"] = "Pattern" },
        ["Exif.Photo.ExposureProgram"] = new() { ["Auto"] = "Normal program" },
        ["Exif.Image.CalibrationIlluminant1"] = new() { ["Standard light A"] = "17" },
        ["Exif.Image.CalibrationIlluminant2"] = new() { ["D65"] = "21" },
        ["Exif.Photo.SensitivityType"] = new() { ["0"] = "Unknown", ["1"] = "Standard output sensitivity (SOS)", ["2"] = "Recommended exposure index (REI)", ["3"] = "ISO speed", ["4"] = "Standard output sensitivity (SOS) and recommended exposure index (REI)", ["5"] = "Standard output sensitivity (SOS) and ISO speed", ["6"] = "Recommended exposure index (REI) and ISO speed", ["7"] = "Standard output sensitivity (SOS) and recommended exposure index (REI) and ISO speed" },
    };

    public static readonly OrderedDictionary<string, string> DisplayReasons = new()
    {
        ["Exif.Image.CalibrationIlluminant1"] = "The DNG field's local catalog entry preserves its numeric code; Exiv2 renders code 17 with the standard LightSource label. This exact code-to-label pair is accepted without extending the other-source interpretation table.",
        ["Exif.Image.CalibrationIlluminant2"] = "The DNG field's local catalog entry preserves its numeric code; Exiv2 renders code 21 with the standard LightSource label. This exact code-to-label pair is accepted without extending the other-source interpretation table.",
        ["Exif.Photo.MeteringMode"] = "The same EXIF metering code is called Pattern by the local vocabulary and Multi-segment by Exiv2; these terms describe the same assigned mode.",
        ["Exif.Photo.ExposureProgram"] = "The same EXIF exposure-program code is called Normal program by the local vocabulary and Auto by Exiv2; this is a terminology-only difference.",
        ["Exif.Photo.SensitivityType"] = "MetadataSharp renders the assigned EXIF meaning; Exiv2 0.28.9 prints the stored numeric code for this tag. Exact code-to-label pairs are listed and checked, not generalized to arbitrary values.",
    };

    public const string SidecarConversionRule = "Exiv2's XMP sidecar reader converts the packet into an EXIF block and prints the converted tags; MetadataSharp reads a sidecar as a packet and offers that conversion explicitly through MetadataSync.XmpToExif instead. The file has no EXIF block of its own (the gate requires that the library prints no EXIF row for it), so these rows measure the reference's conversion, not its tag table.";

    // Exact reference rows that no local row can answer, because the reference reads something the library
    // deliberately does not. The key is (pinned file name, key pattern): a pattern ending with "." covers
    // that group prefix, the pattern "Exif.*" covers every EXIF key of a file that has no EXIF block of its
    // own, and any other pattern must equal the key exactly. Only reference-only rows can be covered: a
    // documented row that the library starts printing fails the comparison until the entry is removed, and
    // so does every row the patterns do not name.
    public static readonly OrderedDictionary<(string File, string Pattern), string> RowExceptions = new()
    {
        [("BlueSquare.xmp", "Exif.*")] = SidecarConversionRule,
        [("StaffPhotographer-Example.xmp", "Exif.*")] = SidecarConversionRule,
        [("exiv2-bug1112.xmp", "Exif.*")] = SidecarConversionRule,
        [("issue_1959_poc.xmp", "Exif.*")] = SidecarConversionRule,
        [("issue_1998.xmp", "Exif.*")] = SidecarConversionRule,
    };

    // These are upstream expectation basenames for command-line/unit tests, not metadata files.
    // Corpus.DiscoverExiv2 deliberately does not guess an image for a *_test input.
    public static readonly OrderedDictionary<string, string> MissingInputExceptions = new()
    {
        ["addmoddel"] = "Synthetic command-line test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["conversions"] = "Synthetic conversion-test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["crw-test"] = "Synthetic CRW test expectation; no corresponding CRW input is present in the pinned test/data tree.",
        ["exifdata-test"] = "Synthetic ExifData unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["exiv2-bug1145a.exv_test"] = "Synthetic regression-test expectation; no corresponding EXV input is present in the pinned test/data tree.",
        ["exiv2-test"] = "Synthetic CLI test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["geotag-test"] = "Synthetic geotag unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["icc-test"] = "Synthetic ICC unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["iotest"] = "Synthetic I/O test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["iso65k-test"] = "Synthetic ISO-65K unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["modify-test"] = "Synthetic metadata-edit test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["nls-test"] = "Synthetic localization test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["png-test"] = "Synthetic PNG unit-test expectation; no corresponding PNG input is present in the pinned test/data tree.",
        ["preview-test"] = "Synthetic preview unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["stdin-test"] = "Synthetic standard-input test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["stringto-test"] = "Synthetic string-formatting unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["test_pr_1905_poc1_ref"] = "Synthetic pull-request proof-of-concept expectation; its generated input is not checked into the pinned test/data tree.",
        ["tiff-test"] = "Synthetic TIFF unit-test expectation; no corresponding TIFF input is present in the pinned test/data tree.",
        ["webp-test"] = "Synthetic WebP unit-test expectation; no corresponding WebP input is present in the pinned test/data tree.",
        ["write-test-debug"] = "Synthetic writer-debug test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["write-test"] = "Synthetic writer unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["write2-test"] = "Synthetic second writer unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
        ["xmpparser-test"] = "Synthetic XMP parser unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    };

    // Only the observed result for each exact pinned input is allowed. A changed outcome returns to parity.
    public static readonly OrderedDictionary<string, TagPrintRefusal> RefusalExceptions = new()
    {
        ["1-string-format.jpg"] = new("metadatasharp-refused", "refused", "InvalidJpeg", "MetadataSharp rejects the JPEG structure with InvalidJpeg. The exact pinned file 1-string-format.jpg is listed separately; the refusal is not counted as a successful comparison."),
        ["2018-01-09-exiv2-crash-002.tiff"] = new("metadatasharp-refused", "refused", "AmbiguousMetadata", "MetadataSharp refuses to resolve ambiguous metadata: the fuzzed directory of this input lists tag 0x9E9E twice, which is what this reader stops at now that an unsorted directory is read. The exact pinned file 2018-01-09-exiv2-crash-002.tiff is listed separately; the refusal is not counted as a successful comparison."),
        ["2018-01-09-exiv2-crash-003.tiff"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file 2018-01-09-exiv2-crash-003.tiff is listed separately; the refusal is not counted as a successful comparison."),
        ["Free_Test_Data_500KB_WAV.wav"] = new("exiv2-refused", 1L, null, "Pinned Exiv2 0.28.9 exits with status 1 for this exact input; its -pt oracle row set is unavailable. The refusal is named and not counted as a successful comparison."),
        ["FurnaceCreekInn.jpg"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file FurnaceCreekInn.jpg is listed separately; the refusal is not counted as a successful comparison."),
        ["IMGP0006-min.jpg"] = new("metadatasharp-refused", "refused", "InvalidJpeg", "MetadataSharp rejects the JPEG structure with InvalidJpeg. The exact pinned file IMGP0006-min.jpg is listed separately; the refusal is not counted as a successful comparison."),
        ["coverage_xmp_doctype.exv"] = new("metadatasharp-refused", "refused", "InvalidXmp", "MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file coverage_xmp_doctype.exv is listed separately; the refusal is not counted as a successful comparison."),
        ["coverage_xmpsidecar_isXmpType.xmp"] = new("metadatasharp-refused", "refused", "InvalidXmp", "MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file coverage_xmpsidecar_isXmpType.xmp is listed separately; the refusal is not counted as a successful comparison."),
        ["cve_2017_18005_reproducer.tiff"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file cve_2017_18005_reproducer.tiff is listed separately; the refusal is not counted as a successful comparison."),
        ["exiv2-bug443.jpg"] = new("metadatasharp-refused", "refused", "LimitExceeded", "MetadataSharp stops at its configured parsing limit. The exact pinned file exiv2-bug443.jpg is listed separately; the refusal is not counted as a successful comparison."),
        ["exiv2-bug479.jpg"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file exiv2-bug479.jpg is listed separately; the refusal is not counted as a successful comparison."),
        ["exiv2-bug855.jpg"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file exiv2-bug855.jpg is listed separately; the refusal is not counted as a successful comparison."),
        ["exiv2-bug922.jpg"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file exiv2-bug922.jpg is listed separately; the refusal is not counted as a successful comparison."),
        ["exiv2_0-26_exiv2_uncontrolled-recursion_printIFDStructure.tif"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file exiv2_0-26_exiv2_uncontrolled-recursion_printIFDStructure.tif is listed separately; the refusal is not counted as a successful comparison."),
        ["flame.avi"] = new("exiv2-refused", 1L, null, "Pinned Exiv2 0.28.9 exits with status 1 for this exact input; its -pt oracle row set is unavailable. The refusal is named and not counted as a successful comparison."),
        ["heap-oob-write.tiff"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file heap-oob-write.tiff is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_1706_poc.exv"] = new("metadatasharp-refused", "refused", "LimitExceeded", "MetadataSharp stops at its configured parsing limit. The exact pinned file issue_1706_poc.exv is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_1713_poc.xmp"] = new("metadatasharp-refused", "refused", "InvalidXmp", "MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_1713_poc.xmp is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_1819_poc.exv"] = new("metadatasharp-refused", "refused", "InvalidXmp", "MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_1819_poc.exv is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_1821_poc.xmp"] = new("metadatasharp-refused", "refused", "InvalidXmp", "MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_1821_poc.xmp is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_1912_poc.jpg"] = new("metadatasharp-refused", "refused", "InvalidJpeg", "MetadataSharp rejects the JPEG structure with InvalidJpeg. The exact pinned file issue_1912_poc.jpg is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_1920_poc.tiff"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file issue_1920_poc.tiff is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_2006_poc.tiff"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file issue_2006_poc.tiff is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_851_poc.xmp"] = new("metadatasharp-refused", "refused", "InvalidXmp", "MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_851_poc.xmp is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_867_poc.psd"] = new("metadatasharp-refused", "refused", "InvalidPsd", "MetadataSharp rejects the PSD structure with InvalidPsd. The exact pinned file issue_867_poc.psd is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_94_poc3.pgf"] = new("metadatasharp-refused", "refused", "InvalidPng", "The image reader reports InvalidPng for this PGF fixture. The exact pinned file issue_94_poc3.pgf is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_ghsa_5p8g_9xf3_gfrr_poc.exv"] = new("metadatasharp-refused", "refused", "UnsupportedFeature", "MetadataSharp reports an unsupported feature for this encrypted Nikon MakerNote fixture. The exact pinned file issue_ghsa_5p8g_9xf3_gfrr_poc.exv is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_ghsa_5p8g_9xf3_gfrr_poc.webp"] = new("metadatasharp-refused", "refused", "UnsupportedFeature", "MetadataSharp reports an unsupported feature for this encrypted Nikon MakerNote fixture. The exact pinned file issue_ghsa_5p8g_9xf3_gfrr_poc.webp is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_ghsa_g44w_q3vm_gwjq_poc.jpg"] = new("metadatasharp-refused", "refused", "InvalidJpeg", "MetadataSharp rejects the JPEG structure with InvalidJpeg. The exact pinned file issue_ghsa_g44w_q3vm_gwjq_poc.jpg is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_ghsa_h9x9_4f77_336w_poc.exv"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file issue_ghsa_h9x9_4f77_336w_poc.exv is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_ghsa_jgm9_5fw5_pw9p_poc.exv"] = new("metadatasharp-refused", "refused", "AmbiguousMetadata", "MetadataSharp refuses to resolve ambiguous metadata. The exact pinned file issue_ghsa_jgm9_5fw5_pw9p_poc.exv is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_ghsa_jgm9_5fw5_pw9p_poc.jp2"] = new("metadatasharp-refused", "refused", "InvalidJp2", "MetadataSharp rejects the JP2 structure with InvalidJp2. The exact pinned file issue_ghsa_jgm9_5fw5_pw9p_poc.jp2 is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_ghsa_m479_7frc_gqqg_poc.crw"] = new("metadatasharp-refused", "refused", "InvalidExif", "MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file issue_ghsa_m479_7frc_gqqg_poc.crw is listed separately; the refusal is not counted as a successful comparison."),
        ["issue_ghsa_mxw9_qx4c_6m8v_poc.jp2"] = new("exiv2-refused", 1L, null, "Pinned Exiv2 0.28.9 exits with status 1 for this exact input; its -pt oracle row set is unavailable. The refusal is named and not counted as a successful comparison."),
        ["issue_ghsa_v5g7_46xf_h728_poc.exv"] = new("metadatasharp-refused", "refused", "InvalidXmp", "MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_ghsa_v5g7_46xf_h728_poc.exv is listed separately; the refusal is not counted as a successful comparison."),
        ["poc_2542.exv"] = new("metadatasharp-refused", "refused", "AmbiguousMetadata", "MetadataSharp refuses to resolve ambiguous metadata. The exact pinned file poc_2542.exv is listed separately; the refusal is not counted as a successful comparison."),
        ["pr_2574.dcp"] = new("metadatasharp-refused", "not-attempted", "magic", "MetadataSharp does not recognize this DCP input signature and marks the file not attempted. The exact pinned file pr_2574.dcp is listed separately; the refusal is not counted as a successful comparison."),
        ["sample_960x540.asf"] = new("exiv2-refused", 1L, null, "Pinned Exiv2 0.28.9 exits with status 1 for this exact input; its -pt oracle row set is unavailable. The refusal is named and not counted as a successful comparison."),
    };

    // The reference prints one record per line and gives each column a width: the key is left-aligned in
    // 44 characters, the field type in 9, the count is right-aligned in 3, and the printed value follows
    // two spaces of its own (app/actions.cpp, printMetadatum). A column is a minimum width, so a longer key
    // or count pushes the rest of the line right. Both details matter here: collapsing runs of spaces would
    // drop the leading spaces of a stored value, and a key of the Minolta camera-settings groups contains a
    // space of its own, which a whitespace split reads as the start of the type column.
    public const int PrintKeyWidth = 44;
    private const int PrintTypeWidth = 9;
    private static readonly string[] TypeNames =
        ["Byte", "Ascii", "Short", "Long", "Rational", "SByte", "Undefined", "SShort", "SLong", "SRational", "Float", "Double", "Ifd"];
    // An unknown field type is printed as its four-digit hexadecimal code.
    private static readonly string TypeAlternation = "(?:" + string.Join("|", TypeNames) + "|0[xX][0-9a-fA-F]{4})";
    // The count is right-aligned in three characters and the value follows two spaces; a recorded row
    // without a value (a value of no bytes) ends after the count.
    private static readonly Regex CountAndValue = new(@"^ *(?<count>\d+)(?:  (?<value>.*))?\z", RegexOptions.CultureInvariant);
    private static readonly Regex WideRow = new(@"^(?<key>.*?) +(?<type>" + TypeAlternation + @") *(?<tail>.*)\z", RegexOptions.CultureInvariant);

    private static Dictionary<string, long> LoadCatalogKeyIds()
    {
        var data = (PyDict)PyJson.Load(Repo.Path("src/MetadataSharp.Core/tag-catalog-data.json"))!;
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (PyDict group in data.List("exifGroups").Cast<PyDict>())
        {
            foreach (PyDict tag in group.List("tags").Cast<PyDict>())
            {
                foreach (string name in tag.List("names").Cast<string>()) ids["Exif." + group.Str("name") + "." + name] = tag.Long("id");
            }
        }
        return ids;
    }

    /// <summary>The identity a key is compared under: (group, tag number) for a catalogued or numeric key, else the key.</summary>
    public static PyTuple CanonicalKey(string key)
    {
        if (CatalogKeyIds.TryGetValue(key, out long id)) return new PyTuple(key[..key.LastIndexOf('.')], id);
        var match = NumericKey.Match(key);
        if (match.Success) return new PyTuple(match.Groups[1].Value, Convert.ToInt64(match.Groups[2].Value, 16));
        return new PyTuple(key);
    }

    // TIFF IFD (type 13) is a 32-bit offset; the local value model stores that word as Long.
    private static string CanonicalType(string typeName) => typeName == "Ifd" ? "Long" : typeName;

    /// <summary>Whether a key is a numeric fallback such as Exif.Canon.0x0019.</summary>
    public static bool IsRawNumericTag(string key) => RawNumericTag.IsMatch(key);

    /// <summary>The documented reason for a reference row with no local counterpart, or null.</summary>
    public static string? RowException(string fileName, PyDict row, bool hasLocalExif)
    {
        string key = Py.Str(row.Get("key"));
        foreach (var ((name, pattern), reason) in RowExceptions)
        {
            if (name != fileName) continue;
            if (pattern == "Exif.*")
            {
                if (!hasLocalExif && key.StartsWith("Exif.", StringComparison.Ordinal)) return reason;
            }
            else if (key.StartsWith(pattern, StringComparison.Ordinal)) return reason;
        }
        return null;
    }

    /// <summary>The named refusal a problem matches exactly, or null.</summary>
    public static PyDict? DocumentRefusal(string name, PyDict problem)
    {
        if (!RefusalExceptions.TryGetValue(name, out var expected)) return null;
        if (!Py.Eq(problem.Get("kind"), expected.Kind) || !Py.Eq(problem.Get("status"), expected.Status)) return null;
        if (expected.Detail is not null && !Py.Eq(problem.Get("detail"), expected.Detail)) return null;
        return PyDict.Of(("file", name), ("kind", expected.Kind), ("status", expected.Status), ("detail", problem.Get("detail")), ("reason", expected.Reason));
    }

    /// <summary>Undo the dump's escapes: \t, \n, \r, \\ and \xHH.</summary>
    public static string Decode(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        int at = 0;
        while (at < value.Length)
        {
            if (value[at] != '\\' || at + 1 == value.Length)
            {
                result.Append(value[at]);
                at++;
                continue;
            }
            char code = value[at + 1];
            if (code == 't') { result.Append('\t'); at += 2; }
            else if (code == 'n') { result.Append('\n'); at += 2; }
            else if (code == 'r') { result.Append('\r'); at += 2; }
            else if (code == '\\') { result.Append('\\'); at += 2; }
            else if (code == 'x' && at + 3 < value.Length)
            {
                if (TryHex(value.Substring(at + 2, 2), out int number))
                {
                    result.Append((char)number);
                    at += 4;
                }
                else
                {
                    result.Append('\\');
                    at += 1;
                }
            }
            else
            {
                result.Append('\\');
                at += 1;
            }
        }
        return result.ToString();
    }

    // int(text, 16) for the two characters of an escape: Python accepts a sign, whitespace and underscores.
    private static bool TryHex(string text, out int value)
    {
        value = 0;
        if (!PyText.TryParseInt(text, out long parsed, 16) || parsed < 0) return false;
        value = (int)parsed;
        return true;
    }

    /// <summary>Split one printed row into its key, type, count and the exact text of the value column.</summary>
    public static PyDict? ParsePtRow(string line)
    {
        int typeStart = PrintKeyWidth + 1, typeEnd = typeStart + PrintTypeWidth;
        var points = Py.CodePoints(line).ToList();
        if (points.Count > typeEnd && points[PrintKeyWidth] == ' ' && points[typeEnd] == ' ')
        {
            string key = PyText.RStrip(Slice(points, 0, PrintKeyWidth), " ");
            string typeName = PyText.RStrip(Slice(points, typeStart, typeEnd), " ");
            var count = CountAndValue.Match(Slice(points, typeEnd + 1, points.Count));
            if (typeName.Length > 0 && count.Success)
            {
                return PyDict.Of(("key", key), ("type", typeName), ("count", PyText.ParseInt(count.Groups["count"].Value)),
                                 ("value", count.Groups["value"].Success ? count.Groups["value"].Value : ""));
            }
        }
        // A key longer than its column, or a count wider than its own: read the columns by their content.
        var wide = WideRow.Match(line);
        if (!wide.Success) return null;
        var tail = CountAndValue.Match(wide.Groups["tail"].Value);
        if (!tail.Success) return null;
        return PyDict.Of(("key", wide.Groups["key"].Value), ("type", wide.Groups["type"].Value), ("count", PyText.ParseInt(tail.Groups["count"].Value)),
                         ("value", tail.Groups["value"].Success ? tail.Groups["value"].Value : ""));
    }

    private static string Slice(List<int> points, int start, int end) =>
        string.Concat(points.Skip(start).Take(Math.Max(0, end - start)).Select(Py.FromCodePoint));

    /// <summary>Parse -pt rows: key, type, count and the one value Exiv2 prints (translated when known).</summary>
    public static List<PyDict> ParseExiv2(string text)
    {
        var rows = new List<PyDict>();
        foreach (string line in PyText.SplitLines(text))
        {
            if (!line.StartsWith("Exif.", StringComparison.Ordinal)) continue;
            var row = ParsePtRow(line);
            if (row is not null) rows.Add(row);
        }
        return rows;
    }

    /// <summary>Select the text a -pt style print would show from each dump record.</summary>
    public static List<PyDict> ProjectDump(IEnumerable<PyDict> rows) =>
        rows.Where(row => row.Str("key").StartsWith("Exif.", StringComparison.Ordinal))
            .Select(row => PyDict.Of(("key", row["key"]), ("type", row["type"]), ("count", row["count"]),
                                     ("value", Py.Or(row["translated"], row["raw"])), ("_raw_value", row["raw"]),
                                     ("_has_translation", Py.Truthy(row["translated"]))))
            .ToList();

    /// <summary>Parse the dump protocol: FILE, TAG and STATUS records, rejecting open or malformed blocks.</summary>
    public static PyDict ParseDump(string text)
    {
        var files = new PyDict();
        PyDict? current = null;
        int number = 0;
        foreach (string line in PyText.SplitLines(text))
        {
            number++;
            if (line.Length == 0) continue;
            var fields = line.Split('\t');
            if (fields[0] == "FILE" && fields.Length == 2)
            {
                if (current is not null && current["status"] is null)
                    throw new TagPrintError($"dump:{number}: FILE record before the previous block ended");
                string path = Decode(fields[1]);
                if (files.ContainsKey(path)) throw new TagPrintError($"dump:{number}: repeated FILE record {path}");
                current = PyDict.Of(("rows", new List<object?>()), ("status", null), ("detail", ""));
                files[path] = current;
            }
            else if (fields[0] == "TAG" && fields.Length is 5 or 6 && current is not null && current["status"] is null)
            {
                if (!PyText.TryParseInt(fields[3], out long count)) throw new TagPrintError($"dump:{number}: invalid TAG count {Py.ReprString(fields[3])}");
                current.List("rows").Add(PyDict.Of(("key", Decode(fields[1])), ("type", fields[2]), ("count", count), ("raw", Decode(fields[4])),
                                                   ("translated", fields.Length == 6 ? Decode(fields[5]) : "")));
            }
            else if (fields[0] == "STATUS" && fields.Length >= 2 && current is not null && current["status"] is null)
            {
                current["status"] = fields[1];
                current["detail"] = Decode(string.Join("\t", fields.Skip(2)));
            }
            else throw new TagPrintError($"dump:{number}: unexpected record: {Py.ReprString(line)}");
        }
        foreach (var (path, result) in files)
        {
            if (((PyDict)result!)["status"] is null) throw new TagPrintError($"dump omitted the end status for {path}");
        }
        return files;
    }

    /// <summary>Compare rows as a multiset, allowing only exact, documented display aliases.</summary>
    public static List<PyDict> CompareRows(List<PyDict> reference, List<PyDict> actual,
                                           OrderedDictionary<string, OrderedDictionary<string, string>>? descriptionAliases = null,
                                           Counter? acceptedAliases = null, Counter? acceptedPadding = null, Counter? acceptedNames = null,
                                           Counter? acceptedLineTruncations = null, Counter? acceptedTypeAliases = null)
    {
        static PyTuple Identity(PyDict row) =>
            new(CanonicalKey(Py.Str(row.Get("key"))), CanonicalType(Py.Str(row.Get("type"))), row.Get("count"));

        var expectedByKey = new Dictionary<PyTuple, List<object?>>();
        var actualByKey = new Dictionary<PyTuple, List<object?>>();
        foreach (var row in reference) Bucket(expectedByKey, Identity(row)).Add(row);
        foreach (var row in actual) Bucket(actualByKey, Identity(row)).Add(row);
        var differences = new List<PyDict>();
        var keys = expectedByKey.Keys.Union(actualByKey.Keys).ToList();
        foreach (var key in Py.Sorted(keys, k => (object?)Py.Repr(k)))
        {
            var expected = new List<object?>(Bucket(expectedByKey, key));
            var observed = new List<object?>(Bucket(actualByKey, key));
            foreach (PyDict row in expected.ToList().Cast<PyDict>())
            {
                bool IsPaddingEquivalent(PyDict candidate)
                {
                    string keyName = Py.Str(row.Get("key"));
                    if (!Py.Eq(row.Get("type"), "Ascii") && keyName != "Exif.Photo.UserComment") return false;
                    return row.Get("value") is string expectedValue && candidate.Get("value") is string actualValue
                           && expectedValue != actualValue && PyText.RStrip(expectedValue, " \0") == PyText.RStrip(actualValue, " \0");
                }

                bool IsLineBoundaryEquivalent(PyDict candidate)
                {
                    string keyName = Py.Str(row.Get("key"));
                    if (!Py.Eq(row.Get("type"), "Ascii") && keyName != "Exif.Photo.UserComment") return false;
                    if (row.Get("value") is not string expectedValue || candidate.Get("value") is not string actualValue) return false;
                    // The reference prints the text up to the first line break of a longer local value.
                    if (expectedValue.Length == 0 || !actualValue.StartsWith(expectedValue, StringComparison.Ordinal)) return false;
                    string remainder = actualValue[expectedValue.Length..];
                    return remainder.StartsWith('\n') || remainder.StartsWith("\r\n", StringComparison.Ordinal);
                }

                var match = observed.Cast<PyDict>().FirstOrDefault(candidate =>
                    Py.Eq(candidate, row)
                    || (Py.Eq(CanonicalKey(Py.Str(candidate.Get("key"))), CanonicalKey(Py.Str(row.Get("key"))))
                        && Py.Eq(candidate.Get("value"), row.Get("value")))
                    || IsPaddingEquivalent(candidate) || IsLineBoundaryEquivalent(candidate)
                    || (descriptionAliases is not null
                        && Py.Eq(descriptionAliases.TryGetValue(Py.Str(row.Get("key")), out var aliases)
                                     ? (aliases.TryGetValue(Py.Str(row.Get("value")), out string? alias) ? alias : null)
                                     : null,
                                 candidate.Get("value"))));
                if (match is null) continue;
                if (!Py.Eq(match.Get("key"), row.Get("key")) && Py.Eq(match.Get("value"), row.Get("value")) && acceptedNames is not null)
                    acceptedNames.Add(new PyTuple(Py.Str(row.Get("key")), Py.Str(match.Get("key"))));
                else if (!Py.Eq(match.Get("type"), row.Get("type")) && acceptedTypeAliases is not null)
                    acceptedTypeAliases.Add(new PyTuple(Py.Str(row.Get("type")), Py.Str(match.Get("type"))));
                else if (!Py.Eq(match, row) && IsPaddingEquivalent(match) && acceptedPadding is not null)
                    acceptedPadding.Add(Py.Str(row.Get("type")));
                else if (!Py.Eq(match, row) && IsLineBoundaryEquivalent(match) && acceptedLineTruncations is not null)
                    acceptedLineTruncations.Add(Py.Str(row.Get("type")));
                else if (!Py.Eq(match.Get("value"), row.Get("value")) && acceptedAliases is not null)
                    acceptedAliases.Add(new PyTuple(Py.Str(row.Get("key")), Py.Str(row.Get("value")), Py.Str(match.Get("value"))));
                expected.Remove(row);
                observed.Remove(match);
            }
            while (expected.Count > 0 && observed.Count > 0)
            {
                var exiv2 = expected[^1];
                expected.RemoveAt(expected.Count - 1);
                var local = observed[^1];
                observed.RemoveAt(observed.Count - 1);
                differences.Add(PyDict.Of(("kind", "value-diff"), ("exiv2", exiv2), ("metadatasharp", local)));
            }
            differences.AddRange(expected.Select(row => PyDict.Of(("kind", "missing"), ("exiv2", row), ("metadatasharp", null))));
            differences.AddRange(observed.Select(row => PyDict.Of(("kind", "extra"), ("exiv2", null), ("metadatasharp", row))));
        }
        return differences;
    }

    private static List<object?> Bucket(Dictionary<PyTuple, List<object?>> buckets, PyTuple key)
    {
        if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = [];
        return list;
    }

    /// <summary>Resolve -pt text differences only when Exiv2 -pv confirms identical raw value and structure.</summary>
    public static (List<PyDict> Remaining, List<PyDict> Verified) VerifyRawValueDifferences(
        List<PyDict> differences, List<PyDict> rawReference, List<PyDict> sourceRows, List<PyDict>? probes = null)
    {
        var rawByIdentity = new Dictionary<PyTuple, List<object?>>();
        var sourceByIdentity = new Dictionary<PyTuple, List<object?>>();
        foreach (var row in rawReference) Bucket(rawByIdentity, new PyTuple(CanonicalKey(Py.Str(row.Get("key"))), row.Get("type"), row.Get("count"))).Add(row);
        foreach (var row in sourceRows) Bucket(sourceByIdentity, new PyTuple(row.Get("key"), row.Get("type"), row.Get("count"))).Add(row);
        var remaining = new List<PyDict>();
        var verified = new List<PyDict>();
        foreach (var difference in differences)
        {
            if (!Py.Eq(difference.Get("kind"), "value-diff"))
            {
                remaining.Add(difference);
                continue;
            }
            var expected = (PyDict)difference["exiv2"]!;
            var observed = (PyDict)difference["metadatasharp"]!;
            var identity = new PyTuple(CanonicalKey(Py.Str(expected.Get("key"))), expected.Get("type"), expected.Get("count"));
            var localKey = new PyTuple(observed.Get("key"), observed.Get("type"), observed.Get("count"));
            var localSource = (sourceByIdentity.TryGetValue(localKey, out var sources) ? sources : []).Cast<PyDict>()
                .FirstOrDefault(row => Py.Eq(Py.Or(row.Get("translated"), row.Get("raw")), observed.Get("value")));
            if (localSource is null)
            {
                if (probes is not null && probes.Count < 20)
                    probes.Add(PyDict.Of(("key", expected.Get("key")), ("reason", "no matching local raw source row"),
                                         ("pt_reference", expected.Get("value")), ("pt_local", observed.Get("value"))));
                remaining.Add(difference);
                continue;
            }
            var rawCandidates = rawByIdentity.TryGetValue(identity, out var raws) ? raws : [];
            var rawMatch = rawCandidates.Cast<PyDict>().FirstOrDefault(row => Py.Eq(row.Get("value"), localSource.Get("raw")));
            if (rawMatch is null)
            {
                if (probes is not null && probes.Count < 20)
                    probes.Add(PyDict.Of(("key", expected.Get("key")), ("reason", "raw values differ"),
                                         ("pt_reference", expected.Get("value")), ("pt_local", observed.Get("value")),
                                         ("local_raw", localSource.Get("raw")),
                                         ("exiv2_pv_raw", rawCandidates.Cast<PyDict>().Select(row => row.Get("value")).ToList())));
                remaining.Add(difference);
                continue;
            }
            rawByIdentity[identity].Remove(rawMatch);
            verified.Add(PyDict.Of(("key", Py.Str(expected.Get("key"))), ("exiv2_pt", Py.Str(expected.Get("value"))),
                                   ("metadatasharp_pt", Py.Str(observed.Get("value"))), ("raw_value", Py.Str(rawMatch.Get("value")))));
        }
        return (remaining, verified);
    }

    // Keep the one-line workflow annotation below GitHub's annotation payload limit while making the first
    // discrepancies actionable; the full report is written to the JSON artifact.
    internal static PyDict Summary(PyDict report)
    {
        var selected = new List<PyDict>();
        var refusalCounts = new Counter();
        var sampledExtensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (PyDict item in report.List("differing").Cast<PyDict>())
        {
            string kind = item.Str("kind");
            string extension = PyPath.Suffix(item.Str("file")).ToLowerInvariant();
            if (kind is "metadatasharp-refused" or "exiv2-refused")
            {
                if (refusalCounts.CountOf(kind) >= 2) continue;
                refusalCounts.Add(kind);
            }
            else if (kind == "rows-differ")
            {
                if (sampledExtensions.Contains(extension)) continue;
                sampledExtensions.Add(extension);
            }
            if (selected.Count >= 8) break;
            selected.Add(item);
        }
        var samples = new List<object?>();
        foreach (var item in selected)
        {
            var sample = new PyDict(item.Where(pair => !Py.Eq(pair.Key, "rows")));
            if (item.ContainsKey("rows"))
            {
                var rows = new List<object?>();
                sample["rows"] = rows;
                foreach (PyDict row in item.List("rows").Take(5).Cast<PyDict>())
                {
                    var compact = row.Copy();
                    foreach (string side in new[] { "exiv2", "metadatasharp" })
                    {
                        if (compact.Get(side) is PyDict record)
                        {
                            compact[side] = new PyDict(record.Select(pair => new KeyValuePair<object, object?>(pair.Key,
                                pair.Value is string text && Py.Len(text) > 160 ? PyText.Head(text, 160) + "\u2026" : pair.Value)));
                        }
                    }
                    rows.Add(compact);
                }
            }
            samples.Add(sample);
        }
        return PyDict.Of(
            ("totals", report["totals"]), ("failure_kinds", report["failure_kinds"]),
            ("difference_types", report["difference_types"]),
            ("padding_format_rule", report["padding_format_rule"]),
            ("line_boundary_rule", report["line_boundary_rule"]),
            ("mismatch_groups", report.List("mismatch_groups").Take(150).ToList()),
            ("display_rule_hits", report["display_rule_hits"]),
            ("name_rule_hits", report["name_rule_hits"]),
            ("type_rule_hits", report["type_rule_hits"]),
            ("raw_value_display_hits", report["raw_value_display_hits"]),
            ("row_rule_hits", report["row_rule_hits"]),
            ("raw_display_probes", report.List("raw_display_probes").Take(3).Cast<PyDict>().Select(probe => (object?)new PyDict(probe.Select(pair =>
                new KeyValuePair<object, object?>(pair.Key, pair.Value switch
                {
                    List<object?> list => list.Take(3).ToList(),
                    string text => PyText.Head(text, 120),
                    _ => pair.Value,
                })))).ToList()),
            ("value_diff_by_key", report.List("value_diff_by_key").Take(40).ToList()),
            ("refusals", report["refusals"]),
            ("formats", report["formats"]),
            ("exceptions", report["exceptions"]), ("tag_exceptions", report["tag_exceptions"]),
            ("raw_only_tag_exception_total", (long)report.List("raw_only_tag_exceptions").Count),
            ("raw_only_tag_exceptions", report.List("raw_only_tag_exceptions").Take(30).ToList()),
            ("differing_total", (long)report.List("differing").Count),
            ("differing_sample", samples));
    }

    internal static void EmitAnnotation(PyDict payload)
    {
        void Emit(string title, PyDict part)
        {
            string encoded = Out.Compressed(PyJson.Compact(part, ensureAscii: false));
            if (encoded.Length <= 3500)
            {
                Out.Print($"::notice title={title}::{encoded}");
                return;
            }
            // A failing run is exactly when the report is large, so an oversized payload is halved rather
            // than dropped: losing it leaves the failure unexplained.
            var keys = part.Keys.ToList();
            if (keys.Count < 2) throw new TagPrintError($"annotation payload too large for {title}: {encoded.Length} characters");
            int half = keys.Count / 2;
            Emit($"{title} 1/2", new PyDict(keys.Take(half).Select(key => new KeyValuePair<object, object?>(key, part[key]))));
            Emit($"{title} 2/2", new PyDict(keys.Skip(half).Select(key => new KeyValuePair<object, object?>(key, part[key]))));
        }

        var grouped = (List<object?>?)payload.Pop("mismatch_groups", new List<object?>())!;
        var refusals = (List<object?>?)payload.Pop("refusals", new List<object?>())!;
        var valueDifferences = (List<object?>?)payload.Pop("value_diff_by_key", new List<object?>())!;
        var aliasHits = (List<object?>?)payload.Pop("display_rule_hits", new List<object?>())!;
        var nameHits = (List<object?>?)payload.Pop("name_rule_hits", new List<object?>())!;
        var typeHits = (List<object?>?)payload.Pop("type_rule_hits", new List<object?>())!;
        var rawValueHits = (List<object?>?)payload.Pop("raw_value_display_hits", new List<object?>())!;
        var rowRules = (List<object?>?)payload.Pop("row_rule_hits", new List<object?>())!;
        Emit($"{Title} summary", payload);
        foreach (var (label, items, size) in new (string, List<object?>, int)[]
                 {
                     ("groups", grouped, 75), ("refusals", refusals, 17), ("values", valueDifferences, 40),
                     ("display rules", aliasHits, 20), ("key-name rules", nameHits, 20), ("type rules", typeHits, 20),
                     ("raw-value display rules", rawValueHits, 20), ("row rules", rowRules, 25),
                 })
        {
            var chunks = Chunk(items, size);
            for (int index = 0; index < chunks.Count; index++)
                Emit($"{Title} {label} {index + 1}/{chunks.Count}", PyDict.Of((label, chunks[index])));
        }
    }

    /// <summary>[items[i:i + size] for i in range(0, len(items), size)] or [[]].</summary>
    public static List<List<object?>> Chunk(List<object?> items, int size)
    {
        var chunks = new List<List<object?>>();
        for (int i = 0; i < items.Count; i += size) chunks.Add(items.Skip(i).Take(size).ToList());
        if (chunks.Count == 0) chunks.Add([]);
        return chunks;
    }

    /// <summary>Run the dump tool over every path, reading the list from standard input.</summary>
    public static PyDict RunDump(IReadOnlyList<string> command, IEnumerable<string> paths)
    {
        string listing = string.Concat(paths.Select(path => path + "\n"));
        Completed result;
        try
        {
            result = PyProcess.Run(command.Concat(["--files-from", "-"]).ToList(), input: listing, timeout: 1800L);
        }
        catch (Exception error) when (error is PyOSError { Errno: 2 } or PyTimeoutExpired)
        {
            throw new TagPrintError($"MetadataSharp.Dump failed to run: {error.Message}");
        }
        if (result.ReturnCode != 0)
            throw new TagPrintError($"MetadataSharp.Dump exited {result.ReturnCode}: {PyText.Head(PyText.Strip(result.Stderr), 400)}");
        return ParseDump(result.Stdout);
    }

    /// <summary>Run the pinned reference on one file: (status, stdout, stderr).</summary>
    public static (int Status, string Output, string Error) RunReference(IReadOnlyList<string> command, string path, string mode = "-pt")
    {
        try
        {
            var result = PyProcess.Run(command.Concat([mode, path]).ToList(), timeout: 120L);
            return (result.ReturnCode, result.Stdout, result.Stderr);
        }
        catch (Exception error) when (error is PyOSError { Errno: 2 } or PyTimeoutExpired)
        {
            throw new TagPrintError($"Exiv2 -pt failed for {PyPath.Name(path)}: {error.Message}");
        }
    }

    /// <summary>Build the full report over every candidate; runReference stands in for the pinned tool in tests.</summary>
    public static PyDict BuildReport(List<Candidate> candidates, IReadOnlyList<string> referenceCommand, PyDict dump, List<string>? unmatched = null,
                                     Func<IReadOnlyList<string>, string, string, (int, string, string)>? runReference = null)
    {
        runReference ??= RunReference;
        var unmatchedNames = new HashSet<string>(unmatched ?? [], StringComparer.Ordinal);
        var unexplained = unmatchedNames.Where(name => !MissingInputExceptions.ContainsKey(name)).ToList();
        if (unexplained.Count > 0)
            throw new TagPrintError("unmatched upstream expectations need named, reviewed exceptions: " + string.Join(", ", Py.Sorted(unexplained)));
        var exceptions = Py.Sorted(unmatchedNames).Select(name => (object?)PyDict.Of(("file", name), ("reason", MissingInputExceptions[name]))).ToList();
        var tagExceptions = Py.Sorted(KeyExceptions, pair => pair.Key).Select(pair => (object?)PyDict.Of(("key", pair.Key), ("reason", pair.Value))).ToList();
        var rawOnlyTags = new Counter();
        var differing = new List<object?>();
        var namedRefusals = new List<object?>();
        var totals = new Counter();
        var failureKinds = new Counter();
        var differenceTypes = new Counter();
        var valuePairs = new Counter();
        var acceptedAliases = new Counter();
        var acceptedPadding = new Counter();
        var acceptedLineTruncations = new Counter();
        var acceptedTypeAliases = new Counter();
        var acceptedNames = new Counter();
        var verifiedRawDisplays = new Counter();
        var rawDisplayProbes = new List<PyDict>();
        var mismatchGroups = new Counter();
        var rowRuleHits = new Counter();
        var mismatchLocations = new Counter();
        var formats = new OrderedDictionary<string, Counter>();
        foreach (var candidate in candidates)
        {
            string name = candidate.Label;
            if (MissingInputExceptions.TryGetValue(name, out string? missingReason))
            {
                exceptions.Add(PyDict.Of(("file", name), ("reason", missingReason)));
                continue;
            }
            totals.Add("files");
            string formatName = candidate.Format.ToUpperInvariant();
            if (!formats.TryGetValue(formatName, out var formatCounts)) formats[formatName] = formatCounts = new Counter();
            formatCounts.Add("files");
            var (status, output, error) = runReference(referenceCommand, candidate.Path, "-pt");
            var ours = (PyDict?)dump.Get(PyPath.Normalize(candidate.Path));
            PyDict? problem = null;
            if (status != 0)
            {
                problem = PyDict.Of(("file", name), ("kind", "exiv2-refused"), ("status", (long)status),
                                    ("detail", PyText.Head((string)Py.Or(PyText.Strip(error), PyText.Strip(output))!, 300)));
            }
            else if (ours is null) problem = PyDict.Of(("file", name), ("kind", "missing-dump-file"));
            else if (!Py.Eq(ours["status"], "ok"))
                problem = PyDict.Of(("file", name), ("kind", "metadatasharp-refused"), ("status", ours["status"]), ("detail", ours["detail"]));
            else
            {
                var sourceRows = ours.List("rows").Cast<PyDict>().ToList();
                var referenceRows = ParseExiv2(output).Where(row => !KeyExceptions.ContainsKey(row.Str("key"))).ToList();
                bool hasLocalExif = sourceRows.Any(row => Py.Str(row["key"]).StartsWith("Exif.", StringComparison.Ordinal));
                var allObserved = ProjectDump(sourceRows);
                var expected = new List<PyDict>();
                foreach (var referenceRow in referenceRows)
                {
                    string? reason = RowException(name, referenceRow, hasLocalExif);
                    if (reason is null) expected.Add(referenceRow);
                    else rowRuleHits.Add(new PyTuple(name, Py.Str(referenceRow["key"]), Py.Str(referenceRow["type"]), referenceRow.Long("count"), reason));
                }
                var sourceByIdentity = new Dictionary<PyTuple, List<object?>>();
                foreach (var sourceRow in sourceRows) Bucket(sourceByIdentity, new PyTuple(sourceRow["key"], sourceRow["type"], sourceRow["count"])).Add(sourceRow);
                var expectedKeys = new HashSet<PyTuple>(expected.Select(row => CanonicalKey(row.Str("key"))));
                foreach (var row in allObserved)
                {
                    if (IsRawNumericTag(row.Str("key")) && !expectedKeys.Contains(CanonicalKey(row.Str("key")))) rawOnlyTags.Add(row.Str("key"));
                }
                var observed = allObserved.Where(row => (expectedKeys.Contains(CanonicalKey(row.Str("key"))) || !IsRawNumericTag(row.Str("key")))
                                                        && !KeyExceptions.ContainsKey(row.Str("key"))).ToList();
                var rows = CompareRows(expected, observed, DisplayAliases, acceptedAliases, acceptedPadding, acceptedNames,
                                       acceptedLineTruncations, acceptedTypeAliases);
                if (rows.Any(row => Py.Eq(row["kind"], "value-diff")))
                {
                    var (rawStatus, rawOutput, _) = runReference(referenceCommand, candidate.Path, "-Pkycv");
                    if (rawStatus == 0)
                    {
                        (rows, var rawVerified) = VerifyRawValueDifferences(rows, ParseExiv2(rawOutput), sourceRows, rawDisplayProbes);
                        foreach (var item in rawVerified)
                            verifiedRawDisplays.Add(new PyTuple(item["key"], item["exiv2_pt"], item["metadatasharp_pt"], item["raw_value"]));
                    }
                }
                if (rows.Count > 0)
                {
                    foreach (var row in rows)
                    {
                        differenceTypes.Add(row.Str("kind"));
                        if (Py.Eq(row["kind"], "value-diff"))
                        {
                            var expectedRow = (PyDict)row["exiv2"]!;
                            var actualRow = (PyDict)row["metadatasharp"]!;
                            string key = expectedRow.Str("key");
                            valuePairs.Add(new PyTuple(key, expectedRow["value"], actualRow["value"]));
                            var bucket = Bucket(sourceByIdentity, new PyTuple(actualRow["key"], actualRow["type"], actualRow["count"]));
                            var localSource = bucket.Cast<PyDict>().FirstOrDefault(source => Py.Eq(ProjectDump([source])[0]["value"], actualRow["value"])) ?? new PyDict();
                            bool localIsInterpreted = Py.Truthy(localSource.Get("translated"));
                            string category = localIsInterpreted ? "description" : "value";
                            if (Py.Eq(expectedRow["type"], "Undefined") && !localIsInterpreted && DigitRun.IsMatch(PyText.Strip(actualRow.Str("value"))))
                                category = "format";
                            mismatchGroups.Add(new PyTuple(category, key, expectedRow["type"], expectedRow.Str("value") + " => " + actualRow.Str("value")));
                            mismatchLocations.Add(new PyTuple(category, key, expectedRow["type"], name));
                        }
                        else
                        {
                            var side = (PyDict)Py.Or(row["exiv2"], row["metadatasharp"])!;
                            mismatchGroups.Add(new PyTuple(row["kind"], side["key"], side["type"], Py.Str(side["count"])));
                            mismatchLocations.Add(new PyTuple(row["kind"], side["key"], side["type"], name));
                        }
                    }
                    problem = PyDict.Of(("file", name), ("kind", "rows-differ"), ("exiv2_rows", (long)expected.Count),
                                        ("metadatasharp_rows", (long)observed.Count), ("rows", rows.Take(8).Cast<object?>().ToList()));
                }
                else
                {
                    totals.Add("matched_rows", expected.Count);
                    formatCounts.Add("matched_rows", expected.Count);
                }
            }
            if (problem is not null)
            {
                var documented = DocumentRefusal(name, problem);
                if (documented is not null)
                {
                    namedRefusals.Add(documented);
                    problem = null;
                }
            }
            if (problem is not null)
            {
                totals.Add("failed_files");
                failureKinds.Add(problem.Str("kind"));
                formatCounts.Add("failed_files");
                differing.Add(problem);
            }
        }
        var rawOnlyExceptions = Py.Sorted(rawOnlyTags.Items(), pair => (object?)new PyTuple(pair.Key, pair.Value)).Select(pair => (object?)PyDict.Of(
            ("key", pair.Key), ("occurrences", pair.Value),
            ("reason", "This exact numeric/vendor key is absent from the pinned Exiv2 0.28.9 tag registry; the raw-only MetadataSharp value is outside the -pt named-tag table comparison."))).ToList();
        var valueDiffByKey = valuePairs.MostCommon(100).Select(pair =>
        {
            var tuple = (PyTuple)pair.Key;
            return (object?)PyDict.Of(("key", tuple[0]), ("exiv2", PyText.Head(tuple.S(1), 100)), ("metadatasharp", PyText.Head(tuple.S(2), 100)), ("occurrences", pair.Value));
        }).ToList();
        var mismatchByKey = new Counter();
        var mismatchExamples = new Dictionary<PyTuple, Counter>();
        foreach (var (groupKey, count) in mismatchGroups.Items())
        {
            var tuple = (PyTuple)groupKey;
            var group = new PyTuple(tuple[0], tuple[1], tuple[2]);
            mismatchByKey.Add(group, count);
            if (!mismatchExamples.TryGetValue(group, out var examples)) mismatchExamples[group] = examples = new Counter();
            examples.Add(tuple[3]!, count);
        }
        var locationsSorted = Py.Sorted(mismatchLocations.Items(), item => (object?)new PyTuple(-item.Value, ((PyTuple)item.Key)[3]));
        var groupedDifferences = Py.Sorted(mismatchByKey.Items(), item => (object?)new PyTuple(-item.Value, item.Key)).Take(200).Select(item =>
        {
            var group = (PyTuple)item.Key;
            return (object?)PyDict.Of(
                ("category", group[0]), ("key", group[1]), ("type", group[2]), ("occurrences", item.Value),
                ("examples", mismatchExamples[group].MostCommon(3).Select(example => (object?)PyDict.Of(
                    ("difference", PyText.Head((string)example.Key, 180)), ("occurrences", example.Value))).ToList()),
                ("files", locationsSorted.Where(location => Py.Eq(new PyTuple(((PyTuple)location.Key)[0], ((PyTuple)location.Key)[1], ((PyTuple)location.Key)[2]), group))
                    .Select(location => (object?)PyDict.Of(("file", ((PyTuple)location.Key)[3]), ("occurrences", location.Value))).Take(10).ToList()));
        }).ToList();
        var refusals = differing.Cast<PyDict>().Where(item => item.Str("kind") is "metadatasharp-refused" or "exiv2-refused" or "missing-dump-file")
            .Cast<object?>().ToList();
        var displayRuleHits = Py.Sorted(acceptedAliases.Items(), item => (object?)new PyTuple(item.Key, item.Value)).Select(item =>
        {
            var tuple = (PyTuple)item.Key;
            return (object?)PyDict.Of(("key", tuple[0]), ("exiv2", tuple[1]), ("metadatasharp", tuple[2]), ("occurrences", item.Value),
                                      ("rule", DisplayReasons[tuple.S(0)]));
        }).ToList();
        var nameRuleHits = Py.Sorted(acceptedNames.Items(), item => (object?)new PyTuple(item.Key, item.Value)).Select(item =>
        {
            var tuple = (PyTuple)item.Key;
            return (object?)PyDict.Of(("exiv2_key", tuple[0]), ("metadatasharp_key", tuple[1]), ("occurrences", item.Value),
                ("rule", "The keys identify the same tag number in the same EXIF directory; the local catalog supplies the name where Exiv2 prints a numeric fallback."));
        }).ToList();
        var typeRuleHits = Py.Sorted(acceptedTypeAliases.Items(), item => (object?)new PyTuple(item.Key, item.Value)).Select(item =>
        {
            var tuple = (PyTuple)item.Key;
            return (object?)PyDict.Of(("exiv2_type", tuple[0]), ("metadatasharp_type", tuple[1]), ("occurrences", item.Value),
                ("rule", "TIFF IFD (type 13) is a 32-bit offset; MetadataSharp's public value model stores the same word as Long. Values and counts still compare exactly."));
        }).ToList();
        var rawValueDisplayHits = Py.Sorted(verifiedRawDisplays.Items(), item => (object?)new PyTuple(item.Key, item.Value)).Select(item =>
        {
            var tuple = (PyTuple)item.Key;
            return (object?)PyDict.Of(("key", tuple[0]), ("exiv2_pt", tuple[1]), ("metadatasharp_pt", tuple[2]), ("raw_value", tuple[3]),
                ("occurrences", item.Value),
                ("rule", "The pinned Exiv2 -Pkycv and MetadataSharp dump report the same raw value with the same type and count; only -pt presentation text differs."));
        }).ToList();
        var rowRules = Py.Sorted(rowRuleHits.Items(), item => (object?)new PyTuple(item.Key, item.Value)).Select(item =>
        {
            var tuple = (PyTuple)item.Key;
            return (object?)PyDict.Of(("file", tuple[0]), ("key", tuple[1]), ("type", tuple[2]), ("count", tuple[3]), ("occurrences", item.Value),
                                      ("rule", tuple[4]));
        }).ToList();
        var paddingFormatRule = PyDict.Of(
            ("occurrences", acceptedPadding.Total()),
            ("types", new PyDict(Py.Sorted(acceptedPadding, pair => (object?)new PyTuple(pair.Key, pair.Value)))),
            ("rule", "Trailing space/NUL padding is ignored only for ASCII fields and EXIF UserComment; internal whitespace and all non-text values remain exact."));
        var lineBoundaryRule = PyDict.Of(
            ("occurrences", acceptedLineTruncations.Total()),
            ("types", new PyDict(Py.Sorted(acceptedLineTruncations, pair => (object?)new PyTuple(pair.Key, pair.Value)))),
            ("rule", "For ASCII and EXIF UserComment text only, a local value may continue after the exact pinned -pt text with a line break, including when it ends there; the complete local text is retained. No other prefix or value difference is accepted."));
        return PyDict.Of(
            ("kind", "TagPrint"), ("totals", totals.Copy()), ("failure_kinds", new PyDict(Py.Sorted(failureKinds, pair => (object?)new PyTuple(pair.Key, pair.Value)))),
            ("difference_types", new PyDict(Py.Sorted(differenceTypes, pair => (object?)new PyTuple(pair.Key, pair.Value)))),
            ("mismatch_groups", groupedDifferences),
            ("display_rule_hits", displayRuleHits),
            ("name_rule_hits", nameRuleHits),
            ("type_rule_hits", typeRuleHits),
            ("raw_value_display_hits", rawValueDisplayHits),
            ("row_rule_hits", rowRules),
            ("raw_display_probes", rawDisplayProbes.Cast<object?>().ToList()),
            ("padding_format_rule", paddingFormatRule),
            ("line_boundary_rule", lineBoundaryRule),
            ("value_diff_by_key", valueDiffByKey),
            ("refusals", refusals.Concat(namedRefusals).ToList()),
            ("named_refusals", namedRefusals),
            ("formats", new PyDict(Py.Sorted(formats, pair => (object?)pair.Key).Select(pair => new KeyValuePair<object, object?>(pair.Key, pair.Value.Copy())))),
            ("exceptions", exceptions), ("tag_exceptions", tagExceptions),
            ("raw_only_tag_exceptions", rawOnlyExceptions), ("differing", differing));
    }

    /// <summary>The text report the step prints.</summary>
    public static string TextReport(PyDict report)
    {
        var totals = report.Dict("totals");
        var lines = new List<string>
        {
            $"{Title} against pinned Exiv2 -pt",
            $"  files: {Py.Str(totals.Get("files", 0L))}; matched rows: {Py.Str(totals.Get("matched_rows", 0L))}; failed files: {Py.Str(totals.Get("failed_files", 0L))}",
        };
        foreach (var (formatName, value) in report.Dict("formats"))
        {
            var counts = (PyDict)value!;
            lines.Add($"  {formatName}: {Py.Str(counts.Get("matched_rows", 0L))} rows matched / {Py.Str(counts["files"])} files ({Py.Str(counts.Get("failed_files", 0L))} failed)");
        }
        foreach (PyDict entry in report.List("exceptions").Cast<PyDict>()) lines.Add($"  named missing-input exception: {entry["file"]}: {entry["reason"]}");
        foreach (PyDict entry in report.List("tag_exceptions").Cast<PyDict>()) lines.Add($"  named tag exception: {entry["key"]}: {entry["reason"]}");
        var paddingRule = report.Dict("padding_format_rule");
        lines.Add($"  padding-format rule: {Py.Str(paddingRule["occurrences"])} rows across {Py.Str(paddingRule["types"])}: {paddingRule["rule"]}");
        var lineBoundaryRule = report.Dict("line_boundary_rule");
        lines.Add($"  line-boundary text rule: {Py.Str(lineBoundaryRule["occurrences"])} rows across {Py.Str(lineBoundaryRule["types"])}: {lineBoundaryRule["rule"]}");
        foreach (PyDict entry in report.List("display_rule_hits").Cast<PyDict>())
            lines.Add($"  display rule: {entry["key"]} {Py.Repr(entry["exiv2"])} => {Py.Repr(entry["metadatasharp"])} ({Py.Str(entry["occurrences"])} rows): {entry["rule"]}");
        foreach (PyDict entry in report.List("row_rule_hits").Cast<PyDict>())
            lines.Add($"  row rule: {entry["file"]} {entry["key"]} [{entry["type"]} {Py.Str(entry["count"])}] ({Py.Str(entry["occurrences"])} rows): {entry["rule"]}");
        foreach (PyDict entry in report.List("raw_only_tag_exceptions").Cast<PyDict>())
            lines.Add($"  named raw-only tag exception: {entry["key"]} ({Py.Str(entry["occurrences"])} rows): {entry["reason"]}");
        foreach (PyDict entry in report.List("mismatch_groups").Cast<PyDict>())
        {
            string examples = string.Join("; ", entry.List("examples").Cast<PyDict>().Select(item => $"{item["difference"]} ({Py.Str(item["occurrences"])})"));
            lines.Add($"  {entry["category"]} {entry["key"]} [{entry["type"]}]: {Py.Str(entry["occurrences"])} rows; {examples}");
        }
        foreach (PyDict entry in report.List("refusals").Cast<PyDict>())
            lines.Add($"  refusal {entry["file"]}: {entry["kind"]} {Py.Str(entry.Get("status", ""))} {Py.Str(entry.Get("detail", ""))} {Py.Str(entry.Get("reason", ""))}");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Require the pinned corpus commit and the pinned reference version.</summary>
    public static void VerifyOracle(string tree, string executable)
    {
        string revision, version;
        try
        {
            revision = PyText.Strip(PyProcess.Run(["git", "-C", PyPath.Normalize(tree), "rev-parse", "HEAD"], timeout: 30L, check: true, strict: true).Stdout);
            var lines = PyText.SplitLines(PyProcess.Run([executable, "--version"], timeout: 30L, check: true, strict: true).Stdout);
            if (lines.Count == 0) throw new IndexOutOfRangeException("list index out of range");
            version = lines[0];
        }
        catch (Exception error) when (error is PyOSError or PyTimeoutExpired or PyCalledProcessError or IndexOutOfRangeException)
        {
            throw new TagPrintError($"cannot verify pinned Exiv2 oracle: {error.Message}");
        }
        if (revision != PinnedExiv2Commit) throw new TagPrintError($"Exiv2 source checkout is {revision}, expected {PinnedExiv2Commit}");
        if (!version.Contains("0.28.9", StringComparison.Ordinal)) throw new TagPrintError($"Exiv2 binary is not version 0.28.9: {version}");
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("tag-print", "Compare MetadataSharp's tag output with pinned exiv2 -pt.")
            .Add("--tree", required: true, help: "pinned Exiv2 checkout")
            .Add("--fixtures", ArgAction.Append, help: "extra files to compare, against the pinned tool's live -pt output (the pinned RAW corpus, tools/parity/raw-corpus.json)")
            .Add("--exiv2", required: true, help: "pinned Exiv2 executable")
            .Add("--dump-command", required: true, help: "MetadataSharp.Dump command")
            .Add("--out", @default: Repo.Path("artifacts/parity/tag-print"))
            .Add("--annotate", ArgAction.StoreTrue);
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        try
        {
            VerifyOracle(args.Str("tree"), args.Str("exiv2"));
            var (candidates, unmatched) = Corpus.DiscoverExiv2(args.Str("tree"));
            if (args.List("fixtures").Count > 0) candidates = candidates.Concat(Corpus.DiscoverGenerated(args.List("fixtures").ToArray())).ToList();
            var paths = candidates.Select(candidate => candidate.Path).ToList();
            var dump = RunDump(Shlex.Split(args.Str("dump_command")), paths);
            var expectedPaths = new HashSet<string>(paths.Select(PyPath.Normalize), StringComparer.Ordinal);
            var unexpectedPaths = dump.Keys.Cast<string>().Where(path => !expectedPaths.Contains(path)).ToList();
            if (unexpectedPaths.Count > 0)
                throw new TagPrintError("dump returned unrequested files: " + string.Join(", ", Py.Sorted(unexpectedPaths).Take(5)));
            var report = BuildReport(candidates, [args.Str("exiv2")], dump, unmatched);
            string outDirectory = args.Str("out");
            Directory.CreateDirectory(outDirectory);
            File.WriteAllText(PyPath.Join(outDirectory, "tag-print-report.json"), PyJson.Dumps(report, indent: 2, sortKeys: true) + "\n");
            string text = TextReport(report);
            File.WriteAllText(PyPath.Join(outDirectory, "tag-print-report.txt"), text);
            Console.Out.Write(text);
            if (args.Flag("annotate")) EmitAnnotation(Summary(report));
            return Py.Truthy(report.Dict("totals").Get("failed_files")) ? 1 : 0;
        }
        catch (Exception failure) when (failure is TagPrintError or CorpusError)
        {
            Out.Error($"::error title={Title} harness::{failure.Message}");
            return 2;
        }
    }
}
