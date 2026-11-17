using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>One compared key: the direction, the file, the key, the outcome and both sides' printed values.</summary>
internal sealed record SyncRow(string Direction, string File, string Key, string Outcome, List<string>? Theirs, List<string>? Ours);

// What ExifToXmp and XmpToExif produce, held against the conversion of the pinned exiv2. Formerly
// tools/reference/sync_parity.py.
//
// A separate check from key parity. The tool converts when it writes and when it reads an XMP sidecar, and that is
// what is used here, on the JPEG files of the pinned corpus:
//
//   1. Each file is copied and "exiv2 -d xi" takes the XMP and the IPTC out of the copy, so that EXIF is the only
//      source.
//   2. "exiv2 -eX" writes NAME.xmp from the copy: the tool's EXIF to XMP.
//   3. The fixture generator (sync-parity) writes NAME.ours.xmp, this library's ExifToXmp of the same copy, and
//      NAME.back.jpg, a file whose EXIF block is this library's XmpToExif of the tool's NAME.xmp.
//   4. EXIF to XMP: "exiv2 -PXkv" of NAME.xmp against the same of NAME.ours.xmp. XMP to EXIF: "exiv2 -PEkv" of
//      NAME.xmp, which is the tool's own conversion of its sidecar back to EXIF, against the same of NAME.back.jpg.
//
// Both sides of a comparison are printed by the tool, so what is compared is which keys there are and the value the
// tool prints for each.
//
// A key is "same" when both sides print the same value, and "form" when the values are the same items written apart
// differently (one item per element here, one text with spaces there) or the same rationals over another
// denominator. Every other outcome ("differs", "character code" for the same comment under another character code,
// "tool only", "tool only (empty)", "ours only") has to be one this file documents in Documented, with the reason; an
// outcome that is not documented fails the check. The counts of every outcome are printed either way.
//
// A file this library refuses to read has no conversion to compare: it is left out of that direction and counted
// under the code of the refusal. That is a matter of the reader, not of the conversion, and the check says nothing
// on it.
//
// Usage: sync-parity CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."
internal static class SyncParity
{
    public const long MaxBytes = 32 * 1024 * 1024;
    public const string ToXmp = "EXIF to XMP", ToExif = "XMP to EXIF";

    // Keys a JPEG file has for its own structure, which a converted sidecar has no use for.
    public static readonly HashSet<string> Structure = new(StringComparer.Ordinal) { "Exif.Image.ExifTag", "Exif.Image.GPSTag", "Exif.Photo.InteroperabilityTag" };

    // (direction, key, outcome) -> why the difference is intended. A key may end in * for a prefix.
    public static readonly OrderedDictionary<(string Direction, string Key, string Outcome), string> Documented = new();

    public static void Document(string direction, string key, string outcomes, string reason)
    {
        foreach (string outcome in outcomes.Split(',')) Documented[(direction, key, PyText.Strip(outcome))] = reason;
    }

    static SyncParity() => DocumentDefaults();

    /// <summary>The documented differences as the script declared them.</summary>
    public static void DocumentDefaults()
    {
        foreach (string direction in new[] { ToXmp, ToExif })
            Document(direction, "*", "tool only (empty)", "an empty text is not converted: the tool writes the empty value, this library writes nothing");
        foreach (string name in new[] { "CFAPattern", "OECF", "SpatialFrequencyResponse", "DeviceSettingDescription" })
        {
            Document(ToXmp, $"Xmp.exif.{name}*", "tool only", "not in the mapping table: the structure is not converted (a difference approved with the API)");
            Document(ToExif, $"Exif.Photo.{name}", "tool only", "not in the mapping table: the structure is not converted (a difference approved with the API)");
        }
        Document(ToXmp, "Xmp.exif.GPSTimeStamp", "differs",
                 "GPS time is UTC: written with Z and with the fraction the EXIF value has; the tool writes nine zeros and no zone");
        foreach (string name in new[] { "Xmp.dc.subject", "Xmp.xmpMM.*" })
            Document(ToXmp, name, "tool only", "not a property any EXIF tag is converted to: the copy still held XMP or IPTC after exiv2 -d xi");
        foreach (string name in new[] { "SubSecTime", "SubSecTimeOriginal", "SubSecTimeDigitized" })
        {
            Document(ToExif, $"Exif.Photo.{name}", "differs, ours only",
                     "the digits of the fraction as XMP has them (.06 gives 06, .00 gives 00); the tool writes a count of "
                     + "nanoseconds without leading zeros (.06 gives 60000000) and nothing for a fraction of zero");
        }
        Document(ToExif, "Exif.Photo.UserComment", "character code", "text that is ASCII is written under the ASCII code; the tool writes every comment under UNICODE");
        Document(ToXmp, "Xmp.exif.FileSource", "tool only", "an EXIF value of a count the tag is not defined with (FileSource of four bytes) is not converted, and noted");
        foreach (string name in new[] { "Exif.Photo.FileSource", "Exif.Image.BitsPerSample" })
        {
            Document(ToExif, name, "tool only",
                     "a count the EXIF tag is not defined with (FileSource of four, BitsPerSample other than three) is not written, and noted");
        }
    }

    /// <summary>The reason an outcome is intended, or null.</summary>
    public static string? IsDocumented(string direction, string key, string outcome)
    {
        if (Documented.TryGetValue((direction, key, outcome), out string? reason)) return reason;
        foreach (var ((way, pattern, kind), text) in Documented)
        {
            if (way == direction && kind == outcome && pattern.EndsWith('*') && key.StartsWith(pattern[..^1], StringComparison.Ordinal)) return text;
        }
        return null;
    }

    /// <summary>key -> the values printed for it, in order, from a "-Pkv" print-out.</summary>
    public static OrderedDictionary<string, List<string>> Properties(string text, string prefix)
    {
        var found = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string line in PyText.SplitLines(text))
        {
            var parts = PyText.Split(line, 1);
            if (parts.Count == 0 || !parts[0].StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!found.TryGetValue(parts[0], out var values)) found[parts[0]] = values = [];
            values.Add(parts.Count > 1 ? PyText.Strip(parts[1]) : "");
        }
        return found;
    }

    private static readonly Regex Rational = new(@"^(-?\d+)/(\d+)\z", RegexOptions.CultureInvariant);
    private static readonly Regex Separators = new($"(?:,|{PyRe.Space})+", RegexOptions.CultureInvariant);
    private static readonly Regex Charset = new($@"\Gcharset=({PyRe.NonSpace}+){PyRe.Space}?", RegexOptions.CultureInvariant);
    private static readonly Regex EmptyValue = new($@"^(lang=""[^""]*""|charset={PyRe.NonSpace}+)?{PyRe.Space}*\z", RegexOptions.CultureInvariant);

    /// <summary>A rational in lowest terms, so that 54/1 and 540000/10000 are one number; any other item as it is.</summary>
    public static object Number(string item)
    {
        var match = Rational.Match(item);
        if (!match.Success || ToolRunner.ParseDigits(match.Groups[2].Value).IsZero) return item;
        return new Fraction(ToolRunner.ParseDigits(match.Groups[1].Value), ToolRunner.ParseDigits(match.Groups[2].Value));
    }

    /// <summary>The items of printed values, however they were kept apart and whatever a rational is divided by.</summary>
    public static List<object> Items(IEnumerable<string> values) =>
        values.SelectMany(value => Separators.Split(value)).Where(item => item.Length > 0).Select(Number).ToList();

    /// <summary>The character code the tool prints before a comment, and the values without it.</summary>
    public static (List<string> Codes, List<string> Texts) Coded(IEnumerable<string> values)
    {
        var codes = new List<string>();
        var texts = new List<string>();
        foreach (string value in values)
        {
            var match = Charset.Match(value);
            codes.Add(match.Success ? match.Groups[1].Value : "");
            texts.Add(match.Success ? value[match.Length..] : value);
        }
        return (codes, texts);
    }

    /// <summary>Whether the tool printed nothing but the form of a value: an empty text.</summary>
    public static bool Empty(IEnumerable<string> values) => values.All(value => EmptyValue.IsMatch(value));

    private static bool Same(List<string> left, List<string> right) => left.SequenceEqual(right, StringComparer.Ordinal);

    public static string Outcome(List<string>? theirs, List<string>? ours)
    {
        if (theirs is null) return "ours only";
        if (ours is null) return Empty(theirs) ? "tool only (empty)" : "tool only";
        if (Same(theirs, ours)) return "same";
        if (Items(theirs).SequenceEqual(Items(ours))) return "form";
        var (theirCodes, theirTexts) = Coded(theirs);
        var (ourCodes, ourTexts) = Coded(ours);
        if (Same(theirTexts, ourTexts) && !Same(theirCodes, ourCodes)) return "character code";
        return "differs";
    }

    /// <summary>(key, outcome, tool's values, our values) for every key of either side.</summary>
    public static List<(string Key, string Outcome, List<string>? Theirs, List<string>? Ours)> Compare(
        OrderedDictionary<string, List<string>> theirs, OrderedDictionary<string, List<string>> ours, IReadOnlySet<string>? ignore = null)
    {
        var rows = new List<(string, string, List<string>?, List<string>?)>();
        foreach (string key in theirs.Keys.Concat(ours.Keys.Where(key => !theirs.ContainsKey(key))))
        {
            if (ignore is not null && ignore.Contains(key)) continue;
            var theirValues = theirs.GetValueOrDefault(key);
            var ourValues = ours.GetValueOrDefault(key);
            rows.Add((key, Outcome(theirValues, ourValues), theirValues, ourValues));
        }
        return rows;
    }

    /// <summary>Copies the JPEG files, leaves EXIF as their only metadata, and has the tool write the sidecars.</summary>
    public static (OrderedDictionary<string, string> Names, Counter Skipped) Prepare(string corpus, string work, string exiv2, Runner runner)
    {
        work = PyPath.Normalize(work);
        PyFile.MakeDirs(work);
        var names = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        var skipped = new Counter();
        var sources = PyPath.RGlob(corpus).Where(path => File.Exists(path) && PyPath.Suffix(path).ToLowerInvariant() is ".jpg" or ".jpeg").ToList();
        foreach (string path in sources)
        {
            if (new FileInfo(path).Length > MaxBytes)
            {
                skipped.Add("too large");
                continue;
            }
            string name = $"s{names.Count + skipped.Total():00000}";
            string copy = PyPath.Join(work, name + ".jpg");
            PyShutil.CopyFile(path, copy);
            var (code, _, _) = runner([exiv2, "-d", "xi", copy]);
            if (code != 0)
            {
                File.Delete(copy);
                skipped.Add("not rewritten by the tool");
                continue;
            }
            (code, _, _) = runner([exiv2, "-eX", copy]);
            string sidecar = PyPath.Join(work, name + ".xmp");
            if (code != 0 && PyPath.Exists(sidecar)) File.Delete(sidecar);
            names[name] = path;
        }
        return (names, skipped);
    }

    /// <summary>The properties the tool prints for a file; null when there is no such file or the tool fails on it.</summary>
    public static OrderedDictionary<string, List<string>>? Printed(string exiv2, string mode, string path, string prefix, Runner runner)
    {
        if (!PyPath.Exists(path)) return null;
        var (code, output, _) = runner([exiv2, mode, path]);
        var found = Properties(output, prefix);
        // The tool exits with 253 for a file without that kind of metadata.
        if (code != 0 && found.Count == 0) return code == 253 ? new OrderedDictionary<string, List<string>>(StringComparer.Ordinal) : null;
        return found;
    }

    /// <summary>(file, stage) -> (status, text) from the ours.tsv the fixture generator wrote.</summary>
    public static OrderedDictionary<(string File, string Stage), (string Status, string Text)> Statuses(string work)
    {
        var found = new OrderedDictionary<(string, string), (string, string)>();
        string path = PyPath.Join(work, "ours.tsv");
        if (PyPath.Exists(path))
        {
            foreach (string line in PyText.SplitLines(PyFile.ReadText(path)))
            {
                var fields = line.Split('\t');
                if (fields.Length >= 3) found[(fields[0], fields[1])] = (fields[2], fields.Length > 3 ? fields[3] : "");
            }
        }
        return found;
    }

    private static bool Truthy(OrderedDictionary<string, List<string>>? found) => found is { Count: > 0 };

    private static OrderedDictionary<string, List<string>> OrEmpty(OrderedDictionary<string, List<string>>? found) =>
        Truthy(found) ? found! : new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);

    /// <summary>Every row of every file as (direction, file, key, outcome, theirs, ours), the files compared per
    /// direction, and the files left out per (direction, status): a file this library refuses to read has no
    /// conversion to compare, which is a matter of the reader and not of the conversion.</summary>
    public static (List<SyncRow> Rows, Counter Files, Counter LeftOut) Check(string work, IEnumerable<string> names, string exiv2, Runner runner)
    {
        work = PyPath.Normalize(work);
        var rows = new List<SyncRow>();
        var files = new Counter();
        var leftOut = new Counter();
        var state = Statuses(work);
        foreach (string name in names)
        {
            string status = state.TryGetValue((name, "to-xmp"), out var toXmp) ? toXmp.Status : "";
            if (status.StartsWith("refused", StringComparison.Ordinal))
            {
                leftOut.Add(new PyTuple(ToXmp, status));
            }
            else
            {
                var theirs = Printed(exiv2, "-PXkv", PyPath.Join(work, name + ".xmp"), "Xmp.", runner);
                var ours = Printed(exiv2, "-PXkv", PyPath.Join(work, name + ".ours.xmp"), "Xmp.", runner);
                if (Truthy(theirs) || Truthy(ours))
                {
                    files.Add(ToXmp);
                    rows.AddRange(Compare(OrEmpty(theirs), OrEmpty(ours)).Select(row => new SyncRow(ToXmp, name, row.Key, row.Outcome, row.Theirs, row.Ours)));
                }
            }
            status = state.TryGetValue((name, "to-exif"), out var toExif) ? toExif.Status : "";
            if (status.StartsWith("refused", StringComparison.Ordinal))
            {
                leftOut.Add(new PyTuple(ToExif, status));
            }
            else
            {
                var theirs = Printed(exiv2, "-PEkv", PyPath.Join(work, name + ".xmp"), "Exif.", runner);
                var ours = Printed(exiv2, "-PEkv", PyPath.Join(work, name + ".back.jpg"), "Exif.", runner);
                if (Truthy(theirs) || Truthy(ours))
                {
                    files.Add(ToExif);
                    rows.AddRange(Compare(OrEmpty(theirs), OrEmpty(ours), Structure).Select(row => new SyncRow(ToExif, name, row.Key, row.Outcome, row.Theirs, row.Ours)));
                }
            }
        }
        return (rows, files, leftOut);
    }

    /// <summary>What the fixture generator said, grouped: refusals by their text, notes by theirs.</summary>
    public static (List<string> Refusals, List<string> Notes) Grouped(string work)
    {
        var refusals = new Counter();
        var notes = new Counter();
        var first = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ((name, stage), (status, text)) in Statuses(work))
        {
            if (status.StartsWith("refused", StringComparison.Ordinal))
            {
                string key = $"{stage} {status}: {text}";
                refusals.Add(key);
                first.TryAdd(key, name);
            }
            else if (text.Length > 0)
            {
                foreach (string note in PyText.Split(text, " | "))
                {
                    string key = $"{stage} {note}";
                    notes.Add(key);
                    first.TryAdd(key, name);
                }
            }
        }
        return (refusals.MostCommon().Select(pair => $"{pair.Value} file(s), first {first[(string)pair.Key]}: {pair.Key}").ToList(),
                notes.MostCommon().Select(pair => $"{pair.Value} file(s), first {first[(string)pair.Key]}: {pair.Key}").ToList());
    }

    /// <summary>(direction, key, outcome) -> (count, first example) for every outcome that is not "same".</summary>
    public static OrderedDictionary<(string Direction, string Key, string Outcome), (long Count, string Example)> Summarise(IEnumerable<SyncRow> rows)
    {
        var table = new OrderedDictionary<(string, string, string), (long, string)>();
        foreach (var row in rows)
        {
            if (row.Outcome == "same") continue;
            var key = (row.Direction, row.Key, row.Outcome);
            if (!table.TryGetValue(key, out var entry)) entry = (0, $"{row.File}: tool {Py.Repr(row.Theirs)} ours {Py.Repr(row.Ours)}");
            table[key] = (entry.Item1 + 1, entry.Item2);
        }
        return table;
    }

    public static int Run(string[] argv) => Execute(argv, ToolRunner.Run);

    public static int Execute(IReadOnlyList<string> argv, Runner runner)
    {
        var parser = new ArgParser("sync-parity", "What ExifToXmp and XmpToExif produce, held against the conversion of the pinned exiv2.")
            .Add("corpus")
            .Add("work")
            .Add("--exiv2", required: true)
            .Add("--fixture", required: true, help: "the fixture generator, to which 'sync-parity WORK' is added");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string work = args.Str("work"), exiv2 = args.Str("exiv2");

        var (names, skipped) = Prepare(args.Str("corpus"), work, exiv2, runner);
        if (names.Count == 0)
        {
            Out.Print("::error title=Sync parity::no JPEG file of the corpus was prepared");
            return 1;
        }
        var (code, output, errors) = runner([.. Shlex.Split(args.Str("fixture")), "sync-parity", work], 1500);
        Console.Out.Write(output);
        if (code != 0)
        {
            Console.Out.Write(errors);
            Out.Print($"::error title=Sync parity::the fixture generator failed (exit {code})");
            return 1;
        }

        var (rows, files, leftOut) = Check(work, names.Keys, exiv2, runner);
        var totals = new Counter(rows.Select(row => (object)new PyTuple(row.Direction, row.Outcome)));
        var table = Summarise(rows);
        var unexplained = table.Where(pair => pair.Key.Outcome != "form" && IsDocumented(pair.Key.Direction, pair.Key.Key, pair.Key.Outcome) is null).ToList();
        var explained = table.Where(pair => pair.Key.Outcome != "form" && IsDocumented(pair.Key.Direction, pair.Key.Key, pair.Key.Outcome) is not null).ToList();
        var forms = table.Where(pair => pair.Key.Outcome == "form").ToList();

        // The annotations of a job are capped: what fails goes first.
        var byCodePoint = Comparer<string>.Create(Py.CompareStrings);
        unexplained = unexplained.OrderBy(pair => pair.Key.Outcome != "differs").ThenBy(pair => pair.Key.Direction, byCodePoint).ThenBy(pair => pair.Key.Key, byCodePoint).ToList();
        ToolRunner.Annotate("error", "Sync parity - differences that are not documented",
                            unexplained.Select(pair => $"{pair.Key.Direction} {pair.Key.Key} {pair.Key.Outcome}: {pair.Value.Count}; {pair.Value.Example}"), size: 2500, limit: 6);
        foreach (string direction in new[] { ToXmp, ToExif })
        {
            string counts = string.Join(", ", new[] { "same", "form", "differs", "character code", "tool only", "tool only (empty)", "ours only" }
                                                  .Select(kind => $"{kind} {totals.CountOf(new PyTuple(direction, kind))}"));
            string left = string.Join(", ", Py.Sorted(leftOut.Items(), pair => (object?)pair.Key)
                                              .Where(pair => ((PyTuple)pair.Key).S(0) == direction)
                                              .Select(pair => $"{pair.Value} {((PyTuple)pair.Key).S(1)}"));
            string skippedText = string.Join(", ", skipped.Items().Select(pair => $"{pair.Value} {pair.Key}"));
            Out.Print($"::notice title=Sync parity - {direction}::{files.CountOf(direction)} files compared of {names.Count} prepared "
                      + $"({(skippedText.Length > 0 ? skippedText : "none skipped")}); "
                      + $"left out, not read by this library: {(left.Length > 0 ? left : "none")}; values: {counts}");
        }
        var (refusals, notes) = Grouped(work);
        ToolRunner.Annotate("notice", "Sync parity - files this library did not read", refusals, limit: 1);
        ToolRunner.Annotate("notice", "Sync parity - values not converted and noted", notes, limit: 1);
        ToolRunner.Annotate("notice", "Sync parity - same items in another form",
                            forms.Select(pair => $"{pair.Key.Direction} {pair.Key.Key}: {pair.Value.Count}; {pair.Value.Example}"), limit: 1);
        ToolRunner.Annotate("notice", "Sync parity - documented differences",
                            explained.Select(pair => $"{pair.Key.Direction} {pair.Key.Key} {pair.Key.Outcome}: {pair.Value.Count}; "
                                                     + $"{IsDocumented(pair.Key.Direction, pair.Key.Key, pair.Key.Outcome)}; {pair.Value.Example}"), limit: 2);
        foreach (var pair in unexplained) Out.Print($"FAIL {pair.Key.Direction} {pair.Key.Key} {pair.Key.Outcome}: {pair.Value.Count}; {pair.Value.Example}");
        return unexplained.Count > 0 ? 1 : 0;
    }
}
