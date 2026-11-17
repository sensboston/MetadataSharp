using System.Text;
using System.Xml;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Ci;

// The TRX results of the test runs of a build leg (TestResults/**/*.trx): the failed tests as annotations, and the
// gate that the tests ran at all - a result file, its counters, every test passed, and at least 450 of them.
// (Formerly the Python steps "Report failed test details" and "Verify tests actually ran" of build-and-test.yml;
// the annotations, the messages and the threshold are unchanged.)
internal static class Trx
{
    public const string Namespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>The fewest tests a leg may run.</summary>
    public const int Minimum = 450;

    /// <summary>Path(directory).rglob('*.trx'): the files in the order the file system lists them, directory by
    /// directory as pathlib walks them.</summary>
    public static List<string> Files(string directory)
    {
        var found = new List<string>();
        string root = PyPath.Normalize(directory);
        if (!Directory.Exists(root)) return found;
        var regex = PyPath.FnMatch("*.trx");
        // Python 3.12 walks top-down and lists the subdirectories of each directory it reaches before it goes
        // into the first of them.
        var directories = new List<string> { root };
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            var children = Directory.EnumerateDirectories(current).Select(entry => PyPath.Join(current, System.IO.Path.GetFileName(entry)))
                                    .Where(child => new DirectoryInfo(child).LinkTarget is null).ToList();
            directories.AddRange(children);
            for (int i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
        foreach (string current in directories)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                string name = System.IO.Path.GetFileName(entry);
                if (regex.IsMatch(name)) found.Add(PyPath.Join(current, name));
            }
        }
        return found;
    }

    private static XmlElement Load(string path)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        document.Load(reader);
        return document.DocumentElement!;
    }

    private static bool IsText(XmlNode node) => node is XmlText or XmlCDataSection or XmlWhitespace or XmlSignificantWhitespace;

    /// <summary>Element.itertext() of ElementTree: the text of the element, then of each child and its tail; the
    /// character data between two tags is one piece, whatever comments it holds.</summary>
    public static IEnumerable<string> IterText(XmlElement element)
    {
        var pieces = new List<string>();
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length > 0) pieces.Add(text.ToString());
            text.Clear();
        }
        foreach (XmlNode child in element.ChildNodes)
        {
            if (IsText(child))
            {
                text.Append(child.Value);
            }
            else if (child is XmlElement inner)
            {
                Flush();
                pieces.AddRange(IterText(inner));
            }
        }
        Flush();
        return pieces;
    }

    private static string? Attribute(XmlElement element, string name) => element.HasAttribute(name) ? element.GetAttribute(name) : null;

    /// <summary>Every failed test of every result file, as an error annotation.</summary>
    public static int Failures(string[] argv)
    {
        string directory = argv.Length > 0 ? argv[0] : "TestResults";
        foreach (string path in Files(directory))
        {
            foreach (var result in Load(path).GetElementsByTagName("UnitTestResult", Namespace).Cast<XmlElement>())
            {
                if (Attribute(result, "outcome") != "Failed") continue;
                string text = (Attribute(result, "testName") ?? "") + ": " + string.Join(" ", IterText(result));
                text = PyText.Head(text, 6000).Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);
                Out.Print("::error title=Unit test failure::" + text);
            }
        }
        return 0;
    }

    /// <summary>The gate: result files with counters, every test passed, and at least the minimum of them.</summary>
    public static int Count(string[] argv)
    {
        string directory = argv.Length > 0 ? argv[0] : "TestResults";
        var files = Files(directory);
        if (files.Count == 0) throw new SystemExit(1, "No TRX results found");
        long total = 0;
        foreach (string file in files)
        {
            var root = Load(file);
            var counters = root.ChildNodes.OfType<XmlElement>().Where(element => element.LocalName == "ResultSummary" && element.NamespaceURI == Namespace)
                               .SelectMany(summary => summary.ChildNodes.OfType<XmlElement>())
                               .FirstOrDefault(element => element.LocalName == "Counters" && element.NamespaceURI == Namespace);
            if (counters is null) throw new SystemExit(1, $"No test counters in {file}");
            var attributes = new PyDict();
            foreach (XmlAttribute attribute in counters.Attributes)
            {
                if (attribute.Name == "xmlns" || attribute.Prefix == "xmlns") continue;
                attributes[attribute.NamespaceURI.Length > 0 ? $"{{{attribute.NamespaceURI}}}{attribute.LocalName}" : attribute.LocalName] = attribute.Value;
            }
            long count = PyText.ParseInt((string)(attributes.Get("total") ?? throw new PyException("KeyError", "'total'")));
            long passed = PyText.ParseInt((string)(attributes.Get("passed") ?? throw new PyException("KeyError", "'passed'")));
            if (count != passed) throw new SystemExit(1, $"Not all tests passed in {file}: {Py.Repr(attributes)}");
            total += count;
        }
        if (total < Minimum) throw new SystemExit(1, $"Expected at least 450 M01–M16 cases, got {total}");
        Out.Print($"::notice title=Executed test count::Validated {total} passing tests");
        return 0;
    }
}
