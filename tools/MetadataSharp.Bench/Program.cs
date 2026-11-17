using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using MetadataSharp.Formats;

namespace MetadataSharp.Bench;

/// <summary>The entry point of the measurement tool.</summary>
public static class Program
{
    /// <summary>Runs the benchmarks this assembly carries.</summary>
    /// <param name="args">BenchmarkDotNet filters, passed through: a class name runs one series.</param>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}


/// <summary>
/// Runs every benchmark under one short job, so a run finishes in minutes and a hundredth of a
/// millisecond is not read as noise.
/// </summary>
/// <remarks>
/// The measurements are a report, not a gate: they compare the whole-file read of the same library
/// with the range read of the same library, on the same file, in the same process. Absolute
/// numbers belong to the machine that produced them; the ratio does not.
/// </remarks>
public sealed class ShortJob : ManualConfig
{
    /// <summary>Configures the job from the environment of the run.</summary>
    public ShortJob()
    {
        // One dry iteration over the whole corpus keeps a CI leg inside its timeout; a local
        // run without METADATASHARP_BENCH_QUICK gets the short job instead.
        AddJob(Environment.GetEnvironmentVariable("METADATASHARP_BENCH_QUICK") == "1"
            ? Job.Dry.WithId("quick")
            : Job.ShortRun.WithId("short"));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(BenchmarkDotNet.Exporters.Json.JsonExporter.Default);
    }
}

/// <summary>
/// Reads one file four ways: preloaded bytes, whole-file I/O plus parsing, a path, and a stream.
/// </summary>
/// <remarks>
/// <c>File.ReadAllBytes + Read</c> is the previous path behaviour and is the baseline; <c>path</c> and
/// <c>stream</c> read the ranges the parsers ask for. The difference is the change this tool was
/// written to measure: a fixed cost of opening a file against a cost that grows with the file.
/// </remarks>
[Config(typeof(ShortJob))]
public class ReadBenchmarks
{
    private byte[] bytes = [];

    /// <summary>The file this series measures, named by <see cref="Corpus"/>.</summary>
    [ParamsSource(nameof(Files))]
    public string File { get; set; } = string.Empty;

    /// <summary>The files to measure: one per format, largest first.</summary>
    /// <returns>The paths.</returns>
    public IEnumerable<string> Files => Corpus.Files();

    /// <summary>Holds the file in memory once, so the array run measures parsing alone.</summary>
    [GlobalSetup]
    public void Setup() => bytes = System.IO.File.ReadAllBytes(File);

    /// <summary>Reads the file the way every earlier release did: all of it, from memory.</summary>
    /// <returns>The metadata.</returns>
    [Benchmark(Description = "Read(byte[]) - preloaded parsing only")]
    public ImageMetadata FromArray() => ImageMetadata.Read((ReadOnlySpan<byte>)bytes);

    /// <summary>Reads the entire file and parses it, including I/O and allocation.</summary>
    /// <returns>The metadata.</returns>
    [Benchmark(Baseline = true, Description = "File.ReadAllBytes + Read")]
    public ImageMetadata FromWholeFile() => ImageMetadata.Read((ReadOnlySpan<byte>)System.IO.File.ReadAllBytes(File));

    /// <summary>Reads the file from its path, where the readers ask for ranges.</summary>
    /// <returns>The metadata.</returns>
    [Benchmark(Description = "Read(path) - the ranges only")]
    public ImageMetadata FromPath() => ImageMetadata.Read(File);

    /// <summary>Reads the file from a fresh stream, where the readers ask for ranges.</summary>
    /// <returns>The metadata.</returns>
    [Benchmark(Description = "Read(Stream) - the ranges only")]
    public ImageMetadata FromStream()
    {
        using FileStream stream = System.IO.File.OpenRead(File);
        return ImageMetadata.Read(stream);
    }
}

/// <summary>
/// Chooses the files to measure from the environment of the run.
/// </summary>
internal static class Corpus
{
    private const int PerRun = 6;

    /// <summary>One file per extension in <c>METADATASHARP_BENCH_DIR</c>, largest first.</summary>
    /// <returns>The paths, or a small generated file when the directory holds none.</returns>
    /// <remarks>
    /// The largest file of an extension is the one whose whole-file read costs most, which is where
    /// a range read shows. A directory that does not exist is not an error: the run then measures
    /// the fixtures of the repository, and a repository without fixtures gets a file of its own, so
    /// that a benchmark run never fails for want of input.
    /// </remarks>
    internal static IEnumerable<string> Files()
    {
        string root = Environment.GetEnvironmentVariable("METADATASHARP_BENCH_DIR") is { Length: > 0 } dir
            ? dir
            : RepositoryRoot() is { } repository ? Path.Combine(repository, "tests", "fixtures") : string.Empty;
        if (root.Length != 0 && Directory.Exists(root))
        {
            string[] chosen = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .GroupBy(file => file.Extension.ToLowerInvariant())
                .Select(group => group.OrderByDescending(file => file.Length).First())
                .OrderByDescending(file => file.Length)
                .Take(PerRun)
                .Select(file => file.FullName)
                .ToArray();
            if (chosen.Length != 0)
                return chosen;
        }
        return [WriteSample()];
    }

    private static string WriteSample()
    {
        string path = Path.Combine(Path.GetTempPath(), "metadatasharp-bench.bmp");
        if (!System.IO.File.Exists(path))
        {
            // A 24-bit BMP header and its first pixel row: enough for ImageMetadata.Read to answer.
            byte[] header = new byte[54 + 3];
            header[0] = (byte)'B';
            header[1] = (byte)'M';
            BitConverter.TryWriteBytes(header.AsSpan(2), header.Length);
            BitConverter.TryWriteBytes(header.AsSpan(10), 54);
            BitConverter.TryWriteBytes(header.AsSpan(14), 40);
            System.IO.File.WriteAllBytes(path, header);
        }
        return path;
    }

    private static string? RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (System.IO.File.Exists(Path.Combine(directory.FullName, "MetadataSharp.sln")))
                return directory.FullName;
        }
        return null;
    }
}
