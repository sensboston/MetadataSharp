using System.Buffers.Binary;
using System.Globalization;
using MetadataSharp.Core.Sync;

namespace MetadataSharp.Core.Xmp;

/// <summary>
/// What the library makes of an XMP value: the reading the reference tool prints beside the text.
/// </summary>
/// <remarks>
/// <para>
/// The reference prints a property it knows a reading for with the print function of the EXIF tag
/// the property converts to, applied to the property's text: <c>Xmp.exif.FNumber</c> holding
/// <c>5/1</c> reads <c>F5</c>, <c>Xmp.tiff.Orientation</c> holding <c>1</c> reads <c>top, left</c>.
/// The set of properties is the reference's own (<c>xmpPrintInfo</c> in <c>src/properties.cpp</c>),
/// and a property outside it has no reading: the value is the packet's text. So a property with no
/// EXIF counterpart, a structure, an array of structures and a code the reference's table does not
/// name all read as nothing or as the bare number in parentheses, as they do there.
/// </para>
/// <para>
/// The IPTC and PLUS vocabularies the reference reads a value through are published standards, so
/// this library carries their terms and prints the name the reference prints:
/// <c>Xmp.iptc.Scene</c> holding <c>011900</c> reads <c>Action</c>, <c>Xmp.plus.ModelReleaseStatus</c>
/// holding <c>http://ns.useplus.org/ldf/vocab/MR-NAP</c> reads <c>Not Applicable</c>. The lists and
/// their licences are in <see cref="XmpVocabularies"/>; a term no list names reads as the reference
/// reads it, the bare value in parentheses.
/// </para>
/// <para>
/// A time in <c>Xmp.exif.GPSTimeStamp</c> without a date is completed from the packet's own
/// <c>exif:DateTimeOriginal</c>, or <c>exif:DateTimeDigitized</c>, before it is read, exactly as the
/// XMP toolkit the reference parses packets with does (<c>FixGPSTimeStamp</c> in its
/// <c>XMPMeta-Parse.cpp</c>): the reading is a date and a time, while the property's text stays the
/// time the packet holds.
/// </para>
/// </remarks>
public static class XmpTags
{
    private enum Reading { Label, Integer, Decimal, Date, Version, Components }

    // The properties the reference prints a reading for, and the print function each is read with.
    private static readonly Dictionary<string, Reading> Forms = new(StringComparer.Ordinal)
    {
        // printInt64: an integer, or the text in parentheses when it names none.
        ["Xmp.tiff.XResolution"] = Reading.Integer,
        ["Xmp.tiff.YResolution"] = Reading.Integer,
        // printFloat
        ["Xmp.exif.BrightnessValue"] = Reading.Decimal,
        ["Xmp.exif.FocalPlaneXResolution"] = Reading.Decimal,
        ["Xmp.exif.FocalPlaneYResolution"] = Reading.Decimal,
        // printXmpDate
        ["Xmp.exif.CreateDate"] = Reading.Date,
        ["Xmp.exif.DateTimeOriginal"] = Reading.Date,
        ["Xmp.exif.GPSTimeStamp"] = Reading.Date,
        ["Xmp.exif.ModifyDate"] = Reading.Date,
        // printXmpVersion
        ["Xmp.exif.ExifVersion"] = Reading.Version,
        ["Xmp.exif.FlashpixVersion"] = Reading.Version,
        // print0x9101: the components a colour model is built from, one letter each.
        ["Xmp.exif.ComponentsConfiguration"] = Reading.Components,
        // The rest of the reference's table is an enumerated tag: a code and its name.
        ["Xmp.exif.ApertureValue"] = Reading.Label,
        ["Xmp.exif.ColorSpace"] = Reading.Label,
        ["Xmp.exif.Contrast"] = Reading.Label,
        ["Xmp.exif.CustomRendered"] = Reading.Label,
        ["Xmp.exif.ExposureBiasValue"] = Reading.Label,
        ["Xmp.exif.ExposureMode"] = Reading.Label,
        ["Xmp.exif.ExposureProgram"] = Reading.Label,
        ["Xmp.exif.FileSource"] = Reading.Label,
        ["Xmp.exif.FNumber"] = Reading.Label,
        ["Xmp.exif.FocalLength"] = Reading.Label,
        ["Xmp.exif.FocalPlaneResolutionUnit"] = Reading.Label,
        ["Xmp.exif.GainControl"] = Reading.Label,
        ["Xmp.exif.GPSAltitudeRef"] = Reading.Label,
        ["Xmp.exif.GPSDestBearingRef"] = Reading.Label,
        ["Xmp.exif.GPSDestDistanceRef"] = Reading.Label,
        ["Xmp.exif.GPSDifferential"] = Reading.Label,
        ["Xmp.exif.GPSImgDirectionRef"] = Reading.Label,
        ["Xmp.exif.GPSMeasureMode"] = Reading.Label,
        ["Xmp.exif.GPSSpeedRef"] = Reading.Label,
        ["Xmp.exif.GPSStatus"] = Reading.Label,
        ["Xmp.exif.GPSTrackRef"] = Reading.Label,
        ["Xmp.exif.LightSource"] = Reading.Label,
        ["Xmp.exif.MeteringMode"] = Reading.Label,
        ["Xmp.exif.Saturation"] = Reading.Label,
        ["Xmp.exif.SceneCaptureType"] = Reading.Label,
        ["Xmp.exif.SceneType"] = Reading.Label,
        ["Xmp.exif.SensingMethod"] = Reading.Label,
        ["Xmp.exif.Sharpness"] = Reading.Label,
        ["Xmp.exif.ShutterSpeedValue"] = Reading.Label,
        ["Xmp.exif.SubjectDistanceRange"] = Reading.Label,
        ["Xmp.exif.WhiteBalance"] = Reading.Label,
        ["Xmp.tiff.Orientation"] = Reading.Label,
        ["Xmp.tiff.ResolutionUnit"] = Reading.Label,
        ["Xmp.tiff.YCbCrPositioning"] = Reading.Label,
    };

    // The direction references are the letters themselves in the reference's table, so a text that is
    // no number reads as (0), exactly as it does there.
    private static readonly Dictionary<long, string> Directions = new() { [84] = "True direction", [77] = "Magnetic direction" };

    // The codes the reference's own tables name, where a name differs from the publication's or the
    // table itself does: its reading is the one that counts here, and a code it does not name reads
    // as the bare number in parentheses.
    private static readonly Dictionary<string, Dictionary<long, string>> Tables = new(StringComparer.Ordinal)
    {
        ["Xmp.exif.ColorSpace"] = new() { [1] = "sRGB", [2] = "Adobe RGB", [0xffff] = "Uncalibrated" },
        ["Xmp.exif.FileSource"] = new() { [1] = "Film scanner", [2] = "Reflexion print scanner", [3] = "Digital still camera" },
        ["Xmp.exif.GPSAltitudeRef"] = new() { [0] = "Above sea level", [1] = "Below sea level" },
        ["Xmp.exif.MeteringMode"] = new()
        {
            [0] = "Unknown", [1] = "Average", [2] = "Center weighted average", [3] = "Spot",
            [4] = "Multi-spot", [5] = "Multi-segment", [6] = "Partial", [255] = "Other",
        },
        ["Xmp.exif.SensingMethod"] = new()
        {
            [1] = "Not defined", [2] = "One-chip color area", [3] = "Two-chip color area",
            [4] = "Three-chip color area", [5] = "Color sequential area", [7] = "Trilinear sensor",
            [8] = "Color sequential linear",
        },
        ["Xmp.exif.GPSDestBearingRef"] = Directions,
        ["Xmp.exif.GPSImgDirectionRef"] = Directions,
        ["Xmp.exif.GPSTrackRef"] = Directions,
        ["Xmp.exif.GPSDestDistanceRef"] = new() { [75] = "km", [77] = "miles", [78] = "nautical miles" },
        ["Xmp.exif.GPSSpeedRef"] = new() { [75] = "km/h", [77] = "mph", [78] = "knots" },
        ["Xmp.exif.GPSStatus"] = new() { [65] = "Measurement in progress", [86] = "Measurement interrupted" },
    };

    private static readonly Dictionary<string, ExifXmpMapping> ByPath = BuildByPath();

    // One table for the whole conversion, exactly as the sync direction uses it: where two EXIF tags
    // name one property, the table's last row is the one the conversion reads and writes.
    private static Dictionary<string, ExifXmpMapping> BuildByPath()
    {
        Dictionary<string, ExifXmpMapping> table = new(StringComparer.Ordinal);
        foreach (ExifXmpMapping mapping in MetadataSync.ExifMappings) table[mapping.XmpPath] = mapping;
        return table;
    }

    /// <summary>What the library makes of one entry, or null when it makes nothing of it.</summary>
    /// <param name="entry">A node of a packet's flattened view.</param>
    /// <param name="packet">
    /// The packet <paramref name="entry"/> came from, when there is one: a reading may be completed
    /// from another property of the same packet.
    /// </param>
    /// <returns>The reading the reference prints beside the text, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public static string? Interpret(XmpEntry entry, XmpPacket? packet = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // A structure and an array whose items are not plain text hold no value of their own.
        if (entry.HasChildEntries) return null;
        // A field of a structure, an item of one and a qualifier are not properties of their own.
        if (entry.Path.Contains("/", StringComparison.Ordinal) || entry.Path.Contains("[", StringComparison.Ordinal))
            return null;
        string key = "Xmp." + entry.Prefix + "." + entry.Path;
        // A property the reference reads through a published code list is read through it.
        if (XmpVocabularies.ByProperty.TryGetValue(key, out XmpVocabularies.CodeList? list))
            return list.Many ? VocabularyList(list, entry) : Vocabulary(list, Text(entry));
        if (!Forms.TryGetValue(key, out Reading form)) return null;
        if (form is Reading.Components) return Components(entry);
        string? text = Text(entry);
        if (text is null) return null;
        switch (form)
        {
            case Reading.Integer:
                return Integer(text);
            case Reading.Decimal:
                return Decimal(text);
            case Reading.Date:
                return Date(key, text, packet);
            case Reading.Version:
                return Version(text);
            default:
                return Label(key, text, entry);
        }
    }

    // The text a value reads as: the default text of a language alternative, the first item of an
    // array, or the value itself.
    private static string? Text(XmpEntry entry)
    {
        if (entry.Kind is XmpEntryKind.LangAlt)
        {
            foreach ((string language, string value) in entry.Languages)
                if (language == "x-default") return value;
            return null;
        }
        return entry.Values.Count == 0 ? null : entry.Values[0];
    }

    // printInt64: the value of a ratio, or the text in parentheses when it names no ratio.
    private static string Integer(string text) =>
        TryRatio(text, out long numerator, out long denominator)
            ? (denominator > 0 ? (numerator / denominator).ToString(CultureInfo.InvariantCulture) : "(" + text + ")")
            : "(" + text + ")";

    // printFloat: the value of a ratio with the six significant digits an output stream gives it.
    private static string Decimal(string text) =>
        TryRatio(text, out long numerator, out long denominator) && denominator != 0
            ? ((double)numerator / denominator).ToString("G6", CultureInfo.InvariantCulture)
            : "(" + text + ")";

    // printXmpDate: a date to the second, in the EXIF spelling; anything else is printed as it is.
    // A GPS time without a date is first completed from the packet's own date, as the toolkit the
    // reference parses with does, so "14:58:24Z" beside a DateTimeOriginal reads as that day's time.
    private static string Date(string key, string text, XmpPacket? packet)
    {
        if (key == "Xmp.exif.GPSTimeStamp") text = CompleteGpsTimestamp(text, packet);
        if (text.Length is not (19 or 20)) return text;
        if (text.Length == 20)
        {
            if (text[19] != 'Z') return text;
            text = text[..19];
        }
        return text.Replace('T', ' ').Replace('-', ':');
    }

    // FixGPSTimeStamp: a time that names no date takes the day of the packet's DateTimeOriginal, or
    // DateTimeDigitized, and is written back as that day and the time as it stands. A time that
    // already names a date, and a packet that gives no date to take, are left alone.
    private static string CompleteGpsTimestamp(string text, XmpPacket? packet)
    {
        if (packet is null || HasDate(text)) return text;
        string? original = PacketText(packet, "exif", "DateTimeOriginal") ?? PacketText(packet, "exif", "DateTimeDigitized");
        if (original is null || !HasDate(original)) return text;
        return original[..10] + "T" + text;
    }

    // A date as ISO 8601 writes one: four digits, a month and a day, in the value's first ten
    // characters. A time alone, and a year alone, name no day.
    private static bool HasDate(string text) =>
        text.Length >= 10 && text[4] == '-' && text[7] == '-' &&
        Digits(text, 0, 4) && Digits(text, 5, 2) && Digits(text, 8, 2);

    private static bool Digits(string text, int at, int count)
    {
        for (int i = at; i < at + count; i++) if (!char.IsAsciiDigit(text[i])) return false;
        return true;
    }

    // The text of a property the packet holds at its top level.
    private static string? PacketText(XmpPacket packet, string prefix, string path)
    {
        foreach (XmpEntry entry in packet.Entries)
            if (entry.Prefix == prefix && entry.Path == path) return Text(entry);
        return null;
    }

    // printTagVocabulary: the name the published code list gives the value, or the value in
    // parentheses when the list names none.
    private static string? Vocabulary(XmpVocabularies.CodeList list, string? text) =>
        text is null ? null : list.Name(text) ?? "(" + text + ")";

    // printTagVocabularyMulti: the same, for every item of the array, joined by ", ".
    private static string VocabularyList(XmpVocabularies.CodeList list, XmpEntry entry)
    {
        if (entry.Values.Count == 0) return "()";
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < entry.Values.Count; i++)
        {
            if (i != 0) text.Append(", ");
            text.Append(list.Name(entry.Values[i]) ?? "(" + entry.Values[i] + ")");
        }
        return text.ToString();
    }

    // printXmpVersion: four characters, the leading zero dropped when there is one.
    private static string Version(string text)
    {
        if (text.Length != 4) return "(" + text + ")";
        return (text[0] == '0' ? string.Empty : text[..1]) + text[1..2] + "." + text[2..];
    }

    // print0x9101: a letter for the colour model each component code names, one per code the value
    // holds. A property that is one text is one code - the number the text begins with - so the run
    // "1, 2, 3, 0" read as one text is "Y" and read as four items is "YCbCr", as the reference has it.
    // A component with no letter is printed as the bare number in parentheses.
    private static string Components(XmpEntry entry)
    {
        if (entry.Kind is XmpEntryKind.Text) return Component(Text(entry) ?? string.Empty);
        var text = new System.Text.StringBuilder();
        foreach (string item in entry.Values) text.Append(Component(item));
        return text.ToString();
    }

    private static string Component(string code) => ToInt64(code) switch
    {
        0 => string.Empty,
        1 => "Y",
        2 => "Cb",
        3 => "Cr",
        4 => "R",
        5 => "G",
        6 => "B",
        long other => Bracket(other)
    };

    // An enumerated tag: the reference's own table where it has one, else the tag's own reading of the
    // value the text converts to, and the bare number in parentheses where neither names the code.
    private static string Label(string key, string text, XmpEntry entry)
    {
        long code = ToInt64(text);
        if (Tables.TryGetValue(key, out Dictionary<long, string>? table))
            return table.TryGetValue(code, out string? named) ? named : Bracket(code);
        if (!ByPath.TryGetValue(key, out ExifXmpMapping? mapping)) return Bracket(code);
        string? reading = Converted(mapping, entry);
        reading ??= ExifTags.Interpret(mapping.Tag, Number(mapping, code));
        return reading ?? Bracket(code);
    }

    private static string Bracket(long code) => "(" + code.ToString(CultureInfo.InvariantCulture) + ")";

    // The value the text converts to, read with the tag's own interpretation: the conversion is the one
    // the sync direction makes, which is what gives a ratio its numerator and denominator back.
    private static string? Converted(ExifXmpMapping mapping, XmpEntry entry)
    {
        string[] items = entry.Kind is XmpEntryKind.LangAlt ? Items(entry) : entry.Values.ToArray();
        if (items.Length == 0) return null;
        var edit = new ExifEdit();
        try
        {
            SyncExif.Write(edit, mapping, items, existing: null);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or MetadataException)
        {
            return null;
        }
        if (!edit.Snapshot().Changes.TryGetValue(mapping.Tag, out ExifValue? value) || value is null) return null;
        return ExifTags.Interpret(mapping.Tag, value);
    }

    // The default text of a language alternative, or the texts of the entry as they stand.
    private static string[] Items(XmpEntry entry)
    {
        foreach ((string language, string value) in entry.Languages)
            if (language == "x-default") return [value];
        return [];
    }

    // The code as the tag holds it, or as a signed long when the tag's own type cannot hold it: the
    // reference reads the number either way.
    private static ExifValue Number(ExifXmpMapping mapping, long code)
    {
        TiffFieldType type = mapping.FieldType;
        byte[] raw;
        if (type is TiffFieldType.Byte or TiffFieldType.Undefined && code is >= 0 and <= 255)
            raw = [(byte)code];
        else if (type is TiffFieldType.Short && code is >= 0 and <= ushort.MaxValue)
        {
            raw = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(raw, (ushort)code);
        }
        else if (type is TiffFieldType.Long && code is >= 0 and <= uint.MaxValue)
        {
            raw = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(raw, (uint)code);
        }
        else if (type is TiffFieldType.SShort && code is >= short.MinValue and <= short.MaxValue)
        {
            raw = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(raw, (short)code);
        }
        else
        {
            type = TiffFieldType.SLong;
            raw = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(raw, code is >= int.MinValue and <= int.MaxValue ? (int)code : 0);
        }
        return ExifValue.FromRaw(type, 1, Endianness.LittleEndian, raw);
    }

    // The number a text begins with, read as the reference reads a text: a sign and the digits that
    // follow, a ratio's whole value, and zero where neither is there.
    private static long ToInt64(string text)
    {
        if (TryRatio(text, out long numerator, out long denominator))
            return denominator == 0 ? 0 : numerator / denominator;
        string trimmed = text.TrimStart();
        int index = trimmed.Length != 0 && trimmed[0] is '-' or '+' ? 1 : 0;
        int start = index;
        while (index < trimmed.Length && char.IsAsciiDigit(trimmed[index])) index++;
        if (index == start) return 0;
        return long.TryParse(trimmed[..index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) ? value : 0;
    }

    // A ratio as "numerator/denominator", and a bare integer as itself over one.
    private static bool TryRatio(string text, out long numerator, out long denominator)
    {
        numerator = 0;
        denominator = 1;
        string trimmed = text.Trim();
        int slash = trimmed.IndexOf('/');
        if (slash < 0)
            return long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out numerator);
        return long.TryParse(trimmed[..slash].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out numerator) &&
            long.TryParse(trimmed[(slash + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out denominator);
    }
}
