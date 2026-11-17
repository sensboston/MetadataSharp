using System.Text;

namespace MetadataSharp.Harness.Python;

// shlex.split and shlex.quote in POSIX mode: the dump, fixture and exiv2 commands arrive as one string.
internal static class Shlex
{
    /// <summary>shlex.split(text): quotes, escapes and whitespace as a POSIX shell reads them (no comments).</summary>
    public static List<string> Split(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        bool inWord = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                if (inWord)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    inWord = false;
                }
                continue;
            }
            inWord = true;
            if (c == '\\')
            {
                if (++i >= text.Length) throw new FormatException("No escaped character");
                current.Append(text[i]);
            }
            else if (c == '\'')
            {
                int end = text.IndexOf('\'', i + 1);
                if (end < 0) throw new FormatException("No closing quotation");
                current.Append(text, i + 1, end - i - 1);
                i = end;
            }
            else if (c == '"')
            {
                i++;
                while (true)
                {
                    if (i >= text.Length) throw new FormatException("No closing quotation");
                    char d = text[i];
                    if (d == '"') break;
                    if (d == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                    {
                        current.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }
                    current.Append(d);
                    i++;
                }
            }
            else current.Append(c);
        }
        if (inWord) words.Add(current.ToString());
        return words;
    }

    /// <summary>shlex.quote(word).</summary>
    public static string Quote(string word)
    {
        if (word.Length == 0) return "''";
        bool safe = word.All(c => char.IsAsciiLetterOrDigit(c) || "@%+=:,./-_".Contains(c));
        return safe ? word : "'" + word.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    /// <summary>shlex.join(words).</summary>
    public static string Join(IEnumerable<string> words) => string.Join(" ", words.Select(Quote));
}
