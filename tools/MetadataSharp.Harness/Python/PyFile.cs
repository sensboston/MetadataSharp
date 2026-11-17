using System.Text;

namespace MetadataSharp.Harness.Python;

// Path.read_text, write_text and friends: UTF-8, and text-mode reading's universal newlines.
internal static class PyFile
{
    private static readonly UTF8Encoding Strict = new(false, true);
    private static readonly UTF8Encoding Replacing = new(false, false);

    /// <summary>
    /// Path(path).read_text(encoding="utf-8"[, errors="replace"]): decoded, \r\n and \r read as \n. A file
    /// that cannot be read raises PyOSError as Python's OSError reads.
    /// </summary>
    public static string ReadText(string path, bool replace = false)
    {
        byte[] data = ReadBytes(path);
        string text = (replace ? Replacing : Strict).GetString(data);
        return PyText.UniversalNewlines(text);
    }

    /// <summary>Path(path).write_text(text, encoding="utf-8") on a POSIX system: the text as written.</summary>
    public static void WriteText(string path, string text) => WriteBytes(path, Strict.GetBytes(text));

    /// <summary>open(path, "a", encoding="utf-8").write(text) on a POSIX system.</summary>
    public static void AppendText(string path, string text)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(Strict.GetBytes(text));
        }
        catch (Exception error) when (PyOSError.From(error, path) is { } python)
        {
            throw python;
        }
    }

    /// <summary>Path(path).write_bytes(data).</summary>
    public static void WriteBytes(string path, byte[] data)
    {
        try
        {
            File.WriteAllBytes(path, data);
        }
        catch (Exception error) when (PyOSError.From(error, path) is { } python)
        {
            throw python;
        }
    }

    /// <summary>Path(path).read_bytes().</summary>
    public static byte[] ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception error) when (PyOSError.From(error, path) is { } python)
        {
            throw python;
        }
    }

    /// <summary>Path(path).mkdir(parents=True, exist_ok=True).</summary>
    public static void MakeDirs(string path) => Directory.CreateDirectory(path);

    /// <summary>A temporary directory that is removed with everything in it, as tempfile.TemporaryDirectory.</summary>
    public static TemporaryDirectory TempDir(string prefix = "tmp") => new(prefix);
}

// tempfile.TemporaryDirectory(prefix=...): created at once, removed on dispose.
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string prefix)
    {
        string root = System.IO.Path.GetTempPath();
        do
        {
            Path = PyPath.Normalize(System.IO.Path.Combine(root, prefix + Guid.NewGuid().ToString("N")[..8]));
        } while (Directory.Exists(Path));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The directory's path.</summary>
    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open by a child leaves the directory behind, as the cleanup error would.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
