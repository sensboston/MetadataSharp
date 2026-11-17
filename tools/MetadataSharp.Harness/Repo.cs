using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness;

// The repository checkout the harness runs from: the directory that holds MetadataSharp.sln above the
// executable, so a gate finds the catalog, fixtures and manifests wherever it is started.
internal static class Repo
{
    /// <summary>The absolute path of the repository root, with '/' separators.</summary>
    public static string Root { get; } = Find();

    /// <summary>A path below the repository root.</summary>
    public static string Path(params string[] parts) => PyPath.Join(Root, parts);

    private static string Find()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(System.IO.Path.Combine(directory.FullName, "MetadataSharp.sln"))) return PyPath.Normalize(directory.FullName);
                directory = directory.Parent;
            }
        }
        return PyPath.Normalize(Directory.GetCurrentDirectory());
    }
}

// Output helpers: print() to stdout or stderr with Python's line ending, and GitHub workflow commands.
internal static class Out
{
    /// <summary>print(text).</summary>
    public static void Print(string text = "") => Console.Out.Write(text + "\n");

    /// <summary>print(text, file=sys.stderr).</summary>
    public static void Error(string text) => Console.Error.Write(text + "\n");

    /// <summary>The base64 text of a zlib stream of the UTF-8 bytes, as the annotations carry evidence.</summary>
    public static string Compressed(string text, int level = -1) => Convert.ToBase64String(Zlib.Compress(PyText.Utf8(text), level));
}
