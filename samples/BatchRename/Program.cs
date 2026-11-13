using MetadataSharp.Core.Operations;
using MetadataSharp.Formats;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("batchrename [--pattern '%Y%m%d_%H%M%S'] [--dry-run] image...");
    return 0;
}
try
{
    string pattern = Value("--pattern") ?? MetadataFileNames.DefaultFormat;
    bool dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
    int consumed = Array.IndexOf(args, "--pattern");
    string[] files = args.Where((a, i) => a != "--dry-run" && i != consumed && !(consumed >= 0 && i == consumed + 1)).ToArray();
    if (files.Length == 0) throw new ArgumentException("Provide one or more image files.");
    StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var reserved = new HashSet<string>(pathComparer);
    var plans = new List<(string Source, string Destination)>();
    foreach (string source in files)
    {
        DateTime? captured = ImageMetadata.Read(source).Exif?.DateTaken;
        if (captured is null) { Console.WriteLine($"skip (no DateTimeOriginal): {source}"); continue; }
        string directory = Path.GetDirectoryName(Path.GetFullPath(source))!;
        string desired = Path.GetFullPath(Path.Combine(directory, MetadataFileNames.Format(pattern, captured.Value, source)));
        string current = Path.GetFullPath(source);
        string candidate = desired;
        bool Taken(string name) => reserved.Contains(name) || (!pathComparer.Equals(name, current) && File.Exists(name));
        for (int suffix = 1; Taken(candidate); suffix++)
            candidate = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(desired)}_{suffix}{Path.GetExtension(desired)}");
        if (!pathComparer.Equals(candidate, current)) reserved.Add(candidate);
        plans.Add((source, candidate));
    }
    foreach ((string source, string target) in plans) Console.WriteLine($"{source} -> {target}" + (dryRun ? " [dry-run]" : string.Empty));
    if (!dryRun)
    {
        // Plan first, move to unique staging names, then to final names; this permits safe cycles.
        var staged = new List<(string Temp, string Target)>();
        try
        {
            foreach ((string source, string target) in plans)
            {
                if (string.Equals(Path.GetFullPath(source), target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, ".batchrename-" + Guid.NewGuid().ToString("N") + Path.GetExtension(source));
                File.Move(source, temp); staged.Add((temp, target));
            }
            foreach ((string temp, string target) in staged) File.Move(temp, target);
        }
        catch
        {
            foreach ((string temp, string target) in staged)
                if (File.Exists(temp) && !File.Exists(target)) File.Move(temp, target);
            throw;
        }
    }
    return 0;

    string? Value(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
}
catch (Exception e) when (e is IOException or MetadataException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"batchrename: {e.Message}"); return 1;
}
