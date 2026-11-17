using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>What "exiv2 -u -PEkv" printed for a file: key -> values, the exit code, the warnings without the path.</summary>
internal sealed record PrintOut(OrderedDictionary<string, List<string>> Values, int Code, List<string> Warnings);

// Edits of MakerNote tags by this library, read back by the pinned exiv2. Formerly tools/reference/makernote_parity.py.
//
//     makernote-parity CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."
//
// Copies the JPEG and TIFF-based raw files of CORPUS to WORK, has the fixture generator (makernote-parity) set tags
// of their MakerNotes, each in a copy of its own, and prints the source and every copy with "exiv2 -u -PEkv" (the
// key and the value as it is stored, tags without a name too; the tag that holds the note is left out, since its
// value is the bytes of the note). A copy passes when
//
//   * the tool prints the key that was set with the value that was set,
//   * every other key is printed with the lines the source has for it, and no key came or went,
//   * the tool exits as it does for the source and warns of nothing it does not warn of there.
//
// The operations are "same" (a text of the length the tag has), "grow" (a longer text, which the library appends)
// and "short" (another number in a SHORT tag). A refusal of the library is no failure: it is counted, by the layout
// of the note and its code. A copy that does not pass fails the job unless Expected names the layout, the operation
// and the verdict with a reason.
//
// The same job also tries the public MakerNote=Copy path for every corpus file with a decoded note, counts
// copied/skipped/refused by layout and reason, and compares every copied note with Exiv2 (-u -PEkv). For skips it
// tests Exiv2's own -ea/-ia round trip into the same neutral JPEG. If Exiv2 carries a skipped note, the gate requires
// an explicit structured refusal and a non-empty diagnostic; those cases are recorded as documented, unproven
// relocations, not counted as parity successes. The full per-file ledger is copy-report.json.
//
// What this decides: MakerNoteKeys.Grows lists the layouts in which a value may grow. A layout is listed there only
// when its "grow" rows pass here.
internal static class MakerNoteParity
{
    public const long MaxBytes = 24 * 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [".jpg"] = "jpg", [".jpeg"] = "jpg", [".cr2"] = "cr2", [".nef"] = "nef", [".arw"] = "arw", [".pef"] = "pef",
        [".orf"] = "orf", [".dng"] = "dng", [".srw"] = "srw", [".sr2"] = "sr2", [".nrw"] = "nrw",
        // Read-only families are read here too: a note this library does not carry has to be a documented skip with a
        // reason, not a silent loss.
        [".mrw"] = "mrw", [".raf"] = "raf", [".crw"] = "crw", [".rw2"] = "rw2",
    };

    // The tags whose value is the note itself: its bytes, which an edit changes.
    public static readonly string[] Holders = ["Exif.Photo.MakerNote", "Exif.Image.DNGPrivateData"];

    // The makers whose notes the pinned tree keeps only in .exv containers, recognised by the make the container
    // carries as text.
    public static readonly byte[][] VendorMarks = ["MINOLTA"u8.ToArray(), "SAMSUNG"u8.ToArray(), "CASIO"u8.ToArray(), "SIGMA"u8.ToArray(), "FOVEON"u8.ToArray()];

    // (group of the key, operation, verdict) -> why the difference is accepted. A group is the second part of the key
    // ("Canon" of Exif.Canon.OwnerName); "*" stands for every group.
    public static readonly Dictionary<(string Group, string Operation, string Verdict), string> Expected = new();

    // A skip is a documented outcome, not a parity success: if the tool proves it can carry the note, the report
    // retains the exact refusal diagnostic and counts it by reason. This prevents an unannotated/silent loss from being
    // mistaken for an acceptable refusal.
    public static readonly HashSet<string> DocumentedUnsafeCopyReasons = new(StringComparer.Ordinal)
    {
        "MakerNoteNotDecoded", "UnsafeMakerNoteRelocation", "MakerNoteCipherKeysUnavailable",
    };

    public static bool DocumentedCopySkip(string reasonCodes, string message)
    {
        var codes = reasonCodes.Split(',').Where(code => code.Length > 0).ToHashSet(StringComparer.Ordinal);
        return codes.Overlaps(DocumentedUnsafeCopyReasons) && PyText.Strip(message).Length > 0;
    }

    public static string? IsExpected(string group, string operation, string verdict)
    {
        string? reason = Expected.GetValueOrDefault((group, operation, verdict));
        return string.IsNullOrEmpty(reason) ? Expected.GetValueOrDefault(("*", operation, verdict)) : reason;
    }

    private static readonly Regex Whitespace = new($"{PyRe.Space}+", RegexOptions.CultureInvariant);

    /// <summary>(key -> the values "exiv2 -PEkv" prints for it, the exit code, the warnings without the path).</summary>
    public static PrintOut Printed(string exiv2, string path, Runner runner)
    {
        var (code, output, errors) = runner([exiv2, "-u", "-PEkv", path]);
        var found = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string line in PyText.SplitLines(output))
        {
            var parts = PyText.Split(line, 1);
            if (parts.Count > 0 && parts[0].StartsWith("Exif.", StringComparison.Ordinal) && !Holders.Contains(parts[0]))
            {
                if (!found.TryGetValue(parts[0], out var values)) found[parts[0]] = values = [];
                values.Add(parts.Count > 1 ? PyText.Strip(parts[1]) : "");
            }
        }
        string name = PyPath.Name(path);
        var warnings = PyText.SplitLines(errors).Where(line => PyText.Strip(line).Length > 0)
            .Select(line => PyText.Strip(Whitespace.Replace(line.Replace(path, "", StringComparison.Ordinal).Replace(name, "", StringComparison.Ordinal), " ")))
            .Distinct(StringComparer.Ordinal).ToList();
        warnings.Sort(Py.CompareStrings);
        return new(found, code, warnings);
    }

    private static bool SameValues(List<string> left, List<string> right) => left.SequenceEqual(right, StringComparer.Ordinal);

    /// <summary>OrderedDict equality: the same keys in the same order, each with the same values.</summary>
    public static bool SameOrdered(OrderedDictionary<string, List<string>> left, OrderedDictionary<string, List<string>> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First.Key == pair.Second.Key && SameValues(pair.First.Value, pair.Second.Value));

    private static string ReprList(IEnumerable<string> values) => Py.Repr(values.ToList());

    /// <summary>
    /// "ok", or what is wrong with the print-out of an edited copy, and the detail. A text has to be one of the values
    /// the tool prints for the key. A number is one field of such a value, since an array tag prints all of its
    /// elements on the line: the value has to be one of the fields and not a part of one, so 1234 must not match
    /// 12345.
    /// </summary>
    public static (string Verdict, string Detail) Verdict(PrintOut source, PrintOut result, string key, string value, string operation = "same")
    {
        var (before, codeBefore, warnedBefore) = source;
        var (after, codeAfter, warnedAfter) = result;
        if (!before.ContainsKey(key)) return ("key the tool does not print", key);
        if (!after.TryGetValue(key, out var printed)) return ("key gone for the tool", key);
        bool found = operation == "number"
            ? printed.Any(text => PyText.Split(text.Replace(",", " ", StringComparison.Ordinal)).Contains(value))
            : printed.Contains(value);
        if (!found) return ("value the tool does not print", $"{key}: wanted {Py.ReprString(value)}, printed {ReprList(printed.Take(2))}");
        foreach (var (other, values) in before)
        {
            if (other == key) continue;
            if (!after.TryGetValue(other, out var now)) return ("another key gone", other);
            if (!SameValues(now, values)) return ("another key changed", $"{other}: {ReprList(values.Take(1))} -> {ReprList(now.Take(1))}");
        }
        foreach (string other in after.Keys)
        {
            if (!before.ContainsKey(other)) return ("a key appeared", other);
        }
        if (codeAfter != codeBefore) return ("exit code changed", $"{codeBefore} -> {codeAfter}");
        var fresh = warnedAfter.Where(line => !warnedBefore.Contains(line)).ToList();
        if (fresh.Count > 0) return ("new warning", fresh[0]);
        return ("ok", "");
    }

    private static string Lower(string text) => text.ToLowerInvariant();

    public static List<string> Sources(string corpus) =>
        PyPath.RGlob(corpus).Where(path => File.Exists(path) && Extensions.ContainsKey(Lower(PyPath.Suffix(path))) && new FileInfo(path).Length <= MaxBytes).ToList();

    // The one-byte table and the codes of a 1x1 JPEG: what an .exv source is placed in, so that a note that exists in
    // the reference's test tree only as metadata is edited like any other file.
    private static byte[] Segment(int marker, byte[] payload) => [0xFF, (byte)marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];

    public static byte[] NeutralJpeg()
    {
        byte[] quant = [0, .. Enumerable.Repeat((byte)1, 64)];
        var huffman = new byte[36];
        huffman[1] = 1;
        huffman[18] = 0x10;
        huffman[19] = 1;
        return [0xFF, 0xD8, .. Segment(0xDB, quant), .. Segment(0xC4, huffman), .. Segment(0xC0, [8, 0, 8, 0, 8, 1, 1, 0x11, 0]),
                .. Segment(0xDA, [1, 1, 0, 0, 63, 0]), 0x3F, 0xFF, 0xD9];
    }

    /// <summary>
    /// A JPEG that carries the segments of an .exv file, or null when the file is not one. An .exv holds an 8-byte
    /// header (FF 01 and "Exiv2") and then the segments an APPn would hold in a JPEG. Its EXIF is placed unchanged in a
    /// complete 1x1 JPEG, so the source the library edits is the reference's own bytes and not a copy either tool has
    /// rewritten.
    /// </summary>
    public static byte[]? AsJpeg(byte[] exv)
    {
        if (exv.Length < 9 || exv[0] != 0xFF || exv[1] != 0x01 || !exv.AsSpan(2, 5).SequenceEqual("Exiv2"u8)) return null;
        var segments = new List<byte[]>();
        int at = 7;
        while (at + 2 <= exv.Length)
        {
            if (exv[at] != 0xFF || exv[at + 1] == 0xD9) break;
            int length = (int)BigEndian(PyBytes.Slice(exv, at + 2, at + 4));
            if (length < 2) break;
            segments.Add(PyBytes.Slice(exv, at, at + 2 + length));
            at += 2 + length;
        }
        if (!segments.Any(segment => segment[1] == 0xE1)) return null;
        return [0xFF, 0xD8, .. segments.SelectMany(segment => segment), .. NeutralJpeg()[2..]];
    }

    private static long BigEndian(byte[] data)
    {
        long value = 0;
        foreach (byte b in data) value = (value << 8) | b;
        return value;
    }

    /// <summary>(name, number, operation, key, status, value) for every line of ours.tsv.</summary>
    public static List<string[]> RowsOf(string work)
    {
        var rows = new List<string[]>();
        foreach (string line in PyText.SplitLines(PyFile.ReadText(PyPath.Join(work, "ours.tsv"))))
        {
            var parts = line.Split('\t');
            if (parts.Length == 6) rows.Add(parts);
        }
        return rows;
    }

    public static string GroupOf(string key)
    {
        var parts = key.Split('.');
        return parts.Length > 2 ? parts[1] : "?";
    }

    /// <summary>
    /// A second or later image directory of the container, which no copy of a MakerNote can hold. Exiv2 names the
    /// directories a file chains after its first Image and Thumbnail ones Image2, Image3 and so on, and the
    /// sub-images of a TIFF SubImage and SubThumb. They are rows of the picture's own directories, not of the
    /// MakerNote: a copy that carries the note over cannot keep them, so leaving them in this comparison would ask the
    /// copy to carry the container.
    /// </summary>
    public static bool IsImageDirectory(string group)
    {
        if (group.StartsWith("SubImage", StringComparison.Ordinal) || group.StartsWith("SubThumb", StringComparison.Ordinal)) return true;
        return group.StartsWith("Image", StringComparison.Ordinal) && PyText.IsDigit(group[5..]);
    }

    private static readonly HashSet<string> Standard = new(StringComparer.Ordinal) { "Image", "Photo", "GPSInfo", "Iop", "Thumbnail", "MakerNote" };

    /// <summary>The decoded MakerNote keys and values, excluding the container's own directories.</summary>
    public static PrintOut MakerNotePrinted(string exiv2, string path, Runner runner)
    {
        var (values, code, warnings) = Printed(exiv2, path, runner);
        var kept = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            var parts = key.Split('.');
            if (parts.Length > 2 && !Standard.Contains(parts[1]) && !IsImageDirectory(parts[1])) kept[key] = value;
        }
        return new(kept, code, warnings);
    }

    /// <summary>Counter of (group, operation, verdict or refusal), and an example for each.</summary>
    public static (Counter Counts, Dictionary<PyTuple, string> Examples) Check(string work, OrderedDictionary<string, (string Path, string Extension)> names, string exiv2,
                                                                                Runner runner)
    {
        work = PyPath.Normalize(work);
        var counts = new Counter();
        var examples = new Dictionary<PyTuple, string>();
        var prints = new Dictionary<string, PrintOut>(StringComparer.Ordinal);
        foreach (var row in RowsOf(work))
        {
            string name = row[0], number = row[1], operation = row[2], key = row[3], status = row[4], value = row[5];
            if (operation == "none")
            {
                counts.Add(new PyTuple("-", "none", status));
                continue;
            }
            if (!names.TryGetValue(name, out var source)) throw new PyException("KeyError", Py.ReprString(name));
            string extension = source.Extension;
            string outcome, detail;
            if (status != "written")
            {
                (outcome, detail) = (status, $"{key}: {value}");
            }
            else
            {
                if (!prints.ContainsKey(name)) prints[name] = Printed(exiv2, PyPath.Join(work, $"{name}.{extension}"), runner);
                string copy = PyPath.Join(work, $"{name}.{number}.ours.{extension}");
                (outcome, detail) = Verdict(prints[name], Printed(exiv2, copy, runner), key, value, operation);
                if (File.Exists(copy)) File.Delete(copy);
            }
            var entry = new PyTuple(GroupOf(key), operation, outcome);
            if (outcome == "ok") counts.Add(new PyTuple("-", "files", extension));
            counts.Add(entry);
            examples.TryAdd(entry, $"{PyPath.Name(source.Path)}: {detail}");
        }
        return (counts, examples);
    }

    public static int Run(string[] argv) => Execute(argv, ToolRunner.Run, ToolRunner.Annotate);

    public static int Execute(IReadOnlyList<string> argv, Runner runner, Action<string, string, IEnumerable<string>, int, int> annotate)
    {
        void Annotate(string level, string title, IEnumerable<string> lines, int size = 3000, int limit = 9) => annotate(level, title, lines, size, limit);

        var parser = new ArgParser("makernote-parity", "Edits of MakerNote tags by this library, read back by the pinned exiv2.")
            .Add("corpus")
            .Add("work")
            .Add("--exiv2", required: true)
            .Add("--fixture", required: true, help: "the fixture generator, to which the command and WORK are added");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string work = PyPath.Normalize(args.Str("work")), corpus = args.Str("corpus"), exiv2 = args.Str("exiv2");
        PyFile.MakeDirs(work);

        var names = new OrderedDictionary<string, (string Path, string Extension)>(StringComparer.Ordinal);
        foreach (string path in Sources(corpus))
        {
            string name = $"s{names.Count:00000}", extension = Extensions[Lower(PyPath.Suffix(path))];
            PyShutil.CopyFile(path, PyPath.Join(work, $"{name}.{extension}"));
            names[name] = (path, extension);
        }
        // The reference keeps some cameras' notes only in its own .exv containers (the Minolta DSLRs and a Samsung
        // phone among them). Placed in a JPEG as they are, those notes are edited here too.
        int placed = 0;
        foreach (string path in PyPath.RGlob(corpus))
        {
            if (!File.Exists(path) || Lower(PyPath.Suffix(path)) != ".exv" || new FileInfo(path).Length > MaxBytes) continue;
            byte[] data = PyFile.ReadBytes(path);
            // Only the cameras whose notes the corpus holds nowhere else: an .exv of a camera the corpus already has
            // as a file would add a second, heavier copy of the same edit.
            if (!VendorMarks.Any(mark => PyBytes.Contains(data, mark))) continue;
            byte[]? jpeg = AsJpeg(data);
            if (jpeg is null) continue;
            string name = $"s{names.Count:00000}";
            PyFile.WriteBytes(PyPath.Join(work, $"{name}.jpg"), jpeg);
            names[name] = (path, "jpg");
            placed++;
        }
        if (names.Count == 0)
        {
            Out.Print("::error title=MakerNote parity::no file of the corpus was prepared");
            return 1;
        }

        // The edit corpus above is intentionally JPEG/raw-only. The copy scan is broader: give the fixture generator
        // every corpus file and let the public reader identify decoded notes.
        string copyInputDir = PyPath.Join(work, "copy-inputs");
        PyFile.MakeDirs(copyInputDir);
        var copyInputNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in PyPath.RGlob(corpus))
        {
            if (!File.Exists(path)) continue;
            string ident = $"c{copyInputNames.Count:000000}";
            string suffix = Lower(PyPath.Suffix(path));
            if (Py.Len(suffix) > 12) suffix = ".bin";
            string target = PyPath.Join(copyInputDir, ident + suffix);
            PyShutil.CopyFile(path, target);
            copyInputNames[ident] = target;
        }
        var (code, output, errors) = runner([.. Shlex.Split(args.Str("fixture")), "makernote-parity", work], 900);
        Console.Out.Write(output);
        if (code != 0)
        {
            Console.Out.Write(errors);
            var tail = PyText.SplitLines(PyText.Strip(errors)).TakeLast(12).ToList();
            if (tail.Count == 0) tail = PyText.SplitLines(PyText.Strip(output)).TakeLast(12).ToList();
            if (tail.Count == 0) tail = ["no output"];
            Annotate("error", $"MakerNote parity - the fixture generator failed (exit {code})", tail);
            return 1;
        }

        var (counts, examples) = Check(work, names, exiv2, runner);
        var failures = new List<string>();
        var accepted = new List<string>();
        var summary = new List<string>();
        var copyRows = new List<string[]>();
        string copyTsv = PyPath.Join(work, "copy.tsv");
        if (PyPath.Exists(copyTsv))
        {
            foreach (string line in PyText.SplitLines(PyFile.ReadText(copyTsv)))
            {
                var parts = PyText.Split(line, "\t", 4);
                if (parts.Count == 5) copyRows.Add([.. parts]);
            }
        }
        var copyCounts = new Counter();
        var copyExamples = new Dictionary<PyTuple, string>();
        var copyDetails = new List<object?>();
        string oracleRoot = PyPath.Join(work, "copy-oracle");
        string neutral = PyPath.Join(work, "copy-neutral.jpg");
        if (copyRows.Count == 0) failures.Add("the corpus scan found no decoded MakerNotes");

        static List<string> MissingKeys(PrintOut before, PrintOut after) =>
            before.Values.Where(pair => !(after.Values.TryGetValue(pair.Key, out var now) && SameValues(now, pair.Value))).Select(pair => pair.Key).ToList();

        static List<string> ExtraKeys(PrintOut before, PrintOut after) => after.Values.Keys.Where(key => !before.Values.ContainsKey(key)).ToList();

        // Try Exiv2's own -ea/-ia path into the same neutral JPEG; return status and detail.
        (string Status, string Detail) OracleCopy(string sourcePath, string ident)
        {
            string directory = PyPath.Join(oracleRoot, ident);
            PyFile.MakeDirs(directory);
            string localSource = PyPath.Join(directory, "source" + Lower(PyPath.Suffix(sourcePath)));
            PyShutil.CopyFile(sourcePath, localSource);
            string exv = PyPath.WithSuffix(localSource, ".exv");
            var (extractCode, extractOutput, extractErrors) = runner([exiv2, "-ea", "ex", localSource]);
            if (extractCode != 0 || !File.Exists(exv))
                return ("refused", $"extract exit={extractCode}: {PyText.Head(PyText.Strip(extractErrors.Length > 0 ? extractErrors : extractOutput), 400)}");
            string dest = PyPath.Join(directory, "destination.jpg");
            PyShutil.CopyFile(neutral, dest);
            PyShutil.CopyFile(exv, PyPath.WithSuffix(dest, ".exv"));
            var (insertCode, insertOutput, insertErrors) = runner([exiv2, "-ia", "in", dest]);
            if (insertCode != 0)
                return ("refused", $"insert exit={insertCode}: {PyText.Head(PyText.Strip(insertErrors.Length > 0 ? insertErrors : insertOutput), 400)}");
            var before = MakerNotePrinted(exiv2, localSource, runner);
            var after = MakerNotePrinted(exiv2, dest, runner);
            if (before.Code != after.Code) return ("differs", $"-PEkv exit code {before.Code} -> {after.Code}");
            if (before.Values.Count == 0) return ("not_printable", "Exiv2 -u -PEkv printed no decoded MakerNote keys from the source");
            if (SameOrdered(before.Values, after.Values)) return ("carried", $"Exiv2 preserved {before.Values.Count} MakerNote keys/values through -ea/-ia");
            return ("differs", $"Exiv2's own copy changed/dropped values: missing/changed={Py.Repr(MissingKeys(before, after).Take(4).ToList())}, "
                               + $"extra={Py.Repr(ExtraKeys(before, after).Take(4).ToList())}");
        }

        foreach (var row in copyRows)
        {
            string ident = row[0], layout = row[1], status = row[2], reasonCodes = row[3], message = row[4];
            string reason = reasonCodes.Length > 0 ? reasonCodes : "none";
            var (oracleStatus, oracleDetail) = ("not_applicable", "");
            if (status == "copied")
            {
                string? sourcePath = copyInputNames.GetValueOrDefault(ident);
                string copiedPath = PyPath.Join(work, "copy-output", ident + ".jpg");
                if (sourcePath is null || !File.Exists(copiedPath))
                {
                    failures.Add($"{layout} {ident}: copied status has no source or destination file");
                    (oracleStatus, oracleDetail) = ("missing", "copy output/source missing");
                }
                else
                {
                    var before = MakerNotePrinted(exiv2, sourcePath, runner);
                    var after = MakerNotePrinted(exiv2, copiedPath, runner);
                    if (before.Values.Count == 0)
                    {
                        string warned = before.Warnings.Count > 0 ? Py.Repr(before.Warnings.Take(240).ToList()) : "";
                        failures.Add($"{layout} {ident}: Exiv2 printed no MakerNote keys on the corpus source (exit {before.Code}; {warned})");
                        (oracleStatus, oracleDetail) = ("source_not_printable", "");
                    }
                    else if (before.Code != after.Code || !SameOrdered(before.Values, after.Values))
                    {
                        failures.Add($"{layout} {ident}: copied MakerNote differs under Exiv2; exit {before.Code}->{after.Code}, "
                                     + $"missing/changed={Py.Repr(MissingKeys(before, after).Take(5).ToList())}, extra={Py.Repr(ExtraKeys(before, after).Take(5).ToList())}");
                        (oracleStatus, oracleDetail) = ("ours_differs", "");
                    }
                    else
                    {
                        (oracleStatus, oracleDetail) = ("matched", $"{before.Values.Count} MakerNote keys/values");
                    }
                }
            }
            else if (status == "skipped")
            {
                string? sourcePath = copyInputNames.GetValueOrDefault(ident);
                if (sourcePath is null)
                {
                    (oracleStatus, oracleDetail) = ("missing", "source path missing");
                    failures.Add($"{layout} {ident}: skipped source path is missing");
                }
                else
                {
                    (oracleStatus, oracleDetail) = OracleCopy(sourcePath, ident);
                    if (oracleStatus == "carried" && !DocumentedCopySkip(reasonCodes, message))
                    {
                        failures.Add($"{layout} {ident}: MetadataSharp skipped ({reason}) but Exiv2 carries the MakerNote "
                                     + "without a structured relocation refusal and diagnostic; " + oracleDetail);
                    }
                }
            }
            var countKey = new PyTuple(layout, status, reason, oracleStatus);
            copyCounts.Add(countKey);
            copyExamples.TryAdd(countKey, $"{ident}: {(message.Length > 0 ? message : oracleDetail)}");
            copyDetails.Add(PyDict.Of(("id", ident), ("layout", layout), ("status", status), ("reason_codes", reasonCodes), ("message", message),
                                      ("exiv2_copy", oracleStatus), ("detail", oracleDetail)));
        }

        var sortedCopyCounts = Py.Sorted(copyCounts.Items(), pair => (object?)new PyTuple(pair.Key, pair.Value));
        foreach (var (key, count) in sortedCopyCounts)
        {
            var parts = (PyTuple)key;
            string line = $"{parts.S(0)} copy {parts.S(1)} reason={parts.S(2)} exiv2={parts.S(3)}: {count}";
            summary.Add(line + (parts.S(1) != "copied" ? $"; e.g. {copyExamples[parts]}" : ""));
        }
        var copyReport = PyDict.Of(
            ("decoded_makernote_files", (long)copyRows.Count),
            ("counts", sortedCopyCounts.Select(pair =>
            {
                var parts = (PyTuple)pair.Key;
                return (object?)PyDict.Of(("layout", parts[0]), ("status", parts[1]), ("reason_codes", parts[2]), ("exiv2_copy", parts[3]), ("count", pair.Value));
            }).ToList()),
            ("files", copyDetails));
        PyFile.WriteText(PyPath.Join(work, "copy-report.json"), PyJson.Dumps(copyReport, indent: 2) + "\n");
        var copyTotals = new Counter(copyRows.Select(row => (object)row[2]));
        summary.Insert(0, $"Corpus MakerNote copies: {copyRows.Count} decoded; "
                          + string.Join(", ", new[] { "copied", "skipped", "refused" }.Select(name => $"{name}={copyTotals.CountOf(name)}")));
        foreach (var (key, count) in Py.Sorted(counts.Items(), pair => (object?)new PyTuple(pair.Key, pair.Value)))
        {
            var parts = (PyTuple)key;
            string group = parts.S(0), operation = parts.S(1), outcome = parts.S(2);
            string line = $"{group} {operation}: {outcome} {count}";
            if (operation == "files") summary.Add($"edits read back in .{outcome} files: {count}");
            else if (operation == "none" || outcome == "ok") summary.Add(line);
            else if (outcome.StartsWith("refused ", StringComparison.Ordinal)) summary.Add($"{line}; e.g. {examples[parts]}");
            else if (IsExpected(group, operation, outcome) is { Length: > 0 } why) accepted.Add($"{line}; {why}; e.g. {examples[parts]}");
            else failures.Add($"{line}; e.g. {examples[parts]}");
        }
        long passed = counts.Items().Where(pair => ((PyTuple)pair.Key).S(2) == "ok" && ((PyTuple)pair.Key).S(1) != "files").Sum(pair => pair.Value);
        long Written(string operation) => counts.Items().Where(pair => ((PyTuple)pair.Key).S(1) == operation && ((PyTuple)pair.Key).S(2) == "ok").Sum(pair => pair.Value);
        Annotate("error", "MakerNote parity - edits the tool does not read back as set", failures, size: 2500, limit: 8);
        Annotate("notice", "MakerNote parity - accepted differences", accepted);
        if (placed > 0) summary.Add($"{placed} .exv source(s) placed in a neutral JPEG and edited as .jpg");
        Annotate("notice", $"MakerNote parity - {names.Count} files; {passed} edits read back by the tool "
                           + $"(same {Written("same")}; grow {Written("grow")}; short {Written("short")}; number {Written("number")})", summary);
        if (passed == 0)
        {
            Out.Print("::error title=MakerNote parity::no edit was written and read back");
            return 1;
        }
        return failures.Count > 0 ? 1 : 0;
    }
}
