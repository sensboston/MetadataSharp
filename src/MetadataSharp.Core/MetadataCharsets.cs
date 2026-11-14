using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MetadataSharp.Core;

/// <summary>
/// One character set of the matrix: the name Exiv2's <c>-n</c> option and its own message texts use,
/// and the .NET encoding that reads and writes it.
/// </summary>
/// <remarks>
/// The encoding given here is strict: a byte sequence the character set cannot represent raises
/// <see cref="DecoderFallbackException"/> instead of being replaced, so a wrong choice of character
/// set is visible rather than silent. Nothing in this library changes a stored value on the strength
/// of such a choice: the bytes stay what they are, and the choice only says how to read or write them.
/// </remarks>
public sealed class MetadataCharset
{
    private readonly Lazy<Encoding> encoding;

    internal MetadataCharset(string name, int codePage, params string[] aliases)
    {
        Name = name;
        CodePage = codePage;
        Aliases = aliases;
        encoding = new Lazy<Encoding>(() => MetadataCharsets.Create(codePage));
    }

    /// <summary>The name this library prints for the character set, in the spelling Exiv2 uses: <c>windows-1251</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// The other spellings <see cref="MetadataCharsets.TryGet"/> answers to, including the ones Exiv2
    /// itself writes into its messages: <c>latin1</c>, <c>UCS-2LE</c>, <c>UTF-8</c>.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>The numeric code page of the character set: 1251 for <c>windows-1251</c>.</summary>
    public int CodePage { get; }

    /// <summary>
    /// The encoding of the character set, obtained from the platform's own code-page provider: no
    /// package is added for it. Strict in both directions; the first use is where the provider is
    /// asked for it.
    /// </summary>
    /// <exception cref="NotSupportedException">The platform has no code-page provider.</exception>
    public Encoding Encoding => encoding.Value;

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// The character sets metadata text is read and written in, named the way Exiv2 0.28.9's <c>-n</c>
/// option names them.
/// </summary>
/// <remarks>
/// <para>
/// Exiv2 converts a <c>UNICODE</c> EXIF UserComment or a Windows XP tag through the character set
/// named on its command line, and the conversion is what its messages and its print-out of those
/// values show. This matrix is the same list of names, resolved to encodings the platform itself
/// carries: <see cref="Encoding.RegisterProvider"/> is called once with the code-page provider
/// <c>System.Text.Encoding.CodePages</c>, which is part of the .NET runtime and needs no package.
/// </para>
/// <para>
/// A name is matched without regard to case, and the hyphens and underscores of a name are the
/// same character to it: <c>windows-1251</c>, <c>Windows_1251</c> and <c>CP1251</c> all name one
/// entry. The list is the character sets a camera, an IPTC packet or a person writing metadata is
/// likely to name; it is not every name the platform can resolve, and a name outside it is refused
/// rather than guessed at.
/// </para>
/// </remarks>
public static class MetadataCharsets
{
    /// <summary>The character set of seven-bit text, the default of every metadata text field.</summary>
    public const string Ascii = "US-ASCII";

    /// <summary>The character set of an IPTC packet that declares one, and of Exiv2's own XMP values.</summary>
    public const string Utf8 = "UTF-8";

    private static readonly MetadataCharset[] Matrix =
    [
        new("US-ASCII", 20127, "ASCII", "us-ascii", "ANSI_X3.4-1968"),
        new("UTF-8", 65001, "utf8", "UTF8"),
        new("UTF-16LE", 1200, "UCS-2LE", "utf-16", "utf16", "UCS-2", "unicode", "unicodeLittle"),
        new("UTF-16BE", 1201, "UCS-2BE", "utf-16be", "utf16be", "unicodeBigUnmarked"),
        new("ISO-8859-1", 28591, "latin1", "l1", "ISO8859-1", "CP819"),
        new("ISO-8859-2", 28592, "latin2", "l2", "ISO8859-2"),
        new("ISO-8859-3", 28593, "latin3", "ISO8859-3"),
        new("ISO-8859-4", 28594, "latin4", "ISO8859-4"),
        new("ISO-8859-5", 28595, "ISO8859-5", "cyrillic"),
        new("ISO-8859-6", 28596, "ISO8859-6", "arabic"),
        new("ISO-8859-7", 28597, "ISO8859-7", "greek"),
        new("ISO-8859-8", 28598, "ISO8859-8", "hebrew"),
        new("ISO-8859-9", 28599, "latin5", "ISO8859-9"),
        new("ISO-8859-13", 28603, "ISO8859-13", "latin7"),
        new("ISO-8859-15", 28605, "latin9", "ISO8859-15", "Latin-9"),
        new("windows-1250", 1250, "CP1250", "MS-EE"),
        new("windows-1251", 1251, "CP1251", "MS-CYRL"),
        new("windows-1252", 1252, "CP1252", "MS-ANSI"),
        new("windows-1253", 1253, "CP1253", "MS-GREEK"),
        new("windows-1254", 1254, "CP1254", "MS-TURK"),
        new("windows-1255", 1255, "CP1255", "MS-HEBR"),
        new("windows-1256", 1256, "CP1256", "MS-ARAB"),
        new("windows-1257", 1257, "CP1257", "Winbaltrim"),
        new("windows-1258", 1258, "CP1258"),
        new("windows-874", 874, "CP874", "TIS-620", "TIS620"),
        new("CP437", 437, "IBM437", "437"),
        new("CP850", 850, "IBM850", "850"),
        new("CP852", 852, "IBM852", "852"),
        new("CP866", 866, "IBM866", "866"),
        new("KOI8-R", 20866, "KOI8R", "koi8r"),
        new("KOI8-U", 21866, "KOI8U", "koi8u"),
        new("macintosh", 10000, "MacRoman", "MAC", "MACINTOSH"),
        new("Shift_JIS", 932, "SJIS", "Shift-JIS", "MS_Kanji"),
        new("EUC-JP", 51932, "EUCJP", "euc-jp"),
        new("ISO-2022-JP", 50220, "JIS", "ISO2022JP"),
        new("GB2312", 936, "GBK", "CP936", "EUC-CN"),
        new("Big5", 950, "BIG5", "CP950", "Big-5"),
        new("EUC-KR", 949, "CP949", "UHC", "ks_c_5601-1987"),
    ];

    private static readonly Dictionary<string, MetadataCharset> ByName = Build();

    /// <summary>
    /// Every character set of the matrix, in the order of the list, each name once.
    /// </summary>
    public static IReadOnlyList<MetadataCharset> All { get; } = Array.AsReadOnly(Matrix);

    /// <summary>
    /// The names of the matrix, in its order: <c>US-ASCII</c>, <c>UTF-8</c>, <c>windows-1251</c> and
    /// the rest. An alias is not repeated here; <see cref="MetadataCharset.Aliases"/> holds those.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(Matrix.Select(charset => charset.Name).ToArray());

    /// <summary>True when the name, or one of its aliases, is a character set of the matrix.</summary>
    /// <param name="name">The name to look up; case, hyphens and underscores do not matter.</param>
    /// <param name="charset">The character set the name stands for, when there is one.</param>
    /// <returns><see langword="true"/> when the name is known; otherwise, <see langword="false"/>.</returns>
    public static bool TryGet(string? name, [NotNullWhen(true)] out MetadataCharset? charset)
    {
        charset = null;
        return name is not null && ByName.TryGetValue(Key(name), out charset);
    }

    /// <summary>The character set a name stands for.</summary>
    /// <exception cref="ArgumentException">The name is not one of the matrix; the message lists the names it holds.</exception>
    /// <param name="name">The name: <c>latin1</c>, <c>windows-1251</c>, <c>UCS-2LE</c>.</param>
    public static MetadataCharset Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (TryGet(name, out MetadataCharset? charset))
            return charset;
        throw new ArgumentException(
            $"`{name}' is not a character set of the matrix. The names it holds are: {string.Join(", ", Names)}.",
            nameof(name));
    }

    /// <summary>The name this library prints for an encoding, when the encoding is one of the matrix.</summary>
    /// <param name="encoding">The encoding to look up.</param>
    /// <param name="name">The name of the character set the encoding is, when it is one of the matrix.</param>
    /// <returns><see langword="true"/> when the encoding is one of the matrix; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetName(Encoding encoding, [NotNullWhen(true)] out string? name)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        foreach (MetadataCharset charset in Matrix)
        {
            if (charset.CodePage == encoding.CodePage)
            {
                name = charset.Name;
                return true;
            }
        }
        name = null;
        return false;
    }

    // The provider that carries the code pages is part of the .NET runtime: the source projects of
    // this library declare no package reference, and this is where that promise is spent.
    internal static Encoding Create(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static string Key(string name) =>
        new(name.Where(character => character is not ('-' or '_' or ' ')).Select(char.ToLowerInvariant).ToArray());

    private static Dictionary<string, MetadataCharset> Build()
    {
        var names = new Dictionary<string, MetadataCharset>(StringComparer.Ordinal);
        foreach (MetadataCharset charset in Matrix)
        {
            names[Key(charset.Name)] = charset;
            foreach (string alias in charset.Aliases)
                names.TryAdd(Key(alias), charset);
        }
        return names;
    }
}
