using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>One row of manifest.tsv: the source, the edited file, the keys the edit may change, the texts to print.</summary>
internal sealed record EditEntry(string Source, string Edited, HashSet<string> Changed, List<string> Expected);

// The pinned exiv2 must still read every file the TIFF-based and BMFF writers edited. Formerly
// tools/reference/tiff_based_edits.py.
//
// A separate check from key parity: nothing here is compared with what this library reads. The fixture generator
// (tiff-based-edits, bmff-edits) writes authored TIFF, DNG, NEF-like and CR2-like files, or HEIC and AVIF files, the
// same files after each kind of edit, and manifest.tsv with one row per edited file:
//
//     source <TAB> edited <TAB> keys the edit may change, comma-separated
//            <TAB> text the tool has to print for the edited file, |-separated
//
// For each row the tool is run on both files. The edited file passes when the tool exits with 0 for it, prints every
// expected text, and prints every line it printed for the source whose key the edit was not allowed to change; a key
// that ends in * names every key that begins with what is before it. A source the tool cannot read is a broken
// fixture, and a failure.
//
// A source named "-" is no file: the edit gave metadata to a file that had none, for which the tool prints nothing and
// does not exit with 0. Such an edited file passes when the tool exits with 0 for it and prints every expected text.
//
// The generator's tiff-based-corpus and bmff-corpus commands write the same manifest for files this repository did not
// author. Many of those are damaged on purpose, so with --skip-unreadable-sources a source the tool cannot read is
// counted and skipped: the rule is about files the tool could read before the edit.
//
// Usage: tiff-based-edits DIRECTORY --exiv2-command "exiv2 -pa" [--skip-unreadable-sources]
internal static class TiffBasedEdits
{
    public static readonly string[] Families = ["Exif.", "Iptc.", "Xmp."];
    public const string NoSource = "-";

    /// <summary>The lines of a print-out by key: the first column, then the rest with its padding folded. A value that
    /// holds a line break continues on the next line, which then begins with no key: it belongs to the row before it.</summary>
    public static OrderedDictionary<string, List<string>> Rows(string text)
    {
        var byKey = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? last = null;
        foreach (string line in PyText.SplitLines(text))
        {
            var parts = PyText.Split(line, 3);
            if (parts.Count >= 2 && Families.Any(family => parts[0].StartsWith(family, StringComparison.Ordinal)))
            {
                if (!byKey.TryGetValue(parts[0], out last)) byKey[parts[0]] = last = [];
                last.Add(string.Join(" ", parts.Skip(1)));
            }
            else if (last is not null && PyText.Strip(line).Length > 0)
            {
                last[^1] += "\n" + string.Join(" ", PyText.Split(line));
            }
        }
        return byKey;
    }

    public static List<EditEntry> ParseManifest(string text)
    {
        var entries = new List<EditEntry>();
        int number = 0;
        foreach (string line in PyText.SplitLines(text))
        {
            number++;
            if (line.Length == 0) continue;
            var fields = line.Split('\t');
            if (fields.Length != 4 || fields[0].Length == 0 || fields[1].Length == 0) throw new PyException("ValueError", $"manifest line {number} does not have four columns");
            entries.Add(new(fields[0], fields[1], new HashSet<string>(fields[2].Split(',').Where(key => key.Length > 0), StringComparer.Ordinal),
                            fields[3].Split('|').Where(item => item.Length > 0).ToList()));
        }
        if (entries.Count == 0) throw new PyException("ValueError", "the manifest names no edited file");
        return entries;
    }

    /// <summary>What the print-out of an edited file lacks; an empty list when it lacks nothing.</summary>
    public static List<string> Compare(string sourceOutput, string editedOutput, IReadOnlySet<string> changed, IEnumerable<string> expected)
    {
        var problems = new List<string>();
        foreach (string item in expected)
        {
            if (!editedOutput.Contains(item, StringComparison.Ordinal)) problems.Add($"expected text is not printed: {item}");
        }
        var before = Rows(sourceOutput);
        var after = Rows(editedOutput);
        var prefixes = changed.Where(key => key.EndsWith('*')).Select(key => key[..^1]).ToList();
        foreach (var (key, lines) in before)
        {
            if (changed.Contains(key) || prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))) continue;
            if (!after.TryGetValue(key, out var now)) problems.Add($"{key} is no longer printed");
            else if (!now.SequenceEqual(lines, StringComparer.Ordinal)) problems.Add($"{key} changed: {Py.Repr(lines)} -> {Py.Repr(now)}");
        }
        return problems;
    }

    /// <summary>Every problem found, as (file, message); the edited files checked; those skipped for their source.</summary>
    public static (List<(string Name, string Message)> Problems, int Checked, int Skipped) Check(string directory, IReadOnlyList<string> command, TextRunner runner,
                                                                                               bool skipUnreadable = false)
    {
        directory = PyPath.Normalize(directory);
        var entries = ParseManifest(PyFile.ReadText(PyPath.Join(directory, "manifest.tsv")));
        var problems = new List<(string, string)>();
        var sources = new Dictionary<string, ToolResult>(StringComparer.Ordinal);
        int checkedCount = 0, skipped = 0;
        foreach (var (source, edited, changed, expected) in entries)
        {
            ToolResult read;
            if (source == NoSource)
            {
                read = new(0, "", "");
            }
            else
            {
                if (!sources.TryGetValue(source, out read)) sources[source] = read = runner(command, PyPath.Join(directory, source));
            }
            var (code, output, errors) = read;
            if (source != NoSource && (code != 0 || PyText.Strip(output).Length == 0))
            {
                if (skipUnreadable) skipped++;
                else problems.Add((source, $"the source is not read (exit {code}): {PyText.Head(PyText.Strip(errors), 300)}"));
                continue;
            }
            checkedCount++;
            var (editedCode, editedOutput, editedErrors) = runner(command, PyPath.Join(directory, edited));
            if (editedCode != 0)
            {
                problems.Add((edited, $"not read (exit {editedCode}): {PyText.Head(PyText.Strip(editedErrors), 300)}"));
                continue;
            }
            foreach (string problem in Compare(output, editedOutput, changed, expected)) problems.Add((edited, problem));
        }
        return (problems, checkedCount, skipped);
    }

    public static int Run(string[] argv) => Execute(argv, EditChecks.RunText);

    public static int Execute(IReadOnlyList<string> argv, TextRunner runner)
    {
        var parser = new ArgParser("tiff-based-edits", "The pinned exiv2 must still read every file the TIFF-based and BMFF writers edited.")
            .Add("directory")
            .Add("--exiv2-command", required: true)
            .Add("--skip-unreadable-sources", ArgAction.StoreTrue)
            .Add("--title", @default: "TIFF-based edits");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string title = args.Str("title");
        var (problems, count, skipped) = Check(args.Str("directory"), Shlex.Split(args.Str("exiv2_command")), runner, args.Flag("skip_unreadable_sources"));
        if (EditChecks.Report(title, problems, count, "edited file(s)")) return 1;
        string leftOut = skipped > 0 ? $" ({skipped} left out: the tool does not read their source)" : "";
        Out.Print($"::notice title={title}::{count} edited files are read by the pinned tool; "
                  + $"every line of their sources that the edit did not name is still printed{leftOut}");
        return 0;
    }
}
