using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>Runs the pinned tool with its options on one file; the edit checks take one so that their tests can stand
/// in for the tool.</summary>
internal delegate ToolResult TextRunner(IReadOnlyList<string> command, string path);

/// <summary>What a child left when its standard output is kept as bytes.</summary>
internal readonly record struct BytesResult(int Code, byte[] Stdout, string Stderr);

/// <summary>Runs the pinned tool with its options on one file, its standard output kept as bytes.</summary>
internal delegate BytesResult BytesRunner(IReadOnlyList<string> command, string path);

// What the checks of edited files (tiff-based-edits, xmp-edits, iptc-edits, embedded-edits, png-chunk-edits) share:
// the way they run the pinned tool and the way they report a problem.
internal static class EditChecks
{
    public const int MaxAnnotations = 20;

    /// <summary>subprocess.run(command + [path], capture_output=True, timeout=60), both outputs decoded with
    /// replacement; 124 when the tool does not finish.</summary>
    public static ToolResult RunText(IReadOnlyList<string> command, string path)
    {
        try
        {
            var done = PyProcess.Run([.. command, path], timeout: 60L, text: false);
            return new(done.ReturnCode, PyText.Utf8Replace(done.StdoutBytes), PyText.Utf8Replace(done.StderrBytes));
        }
        catch (PyTimeoutExpired)
        {
            return new(124, "", "the tool did not finish in 60 seconds");
        }
    }

    /// <summary>The same with a timeout of 120 seconds and the standard output as bytes.</summary>
    public static BytesResult RunBytes(IReadOnlyList<string> command, string path)
    {
        try
        {
            var done = PyProcess.Run([.. command, path], timeout: 120L, text: false);
            return new(done.ReturnCode, done.StdoutBytes, PyText.Utf8Replace(done.StderrBytes));
        }
        catch (PyTimeoutExpired)
        {
            return new(124, [], "the tool did not finish in 120 seconds");
        }
    }

    /// <summary>A message as the data of a workflow command.</summary>
    public static string Escape(string message) =>
        message.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);

    /// <summary>The problems as annotations (the first twenty) and FAIL lines, and the count; false when there are none.</summary>
    public static bool Report(string title, List<(string Name, string Message)> problems, int count, string what, int head = -1)
    {
        foreach (var (name, message) in problems.Take(MaxAnnotations))
            Out.Print($"::error title={title} {name}::{Escape(head < 0 ? message : PyText.Head(message, head))}");
        foreach (var (name, message) in problems) Out.Print($"FAIL {name}: {message}");
        if (problems.Count == 0) return false;
        Out.Print($"{problems.Count} problem(s) in {count} {what}");
        return true;
    }

    /// <summary>text.split(None, 3) folded back with single spaces.</summary>
    public static string Folded(List<string> parts) => string.Join(" ", parts);

    private static readonly byte[] AsciiWhitespace = " \t\n\r\x0b\x0c"u8.ToArray();

    /// <summary>bytes.splitlines(keepends=True): \n, \r and \r\n end a line.</summary>
    public static List<byte[]> SplitLinesKeepEnds(byte[] data)
    {
        var lines = new List<byte[]>();
        int start = 0, i = 0;
        while (i < data.Length)
        {
            if (data[i] == (byte)'\n' || data[i] == (byte)'\r')
            {
                int end = data[i] == (byte)'\r' && i + 1 < data.Length && data[i + 1] == (byte)'\n' ? i + 2 : i + 1;
                lines.Add(data[start..end]);
                start = i = end;
                continue;
            }
            i++;
        }
        if (start < data.Length) lines.Add(data[start..]);
        return lines;
    }

    /// <summary>bytes.splitlines(): the same lines without their ends.</summary>
    public static List<byte[]> SplitLines(byte[] data) =>
        SplitLinesKeepEnds(data).Select(line => line.Length > 0 && line[^1] == (byte)'\n' ? line[..(line.Length >= 2 && line[^2] == (byte)'\r' ? ^2 : ^1)]
                                            : line.Length > 0 && line[^1] == (byte)'\r' ? line[..^1] : line).ToList();

    /// <summary>bytes.split(None, 1)[:1]: the first field of a line, or null for a line of whitespace.</summary>
    public static byte[]? FirstField(byte[] line)
    {
        int start = 0;
        while (start < line.Length && AsciiWhitespace.Contains(line[start])) start++;
        if (start == line.Length) return null;
        int end = start;
        while (end < line.Length && !AsciiWhitespace.Contains(line[end])) end++;
        return line[start..end];
    }

    /// <summary>bytes.hex() of the first bytes, for a message.</summary>
    public static string Hex(byte[] data, int count) => PyText.Hex(data.AsSpan(0, Math.Min(count, data.Length)));
}
