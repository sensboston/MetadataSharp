using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Core.Sync;

/// <summary>The XMP side of a conversion: values are read as lists of texts and written in the form of the mapping.</summary>
internal sealed class SyncXmp
{
    internal const string ExifUri = "http://ns.adobe.com/exif/1.0/";
    internal static readonly string[] FlashFields = ["Fired", "Return", "Mode", "Function", "RedEyeMode"];

    private readonly Dictionary<(string Uri, string Path), XmpEntry> entries = [];

    internal SyncXmp(XmpPacket? packet)
    {
        if (packet is null) return;
        foreach (XmpEntry entry in packet.Entries)
        {
            // A field of a structure is found by local names: the prefix of a step is the packet's choice.
            string path = entry.Path;
            int slash = path.IndexOf('/'), colon = path.LastIndexOf(':');
            if (slash > 0 && colon > slash && path.IndexOf('/', slash + 1) < 0 && path.IndexOf('[') < 0 && path[slash + 1] != '?')
                path = string.Concat(path.AsSpan(0, slash + 1), path.AsSpan(colon + 1));
            entries.TryAdd((entry.NamespaceUri, path), entry);
        }
    }

    // Resolve only built-in mapping prefixes; a table error is a programming invariant violation.
    internal static string Uri(string prefix) =>
        XmpNamespaces.TryGetUri(prefix, out string uri) ? uri : throw new InvalidOperationException("A mapping names an unknown prefix.");

    internal bool Has(string uri, string name) => entries.ContainsKey((uri, name));

    internal XmpEntryKind? Kind(string uri, string name) => entries.TryGetValue((uri, name), out XmpEntry? entry) ? entry.Kind : null;

    /// <summary>
    /// The texts of a property: its value, its items, or the default text of a language alternative.
    /// Null when the packet does not have it, it is empty, or it holds something other than texts.
    /// </summary>
    internal string[]? Read(string uri, string name)
    {
        if (!entries.TryGetValue((uri, name), out XmpEntry? entry) || entry.HasChildEntries) return null;
        if (entry.Kind == XmpEntryKind.LangAlt)
        {
            foreach ((string language, string value) in entry.Languages)
                if (string.Equals(language, "x-default", StringComparison.OrdinalIgnoreCase)) return value.Length == 0 ? null : [value];
        }
        if (entry.Kind is XmpEntryKind.LangAlt or XmpEntryKind.Alt)
            return entry.Values.Count == 0 || entry.Values[0].Length == 0 ? null : [entry.Values[0]];
        string[] values = entry.Values.Where(value => value.Length != 0).ToArray();
        return values.Length == 0 ? null : values;
    }

    /// <summary>The five fields of exif:Flash in the order of <see cref="FlashFields"/>; null when the structure has none of them.</summary>
    internal string[]? ReadFlash()
    {
        string[] fields = new string[FlashFields.Length];
        bool any = false;
        for (int i = 0; i < fields.Length; i++)
        {
            string[]? value = Read(ExifUri, "Flash/" + FlashFields[i]);
            fields[i] = value is { Length: 1 } ? value[0] : string.Empty;
            any |= fields[i].Length != 0;
        }
        return any ? fields : null;
    }

    /// <exception cref="ArgumentException">The texts do not fit the form, or a text cannot be XML.</exception>
    // Preserve existing language alternatives but replace a conflicting XMP form as a whole.
    internal void Write(XmpEdit edit, string path, string uri, string name, XmpValueKind form, string[] items)
    {
        switch (form)
        {
            case XmpValueKind.Text:
                if (items.Length != 1) throw new ArgumentException("A simple property takes one value.");
                edit.Set(path, items[0]);
                break;
            case XmpValueKind.LanguageAlternative:
                if (items.Length != 1) throw new ArgumentException("A language alternative takes one default text.");
                // The other languages of an alternative stay; a property of another form is replaced whole.
                if (Kind(uri, name) is null or XmpEntryKind.LangAlt) edit.SetLanguage(path, "x-default", items[0]);
                else edit.SetValue(path, XmpValue.CreateLanguageAlternative(new[] { new KeyValuePair<string, string>("x-default", items[0]) }));
                break;
            default:
                edit.SetValue(path, XmpValue.CreateArray(form, items));
                break;
        }
    }

    // Keep the EXIF Flash structure fields in the standard order expected by the shared mapping.
    internal static void WriteFlash(XmpEdit edit, string[] fields)
    {
        for (int i = 0; i < FlashFields.Length; i++)
            edit.Set("Xmp.exif.Flash/exif:" + FlashFields[i], fields[i]);
    }
}
