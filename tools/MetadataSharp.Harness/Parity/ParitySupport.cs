using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// The report run cannot be trusted because its input or tool failed structurally.
internal sealed class StructuralError(string message) : Exception(message);

// Shared helpers of the preview, ICC and soundness reports (formerly tools/parity/parity_support.py).
internal static class ParitySupport
{
    public const int ChunkLimit = 10;
    public const int ChunkSize = 4000;

    /// <summary>The pinned reference's stdout for one file; with requireSuccess a failing run is structural.</summary>
    public static string RunReference(IReadOnlyList<string> referenceCommand, string path, bool requireSuccess = false)
    {
        Completed finished;
        try
        {
            finished = PyProcess.Run(referenceCommand.Concat([PyPath.Normalize(path)]).ToList(), timeout: 300L);
        }
        catch (PyOSError error) when (error.Errno == 2)
        {
            throw new StructuralError($"the pinned reference tool is not runnable: {error.Message}");
        }
        catch (PyTimeoutExpired)
        {
            throw new StructuralError($"the pinned reference tool timed out on {PyPath.Name(path)}");
        }
        if (requireSuccess && finished.ReturnCode != 0)
        {
            string detail = PyText.Head(PyText.Strip((string)Py.Or(Py.Or(finished.Stderr, finished.Stdout), "")!).Replace("\n", " ", StringComparison.Ordinal), 400);
            throw new StructuralError($"pinned reference failed on {PyPath.Name(path)} with {finished.ReturnCode}: {detail}");
        }
        return finished.Stdout;
    }

    /// <summary>The payload as compressed, chunked notices; the number of chunks is limited to 1..10.</summary>
    public static int EmitAnnotations(PyDict payload, string title, string word = "summary")
    {
        string encoded = Out.Compressed(PyJson.Compact(payload, sortKeys: true, ensureAscii: false), 9);
        var parts = new List<string>();
        for (int i = 0; i < encoded.Length; i += ChunkSize) parts.Add(encoded.Substring(i, Math.Min(ChunkSize, encoded.Length - i)));
        if (parts.Count == 0 || parts.Count > ChunkLimit)
            throw new StructuralError($"the {word} requires {parts.Count} annotations; allowed 1..{ChunkLimit}");
        for (int number = 1; number <= parts.Count; number++) Out.Print($"::notice title={title} {word} {number}/{parts.Count}::{parts[number - 1]}");
        return parts.Count;
    }
}
