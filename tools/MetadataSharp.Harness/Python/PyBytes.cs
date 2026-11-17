using System.Text;

namespace MetadataSharp.Harness.Python;

// The bytes methods the gates used, with Python's slicing, ASCII-only case and whitespace, and errors.
internal static class PyBytes
{
    private const string AsciiWhitespace = " \t\n\r\u000b\u000c";

    /// <summary>bytes.find(needle, start): the first index at or after start, or -1.</summary>
    public static int Find(byte[] haystack, ReadOnlySpan<byte> needle, long start = 0)
    {
        long from = start < 0 ? Math.Max(0, haystack.Length + start) : start;
        if (from > haystack.Length) return -1;
        int found = haystack.AsSpan((int)from).IndexOf(needle);
        return found < 0 ? -1 : found + (int)from;
    }

    /// <summary>needle in haystack.</summary>
    public static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    /// <summary>data[start:stop] with Python's clamping and negative indices.</summary>
    public static byte[] Slice(byte[] data, long start, long? stop = null)
    {
        long length = data.Length;
        long from = start < 0 ? Math.Max(0, length + start) : Math.Min(start, length);
        long to = stop is null ? length : stop.Value < 0 ? Math.Max(0, length + stop.Value) : Math.Min(stop.Value, length);
        return to <= from ? [] : data[(int)from..(int)to];
    }

    /// <summary>bytes.upper(): ASCII letters only.</summary>
    public static byte[] Upper(ReadOnlySpan<byte> data)
    {
        var result = data.ToArray();
        for (int i = 0; i < result.Length; i++) if (result[i] is >= (byte)'a' and <= (byte)'z') result[i] -= 32;
        return result;
    }

    /// <summary>bytes.lower(): ASCII letters only.</summary>
    public static byte[] Lower(ReadOnlySpan<byte> data)
    {
        var result = data.ToArray();
        for (int i = 0; i < result.Length; i++) if (result[i] is >= (byte)'A' and <= (byte)'Z') result[i] += 32;
        return result;
    }

    /// <summary>A byte of bytes.strip()'s default set: ASCII whitespace.</summary>
    public static bool IsSpace(byte value) => AsciiWhitespace.Contains((char)value);

    /// <summary>bytes.strip() or bytes.strip(chars).</summary>
    public static byte[] Strip(byte[] data, ReadOnlySpan<byte> chars = default, bool useChars = false)
    {
        int start = 0, end = data.Length;
        while (start < end && (useChars ? chars.IndexOf(data[start]) >= 0 : IsSpace(data[start]))) start++;
        while (end > start && (useChars ? chars.IndexOf(data[end - 1]) >= 0 : IsSpace(data[end - 1]))) end--;
        return data[start..end];
    }

    /// <summary>bytes.lstrip(chars).</summary>
    public static byte[] LStrip(byte[] data, ReadOnlySpan<byte> chars)
    {
        int start = 0;
        while (start < data.Length && chars.IndexOf(data[start]) >= 0) start++;
        return data[start..];
    }

    /// <summary>bytes.split(separator, maxsplit).</summary>
    public static List<byte[]> Split(byte[] data, ReadOnlySpan<byte> separator, int maxSplit = -1)
    {
        var parts = new List<byte[]>();
        int start = 0;
        while (maxSplit < 0 || parts.Count < maxSplit)
        {
            int found = data.AsSpan(start).IndexOf(separator);
            if (found < 0) break;
            parts.Add(data[start..(start + found)]);
            start += found + separator.Length;
        }
        parts.Add(data[start..]);
        return parts;
    }

    /// <summary>bytes.partition(separator).</summary>
    public static (byte[] Before, byte[] Separator, byte[] After) Partition(byte[] data, ReadOnlySpan<byte> separator)
    {
        int found = data.AsSpan().IndexOf(separator);
        return found < 0 ? (data, [], []) : (data[..found], separator.ToArray(), data[(found + separator.Length)..]);
    }

    /// <summary>bytes.fromhex(text): pairs of hex digits, ASCII whitespace allowed between pairs only.</summary>
    public static byte[] FromHex(string text)
    {
        var result = new List<byte>();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] < 0x80 && IsSpace((byte)text[i]))
            {
                while (i < text.Length && text[i] < 0x80 && IsSpace((byte)text[i])) i++;
                if (i >= text.Length) break;
            }
            int top = HexValue(text[i]);
            if (top < 0) throw new FormatException($"non-hexadecimal number found in fromhex() arg at position {i}");
            i++;
            int bottom = i < text.Length ? HexValue(text[i]) : -1;
            if (bottom < 0) throw new FormatException($"non-hexadecimal number found in fromhex() arg at position {i}");
            i++;
            result.Add((byte)((top << 4) + bottom));
        }
        return result.ToArray();
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    /// <summary>bytes.decode('ascii'): fails on a byte above 0x7F as Python raises UnicodeDecodeError.</summary>
    public static string DecodeAscii(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
            if (data[i] >= 0x80) throw new DecoderFallbackException($"'ascii' codec can't decode byte 0x{data[i]:x2} in position {i}: ordinal not in range(128)");
        return Encoding.ASCII.GetString(data);
    }

    /// <summary>int(data) for bytes: ASCII whitespace around, a sign, ASCII digits with single underscores between.</summary>
    public static bool TryParseInt(ReadOnlySpan<byte> data, out long value)
    {
        value = 0;
        int start = 0, end = data.Length;
        while (start < end && IsSpace(data[start])) start++;
        while (end > start && IsSpace(data[end - 1])) end--;
        bool negative = false;
        if (start < end && data[start] is (byte)'+' or (byte)'-')
        {
            negative = data[start] == '-';
            start++;
        }
        if (start >= end || data[start] == '_' || data[end - 1] == '_') return false;
        long result = 0;
        for (int i = start; i < end; i++)
        {
            byte b = data[i];
            if (b == '_')
            {
                if (data[i - 1] == '_') return false;
                continue;
            }
            if (b is < (byte)'0' or > (byte)'9') return false;
            try
            {
                result = checked(result * 10 + (b - '0'));
            }
            catch (OverflowException)
            {
                return false;
            }
        }
        value = negative ? -result : result;
        return true;
    }

    /// <summary>Every index where needle starts in haystack, overlapping matches included.</summary>
    public static List<int> FindAll(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        var result = new List<int>();
        if (needle.IsEmpty) return result;
        int at = 0;
        while (at <= haystack.Length)
        {
            int found = haystack.AsSpan(at).IndexOf(needle);
            if (found < 0) break;
            result.Add(at + found);
            at += found + 1;
        }
        return result;
    }
}
