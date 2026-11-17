using System.Collections;

namespace MetadataSharp.Harness.Python;

// A Python exception a script could meet in a malformed report: its type name, message and repr().
internal class PyException(string type, string message) : Exception(message)
{
    public string Type { get; } = type;

    /// <summary>repr(error) for an exception raised with one string argument.</summary>
    public string Repr() => $"{Type}({Py.ReprString(Message)})";

    /// <summary>repr(error) of any exception: Python's form for these, the .NET type and message otherwise.</summary>
    public static string ReprOf(Exception error) => error is PyException python ? python.Repr() : $"{error.GetType().Name}({Py.ReprString(error.Message)})";

    /// <summary>The name Python gives the exception it would have raised: the type of a PyException, the OSError
    /// subclass of an errno, the subprocess errors; the .NET type name of anything else.</summary>
    public static string TypeOf(Exception error) => error switch
    {
        PyException python => python.Type,
        PyOSError os => os.Errno switch
        {
            2 => "FileNotFoundError",
            13 => "PermissionError",
            17 => "FileExistsError",
            20 => "NotADirectoryError",
            21 => "IsADirectoryError",
            _ => "OSError",
        },
        PyTimeoutExpired => "subprocess.TimeoutExpired",
        PyCalledProcessError => "subprocess.CalledProcessError",
        System.Text.DecoderFallbackException => "UnicodeDecodeError",
        KeyNotFoundException => "KeyError",
        _ => error.GetType().Name,
    };

    /// <summary>The last line of the traceback of an uncaught exception: "Type: message", or the type alone.</summary>
    public static string LastLineOf(Exception error) => error.Message.Length > 0 ? $"{TypeOf(error)}: {error.Message}" : TypeOf(error);

    /// <summary>traceback.format_exc() as far as .NET can give it: the header, the stack and the last line.</summary>
    public static string TraceOf(Exception error) => "Traceback (most recent call last):\n" + error.StackTrace + "\n" + LastLineOf(error);
}

internal sealed class PyTypeError(string message) : PyException("TypeError", message);

internal sealed class PyAttributeError(string message) : PyException("AttributeError", message);

internal sealed class PyKeyError(object? key) : PyException("KeyError", Py.Repr(key))
{
    public object? Key { get; } = key;
}

// The operations a script applied to values of a loaded JSON document, with the failures Python has
// when the document does not have the expected shape.
internal static class PyOps
{
    /// <summary>value.get(key, fallback): an AttributeError for anything but a dict.</summary>
    public static object? Get(object? target, object key, object? fallback = null)
    {
        if (target is PyDict dict) return dict.Get(key, fallback);
        throw new PyAttributeError($"'{Py.TypeName(target)}' object has no attribute 'get'");
    }

    /// <summary>value[key] of a dict.</summary>
    public static object? Item(object? target, object key)
    {
        if (target is PyDict dict) return dict.TryGetValue(key, out object? value) ? value : throw new PyKeyError(key);
        throw new PyTypeError(target is null ? "'NoneType' object is not subscriptable" : $"'{Py.TypeName(target)}' object is not subscriptable");
    }

    /// <summary>hash(value) succeeds: lists and dicts are unhashable.</summary>
    public static object? Hashable(object? value)
    {
        if (value is PyDict || (value is IList && value is not PyTuple && value is not byte[]))
            throw new PyTypeError($"unhashable type: '{Py.TypeName(value)}'");
        if (value is PyTuple tuple) foreach (object? item in tuple) Hashable(item);
        return value;
    }

    /// <summary>value in a set of strings.</summary>
    public static bool In(object? value, IReadOnlySet<string> set) => Hashable(value) is string text && set.Contains(text);

    /// <summary>iter(value): a list or tuple's items, a dict's keys, a string's characters.</summary>
    public static IEnumerable<object?> Iterate(object? value) => value switch
    {
        string text => Py.CodePoints(text).Select(point => (object?)Py.FromCodePoint(point)).ToList(),
        PyDict dict => dict.Keys.Cast<object?>().ToList(),
        PyTuple tuple => tuple,
        IList list when value is not byte[] => list.Cast<object?>(),
        _ => throw new PyTypeError($"'{Py.TypeName(value)}' object is not iterable"),
    };

    /// <summary>separator.join(items): every item must be a string.</summary>
    public static string Join(string separator, IEnumerable<object?> items)
    {
        var parts = new List<string>();
        int index = 0;
        foreach (object? item in items)
        {
            if (item is not string text) throw new PyTypeError($"sequence item {index}: expected str instance, {Py.TypeName(item)} found");
            parts.Add(text);
            index++;
        }
        return string.Join(separator, parts);
    }
}
