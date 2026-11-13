using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Core.Operations;

/// <summary>What a line of a modify script does.</summary>
public enum ModifyCommandKind
{
    /// <summary><c>set key [[type] value]</c>.</summary>
    Set,
    /// <summary><c>add key [[type] value]</c>.</summary>
    Add,
    /// <summary><c>del key</c>.</summary>
    Delete,
    /// <summary><c>reg prefix namespace</c>.</summary>
    Register,
}

/// <summary>One command of a modify script, as it was read.</summary>
/// <param name="Line">The number of the line, from one.</param>
/// <param name="Kind">The command.</param>
/// <param name="Key">The key; for <see cref="ModifyCommandKind.Register"/> the prefix.</param>
/// <param name="Type">The type name the line gives, or null when it gives none and the default of the key applies.</param>
/// <param name="Value">The value with its escapes read and its quotes taken off; for <see cref="ModifyCommandKind.Register"/> the namespace. Empty when the line gives none.</param>
public sealed record ModifyCommand(int Line, ModifyCommandKind Kind, string Key, string? Type, string Value);

/// <summary>A line of a modify script that cannot be carried out.</summary>
public sealed class ModifyScriptException : FormatException
{
    internal ModifyScriptException(int line, string reason)
        : base("Line " + line.ToString(CultureInfo.InvariantCulture) + ": " + reason)
    {
        Line = line;
        Reason = reason;
    }

    /// <summary>The number of the line, from one.</summary>
    public int Line { get; }
    /// <summary>What is wrong with the line, without its number.</summary>
    public string Reason { get; }
}

/// <summary>
/// The commands of <c>exiv2 -m file</c> and <c>-M "command"</c>, read into edits. Nothing is
/// written: the edits are given to a document.
/// </summary>
/// <remarks>
/// <para>
/// A line is <c>set|add key [[type] value]</c>, <c>del key</c> or <c>reg prefix namespace</c>;
/// words are kept apart by spaces or tabs; an empty line and a line that starts with <c>#</c>
/// are skipped. The value runs to the end of the line; <c>\\ \n \r \t \uXXXX</c> are read, and one
/// pair of quotes around the whole value is taken off. A second word that is not the name of a
/// type is the start of the value.
/// </para>
/// <para>
/// EXIF. Types <c>Byte Ascii Short Long Rational Undefined SByte SShort SLong SRational Float
/// Double Comment</c>; without one, the first type the tag is defined with, or Ascii for a tag
/// without a definition. Numbers are decimal with spaces between them, rationals <c>n/d</c>;
/// Undefined and Byte take the numbers of the bytes. A Comment value may start with
/// <c>charset=Ascii|Unicode|Undefined </c>. The value is written as given, in any count
/// (<see cref="ExifEdit.SetUnchecked"/>). <c>add</c> is <c>set</c>: a directory does not hold a
/// tag twice (Exiv2 writes the duplicate).
/// </para>
/// <para>
/// IPTC. Types <c>String Date Time Short Undefined</c>; without one, the form the dataset is defined
/// with. A Date is <c>YYYY-MM-DD</c> or <c>YYYYMMDD</c>, a Time <c>HH:MM:SS</c> with an optional
/// <c>±HH:MM</c> or the eleven characters of the dataset. <c>set</c> replaces the first occurrence
/// and leaves the others, or adds the dataset when there is none; <c>add</c> appends an occurrence
/// (for a dataset that does not repeat it sets the one); <c>del</c> removes every occurrence. All
/// as Exiv2 does.
/// </para>
/// <para>
/// XMP. Types <c>XmpText XmpBag XmpSeq XmpAlt LangAlt</c>; without one, the form the property is
/// known to have (the properties of <see cref="MetadataSync"/> and the Dublin Core arrays), else
/// XmpText. For an array <c>set</c> and <c>add</c> both append an item, as with Exiv2, where
/// <c>set</c> on a bag adds to it. A LangAlt value may start with <c>lang=xx </c> or
/// <c>lang="xx" </c>; without it the language is x-default. <c>reg</c> registers a prefix for the
/// lines after it; a key with a prefix that is neither known nor registered is not a key.
/// </para>
/// </remarks>
public sealed class ModifyScript
{
    private static readonly char[] Blank = [' ', '\t'];
    private static readonly string[] ExifTypes = ["Byte", "Ascii", "Short", "Long", "Rational", "Undefined", "SByte", "SShort", "SLong", "SRational", "Float", "Double", "Comment"];
    private static readonly string[] IptcTypes = ["String", "Date", "Time", "Short", "Undefined"];
    private static readonly string[] XmpTypes = ["XmpText", "XmpBag", "XmpSeq", "XmpAlt", "LangAlt"];
    /// <summary>Every type name Exiv2 knows: one of these as the second word is a type, whatever the key.</summary>
    private static readonly string[] AllTypes = [.. ExifTypes, .. IptcTypes, .. XmpTypes, "Ifd", "Directory"];

    private readonly List<ModifyCommand> commands = [];

    private ModifyScript() => Commands = new ReadOnlyCollection<ModifyCommand>(commands);

    /// <summary>The commands in the order of the script.</summary>
    public IReadOnlyList<ModifyCommand> Commands { get; }
    /// <summary>The changes to EXIF.</summary>
    public ExifEdit Exif { get; } = new();
    /// <summary>The changes to IPTC.</summary>
    public IptcEdit Iptc { get; } = new();
    /// <summary>The changes to XMP.</summary>
    public XmpEdit Xmp { get; } = new();
    /// <summary>True when the script changes nothing.</summary>
    public bool IsEmpty => Exif.IsEmpty && Iptc.IsEmpty && Xmp.IsEmpty;

    /// <summary>Reads a script: the text of a command file, or commands with line ends between them.</summary>
    /// <param name="script">The text.</param>
    /// <param name="exifByteOrder">The byte order of the EXIF block the edits are for; it decides the order of the two bytes of a character in a <c>charset=Unicode</c> comment and nothing else.</param>
    /// <returns>The parsed edit script.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is null.</exception>
    /// <exception cref="ModifyScriptException">A line is not a command, names a key that is not one, or gives a value the type cannot hold. The first such line; nothing of the script is to be used.</exception>
    public static ModifyScript Parse(string script, Endianness exifByteOrder = Endianness.LittleEndian)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Parse(script.Split('\n'), exifByteOrder);
    }

    /// <summary>Reads a script given as lines; see <see cref="Parse(string, Endianness)"/>.</summary>
    /// <param name="lines">The command lines in order.</param>
    /// <param name="exifByteOrder">The byte order used to decode Unicode EXIF comments.</param>
    /// <returns>The parsed edit script.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is null.</exception>
    /// <exception cref="ModifyScriptException">As <see cref="Parse(string, Endianness)"/>.</exception>
    public static ModifyScript Parse(IEnumerable<string> lines, Endianness exifByteOrder = Endianness.LittleEndian)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new ModifyScript();
        int number = 0;
        foreach (string raw in lines)
        {
            number++;
            string line = (raw ?? string.Empty).TrimEnd('\r').Trim(Blank);
            if (line.Length == 0 || line[0] == '#') continue;
            try { result.Line(number, line, exifByteOrder); }
            catch (ModifyScriptException) { throw; }
            catch (Exception e) when (e is ArgumentException or MetadataException or FormatException or OverflowException)
            {
                throw new ModifyScriptException(number, e.Message);
            }
        }
        return result;
    }

    // Advance the shared cursor over one whitespace-delimited token without consuming the value tail.
    private static string Word(string line, ref int at)
    {
        while (at < line.Length && line[at] is ' ' or '\t') at++;
        int start = at;
        while (at < line.Length && line[at] is not (' ' or '\t')) at++;
        return line[start..at];
    }

    // Parse command shape and dispatch only after escapes, optional types, and key forms are resolved.
    private void Line(int number, string line, Endianness order)
    {
        int at = 0;
        string word = Word(line, ref at), key = Word(line, ref at);
        ModifyCommandKind? kind = word switch
        {
            "set" => ModifyCommandKind.Set,
            "add" => ModifyCommandKind.Add,
            "del" => ModifyCommandKind.Delete,
            "reg" => ModifyCommandKind.Register,
            _ => null,
        };
        if (kind is not ModifyCommandKind command) throw new ModifyScriptException(number, $"Invalid command `{word}'.");
        if (key.Length == 0) throw new ModifyScriptException(number, "The command has no key.");

        string? type = null;
        string value = string.Empty;
        if (command != ModifyCommandKind.Delete)
        {
            int mark = at;
            string next = Word(line, ref at);
            if (command != ModifyCommandKind.Register && at < line.Length && Array.IndexOf(AllTypes, next) >= 0)
            {
                type = next;
                mark = at;
            }
            value = Escapes(line[mark..].Trim(Blank));
            if (type is not null && value.Length == 0) throw new ModifyScriptException(number, "A type is given and no value.");
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
        }

        if (command == ModifyCommandKind.Register)
        {
            if (value.Length == 0) throw new ModifyScriptException(number, $"Empty value for key `{key}'.");
            Xmp.RegisterNamespace(key, value);
        }
        else if (MetadataKeys.TryParseIptc(key, out IptcDatasetKey dataset)) IptcLine(number, command, dataset, type, value);
        else if (MetadataKeys.TryParseExif(key, out ExifTagKey tag)) ExifLine(number, command, tag, type, value, order);
        else if (ExifEdit.IsMakerNoteKey(key)) MakerNoteLine(number, command, key, type, value);
        else if (XmpPrefix(key) is string prefix && (XmpNamespaces.TryGetUri(prefix, out _) || Xmp.Namespaces.ContainsKey(prefix)))
            XmpLine(number, command, key, type, value);
        else throw new ModifyScriptException(number, $"Invalid key `{key}'.");
        commands.Add(new(number, command, key, type, value));
    }

    /// <summary>Reads <c>\\ \n \r \t \uXXXX</c>; any other backslash stays.</summary>
    private static string Escapes(string text)
    {
        if (text.IndexOf('\\') < 0) return text;
        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\\' || i == text.Length - 1) { result.Append(c); continue; }
            char next = text[i + 1];
            if (next is '\\' or 'n' or 'r' or 't')
            {
                result.Append(next switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => '\\' });
                i++;
            }
            else if (next == 'u' && i + 5 < text.Length && IsHex(text.AsSpan(i + 2, 4)))
            {
                result.Append((char)ushort.Parse(text.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                i += 5;
            }
            else result.Append(c);
        }
        return result.ToString();
    }

    // Accept Unicode escape digits only when all four source characters are ASCII hexadecimal.
    private static bool IsHex(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
            if (!char.IsAsciiHexDigit(c)) return false;
        return true;
    }

    // ---- EXIF --------------------------------------------------------------------------------------------

    // Preserve script semantics by parsing EXIF values and storing them through the unchecked edit path.
    private void ExifLine(int number, ModifyCommandKind command, ExifTagKey key, string? type, string value, Endianness order)
    {
        if (command == ModifyCommandKind.Delete) { Exif.Remove(key); return; }
        if (type is not null && Array.IndexOf(ExifTypes, type) < 0) throw new ModifyScriptException(number, $"`{type}' is not a type of an EXIF value.");
        if (type == "Comment" || (type is null && key == new ExifTagKey(ExifDirectoryKind.Exif, 0x9286)))
        {
            byte[] comment = Comment(number, value, order);
            Exif.SetUnchecked(key, ExifValue.FromRaw(TiffFieldType.Undefined, (uint)comment.Length, Endianness.LittleEndian, comment));
            return;
        }
        TiffFieldType field = type is null
            ? (ExifTags.Find(key) is { Types.IsDefaultOrEmpty: false } definition ? definition.Types[0] : TiffFieldType.Ascii)
            : Enum.Parse<TiffFieldType>(type);
        ExifValue parsed;
        try { parsed = ExifValueText.Parse(field, value); }
        catch (ArgumentException refusal) { throw new ModifyScriptException(number, refusal.Message); }
        Exif.SetUnchecked(key, parsed);
    }

    /// <summary>
    /// A tag of the MakerNote. Without a type the value is read, when the edit is written, in the type
    /// the tag has in the file; with one it is read now. <c>add</c> is <c>set</c>: a tag that is not
    /// in the note is not added, which the write refuses.
    /// </summary>
    private void MakerNoteLine(int number, ModifyCommandKind command, string key, string? type, string value)
    {
        if (command == ModifyCommandKind.Delete) { Exif.RemoveByKey(key); return; }
        if (type is null) { Exif.SetTextByKey(key, value); return; }
        if (Array.IndexOf(ExifTypes, type) < 0 || type == "Comment") throw new ModifyScriptException(number, $"`{type}' is not a type of a MakerNote value.");
        ExifValue parsed;
        try { parsed = ExifValueText.Parse(Enum.Parse<TiffFieldType>(type), value); }
        catch (ArgumentException refusal) { throw new ModifyScriptException(number, refusal.Message); }
        Exif.SetByKey(key, parsed);
    }

    /// <summary>The bytes of a comment: the eight bytes of the character code and the text. Without a marker the code is undefined, as with Exiv2.</summary>
    private static byte[] Comment(int number, string value, Endianness order)
    {
        string code = "\0\0\0\0\0\0\0\0";
        byte[] body;
        if (value.StartsWith("charset=", StringComparison.Ordinal))
        {
            int space = value.IndexOf(' ');
            string name = (space < 0 ? value[8..] : value[8..space]).Trim('"');
            value = space < 0 ? string.Empty : value[(space + 1)..];
            code = name switch
            {
                "Ascii" => "ASCII\0\0\0",
                "Unicode" => "UNICODE\0",
                "Undefined" => code,
                _ => throw new ModifyScriptException(number, $"The character code `{name}' is not Ascii, Unicode or Undefined."),
            };
        }
        if (code[0] == 'U') body = new UnicodeEncoding(order == Endianness.BigEndian, false, true).GetBytes(value);
        else if (code[0] == 'A')
        {
            if (value.Any(c => c > '\u007F')) throw new ModifyScriptException(number, "charset=Ascii is given for text that is not ASCII.");
            body = Encoding.ASCII.GetBytes(value);
        }
        else body = Encoding.UTF8.GetBytes(value);
        byte[] bytes = new byte[8 + body.Length];
        Encoding.ASCII.GetBytes(code, bytes);
        body.CopyTo(bytes, 8);
        return bytes;
    }

    // ---- IPTC --------------------------------------------------------------------------------------------

    private void IptcLine(int number, ModifyCommandKind command, IptcDatasetKey key, string? type, string value)
    {
        if (command == ModifyCommandKind.Delete) { Iptc.Remove(key); return; }
        if (type is not null && Array.IndexOf(IptcTypes, type) < 0) throw new ModifyScriptException(number, $"`{type}' is not a type of an IPTC value.");
        type ??= IptcDatasetNames.FormatOf(key) switch
        {
            IptcValueFormat.UInt16 => "Short",
            IptcValueFormat.Date => "Date",
            IptcValueFormat.Time => "Time",
            IptcValueFormat.Binary => "Undefined",
            _ => "String",
        };
        switch (type)
        {
            case "Short":
                if (command == ModifyCommandKind.Add) throw new ModifyScriptException(number, "A Short dataset is set, not added: it does not repeat.");
                Iptc.SetUInt16(key, ushort.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture));
                return;
            case "Undefined":
                throw new ModifyScriptException(number, "A binary dataset is not written from a script.");
            case "Date":
            {
                string digits = value.Length == 10 && value[4] == '-' && value[7] == '-' ? value.Replace("-", string.Empty, StringComparison.Ordinal) : value;
                if (digits.Length != 8 || !digits.All(char.IsAsciiDigit)) throw new ModifyScriptException(number, "A Date is YYYY-MM-DD.");
                value = digits;
                break;
            }
            case "Time":
            {
                // HH:MM:SS, HH:MM:SS±HH:MM, or the dataset's own HHMMSS±HHMM.
                string compact = value.Replace(":", string.Empty, StringComparison.Ordinal);
                if (compact.Length == 6) compact += "+0000";
                if (compact.Length != 11 || compact[6] is not ('+' or '-') || !compact[..6].All(char.IsAsciiDigit) || !compact[7..].All(char.IsAsciiDigit))
                    throw new ModifyScriptException(number, "A Time is HH:MM:SS with an optional +HH:MM or -HH:MM.");
                value = compact;
                break;
            }
        }
        if (command == ModifyCommandKind.Set) Iptc.SetFirst(key, value);
        // A dataset that does not repeat is added as it is set: there is one place for it.
        else if (IptcDatasets.TryGet(key, out IptcDatasetDefinition? definition) && !definition.IsRepeatable) Iptc.SetFirst(key, value);
        else Iptc.Append(key, value);
    }

    // ---- XMP ---------------------------------------------------------------------------------------------

    /// <summary>The prefix of <c>Xmp.prefix.name</c>; null for a key of another shape.</summary>
    private static string? XmpPrefix(string key)
    {
        if (!key.StartsWith("Xmp.", StringComparison.Ordinal)) return null;
        int dot = key.IndexOf('.', 4);
        return dot > 4 && dot < key.Length - 1 ? key[4..dot] : null;
    }

    // Choose known array/value forms from sync mappings before falling back to Dublin Core conventions.
    private static string XmpDefault(string key)
    {
        foreach (IptcXmpMapping mapping in MetadataSync.IptcMappings)
            if (mapping.XmpPath == key) return Name(mapping.XmpForm);
        foreach (ExifXmpMapping mapping in MetadataSync.ExifMappings)
            if (mapping.XmpPath == key) return Name(mapping.XmpForm);
        return key switch
        {
            "Xmp.dc.subject" or "Xmp.dc.type" or "Xmp.dc.language" or "Xmp.dc.publisher" or "Xmp.dc.contributor" or "Xmp.dc.relation" => "XmpBag",
            "Xmp.dc.creator" or "Xmp.dc.date" => "XmpSeq",
            "Xmp.dc.title" or "Xmp.dc.description" or "Xmp.dc.rights" => "LangAlt",
            _ => "XmpText",
        };

        static string Name(XmpValueKind form) => form switch
        {
            XmpValueKind.Bag => "XmpBag",
            XmpValueKind.Sequence => "XmpSeq",
            XmpValueKind.Alternative => "XmpAlt",
            XmpValueKind.LanguageAlternative => "LangAlt",
            _ => "XmpText",
        };
    }

    // Translate script array and language-alternative commands into the corresponding ordered XMP operations.
    private void XmpLine(int number, ModifyCommandKind command, string key, string? type, string value)
    {
        if (command == ModifyCommandKind.Delete) { Xmp.RemovePath(key); return; }
        if (type is not null && Array.IndexOf(XmpTypes, type) < 0) throw new ModifyScriptException(number, $"`{type}' is not a type of an XMP value.");
        switch (type ?? XmpDefault(key))
        {
            case "XmpText":
                Xmp.Set(key, value);
                break;
            case "LangAlt":
            {
                string language = "x-default";
                if (value.StartsWith("lang=", StringComparison.Ordinal))
                {
                    int space = value.IndexOf(' ');
                    language = (space < 0 ? value[5..] : value[5..space]).Trim('"');
                    value = space < 0 ? string.Empty : value[(space + 1)..];
                }
                Xmp.SetLanguage(key, language, value);
                break;
            }
            case string array:
            {
                XmpValueKind form = array == "XmpBag" ? XmpValueKind.Bag : array == "XmpSeq" ? XmpValueKind.Sequence : XmpValueKind.Alternative;
                // An empty value makes an array without items, which is how a script starts an array of structures.
                if (value.Length == 0) Xmp.SetValue(key, XmpValue.CreateArray(form, []));
                else Xmp.Append(key, form, value);
                break;
            }
        }
    }
}
