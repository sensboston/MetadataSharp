using System.Xml;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

// The pinned exiv2 must print every XMP property an edit by path wrote, as it was written. Formerly
// tools/reference/xmp_edits.py.
//
// A separate check from key parity: nothing here is compared with what this library reads. The fixture generator
// (xmp-edits) writes files after XMP edits and manifest.tsv with one row per file:
//
//     file <TAB> the lines the tool has to print for the XMP of the file, |-separated
//
// A line is "key type count value" with single spaces, which is what a line of "exiv2 -pa" is once its padding is
// folded. A value of * stands for any value.
//
// A file passes when
//   - "exiv2 -pa" exits with 0 for it, prints every line of the row, and prints no Xmp key the row does not name:
//     nothing was lost and nothing appeared;
//   - "exiv2 -pX" exits with 0 for it and prints one well-formed XML document that holds an rdf:RDF element: the
//     packet as the tool takes it out. The tool does not take the packet out of a sidecar (.xmp): there the file
//     itself has to be that document.
//
// Usage: xmp-edits DIRECTORY --exiv2 PATH
internal static class XmpEdits
{
    public const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    public const string Any = "*";
    public const string Sidecar = ".xmp";

    /// <summary>The Xmp lines of a print-out with their padding folded: key, type, count, value.</summary>
    public static List<string> XmpLines(string text)
    {
        var lines = new List<string>();
        foreach (string line in PyText.SplitLines(text))
        {
            var parts = PyText.Split(line, 3);
            if (parts.Count >= 3 && parts[0].StartsWith("Xmp.", StringComparison.Ordinal)) lines.Add(string.Join(" ", parts));
        }
        return lines;
    }

    /// <summary>The rows of a manifest with two columns: the file and the lines it has to print.</summary>
    public static List<(string Name, List<string> Expected)> ParseManifest(string text)
    {
        var entries = new List<(string, List<string>)>();
        int number = 0;
        foreach (string line in PyText.SplitLines(text))
        {
            number++;
            if (line.Length == 0) continue;
            var fields = line.Split('\t');
            if (fields.Length != 2 || fields[0].Length == 0 || fields[1].Length == 0) throw new PyException("ValueError", $"manifest line {number} does not have two columns");
            entries.Add((fields[0], fields[1].Split('|').Where(item => item.Length > 0).ToList()));
        }
        if (entries.Count == 0) throw new PyException("ValueError", "the manifest names no file");
        return entries;
    }

    /// <summary>What the print-out lacks or has too much of; an empty list when it is the expected one.</summary>
    public static List<string> Compare(string output, IEnumerable<string> expected)
    {
        var printed = XmpLines(output);
        var problems = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string item in expected)
        {
            keys.Add(PyText.Split(item, " ", 1)[0]);
            if (item.EndsWith(" " + Any, StringComparison.Ordinal))
            {
                string stem = item[..^Any.Length];
                if (!printed.Any(line => line.StartsWith(stem, StringComparison.Ordinal))) problems.Add($"not printed: {item}");
            }
            else if (!printed.Contains(item))
            {
                problems.Add($"not printed: {item}");
            }
        }
        foreach (string line in printed)
        {
            if (!keys.Contains(PyText.Split(line, " ", 1)[0])) problems.Add($"printed, not written: {line}");
        }
        return problems;
    }

    /// <summary>Why the output of -pX is not an XMP packet, or null. Well-formedness is System.Xml's, which rejects
    /// what expat rejects; the text of a parse failure is System.Xml's too, but for an empty text, which is expat's.</summary>
    public static string? PacketProblem(string output)
    {
        string text = PyText.Strip(output);
        XmlElement root;
        try
        {
            if (text.Length == 0) throw new XmlException("no element found: line 1, column 0");
            var document = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(text), settings)) document.Load(reader);
            root = document.DocumentElement ?? throw new XmlException("no element found");
        }
        catch (XmlException error)
        {
            return $"the packet is not well-formed XML: {error.Message}";
        }
        bool IsRdf(XmlElement element) => element.LocalName == "RDF" && element.NamespaceURI == RdfNamespace;
        if (!IsRdf(root) && !root.GetElementsByTagName("RDF", RdfNamespace).Cast<XmlElement>().Any()) return "the packet holds no rdf:RDF element";
        return null;
    }

    /// <summary>Every problem found, as (file, message), and the number of files checked.</summary>
    public static (List<(string Name, string Message)> Problems, int Count) Check(string directory, string exiv2, TextRunner runner)
    {
        directory = PyPath.Normalize(directory);
        var entries = ParseManifest(PyFile.ReadText(PyPath.Join(directory, "manifest.tsv")));
        var problems = new List<(string, string)>();
        foreach (var (name, expected) in entries)
        {
            string path = PyPath.Join(directory, name);
            var (code, output, errors) = runner([exiv2, "-pa"], path);
            if (code != 0)
            {
                problems.Add((name, $"-pa: not read (exit {code}): {PyText.Head(PyText.Strip(errors), 300)}"));
            }
            else
            {
                var found = Compare(output, expected);
                // The whole print-out goes with the first problem: one run shows what the tool prints.
                if (found.Count > 0) found[0] += "\nprinted:\n" + string.Join("\n", XmpLines(output));
                problems.AddRange(found.Select(problem => (name, problem)));
            }
            if (name.ToLowerInvariant().EndsWith(Sidecar, StringComparison.Ordinal))
            {
                // The tool has no -pX for a sidecar: the file is the packet, and is held to the same rule.
                string? sidecar = PacketProblem(PyFile.ReadText(path));
                if (sidecar is not null) problems.Add((name, "the sidecar: " + sidecar));
                continue;
            }
            (code, output, errors) = runner([exiv2, "-pX"], path);
            if (code != 0)
            {
                problems.Add((name, $"-pX: not read (exit {code}): {PyText.Head(PyText.Strip(errors), 300)}"));
            }
            else
            {
                string? problem = PacketProblem(output);
                if (problem is not null) problems.Add((name, "-pX: " + problem));
            }
        }
        return (problems, entries.Count);
    }

    public static int Run(string[] argv) => Execute(argv, EditChecks.RunText);

    public static int Execute(IReadOnlyList<string> argv, TextRunner runner)
    {
        var parser = new ArgParser("xmp-edits", "The pinned exiv2 must print every XMP property an edit by path wrote, as it was written.")
            .Add("directory")
            .Add("--exiv2", required: true)
            .Add("--title", @default: "XMP edits");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string title = args.Str("title");
        var (problems, count) = Check(args.Str("directory"), args.Str("exiv2"), runner);
        if (EditChecks.Report(title, problems, count, "file(s)", head: 6000)) return 1;
        Out.Print($"::notice title={title}::{count} files after an XMP edit are read by the pinned tool; "
                  + "every property is printed as it was written and no other, and the packet is taken out whole");
        return 0;
    }
}
