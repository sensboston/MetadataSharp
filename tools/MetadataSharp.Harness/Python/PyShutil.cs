namespace MetadataSharp.Harness.Python;

// shutil.which, as the gates used it to find curl and the reference tools.
internal static class PyShutil
{
    /// <summary>shutil.which(command): the first executable file of that name on PATH, or null.</summary>
    public static string? Which(string command)
    {
        if (System.IO.Path.GetDirectoryName(command) is { Length: > 0 }) return IsExecutable(command) ? command : null;
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        var names = new List<string> { command };
        if (OperatingSystem.IsWindows())
        {
            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (!extensions.Any(extension => command.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                names = extensions.Select(extension => command + extension).Prepend(command).ToList();
        }
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string directory in path.Split(System.IO.Path.PathSeparator))
        {
            if (!seen.Add(directory)) continue;
            foreach (string name in names)
            {
                string candidate = directory.Length == 0 ? name : System.IO.Path.Join(directory, name);
                if (IsExecutable(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>shutil.copyfile(source, target): the content only, replacing what target held.</summary>
    public static void CopyFile(string source, string target)
    {
        byte[] data = PyFile.ReadBytes(source);
        PyFile.WriteBytes(target, data);
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return true;
        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }
}
