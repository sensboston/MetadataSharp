using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Ci;

// Emit concise Roslyn inventory findings as GitHub check annotations: the summary of an inventory that
// tools/MetadataSharp.DocInventory wrote, then its public gaps and its undocumented non-trivial internal
// declarations, deflated and in base64, in annotations of at most 5600 characters of rows each.
// (Formerly tools/MetadataSharp.DocInventory/report.py; the arguments, the annotations and the exit status are
// unchanged.)
internal static class DocInventoryReport
{
    /// <summary>The rows of one category, in chunks of at most 5600 characters, one annotation a chunk.</summary>
    public static void Emit(string project, string category, IReadOnlyList<string> rows)
    {
        var chunks = new List<string>();
        var current = new List<string>();
        int size = 0;
        foreach (string row in rows)
        {
            int length = Py.Len(row);
            if (current.Count > 0 && size + length + 1 > 5600)
            {
                chunks.Add(string.Join("\n", current));
                current.Clear();
                size = 0;
            }
            current.Add(row);
            size += length + 1;
        }
        if (current.Count > 0) chunks.Add(string.Join("\n", current));

        for (int number = 1; number <= chunks.Count; number++)
        {
            string encoded = Out.Compressed(chunks[number - 1], 9);
            string title = $"Roslyn baseline {project} {category} {number}/{chunks.Count}";
            Out.Print($"::notice title={title}::{encoded}");
        }
    }

    public static int Run(string[] argv)
    {
        if (argv.Length != 2)
        {
            Out.Error("Usage: doc-inventory-report PROJECT INVENTORY.json");
            return 2;
        }
        string project = argv[0], reportPath = argv[1];
        object? report = PyJson.Loads(PyFile.ReadText(reportPath));
        Out.Print($"::notice title=Roslyn baseline {project} summary::{PyJson.Compact(PyOps.Item(report, "Summary"))}");

        var publicRows = new List<string>();
        var internalRows = new List<string>();
        foreach (object? entry in PyOps.Iterate(PyOps.Item(report, "Declarations")))
        {
            string location = $"{Py.Str(PyOps.Item(entry, "Path"))}:{Py.Str(PyOps.Item(entry, "Line"))}";
            object? gaps = PyOps.Item(entry, "PublicGaps");
            if (Py.Truthy(gaps))
            {
                publicRows.Add($"{location} {Py.Str(PyOps.Item(entry, "Name"))} missing {PyOps.Join(", ", PyOps.Iterate(gaps))}");
            }
            else if (!Py.Truthy(PyOps.Item(entry, "Public")) && Py.Truthy(PyOps.Item(entry, "NonTrivial")) && !Py.Truthy(PyOps.Item(entry, "PurposeComment")))
            {
                internalRows.Add($"{location} {Py.Str(PyOps.Item(entry, "Kind"))} {Py.Str(PyOps.Item(entry, "Name"))}");
            }
        }
        Emit(project, "public", publicRows);
        Emit(project, "internal", internalRows);
        return 0;
    }
}
