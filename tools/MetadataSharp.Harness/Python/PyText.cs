using System.Globalization;
using System.Text;

namespace MetadataSharp.Harness.Python;

// The str methods the gates used, with Python's definitions of whitespace, line boundaries and length.
internal static class PyText
{
    private const string Whitespace = "\t\n\u000b\u000c\r\u001c\u001d\u001e\u001f \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";

    /// <summary>str.isspace() for one character.</summary>
    public static bool IsSpace(char c) => Whitespace.Contains(c);

    /// <summary>str.isspace(): true for a non-empty string of whitespace.</summary>
    public static bool IsSpace(string text) => text.Length > 0 && text.All(IsSpace);

    /// <summary>str.strip() or str.strip(chars).</summary>
    public static string Strip(string text, string? chars = null) => RStrip(LStrip(text, chars), chars);

    /// <summary>str.lstrip() or str.lstrip(chars).</summary>
    public static string LStrip(string text, string? chars = null)
    {
        int start = 0;
        while (start < text.Length && (chars is null ? IsSpace(text[start]) : chars.Contains(text[start]))) start++;
        return text[start..];
    }

    /// <summary>str.rstrip() or str.rstrip(chars).</summary>
    public static string RStrip(string text, string? chars = null)
    {
        int end = text.Length;
        while (end > 0 && (chars is null ? IsSpace(text[end - 1]) : chars.Contains(text[end - 1]))) end--;
        return text[..end];
    }

    /// <summary>str.splitlines(keepends): every line boundary Python recognizes, not only \n.</summary>
    public static List<string> SplitLines(string text, bool keepEnds = false)
    {
        var lines = new List<string>();
        int start = 0, i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            int boundary = 0;
            if (c == '\r') boundary = i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            else if (c is '\n' or '\u000b' or '\u000c' or '\u001c' or '\u001d' or '\u001e' or '\u0085' or '\u2028' or '\u2029') boundary = 1;
            if (boundary == 0)
            {
                i++;
                continue;
            }
            lines.Add(text[start..(keepEnds ? i + boundary : i)]);
            i += boundary;
            start = i;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    /// <summary>bytes.splitlines(): only \n, \r and \r\n end a line.</summary>
    public static List<byte[]> SplitLines(ReadOnlySpan<byte> data)
    {
        var lines = new List<byte[]>();
        int start = 0, i = 0;
        while (i < data.Length)
        {
            byte c = data[i];
            int boundary = c == '\r' ? (i + 1 < data.Length && data[i + 1] == '\n' ? 2 : 1) : c == '\n' ? 1 : 0;
            if (boundary == 0)
            {
                i++;
                continue;
            }
            lines.Add(data[start..i].ToArray());
            i += boundary;
            start = i;
        }
        if (start < data.Length) lines.Add(data[start..].ToArray());
        return lines;
    }

    /// <summary>str.split() without a separator: runs of whitespace, leading and trailing ignored.</summary>
    public static List<string> Split(string text, int maxSplit = -1)
    {
        var parts = new List<string>();
        int i = 0;
        while (true)
        {
            while (i < text.Length && IsSpace(text[i])) i++;
            if (i == text.Length) break;
            if (maxSplit >= 0 && parts.Count == maxSplit)
            {
                // The rest after the last split, trailing whitespace included, as CPython's split_whitespace adds it.
                parts.Add(text[i..]);
                break;
            }
            int start = i;
            while (i < text.Length && !IsSpace(text[i])) i++;
            parts.Add(text[start..i]);
        }
        return parts;
    }

    /// <summary>str.split(sep, maxsplit).</summary>
    public static List<string> Split(string text, string separator, int maxSplit = -1)
    {
        if (separator.Length == 0) throw new ArgumentException("empty separator");
        var parts = new List<string>();
        int start = 0;
        while (maxSplit < 0 || parts.Count < maxSplit)
        {
            int at = text.IndexOf(separator, start, StringComparison.Ordinal);
            if (at < 0) break;
            parts.Add(text[start..at]);
            start = at + separator.Length;
        }
        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>str.rsplit(sep, maxsplit).</summary>
    public static List<string> RSplit(string text, string separator, int maxSplit = -1)
    {
        var parts = new List<string>();
        int end = text.Length;
        while (maxSplit < 0 || parts.Count < maxSplit)
        {
            int at = end == 0 ? -1 : text.LastIndexOf(separator, end - 1, StringComparison.Ordinal);
            if (at < 0 || at + separator.Length > end) break;
            parts.Insert(0, text[(at + separator.Length)..end]);
            end = at;
        }
        parts.Insert(0, text[..end]);
        return parts;
    }

    /// <summary>str.partition(sep).</summary>
    public static (string Before, string Separator, string After) Partition(string text, string separator)
    {
        int at = text.IndexOf(separator, StringComparison.Ordinal);
        return at < 0 ? (text, "", "") : (text[..at], separator, text[(at + separator.Length)..]);
    }

    /// <summary>str.rpartition(sep).</summary>
    public static (string Before, string Separator, string After) RPartition(string text, string separator)
    {
        int at = text.LastIndexOf(separator, StringComparison.Ordinal);
        return at < 0 ? ("", "", text) : (text[..at], separator, text[(at + separator.Length)..]);
    }

    /// <summary>format(text, '&lt;N'): pad to N code points on the right.</summary>
    public static string LJust(string text, int width, char fill = ' ')
    {
        int length = Py.Len(text);
        return length >= width ? text : text + new string(fill, width - length);
    }

    /// <summary>format(text, '&gt;N'): pad to N code points on the left.</summary>
    public static string RJust(string text, int width, char fill = ' ')
    {
        int length = Py.Len(text);
        return length >= width ? text : new string(fill, width - length) + text;
    }

    /// <summary>text[:n] counted in code points, as a Python slice counts.</summary>
    public static string Head(string text, int count)
    {
        if (count <= 0) return "";
        int units = 0, points = 0;
        while (units < text.Length && points < count)
        {
            units += char.IsHighSurrogate(text[units]) && units + 1 < text.Length && char.IsLowSurrogate(text[units + 1]) ? 2 : 1;
            points++;
        }
        return text[..units];
    }

    /// <summary>[text[i:i + size] for i in range(0, len(text), size)], counted in code points.</summary>
    public static IEnumerable<string> Chunks(string text, int size)
    {
        int start = 0;
        while (start < text.Length)
        {
            string chunk = Head(text[start..], size);
            yield return chunk;
            start += chunk.Length;
        }
    }

    /// <summary>format(value, ',') for an integer.</summary>
    public static string Thousands(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>text[-n:] counted in code points.</summary>
    public static string Tail(string text, int count)
    {
        if (count <= 0) return "";
        int units = text.Length, points = 0;
        while (units > 0 && points < count)
        {
            units -= char.IsLowSurrogate(text[units - 1]) && units >= 2 && char.IsHighSurrogate(text[units - 2]) ? 2 : 1;
            points++;
        }
        return text[units..];
    }

    /// <summary>str.isdigit() for a whole string.</summary>
    public static bool IsDigit(string text) =>
        text.Length > 0 && Py.CodePoints(text).All(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.DecimalDigitNumber
                                                         || CharUnicodeInfo.GetDecimalDigitValue(Py.FromCodePoint(c), 0) >= 0
                                                         || CharUnicodeInfo.GetDigitValue(Py.FromCodePoint(c), 0) >= 0);

    /// <summary>str.isdecimal() for a whole string.</summary>
    public static bool IsDecimal(string text) =>
        text.Length > 0 && Py.CodePoints(text).All(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.DecimalDigitNumber);

    /// <summary>str.isascii().</summary>
    public static bool IsAscii(string text) => text.All(c => c < 0x80);

    /// <summary>str.isprintable().</summary>
    public static bool IsPrintable(string text) => Py.CodePoints(text).All(Py.IsPrintable);

    /// <summary>int(text) as Python parses it: optional sign, digits, surrounding whitespace, underscores between digits.</summary>
    public static bool TryParseInt(string text, out long value, int radix = 10)
    {
        value = 0;
        string body = Strip(text);
        bool negative = false;
        if (body.StartsWith('+') || body.StartsWith('-'))
        {
            negative = body[0] == '-';
            body = body[1..];
        }
        if (radix == 16 && (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))) body = body[2..];
        if (body.Length == 0 || body.StartsWith('_') || body.EndsWith('_') || body.Contains("__", StringComparison.Ordinal)) return false;
        long result = 0;
        foreach (char c in body)
        {
            if (c == '_') continue;
            int digit = DigitValue(c);
            if (digit < 0 || digit >= radix) return false;
            result = checked(result * radix + digit);
        }
        value = negative ? -result : result;
        return true;
    }

    /// <summary>int(text): raises ValueError (here FormatException) as Python does.</summary>
    public static long ParseInt(string text, int radix = 10) =>
        TryParseInt(text, out long value, radix)
            ? value
            : throw new FormatException($"invalid literal for int() with base {radix}: {Py.ReprString(text)}");

    private static int DigitValue(char c)
    {
        if (c is >= '0' and <= '9') return c - '0';
        if (c is >= 'a' and <= 'z') return c - 'a' + 10;
        if (c is >= 'A' and <= 'Z') return c - 'A' + 10;
        int unicode = CharUnicodeInfo.GetDecimalDigitValue(c);
        return unicode;
    }

    /// <summary>bytes.decode('latin-1').</summary>
    public static string Latin1(ReadOnlySpan<byte> data) => Encoding.Latin1.GetString(data);

    /// <summary>bytes.decode('utf-8', 'replace').</summary>
    public static string Utf8Replace(ReadOnlySpan<byte> data) => new UTF8Encoding(false, false).GetString(data);

    /// <summary>bytes.decode('ascii', 'replace'): every byte above 0x7F becomes U+FFFD.</summary>
    public static string AsciiReplace(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder(data.Length);
        foreach (byte b in data) builder.Append(b < 0x80 ? (char)b : '\uFFFD');
        return builder.ToString();
    }

    /// <summary>Whether bytes.decode('utf-8') succeeds (strict: no overlong forms, surrogates or code points
    /// above U+10FFFF, as in CPython).</summary>
    public static bool IsUtf8(ReadOnlySpan<byte> data) => System.Text.Unicode.Utf8.IsValid(data);

    /// <summary>str.encode('utf-8'); an unpaired surrogate fails as it does in Python.</summary>
    public static byte[] Utf8(string text) => new UTF8Encoding(false, true).GetBytes(text);

    /// <summary>bytes.hex().</summary>
    public static string Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(data);

    /// <summary>The translation text-mode reading applies: \r\n and a lone \r become \n.</summary>
    public static string UniversalNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}

// Regular-expression fragments with Python's meaning where .NET's differs.
internal static class PyRe
{
    /// <summary>Python's \s for str patterns: str.isspace(), which includes \x1c-\x1f that .NET's \s omits.</summary>
    public const string Space = @"[\t\n\x0b\x0c\r\x1c-\x1f \x85\xa0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]";

    /// <summary>Python's \S.</summary>
    public const string NonSpace = @"[^\t\n\x0b\x0c\r\x1c-\x1f \x85\xa0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]";
}
