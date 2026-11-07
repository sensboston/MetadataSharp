using System.Collections.ObjectModel;
using System.Text;
using System.Xml;

namespace MetadataSharp.Core.Xmp;

/// <summary>The supported literal and literal-array value forms.</summary>
public enum XmpValueKind { Text, Bag, Sequence, LanguageAlternative }

/// <summary>A deeply owned immutable XMP value; strings are not interpreted as domain values.</summary>
public sealed class XmpValue
{
    private XmpValue(XmpValueKind kind, string? text, string[] items, Dictionary<string, string> languages)
    { Kind = kind; Text = text; Items = Array.AsReadOnly(items); Languages = new ReadOnlyDictionary<string, string>(languages); }
    public XmpValueKind Kind { get; }
    public string? Text { get; }
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyDictionary<string, string> Languages { get; }

    public static XmpValue CreateText(string text)
    {
        int bytes = 0;
        CheckText(text, ref bytes);
        return new(XmpValueKind.Text, text, [], new());
    }
    public static XmpValue CreateArray(XmpValueKind kind, IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (kind is not (XmpValueKind.Bag or XmpValueKind.Sequence)) throw new ArgumentException("An array must be Bag or Sequence.", nameof(kind));
        var copy = new List<string>(); int bytes = 0;
        foreach (string text in items)
        {
            if (copy.Count >= 8192) throw new ArgumentException("Array exceeds the default item budget.", nameof(items));
            CheckText(text, ref bytes); copy.Add(text);
        }
        return new(kind, null, copy.ToArray(), new());
    }
    public static XmpValue CreateLanguageAlternative(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); int bytes = 0;
        foreach (var pair in values)
        {
            if (copy.Count >= 8192 || !ValidLanguage(pair.Key)) throw new ArgumentException("Invalid language or item budget.", nameof(values));
            CheckText(pair.Key, ref bytes); CheckText(pair.Value, ref bytes);
            if (!copy.TryAdd(pair.Key, pair.Value)) throw new ArgumentException("Duplicate language.", nameof(values));
        }
        return new(XmpValueKind.LanguageAlternative, null, [], copy);
    }
    internal static bool ValidLanguage(string? language)
    {
        if (string.IsNullOrEmpty(language) || language.Length > 1024) return false;
        string[] parts = language.Split('-');
        return parts.All(p => p.Length > 0) && parts[0].All(char.IsAsciiLetter) &&
            parts.Skip(1).All(p => p.All(char.IsAsciiLetterOrDigit));
    }
    private static void CheckText(string text, ref int bytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 1_048_576) throw new ArgumentException("Text exceeds the default value budget.");
        try { XmlConvert.VerifyXmlChars(text); }
        catch (XmlException e) { throw new ArgumentException("Invalid XML text.", e); }
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(text));
        if (bytes > 1_048_576) throw new ArgumentException("Value exceeds the default byte budget.");
    }
}
