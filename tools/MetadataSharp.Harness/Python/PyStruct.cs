using System.Buffers.Binary;
using System.Globalization;

namespace MetadataSharp.Harness.Python;

/// <summary>
/// The part of Python's struct module the fixture writers use: byte order prefixes ('&lt;', '&gt;', '!', '='),
/// repeat counts and the codes x, c, b, B, ?, h, H, i, I, l, L, q, Q, f, d and s. Standard sizes, no alignment
/// (the scripts always give a byte order).
/// </summary>
internal static class PyStruct
{
    private readonly record struct Code(char Kind, int Count);

    private static (bool Little, List<Code> Codes) Parse(string format)
    {
        bool little = !BitConverter.IsLittleEndian ? false : true;
        int start = 0;
        if (format.Length > 0 && format[0] is '<' or '>' or '!' or '=' or '@')
        {
            little = format[0] switch { '<' => true, '>' or '!' => false, _ => BitConverter.IsLittleEndian };
            start = 1;
        }
        var codes = new List<Code>();
        int count = -1;
        for (int i = start; i < format.Length; i++)
        {
            char c = format[i];
            if (char.IsWhiteSpace(c)) continue;
            if (char.IsAsciiDigit(c))
            {
                count = (count < 0 ? 0 : count * 10) + (c - '0');
                continue;
            }
            if (Size(c) < 0) throw new PyException("struct.error", "bad char in struct format");
            codes.Add(new Code(c, count < 0 ? 1 : count));
            count = -1;
        }
        return (little, codes);
    }

    private static int Size(char kind) => kind switch
    {
        'x' or 'c' or 'b' or 'B' or '?' or 's' => 1,
        'h' or 'H' => 2,
        'i' or 'I' or 'l' or 'L' or 'f' => 4,
        'q' or 'Q' or 'd' => 8,
        _ => -1,
    };

    /// <summary>struct.calcsize(format).</summary>
    public static int CalcSize(string format) => Parse(format).Codes.Sum(code => Size(code.Kind) * code.Count);

    /// <summary>struct.pack(format, *values): integers as long (or int), floats as double, 's' as byte[].</summary>
    public static byte[] Pack(string format, params object[] values)
    {
        var buffer = new byte[CalcSize(format)];
        PackInto(format, buffer, 0, values);
        return buffer;
    }

    /// <summary>struct.pack_into(format, buffer, offset, *values).</summary>
    public static void PackInto(string format, Span<byte> buffer, int offset, params object[] values)
    {
        var (little, codes) = Parse(format);
        int at = offset, next = 0;
        foreach (var code in codes)
        {
            if (code.Kind == 'x')
            {
                buffer.Slice(at, code.Count).Clear();
                at += code.Count;
                continue;
            }
            if (code.Kind == 's')
            {
                var data = (byte[])values[next++];
                var target = buffer.Slice(at, code.Count);
                target.Clear();
                data.AsSpan(0, Math.Min(data.Length, code.Count)).CopyTo(target);
                at += code.Count;
                continue;
            }
            for (int n = 0; n < code.Count; n++)
            {
                Write(code.Kind, little, buffer.Slice(at), values[next++]);
                at += Size(code.Kind);
            }
        }
        if (next != values.Length) throw new PyException("struct.error", $"pack expected {next} items for packing (got {values.Length})");
    }

    private static void Write(char kind, bool little, Span<byte> target, object value)
    {
        if (kind is 'f' or 'd')
        {
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (kind == 'f')
            {
                if (little) BinaryPrimitives.WriteSingleLittleEndian(target, (float)number);
                else BinaryPrimitives.WriteSingleBigEndian(target, (float)number);
            }
            else if (little) BinaryPrimitives.WriteDoubleLittleEndian(target, number);
            else BinaryPrimitives.WriteDoubleBigEndian(target, number);
            return;
        }
        if (kind == 'c')
        {
            target[0] = ((byte[])value)[0];
            return;
        }
        long v = value is bool flag ? (flag ? 1 : 0) : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        (long min, long max) = kind switch
        {
            'b' => (sbyte.MinValue, sbyte.MaxValue),
            'B' or '?' => (0, byte.MaxValue),
            'h' => (short.MinValue, short.MaxValue),
            'H' => (0, ushort.MaxValue),
            'i' or 'l' => (int.MinValue, int.MaxValue),
            'I' or 'L' => (0, uint.MaxValue),
            _ => (long.MinValue, long.MaxValue),
        };
        if (kind != 'Q' && (v < min || v > max))
            throw new PyException("struct.error", $"'{kind}' format requires {min} <= number <= {max}");
        switch (kind)
        {
            case 'b' or 'B' or '?': target[0] = (byte)v; break;
            case 'h' or 'H':
                if (little) BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)v);
                else BinaryPrimitives.WriteUInt16BigEndian(target, (ushort)v);
                break;
            case 'i' or 'I' or 'l' or 'L':
                if (little) BinaryPrimitives.WriteUInt32LittleEndian(target, (uint)v);
                else BinaryPrimitives.WriteUInt32BigEndian(target, (uint)v);
                break;
            default:
                if (little) BinaryPrimitives.WriteUInt64LittleEndian(target, (ulong)v);
                else BinaryPrimitives.WriteUInt64BigEndian(target, (ulong)v);
                break;
        }
    }

    /// <summary>struct.unpack_from(format, data, offset): integers as long, floats as double, 's' as byte[].</summary>
    public static object[] UnpackFrom(string format, ReadOnlySpan<byte> data, int offset = 0)
    {
        var (little, codes) = Parse(format);
        int size = codes.Sum(code => Size(code.Kind) * code.Count);
        if (offset < 0 || offset + size > data.Length)
            throw new PyException("struct.error", $"unpack_from requires a buffer of at least {size + offset} bytes for unpacking {size} bytes at offset {offset} (actual buffer size is {data.Length})");
        var result = new List<object>();
        int at = offset;
        foreach (var code in codes)
        {
            if (code.Kind == 'x')
            {
                at += code.Count;
                continue;
            }
            if (code.Kind == 's')
            {
                result.Add(data.Slice(at, code.Count).ToArray());
                at += code.Count;
                continue;
            }
            for (int n = 0; n < code.Count; n++)
            {
                var span = data.Slice(at);
                result.Add(code.Kind switch
                {
                    'b' => (long)(sbyte)span[0],
                    'B' => (long)span[0],
                    '?' => span[0] != 0,
                    'c' => new[] { span[0] },
                    'h' => (long)(little ? BinaryPrimitives.ReadInt16LittleEndian(span) : BinaryPrimitives.ReadInt16BigEndian(span)),
                    'H' => (long)(little ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt16BigEndian(span)),
                    'i' or 'l' => (long)(little ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span)),
                    'I' or 'L' => (long)(little ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span)),
                    'q' => little ? BinaryPrimitives.ReadInt64LittleEndian(span) : BinaryPrimitives.ReadInt64BigEndian(span),
                    'Q' => (object)(long)(little ? BinaryPrimitives.ReadUInt64LittleEndian(span) : BinaryPrimitives.ReadUInt64BigEndian(span)),
                    'f' => (double)(little ? BinaryPrimitives.ReadSingleLittleEndian(span) : BinaryPrimitives.ReadSingleBigEndian(span)),
                    _ => little ? BinaryPrimitives.ReadDoubleLittleEndian(span) : BinaryPrimitives.ReadDoubleBigEndian(span),
                });
                at += Size(code.Kind);
            }
        }
        return [.. result];
    }

    /// <summary>struct.unpack(format, data): the whole buffer has to be the format's size.</summary>
    public static object[] Unpack(string format, ReadOnlySpan<byte> data)
    {
        int size = CalcSize(format);
        if (data.Length != size) throw new PyException("struct.error", $"unpack requires a buffer of {size} bytes");
        return UnpackFrom(format, data);
    }

    /// <summary>One unsigned or signed integer at an offset: the common case of unpack_from(...)[0].</summary>
    public static long U(string format, ReadOnlySpan<byte> data, int offset = 0) => (long)UnpackFrom(format, data, offset)[0];
}
