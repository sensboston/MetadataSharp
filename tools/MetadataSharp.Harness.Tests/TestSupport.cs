using System.Text;
using MetadataSharp.Harness.Python;
using Xunit;

// The gates print to the console and read the environment, both of which belong to the process: the tests
// run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MetadataSharp.Harness.Tests;

internal static class TestSupport
{
    /// <summary>A JSON document as the gates hold one: PyDict, List of object, long, double, string, bool or null.</summary>
    public static object? J(string json) => PyJson.Loads(json);

    /// <summary>A JSON object.</summary>
    public static PyDict D(string json) => (PyDict)PyJson.Loads(json)!;

    /// <summary>
    /// assertEqual of JSON-shaped values as Python compares them: dicts without regard to their order, lists and
    /// everything else exactly. Both sides are compared as canonical JSON text, so a difference shows as text.
    /// </summary>
    public static void AssertJson(string expectedJson, object? actual) => AssertJsonEqual(J(expectedJson), actual);

    /// <summary>A value as canonical JSON text (sorted keys), for comparing values the gates hold.</summary>
    public static string Json(object? value) => PyJson.Dumps(value, sortKeys: true);

    /// <summary>The same comparison of two values the gates hold.</summary>
    public static void AssertJsonEqual(object? expected, object? actual) =>
        Assert.Equal(PyJson.Dumps(expected, sortKeys: true), PyJson.Dumps(actual, sortKeys: true));

    /// <summary>Run a gate with its console captured: its exit status, stdout and stderr.</summary>
    public static (int Code, string Stdout, string Stderr) Capture(Func<int> run)
    {
        TextWriter stdout = Console.Out, stderr = Console.Error;
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            int code = run();
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
    }

    /// <summary>Set environment variables for the duration of a block, as unittest's patch.dict(os.environ).</summary>
    public static IDisposable Environment(params (string Name, string? Value)[] values) => new EnvironmentScope(values);

    /// <summary>A fresh directory below the system temporary directory, deleted on dispose.</summary>
    public static TempDirectory Temp() => new();

    /// <summary>Write a UTF-8 text file, creating its directory.</summary>
    public static string Write(string path, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// An executable script that stands in for a tool: a POSIX shell script, or a .cmd file on Windows. The tests
    /// that need one are skipped on Windows by returning null.
    /// </summary>
    public static string? Script(string directory, string name, string body)
    {
        if (OperatingSystem.IsWindows()) return null;
        string path = System.IO.Path.Combine(directory, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body, new UTF8Encoding(false));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly List<(string Name, string? Value)> previous = [];

        public EnvironmentScope((string Name, string? Value)[] values)
        {
            foreach (var (name, value) in values)
            {
                previous.Add((name, System.Environment.GetEnvironmentVariable(name)));
                System.Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in previous) System.Environment.SetEnvironmentVariable(name, value);
        }
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = PyPath.Normalize(Directory.CreateTempSubdirectory("harness-tests-").FullName);
    }

    public string Path { get; }

    public string this[params string[] parts] => PyPath.Join(Path, parts);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still open on Windows: the directory is left to the system.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
