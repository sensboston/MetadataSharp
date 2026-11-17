using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MetadataSharp.Harness.Python;

// json.dumps and json.loads with the output of CPython's encoder: its separators, indentation, key order,
// float repr and escapes, so a report file is the same bytes the script wrote.
internal static class PyJson
{
    /// <summary>json.dumps(value, indent=, sort_keys=, ensure_ascii=, separators=).</summary>
    public static string Dumps(object? value, int? indent = null, bool sortKeys = false, bool ensureAscii = true,
                               string? itemSeparator = null, string? keySeparator = null)
    {
        itemSeparator ??= indent is null ? ", " : ",";
        keySeparator ??= ": ";
        var builder = new StringBuilder();
        Write(builder, value, indent, sortKeys, ensureAscii, itemSeparator, keySeparator, 0);
        return builder.ToString();
    }

    /// <summary>json.dumps(value, separators=(",", ":")) and the other compact options.</summary>
    public static string Compact(object? value, bool sortKeys = false, bool ensureAscii = true) =>
        Dumps(value, null, sortKeys, ensureAscii, ",", ":");

    private static void Write(StringBuilder builder, object? value, int? indent, bool sortKeys, bool ensureAscii,
                              string itemSeparator, string keySeparator, int level)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                return;
            case bool b:
                builder.Append(b ? "true" : "false");
                return;
            case string s:
                WriteString(builder, s, ensureAscii);
                return;
            case int or long or BigInteger:
                builder.Append(System.Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            case double d:
                builder.Append(double.IsNaN(d) ? "NaN" : double.IsPositiveInfinity(d) ? "Infinity" : double.IsNegativeInfinity(d) ? "-Infinity" : Py.FloatRepr(d));
                return;
            case PyDict dict:
                WriteDict(builder, dict, indent, sortKeys, ensureAscii, itemSeparator, keySeparator, level);
                return;
            case PyTuple tuple:
                WriteList(builder, tuple.ToList(), indent, sortKeys, ensureAscii, itemSeparator, keySeparator, level);
                return;
            case IEnumerable<string> strings:
                WriteList(builder, strings.Cast<object?>().ToList(), indent, sortKeys, ensureAscii, itemSeparator, keySeparator, level);
                return;
            case IList list:
                WriteList(builder, list.Cast<object?>().ToList(), indent, sortKeys, ensureAscii, itemSeparator, keySeparator, level);
                return;
            default:
                throw new InvalidOperationException($"Object of type {value.GetType().Name} is not JSON serializable");
        }
    }

    private static void WriteList(StringBuilder builder, List<object?> items, int? indent, bool sortKeys, bool ensureAscii,
                                  string itemSeparator, string keySeparator, int level)
    {
        if (items.Count == 0)
        {
            builder.Append("[]");
            return;
        }
        builder.Append('[');
        string? newline = indent is null ? null : "\n" + new string(' ', indent.Value * (level + 1));
        if (newline is not null) builder.Append(newline);
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(itemSeparator);
                if (newline is not null) builder.Append(newline);
            }
            Write(builder, items[i], indent, sortKeys, ensureAscii, itemSeparator, keySeparator, level + 1);
        }
        if (indent is not null) builder.Append('\n').Append(' ', indent.Value * level);
        builder.Append(']');
    }

    private static void WriteDict(StringBuilder builder, PyDict dict, int? indent, bool sortKeys, bool ensureAscii,
                                  string itemSeparator, string keySeparator, int level)
    {
        if (dict.Count == 0)
        {
            builder.Append("{}");
            return;
        }
        IEnumerable<KeyValuePair<object, object?>> pairs = dict;
        if (sortKeys) pairs = Py.Sorted(dict, pair => pair.Key);
        builder.Append('{');
        string? newline = indent is null ? null : "\n" + new string(' ', indent.Value * (level + 1));
        if (newline is not null) builder.Append(newline);
        bool first = true;
        foreach (var pair in pairs)
        {
            if (!first)
            {
                builder.Append(itemSeparator);
                if (newline is not null) builder.Append(newline);
            }
            first = false;
            string key = pair.Key switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                double d => Py.FloatRepr(d),
                int or long or BigInteger => System.Convert.ToString(pair.Key, CultureInfo.InvariantCulture)!,
                _ => throw new InvalidOperationException($"keys must be str, int, float, bool or None, not {Py.TypeName(pair.Key)}"),
            };
            WriteString(builder, key, ensureAscii);
            builder.Append(keySeparator);
            Write(builder, pair.Value, indent, sortKeys, ensureAscii, itemSeparator, keySeparator, level + 1);
        }
        if (indent is not null) builder.Append('\n').Append(' ', indent.Value * level);
        builder.Append('}');
    }

    private static void WriteString(StringBuilder builder, string text, bool ensureAscii)
    {
        builder.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (c < ' ' || (ensureAscii && c > '~')) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }

    /// <summary>
    /// json.loads(text) as CPython's C scanner decodes it: objects become PyDict in document order (a repeated
    /// key keeps its first place and its last value), integers long or BigInteger, other numbers double, and
    /// NaN, Infinity and -Infinity are read. Malformed text raises JsonDecodeError with CPython's message.
    /// </summary>
    public static object? Loads(string text) => Loads(text, pairs: false);

    /// <summary>json.loads(text[, object_pairs_hook=list]): with pairs, every object becomes the list of its
    /// (key, value) tuples in document order, repeated keys included.</summary>
    public static object? Loads(string text, bool pairs)
    {
        if (text.StartsWith('\ufeff')) throw new JsonDecodeError("Unexpected UTF-8 BOM (decode using utf-8-sig)", text, 0);
        var decoder = new Decoder(text, pairs);
        int at = decoder.SkipWhitespace(0);
        object? value;
        try
        {
            value = decoder.ScanOnce(at, out at);
        }
        catch (StopIteration stop)
        {
            throw new JsonDecodeError("Expecting value", text, stop.Position);
        }
        at = decoder.SkipWhitespace(at);
        if (at != text.Length) throw new JsonDecodeError("Extra data", text, at);
        return value;
    }

    /// <summary>json.loads(bytes) of UTF-8 text.</summary>
    public static object? Loads(byte[] utf8) => Loads(Encoding.UTF8.GetString(utf8));

    /// <summary>json.loads(Path(path).read_text(encoding="utf-8")).</summary>
    public static object? Load(string path) => Loads(PyFile.ReadText(path));

    // No value starts at Position: the scanner's StopIteration, which decode() reports as "Expecting value".
    private sealed class StopIteration(int position) : Exception
    {
        public int Position { get; } = position;
    }

    // A port of Modules/_json.c (scan_once_unicode and its helpers) as of CPython 3.12.
    private sealed class Decoder(string text, bool pairs = false)
    {
        private readonly int endIndex = text.Length - 1;

        private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r';

        public int SkipWhitespace(int at)
        {
            while (at <= endIndex && IsWhitespace(text[at])) at++;
            return at;
        }

        private bool Follows(int at, string word) =>
            at + word.Length <= text.Length && string.CompareOrdinal(text, at, word, 0, word.Length) == 0;

        public object? ScanOnce(int at, out int next)
        {
            if (at >= text.Length) throw new StopIteration(at);
            switch (text[at])
            {
                case '"':
                    return ScanString(at + 1, out next);
                case '{':
                    return ParseObject(at + 1, out next);
                case '[':
                    return ParseArray(at + 1, out next);
                case 'n' when Follows(at, "null"):
                    next = at + 4;
                    return null;
                case 't' when Follows(at, "true"):
                    next = at + 4;
                    return true;
                case 'f' when Follows(at, "false"):
                    next = at + 5;
                    return false;
                case 'N' when Follows(at, "NaN"):
                    next = at + 3;
                    return double.NaN;
                case 'I' when Follows(at, "Infinity"):
                    next = at + 8;
                    return double.PositiveInfinity;
                case '-' when Follows(at, "-Infinity"):
                    next = at + 9;
                    return double.NegativeInfinity;
            }
            return MatchNumber(at, out next);
        }

        private object MatchNumber(int start, out int next)
        {
            int at = start;
            bool isFloat = false;
            if (text[at] == '-')
            {
                at++;
                if (at > endIndex) throw new StopIteration(start);
            }
            if (text[at] is >= '1' and <= '9')
            {
                at++;
                while (at <= endIndex && text[at] is >= '0' and <= '9') at++;
            }
            else if (text[at] == '0')
            {
                at++;
            }
            else
            {
                throw new StopIteration(start);
            }
            if (at < endIndex && text[at] == '.' && text[at + 1] is >= '0' and <= '9')
            {
                isFloat = true;
                at += 2;
                while (at <= endIndex && text[at] is >= '0' and <= '9') at++;
            }
            if (at < endIndex && text[at] is 'e' or 'E')
            {
                int exponentStart = at;
                at++;
                if (at < endIndex && text[at] is '-' or '+') at++;
                while (at <= endIndex && text[at] is >= '0' and <= '9') at++;
                if (text[at - 1] is >= '0' and <= '9') isFloat = true;
                else at = exponentStart;
            }
            next = at;
            string number = text[start..at];
            if (isFloat) return double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long whole)) return whole;
            return BigInteger.Parse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        private string ScanString(int end, out int next)
        {
            int begin = end - 1;
            var builder = new StringBuilder();
            int at = end;
            while (true)
            {
                // The run of characters up to a quote, a backslash or (strictly) a control character.
                int runStart = at;
                char c = '\0';
                for (; at < text.Length; at++)
                {
                    c = text[at];
                    if (c is '"' or '\\') break;
                    if (c <= 0x1f) throw new JsonDecodeError("Invalid control character at", text, at);
                }
                if (at >= text.Length) throw new JsonDecodeError("Unterminated string starting at", text, begin);
                builder.Append(text, runStart, at - runStart);
                if (c == '"')
                {
                    next = at + 1;
                    return builder.ToString();
                }
                at++; // past the backslash
                if (at == text.Length) throw new JsonDecodeError("Unterminated string starting at", text, begin);
                c = text[at];
                if (c != 'u')
                {
                    char? escaped = c switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => null,
                    };
                    if (escaped is null) throw new JsonDecodeError("Invalid \\escape", text, at - 1);
                    builder.Append(escaped.Value);
                    at++;
                    continue;
                }
                at++; // past the u
                int end4 = at + 4;
                if (end4 >= text.Length) throw new JsonDecodeError("Invalid \\uXXXX escape", text, at - 1);
                int unit = Hex4(at, end4);
                at = end4;
                if (unit is >= 0xD800 and <= 0xDBFF && end4 + 6 < text.Length && text[at] == '\\' && text[at + 1] == 'u')
                {
                    int second = Hex4(at + 2, end4 + 6);
                    if (second is >= 0xDC00 and <= 0xDFFF)
                    {
                        builder.Append((char)unit).Append((char)second);
                        at = end4 + 6;
                        continue;
                    }
                }
                builder.Append((char)unit);
            }
        }

        // Four hex digits from at to end; a bad digit is reported at the escape's backslash-u.
        private int Hex4(int at, int end)
        {
            int value = 0;
            for (int i = at; i < end; i++)
            {
                char digit = text[i];
                int nibble = digit switch
                {
                    >= '0' and <= '9' => digit - '0',
                    >= 'a' and <= 'f' => digit - 'a' + 10,
                    >= 'A' and <= 'F' => digit - 'A' + 10,
                    _ => -1,
                };
                if (nibble < 0) throw new JsonDecodeError("Invalid \\uXXXX escape", text, end - 5);
                value = (value << 4) | nibble;
            }
            return value;
        }

        private object ParseObject(int at, out int next)
        {
            var result = new PyDict();
            var pairList = new List<object?>();
            at = SkipWhitespace(at);
            if (at > endIndex || text[at] != '}')
            {
                while (true)
                {
                    if (at > endIndex || text[at] != '"') throw new JsonDecodeError("Expecting property name enclosed in double quotes", text, at);
                    string key = ScanString(at + 1, out at);
                    at = SkipWhitespace(at);
                    if (at > endIndex || text[at] != ':') throw new JsonDecodeError("Expecting ':' delimiter", text, at);
                    at = SkipWhitespace(at + 1);
                    object? value = ScanOnce(at, out at);
                    if (pairs) pairList.Add(new PyTuple(key, value));
                    else result[key] = value;
                    at = SkipWhitespace(at);
                    if (at <= endIndex && text[at] == '}') break;
                    if (at > endIndex || text[at] != ',') throw new JsonDecodeError("Expecting ',' delimiter", text, at);
                    at = SkipWhitespace(at + 1);
                }
            }
            next = at + 1;
            return pairs ? pairList : result;
        }

        private List<object?> ParseArray(int at, out int next)
        {
            var result = new List<object?>();
            at = SkipWhitespace(at);
            if (at > endIndex || text[at] != ']')
            {
                while (true)
                {
                    result.Add(ScanOnce(at, out at));
                    at = SkipWhitespace(at);
                    if (at <= endIndex && text[at] == ']') break;
                    if (at > endIndex || text[at] != ',') throw new JsonDecodeError("Expecting ',' delimiter", text, at);
                    at = SkipWhitespace(at + 1);
                }
            }
            next = at + 1;
            return result;
        }
    }
}

// json.JSONDecodeError, a ValueError: "msg: line L column C (char P)", with positions in code points.
internal sealed class JsonDecodeError : ValueError
{
    public JsonDecodeError(string message, string document, int position)
        : base(Describe(message, document, position, out int line, out int column, out int character))
    {
        Msg = message;
        Pos = character;
        LineNo = line;
        ColNo = column;
    }

    public string Msg { get; }

    public int Pos { get; }

    public int LineNo { get; }

    public int ColNo { get; }

    private static string Describe(string message, string document, int position, out int line, out int column, out int character)
    {
        // Python counts characters; a character outside the BMP is two UTF-16 units here.
        string before = document[..Math.Min(position, document.Length)];
        character = Py.Len(before);
        line = before.Count(c => c == '\n') + 1;
        int lastNewline = before.LastIndexOf('\n');
        column = lastNewline < 0 ? character + 1 : Py.Len(before[(lastNewline + 1)..]) + 1;
        return $"{message}: line {line} column {column} (char {character})";
    }
}

// ValueError and its subclasses.
internal class ValueError(string message) : Exception(message);
