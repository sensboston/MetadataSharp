using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

// The pinned exiv2 must print every IPTC dataset an edit wrote, as it was written. Formerly
// tools/reference/iptc_edits.py.
//
// A separate check from key parity: nothing here is compared with what this library reads. The fixture generator
// (iptc-edits) writes files after IPTC edits and manifest.tsv with one row per file:
//
//     file <TAB> the lines the tool has to print for the IPTC of the file, |-separated
//
// A line is "key type count value" with single spaces, which is what a line of "exiv2 -pi" is once its padding is
// folded. A value of * stands for any value. A dataset that occurs several times is several lines with the same key.
//
// The tool leaves out a dataset it has no name for unless it is asked with -u, so both modes are run with it: a
// dataset the specification does not define, written as raw bytes, has to be printed too, under the number of its
// dataset.
//
// A file passes when both "exiv2 -u -pi" and "exiv2 -u -pa" exit with 0 for it, print every line of the row as often
// as the row has it, and print no other Iptc line: nothing was lost, nothing appeared, and no occurrence was doubled.
// The order of the lines is not compared.
//
// Usage: iptc-edits DIRECTORY --exiv2 PATH
internal static class IptcEdits
{
    public const string Any = "*";
    public static readonly string[] Modes = ["-pi", "-pa"];

    /// <summary>The Iptc lines of a print-out with their padding folded: key, type, count, value.</summary>
    public static List<string> IptcLines(string text)
    {
        var lines = new List<string>();
        foreach (string line in PyText.SplitLines(text))
        {
            var parts = PyText.Split(line, 3);
            if (parts.Count >= 3 && parts[0].StartsWith("Iptc.", StringComparison.Ordinal)) lines.Add(string.Join(" ", parts));
        }
        return lines;
    }

    public static List<(string Name, List<string> Expected)> ParseManifest(string text) => XmpEdits.ParseManifest(text);

    public static bool Matches(string item, string line) =>
        item.EndsWith(" " + Any, StringComparison.Ordinal) ? line.StartsWith(item[..^Any.Length], StringComparison.Ordinal) : item == line;

    /// <summary>What the print-out lacks or has too much of; an empty list when it is the expected one.</summary>
    public static List<string> Compare(string output, IEnumerable<string> expected)
    {
        var left = IptcLines(output);
        var problems = new List<string>();
        foreach (string item in expected)
        {
            int index = left.FindIndex(line => Matches(item, line));
            if (index >= 0) left.RemoveAt(index);
            else problems.Add($"not printed: {item}");
        }
        problems.AddRange(left.Select(line => $"printed, not written: {line}"));
        return problems;
    }

    /// <summary>Every problem found, as (file, message), and the number of files checked.</summary>
    public static (List<(string Name, string Message)> Problems, int Count) Check(string directory, string exiv2, TextRunner runner)
    {
        directory = PyPath.Normalize(directory);
        var entries = ParseManifest(PyFile.ReadText(PyPath.Join(directory, "manifest.tsv")));
        var problems = new List<(string, string)>();
        foreach (var (name, expected) in entries)
        {
            foreach (string mode in Modes)
            {
                var (code, output, errors) = runner([exiv2, "-u", mode], PyPath.Join(directory, name));
                if (code != 0)
                {
                    problems.Add((name, $"{mode}: not read (exit {code}): {PyText.Head(PyText.Strip(errors), 300)}"));
                    continue;
                }
                var found = Compare(output, expected);
                // The whole print-out goes with the first problem: one run shows what the tool prints.
                if (found.Count > 0) found[0] += "\nprinted:\n" + string.Join("\n", IptcLines(output));
                problems.AddRange(found.Select(problem => (name, $"{mode}: {problem}")));
            }
        }
        return (problems, entries.Count);
    }

    public static int Run(string[] argv) => Execute(argv, EditChecks.RunText);

    public static int Execute(IReadOnlyList<string> argv, TextRunner runner)
    {
        var parser = new ArgParser("iptc-edits", "The pinned exiv2 must print every IPTC dataset an edit wrote, as it was written.")
            .Add("directory")
            .Add("--exiv2", required: true)
            .Add("--title", @default: "IPTC edits");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string title = args.Str("title");
        var (problems, count) = Check(args.Str("directory"), args.Str("exiv2"), runner);
        if (EditChecks.Report(title, problems, count, "file(s)", head: 6000)) return 1;
        Out.Print($"::notice title={title}::{count} files after an IPTC edit are read by the pinned tool; "
                  + "every dataset is printed as it was written, as often as it was written, and no other");
        return 0;
    }
}
