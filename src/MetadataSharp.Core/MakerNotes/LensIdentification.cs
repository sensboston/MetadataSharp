using System.Globalization;
using System.Text.RegularExpressions;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>
/// The lens a code field of a vendor note names. One rule per vendor, because the code alone is enough
/// for none of them: Canon and Pentax record a number that several lenses share and decide between them
/// with the focal range, the widest aperture or the settings array of the note; Nikon records the lens
/// as eight bytes of its lens data; Sony and Minolta keep their alternatives in one string.
/// </summary>
/// <remarks>
/// A rule answers only for the value shape it was written for and returns null for anything else, so a
/// tag the note does not hold in that shape keeps the reading it had. A code the table does not hold is
/// answered by the code itself, in the parentheses the other uninterpreted numbers of a note use.
/// </remarks>
internal static partial class LensIdentification
{
    /// <summary>The parts of a Canon lens name that a code alone leaves open: the focal range and the widest aperture at each end.</summary>
    private const string CanonLensName =
        @"(?:([0-9]+)\-)?" +
        @"([0-9]+)mm" +
        @".*?" +
        @"(?:(?:f/)|T|F)" +
        @"(?:([0-9]+(?:\.[0-9]+)?)\-)?" +
        @"([0-9]+(?:\.[0-9])?)" +
        @"(?:.*?\+.*?([0-9.]+)x)?";

    /// <summary>The Pentax codes whose number stands for several lenses and whose fields decide between them.</summary>
    private static readonly ushort[] PentaxResolved = [0x0317, 0x0319, 0x031b, 0x031c, 0x031d, 0x031f, 0x0329, 0x032c, 0x032e, 0x0334, 0x03ff, 0x041a, 0x042d, 0x08ff];

    /// <summary>The Sony and Minolta ids whose label holds several lenses and whose fields decide between them.</summary>
    private static readonly ushort[] SonyResolved = [0x001c, 0x0029, 0x0034, 0x0080, 0x00ff, 0xffff];

    /// <summary>The seven fields of a Nikon lens data array that a lens name is read with, in the order of the bytes.</summary>
    private static readonly string[] NikonLensBytes =
        ["LensIDNumber", "LensFStops", "MinFocalLength", "MaxFocalLength", "MaxApertureAtMinFocal", "MaxApertureAtMaxFocal", "MCUVersion"];

    /// <summary>The bytes of a Nikon lens data array that a Sigma lens may leave free, because they move with its focal length.</summary>
    private const ulong NikonFStopsMask = 0xFFUL << 48;

    /// <summary>
    /// The Canon lens of a camera settings block: the name of the lens whose focal range and widest
    /// aperture match the ones the block records, several of them joined with <c> *OR* </c> when the code
    /// does not decide, the code alone when the block records neither.
    /// </summary>
    /// <param name="code">The value of the LensType field.</param>
    /// <param name="settings">The entry of the camera settings array the field belongs to.</param>
    /// <param name="model">The camera model of the file, if known.</param>
    internal static string? Canon(ExifValue code, MakerNoteEntry settings, string? model)
    {
        if (code.FieldType != TiffFieldType.Short || code.Count == 0 || !ExifTags.TryInteger(code, 0, out long stored) ||
            stored is < 0 or > ushort.MaxValue)
            return null;
        ushort type = (ushort)stored;

        // One camera writes the code of no lens for a lens it knows; its settings identify that one.
        if (type == 0xFFFF && CanonThirtyDSettings(settings, model))
            return "Canon EF-S 24mm f/2.8 STM";

        if (Field(settings, "Lens") is not ExifValue lens || lens.FieldType != TiffFieldType.Short || lens.Count < 3 ||
            !ExifTags.TryInteger(lens, 2, out long units) || units == 0 ||
            !ExifTags.TryInteger(lens, 0, out long longFocal) || !ExifTags.TryInteger(lens, 1, out long shortFocal))
            return type == 0xFFFF ? Label(LensTables.Canon, type) ?? UnknownLens(type) : UnknownLens(type);

        float unit = units;
        int focalShort = (int)(shortFocal / unit);
        int focalLong = (int)(longFocal / unit);

        if (Field(settings, "MaxAperture") is not ExifValue aperture || aperture.Count != 1 ||
            aperture.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(aperture, 0, out long apertureStored))
            return type == 0xFFFF ? Label(LensTables.Canon, type) ?? UnknownLens(type) : UnknownLens(type);

        float widest = Aperture(apertureStored);
        var names = new List<string>();
        foreach ((ushort candidate, string name) in LensTables.Canon)
        {
            if (candidate != type)
                continue;
            Match match = CanonLens().Match(name);
            if (!match.Success)
                continue;
            float teleconverter = match.Groups[5].Length > 0 ? Number(match.Groups[5].Value) : 1f;
            int labelLong = (int)(Number(match.Groups[2].Value) * teleconverter);
            int labelShort = match.Groups[1].Length > 0 ? (int)(Number(match.Groups[1].Value) * teleconverter) : labelLong;
            float labelTele = Number(match.Groups[4].Value) * teleconverter;
            float labelWide = match.Groups[3].Length > 0 ? Number(match.Groups[3].Value) * teleconverter : labelTele;
            if (labelShort != focalShort || labelLong != focalLong ||
                widest < labelWide - (.1f * teleconverter) || widest > labelTele + (.1f * teleconverter))
                continue;
            names.Add(name);
        }
        if (names.Count != 0)
            return string.Join(" *OR* ", names);
        return type == 0xFFFF ? Label(LensTables.Canon, type) ?? UnknownLens(type) : UnknownLens(type);
    }

    /// <summary>
    /// The Nikon F-mount lens of a lens data array: the row whose eight bytes are those of the array, a
    /// row of Sigma also when its f-stops field moved, the code alone when the array or the lens type of
    /// the note is not there or no row matches.
    /// </summary>
    /// <param name="code">The value of the LensIDNumber field.</param>
    /// <param name="lensData">The entry of the lens data array the field belongs to.</param>
    /// <param name="note">The note, which holds the lens type beside the array.</param>
    internal static string? NikonF(ExifValue code, MakerNoteEntry lensData, MakerNote note)
    {
        Span<byte> raw = stackalloc byte[8];
        for (int index = 0; index < NikonLensBytes.Length; index++)
        {
            if (Field(lensData, NikonLensBytes[index]) is not ExifValue field || field.FieldType != TiffFieldType.Byte ||
                field.Count == 0 || !field.TryGetByteArray(out byte[]? bytes))
                return Parenthesized(code);
            raw[index] = bytes[0];
        }
        if (!note.TryGetEntry(0x0083, out MakerNoteEntry? lensType) || lensType.Value.FieldType != TiffFieldType.Byte ||
            lensType.Value.Count == 0 || !lensType.Value.TryGetByteArray(out byte[]? types))
            return Parenthesized(code);
        raw[7] = types[0];

        ulong key = NikonKey(raw);
        foreach ((ulong candidate, string manufacturer, string name) in LensTables.NikonF)
        {
            if ((candidate >> 56) > raw[0])
                break;
            if ((candidate >> 56) != raw[0])
                continue;
            if (candidate == key || (manufacturer == "Sigma" && (candidate & ~NikonFStopsMask) == (key & ~NikonFStopsMask)))
                return manufacturer + " " + name;
        }
        return Parenthesized(code);
    }

    /// <summary>The Nikon Z-mount lens of a lens data array, or the id alone when the table does not hold it.</summary>
    /// <param name="code">The value of the LensID field.</param>
    internal static string? NikonZ(ExifValue code)
    {
        if (code.Count != 1 || !ExifTags.TryInteger(code, 0, out long stored) || stored is < 0 or > ushort.MaxValue)
            return null;
        ushort type = (ushort)stored;
        foreach ((ushort candidate, string vendor, string name) in LensTables.NikonZ)
            if (candidate == type)
                return vendor + " " + name;
        return Parenthesized(code);
    }

    /// <summary>
    /// The Pentax lens of the two bytes of a lens type: their number names a lens on its own, or one of
    /// the lenses the number shares, which the lens info array, the camera and the focal length of the
    /// picture decide between.
    /// </summary>
    /// <param name="code">The value of the LensType tag.</param>
    /// <param name="note">The note, which holds the lens info array.</param>
    /// <param name="model">The camera model of the file, if known.</param>
    /// <param name="image">The rest of the file, if known.</param>
    internal static string? Pentax(ExifValue code, MakerNote note, string? model, ExifMetadata? image)
    {
        if (code.FieldType != TiffFieldType.Byte || !code.TryGetByteArray(out byte[]? bytes) ||
            bytes.Length is < 2 or > 4)
            return null;
        byte[] lensInfo = LensInfo(note);
        int number = (bytes[0] << 8) | bytes[1];
        int alternative = Array.IndexOf(PentaxResolved, (ushort)number) < 0 ? 0 : PentaxAlternative(bytes, lensInfo, model, image);
        string? name = Alternative(LensTables.Pentax, (ushort)number, alternative);
        return name ?? "Unknown (0x" + number.ToString("x4", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The lens of a Sony or Minolta lens id: the label the table holds for it, the one alternative of
    /// that label the camera and the picture decide between, or the id alone when the table names none.
    /// </summary>
    /// <param name="code">The value of the lens id tag.</param>
    /// <param name="model">The camera model of the file, if known.</param>
    /// <param name="image">The rest of the file, if known.</param>
    internal static string? MinoltaSony(ExifValue code, string? model, ExifMetadata? image)
    {
        if (!ExifTags.TryInteger(code, 0, out long stored) || stored is < 0 or > ushort.MaxValue)
            return null;
        ushort id = (ushort)stored;
        string? label = Label(LensTables.MinoltaSony, id);
        if (label is null)
            return Parenthesized(code);
        if (Array.IndexOf(SonyResolved, id) < 0)
            return label;
        var names = new List<string>();
        foreach (string part in label.Split('|'))
        {
            if (part.Trim().Length != 0)
                names.Add(part.Trim());
        }
        int alternative = SonyAlternative(id, model, image);
        return alternative != 0 && alternative <= names.Count ? names[alternative - 1] : label;
    }

    /// <summary>Which of the lenses a Pentax number shares took the picture, as the index of its label, zero for none.</summary>
    private static int PentaxAlternative(byte[] value, byte[] lensInfo, string? model, ExifMetadata? image)
    {
        switch ((value[0] << 8) | value[1])
        {
            case 0x0319:
                if (value.Length == 4 && lensInfo.Length == 128 && StartsWith(model, "PENTAX K-3") &&
                    lensInfo[1] == 131 && lensInfo[2] == 128)
                    return 6;
                if (value.Length == 2)
                {
                    if (StartsWith(model, "PENTAX K100D") && lensInfo.Length == 44)
                        return 6;
                    if (StartsWith(model, "PENTAX *ist DL") && lensInfo.Length == 36)
                        return 6;
                }
                return 0;
            case 0x032C:
                // The one code of the range that the focal length of the picture separates from the rest.
                return FocalLength(image) is long focal && focal is >= 10 and <= 20 ? 1 : 0;
            case 0x03FF:
                if (lensInfo.Length < 5)
                    return 0;
                // The tests are the three that were recorded for the id, in their order: a later one that
                // holds names the lens the earlier one was too wide for.
                int alternative = 0;
                if (value.Length == 2)
                {
                    bool plain = (lensInfo[2] & 0x01) == 0 && (lensInfo[3] & 0x06) == 0 && (lensInfo[4] & 0xF8) == 0x28;
                    if (plain && At(lensInfo, 5) == 148)
                        alternative = 8;
                    if (plain && At(lensInfo, 6) == 110)
                        alternative = 7;
                    if (plain && lensInfo[4] == 110)
                        alternative = 7;
                    return alternative;
                }
                if (value.Length == 3)
                    return At(lensInfo, 4) == 0 && At(lensInfo, 5) == 40 && At(lensInfo, 6) == 148 ? 8 : 0;
                if (lensInfo.Length == 128 && lensInfo[1] == 131 && lensInfo[2] == 128)
                    alternative = 8;
                if (At(lensInfo, 6) == 5)
                    alternative = 7;
                return alternative;
            case 0x08FF:
                return value.Length == 4 && lensInfo.Length == 128 && StartsWith(model, "PENTAX K-3") &&
                       lensInfo[1] == 168 && lensInfo[2] == 144
                    ? 7
                    : 0;
        }
        return 0;
    }

    /// <summary>Which of the lenses a Sony or Minolta label holds took the picture, as the number of its part, zero for none.</summary>
    private static int SonyAlternative(ushort id, string? model, ExifMetadata? image)
    {
        string? lens = image?.LensModel;
        string aperture = Aperture(image);
        long focal = FocalLength(image) ?? -1;
        switch (id)
        {
            case 0x001C:
                return model == "SLT-A77V" && lens == "100mm F2.8 Macro" ? 2 : 0;
            case 0x0029:
                return model == "SLT-A77V" && lens == "DT 11-18mm F4.5-5.6" ? 2 : 0;
            case 0x0034:
                if (model != "SLT-A77V")
                    return 0;
                if (aperture == "760/256")
                    return 4;
                return focal is >= 70 and <= 300 ? 3 : 0;
            case 0x0080:
                return model == "SLT-A77V" && aperture == "1024/256" && focal is >= 18 and <= 200 ? 2 : 0;
            case 0x00FF:
                return model == "SLT-A77V" && aperture == "760/256" && focal is >= 17 and <= 50 ? 1 : 0;
            case 0xFFFF:
                // The label of this id names the lens no camera reports and two lenses of one camera, which
                // the widest aperture and the ratio of the two focal lengths of the picture separate.
                if (model != "ILCE-6000" || focal <= 0 || image?.FocalLengthIn35mmFilm is not int focal35mm)
                    return 1;
                long ratio = (focal35mm * 100) / focal;
                if (ratio is < 145 or > 155)
                    return 1;
                if (aperture == "434/256")
                    return 2;
                return aperture is "926/256" or "1024/256" or "1110/256" or "1188/256" or "1272/256" ? 3 : 1;
        }
        return 0;
    }

    /// <summary>The settings of the one camera whose lens type of no lens still names a lens, or false.</summary>
    private static bool CanonThirtyDSettings(MakerNoteEntry settings, string? model) =>
        model?.Trim() == "Canon EOS 30D" &&
        Field(settings, "Lens") is ExifValue lens && lens.Count == 3 &&
        ExifTags.TryInteger(lens, 0, out long longFocal) && longFocal == 24 &&
        ExifTags.TryInteger(lens, 1, out long shortFocal) && shortFocal == 24 &&
        ExifTags.TryInteger(lens, 2, out long units) && units == 1 &&
        Field(settings, "MaxAperture") is ExifValue aperture && ExifTags.TryInteger(aperture, 0, out long stored) &&
        stored == 95;

    /// <summary>The widest aperture, in f-numbers, of a Canon aperture value.</summary>
    private static float Aperture(long stored) =>
        (float)Math.Pow(2.0, MakerNoteLabels.CanonEv(unchecked((short)stored)) / 2.0);

    /// <summary>The widest aperture of the picture as the reference of Sony and Minolta compares it, or an empty text.</summary>
    private static string Aperture(ExifMetadata? image) =>
        image?.MaxApertureValue is UnsignedRational aperture
            ? aperture.Numerator.ToString(CultureInfo.InvariantCulture) + "/" + aperture.Denominator.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>The focal length of the picture in whole millimetres, or null.</summary>
    private static long? FocalLength(ExifMetadata? image) =>
        image?.FocalLength is double focal && !double.IsNaN(focal) && !double.IsInfinity(focal) ? (long)focal : null;

    /// <summary>The lens info array of a Pentax note, which is a directory of bytes, or an empty array.</summary>
    private static byte[] LensInfo(MakerNote note) =>
        note.TryGetEntry(0x0207, out MakerNoteEntry? info) && info.Value.TryGetByteArray(out byte[]? bytes) ? bytes : [];

    /// <summary>One byte of a value, or zero for an index the value does not hold.</summary>
    private static int At(byte[] bytes, int index) => index < bytes.Length ? bytes[index] : 0;

    /// <summary>The value of a named field of an entry, or null when the entry does not hold one.</summary>
    private static ExifValue? Field(MakerNoteEntry entry, string name)
    {
        foreach (MakerNoteField field in entry.Fields)
            if (field.Name == name)
                return field.Value;
        return null;
    }

    /// <summary>The eight bytes of a Nikon lens as one number, in the order the note stores them.</summary>
    private static ulong NikonKey(ReadOnlySpan<byte> raw)
    {
        ulong key = 0;
        foreach (byte value in raw)
            key = (key << 8) | value;
        return key;
    }

    /// <summary>The first label of a code, or null when the table does not hold the code.</summary>
    private static string? Label((ushort Code, string Name)[] table, ushort code)
    {
        foreach ((ushort candidate, string name) in table)
            if (candidate == code)
                return name;
        return null;
    }

    /// <summary>The label of a code at one position among the alternatives of that code, or null.</summary>
    private static string? Alternative((ushort Code, string Name)[] table, ushort code, int alternative)
    {
        int seen = 0;
        foreach ((ushort candidate, string name) in table)
        {
            if (candidate != code)
                continue;
            if (seen++ == alternative)
                return name;
        }
        return null;
    }

    /// <summary>A number of a lens name, read as the reference reads it: an invariant fraction of no exponent.</summary>
    private static float Number(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>True for a model that starts with the given text.</summary>
    private static bool StartsWith(string? model, string prefix) =>
        model is not null && model.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>A code the table does not name, in the parentheses of the other unnamed numbers.</summary>
    private static string UnknownLens(ushort code) => "Unknown Lens (" + code.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>A value of a note, in the parentheses of the other unnamed numbers.</summary>
    private static string Parenthesized(ExifValue value) =>
        ExifTags.TryInteger(value, 0, out long number)
            ? "(" + number.ToString(CultureInfo.InvariantCulture) + ")"
            : "(" + value.Count.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>The parts of a Canon lens name: the focal range and the widest aperture at each end.</summary>
    [GeneratedRegex(CanonLensName, RegexOptions.CultureInvariant)]
    private static partial Regex CanonLens();
}
