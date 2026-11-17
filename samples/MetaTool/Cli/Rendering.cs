using System.Globalization;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Operations;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;

namespace MetaTool.Cli;

/// <summary>One line of a list print: what one tag, dataset or property holds, in every column <c>-P</c> can ask for.</summary>
internal sealed class MetadataRow
{
    /// <summary>The numeric tag or dataset identifier, or -1 for a property that has none.</summary>
    internal int Tag { get; init; } = -1;
    /// <summary>The group, <c>Image</c>, <c>CanonCs</c> or <c>Application2</c>.</summary>
    internal string Group { get; init; } = string.Empty;
    /// <summary>The key, <c>Exif.Image.Make</c>.</summary>
    internal string Key { get; init; } = string.Empty;
    /// <summary>The name of the tag in the tag table.</summary>
    internal string Name { get; init; } = string.Empty;
    /// <summary>The label of the tag table.</summary>
    internal string Label { get; init; } = string.Empty;
    /// <summary>The description of the tag table.</summary>
    internal string Description { get; init; } = string.Empty;
    /// <summary>The type, <c>Ascii</c> or <c>LangAlt</c>.</summary>
    internal string Type { get; init; } = string.Empty;
    /// <summary>The number of values.</summary>
    internal long Count { get; init; }
    /// <summary>The size of the value in bytes.</summary>
    internal int Size { get; init; }
    /// <summary>The value as stored, as text.</summary>
    internal string Value { get; init; } = string.Empty;
    /// <summary>The value as the tag table reads it, or null when it reads nothing.</summary>
    internal string? Translated { get; init; }
    /// <summary>The value bytes, for the hexadecimal column.</summary>
    internal byte[]? Raw { get; init; }
}

/// <summary>Turns a metadata snapshot into the rows of a list print.</summary>
internal static class Rows
{
    /// <summary>Every EXIF tag, with the MakerNote tags the decoder recognises after them.</summary>
    /// <param name="exif">The EXIF block.</param>
    /// <returns>The rows, in the order the tags are stored.</returns>
    internal static IEnumerable<MetadataRow> Exif(ExifMetadata exif)
    {
        foreach (ExifEntry entry in exif.Entries) yield return Standard(exif, entry);
        if (exif.MakerNote is not { } note) yield break;
        foreach (MakerNoteEntry entry in note.Entries)
        {
            string key = note.GetKey(entry);
            string group = GroupOf(key);
            string name = entry.Name is { Length: > 0 } known ? known : Number(entry.TagId);
            (string label, string description) = MakerNoteText(group, entry.TagId);
            yield return new MetadataRow
            {
                Tag = entry.TagId,
                Group = group,
                Key = key,
                Name = name,
                Label = label,
                Description = description,
                Type = TypeName(entry.Value),
                Count = entry.Value.Count,
                Size = entry.Value.ByteLength,
                Value = SampleSupport.RawText(entry.Value),
                Translated = note.InterpretForImage(entry, exif) ?? note.Interpret(entry),
                Raw = Bytes(entry.Value),
            };
        }
    }

    /// <summary>Every IPTC dataset, in the order it is stored.</summary>
    /// <param name="iptc">The IPTC stream.</param>
    /// <returns>The rows.</returns>
    internal static IEnumerable<MetadataRow> Iptc(IptcPacket iptc)
    {
        for (int i = 0; i < iptc.Datasets.Count; i++)
        {
            IptcDataset dataset = iptc.Datasets[i];
            string key = MetadataKeys.Format(dataset.Key) ?? string.Join('.', "Iptc", dataset.Key.Record.ToString(CultureInfo.InvariantCulture), dataset.Key.Dataset.ToString(CultureInfo.InvariantCulture));
            string? name = IptcDatasetNames.TryGetName(dataset.Key, out string? known) ? known : null;
            string text = iptc.GetText(i);
            yield return new MetadataRow
            {
                Tag = dataset.Key.Dataset,
                Group = GroupOf(key),
                Key = key,
                Name = name ?? Number(dataset.Key.Dataset),
                Type = IptcTypeName(IptcDatasetNames.FormatOf(dataset.Key)),
                Count = dataset.Value.ByteLength,
                Size = dataset.Value.ByteLength,
                Value = text,
                Translated = IptcDatasetNames.Interpret(dataset.Key, dataset.Value) ?? text,
                Raw = Bytes(dataset.Value),
            };
        }
    }

    /// <summary>Every XMP property, flattened as the packet lists it.</summary>
    /// <param name="xmp">The packet.</param>
    /// <returns>The rows.</returns>
    internal static IEnumerable<MetadataRow> Xmp(XmpPacket xmp)
    {
        foreach (XmpEntry entry in xmp.Entries)
        {
            string value = string.Join("; ", entry.Values);
            (string label, string description) = XmpText(entry);
            yield return new MetadataRow
            {
                Group = entry.Prefix,
                Key = entry.Key,
                Name = entry.Path,
                Label = label,
                Description = description,
                Type = XmpTypeName(entry.Kind),
                Count = entry.Values.Count == 0 ? 1 : entry.Values.Count,
                Size = Encoding.UTF8.GetByteCount(value),
                Value = value,
                Translated = XmpTags.Interpret(entry, xmp) ?? value,
                Raw = entry.Values.Count == 0 ? null : Encoding.UTF8.GetBytes(value),
            };
        }
    }

    /// <summary>The group of a dotted key: the second part, or nothing when the key has fewer parts.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The group name.</returns>
    internal static string GroupOf(string key)
    {
        string[] parts = key.Split('.');
        return parts.Length > 2 ? parts[1] : string.Empty;
    }

    /// <summary>The type name of a value, in the spelling the reference prints.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The type name, or the hexadecimal field type for a type the library keeps as unknown.</returns>
    internal static string TypeName(ExifValue value) => value.FieldType switch
    {
        TiffFieldType.Byte => "Byte",
        TiffFieldType.Ascii => "Ascii",
        TiffFieldType.Short => "Short",
        TiffFieldType.Long => "Long",
        TiffFieldType.Rational => "Rational",
        TiffFieldType.SByte => "SByte",
        TiffFieldType.Undefined => "Undefined",
        TiffFieldType.SShort => "SShort",
        TiffFieldType.SLong => "SLong",
        TiffFieldType.SRational => "SRational",
        TiffFieldType.Float => "Float",
        TiffFieldType.Double => "Double",
        _ => "0x" + value.FieldTypeCode.ToString("x4", CultureInfo.InvariantCulture),
    };

    private static MetadataRow Standard(ExifMetadata exif, ExifEntry entry)
    {
        string key = MetadataKeys.Format(entry.Key);
        string group = GroupOf(key);
        string name = ExifTags.Find(entry.Key)?.Name ?? Number(entry.Key.TagId);
        (string label, string description) = ExifText(group, entry.Key.TagId);
        return new MetadataRow
        {
            Tag = entry.Key.TagId,
            Group = group,
            Key = key,
            Name = name,
            Label = label,
            Description = description,
            Type = TypeName(entry.Value),
            Count = entry.Value.Count,
            Size = entry.Value.ByteLength,
            Value = SampleSupport.RawText(entry.Value),
            Translated = ExifTags.Interpret(exif, entry.Key),
            Raw = Bytes(entry.Value),
        };
    }

    private static (string Label, string Description) ExifText(string group, ushort tag)
    {
        if (group.Length != 0 && MetadataTagCatalog.TryGet(group, tag, out NumberedTagDefinition? definition))
            return (definition!.Label, definition.Description);
        return (string.Empty, string.Empty);
    }

    private static (string Label, string Description) MakerNoteText(string group, ushort tag) => ExifText(group, tag);

    private static (string Label, string Description) XmpText(XmpEntry entry)
    {
        string name = entry.Path;
        int slash = name.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0) name = name[..slash];
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0) name = name[(colon + 1)..];
        if (entry.Prefix.Length != 0 && name.Length != 0 &&
            MetadataTagCatalog.TryGetXmp(entry.Prefix, name, out XmpTagDefinition? definition))
            return (definition!.Label, definition.Description);
        return (string.Empty, string.Empty);
    }

    private static string Number(int id) => "0x" + id.ToString("x4", CultureInfo.InvariantCulture);

    private static string IptcTypeName(IptcValueFormat format) => format switch
    {
        IptcValueFormat.UInt16 => "Short",
        IptcValueFormat.Date => "Date",
        IptcValueFormat.Time => "Time",
        IptcValueFormat.Binary => "Undefined",
        _ => "String",
    };

    private static string XmpTypeName(XmpEntryKind kind) => kind switch
    {
        XmpEntryKind.Bag => "XmpBag",
        XmpEntryKind.Seq => "XmpSeq",
        XmpEntryKind.Alt => "XmpAlt",
        XmpEntryKind.LangAlt => "LangAlt",
        XmpEntryKind.Struct => "XmpStruct",
        _ => "XmpText",
    };

    private static byte[]? Bytes(ExifValue value)
    {
        if (value.ByteLength == 0) return null;
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return bytes;
    }

    private static byte[]? Bytes(IptcValue value)
    {
        if (value.ByteLength == 0) return null;
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return bytes;
    }
}

/// <summary>Writes the columns of one row, in the widths and the spacing the reference uses.</summary>
internal static class RowWriter
{
    private const int GroupWidth = 12, KeyWidth = 44, NameWidth = 27, LabelWidth = 30, DescriptionWidth = 30, TypeWidth = 9, NumberWidth = 3;

    /// <summary>Writes one row.</summary>
    /// <param name="output">Where the row goes.</param>
    /// <param name="row">The row.</param>
    /// <param name="columns">The columns to write, in the fixed order of the reference.</param>
    /// <param name="filePrefix">The name of the file, when more than one file is printed.</param>
    internal static void Write(TextWriter output, MetadataRow row, PrintColumns columns, string? filePrefix)
    {
        if (filePrefix is not null) output.Write(Pad(filePrefix, 20) + "  ");
        bool first = true;
        if (columns.HasFlag(PrintColumns.Set)) { output.Write("set"); first = false; }
        if (columns.HasFlag(PrintColumns.Tag))
        {
            if (!first) output.Write(' ');
            first = false;
            output.Write("0x" + row.Tag.ToString("x4", CultureInfo.InvariantCulture));
        }
        if (columns.HasFlag(PrintColumns.Group)) { if (!first) output.Write(' '); first = false; output.Write(Pad(row.Group, GroupWidth)); }
        if (columns.HasFlag(PrintColumns.Key)) { if (!first) output.Write(' '); first = false; output.Write(Pad(row.Key, KeyWidth)); }
        if (columns.HasFlag(PrintColumns.Name)) { if (!first) output.Write(' '); first = false; output.Write(Pad(row.Name, NameWidth)); }
        if (columns.HasFlag(PrintColumns.Label)) { if (!first) output.Write(' '); first = false; output.Write(Pad(row.Label, LabelWidth)); }
        if (columns.HasFlag(PrintColumns.Description)) { if (!first) output.Write(' '); first = false; output.Write(Pad(row.Description, DescriptionWidth)); }
        if (columns.HasFlag(PrintColumns.Type)) { if (!first) output.Write(' '); first = false; output.Write(Pad(row.Type, TypeWidth)); }
        if (columns.HasFlag(PrintColumns.Count)) { if (!first) output.Write(' '); first = false; output.Write(PadLeft(row.Count.ToString(CultureInfo.InvariantCulture), NumberWidth)); }
        if (columns.HasFlag(PrintColumns.Size)) { if (!first) output.Write(' '); first = false; output.Write(PadLeft(row.Size.ToString(CultureInfo.InvariantCulture), NumberWidth)); }
        if (columns.HasFlag(PrintColumns.Value) && row.Size > 0)
        {
            if (!first) output.Write("  ");
            first = false;
            output.Write(row.Value);
        }
        if (columns.HasFlag(PrintColumns.Translated))
        {
            if (!first) output.Write("  ");
            first = false;
            output.Write(row.Translated ?? string.Empty);
        }
        if (columns.HasFlag(PrintColumns.Hex))
        {
            if (!first) output.Write('\n');
            HexDump.Write(output, row.Raw);
        }
        output.Write('\n');
    }

    /// <summary>Pads a text on the right to a width, leaving text that is longer alone, as the reference does.</summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The width.</param>
    /// <returns>The text, padded when it is shorter.</returns>
    internal static string Pad(string text, int width) => text.Length >= width ? text : text + new string(' ', width - text.Length);

    /// <summary>Pads a text on the left to a width, leaving text that is longer alone.</summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The width.</param>
    /// <returns>The text, padded when it is shorter.</returns>
    internal static string PadLeft(string text, int width) => text.Length >= width ? text : new string(' ', width - text.Length) + text;
}

/// <summary>The hexadecimal dump of a value, in the layout of the reference: offset, bytes, then text.</summary>
internal static class HexDump
{
    /// <summary>Writes the dump, one 16-byte line at a time.</summary>
    /// <param name="output">Where the dump goes.</param>
    /// <param name="bytes">The bytes, or null when there are none.</param>
    internal static void Write(TextWriter output, ReadOnlySpan<byte> bytes)
    {
        for (int start = 0; start < bytes.Length; start += 16)
        {
            int count = Math.Min(16, bytes.Length - start);
            output.Write("  " + start.ToString("x4", CultureInfo.InvariantCulture) + "  ");
            for (int i = 0; i < count; i++)
                output.Write(bytes[start + i].ToString("x2", CultureInfo.InvariantCulture) + " ");
            // The text column starts at the same place on every line, as it does in the reference.
            output.Write(new string(' ', 49 - 3 * count));
            for (int i = 0; i < count; i++)
            {
                byte value = bytes[start + i];
                output.Write(value is >= 31 and < 127 ? (char)value : '.');
            }
            output.Write('\n');
        }
    }
}
