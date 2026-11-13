using System.Collections.ObjectModel;
using System.Text;
using System.Xml;

namespace MetadataSharp.Core.Xmp;

/// <summary>The supported literal and literal-array value forms.</summary>
public enum XmpValueKind
{
    /// <summary>A simple text value.</summary>
    Text,
    /// <summary>An unordered array (<c>rdf:Bag</c>).</summary>
    Bag,
    /// <summary>An ordered array (<c>rdf:Seq</c>).</summary>
    Sequence,
    /// <summary>Alternative texts by language (<c>rdf:Alt</c> with <c>xml:lang</c>).</summary>
    LanguageAlternative,
    /// <summary>An array of alternatives that are not told apart by language (<c>rdf:Alt</c>); the first item is the default.</summary>
    Alternative
}

/// <summary>A deeply owned immutable XMP value; strings are not interpreted as domain values.</summary>
public sealed class XmpValue
{
    private XmpValue(XmpValueKind kind, string? text, string[] items, Dictionary<string, string> languages)
    { Kind = kind; Text = text; Items = Array.AsReadOnly(items); Languages = new ReadOnlyDictionary<string, string>(languages); }
    /// <summary>The form of the value.</summary>
    public XmpValueKind Kind { get; }
    /// <summary>The text of a <see cref="XmpValueKind.Text"/> value; null for the other forms.</summary>
    public string? Text { get; }
    /// <summary>The items of a bag, a sequence or an array of alternatives, in order; empty for the other forms.</summary>
    public IReadOnlyList<string> Items { get; }
    /// <summary>The texts of a language alternative by language tag, compared without case; empty for the other forms.</summary>
    public IReadOnlyDictionary<string, string> Languages { get; }

    /// <summary>Creates a simple text value.</summary>
    /// <param name="text">The XML-valid text to store.</param>
    /// <returns>An immutable text value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentException">The text holds characters XML does not allow or exceeds the default budget.</exception>
    public static XmpValue CreateText(string text)
    {
        int bytes = 0;
        CheckText(text, ref bytes);
        return new(XmpValueKind.Text, text, [], new());
    }
    /// <summary>Creates a bag, a sequence or an array of alternatives of texts.</summary>
    /// <param name="kind">The array form to create.</param>
    /// <param name="items">The text values in array order.</param>
    /// <returns>An immutable array value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or an item is null.</exception>
    /// <exception cref="ArgumentException">The kind is not <see cref="XmpValueKind.Bag"/>, <see cref="XmpValueKind.Sequence"/> or <see cref="XmpValueKind.Alternative"/>, or the items are not valid or exceed the default budget.</exception>
    public static XmpValue CreateArray(XmpValueKind kind, IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (kind is not (XmpValueKind.Bag or XmpValueKind.Sequence or XmpValueKind.Alternative)) throw new ArgumentException("An array must be Bag, Sequence or Alternative.", nameof(kind));
        var copy = new List<string>(); int bytes = 0;
        foreach (string text in items)
        {
            if (copy.Count >= 8192) throw new ArgumentException("Array exceeds the default item budget.", nameof(items));
            CheckText(text, ref bytes); copy.Add(text);
        }
        return new(kind, null, copy.ToArray(), new());
    }
    /// <summary>Creates a language alternative from pairs of a language tag (<c>x-default</c>, <c>en-US</c>) and a text.</summary>
    /// <param name="values">The language-tag and text pairs; tags are unique without regard to case.</param>
    /// <returns>An immutable language-alternative value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or a language or text is null.</exception>
    /// <exception cref="ArgumentException">A language tag is not valid or occurs twice, or the texts are not valid or exceed the default budget.</exception>
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
    internal static void CheckText(string text)
    {
        int bytes = 0;
        CheckText(text, ref bytes);
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
