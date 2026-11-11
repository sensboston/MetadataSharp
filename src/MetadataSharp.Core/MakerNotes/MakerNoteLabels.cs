using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.MakerNotes;

// The human-readable form of a MakerNote value. As for standard EXIF, the raw value stays the truth and
// an interpretation is only derived from it for display.
//
// The tables are this project's own and are written by hand, one field at a time: a number and the short
// label a property sheet shows for it, or the unit a number is counted in. They cover the Canon, Nikon
// and Sony fields that occur in the comparison corpus, and for each of those the numbers that occur
// there; they are not complete vendor vocabularies. A number a table does not list is shown as stored,
// in parentheses, so that a missing label is never mistaken for a meaning.
//
// Everything is integer arithmetic. Fields whose meaning needs a power, a logarithm or another field of
// the note (apertures and shutter speeds in APEX-like codes, lens identifiers, fields that depend on the
// camera model) are deliberately left without an interpretation.
internal static partial class MakerNoteLabels
{
    /// <summary>The scope of a tag of the note's own directory; a field of an array has its tag as scope.</summary>
    private const int Top = -1;

    /// <summary>What a number without a label of its own is read as.</summary>
    private enum Form
    {
        /// <summary>An enumerated number: one without a label answers as stored, in parentheses.</summary>
        Label,

        /// <summary>A count of millimetres.</summary>
        Millimetres,

        /// <summary>A frequency in hertz.</summary>
        Hertz,

        /// <summary>A colour temperature in kelvin.</summary>
        Kelvin,

        /// <summary>Hundredths of a metre, written as metres with two decimals.</summary>
        CentimetresFixed,

        /// <summary>Hundredths of a metre, written as metres without trailing zeros.</summary>
        Centimetres,

        /// <summary>Tenths of a second, written as seconds without trailing zeros.</summary>
        TenthsOfSecond,

        /// <summary>Degrees Celsius stored with 128 added.</summary>
        CelsiusPlus128,

        /// <summary>A setting stored with 128 added, so that 128 is the middle of its scale.</summary>
        Plus128,

        /// <summary>A signed byte counting sixths of a stop downwards: 252 is +2/3 EV.</summary>
        SixthsOfStopDown,

        /// <summary>
        /// Four bytes: a signed count, the number of steps one count is, and the number of steps in a
        /// stop. "250 1 6 0" is six sixths of a stop down.
        /// </summary>
        StopFraction,

        /// <summary>Minutes east of UTC.</summary>
        MinutesFromUtc,

        /// <summary>A directory number and a file number in one decimal number: DDDFFFF.</summary>
        DirectoryAndFile,

        /// <summary>A count that starts at zero for the first item.</summary>
        OneBased,

        /// <summary>A number of shots.</summary>
        Shots,

        /// <summary>A number of files.</summary>
        Files,

        /// <summary>A shift on the green-magenta axis: negative towards green.</summary>
        GreenMagenta,

        /// <summary>Two shifts: amber-blue (negative towards amber), then green-magenta.</summary>
        AmberBlueGreenMagenta,

        /// <summary>A height and then a width, written width first.</summary>
        HeightThenWidth,

        /// <summary>SHORT bit masks: the positions of the set bits, counted across the elements.</summary>
        BitPositions,

        /// <summary>Four bytes read as one number, most significant first, and looked up as such.</summary>
        FourBytes,

        /// <summary>Text that is an abbreviation with a longer reading.</summary>
        Abbreviation,
    }

    /// <summary>One field: where it is, what it is called, the labels of its numbers and what the rest read as.</summary>
    private sealed record Rule(int Scope, string Name, (long Number, string Label)[] Labels, Form Reading = Form.Label)
    {
        /// <summary>The readings of an <see cref="Form.Abbreviation" />.</summary>
        public (string Text, string Label)[] Texts { get; init; } = [];
    }

    /// <summary>Every rule, for the tests that check the tables against themselves.</summary>
    internal static IEnumerable<(string Vendor, int Scope, string Name, IReadOnlyList<long> Numbers, IReadOnlyList<string> Labels)> RulesForTests()
    {
        foreach ((string vendor, Rule[] rules) in new[] { ("Canon", Canon), ("Nikon", Nikon), ("Sony", Sony) })
        {
            foreach (Rule rule in rules)
            {
                yield return (vendor, rule.Scope, rule.Name,
                    rule.Labels.Select(label => label.Number).ToArray(),
                    rule.Labels.Select(label => label.Label).Concat(rule.Texts.Select(text => text.Label)).ToArray());
            }
        }
    }

    internal static string? Interpret(MakerNoteFormat format, MakerNoteEntry entry) =>
        Interpret(format, Top, entry.Name, entry.Value);

    internal static string? Interpret(MakerNoteFormat format, MakerNoteEntry entry, MakerNoteField field) =>
        Interpret(format, entry.TagId, field.Name, field.Value);

    private static string? Interpret(MakerNoteFormat format, int scope, string? name, ExifValue value)
    {
        if (name is null)
            return null;
        Rule[]? rules = format switch
        {
            MakerNoteFormat.Canon => Canon,
            MakerNoteFormat.Nikon3 => Nikon,
            MakerNoteFormat.Sony1 or MakerNoteFormat.Sony2 => Sony,
            _ => null,
        };
        if (rules is null)
            return null;
        // Every Nikon array that has a version starts with it, and the note has one of its own.
        if (format == MakerNoteFormat.Nikon3 && name == "Version")
            return Version(value);
        foreach (Rule rule in rules)
        {
            if (rule.Scope == scope && rule.Name == name)
                return Apply(rule, value);
        }
        return null;
    }

    private static string? Apply(Rule rule, ExifValue value)
    {
        switch (rule.Reading)
        {
            case Form.BitPositions:
                return BitPositions(value);
            case Form.HeightThenWidth:
                return HeightThenWidth(value);
            case Form.AmberBlueGreenMagenta:
                return AmberBlueGreenMagenta(value);
            case Form.FourBytes:
                return FourBytes(rule, value);
            case Form.StopFraction:
                return StopFraction(value);
            case Form.Abbreviation:
                return Abbreviation(rule, value);
        }

        if (value.Count != 1 || value.FieldType == TiffFieldType.Undefined ||
            !ExifTags.TryInteger(value, 0, out long number))
            return null;
        foreach ((long candidate, string label) in rule.Labels)
        {
            if (candidate == number)
                return label;
        }

        switch (rule.Reading)
        {
            case Form.Millimetres:
                return Whole(number) + " mm";
            case Form.Hertz:
                return Whole(number) + " Hz";
            case Form.Kelvin:
                return Whole(number) + " K";
            case Form.CentimetresFixed:
                return number < 0 ? Stored(number) : Whole(number / 100) + "." + TwoDigits(number % 100) + " m";
            case Form.Centimetres:
                return number < 0 ? Stored(number) : Hundredths(number) + " m";
            case Form.TenthsOfSecond:
                return number < 0 ? Stored(number) : Whole(number / 10) + (number % 10 == 0 ? "" : "." + Whole(number % 10)) + " s";
            case Form.CelsiusPlus128:
                return Whole(number - 128) + " \u00B0C";
            case Form.Plus128:
                return Whole(number - 128);
            case Form.SixthsOfStopDown:
                return SixthsOfStopDown(number);
            case Form.MinutesFromUtc:
                return MinutesFromUtc(number);
            case Form.DirectoryAndFile:
                return number <= 0 ? Stored(number) : Whole(number / 10000) + "-" + (number % 10000).ToString("D4", CultureInfo.InvariantCulture);
            case Form.OneBased:
                return Whole(number + 1);
            case Form.Shots:
                return Whole(number) + (number == 1 ? " shot" : " shots");
            case Form.Files:
                return Whole(number) + (number == 1 ? " file" : " files");
            case Form.GreenMagenta:
                return "G/M: " + Shift(Signed32(value, number), 'G', 'M');
            default:
                return Stored(number);
        }
    }

    private static string Whole(long number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>A number the field has no label for, shown as stored so that it is not mistaken for one.</summary>
    private static string Stored(long number) => "(" + Whole(number) + ")";

    private static string TwoDigits(long number) => number.ToString("D2", CultureInfo.InvariantCulture);

    /// <summary>Hundredths as a decimal without trailing zeros: 107 is 1.07, 150 is 1.5, 200 is 2.</summary>
    private static string Hundredths(long number)
    {
        long whole = number / 100, rest = number % 100;
        if (rest == 0)
            return Whole(whole);
        return Whole(whole) + "." + (rest % 10 == 0 ? Whole(rest / 10) : TwoDigits(rest));
    }

    /// <summary>
    /// The byte is a signed count of sixths of a stop, and a positive count lowers the exposure. The
    /// result is rounded to a tenth; a sixth never falls halfway between two tenths.
    /// </summary>
    private static string SixthsOfStopDown(long number)
    {
        if (number is < 0 or > 255)
            return Stored(number);
        int sixths = unchecked((sbyte)(byte)number);
        if (sixths == 0)
            return "0.0 EV";
        int tenths = ((Math.Abs(sixths) * 10) + 3) / 6;
        return (sixths < 0 ? "+" : "-") + Whole(tenths / 10) + "." + Whole(tenths % 10) + " EV";
    }

    /// <summary>
    /// Count times step over steps-per-stop, rounded to a tenth of a stop; a quotient exactly halfway
    /// between two tenths goes to the even one, as everywhere else in this library.
    /// </summary>
    private static string? StopFraction(ExifValue value)
    {
        if (value.Count != 4 || value.FieldType is not (TiffFieldType.Undefined or TiffFieldType.Byte))
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        int steps = unchecked((sbyte)bytes[0]) * bytes[1];
        int perStop = bytes[2];
        if (perStop == 0)
            return null;
        if (steps == 0)
            return "0.0 EV";
        int size = Math.Abs(steps) * 10;
        int tenths = size / perStop, rest = size % perStop;
        if (rest * 2 > perStop || (rest * 2 == perStop && (tenths & 1) == 1))
            tenths++;
        return (steps < 0 ? "-" : "+") + Whole(tenths / 10) + "." + Whole(tenths % 10) + " EV";
    }

    private static string MinutesFromUtc(long minutes)
    {
        long size = Math.Abs(minutes);
        return "UTC " + (minutes < 0 ? "-" : "+") + TwoDigits(size / 60) + ":" + TwoDigits(size % 60);
    }

    /// <summary>A shift stored in an unsigned LONG is a signed number all the same.</summary>
    private static long Signed32(ExifValue value, long number) =>
        value.FieldType == TiffFieldType.Long ? unchecked((int)(uint)number) : number;

    private static string Shift(long amount, char negative, char positive)
    {
        if (amount == 0)
            return "0";
        return (amount < 0 ? negative : positive) + Whole(Math.Abs(amount));
    }

    private static string? AmberBlueGreenMagenta(ExifValue value)
    {
        if (value.Count != 2 || value.FieldType is not (TiffFieldType.SLong or TiffFieldType.Long) ||
            !ExifTags.TryInteger(value, 0, out long amberBlue) || !ExifTags.TryInteger(value, 1, out long greenMagenta))
            return null;
        return "A/B: " + Shift(Signed32(value, amberBlue), 'A', 'B') +
               ", G/M: " + Shift(Signed32(value, greenMagenta), 'G', 'M');
    }

    private static string? HeightThenWidth(ExifValue value)
    {
        if (value.Count != 2 || value.FieldType is not (TiffFieldType.Long or TiffFieldType.Short) ||
            !ExifTags.TryInteger(value, 0, out long height) || !ExifTags.TryInteger(value, 1, out long width))
            return null;
        return Whole(width) + " x " + Whole(height);
    }

    private static string? BitPositions(ExifValue value)
    {
        if (value.Count == 0 || value.FieldType != TiffFieldType.Short)
            return null;
        var positions = new StringBuilder();
        for (int element = 0; (uint)element < value.Count; element++)
        {
            if (!ExifTags.TryInteger(value, element, out long mask))
                return null;
            for (int bit = 0; bit < 16; bit++)
            {
                if ((mask & (1L << bit)) == 0)
                    continue;
                if (positions.Length != 0)
                    positions.Append(',');
                positions.Append(((element * 16) + bit).ToString(CultureInfo.InvariantCulture));
            }
        }
        return positions.Length == 0 ? "(none)" : positions.ToString();
    }

    private static string? FourBytes(Rule rule, ExifValue value)
    {
        if (value.Count != 4 || value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined))
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        long number = ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
        foreach ((long candidate, string label) in rule.Labels)
        {
            if (candidate == number)
                return label;
        }
        return null;
    }

    private static string? Abbreviation(Rule rule, ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Ascii || value.Count == 0)
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        int length = bytes.IndexOf((byte)0);
        if (length >= 0)
            bytes = bytes.Slice(0, length);
        string text = Encoding.Latin1.GetString(bytes).TrimEnd(' ');
        foreach ((string candidate, string label) in rule.Texts)
        {
            if (string.Equals(candidate, text, StringComparison.Ordinal))
                return label;
        }
        return "(" + text + ")";
    }

    /// <summary>
    /// A version written as four digits, two for each part: "0211" is 2.11 and "0100" is 1.00. Anything
    /// else has no reading.
    /// </summary>
    private static string? Version(ExifValue value)
    {
        if (value.Count != 4 || value.FieldType is not (TiffFieldType.Undefined or TiffFieldType.Byte))
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        foreach (byte digit in bytes)
        {
            if (digit < (byte)'0' || digit > (byte)'9')
                return null;
        }
        string major = bytes[0] == (byte)'0' ? ((char)bytes[1]).ToString() : ((char)bytes[0]).ToString() + (char)bytes[1];
        return major + "." + (char)bytes[2] + (char)bytes[3];
    }
}
