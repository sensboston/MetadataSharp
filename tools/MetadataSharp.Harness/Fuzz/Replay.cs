using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Fuzz;

// Runs inputs through the harness, input by input, restarting it after a death or a hang. The harness reads its
// inputs from the list file it is given (@FILE) and reports each one on its output: "BEGIN path", then "PASS ms
// path" or "FAIL ms path" with the failure's lines ("| ..."), or a "JSON {...}" record of an extraction, then
// "END". An input it does not finish within its budget is a hang; one during which it exits is a death.
internal sealed class Replay(IReadOnlyList<string> command, IReadOnlyDictionary<string, string>? env, TextWriter log)
{
    /// <summary>path -> (status, milliseconds): "pass", "fail", "hang" or "died", in the order first reported.</summary>
    public OrderedDictionary<string, (string Status, long Ms)> Results { get; } = new(StringComparer.Ordinal);

    /// <summary>The failures, each a dict: kind (exception, hang, died, harness), path and detail.</summary>
    public List<object?> Failures { get; } = [];

    /// <summary>The JSON records of an extraction.</summary>
    public List<object?> Records { get; } = [];

    public void Run(IEnumerable<string> paths, string listFile)
    {
        var all = paths.ToList();
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string path in all) sizes[path] = Size(path);
        var pending = all.ToList();
        while (pending.Count > 0)
        {
            PyFile.WriteText(listFile, string.Join("\n", pending) + "\n");
            int before = Results.Count;
            OneProcess(listFile, sizes);
            pending = pending.Where(path => !Results.ContainsKey(path)).ToList();
            if (Results.Count == before)
            {
                Failures.Add(PyDict.Of(("kind", "harness"), ("path", pending[0]), ("detail", "The harness made no progress.")));
                break;
            }
        }
    }

    private static long Size(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (FileNotFoundException)
        {
            throw PyOSError.For(2, path);
        }
    }

    private void OneProcess(string listFile, Dictionary<string, long> sizes)
    {
        using var child = MergedChild.Start([.. command, "@" + listFile], env);
        string? current = null, status = null;
        var started = Stopwatch.StartNew();
        var detail = new List<string>();
        try
        {
            while (true)
            {
                double wait = current is null ? 120.0 : Math.Max(0.1, FuzzRun.Budget(sizes.GetValueOrDefault(current)) - started.Elapsed.TotalSeconds);
                if (!child.Lines.TryTake(out string? line, TimeSpan.FromSeconds(wait)))
                {
                    child.Kill();
                    if (current is not null)
                    {
                        double seconds = Py.Round(started.Elapsed.TotalSeconds, 1);
                        Results[current] = ("hang", (long)(seconds * 1000));
                        Failures.Add(PyDict.Of(("kind", "hang"), ("path", current), ("seconds", seconds), ("budget", Py.Round(FuzzRun.Budget(sizes.GetValueOrDefault(current)), 1)),
                                               ("detail", string.Concat(detail.TakeLast(100)))));
                    }
                    return;
                }
                if (line is null)
                {
                    int code = child.Wait();
                    if (current is not null)
                    {
                        Results[current] = ("died", 0);
                        Failures.Add(PyDict.Of(("kind", "died"), ("path", current), ("exit_code", (long)code), ("detail", string.Concat(detail.TakeLast(200)))));
                    }
                    return;
                }
                log.Write(line);
                string text = line.EndsWith('\n') ? line.TrimEnd('\n') : line;
                if (text.StartsWith("BEGIN ", StringComparison.Ordinal))
                {
                    (current, status) = (text[6..], null);
                    started.Restart();
                    detail = [];
                }
                else if ((text.StartsWith("PASS ", StringComparison.Ordinal) || text.StartsWith("FAIL ", StringComparison.Ordinal)) && current is not null)
                {
                    status = text[..4];
                    Results[current] = (status.ToLowerInvariant(), PyText.ParseInt(text.Split(' ', 3)[1]));
                }
                else if (text.StartsWith("JSON ", StringComparison.Ordinal) && current is not null)
                {
                    Records.Add(PyJson.Loads(text[5..]));
                    Results[current] = ("pass", 0);
                }
                else if (text == "END" && current is not null)
                {
                    if (status == "FAIL")
                        Failures.Add(PyDict.Of(("kind", "exception"), ("path", current), ("ms", Results[current].Ms), ("detail", string.Join("\n", detail.Take(200)))));
                    current = null;
                }
                else
                {
                    detail.Add(text.StartsWith("| ", StringComparison.Ordinal) ? text[2..] : text);
                }
            }
        }
        finally
        {
            child.Kill();
            child.Wait();
            child.JoinReaders(TimeSpan.FromSeconds(10));
            log.Flush();
        }
    }
}

// A child process whose stdout and stderr arrive as one stream of lines, as Popen(stdout=PIPE, stderr=STDOUT,
// text=True, errors='replace') gives them: decoded as UTF-8 with replacement, with universal newlines, each line
// with its "\n" but the last when the output does not end with one. Outside Windows the shell hands the child one
// pipe for both streams (exec, so the child is the process killed and waited for); on Windows the two pipes are
// read line by line into the same queue.
internal sealed class MergedChild : IDisposable
{
    private readonly Process process;
    private readonly List<Thread> readers = [];
    private int open;

    private MergedChild(Process process) => this.process = process;

    /// <summary>The lines in the order they arrived; a null line marks the end of the output.</summary>
    public BlockingCollection<string?> Lines { get; } = new();

    public static MergedChild Start(IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env)
    {
        bool shell = !OperatingSystem.IsWindows();
        var info = new ProcessStartInfo(shell ? "/bin/sh" : args[0])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = !shell,
            RedirectStandardInput = false,
        };
        if (shell)
        {
            string program = args[0].Contains('/') ? args[0] : PyShutil.Which(args[0]) ?? throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
            foreach (string arg in new[] { "-c", "exec \"$0\" \"$@\" 2>&1", program }) info.ArgumentList.Add(arg);
        }
        foreach (string arg in args.Skip(1)) info.ArgumentList.Add(arg);
        if (env is not null)
        {
            info.Environment.Clear();
            foreach (var pair in env) info.Environment[pair.Key] = pair.Value;
        }
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
        }
        var child = new MergedChild(process);
        child.Read(process.StandardOutput.BaseStream);
        if (!shell) child.Read(process.StandardError.BaseStream);
        return child;
    }

    private void Read(Stream stream)
    {
        Interlocked.Increment(ref open);
        var thread = new Thread(() =>
        {
            try
            {
                foreach (string line in ReadLines(stream)) Lines.Add(line);
            }
            catch (IOException)
            {
                // The pipe closed under the reader: the end of the output.
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                if (Interlocked.Decrement(ref open) == 0) Lines.Add(null);
            }
        }) { IsBackground = true };
        readers.Add(thread);
        thread.Start();
    }

    /// <summary>The lines of a stream as Python's text mode reads them: "\r\n" and "\r" become "\n".</summary>
    internal static IEnumerable<string> ReadLines(Stream stream)
    {
        var decoder = new UTF8Encoding(false, false).GetDecoder();
        var buffer = new byte[8192];
        var chars = new char[8192 + 4];
        var pending = new StringBuilder();
        bool eof = false;
        while (!eof)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            eof = read == 0;
            int count = decoder.GetChars(buffer, 0, read, chars, 0, flush: eof);
            pending.Append(chars, 0, count);
            int start = 0;
            for (int i = 0; i < pending.Length; i++)
            {
                char c = pending[i];
                if (c == '\n')
                {
                    yield return pending.ToString(start, i - start) + "\n";
                    start = i + 1;
                }
                else if (c == '\r')
                {
                    // A "\r" at the end of what has arrived may be the first half of "\r\n": wait for more.
                    if (i + 1 == pending.Length && !eof) break;
                    yield return pending.ToString(start, i - start) + "\n";
                    if (i + 1 < pending.Length && pending[i + 1] == '\n') i++;
                    start = i + 1;
                }
            }
            pending.Remove(0, start);
        }
        if (pending.Length > 0) yield return pending.ToString();
    }

    public void Kill()
    {
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch (InvalidOperationException)
        {
            // It ended on its own in between.
        }
    }

    public int Wait()
    {
        process.WaitForExit();
        return PyProcess.ReturnCodeOf(process);
    }

    public void JoinReaders(TimeSpan timeout)
    {
        foreach (var thread in readers) thread.Join(timeout);
    }

    public void Dispose()
    {
        Kill();
        process.Dispose();
    }
}

// subprocess.run of afl-fuzz and afl-tmin: stdout and stderr together into a file (or nowhere), killed and waited
// for after a timeout.
internal static class ChildProcess
{
    /// <summary>The exit status, or null when the timeout ran out and the child was killed.</summary>
    public static int? RunToFile(IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env, string? file, long timeoutSeconds)
    {
        using var output = file is null ? Stream.Null : new FileStream(file, FileMode.Create, FileAccess.Write);
        using var child = MergedBytes.Start(args, env, output);
        if (!child.WaitFor(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            child.Kill();
            child.WaitFor(Timeout.InfiniteTimeSpan);
            return null;
        }
        return child.Code;
    }
}

// The raw bytes of a child's stdout and stderr, in the order written outside Windows, copied to a stream.
internal sealed class MergedBytes : IDisposable
{
    private readonly Process process;
    private readonly List<Task> pumps = [];

    private MergedBytes(Process process) => this.process = process;

    public int Code => PyProcess.ReturnCodeOf(process);

    public static MergedBytes Start(IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env, Stream output)
    {
        bool shell = !OperatingSystem.IsWindows();
        var info = new ProcessStartInfo(shell ? "/bin/sh" : args[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = !shell };
        if (shell)
        {
            string program = args[0].Contains('/') ? args[0] : PyShutil.Which(args[0]) ?? throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
            foreach (string arg in new[] { "-c", "exec \"$0\" \"$@\" 2>&1", program }) info.ArgumentList.Add(arg);
        }
        foreach (string arg in args.Skip(1)) info.ArgumentList.Add(arg);
        info.Environment.Clear();
        foreach (var pair in env) info.Environment[pair.Key] = pair.Value;
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
        }
        var child = new MergedBytes(process);
        object gate = new();
        child.pumps.Add(Pump(process.StandardOutput.BaseStream, output, gate));
        if (!shell) child.pumps.Add(Pump(process.StandardError.BaseStream, output, gate));
        return child;
    }

    private static Task Pump(Stream source, Stream target, object gate) => Task.Run(() =>
    {
        var buffer = new byte[65536];
        int read;
        try
        {
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                lock (gate) target.Write(buffer, 0, read);
            }
        }
        catch (IOException)
        {
        }
    });

    public bool WaitFor(TimeSpan timeout)
    {
        if (!process.WaitForExit(timeout)) return false;
        process.WaitForExit();
        Task.WaitAll([.. pumps], TimeSpan.FromSeconds(10));
        return true;
    }

    public void Kill()
    {
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        Kill();
        process.Dispose();
    }
}
