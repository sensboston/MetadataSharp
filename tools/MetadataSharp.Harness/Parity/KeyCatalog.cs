using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// Fail-closed gap check of the tag catalog against the pinned Exiv2 source tables.
//
//     MetadataSharp.Harness key-catalog SOURCE [--catalog PATH]
//
// The catalog (src/MetadataSharp.Core/tag-catalog-data.json) is the set of keys this library can name:
// Exif.<group>.<name> for the standard directories and for every vendor group of a MakerNote. The
// reference is the pinned Exiv2 checkout, read as text and never configured, built or run: its
// groupInfo[] table maps each IfdId to the group name it prints and to the function that returns the tag
// table of that group, and every entry of that table is one key.
//
// A key the reference has and the catalog does not is a failure: the comparison exists so that a tag the
// pinned corpus never exercises cannot stay unnamed here. A key only the catalog has is reported, not
// failed: this library keeps a few exact keys the reference's own table comments out
// (Exif.Canon.Categories, Exif.Canon.PictureStylePC, Exif.Canon.FilterInfo), and a local name for a tag
// the reference leaves as 0xnnnn is a name, not a loss.
//
// Names in parentheses are the end-of-table markers of the reference ((UnknownCanonCsTag)), not keys:
// they are skipped on both sides. Commented-out entries are not part of a table: comments are removed
// before the tables are read, which is why the five Canon tags Exiv2 0.28.9 leaves commented out are not
// expected here.
//
// The run fails as well when the reference itself cannot be read as expected: an unresolved tagList
// function, no table for a group, or a key count far below what the pinned version has would make a
// green run meaningless.
//
// (Formerly tools/parity/run_key_catalog.py; the arguments and annotations are unchanged.)
internal static class KeyCatalog
{
    public static readonly string DefaultCatalog = Repo.Path("src", "MetadataSharp.Core", "tag-catalog-data.json");
    internal static int MinimumKeys { get; set; } = 6000;
    private static readonly Regex Placeholder = new(@"^\(\w*\)\z", RegexOptions.CultureInvariant);

    /// <summary>The C++ source without its comments: a table entry that is commented out is not a key.</summary>
    public static string StripComments(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return Regex.Replace(text, @"//[^\n]*", "", RegexOptions.CultureInvariant);
    }

    /// <summary>(ifd id, ifd name, group name, tag-list function) per row of groupInfo[].</summary>
    public static List<(string Ifd, string IfdName, string Group, string Function)> GroupInfo(string tags)
    {
        int at = tags.IndexOf("constexpr GroupInfo groupInfo[] = {", StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException("ValueError: substring not found");
        string body = tags[at..];
        int end = body.IndexOf("\n};", StringComparison.Ordinal);
        if (end < 0) throw new InvalidOperationException("ValueError: substring not found");
        body = body[..end];
        return Regex.Matches(body, @"\{(IfdId::\w+),\s*""([^""]*)"",\s*""([^""]*)"",\s*([\w:]+)\}", RegexOptions.CultureInvariant)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value))
            .ToList();
    }

    /// <summary>The file and the table a tagList function returns.</summary>
    public static (string File, string Table)? Resolve(IEnumerable<KeyValuePair<string, string>> text, string function)
    {
        var pattern = new Regex(@"\b" + Regex.Escape(function) + @"\s*\(\s*\)\s*(?:const\s*)?\{\s*return\s+([\w:]+)\s*;", RegexOptions.CultureInvariant);
        foreach (var (name, source) in text)
        {
            var match = pattern.Match(source);
            if (match.Success) return (name, match.Groups[1].Value);
        }
        return null;
    }

    /// <summary>The (tag, name) rows of a table, or null when the table is not in the file.</summary>
    public static List<(string Tag, string Name)>? TableEntries(string source, string table, string? scope)
    {
        string name = table.Split("::")[^1];
        string qualified = scope is not null ? $"{scope}::{name}" : (table.Contains("::", StringComparison.Ordinal) ? table : name);
        var match = new Regex(@"TagInfo\s+" + Regex.Escape(qualified) + @"\[\]\s*=\s*\{", RegexOptions.CultureInvariant).Match(source);
        if (!match.Success) match = new Regex(@"TagInfo\s+(?:[\w:]+::)?" + Regex.Escape(name) + @"\[\]\s*=\s*\{", RegexOptions.CultureInvariant).Match(source);
        if (!match.Success) return null;
        string rest = source[(match.Index + match.Length)..];
        var ends = new[] { rest.IndexOf("\n};", StringComparison.Ordinal), rest.IndexOf("}};", StringComparison.Ordinal) }.Where(index => index >= 0).ToList();
        if (ends.Count == 0) return null;
        return Regex.Matches(rest[..ends.Min()], @"\{\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*""([^""]*)""", RegexOptions.CultureInvariant)
            .Select(found => (found.Groups[1].Value, found.Groups[2].Value))
            .ToList();
    }

    /// <summary>Exif.group.name -> (tag, name) of every table entry, and what could not be read.</summary>
    public static (OrderedDictionary<string, (string Tag, string Name)> Keys, List<string> Unresolved) ReferenceKeys(string source)
    {
        string directory = PyPath.Join(source, "src");
        if (!File.Exists(PyPath.Join(directory, "tags_int.cpp")))
            throw SystemExit.WithMessage($"{PyPath.Normalize(source)} is not an Exiv2 checkout: src/tags_int.cpp is missing");
        var text = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in PyPath.GlobUnordered(directory, "*.cpp").Concat(PyPath.GlobUnordered(directory, "*.hpp")))
            text[PyPath.Name(path)] = StripComments(PyFile.ReadText(path, replace: true));
        var keys = new OrderedDictionary<string, (string Tag, string Name)>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach (var (_, _, group, function) in GroupInfo(text["tags_int.cpp"]))
        {
            if (function == "nullptr") continue;
            var found = Resolve(text, function);
            if (found is null)
            {
                unresolved.Add($"{group}: {function} returns no table this script could find");
                continue;
            }
            var (file, table) = found.Value;
            string? scope = function.Contains("::", StringComparison.Ordinal) ? function[..function.LastIndexOf("::", StringComparison.Ordinal)] : null;
            var entries = TableEntries(text[file], table, scope);
            if (entries is null)
            {
                unresolved.Add($"{group}: table {table} of {file} is not a tag table");
                continue;
            }
            foreach (var (tag, name) in entries)
            {
                if (tag.ToLowerInvariant() is "0xffff" or "65535" || Placeholder.IsMatch(name)) continue;
                keys[$"Exif.{group}.{name}"] = (tag, name);
            }
        }
        return (keys, unresolved);
    }

    public static OrderedDictionary<string, (string Tag, string Name)> CatalogKeys(string path)
    {
        var data = (PyDict)PyJson.Loads(PyFile.ReadText(path))!;
        var keys = new OrderedDictionary<string, (string Tag, string Name)>(StringComparer.Ordinal);
        foreach (PyDict group in data.List("exifGroups").Cast<PyDict>())
        {
            foreach (PyDict tag in group.List("tags").Cast<PyDict>())
            {
                foreach (string name in tag.List("names").Cast<string>())
                {
                    if (Placeholder.IsMatch(name)) continue; // the end-of-table marker the catalog copied from the reference
                    keys[$"Exif.{Py.Str(group["name"])}.{name}"] = (Py.Str(tag["id"]), name);
                }
            }
        }
        return keys;
    }

    /// <summary>One GitHub annotation of up to size characters, the rest counted.</summary>
    public static void Annotate(string level, string title, IReadOnlyList<string> lines, int size = 6000)
    {
        if (lines.Count == 0) return;
        var body = new List<string>();
        int kept = 0;
        foreach (string line in lines)
        {
            if (kept + Py.Len(line) + 1 > size && body.Count > 0)
            {
                body.Add($"... and {lines.Count - body.Count} more");
                break;
            }
            body.Add(line);
            kept += Py.Len(line) + 1;
        }
        string text = string.Join("\n", body).Replace("%", "%25", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
        Out.Print($"::{(level == "error" ? "error" : "notice")} title={title}::{text}");
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("key-catalog", "Fail-closed gap check of the tag catalog against the pinned Exiv2 source tables.")
            .Add("source", help: "the pinned Exiv2 checkout")
            .Add("--catalog", @default: DefaultCatalog);
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;

        var (reference, unresolved) = ReferenceKeys(args.Str("source"));
        var catalog = CatalogKeys(args.Str("catalog"));
        if (unresolved.Count > 0)
        {
            Annotate("error", "PARITY tag keys - the reference could not be read", unresolved);
            return 1;
        }
        if (reference.Count < MinimumKeys)
        {
            Annotate("error", "PARITY tag keys - the reference holds too few keys to judge", [$"{reference.Count} keys read, at least {MinimumKeys} expected"]);
            return 1;
        }

        var missing = Py.Sorted(reference.Keys.Where(key => !catalog.ContainsKey(key)));
        var extra = Py.Sorted(catalog.Keys.Where(key => !reference.ContainsKey(key)));
        Annotate("error", "PARITY tag keys - keys the reference has and this library cannot name", missing);
        Annotate("notice", $"PARITY tag keys - {extra.Count} local-only keys (a name for a tag the reference "
                           + "leaves as 0x, or a key its own table comments out)", extra.Take(40).ToList());

        var shared = Py.Sorted(reference.Keys.Where(catalog.ContainsKey));
        var differs = shared.Where(key => !Py.Eq(Number(reference[key].Tag), Number(catalog[key].Tag)))
            .Select(key => $"{key}: reference {reference[key].Tag}, catalog {catalog[key].Tag}")
            .ToList();
        Annotate("notice", "PARITY tag keys - numbers that differ (split tables and reused groups make "
                           + "these dependent on the entry)", differs.Take(30).ToList());

        var groups = new Counter(reference.Keys.Select(key => (object)key.Split('.')[1]));
        Annotate("notice", $"PARITY tag keys - {reference.Count} reference keys in {groups.Count} groups; "
                           + $"{catalog.Count} catalog keys; {missing.Count} missing; {extra.Count} local-only; "
                           + $"{differs.Count} numbers differ",
                 Py.Sorted(groups.Items(), pair => pair.Key).Select(pair => $"{pair.Key}: {pair.Value}").ToList());
        return missing.Count > 0 ? 1 : 0;
    }

    private static object Number(string tag)
    {
        if (tag.ToLowerInvariant().StartsWith("0x", StringComparison.Ordinal))
            return PyText.TryParseInt(tag, out long hex, 16) ? hex : tag;
        return PyText.TryParseInt(tag, out long value) ? value : tag;
    }
}
