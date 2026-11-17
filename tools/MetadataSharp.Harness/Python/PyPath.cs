using System.Text;
using System.Text.RegularExpressions;

namespace MetadataSharp.Harness.Python;

// pathlib on strings: the same normalization, names, suffixes and ordering, so a path a report prints or
// a dump record keys on is the text the script produced. Separators are always '/'.
internal static class PyPath
{
    /// <summary>str(Path(text)): '/' separators, no empty or '.' components, no trailing slash.</summary>
    public static string Normalize(string path)
    {
        if (path.Length == 0) return ".";
        path = Separators(path);
        bool absolute = path.StartsWith('/');
        var parts = path.Split('/').Where(part => part.Length > 0 && part != ".").ToList();
        string joined = string.Join("/", parts);
        if (absolute) return "/" + joined;
        return joined.Length == 0 ? "." : joined;
    }

    /// <summary>
    /// The separators of the host: '\\' is one only on Windows. On a POSIX system it is an ordinary character
    /// of a name, as PosixPath keeps it.
    /// </summary>
    public static string Separators(string path) => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    /// <summary>Path(a) / b / c.</summary>
    public static string Join(string first, params string[] more)
    {
        string result = Normalize(first);
        foreach (string part in more)
        {
            string next = Separators(part);
            if (next.StartsWith('/') || (OperatingSystem.IsWindows() && next.Length > 1 && next[1] == ':')) result = Normalize(next);
            else if (Normalize(next) != ".") result = result == "." ? Normalize(next) : Normalize(result + "/" + next);
        }
        return result;
    }

    /// <summary>Path.name.</summary>
    public static string Name(string path)
    {
        string normal = Normalize(path);
        if (normal is "." or "/") return "";
        int slash = normal.LastIndexOf('/');
        return slash < 0 ? normal : normal[(slash + 1)..];
    }

    /// <summary>Path.suffix: the last dot-suffix of the name, unless the dot leads or ends it.</summary>
    public static string Suffix(string path)
    {
        string name = Name(path);
        int dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[dot..] : "";
    }

    /// <summary>Path.stem.</summary>
    public static string Stem(string path)
    {
        string name = Name(path);
        int dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[..dot] : name;
    }

    /// <summary>Path.with_suffix(suffix).</summary>
    public static string WithSuffix(string path, string suffix)
    {
        string stem = Stem(path), parent = Parent(path);
        return parent == "." && !Normalize(path).StartsWith("./", StringComparison.Ordinal) ? stem + suffix : Join(parent, stem + suffix);
    }

    /// <summary>Path.with_name(name).</summary>
    public static string WithName(string path, string name) => Join(Parent(path), name);

    /// <summary>Path.parent.</summary>
    public static string Parent(string path)
    {
        string normal = Normalize(path);
        if (normal is "." or "/") return normal;
        int slash = normal.LastIndexOf('/');
        if (slash < 0) return ".";
        return slash == 0 ? "/" : normal[..slash];
    }

    /// <summary>Path.parts.</summary>
    public static List<string> Parts(string path)
    {
        string normal = Normalize(path);
        var parts = new List<string>();
        if (normal.StartsWith('/')) parts.Add("/");
        parts.AddRange(normal.Split('/').Where(part => part.Length > 0 && part != "."));
        return parts;
    }

    /// <summary>Path.resolve(): absolute, with every symbolic link on the way followed, as os.path.realpath.</summary>
    public static string Resolve(string path)
    {
        string text = Separators(path);
        if (OperatingSystem.IsWindows()) return Normalize(System.IO.Path.GetFullPath(path).Replace('\\', '/'));
        string full = text.StartsWith('/') ? text : Directory.GetCurrentDirectory().Replace('\\', '/') + "/" + text;
        var pending = new Stack<string>(full.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse());
        string current = "/";
        int links = 0;
        while (pending.Count > 0)
        {
            string part = pending.Pop();
            if (part == ".") continue;
            if (part == "..")
            {
                current = Parent(current);
                continue;
            }
            string next = current == "/" ? "/" + part : current + "/" + part;
            string? target = null;
            try
            {
                target = new FileInfo(next).LinkTarget;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            if (target is null || ++links > 40)
            {
                // Not a link, or a loop of links: the component is kept as written, as resolve(strict=False) keeps it.
                current = next;
                continue;
            }
            if (target.StartsWith('/')) current = "/";
            foreach (string piece in target.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse()) pending.Push(piece);
        }
        return Normalize(current);
    }

    /// <summary>Path.is_absolute().</summary>
    public static bool IsAbsolute(string path) => OperatingSystem.IsWindows() ? System.IO.Path.IsPathFullyQualified(path) : path.StartsWith('/');

    /// <summary>Path.exists(): a file or a directory, symbolic links followed.</summary>
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>The order of sorted() over Path objects: component by component, by code point.</summary>
    public static int Compare(string left, string right)
    {
        var a = Normalize(left).Split('/');
        var b = Normalize(right).Split('/');
        int count = Math.Min(a.Length, b.Length);
        for (int i = 0; i < count; i++)
        {
            int order = Py.CompareStrings(a[i], b[i]);
            if (order != 0) return order;
        }
        return a.Length.CompareTo(b.Length);
    }

    /// <summary>A comparer that sorts paths as sorted() sorts Path objects.</summary>
    public static readonly IComparer<string> Comparer = Comparer<string>.Create(Compare);

    /// <summary>sorted(Path(directory).rglob("*")): every entry below the directory, as normalized paths.</summary>
    public static List<string> RGlob(string directory, string pattern = "*")
    {
        var found = new List<string>();
        var regex = FnMatch(pattern);
        string root = Normalize(directory);
        // A directory that is missing yields nothing, and one that cannot be listed is skipped, as pathlib's
        // selectors return on PermissionError.
        if (!Directory.Exists(root)) return found;
        void Walk(string current)
        {
            List<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current == "." ? "." : current).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            foreach (string entry in entries)
            {
                string name = System.IO.Path.GetFileName(entry);
                string child = root == "." && current == "." ? name : Join(current, name);
                if (regex.IsMatch(name)) found.Add(child);
                var info = new DirectoryInfo(entry);
                if (Directory.Exists(entry) && info.LinkTarget is null) Walk(child);
            }
        }
        Walk(root);
        found.Sort(Comparer);
        return found;
    }

    /// <summary>sorted(Path(directory).glob(pattern)) for a pattern without separators.</summary>
    public static List<string> Glob(string directory, string pattern)
    {
        string root = Normalize(directory);
        if (!Directory.Exists(root)) return [];
        var regex = FnMatch(pattern);
        var found = Directory.EnumerateFileSystemEntries(root)
            .Select(entry => System.IO.Path.GetFileName(entry))
            .Where(name => regex.IsMatch(name))
            .Select(name => root == "." ? name : Join(root, name))
            .ToList();
        found.Sort(Comparer);
        return found;
    }

    /// <summary>Path(directory).glob(pattern) for a pattern without separators, in the directory's own order (unsorted).</summary>
    public static List<string> GlobUnordered(string directory, string pattern)
    {
        string root = Normalize(directory);
        if (!Directory.Exists(root)) return [];
        var regex = FnMatch(pattern);
        return Directory.EnumerateFileSystemEntries(root)
            .Select(entry => System.IO.Path.GetFileName(entry))
            .Where(name => regex.IsMatch(name))
            .Select(name => root == "." ? name : Join(root, name))
            .ToList();
    }

    /// <summary>The entries of a directory (Path.iterdir()), in no particular order.</summary>
    public static IEnumerable<string> IterDir(string directory)
    {
        string root = Normalize(directory);
        return Directory.EnumerateFileSystemEntries(root).Select(entry => Join(root, System.IO.Path.GetFileName(entry)));
    }

    /// <summary>fnmatch.translate: '*', '?' and '[...]' over a whole name, case-sensitively.</summary>
    public static Regex FnMatch(string pattern)
    {
        var builder = new StringBuilder("^(?:");
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '*') builder.Append("[\\s\\S]*");
            else if (c == '?') builder.Append("[\\s\\S]");
            else if (c == '[')
            {
                int end = pattern.IndexOf(']', i + 2 <= pattern.Length ? i + 2 : i + 1);
                if (end < 0) builder.Append("\\[");
                else
                {
                    string body = pattern[(i + 1)..end];
                    if (body.StartsWith('!')) body = "^" + body[1..];
                    builder.Append('[').Append(body.Replace("\\", "\\\\", StringComparison.Ordinal)).Append(']');
                    i = end;
                }
            }
            else builder.Append(Regex.Escape(c.ToString()));
        }
        return new Regex(builder.Append(")\\z").ToString(), RegexOptions.CultureInvariant);
    }

    /// <summary>Path.relative_to(base) for a path below base.</summary>
    public static string RelativeTo(string path, string basePath)
    {
        var parts = Parts(path);
        var baseParts = Parts(basePath);
        if (baseParts.Count > parts.Count || !baseParts.SequenceEqual(parts.Take(baseParts.Count)))
            throw new ArgumentException($"{Py.ReprString(Normalize(path))} is not in the subpath of {Py.ReprString(Normalize(basePath))}");
        var rest = parts.Skip(baseParts.Count).ToList();
        return rest.Count == 0 ? "." : string.Join("/", rest);
    }
}
