using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>One compared key of one operation: the operation, the file, the key, the outcome and both print-outs.</summary>
internal sealed record OperationRow(string Operation, string File, string Key, string Outcome, List<string>? Theirs, List<string>? Ours);

// The operations of this library, held against the same operations of the pinned exiv2. Formerly
// tools/reference/operations_parity.py.
//
// A separate check from key parity. On a copy of every JPEG file of the pinned corpus the tool runs an operation of
// its command line, and this library runs the operation it offers for the same purpose (the fixture generator,
// operations-parity). Both results are printed by the tool with "exiv2 -pa" and the print-outs are compared, key by
// key:
//
//   adjust     exiv2 -a 1:30:15 -Y 1 -O 1 -D 2 ad    MetadataTimestamps.Adjust
//   modify     exiv2 -m modify.txt                    ModifyScript.Parse, saved
//   delete-a   exiv2 rm                               MetadataOperations.Delete (all)
//   delete-t   exiv2 -d t rm                          ... Delete (thumbnail)
//   delete-xc  exiv2 -d xc rm                         ... Delete (XMP, comment)
//   extract    exiv2 ex         (the .exv is printed) MetadataOperations.ExtractExv
//   insert     exiv2 in         (the tool's .exv into a file without metadata)
//                                                     MetadataOperations.InsertExv
//   thumbnail  exiv2 -i t in    (NAME-thumb.jpg)      ExifEdit.SetThumbnail
//   fixiso     exiv2 fi                               MetadataOperations.FixIso
//   fixcom     exiv2 -n ISO-8859-1 fc                 MetadataOperations.FixUserComment
//   rename     exiv2 -F -r FORMAT mv                  MetadataFileNames.Format
//              (the name the tool gives the file against the name returned)
//
// A key is "same" when both print-outs hold the same line for it. Where they differ, the print-out of what the
// operation started from (the source file; for an insert, the EXV file) says who is responsible: "differs (tool
// changed)" when this library left the line of the source and the tool did not, "differs (ours changed)" for the
// reverse; "ours only (tool removed)", "tool only (ours removed)" and "tool only (tool added)" likewise. For a key the
// operation does not address (Targets), an outcome that the source proves to be the tool's doing is intended as such.
// Every other outcome but "same" has to be one Documented names with its reason; an outcome that is not documented
// fails the check. Keys whose value is an offset of the layout are named in Layout and not compared.
//
// A file the tool fails on has nothing to compare for that operation, and neither has a file this library refuses;
// both are counted and printed, the refusals with their texts.
//
// Usage: operations-parity CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."
internal static class OperationsParity
{
    public const long MaxBytes = 8 * 1024 * 1024;
    public const string RenameFormat = "%Y%m%d_%H%M%S_:basename:";
    // Raw string lines take the line ending of the checkout; the script is written with \n as the Python wrote it.
    public static readonly string ModifyScript = """
        # The same file is given to exiv2 -m and to ModifyScript.Parse.
        set Exif.Image.Artist Parity Artist
        set Exif.Photo.UserComment charset=Ascii a parity comment
        del Exif.Image.Software
        set Iptc.Application2.Headline A parity headline
        add Iptc.Application2.Keywords parity
        set Xmp.dc.subject parity
        set Xmp.photoshop.City Parity City

        """.ReplaceLineEndings("\n");

    // operation -> (the tool's arguments before the file, the extension of the result that is printed)
    public static readonly OrderedDictionary<string, (string[]? Arguments, string Extension)> Operations = new(StringComparer.Ordinal)
    {
        ["adjust"] = (["-a", "1:30:15", "-Y", "1", "-O", "1", "-D", "2", "ad"], "jpg"),
        ["modify"] = (null, "jpg"),
        ["delete-a"] = (["rm"], "jpg"),
        ["delete-t"] = (["-d", "t", "rm"], "jpg"),
        ["delete-xc"] = (["-d", "xc", "rm"], "jpg"),
        ["extract"] = (["ex"], "exv"),
        ["insert"] = (["in"], "jpg"),
        ["thumbnail"] = (["-i", "t", "in"], "jpg"),
        ["fixiso"] = (["fi"], "jpg"),
        ["fixcom"] = (["-n", "ISO-8859-1", "fc"], "jpg"),
    };

    // Keys whose value is where something lies in the block: a matter of the layout, not of the operation.
    public static readonly HashSet<string> Layout = new(StringComparer.Ordinal)
    {
        "Exif.Image.ExifTag", "Exif.Image.GPSTag", "Exif.Photo.InteroperabilityTag", "Exif.Thumbnail.JPEGInterchangeFormat", "Exif.MakerNote.Offset",
    };

    // The corpus file behind each copy, for the examples of the report.
    public static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal);

    // (operation, key, outcome) -> why the difference is intended. A key may end in * for a prefix.
    public static readonly OrderedDictionary<(string Operation, string Key, string Outcome), string> Documented = new();

    public static void Document(string operation, string key, string outcomes, string reason)
    {
        foreach (string outcome in outcomes.Split(',')) Documented[(operation, key, PyText.Strip(outcome))] = reason;
    }

    static OperationsParity() => DocumentDefaults();

    /// <summary>The documented differences as the script declared them.</summary>
    public static void DocumentDefaults()
    {
        foreach (string key in new[]
                 {
                     "Xmp.xmp.ModifyDate", "Xmp.xmp.CreateDate", "Xmp.photoshop.DateCreated", "Xmp.exif.DateTimeOriginal", "Xmp.exif.DateTimeDigitized",
                     "Xmp.tiff.DateTime", "Iptc.Application2.DateCreated", "Iptc.Application2.TimeCreated", "Iptc.Application2.DigitizationDate",
                     "Iptc.Application2.DigitizationTime",
                 })
        {
            Document("adjust", key, "differs (ours changed)", "the XMP and IPTC counterparts of the EXIF timestamps move with them; the tool moves the EXIF tags only");
        }
        foreach (string operation in new[] { "adjust", "modify", "delete-t", "delete-xc", "extract", "thumbnail", "fixiso", "fixcom" })
        {
            Document(operation, "Exif.Photo.MakerNote", "differs (tool changed)",
                     "the tool writes the MakerNote anew when it writes the block; here its bytes stay as the source has them");
            Document(operation, "Exif.(MakerNote).*", "differs (tool changed)",
                     "the tool writes the MakerNote anew, so its byte order and the offsets it holds change; here its bytes stay");
        }
        foreach (string key in new[] { "Iptc.Envelope.CharacterSet", "Iptc.Application2.RecordVersion" })
        {
            Document("modify", key, "ours only", "an IPTC stream that is created declares UTF-8 and its record version; the tool writes the datasets it was given only");
            Document("insert", key, "ours only, differs, differs (ours changed)",
                     "the IPTC stream is written anew and declares UTF-8 and the record version of the writer, not those of the EXV file");
        }
        foreach (string key in new[] { "Exif.Thumbnail.XResolution", "Exif.Thumbnail.YResolution", "Exif.Thumbnail.ResolutionUnit" })
        {
            Document("thumbnail", key, "ours only",
                     "a thumbnail directory is created with the resolution Exif requires of a JPEG thumbnail (72 dpi); "
                     + "the tool creates it with the compression and the stream only");
        }
        Document("insert", "Exif.(MakerNote).*", "tool only, tool only (ours removed), tool only (tool added)",
                 "the MakerNote is not carried by an insert: its offsets belong to the file it was read from "
                 + "(the tool carries it); InsertExv says so in its notes");
        foreach (string key in new[] { "Exif.Thumbnail.XResolution", "Exif.Thumbnail.YResolution", "Exif.Thumbnail.ResolutionUnit" })
            Document("insert", key, "ours only", "a thumbnail directory is created with the resolution Exif requires of a JPEG thumbnail (72 dpi)");
        Document("insert", "Exif.Thumbnail.*", "tool only (ours removed)",
                 "a thumbnail this library does not read as a JPEG stream (see ExtractThumbnail) is not carried, nor the tags of its directory");
        Document("insert", "Iptc.*", "tool only (ours removed)",
                 "a dataset whose text is not UTF-8, from a stream that declares no character set, is not carried into a stream "
                 + "that declares UTF-8; InsertExv says so in its notes");
        foreach (string key in new[] { "Exif.GPSInfo.GPSDestLatitude", "Exif.Image.SubjectDistance" })
        {
            Document("extract", key, "tool only (ours removed)",
                     "the source is a malformed TIFF file of the corpus that is named .jpg: the EXV file of a source that is "
                     + "neither JPEG nor EXV is built anew (ExvFile.Create) and does not carry this value");
        }
        Document("insert", "Exif.Photo.MakerNote", "tool only, tool only (ours removed)", "the MakerNote is not carried by an insert (see Exif.(MakerNote).*)");
    }

    public static readonly string[] StandardGroups = ["Image", "Photo", "GPSInfo", "Iop", "Thumbnail"];

    /// <summary>The key a row is counted under: the tags of the MakerNote groups are one key.</summary>
    public static string GroupedKey(string operation, string key)
    {
        var parts = key.Split('.');
        if (parts[0] == "Exif" && parts.Length > 2 && !StandardGroups.Contains(parts[1])) return "Exif.(MakerNote).*";
        return key;
    }

    public static readonly string[] Dates =
    [
        "Exif.Image.DateTime", "Exif.Photo.DateTimeOriginal", "Exif.Photo.DateTimeDigitized", "Exif.Image.DateTimeOriginal",
        "Xmp.xmp.ModifyDate", "Xmp.xmp.CreateDate", "Xmp.photoshop.DateCreated", "Xmp.exif.DateTimeOriginal",
        "Xmp.exif.DateTimeDigitized", "Xmp.tiff.DateTime", "Iptc.Application2.DateCreated",
        "Iptc.Application2.TimeCreated", "Iptc.Application2.DigitizationDate", "Iptc.Application2.DigitizationTime",
    ];

    // operation -> the keys it is asked to change (a key that ends in "." is a prefix). Every other key is one the
    // operation has no business with.
    public static readonly Dictionary<string, string[]> Targets = new(StringComparer.Ordinal)
    {
        ["adjust"] = Dates,
        ["modify"] = ["Exif.Image.Artist", "Exif.Photo.UserComment", "Exif.Image.Software", "Iptc.", "Xmp.dc.subject", "Xmp.photoshop.City"],
        ["delete-a"] = ["Exif.", "Iptc.", "Xmp."],
        ["delete-t"] = ["Exif.Thumbnail."],
        ["delete-xc"] = ["Xmp."],
        ["extract"] = [],
        ["insert"] = ["Exif.", "Iptc.", "Xmp."],
        ["thumbnail"] = ["Exif.Thumbnail."],
        ["fixiso"] = ["Exif.Photo.ISOSpeedRatings"],
        ["fixcom"] = ["Exif.Photo.UserComment"],
    };

    public static bool Addressed(string operation, string key) =>
        (Targets.GetValueOrDefault(operation) ?? []).Any(target => key == target || (target.EndsWith('.') && key.StartsWith(target, StringComparison.Ordinal)));

    /// <summary>The reason an outcome is intended, or null.</summary>
    public static string? IsDocumented(string operation, string key, string outcome)
    {
        if (Documented.TryGetValue((operation, key, outcome), out string? reason)) return reason;
        foreach (var ((op, pattern, kind), text) in Documented)
        {
            if (op == operation && kind == outcome && pattern.EndsWith('*') && key.StartsWith(pattern[..^1], StringComparison.Ordinal)) return text;
        }
        // The source proves these: this library left the key as the source has it (or without it, as the source is),
        // and the tool, which writes the whole file anew, did not.
        if (outcome is "differs (tool changed)" or "ours only (tool removed)" or "tool only (tool added)" && !Addressed(operation, key))
        {
            return "a key the operation does not address: here it is as the source has it; the tool, writing the "
                   + "file anew, changed it, dropped it or added it";
        }
        if (operation == "insert")
        {
            if (outcome == "differs (tool changed)") return "the value here is the value of the EXV file that was inserted; the tool's result differs from its own EXV file";
            if (outcome == "tool only (no elements)") return "a value of no elements is not written by an edit; InsertExv says so in its notes";
            if (outcome == "tool only (undefined type)") return "a value of a field type TIFF does not define is not written by an edit; InsertExv says so in its notes";
        }
        return null;
    }

    /// <summary>key -> the lines "exiv2 -pa" prints for it, without the key; null when the tool cannot print the file.</summary>
    public static OrderedDictionary<string, List<string>>? Printed(string exiv2, string path, Runner runner)
    {
        if (!PyPath.Exists(path)) return null;
        var (code, output, _) = runner([exiv2, "-pa", path]);
        var found = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string line in PyText.SplitLines(output))
        {
            var parts = PyText.Split(line, 1);
            if (parts.Count > 0 && (parts[0].StartsWith("Exif.", StringComparison.Ordinal) || parts[0].StartsWith("Iptc.", StringComparison.Ordinal)
                                    || parts[0].StartsWith("Xmp.", StringComparison.Ordinal)))
            {
                if (!found.TryGetValue(parts[0], out var values)) found[parts[0]] = values = [];
                values.Add(parts.Count > 1 ? string.Join(" ", PyText.Split(parts[1])) : "");
            }
        }
        // The tool exits with 253 for a file without metadata.
        if (code != 0 && found.Count == 0) return code == 253 ? new OrderedDictionary<string, List<string>>(StringComparer.Ordinal) : null;
        return found;
    }

    private static readonly Regex NoElements = new($@"^{PyRe.NonSpace}+ 0\z", RegexOptions.CultureInvariant);

    private static bool Same(List<string>? left, List<string>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right, StringComparer.Ordinal);

    /// <summary>(key, outcome, the tool's lines, ours) for every key of either side that is no matter of the layout. The
    /// print-out of what the operation started from says who is responsible for a difference.</summary>
    public static List<(string Key, string Outcome, List<string>? Theirs, List<string>? Ours)> Compare(
        OrderedDictionary<string, List<string>> theirs, OrderedDictionary<string, List<string>> ours, OrderedDictionary<string, List<string>>? source = null)
    {
        var rows = new List<(string, string, List<string>?, List<string>?)>();
        source ??= new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string key in theirs.Keys.Concat(ours.Keys.Where(key => !theirs.ContainsKey(key))))
        {
            if (Layout.Contains(key)) continue;
            var left = theirs.GetValueOrDefault(key);
            var right = ours.GetValueOrDefault(key);
            var before = source.GetValueOrDefault(key);
            string kind;
            if (left is null) kind = before is not null && Same(right, before) ? "ours only (tool removed)" : "ours only";
            else if (right is null)
            {
                if (left.All(line => NoElements.IsMatch(line))) kind = "tool only (no elements)";
                else if (left.All(line => line.StartsWith("0x", StringComparison.Ordinal))) kind = "tool only (undefined type)";
                else if (before is null) kind = "tool only (tool added)";
                else if (Same(before, left)) kind = "tool only (ours removed)";
                else kind = "tool only";
            }
            else if (Same(left, right)) kind = "same";
            else if (before is not null && Same(right, before)) kind = "differs (tool changed)";
            else if (before is not null && Same(left, before)) kind = "differs (ours changed)";
            else kind = "differs";
            rows.Add((key, kind, left, right));
        }
        return rows;
    }

    public static List<string> Sources(string corpus) =>
        PyPath.RGlob(corpus).Where(path => File.Exists(path) && PyPath.Suffix(path).ToLowerInvariant() is ".jpg" or ".jpeg" && new FileInfo(path).Length <= MaxBytes).ToList();

    private static string LastLine(string errors, int code)
    {
        var lines = PyText.SplitLines(PyText.Strip(errors));
        return lines.Count > 0 ? lines[^1] : $"exit {code}";
    }

    /// <summary>Runs the tool's operations on copies; (name, operation) -> null when it worked, else its first complaint.</summary>
    public static (Dictionary<(string Name, string Operation), string> Failed, Dictionary<string, string> Renamed) ToolSide(string work, IEnumerable<string> names, string exiv2,
                                                                                                                         Runner runner)
    {
        work = PyPath.Normalize(work);
        var failed = new Dictionary<(string, string), string>();
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            string source = PyPath.Join(work, name + ".jpg");
            foreach (var (operation, (given, _)) in Operations)
            {
                string[]? arguments = given;
                string copy = PyPath.Join(work, $"{name}.{operation}.tool.jpg");
                if (operation == "insert")
                {
                    // The tool reads NAME.exv next to the file it inserts into.
                    string extracted = PyPath.Join(work, $"{name}.extract.tool.exv");
                    if (!PyPath.Exists(extracted))
                    {
                        failed[(name, operation)] = "the tool extracted nothing";
                        continue;
                    }
                    PyShutil.CopyFile(PyPath.Join(work, "blank.jpg"), copy);
                    PyShutil.CopyFile(extracted, PyPath.Join(work, $"{name}.insert.tool.exv"));
                }
                else
                {
                    PyShutil.CopyFile(source, copy);
                }
                if (operation == "thumbnail") PyShutil.CopyFile(PyPath.Join(work, "blank.jpg"), PyPath.Join(work, $"{name}.thumbnail.tool-thumb.jpg"));
                arguments ??= ["-m", PyPath.Join(work, "modify.txt"), "mo"];
                var (code, _, errors) = runner([exiv2, .. arguments, copy]);
                if (code != 0) failed[(name, operation)] = LastLine(errors, code);
            }
            string folder = PyPath.Join(work, "mv-" + name);
            if (!Directory.Exists(folder))
            {
                if (File.Exists(folder)) throw PyOSError.For(17, folder);
                Directory.CreateDirectory(folder);
            }
            PyShutil.CopyFile(source, PyPath.Join(folder, name + ".jpg"));
            var (renameCode, _, renameErrors) = runner([exiv2, "-F", "-r", RenameFormat, "mv", PyPath.Join(folder, name + ".jpg")]);
            var left = PyPath.IterDir(folder).Select(PyPath.Name).ToList();
            left.Sort(Py.CompareStrings);
            if (renameCode != 0 || left.Count != 1) failed[(name, "rename")] = LastLine(renameErrors, renameCode);
            else renamed[name] = left[0];
        }
        return (failed, renamed);
    }

    /// <summary>(file, operation) -> (status, text) from the ours.tsv the fixture generator wrote.</summary>
    public static Dictionary<(string File, string Operation), (string Status, string Text)> Statuses(string work)
    {
        var found = new Dictionary<(string, string), (string, string)>();
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

    /// <summary>Rows (operation, file, key, outcome, theirs, ours), the files compared per operation, and the files left
    /// out per (operation, who, why).</summary>
    public static (List<OperationRow> Rows, Counter Files, Counter LeftOut) Check(string work, IEnumerable<string> names, string exiv2,
                                                                               Dictionary<(string Name, string Operation), string> failed, Dictionary<string, string> renamed,
                                                                               Runner runner)
    {
        work = PyPath.Normalize(work);
        var rows = new List<OperationRow>();
        var files = new Counter();
        var leftOut = new Counter();
        var state = Statuses(work);
        foreach (string name in names)
        {
            var before = Printed(exiv2, PyPath.Join(work, name + ".jpg"), runner);
            foreach (string operation in Operations.Keys.Append("rename"))
            {
                var (status, text) = state.TryGetValue((name, operation), out var found) ? found : ("missing", "");
                if (failed.TryGetValue((name, operation), out string? complaint))
                {
                    leftOut.Add(new PyTuple(operation, "tool", complaint.Replace(name, "NAME", StringComparison.Ordinal)));
                    continue;
                }
                if (status.StartsWith("refused", StringComparison.Ordinal) || status.StartsWith("missing", StringComparison.Ordinal) || (operation == "rename" && text.Length == 0))
                {
                    leftOut.Add(new PyTuple(operation, "ours", text.Length > 0 ? $"{status}: {text}" : operation != "rename" ? status : "no date taken"));
                    continue;
                }
                files.Add(operation);
                if (operation == "rename")
                {
                    if (!renamed.TryGetValue(name, out string? theirName)) throw new PyException("KeyError", Py.ReprString(name));
                    rows.Add(new(operation, name, "file name", theirName == text ? "same" : "differs", [theirName], [text]));
                    continue;
                }
                string extension = Operations[operation].Extension;
                var theirs = Printed(exiv2, PyPath.Join(work, $"{name}.{operation}.tool.{extension}"), runner);
                var ours = Printed(exiv2, PyPath.Join(work, $"{name}.{operation}.ours.{extension}"), runner);
                if (theirs is null || ours is null)
                {
                    rows.Add(new(operation, name, "the file", "differs", [theirs is null ? "not printed" : "printed"], [ours is null ? "not printed" : "printed"]));
                    continue;
                }
                // What an insert starts from is the EXV file the tool wrote.
                var origin = operation == "insert" ? Printed(exiv2, PyPath.Join(work, $"{name}.insert.tool.exv"), runner) : before;
                rows.AddRange(Compare(theirs, ours, origin).Select(row => new OperationRow(operation, name, row.Key, row.Outcome, row.Theirs, row.Ours)));
            }
        }
        return (rows, files, leftOut);
    }

    /// <summary>(operation, key, outcome) -> (count, first example) for every outcome that is not "same".</summary>
    public static OrderedDictionary<(string Operation, string Key, string Outcome), (long Count, string Example)> Summarise(IEnumerable<OperationRow> rows)
    {
        var table = new OrderedDictionary<(string, string, string), (long, string)>();
        foreach (var row in rows)
        {
            if (row.Outcome == "same") continue;
            string counted = GroupedKey(row.Operation, row.Key);
            var key = (row.Operation, counted, row.Outcome);
            if (!table.TryGetValue(key, out var entry))
            {
                entry = (0, $"{Names.GetValueOrDefault(row.File, row.File)} {row.Key}: tool {PyText.Head(Py.Repr(row.Theirs), 70)} ours {PyText.Head(Py.Repr(row.Ours), 70)}");
            }
            table[key] = (entry.Item1 + 1, entry.Item2);
        }
        return table;
    }

    public static int Run(string[] argv)
    {
        try
        {
            return Execute(argv, ToolRunner.Run);
        }
        catch (Exception error) when (error is not SystemExit)
        {
            // The log of a job is not always at hand: the failure is said where the report is.
            var trace = PyText.SplitLines(PyException.TraceOf(error));
            ToolRunner.Annotate("error", "Operations parity - the comparer failed", trace.TakeLast(12));
            return 1;
        }
    }

    public static int Execute(IReadOnlyList<string> argv, Runner runner)
    {
        var parser = new ArgParser("operations-parity", "The operations of this library, held against the same operations of the pinned exiv2.")
            .Add("corpus")
            .Add("work")
            .Add("--exiv2", required: true)
            .Add("--fixture", required: true, help: "the fixture generator, to which the command and WORK are added");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string work = PyPath.Normalize(args.Str("work")), exiv2 = args.Str("exiv2");
        PyFile.MakeDirs(work);
        var fixture = Shlex.Split(args.Str("fixture"));

        var names = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Sources(args.Str("corpus")))
        {
            string name = $"s{names.Count:00000}";
            PyShutil.CopyFile(path, PyPath.Join(work, name + ".jpg"));
            names[name] = path;
            Names[name] = PyPath.Name(path);
        }
        if (names.Count == 0)
        {
            Out.Print("::error title=Operations parity::no JPEG file of the corpus was prepared");
            return 1;
        }
        PyFile.WriteText(PyPath.Join(work, "modify.txt"), ModifyScript);
        PyFile.WriteText(PyPath.Join(work, "rename.txt"), RenameFormat);
        var (code, output, errors) = runner([.. fixture, "operations-parity-blank", PyPath.Join(work, "blank.jpg")], 600);
        if (code != 0)
        {
            Console.Out.Write(output + errors);
            Out.Print($"::error title=Operations parity::the fixture generator failed (exit {code})");
            return 1;
        }
        var (failed, renamed) = ToolSide(work, names.Keys, exiv2, runner);
        (code, output, errors) = runner([.. fixture, "operations-parity", work], 1500);
        Console.Out.Write(output);
        if (code != 0)
        {
            Console.Out.Write(errors);
            var tail = PyText.SplitLines(PyText.Strip(errors)).TakeLast(12).ToList();
            ToolRunner.Annotate("error", $"Operations parity - the fixture generator failed (exit {code})", tail.Count > 0 ? tail : ["no output"]);
            return 1;
        }

        var (rows, files, leftOut) = Check(work, names.Keys, exiv2, failed, renamed, runner);
        var totals = new Counter(rows.Select(row => (object)new PyTuple(row.Operation, row.Outcome)));
        var table = Summarise(rows);
        var unexplained = table.Where(pair => IsDocumented(pair.Key.Operation, pair.Key.Key, pair.Key.Outcome) is null).ToList();
        var explained = table.Where(pair => IsDocumented(pair.Key.Operation, pair.Key.Key, pair.Key.Outcome) is not null).ToList();
        unexplained = unexplained.OrderBy(pair => -pair.Value.Count).ToList();

        ToolRunner.Annotate("error", "Operations parity - differences that are not documented",
                            unexplained.Select(pair => $"{pair.Key.Operation} {pair.Key.Key} {pair.Key.Outcome}: {pair.Value.Count}; {pair.Value.Example}"), size: 2500, limit: 8);
        var summary = new List<string>();
        foreach (string operation in Operations.Keys.Append("rename"))
        {
            var kinds = totals.Keys.Cast<PyTuple>().Where(key => key.S(0) == operation && key.S(1) != "same").Select(key => key.S(1)).Distinct().ToList();
            kinds.Sort(Py.CompareStrings);
            string counts = string.Join(", ", kinds.Prepend("same").Select(kind => $"{kind} {totals.CountOf(new PyTuple(operation, kind))}"));
            long tool = leftOut.Items().Where(pair => ((PyTuple)pair.Key).S(0) == operation && ((PyTuple)pair.Key).S(1) == "tool").Sum(pair => pair.Value);
            long ours = leftOut.Items().Where(pair => ((PyTuple)pair.Key).S(0) == operation && ((PyTuple)pair.Key).S(1) == "ours").Sum(pair => pair.Value);
            summary.Add($"{operation}: {files.CountOf(operation)} files compared (tool failed on {tool}, refused here {ours}); {counts}");
        }
        ToolRunner.Annotate("notice", $"Operations parity - {names.Count} files", summary, limit: 2);
        ToolRunner.Annotate("notice", "Operations parity - files left out",
                            leftOut.MostCommon().Select(pair => $"{((PyTuple)pair.Key).S(0)}, {((PyTuple)pair.Key).S(1)}: {pair.Value} file(s): {((PyTuple)pair.Key).S(2)}"), limit: 3);
        ToolRunner.Annotate("notice", "Operations parity - documented differences",
                            explained.Select(pair => $"{pair.Key.Operation} {pair.Key.Key} {pair.Key.Outcome}: {pair.Value.Count}; "
                                                     + $"{IsDocumented(pair.Key.Operation, pair.Key.Key, pair.Key.Outcome)}; {pair.Value.Example}"), limit: 3);
        foreach (var pair in unexplained) Out.Print($"FAIL {pair.Key.Operation} {pair.Key.Key} {pair.Key.Outcome}: {pair.Value.Count}; {pair.Value.Example}");
        return unexplained.Count > 0 ? 1 : 0;
    }
}
