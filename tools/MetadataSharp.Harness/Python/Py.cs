using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MetadataSharp.Harness.Python;

// The gates were Python scripts, and their reports, annotations and fixture manifests are compared byte
// for byte with what those scripts wrote. The values of a report are therefore modelled as the scripts
// held them: string, long, double, bool, null, PyTuple, List<object?> and PyDict, with Python's
// equality, ordering, repr() and str().
internal static class Py
{
    /// <summary>Python's == on report values: numbers by value, containers element by element.</summary>
    public static bool Eq(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (IsNumber(left) && IsNumber(right)) return CompareNumbers(left, right) == 0;
        switch (left)
        {
            case string s1: return right is string s2 && string.Equals(s1, s2, StringComparison.Ordinal);
            case PyTuple t1: return right is PyTuple t2 && SequenceEq(t1, t2);
            case PyDict d1: return right is PyDict d2 && DictEq(d1, d2);
            case byte[] b1: return right is byte[] b2 && b1.AsSpan().SequenceEqual(b2);
            case IList l1: return right is IList l2 && SequenceEq(l1.Cast<object?>().ToList(), l2.Cast<object?>().ToList());
            default: return left.Equals(right);
        }
    }

    /// <summary>A hash consistent with <see cref="Eq"/>.</summary>
    public static int Hash(object? value) => value switch
    {
        null => 0,
        bool b => b ? 1 : 0,
        int i => ((long)i).GetHashCode(),
        long l => l.GetHashCode(),
        double d => d == Math.Floor(d) && Math.Abs(d) < 9.2e18 ? ((long)d).GetHashCode() : d.GetHashCode(),
        string s => StringComparer.Ordinal.GetHashCode(s),
        PyTuple t => t.Aggregate(17, (hash, item) => unchecked(hash * 31 + Hash(item))),
        _ => value.GetHashCode(),
    };

    /// <summary>Python's ordering (sorted, min, max, tuple comparison); mixed types fail as they do there.</summary>
    public static int Compare(object? left, object? right)
    {
        if (IsNumber(left) && IsNumber(right)) return CompareNumbers(left!, right!);
        switch (left)
        {
            case string a when right is string b: return CompareStrings(a, b);
            case PyTuple a when right is PyTuple b: return CompareSequences(a, b);
            case IList a when right is IList b && left is not PyTuple && right is not PyTuple:
                return CompareSequences(a.Cast<object?>().ToList(), b.Cast<object?>().ToList());
        }
        if (left is null && right is null) return 0;
        throw new PyTypeError($"'<' not supported between instances of '{TypeName(left)}' and '{TypeName(right)}'");
    }

    /// <summary>Strings ordered by code point, not by UTF-16 unit.</summary>
    public static int CompareStrings(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            int x = NextCodePoint(a, ref i), y = NextCodePoint(b, ref j);
            if (x != y) return x < y ? -1 : 1;
        }
        return (a.Length - i).CompareTo(b.Length - j) switch { < 0 => -1, > 0 => 1, _ => 0 };
    }

    // The code point at index, advancing past it; an unpaired surrogate is a code point of its own.
    private static int NextCodePoint(string text, ref int index)
    {
        char c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            int value = char.ConvertToUtf32(c, text[index + 1]);
            index += 2;
            return value;
        }
        index++;
        return c;
    }

    /// <summary>A comparer for List.Sort that follows <see cref="Compare"/>.</summary>
    public static readonly IComparer<object?> Comparer = Comparer<object?>.Create(Compare);

    /// <summary>An equality comparer for dictionary keys that follows <see cref="Eq"/>.</summary>
    public static readonly IEqualityComparer<object?> EqualityComparer = new KeyComparer();

    /// <summary>sorted(items): a stable sort with Python's ordering.</summary>
    public static List<T> Sorted<T>(IEnumerable<T> items) => Sorted(items, item => (object?)item);

    /// <summary>sorted(items, key=key): a stable sort with Python's ordering of the keys.</summary>
    public static List<T> Sorted<T>(IEnumerable<T> items, Func<T, object?> key, bool reverse = false)
    {
        var list = items.Select((item, index) => (item, key: key(item), index)).ToList();
        list.Sort((x, y) =>
        {
            int order = Compare(x.key, y.key);
            if (reverse) order = -order;
            return order != 0 ? order : x.index.CompareTo(y.index);
        });
        return list.Select(entry => entry.item).ToList();
    }

    /// <summary>repr(value).</summary>
    public static string Repr(object? value) => value switch
    {
        null => "None",
        bool b => b ? "True" : "False",
        string s => ReprString(s),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        BigInteger big => big.ToString(CultureInfo.InvariantCulture),
        double d => FloatRepr(d),
        PyTuple t => t.Count == 1 ? "(" + Repr(t[0]) + ",)" : "(" + string.Join(", ", t.Select(Repr)) + ")",
        PyDict d => "{" + string.Join(", ", d.Select(pair => Repr(pair.Key) + ": " + Repr(pair.Value))) + "}",
        byte[] bytes => ReprBytes(bytes),
        IEnumerable<string> strings when value is not string => "[" + string.Join(", ", strings.Select(ReprString)) + "]",
        IList list => "[" + string.Join(", ", list.Cast<object?>().Select(Repr)) + "]",
        _ => value.ToString() ?? "",
    };

    /// <summary>bool(value): None, False, zero and empty strings and containers are false.</summary>
    public static bool Truthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        BigInteger big => !big.IsZero,
        string s => s.Length > 0,
        PyTuple t => t.Count > 0,
        PyDict d => d.Count > 0,
        byte[] bytes => bytes.Length > 0,
        ICollection collection => collection.Count > 0,
        _ => true,
    };

    /// <summary>left or right: the first operand when it is truthy, otherwise the second.</summary>
    public static object? Or(object? left, object? right) => Truthy(left) ? left : right;

    /// <summary>str(value): the text itself for a string, repr() for everything else.</summary>
    public static string Str(object? value) => value is string s ? s : Repr(value);

    /// <summary>repr() of a str: the quote Python chooses and its escapes of unprintable characters.</summary>
    public static string ReprString(string text)
    {
        char quote = text.Contains('\'') && !text.Contains('"') ? '"' : '\'';
        var builder = new StringBuilder(text.Length + 2);
        builder.Append(quote);
        foreach (int c in CodePoints(text))
        {
            if (c == quote || c == '\\') builder.Append('\\').Append((char)c);
            else if (c == '\t') builder.Append("\\t");
            else if (c == '\n') builder.Append("\\n");
            else if (c == '\r') builder.Append("\\r");
            else if (c < ' ' || c == 0x7F) builder.Append("\\x").Append(c.ToString("x2", CultureInfo.InvariantCulture));
            else if (c < 0x7F) builder.Append((char)c);
            else if (IsPrintable(c)) builder.Append(FromCodePoint(c));
            else if (c <= 0xFF) builder.Append("\\x").Append(c.ToString("x2", CultureInfo.InvariantCulture));
            else if (c <= 0xFFFF) builder.Append("\\u").Append(c.ToString("x4", CultureInfo.InvariantCulture));
            else builder.Append("\\U").Append(c.ToString("x8", CultureInfo.InvariantCulture));
        }
        builder.Append(quote);
        return builder.ToString();
    }

    /// <summary>The text of one code point, an unpaired surrogate included.</summary>
    public static string FromCodePoint(int codePoint) =>
        codePoint is >= 0xD800 and <= 0xDFFF ? ((char)codePoint).ToString() : char.ConvertFromUtf32(codePoint);

    /// <summary>repr() of bytes.</summary>
    public static string ReprBytes(ReadOnlySpan<byte> bytes)
    {
        bool hasSingle = bytes.IndexOf((byte)'\'') >= 0, hasDouble = bytes.IndexOf((byte)'"') >= 0;
        char quote = hasSingle && !hasDouble ? '"' : '\'';
        var builder = new StringBuilder("b").Append(quote);
        foreach (byte b in bytes)
        {
            if (b == quote || b == '\\') builder.Append('\\').Append((char)b);
            else if (b == '\t') builder.Append("\\t");
            else if (b == '\n') builder.Append("\\n");
            else if (b == '\r') builder.Append("\\r");
            else if (b < ' ' || b >= 0x7F) builder.Append("\\x").Append(b.ToString("x2", CultureInfo.InvariantCulture));
            else builder.Append((char)b);
        }
        return builder.Append(quote).ToString();
    }

    /// <summary>The code points of a string; an unpaired surrogate is one code point, as in Python.</summary>
    public static IEnumerable<int> CodePoints(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            else yield return c;
        }
    }

    /// <summary>len(text): the number of code points.</summary>
    public static int Len(string text) => CodePoints(text).Count();

    /// <summary>str.isprintable() for one code point.</summary>
    public static bool IsPrintable(int codePoint)
    {
        if (codePoint == ' ') return true;
        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        return category switch
        {
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.SpaceSeparator => false,
            _ => true,
        };
    }

    /// <summary>repr(float): the shortest round-trip digits, with Python's choice of notation.</summary>
    public static string FloatRepr(double value)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        if (value == 0) return double.IsNegative(value) ? "-0.0" : "0.0";
        string sign = value < 0 ? "-" : "";
        (string digits, int point) = ShortestDigits(Math.Abs(value));
        if (point <= -4 || point > 16)
        {
            string mantissa = digits.Length == 1 ? digits : digits[0] + "." + digits[1..];
            int exponent = point - 1;
            return sign + mantissa + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
        }
        return sign + FixedFromDigits(digits, point, true);
    }

    private static string FixedFromDigits(string digits, int point, bool addZero)
    {
        if (point <= 0) return "0." + new string('0', -point) + digits;
        if (point >= digits.Length) return digits + new string('0', point - digits.Length) + (addZero ? ".0" : "");
        return digits[..point] + "." + digits[point..];
    }

    // The shortest decimal digits that round-trip, and the position of the decimal point after the first digit.
    private static (string Digits, int Point) ShortestDigits(double value)
    {
        string shortest = value.ToString("R", CultureInfo.InvariantCulture);
        int exponent = 0;
        int e = shortest.IndexOfAny(['E', 'e']);
        string mantissa = shortest;
        if (e >= 0)
        {
            exponent = int.Parse(shortest[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            mantissa = shortest[..e];
        }
        int dot = mantissa.IndexOf('.');
        string whole = dot >= 0 ? mantissa[..dot] : mantissa;
        string fraction = dot >= 0 ? mantissa[(dot + 1)..] : "";
        string digits = (whole + fraction).TrimStart('0');
        int leadingZeros = (whole + fraction).Length - (whole + fraction).TrimStart('0').Length;
        int point = whole.Length + exponent - leadingZeros;
        digits = digits.TrimEnd('0');
        if (digits.Length == 0) digits = "0";
        return (digits, point);
    }

    /// <summary>format(value, '.Nf'): correctly rounded (half to even on the exact binary value).</summary>
    public static string FormatFixed(double value, int decimals)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsInfinity(value)) return value > 0 ? "inf" : "-inf";
        bool negative = double.IsNegative(value);
        // The exact value is mantissa * 2^exponent; scale it by 10^decimals and round half to even.
        long bits = BitConverter.DoubleToInt64Bits(Math.Abs(value));
        int rawExponent = (int)((bits >> 52) & 0x7FF);
        long fraction = bits & 0xFFFFFFFFFFFFFL;
        BigInteger mantissa = rawExponent == 0 ? fraction : fraction | (1L << 52);
        int exponent = rawExponent == 0 ? -1074 : rawExponent - 1075;
        BigInteger numerator = mantissa * BigInteger.Pow(10, decimals), denominator = BigInteger.One;
        if (exponent >= 0) numerator <<= exponent;
        else denominator <<= -exponent;
        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        BigInteger twice = remainder * 2;
        if (twice > denominator || (twice == denominator && !quotient.IsEven)) quotient += 1;
        string digits = quotient.ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
        string text = decimals == 0 ? digits : digits[..^decimals] + "." + digits[^decimals..];
        return (negative ? "-" : "") + text;
    }

    /// <summary>round(value, ndigits) for ndigits >= 0, as Python rounds: correctly, half to even.</summary>
    public static double Round(double value, int decimals) =>
        double.Parse(FormatFixed(value, decimals), CultureInfo.InvariantCulture);

    /// <summary>Python's floor division of integers.</summary>
    public static long FloorDiv(long a, long b) => (long)Math.Floor((decimal)a / b);

    /// <summary>Python's modulo of integers: the sign of the divisor.</summary>
    public static long Mod(long a, long b)
    {
        long r = a % b;
        return r != 0 && (r < 0) != (b < 0) ? r + b : r;
    }

    private static bool IsNumber(object? value) => value is int or long or double or bool or BigInteger;

    private static int CompareNumbers(object left, object right)
    {
        if (left is double || right is double)
        {
            double a = ToDouble(left), b = ToDouble(right);
            return a.CompareTo(b);
        }
        return ToBig(left).CompareTo(ToBig(right));
    }

    private static double ToDouble(object value) => value switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        int i => i,
        long l => l,
        BigInteger big => (double)big,
        _ => throw new InvalidCastException(),
    };

    private static BigInteger ToBig(object value) => value switch
    {
        bool b => b ? 1 : 0,
        int i => i,
        long l => l,
        BigInteger big => big,
        _ => throw new InvalidCastException(),
    };

    private static bool SequenceEq(IReadOnlyList<object?> a, IReadOnlyList<object?> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!Eq(a[i], b[i])) return false;
        return true;
    }

    private static bool DictEq(PyDict a, PyDict b)
    {
        if (a.Count != b.Count) return false;
        foreach (var pair in a)
        {
            if (!b.TryGetValue(pair.Key, out object? other) || !Eq(pair.Value, other)) return false;
        }
        return true;
    }

    private static int CompareSequences(IReadOnlyList<object?> a, IReadOnlyList<object?> b)
    {
        int count = Math.Min(a.Count, b.Count);
        for (int i = 0; i < count; i++)
        {
            if (Eq(a[i], b[i])) continue;
            return Compare(a[i], b[i]);
        }
        return a.Count.CompareTo(b.Count) switch { < 0 => -1, > 0 => 1, _ => 0 };
    }

    /// <summary>The Python name of a value's type, for the messages Python itself would print.</summary>
    public static string TypeName(object? value) => value switch
    {
        null => "NoneType",
        bool => "bool",
        string => "str",
        int or long or BigInteger => "int",
        double => "float",
        PyTuple => "tuple",
        PyDict => "dict",
        byte[] => "bytes",
        IList => "list",
        _ => value.GetType().Name,
    };

    private sealed class KeyComparer : IEqualityComparer<object?>
    {
        public new bool Equals(object? x, object? y) => Eq(x, y);

        public int GetHashCode(object? obj) => Hash(obj);
    }
}

// An immutable tuple with Python's equality, ordering and repr.
internal sealed class PyTuple : IReadOnlyList<object?>, IEquatable<PyTuple>
{
    private readonly object?[] items;

    public PyTuple(params object?[] items) => this.items = items;

    public int Count => items.Length;

    public object? this[int index] => items[index];

    public IEnumerator<object?> GetEnumerator() => ((IEnumerable<object?>)items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

    public bool Equals(PyTuple? other) => other is not null && Py.Eq(this, other);

    public override bool Equals(object? obj) => obj is PyTuple other && Equals(other);

    public override int GetHashCode() => Py.Hash(this);

    public override string ToString() => Py.Repr(this);

    public string S(int index) => (string)items[index]!;

    public long L(int index) => Convert.ToInt64(items[index], CultureInfo.InvariantCulture);
}

// A dict: insertion-ordered, keyed by any hashable report value, with Python's equality.
internal class PyDict : IEnumerable<KeyValuePair<object, object?>>
{
    private readonly OrderedDictionary<object, object?> items = new(new NonNullKeyComparer());

    public PyDict()
    {
    }

    public PyDict(IEnumerable<KeyValuePair<object, object?>> pairs)
    {
        foreach (var pair in pairs) this[pair.Key] = pair.Value;
    }

    public int Count => items.Count;

    public object? this[object key]
    {
        get => items.TryGetValue(key, out object? value) ? value : throw new KeyNotFoundException(Py.Repr(key));
        set => items[key] = value;
    }

    public IEnumerable<object> Keys => items.Keys;

    public IEnumerable<object?> Values => items.Values;

    public bool ContainsKey(object key) => items.ContainsKey(key);

    public bool TryGetValue(object key, out object? value) => items.TryGetValue(key, out value);

    /// <summary>dict.get(key, default).</summary>
    public object? Get(object key, object? fallback = null) => items.TryGetValue(key, out object? value) ? value : fallback;

    public bool Remove(object key) => items.Remove(key);

    /// <summary>dict.pop(key, default).</summary>
    public object? Pop(object key, object? fallback = null)
    {
        if (!items.TryGetValue(key, out object? value)) return fallback;
        items.Remove(key);
        return value;
    }

    /// <summary>dict.setdefault(key, value).</summary>
    public object? SetDefault(object key, object? value)
    {
        if (items.TryGetValue(key, out object? existing)) return existing;
        items[key] = value;
        return value;
    }

    /// <summary>A shallow copy, as dict(d) or {**d}.</summary>
    public PyDict Copy() => new(items);

    /// <summary>{**this, key: value, ...}.</summary>
    public PyDict With(params (object Key, object? Value)[] changes)
    {
        var copy = Copy();
        foreach (var (key, value) in changes) copy[key] = value;
        return copy;
    }

    public string Str(object key) => (string)this[key]!;

    public string? StrOrNull(object key) => (string?)Get(key);

    public long Long(object key) => Convert.ToInt64(this[key], CultureInfo.InvariantCulture);

    public PyDict Dict(object key) => (PyDict)this[key]!;

    public List<object?> List(object key) => (List<object?>)this[key]!;

    public IEnumerator<KeyValuePair<object, object?>> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override bool Equals(object? obj) => obj is PyDict other && Py.Eq(this, other);

    public override int GetHashCode() => throw new PyTypeError("unhashable type: 'dict'");

    public override string ToString() => Py.Repr(this);

    /// <summary>Build a dict from (key, value) pairs in order.</summary>
    public static PyDict Of(params (object Key, object? Value)[] pairs)
    {
        var dict = new PyDict();
        foreach (var (key, value) in pairs) dict[key] = value;
        return dict;
    }

    private sealed class NonNullKeyComparer : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) => Py.Eq(x, y);

        public int GetHashCode(object obj) => Py.Hash(obj);
    }
}

// collections.Counter: a dict of counts in first-increment order, with most_common().
internal sealed class Counter : PyDict
{
    public Counter()
    {
    }

    public Counter(IEnumerable<object> keys)
    {
        foreach (object key in keys) Add(key);
    }

    /// <summary>counter[key] += amount.</summary>
    public void Add(object key, long amount = 1) => this[key] = CountOf(key) + amount;

    /// <summary>counter[key]: zero when absent, without inserting the key.</summary>
    public long CountOf(object key) => TryGetValue(key, out object? value) ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;

    /// <summary>sum(counter.values()).</summary>
    public long Total() => Values.Sum(value => Convert.ToInt64(value, CultureInfo.InvariantCulture));

    /// <summary>most_common(n): by count, highest first, ties in insertion order.</summary>
    public List<KeyValuePair<object, long>> MostCommon(int? n = null)
    {
        var ordered = Py.Sorted(this.Select(pair => new KeyValuePair<object, long>(pair.Key, Convert.ToInt64(pair.Value, CultureInfo.InvariantCulture))),
            pair => (object?)pair.Value, reverse: true);
        return n is null ? ordered : ordered.Take(Math.Max(0, n.Value)).ToList();
    }

    /// <summary>The (key, count) pairs in insertion order.</summary>
    public IEnumerable<KeyValuePair<object, long>> Items() =>
        this.Select(pair => new KeyValuePair<object, long>(pair.Key, Convert.ToInt64(pair.Value, CultureInfo.InvariantCulture)));
}
