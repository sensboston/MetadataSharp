using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// Report interpreted MakerNote coverage and gate groups completed against the pinned output.
//
// The report compares the interpreted text independently of raw key/value parity. This gate is for a
// finished Exif.<Group> only: every value the reference translates must have the same rendering, and raw
// key/value differences in that group are rejected unless named as a documented exception.
//
// It reads the parity-report.json of tools/MetadataSharp.Parity, which applies the same verdicts itself
// under --require-all-maker-notes; this command re-derives them from the report alone.
//
// (Formerly tools/parity/makernote_render_gate.py; the arguments, output and annotations are unchanged.)
internal static class MakerNoteRenderGate
{
    public const string MatchValue = "match-value";
    public const string MatchKey = "match-key";
    public const string ValueDiff = "value-diff";
    public const string MissingKey = "missing-key";
    public const string ExtraKey = "extra-key";
    public const string NotRead = "not-read";
    public const string MatchInterpreted = "match-interpreted";
    public const string InterpretedDiff = "interpreted-diff";
    public const string NotInterpretedTranslated = "not-interpreted-translated";

    // Interpretation exception: none of the lens codes of the notes is excepted, because the lens tables are
    // part of this project; a key listed here is one this project knowingly leaves to the raw value.
    public static readonly OrderedDictionary<string, string> DocumentedExceptions = new(StringComparer.Ordinal);

    // The parser deliberately refuses these two damaged/over-budget inputs. These are exact pinned corpus
    // files and exact refusal codes, not an exemption for the MakerNote family or for similar future files.
    // The raw Canon decoder retains these named fields, but the pinned Exiv2 corpus output does not publish
    // them as keys. Categories (0x0023) and PictureStylePC (0x4009) are explicitly disabled in Exiv2's Canon
    // tag table; FilterInfo (0x4024) is not surfaced for the pinned records. Keep their original raw bytes
    // available rather than silently dropping keys to mimic that omission.
    public static readonly OrderedDictionary<string, string> DocumentedRawExtraKeys = new(StringComparer.Ordinal)
    {
        ["Exif.Canon.Categories"] = "Exiv2 0.28.9 disables Canon tag 0x0023 in canonmn_int.cpp",
        ["Exif.Canon.PictureStylePC"] = "Exiv2 0.28.9 disables Canon tag 0x4009 in canonmn_int.cpp",
        ["Exif.Canon.FilterInfo"] = "Exiv2 0.28.9 does not surface Canon tag 0x4024 in these pinned records",
    };

    public static readonly OrderedDictionary<string, (string Code, string Reason)> DocumentedFileRefusals = new(StringComparer.Ordinal)
    {
        ["exiv2:issue_2006_poc.tiff"] = ("InvalidExif", "the EXIF structure is malformed"),
        ["exiv2:poc_2542.exv"] = ("AmbiguousMetadata", "the file has ambiguous EXIF metadata"),
        ["exiv2:issue_1706_poc.exv"] = ("LimitExceeded", "the EXIF directory exceeds configured reader limits"),
        ["exiv2:issue_1920_poc.tiff"] = ("InvalidExif", "the EXIF structure is malformed"),
        ["exiv2:issue_ghsa_h9x9_4f77_336w_poc.exv"] = ("InvalidExif", "the EXIF structure is malformed"),
        ["exiv2:IMGP0006-min.jpg"] = ("InvalidJpeg", "the JPEG structure is malformed"),
        ["exiv2:issue_ghsa_5p8g_9xf3_gfrr_poc.exv"] = ("UnsupportedFeature", "the GHSA proof-of-concept uses an unsupported encrypted Nikon MakerNote layout"),
        ["exiv2:issue_ghsa_5p8g_9xf3_gfrr_poc.webp"] = ("UnsupportedFeature", "the GHSA proof-of-concept uses an unsupported encrypted Nikon MakerNote layout"),
    };

    // The reader exposes unnamed raw tags as 0xNNNN keys. Exiv2 has no corresponding named key, so these are
    // informative extras, not discrepancies in a key that both tools print.
    public static readonly Regex OpaqueNumericKey = new(@"^Exif\.[A-Za-z0-9]+\.0x[0-9a-fA-F]{4,8}\z", RegexOptions.CultureInvariant);
    public static readonly IReadOnlySet<string> StandardExifGroups = new HashSet<string>(StringComparer.Ordinal)
    {
        "Exif.Image", "Exif.Photo", "Exif.Thumbnail", "Exif.GPSInfo", "Exif.Iop",
    };
    public static readonly Regex SubImageGroup = new(@"^Exif\.SubImage[0-9]+\z", RegexOptions.CultureInvariant);

    private static bool IsVendorGroup(string group) => !StandardExifGroups.Contains(group) && !SubImageGroup.IsMatch(group);

    private static PyDict Families(object? report) => (PyDict)PyOps.Get(report, "by_family", new PyDict())!;

    private static PyDict InterpretedFamilies(object? report) => (PyDict)PyOps.Get(PyOps.Get(report, "interpreted", new PyDict()), "by_family", new PyDict())!;

    private static List<string> AllGroups(object? report) =>
        Families(report).Keys.Concat(InterpretedFamilies(report).Keys).Cast<string>().Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Every non-standard EXIF family printed by the dump is a vendor MakerNote group. This discovers groups
    /// from raw and interpreted outcomes alike, including vendors with no translated fields yet, so adding a
    /// corpus MakerNote cannot silently leave it outside the gate.
    /// </summary>
    public static List<string> MakerNoteGroups(object? report) =>
        Py.Sorted(AllGroups(report).Where(group => group.StartsWith("Exif.", StringComparison.Ordinal) && IsVendorGroup(group)));

    private static long Count(object? counts, string key) => PyOps.Get(counts, key, 0L) switch
    {
        long value => value,
        int value => value,
        bool value => value ? 1 : 0,
        var other => throw new PyTypeError($"unsupported operand type(s) for +: 'int' and '{Py.TypeName(other)}'"),
    };

    public static PyDict GroupMetrics(object? report, string group)
    {
        object? raw = Families(report).Get(group, new PyDict());
        object? interpreted = InterpretedFamilies(report).Get(group, new PyDict());
        long equal = Count(interpreted, MatchInterpreted);
        long different = Count(interpreted, InterpretedDiff);
        long untranslated = Count(interpreted, NotInterpretedTranslated);
        return PyDict.Of(
            ("keys_found", Count(raw, MatchValue) + Count(raw, MatchKey) + Count(raw, ValueDiff)),
            ("raw_values_equal", Count(raw, MatchValue)),
            ("interpreted_keys_found", equal + different + untranslated),
            ("interpreted_values_equal", equal),
            ("unrendered", untranslated),
            ("interpreted_different", different),
            ("missing_keys", Count(raw, MissingKey)),
            ("extra_keys", Count(raw, ExtraKey)),
            ("not_read", Count(raw, NotRead)),
            ("raw_value_different", Count(raw, ValueDiff) + Count(raw, MatchKey)));
    }

    public static List<(string Group, PyDict Metrics)> Inventory(object? report)
    {
        var rows = AllGroups(report).Where(group => group.StartsWith("Exif.", StringComparison.Ordinal))
            .Select(group => (Group: group, Metrics: GroupMetrics(report, group)))
            .Where(row => row.Metrics.Long("interpreted_keys_found") != 0 || row.Metrics.Long("keys_found") != 0
                          || row.Metrics.Long("missing_keys") != 0 || row.Metrics.Long("not_read") != 0);
        return Py.Sorted(rows, row => new PyTuple(-row.Metrics.Long("unrendered") - row.Metrics.Long("interpreted_different"), row.Group));
    }

    private static string Key(object? row) => PyOps.Get(row, "key", "") as string
                                               ?? throw new PyAttributeError($"'{Py.TypeName(PyOps.Get(row, "key", ""))}' object has no attribute 'startswith'");

    public static (bool Passed, string Message) Evaluate(object? report, IReadOnlyList<string> groups, OrderedDictionary<string, string>? exceptions = null)
    {
        if (PyOps.Get(report, "per_file") is not List<object?> files) return (false, "parity report has no per_file rows");
        exceptions ??= new OrderedDictionary<string, string>(StringComparer.Ordinal);
        var failures = new List<string>();
        var output = new List<string> { "MakerNote interpreted parity gate (Exiv2 0.28.9):" };
        foreach (string group in groups)
        {
            var metrics = GroupMetrics(report, group);
            output.Add($"  {group}: interpreted keys found/equal={metrics["interpreted_keys_found"]}/{metrics["interpreted_values_equal"]}; "
                       + $"raw keys found/equal={metrics["keys_found"]}/{metrics["raw_values_equal"]}");
            if (metrics.Long("missing_keys") != 0 || metrics.Long("raw_value_different") != 0)
                failures.Add($"{group}: raw parity has missing={metrics["missing_keys"]}, value-diff={metrics["raw_value_different"]}");

            long rowsSeen = 0, opaqueExtras = 0;
            var refusedRows = new Counter();
            var keyExceptionRows = new Counter();
            var rawExtraRows = new Counter();
            foreach (object? file in files)
            {
                if (Py.Truthy(PyOps.Get(file, "rows_dropped", 0L)))
                {
                    // The caller requests a high row limit for the corpus; fail closed if any rows were cut.
                    failures.Add($"{group}: {Py.Str(PyOps.Get(file, "label"))}: report dropped rows");
                    continue;
                }
                object? label = PyOps.Get(file, "label", "");
                foreach (object? row in PyOps.Iterate(PyOps.Get(file, "rows", new List<object?>())))
                {
                    string key = Key(row);
                    if (!key.StartsWith(group + ".", StringComparison.Ordinal)) continue;
                    rowsSeen++;
                    object? outcome = PyOps.Get(row, "outcome");
                    if (Py.Eq(outcome, ExtraKey))
                    {
                        if (OpaqueNumericKey.IsMatch(key)) opaqueExtras++;
                        else if (DocumentedRawExtraKeys.ContainsKey(key)) rawExtraRows.Add(key, 1);
                        else failures.Add($"{group}: {Py.Str(label)} {key}: raw outcome {Py.Str(outcome)}");
                        continue;
                    }
                    if (Py.Eq(outcome, NotRead))
                    {
                        if (label is string name && DocumentedFileRefusals.TryGetValue(name, out var refusal)
                            && Py.Eq(PyOps.Get(file, "detail"), refusal.Code) && Py.Eq(PyOps.Get(file, "status"), "refused"))
                        {
                            refusedRows.Add(name, 1);
                            continue;
                        }
                        failures.Add($"{group}: {Py.Str(label)} {key}: raw outcome {Py.Str(outcome)} ({Py.Str(PyOps.Get(file, "detail", ""))})");
                        continue;
                    }
                    if (exceptions.ContainsKey(key))
                    {
                        if (PyOps.Get(row, "translated") is true) keyExceptionRows.Add(key, 1);
                        continue;
                    }
                    if (Py.Eq(outcome, MissingKey) || Py.Eq(outcome, ValueDiff) || Py.Eq(outcome, MatchKey))
                        failures.Add($"{group}: {Py.Str(label)} {key}: raw outcome {Py.Str(outcome)}");
                    if (PyOps.Get(row, "translated") is true && !Py.Eq(PyOps.Get(row, "interpreted"), MatchInterpreted))
                    {
                        failures.Add($"{group}: {Py.Str(label)} {key}: interpreted={Py.Repr(PyOps.Get(row, "interpreted"))}, "
                                     + $"ours={Py.Repr(PyOps.Get(row, "our_interpreted", PyOps.Get(row, "our_value", "")))}, "
                                     + $"Exiv2={Py.Repr(PyOps.Get(row, "their_interpreted", ""))}");
                    }
                }
            }
            if (rowsSeen == 0) failures.Add($"{group}: no report rows were available (check row limit and group spelling)");
            if (opaqueExtras != 0) output.Add($"    retained numeric unknown-tag rows: {opaqueExtras} (no Exiv2 named key exists)");
            foreach (var (label, count) in Py.Sorted(refusedRows.Items(), pair => pair.Key))
                output.Add($"    documented reader refusal: {label}: {count} MakerNote rows; {DocumentedFileRefusals[(string)label].Reason}");
            foreach (var (key, count) in Py.Sorted(rawExtraRows.Items(), pair => pair.Key))
                output.Add($"    retained documented raw extra: {key}: {count} rows; {DocumentedRawExtraKeys[(string)key]}");
            foreach (var (key, reason) in exceptions)
            {
                if (key.StartsWith(group + ".", StringComparison.Ordinal))
                    output.Add($"    documented interpretation exception: {key}: {keyExceptionRows.CountOf(key)} translated rows; {reason}");
            }
        }
        if (failures.Count > 0)
        {
            output.Add("FAIL");
            output.AddRange(failures.Select(failure => "  " + failure));
            return (false, string.Join("\n", output) + "\n");
        }
        output.Add("PASS");
        return (true, string.Join("\n", output) + "\n");
    }

    private static string Encode(object? value, bool sortKeys) => Out.Compressed(PyJson.Compact(value, sortKeys: sortKeys), 9);

    public static string EncodeInventory(List<(string Group, PyDict Metrics)> rows) =>
        Encode(rows.Select(row =>
        {
            var entry = PyDict.Of(("group", row.Group));
            foreach (var (key, value) in row.Metrics) entry[key] = value;
            return (object?)entry;
        }).ToList(), sortKeys: true);

    /// <summary>
    /// Attribute every MakerNote not-read row to its exact file refusal and reason. not-read is not a decode
    /// verdict: it is a reference key for a file our reader refused as a whole. Keep this roll-up separate from
    /// the key inventory, and retain unexplained rows as explicitly unexplained instead of silently classifying
    /// them as an allowed refusal.
    /// </summary>
    public static List<PyDict> UnreadAttribution(object? report)
    {
        var counts = new Counter();
        foreach (object? file in PyOps.Iterate(PyOps.Get(report, "per_file", new List<object?>())))
        {
            object? label = PyOps.Get(file, "label", "");
            object? status = PyOps.Get(file, "status", "");
            object? detail = PyOps.Get(file, "detail", "");
            bool documented = label is string name && DocumentedFileRefusals.TryGetValue(name, out var refusal) && Py.Eq(refusal.Code, detail) && Py.Eq(status, "refused");
            string reason = documented ? DocumentedFileRefusals[(string)label!].Reason : "UNDOCUMENTED refusal or partial-read outcome";
            foreach (object? row in PyOps.Iterate(PyOps.Get(file, "rows", new List<object?>())))
            {
                if (!Py.Eq(PyOps.Get(row, "outcome"), NotRead)) continue;
                string key = Key(row);
                string group = key.StartsWith("Exif.", StringComparison.Ordinal) ? string.Join(".", key.Split('.').Take(2)) : "";
                if (group.Length > 0 && IsVendorGroup(group))
                    counts.Add(new PyTuple(group, PyOps.Hashable(label), PyOps.Hashable(status), PyOps.Hashable(detail), reason), 1);
            }
        }
        return Py.Sorted(counts.Items(), pair => pair.Key).Select(pair =>
        {
            var key = (PyTuple)pair.Key;
            return PyDict.Of(("group", key[0]), ("file", key[1]), ("status", key[2]), ("code", key[3]), ("reason", key[4]), ("rows", pair.Value));
        }).ToList();
    }

    public static string EncodeUnreadAttribution(List<PyDict> rows) => Encode(rows.Cast<object?>().ToList(), sortKeys: true);

    /// <summary>Compact, per-key raw and interpreted gap samples suitable for one CI annotation.</summary>
    public static OrderedDictionary<string, List<PyDict>> GapDetails(object? report, IReadOnlyList<string> groups, int limit = 30)
    {
        var gaps = new OrderedDictionary<string, Counter>(StringComparer.Ordinal);
        foreach (string group in groups) gaps.TryAdd(group, new Counter());
        var examples = new Dictionary<PyTuple, List<object?>>();
        foreach (object? file in PyOps.Iterate(PyOps.Get(report, "per_file", new List<object?>())))
        {
            object? label = PyOps.Get(file, "label", "");
            foreach (object? row in PyOps.Iterate(PyOps.Get(file, "rows", new List<object?>())))
            {
                string key = Key(row);
                string? group = groups.FirstOrDefault(candidate => key.StartsWith(candidate + ".", StringComparison.Ordinal));
                if (group is null) continue;
                string outcome = Py.Str(PyOps.Get(row, "outcome", ""));
                bool translatedGap = PyOps.Get(row, "translated") is true && !Py.Eq(PyOps.Get(row, "interpreted"), MatchInterpreted);
                bool rawGap = outcome is MissingKey or ValueDiff or MatchKey or NotRead || (outcome == ExtraKey && !OpaqueNumericKey.IsMatch(key));
                if (!translatedGap && !rawGap) continue;
                var signature = new PyTuple(group, key, outcome, Py.Str(PyOps.Get(row, "interpreted", "")),
                                            Py.Str(PyOps.Get(row, "our_interpreted", "")), Py.Str(PyOps.Get(row, "their_interpreted", "")),
                                            Py.Str(PyOps.Get(row, "our_type", "")), Py.Str(PyOps.Get(row, "our_count", "")), Py.Str(PyOps.Get(row, "our_value", "")),
                                            Py.Str(PyOps.Get(row, "their_type", "")), Py.Str(PyOps.Get(row, "their_count", "")), Py.Str(PyOps.Get(row, "their_value", "")));
                gaps[group].Add(signature, 1);
                if (!examples.TryGetValue(signature, out var labels)) examples[signature] = labels = [];
                if (Py.Truthy(label) && labels.Count < 3 && !labels.Any(known => Py.Eq(known, label))) labels.Add(label);
            }
        }
        var result = new OrderedDictionary<string, List<PyDict>>(StringComparer.Ordinal);
        foreach (var (group, counter) in gaps)
        {
            var rows = counter.Items().Select(pair =>
            {
                var signature = (PyTuple)pair.Key;
                return PyDict.Of(("key", signature[1]), ("outcome", signature[2]), ("interpretation", signature[3]), ("count", pair.Value),
                                 ("ours", signature[4]), ("exiv2", signature[5]), ("our_type", signature[6]), ("our_count", signature[7]),
                                 ("our_value", signature[8]), ("exiv2_type", signature[9]), ("exiv2_count", signature[10]),
                                 ("exiv2_value", signature[11]), ("files", examples[signature]));
            }).ToList();
            rows = Py.Sorted(rows, row => new PyTuple(!Py.Eq(row["interpretation"], InterpretedDiff), -row.Long("count"), row["key"]));
            result[group] = rows.Take(limit).ToList();
        }
        return result;
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("makernote-render-gate", "Report interpreted MakerNote coverage and gate groups completed against the pinned output.")
            .Add("report")
            .Add("--require", ArgAction.Append, help: "require interpreted and raw parity for a completed group; repeatable")
            .Add("--require-all-maker-notes", ArgAction.StoreTrue,
                 help: "also require raw and interpreted parity for every non-standard EXIF group present in the report")
            .Add("--inventory", ArgAction.StoreTrue, help: "emit the corpus group inventory as one notice");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        object? report;
        try
        {
            report = PyJson.Loads(PyFile.ReadText(PyPath.Normalize(args.Str("report"))));
        }
        catch (Exception error) when (error is PyOSError or JsonDecodeError)
        {
            Out.Print($"MakerNote parity gate could not read report: {error.Message}");
            return 1;
        }
        if (args.Flag("inventory"))
        {
            var rows = Inventory(report);
            Out.Print("MakerNote interpreter inventory (largest full interpretation gap first):");
            foreach (var (group, metrics) in rows)
            {
                long gap = metrics.Long("unrendered") + metrics.Long("interpreted_different");
                Out.Print($"  {group}: translated keys found/equal={metrics["interpreted_keys_found"]}/{metrics["interpreted_values_equal"]}; "
                          + $"gap={gap}; raw keys found/equal={metrics["keys_found"]}/{metrics["raw_values_equal"]}");
            }
            Out.Print($"::notice title=MakerNote interpreter inventory::{EncodeInventory(rows)}");
            var unread = UnreadAttribution(report);
            Out.Print("MakerNote not-read attribution (reference rows on refused files; not missing decodes):");
            foreach (var entry in unread)
            {
                Out.Print($"  {Py.Str(entry["group"])}: {entry["rows"]} rows in {Py.Str(entry["file"])} "
                          + $"[{Py.Str(entry["status"])}/{Py.Str(entry["code"])}]: {Py.Str(entry["reason"])}");
            }
            Out.Print($"::notice title=MakerNote not-read attribution::{EncodeUnreadAttribution(unread)}");
        }
        var require = args.List("require");
        if (args.Flag("require_all_maker_notes")) require = require.Concat(MakerNoteGroups(report)).Distinct(StringComparer.Ordinal).ToList();
        if (require.Count == 0) return 0;
        var (passed, message) = Evaluate(report, require, DocumentedExceptions);
        if (!passed)
        {
            int at = message.IndexOf("FAIL\n", StringComparison.Ordinal);
            var failureLines = PyText.SplitLines(at < 0 ? message : message[(at + 5)..]);
            var failuresByGroup = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string line in failureLines)
            {
                string group = PyText.Strip(line).Split(':', 2)[0];
                if (!failuresByGroup.TryGetValue(group, out var list)) failuresByGroup[group] = list = [];
                list.Add(PyText.Strip(line));
            }
            foreach (var (group, failures) in failuresByGroup)
            {
                for (int start = 0; start < failures.Count; start += 4)
                {
                    var chunk = failures.Skip(start).Take(4).Cast<object?>().ToList();
                    Out.Print($"::error title=MakerNote parity failures {group} {start / 4 + 1}::{Encode(chunk, sortKeys: false)}");
                }
            }
        }
        Console.Out.Write(message);
        foreach (var (group, rows) in GapDetails(report, require))
        {
            if (rows.Count == 0) continue;
            // GitHub truncates individual workflow-command annotations. Bound long values and keep each
            // compressed notice comfortably below the limit so the inventory remains decodable.
            var compact = rows.Select(row =>
            {
                var bounded = new PyDict();
                foreach (var (key, value) in row) bounded[key] = value is string text && Py.Len(text) > 240 ? PyText.Head(text, 240) + "…" : value;
                return (object?)bounded;
            }).ToList();
            for (int start = 0; start < compact.Count; start += 4)
            {
                var chunk = compact.Skip(start).Take(4).ToList();
                Out.Print($"::notice title=MakerNote actionable gaps {group} {start / 4 + 1}::{Encode(chunk, sortKeys: true)}");
            }
        }
        return passed ? 0 : 1;
    }
}
