using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Ci;

// Text files turned into workflow annotations, for readers of a run who cannot read its logs: the last lines of a
// failed build's log as one error, and a whole report in notices of a fixed size. (Formerly the Python programs of
// the samples job and of the reading-cost job of build-and-test.yml; the annotations are unchanged.)
internal static class Annotations
{
    /// <summary>annotate-tail TITLE FILE [LINES]: the last lines of the file, joined by " | ", as one error.</summary>
    public static int Tail(string[] argv)
    {
        var parser = new ArgParser("annotate-tail", "The last lines of a text file as one error annotation.")
            .Add("title")
            .Add("file")
            .Add("--lines", isInt: true, @default: 15L);
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        var lines = PyText.SplitLines(PyFile.ReadText(args.Str("file"), replace: true));
        int count = (int)args.Int("lines");
        Out.Print($"::error title={args.Str("title")}::" + string.Join(" | ", lines.Skip(Math.Max(0, lines.Count - count))));
        return 0;
    }

    /// <summary>annotate-chunks TITLE FILE [SIZE]: the whole file in notices of at most SIZE characters each.</summary>
    public static int Chunks(string[] argv)
    {
        var parser = new ArgParser("annotate-chunks", "A text file as notices of a fixed number of characters.")
            .Add("title")
            .Add("file")
            .Add("--size", isInt: true, @default: 3500L);
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string text = PyFile.ReadText(args.Str("file")).Replace("%", "%25", StringComparison.Ordinal);
        foreach (string chunk in PyText.Chunks(text, (int)args.Int("size")))
            Out.Print($"::notice title={args.Str("title")}::" + chunk.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal));
        return 0;
    }
}
