using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MetadataSharp.Harness.Python;

// What a finished child process left: its exit status and its output, as text or as bytes.
internal sealed class Completed(IReadOnlyList<string> args, int returnCode, byte[] stdout, byte[] stderr, bool text, bool strict)
{
    public IReadOnlyList<string> Args { get; } = args;

    public int ReturnCode { get; } = returnCode;

    public byte[] StdoutBytes { get; } = stdout;

    public byte[] StderrBytes { get; } = stderr;

    /// <summary>stdout as subprocess.run(text=True) returns it: decoded and with universal newlines.</summary>
    public string Stdout => text ? PyProcess.Decode(StdoutBytes, strict) : throw new InvalidOperationException("binary output");

    /// <summary>stderr, decoded the same way.</summary>
    public string Stderr => text ? PyProcess.Decode(StderrBytes, strict) : throw new InvalidOperationException("binary output");
}

// OSError and its subclasses (FileNotFoundError, PermissionError, IsADirectoryError, ...): str() is
// "[Errno N] Description: 'path'".
internal sealed class PyOSError(string message, int errno) : Exception(message)
{
    public int Errno { get; } = errno;

    /// <summary>The OSError for errno and the file it concerns.</summary>
    public static PyOSError For(int errno, string path)
    {
        string description = errno switch
        {
            2 => "No such file or directory",
            13 => "Permission denied",
            17 => "File exists",
            20 => "Not a directory",
            21 => "Is a directory",
            39 => "Directory not empty",
            _ => "Input/output error",
        };
        return new PyOSError($"[Errno {errno}] {description}: {Py.ReprString(path)}", errno);
    }

    /// <summary>The OSError Python raises where .NET threw error for path, or null when it is no I/O failure.</summary>
    public static PyOSError? From(Exception error, string path) => error switch
    {
        PyOSError python => python,
        FileNotFoundException or DirectoryNotFoundException => For(Directory.Exists(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? "/")
                                                                    || !HasFileAncestor(path) ? 2 : 20, path),
        UnauthorizedAccessException => For(Directory.Exists(path) ? 21 : 13, path),
        IOException when Directory.Exists(path) => For(21, path),
        IOException => For(5, path),
        _ => null,
    };

    // Whether some ancestor of path is a file, which makes the open fail with ENOTDIR.
    private static bool HasFileAncestor(string path)
    {
        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(directory)) return true;
            directory = System.IO.Path.GetDirectoryName(directory);
        }
        return false;
    }
}

// subprocess.TimeoutExpired.
internal sealed class PyTimeoutExpired(IReadOnlyList<string> command, object timeout)
    : Exception($"Command '{Py.Repr(command.ToList())}' timed out after {Py.Str(timeout)} seconds")
{
    public IReadOnlyList<string> Command { get; } = command;
}

// subprocess.CalledProcessError.
internal sealed class PyCalledProcessError(IReadOnlyList<string> command, int returnCode, string output)
    : Exception($"Command '{Py.Repr(command.ToList())}' returned non-zero exit status {returnCode}.")
{
    public int ReturnCode { get; } = returnCode;

    public string Output { get; } = output;
}

// How a child's stream is wired: captured, discarded, inherited, or (stderr only) merged into stdout.
internal enum Pipe
{
    Capture,
    DevNull,
    Inherit,
    Stdout,
}

// subprocess.run, with the options the gates passed.
internal static class PyProcess
{
    /// <summary>subprocess.run(args, ...): runs to completion, kills the child on timeout and raises.</summary>
    public static Completed Run(IReadOnlyList<string> args, string? input = null, byte[]? inputBytes = null, object? timeout = null,
                                string? cwd = null, IReadOnlyDictionary<string, string>? env = null, bool text = true,
                                bool strict = false, Pipe stdout = Pipe.Capture, Pipe stderr = Pipe.Capture, bool check = false,
                                Pipe stdin = Pipe.Inherit)
    {
        // stderr=STDOUT hands the child one pipe for both streams, so what it writes keeps its order. Outside
        // Windows the shell does that (exec, so the child is the process waited for and killed); on Windows the two
        // pipes are read into one buffer.
        bool shellMerge = stderr == Pipe.Stdout && stdout == Pipe.Capture && !OperatingSystem.IsWindows();
        var info = new ProcessStartInfo(shellMerge ? "/bin/sh" : args[0])
        {
            UseShellExecute = false,
            RedirectStandardOutput = stdout is Pipe.Capture or Pipe.DevNull,
            RedirectStandardError = !shellMerge && (stderr is Pipe.Capture or Pipe.DevNull or Pipe.Stdout),
            RedirectStandardInput = input is not null || inputBytes is not null || stdin == Pipe.DevNull,
        };
        if (shellMerge)
        {
            // The shell would report a missing or unrunnable program with an exit status; Python raises.
            string program = args[0].Contains('/') ? args[0] : PyShutil.Which(args[0]) ?? throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
            // A relative path is looked for from the child's working directory, as Python's exec does.
            string located = cwd is not null && !System.IO.Path.IsPathRooted(program) ? System.IO.Path.Combine(cwd, program) : program;
            if (!File.Exists(located)) throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(located) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                throw new PyOSError($"[Errno 13] Permission denied: {Py.ReprString(args[0])}", 13);
            foreach (string arg in new[] { "-c", "exec \"$0\" \"$@\" 2>&1", program }) info.ArgumentList.Add(arg);
        }
        foreach (string arg in args.Skip(1)) info.ArgumentList.Add(arg);
        if (cwd is not null) info.WorkingDirectory = cwd;
        if (env is not null)
        {
            info.Environment.Clear();
            foreach (var pair in env) info.Environment[pair.Key] = pair.Value;
        }
        if (cwd is not null && !Directory.Exists(cwd)) throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(cwd)}", 2);
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
        }
        catch (Win32Exception error)
        {
            bool denied = error.NativeErrorCode == 13 || (File.Exists(args[0]) && !OperatingSystem.IsWindows());
            throw denied
                ? new PyOSError($"[Errno 13] Permission denied: {Py.ReprString(args[0])}", 13)
                : new PyOSError($"[Errno 2] No such file or directory: {Py.ReprString(args[0])}", 2);
        }
        using (process)
        {
            var merged = new MemoryStream();
            var errorBuffer = new MemoryStream();
            object gate = new();
            Task readOut = Task.CompletedTask, readErr = Task.CompletedTask;
            if (info.RedirectStandardOutput) readOut = Pump(process.StandardOutput.BaseStream, stdout == Pipe.Capture ? merged : null, gate);
            if (info.RedirectStandardError)
            {
                MemoryStream? target = stderr switch { Pipe.Capture => errorBuffer, Pipe.Stdout => merged, _ => null };
                readErr = Pump(process.StandardError.BaseStream, target, gate);
            }
            Task writeIn = Task.CompletedTask;
            if (info.RedirectStandardInput)
            {
                byte[] payload = inputBytes ?? (input is null ? [] : Encoding.UTF8.GetBytes(input));
                writeIn = Task.Run(() =>
                {
                    try
                    {
                        process.StandardInput.BaseStream.Write(payload);
                        process.StandardInput.BaseStream.Flush();
                    }
                    catch (IOException)
                    {
                        // The child stopped reading; Python ignores the broken pipe the same way.
                    }
                    finally
                    {
                        try { process.StandardInput.Close(); } catch (IOException) { }
                    }
                });
            }
            bool finished = timeout is null
                ? WaitAll(process, -1)
                : WaitAll(process, (int)Math.Ceiling(Convert.ToDouble(timeout, System.Globalization.CultureInfo.InvariantCulture) * 1000));
            if (!finished)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                process.WaitForExit();
                throw new PyTimeoutExpired(args, timeout!);
            }
            process.WaitForExit();
            Task.WaitAll(readOut, readErr, writeIn);
            var result = new Completed(args, ReturnCodeOf(process), merged.ToArray(), errorBuffer.ToArray(), text, strict);
            if (check && result.ReturnCode != 0)
                throw new PyCalledProcessError(args, result.ReturnCode, text ? result.Stdout : "");
            return result;
        }
    }

    /// <summary>
    /// Popen.returncode of a finished child. Outside Windows .NET reports a child that a signal ended as 128 plus the
    /// signal's number, where Python reports the negative number of the signal; a status in that range is taken
    /// for the signal it almost always is (an abort, a segmentation fault, a kill).
    /// </summary>
    public static int ReturnCodeOf(Process process)
    {
        int code = process.ExitCode;
        return !OperatingSystem.IsWindows() && code is > 128 and <= 128 + 64 ? -(code - 128) : code;
    }

    /// <summary>subprocess.check_output(args, text=True): stdout, raising on a non-zero status.</summary>
    public static string CheckOutput(IReadOnlyList<string> args, string? cwd = null, object? timeout = null) =>
        Run(args, cwd: cwd, timeout: timeout, stderr: Pipe.Inherit, check: true).Stdout;

    private static bool WaitAll(Process process, int milliseconds) => process.WaitForExit(milliseconds);

    private static Task Pump(System.IO.Stream source, MemoryStream? target, object gate) => Task.Run(() =>
    {
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (target is null) continue;
            lock (gate) target.Write(buffer, 0, read);
        }
    });

    /// <summary>Decode child output as text mode does: UTF-8 (replacing or strict), then universal newlines.</summary>
    public static string Decode(byte[] data, bool strict)
    {
        string text = strict ? new UTF8Encoding(false, true).GetString(data) : new UTF8Encoding(false, false).GetString(data);
        return PyText.UniversalNewlines(text);
    }
}
