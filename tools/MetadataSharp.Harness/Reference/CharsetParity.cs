using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

// The character-set matrix against the pinned Exiv2, through -n as the reference really uses it. Formerly
// tools/reference/run_charset.py.
//
//     charset-parity WORK --exiv2 PATH --fixture "COMMAND"
//
// The fixture generator (charset-parity) writes one JPEG per character set of MetadataSharp.MetadataCharsets: a
// UNICODE UserComment whose body is the text in that code page, and a UCS-2 little-endian XPTitle. It reads every
// file back through the public API and writes ours.tsv: the file, the character set, the key and the text this
// library shows, plus the text that was written.
//
// -n in Exiv2 0.28.9 names the character set of a UNICODE user comment for the fc action only (app/actions.cpp,
// FixCom::run): the comment is read in that set, converted to UCS-2 and written back. Printing does not consult it -
// CommentValue::write sniffs a byte-order mark and otherwise assumes the file's own byte order (src/value.cpp,
// detectCharset). So the gate asks the pinned tool to do what -n does - exiv2 -v -n <name> fc on a copy of the file -
// and reads the text it converted out of the action's own verbose line, Setting Exif UNICODE user comment to "...".
// That text has to be the text this library reads. (The copy is written back by the tool, and 0.28.9 writes the
// converted value as UCS-2 in big-endian order whatever the file is, so the copy is only read for that line and
// thrown away.) The title is compared through the tool's translated print, -PEkt, which is where its printUcs2 runs.
//
// A round-trip row (written) must read back as it was written. -n is behind Exiv2's optional iconv support: when
// the pinned build cannot convert, the run says so in a notice, compares no text with the tool, and still fails a
// text that does not read back.
internal static class CharsetParity
{
    public const string Comment = "Exif.Photo.UserComment";
    public const string Title = "Exif.Image.XPTitle";
    public const string NoIconv = "Cannot convert text encoding";
    public const string Setting = "Setting Exif UNICODE user comment to";

    // glibc's converter for windows-1258 holds the last character of a text until the conversion is flushed, and the
    // reference never flushes - convertStringCharsetIconv (src/convert.cpp) stops as soon as the input is consumed - so
    // the tool's text is this library's without its final character. Every other character set of the matrix hands
    // over everything without a flush, which the run shows by comparing all of them. The rule is by name and by exact
    // shape: the rest must be equal.
    public static readonly HashSet<string> Unflushed = new(StringComparer.Ordinal) { "windows-1258" };

    /// <summary>(key -> the value the tool prints, exit code, stderr) for one file, or for one tag of it.</summary>
    public static (Dictionary<string, string> Values, int Code, string Errors) Printed(string exiv2, string path, Runner runner, string flags = "-PEkv", string? key = null)
    {
        List<string> command = [exiv2, flags];
        if (!string.IsNullOrEmpty(key)) command.AddRange(["-K", key]);
        var (code, output, errors) = runner([.. command, path]);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in PyText.SplitLines(output))
        {
            var parts = PyText.Split(line, 1);
            if (parts.Count > 0 && parts[0].StartsWith("Exif.", StringComparison.Ordinal)) values[parts[0]] = parts.Count > 1 ? PyText.Strip(parts[1]) : "";
        }
        return (values, code, errors);
    }

    /// <summary>The text the tool reads out of the comment in the named character set, and its exit and errors.</summary>
    public static (string? Text, int Code, string Errors) Converted(string exiv2, string path, string charset, Runner runner)
    {
        var (code, output, errors) = runner([exiv2, "-v", "-n", charset, "fc", path]);
        // Split on \n rather than every line boundary: a converted text may hold a character Python counts as a line
        // boundary, and the text is everything between the marker and the closing quote.
        foreach (string line in output.Split('\n'))
        {
            int at = line.IndexOf(Setting, StringComparison.Ordinal);
            if (at < 0) continue;
            string text = PyText.Strip(line[(at + Setting.Length)..]);
            return (Py.Len(text) >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text, code, errors);
        }
        return (null, code, errors);
    }

    private static string ReprOrNone(string? text) => text is null ? "None" : Py.ReprString(text);

    /// <summary>text[:-1] counted in code points.</summary>
    private static string DropLast(string text) => PyText.Head(text, Py.Len(text) - 1);

    public static int Compare(IReadOnlyList<string> argv, Runner runner)
    {
        var parser = new ArgParser("charset-parity", "The character-set matrix against the pinned Exiv2, through ``-n`` as the reference really uses it.")
            .Add("work")
            .Add("--exiv2", required: true)
            .Add("--fixture", required: true, help: "the fixture generator, to which the command and WORK are added");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string work = PyPath.Normalize(args.Str("work")), exiv2 = args.Str("exiv2");
        PyFile.MakeDirs(work);

        var (code, output, errors) = runner([.. Shlex.Split(args.Str("fixture")), "charset-parity", work], 900);
        Console.Out.Write(output);
        if (code != 0)
        {
            Console.Out.Write(errors);
            var tail = PyText.SplitLines(PyText.Strip(errors.Length > 0 ? errors : output)).TakeLast(12).ToList();
            ToolRunner.Annotate("error", $"PARITY charset - the fixture generator failed (exit {code})", tail.Count > 0 ? tail : ["no output"]);
            return 1;
        }

        string manifest = PyPath.Join(work, "ours.tsv");
        if (!File.Exists(manifest))
        {
            ToolRunner.Annotate("error", "PARITY charset - the fixture wrote no manifest", [$"{manifest} does not exist"]);
            return 1;
        }
        var rows = PyText.SplitLines(PyFile.ReadText(manifest)).Where(line => line.Count(c => c == '\t') == 4).Select(line => line.Split('\t')).ToList();
        if (rows.Count == 0)
        {
            ToolRunner.Annotate("error", "PARITY charset - the fixture wrote no row", ["ours.tsv is empty"]);
            return 1;
        }

        var byFile = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            string name = row[0], charset = row[1], codePage = row[2], key = row[3], text = row[4];
            if (!byFile.TryGetValue(name, out var entry)) byFile[name] = entry = new Dictionary<string, string>(StringComparer.Ordinal);
            entry[key] = text;
            entry["_charset"] = charset;
            entry["_code_page"] = codePage;
        }

        var failures = new List<string>();
        var counts = new Counter();
        bool iconvMissing = false;
        var fileNames = byFile.Keys.ToList();
        fileNames.Sort(Py.CompareStrings);
        foreach (string name in fileNames)
        {
            var values = byFile[name];
            string charset = values["_charset"];
            var written = values.GetValueOrDefault("written", "|").Split('|');
            if (written.Length != 2)
            {
                failures.Add($"{name}: the fixture wrote no text to compare with");
                continue;
            }
            if (values.GetValueOrDefault(Comment, "") != written[0])
            {
                failures.Add($"{name} ({charset}): the comment reads back as {ReprOrNone(values.GetValueOrDefault(Comment))}, written {Py.ReprString(written[0])}");
                counts.Add("round trip differs");
            }
            else
            {
                counts.Add("round trip");
            }
            if (values.GetValueOrDefault(Title, "") != written[1])
                failures.Add($"{name} ({charset}): the title reads back as {ReprOrNone(values.GetValueOrDefault(Title))}, written {Py.ReprString(written[1])}");
            string path = PyPath.Join(work, name);
            // What -n does in the reference: the comment is read in that character set and written back as UCS-2, so
            // the tool's own print of the result is the statement being compared.
            string copy = PyPath.Join(work, name + ".fc.jpg");
            PyShutil.CopyFile(path, copy);
            var (toolText, convertCode, convertErrors) = Converted(exiv2, copy, charset, runner);
            if (File.Exists(copy)) File.Delete(copy);
            if (toolText is null)
            {
                if (convertErrors.Contains(NoIconv, StringComparison.Ordinal))
                {
                    iconvMissing = true;
                    break;
                }
                failures.Add($"{name} ({charset}): the tool converted no comment in that character set (exit {convertCode}): {PyText.Head(PyText.Strip(convertErrors), 300)}");
                continue;
            }
            string ours = values.GetValueOrDefault(Comment, "");
            if (toolText == ours)
            {
                counts.Add("comment matched");
            }
            else if (Unflushed.Contains(charset) && ours.Length > 0 && toolText == DropLast(ours))
            {
                counts.Add("comment matched but for the last character");
            }
            else
            {
                failures.Add($"{name} ({charset}): the tool reads {Py.ReprString(toolText)} ({Py.Len(toolText)} characters, {PyText.Hex(PyText.Utf8(toolText))}), "
                             + $"this library {Py.ReprString(ours)} ({Py.Len(ours)}, {PyText.Hex(PyText.Utf8(ours))})");
                counts.Add("comment differs");
            }
            // Only the title: the translated print of the file would also render the comment, whose bytes are
            // deliberately a code page rather than the UCS-2 its marker promises.
            var (title, titleCode, titleErrors) = Printed(exiv2, path, runner, "-PEkt", Title);
            if (titleCode != 0) failures.Add($"{name}: the tool failed (exit {titleCode}): {PyText.Head(PyText.Strip(titleErrors), 300)}");
            else if (title.GetValueOrDefault(Title, "") != values.GetValueOrDefault(Title, ""))
            {
                failures.Add($"{name}: the tool prints the title as {ReprOrNone(title.GetValueOrDefault(Title))}, this library {ReprOrNone(values.GetValueOrDefault(Title))}");
                counts.Add("title differs");
            }
            else if (title.ContainsKey(Title))
            {
                counts.Add("title matched");
            }
        }

        if (iconvMissing)
        {
            // Nothing can be compared with the tool here, but what was compared without it still counts: a text the
            // fixture wrote and this library does not read back is a failure on its own.
            if (failures.Count > 0) ToolRunner.Annotate("error", "PARITY charset - texts the fixture wrote that do not read back", failures);
            ToolRunner.Annotate("notice", "PARITY charset - the pinned build has no iconv, so -n is not compared",
                                [$"The fixture wrote {byFile.Count} files and this library read {counts.CountOf("round trip")} of "
                                 + "their comments and titles back as written; the matrix, its encodings and its round "
                                 + "trips are covered by the unit tests (MetadataTextTests). The pinned build cannot "
                                 + "convert text encodings, which is what -n is behind, so the tool's reading of them is "
                                 + "not compared here."]);
            return failures.Count == 0 ? 0 : 1;
        }

        ToolRunner.Annotate("error", "PARITY charset - texts the tool and this library read differently", failures);
        var unflushed = Unflushed.ToList();
        unflushed.Sort(Py.CompareStrings);
        ToolRunner.Annotate("notice", $"PARITY charset - {byFile.Count} character sets; comments matched {counts.CountOf("comment matched")}, titles matched {counts.CountOf("title matched")}",
                            Py.Sorted(counts.Items(), pair => (object?)new PyTuple(pair.Key, pair.Value)).Select(pair => $"{pair.Key}: {pair.Value}")
                                .Concat(unflushed.Select(name => $"rule: a {name} text is the tool's without its last character - glibc's converter for that "
                                                                 + "code page holds it until a flush the reference never does")).ToList());
        return failures.Count > 0 ? 1 : 0;
    }

    /// <summary>The comparison, with its own failures reported as annotations: a traceback in the log is not.</summary>
    public static int Execute(IReadOnlyList<string> argv, Runner runner)
    {
        try
        {
            return Compare(argv, runner);
        }
        catch (Exception error) when (error is not SystemExit)
        {
            // A gate that crashes has to say so where the run can be read.
            var trace = PyText.SplitLines(PyException.TraceOf(error)).Where(line => PyText.Strip(line).Length > 0);
            ToolRunner.Annotate("error", "PARITY charset - the comparison itself failed", trace.TakeLast(12).ToList());
            return 1;
        }
    }

    public static int Run(string[] argv) => Execute(argv, ToolRunner.Run);
}
