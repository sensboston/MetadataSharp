using MetadataSharp.Core.Sync;

namespace MetadataSharp.Core.Xmp;

/// <summary>
/// What the library makes of an XMP value, in the terms of the EXIF tag the property converts to.
/// </summary>
/// <remarks>
/// <para>
/// The reference tool prints a property it knows an EXIF counterpart for with that tag's own reading,
/// after converting the text: <c>Xmp.exif.FNumber</c> holding <c>5/1</c> reads <c>F5</c>, and
/// <c>Xmp.tiff.Orientation</c> holding <c>1</c> reads <c>top, left</c>. The conversion is the one
/// <see cref="MetadataSync.XmpToExif"/> makes, and the reading is what
/// <see cref="ExifTags.Interpret"/> makes of the tag the value lands in.
/// </para>
/// <para>
/// A property with no counterpart in EXIF, a structure, an array of structures, and a value the
/// conversion does not take give no reading: there is nothing to compare against, and the value is
/// the packet's text.
/// </para>
/// </remarks>
public static class XmpTags
{
    // One table for the whole conversion, exactly as the sync direction uses it: where two EXIF tags
    // name one property, the table's last row is the one the conversion reads and writes.
    private static readonly Dictionary<string, ExifXmpMapping> ByPath = Build();

    private static Dictionary<string, ExifXmpMapping> Build()
    {
        Dictionary<string, ExifXmpMapping> table = new(StringComparer.Ordinal);
        foreach (ExifXmpMapping mapping in MetadataSync.ExifMappings) table[mapping.XmpPath] = mapping;
        return table;
    }

    /// <summary>What the library makes of one entry, or null when it makes nothing of it.</summary>
    /// <param name="entry">A node of a packet's flattened view.</param>
    /// <returns>The reading, as the reference tool prints it, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public static string? Interpret(XmpEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // A structure and an array whose items are not plain text hold no value of their own.
        if (entry.HasChildEntries) return null;
        // A field of a structure, an item of one and a qualifier are not properties of their own.
        if (entry.Path.Contains('/', StringComparison.Ordinal) || entry.Path.Contains('[', StringComparison.Ordinal))
            return null;
        if (!ByPath.TryGetValue("Xmp." + entry.Prefix + "." + entry.Path, out ExifXmpMapping? mapping)) return null;
        string[] items = Items(entry);
        if (items.Length == 0) return null;
        var edit = new ExifEdit();
        try
        {
            SyncExif.Write(edit, mapping, items, existing: null);
        }
        catch (ArgumentException)
        {
            return null; // The text is not a value the EXIF tag can hold; the packet's text is all there is.
        }
        catch (MetadataException)
        {
            return null; // The tag is not one an edit may place.
        }
        if (!edit.Snapshot().Changes.TryGetValue(mapping.Tag, out ExifValue? value) || value is null) return null;
        return ExifTags.Interpret(mapping.Tag, value);
    }

    // The texts that convert: a language alternative converts its default text, and an array of text
    // converts its items. The conversion takes the first text or the whole run by its own definition.
    private static string[] Items(XmpEntry entry)
    {
        if (entry.Kind is XmpEntryKind.LangAlt)
        {
            foreach ((string language, string value) in entry.Languages)
                if (language == "x-default") return [value];
            return [];
        }
        return entry.Values.ToArray();
    }
}
