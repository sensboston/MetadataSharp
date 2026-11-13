using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.MakerNotes;

// The human-readable form of a MakerNote value. As for standard EXIF, the raw value stays the truth and
// an interpretation is only derived from it for display.
//
// The tables are this project's own and are written by hand, one field at a time: a number and the short
// label a property sheet shows for it, or the unit a number is counted in. They cover selected Canon,
// Nikon, Sony and Pentax DNG fields in the comparison corpus and the numbers checked there; they are
// not complete vendor vocabularies. A number a table does not list is shown as stored,
// in parentheses, so that a missing label is never mistaken for a meaning.
//
// Numeric formats use invariant-culture arithmetic and formatting. Fields whose meaning depends on a
// lens-name database or an unavailable camera-specific rule remain uninterpreted; the stored value stays
// available to callers.
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

        /// <summary>A SHORT bit mask rendered as the vendor's labels for its set flags.</summary>
        BitMask,

        CanonAutoIso,
        CanonBulbDuration,
        CanonFileNumber,
        CanonPictureStyle,
        CanonSerialNumber,
        CanonRaw,
        CanonIso,
        CanonMeasuredEv,
        CanonMeasuredEv2,
        CanonAperture,
        CanonShutter,
        CanonFlashGuideNumber,
        CanonAfPointUsed,
        CanonLens,
        CanonFocalLength,

        /// <summary>Four bytes read as one number, most significant first, and looked up as such.</summary>
        FourBytes,

        /// <summary>Text that is an abbreviation with a longer reading.</summary>
        Abbreviation,

        /// <summary>Bytes written as hexadecimal digits, two for each.</summary>
        Hex,

        /// <summary>Two numbers of which the second is the value.</summary>
        SecondOfTwo,

        /// <summary>
        /// A list of codes in which zero is an empty place: the labels of the others, or the label of
        /// zero when there are none.
        /// </summary>
        CodeList,

        /// <summary>A code in the low seven bits of a byte.</summary>
        LowSevenBits,

        /// <summary>A count of eight pixels.</summary>
        TimesEight,

        /// <summary>A year counted from 2000.</summary>
        YearSince2000,

        /// <summary>A position from 0 to 1024, written as a whole percentage.</summary>
        PercentOf1024,

        /// <summary>Two numbers, written with a comma between them.</summary>
        Pair,

        /// <summary>Two shifts as in <see cref="Form.AmberBlueGreenMagenta" />, in thousandths of a step.</summary>
        AmberBlueGreenMagentaThousandths,

        /// <summary>Four rationals: the shortest and the longest focal length, then the widest aperture at each.</summary>
        LensRange,

        /// <summary>Pentax DNG field whose printed meaning uses its first numeric item.</summary>
        PentaxFirst,

        /// <summary>Pentax DNG version bytes written with dots between their numbers.</summary>
        PentaxVersion,

        /// <summary>Pentax DNG preview dimensions written with an x between the numbers.</summary>
        PentaxResolution,

        /// <summary>Pentax DNG date bytes: a two-byte year, month, and day.</summary>
        PentaxDate,

        /// <summary>Pentax DNG time bytes: hour, minute, and second.</summary>
        PentaxTime,

        /// <summary>Pentax DNG exposure time in hundredths of a millisecond.</summary>
        PentaxExposure,

        /// <summary>Pentax DNG aperture in tenths, prefixed by F.</summary>
        PentaxFNumber,

        /// <summary>Pentax DNG focal length in hundredths of a millimetre, fixed to one decimal.</summary>
        PentaxFocalLength,

        /// <summary>Pentax DNG exposure compensation: (first value minus 50) tenths of an EV.</summary>
        PentaxExposureCompensation,

        /// <summary>Pentax DNG temperature in degrees Celsius.</summary>
        PentaxTemperature,

        /// <summary>Pentax DNG flash compensation in 1/256 EV units.</summary>
        PentaxFlashCompensation,

        /// <summary>Pentax DNG bracketing range and optional extension code.</summary>
        PentaxBracketing,

        /// <summary>A three-byte Pentax DNG composite enum.</summary>
        PentaxThreeByteEnum,

        /// <summary>A four-byte Pentax DNG composite enum.</summary>
        PentaxFourByteEnum,

        /// <summary>A four-byte Pentax DNG dynamic-range status.</summary>
        PentaxDynamicRange,

        /// <summary>Fujifilm white-balance fine tune, two signed shifts in twentieths.</summary>
        FujifilmWhiteBalanceFineTune,

        /// <summary>Fujifilm digital zoom, stored in eighths.</summary>
        FujifilmDigitalZoom,

        /// <summary>Fujifilm packed drive setting.</summary>
        FujifilmDriveSetting,

        /// <summary>Fujifilm face-element type codes stored as ASCII digits.</summary>
        FujifilmFaceElementTypes,

        NikonFocusDistance,
        NikonDigitalZoom,
        NikonAfFocusPosition,
        NikonLd3Aperture,
        NikonLd3FocusDistance,
        NikonLd3FocalLength,
        NikonLd3LensFStops,
        NikonLd3ExitPupil,
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
        foreach ((string vendor, Rule[] rules) in new[] { ("Canon", Canon), ("Nikon", Nikon), ("Nikon1", Nikon1), ("Nikon2", Nikon2), ("Sony", Sony), ("Pentax", PentaxDng), ("PentaxDng", PentaxDng), ("Fujifilm", Fujifilm) })
        {
            foreach (Rule rule in rules)
            {
                yield return (vendor, rule.Scope, rule.Name,
                    rule.Labels.Select(label => label.Number).ToArray(),
                    rule.Labels.Select(label => label.Label).Concat(rule.Texts.Select(text => text.Label)).ToArray());
            }
        }
    }

    internal static string? Interpret(MakerNote note, MakerNoteEntry entry, string? cameraModel = null) =>
        Interpret(note.Format, Top, entry.Name, entry.Value, note, cameraModel);

    internal static string? Interpret(MakerNote note, MakerNoteEntry entry, MakerNoteField field, string? cameraModel = null)
    {
        // Nikon's preview directory holds ordinary TIFF fields, and they read as they do anywhere else.
        if (note.Format == MakerNoteFormat.Nikon3 && entry.TagId == 0x0011)
            return ExifTags.Interpret(new ExifTagKey(ExifDirectoryKind.Ifd0, field.Index), field.Value);
        return Interpret(note.Format, entry.TagId, field.Name, field.Value, note, cameraModel);
    }

    private static string? Interpret(MakerNoteFormat format, int scope, string? name, ExifValue value,
        MakerNote? note = null, string? cameraModel = null)
    {
        if (name is null)
            return null;
        Rule[]? rules = format switch
        {
            MakerNoteFormat.Canon => Canon,
            MakerNoteFormat.Fujifilm => Fujifilm,
            MakerNoteFormat.Nikon1 => Nikon1,
            MakerNoteFormat.Nikon2 => Nikon2,
            MakerNoteFormat.Nikon3 => Nikon,
            MakerNoteFormat.Sony1 or MakerNoteFormat.Sony2 => Sony,
            MakerNoteFormat.Pentax or MakerNoteFormat.PentaxDng => PentaxDng,
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
                return Apply(rule, value, note, cameraModel);
        }
        return null;
    }

    private static string? Apply(Rule rule, ExifValue value, MakerNote? note = null, string? cameraModel = null)
    {
        if (rule.Reading is Form.PentaxFirst or Form.PentaxVersion or Form.PentaxResolution or Form.PentaxDate or Form.PentaxTime or
            Form.PentaxExposure or Form.PentaxFNumber or Form.PentaxFocalLength or Form.PentaxExposureCompensation or
            Form.PentaxTemperature or Form.PentaxFlashCompensation or Form.PentaxBracketing or
            Form.PentaxThreeByteEnum or Form.PentaxFourByteEnum or Form.PentaxDynamicRange)
            return PentaxDngInterpret(rule, value, note);

        switch (rule.Reading)
        {
            case Form.BitPositions:
                return BitPositions(value);
            case Form.BitMask:
                return BitMask(rule, value);
            case Form.CanonAutoIso:
                return CanonAutoIso(value);
            case Form.CanonBulbDuration:
                return CanonBulbDuration(value);
            case Form.CanonFileNumber:
                return CanonFileNumber(value, cameraModel);
            case Form.CanonPictureStyle:
                return CanonPictureStyle(rule, value);
            case Form.CanonSerialNumber:
                return CanonSerialNumber(value, note);
            case Form.CanonRaw:
                return CanonRaw(value);
            case Form.CanonIso:
                return CanonIso(value);
            case Form.CanonMeasuredEv:
                return CanonMeasuredEv(value);
            case Form.CanonMeasuredEv2:
                return CanonMeasuredEv2(value);
            case Form.CanonAperture:
                return CanonAperture(value);
            case Form.CanonShutter:
                return CanonShutter(value);
            case Form.CanonFlashGuideNumber:
                return CanonFlashGuideNumber(value);
            case Form.CanonAfPointUsed:
                return CanonAfPointUsed(value);
            case Form.CanonLens:
                return CanonLens(value);
            case Form.CanonFocalLength:
                return CanonFocalLength(value, note);
            case Form.HeightThenWidth:
                return HeightThenWidth(value);
            case Form.AmberBlueGreenMagenta:
                return AmberBlueGreenMagenta(value, thousandths: false);
            case Form.AmberBlueGreenMagentaThousandths:
                return AmberBlueGreenMagenta(value, thousandths: true);
            case Form.Hex:
                return Hex(value);
            case Form.SecondOfTwo:
                return SecondOfTwo(value);
            case Form.CodeList:
                return CodeList(rule, value);
            case Form.Pair:
                return Pair(value);
            case Form.LensRange:
                return LensRange(value);
            case Form.FourBytes:
                return FourBytes(rule, value);
            case Form.StopFraction:
                return StopFraction(value);
            case Form.Abbreviation:
                return Abbreviation(rule, value);
            case Form.FujifilmWhiteBalanceFineTune:
                return FujifilmWhiteBalanceFineTune(value);
            case Form.FujifilmDigitalZoom:
                return FujifilmDigitalZoom(value);
            case Form.FujifilmDriveSetting:
                return FujifilmDriveSetting(value);
            case Form.FujifilmFaceElementTypes:
                return FujifilmFaceElementTypes(value);
            case Form.NikonFocusDistance:
                return NikonFocusDistance(value);
            case Form.NikonDigitalZoom:
                return NikonDigitalZoom(value);
            case Form.NikonAfFocusPosition:
                return NikonAfFocusPosition(value);
            case Form.NikonLd3Aperture:
                return NikonLd3Aperture(value);
            case Form.NikonLd3FocusDistance:
                return NikonLd3FocusDistance(value);
            case Form.NikonLd3FocalLength:
                return NikonLd3FocalLength(value);
            case Form.NikonLd3LensFStops:
                return NikonLd3LensFStops(value);
            case Form.NikonLd3ExitPupil:
                return NikonLd3ExitPupil(value);
        }

        if (value.Count != 1 || value.FieldType == TiffFieldType.Undefined ||
            !ExifTags.TryInteger(value, 0, out long number))
            return null;
        if (rule.Reading == Form.LowSevenBits)
            number &= 0x7F;
        foreach ((long candidate, string label) in rule.Labels)
        {
            if (candidate == number)
                return label;
        }

        switch (rule.Reading)
        {
            case Form.TimesEight:
                return Whole(number * 8);
            case Form.YearSince2000:
                return Whole(2000 + number);
            case Form.PercentOf1024:
                return number < 0 ? Stored(number) : Whole(HalfEven(number * 100, 1024)) + "%";
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

    /// <summary>A quotient of two non-negative numbers, rounded to a whole one; a half goes to the even one.</summary>
    private static long HalfEven(long dividend, long divisor)
    {
        long quotient = dividend / divisor, rest = dividend % divisor;
        if (rest * 2 > divisor || (rest * 2 == divisor && (quotient & 1) == 1))
            quotient++;
        return quotient;
    }

    private static string Shift(long amount, char negative, char positive, bool thousandths = false)
    {
        if (amount == 0)
            return "0";
        long size = Math.Abs(amount);
        return (amount < 0 ? negative : positive) +
               (thousandths ? Whole(size / 1000) + "." + TwoDigits(size % 1000 / 10) : Whole(size));
    }

    private static string? AmberBlueGreenMagenta(ExifValue value, bool thousandths)
    {
        if (value.Count != 2 || value.FieldType is not (TiffFieldType.SLong or TiffFieldType.Long) ||
            !ExifTags.TryInteger(value, 0, out long amberBlue) || !ExifTags.TryInteger(value, 1, out long greenMagenta))
            return null;
        return "A/B: " + Shift(Signed32(value, amberBlue), 'A', 'B', thousandths) +
               ", G/M: " + Shift(Signed32(value, greenMagenta), 'G', 'M', thousandths);
    }

    private static string? Hex(ExifValue value)
    {
        if (value.Count == 0 || value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined))
            return null;
        return Convert.ToHexString(value.RawBytes).ToLowerInvariant();
    }

    private static string? SecondOfTwo(ExifValue value)
    {
        if (value.Count != 2 || value.FieldType is not (TiffFieldType.Short or TiffFieldType.Long) ||
            !ExifTags.TryInteger(value, 1, out long second))
            return null;
        return Whole(second);
    }

    private static string? Pair(ExifValue value)
    {
        if (value.Count != 2 || value.FieldType is not (TiffFieldType.Short or TiffFieldType.Long) ||
            !ExifTags.TryInteger(value, 0, out long first) || !ExifTags.TryInteger(value, 1, out long second))
            return null;
        return Whole(first) + ", " + Whole(second);
    }

    /// <summary>A code without a label leaves the whole list without a reading: half a list would mislead.</summary>
    private static string? CodeList(Rule rule, ExifValue value)
    {
        if (value.Count == 0 || value.FieldType is not (TiffFieldType.Short or TiffFieldType.Long or TiffFieldType.Byte))
            return null;
        var list = new StringBuilder();
        for (int i = 0; (uint)i < value.Count; i++)
        {
            if (!ExifTags.TryInteger(value, i, out long code))
                return null;
            if (code == 0)
                continue;
            if (Find(rule, code) is not string label)
                return null;
            if (list.Length != 0)
                list.Append(", ");
            list.Append(label);
        }
        return list.Length == 0 ? Find(rule, 0) : list.ToString();
    }

    private static string? Find(Rule rule, long number)
    {
        foreach ((long candidate, string label) in rule.Labels)
        {
            if (candidate == number)
                return label;
        }
        return null;
    }

    private static string? NikonFocusDistance(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Rational ||
            !ExifTags.TryRational(value, 0, out long numerator, out long denominator) || denominator == 0)
            return null;
        if (numerator == 0)
            return "Unknown";
        return ((double)numerator / denominator).ToString("F2", CultureInfo.InvariantCulture) + " m";
    }

    private static string? NikonDigitalZoom(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Rational ||
            !ExifTags.TryRational(value, 0, out long numerator, out long denominator) || denominator == 0)
            return null;
        if (numerator == 0)
            return "Not used";
        return ((double)numerator / denominator).ToString("F1", CultureInfo.InvariantCulture) + "x";
    }

    private static string? NikonAfFocusPosition(ExifValue value)
    {
        if (value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined) || value.Count < 2)
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        string[] areas = ["Single area", "Dynamic area", "Dynamic area, closest subject", "Group dynamic", "Single area (wide)", "Dynamic area (wide)"];
        string[] points = ["Center", "Top", "Bottom", "Left", "Right", "Upper-left", "Upper-right", "Lower-left", "Lower-right", "Left-most", "Right-most"];
        if (bytes[0] >= areas.Length || bytes[1] >= points.Length)
            return null;
        return areas[bytes[0]] + "; " + points[bytes[1]];
    }

    private static bool NikonLd3Byte(ExifValue value, out long number)
    {
        number = 0;
        return value.Count == 1 && value.FieldType == TiffFieldType.Byte && ExifTags.TryInteger(value, 0, out number);
    }

    private static string? NikonLd3Aperture(ExifValue value)
    {
        if (!NikonLd3Byte(value, out long number))
            return null;
        return number == 0 ? "n/a" : "F" + Math.Pow(2, number / 24.0).ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string? NikonLd3FocusDistance(ExifValue value)
    {
        if (!NikonLd3Byte(value, out long number))
            return null;
        return number == 0 ? "n/a" : (0.01 * Math.Pow(10, number / 40.0)).ToString("F2", CultureInfo.InvariantCulture) + " m";
    }

    private static string? NikonLd3FocalLength(ExifValue value)
    {
        if (!NikonLd3Byte(value, out long number))
            return null;
        return number == 0 ? "n/a" : (5 * Math.Pow(2, number / 24.0)).ToString("F1", CultureInfo.InvariantCulture) + " mm";
    }

    private static string? NikonLd3LensFStops(ExifValue value)
    {
        if (!NikonLd3Byte(value, out long number))
            return null;
        return "F" + (number / 12.0).ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string? NikonLd3ExitPupil(ExifValue value)
    {
        if (!NikonLd3Byte(value, out long number) || number == 0)
            return null;
        return (2048.0 / number).ToString("F1", CultureInfo.InvariantCulture) + " mm";
    }

    /// <summary>
    /// "18-250mm F3.5-6.3", or "50mm F1.4" for a lens with one focal length and one widest aperture.
    /// Each number has to be a decimal with at most two places; a lens described otherwise is not read.
    /// </summary>
    private static string? LensRange(ExifValue value)
    {
        if (value.Count != 4 || value.FieldType != TiffFieldType.Rational)
            return null;
        var numbers = new string[4];
        for (int i = 0; i < 4; i++)
        {
            if (!ExifTags.TryRational(value, i, out long numerator, out long denominator) || denominator <= 0 ||
                numerator > int.MaxValue || numerator * 100 % denominator != 0)
                return null;
            numbers[i] = Hundredths(numerator * 100 / denominator);
        }
        return (numbers[0] == numbers[1] ? numbers[0] : numbers[0] + "-" + numbers[1]) + "mm F" +
               (numbers[2] == numbers[3] ? numbers[2] : numbers[2] + "-" + numbers[3]);
    }

    private static string? HeightThenWidth(ExifValue value)
    {
        if (value.Count != 2 || value.FieldType is not (TiffFieldType.Long or TiffFieldType.Short) ||
            !ExifTags.TryInteger(value, 0, out long height) || !ExifTags.TryInteger(value, 1, out long width))
            return null;
        return Whole(width) + " x " + Whole(height);
    }

    private static string? CanonFileNumber(ExifValue value, string? cameraModel)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Long || !ExifTags.TryInteger(value, 0, out long stored))
            return null;
        uint number = unchecked((uint)stored);
        if (cameraModel is null)
            return $"({number.ToString(CultureInfo.InvariantCulture)})";

        bool firstLayout = cameraModel.Contains("20D", StringComparison.Ordinal) ||
            cameraModel.Contains("350D", StringComparison.Ordinal) || cameraModel.EndsWith("REBEL XT", StringComparison.Ordinal) ||
            cameraModel.Contains("Kiss Digital N", StringComparison.Ordinal);
        if (firstLayout)
        {
            uint directory = (number & 0x0000ffc0) >> 6;
            uint file = ((number >> 16) & 0xff) + ((number & 0x3f) << 8);
            return directory.ToString(CultureInfo.InvariantCulture) + "-" + file.ToString("D4", CultureInfo.InvariantCulture);
        }

        bool secondLayout = cameraModel.Contains("30D", StringComparison.Ordinal) ||
            cameraModel.Contains("400D", StringComparison.Ordinal) || cameraModel.Contains("REBEL XTi", StringComparison.Ordinal) ||
            cameraModel.Contains("Kiss Digital X", StringComparison.Ordinal) || cameraModel.Contains("K236", StringComparison.Ordinal);
        if (!secondLayout)
            return $"({number.ToString(CultureInfo.InvariantCulture)})";
        uint secondDirectory = (number & 0x000ffc00) >> 10;
        while (secondDirectory < 100)
            secondDirectory += 0x40;
        uint secondFile = ((number & 0x3ff) << 4) + ((number >> 20) & 0x0f);
        return secondDirectory.ToString(CultureInfo.InvariantCulture) + "-" + secondFile.ToString("D4", CultureInfo.InvariantCulture);
    }

    private static string? CanonBulbDuration(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long stored))
            return null;
        return (stored / 10).ToString(CultureInfo.InvariantCulture);
    }

    private static string? CanonPictureStyle(Rule rule, ExifValue value)
    {
        if (value.Count == 0 || value.FieldType is not (TiffFieldType.Short or TiffFieldType.SShort) || !ExifTags.TryInteger(value, 0, out long code))
            return null;
        string? label = rule.Labels.FirstOrDefault(label => label.Number == code).Label;
        return label ?? $"({code.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string? CanonSerialNumber(ExifValue value, MakerNote? note)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Long || note is null ||
            note.Entries.FirstOrDefault(entry => entry.Name == "ModelID") is not MakerNoteEntry model ||
            !ExifTags.TryInteger(model.Value, 0, out long modelId) || modelId != 0x01140000 ||
            !ExifTags.TryInteger(value, 0, out long serial))
            return null;
        uint number = unchecked((uint)serial);
        return ((number >> 16) & 0xffff).ToString("X4", CultureInfo.InvariantCulture) +
               (number & 0xffff).ToString("D5", CultureInfo.InvariantCulture);
    }

    private static string? CanonRaw(ExifValue value) =>
        value.Count == 1 && ExifTags.TryInteger(value, 0, out long number)
            ? $"({number.ToString(CultureInfo.InvariantCulture)})"
            : null;

    private static string? CanonAutoIso(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long code))
            return null;
        double ev = CanonEv(code);
        return CanonNumber(100.0 * Math.Pow(2.0, ev / 32.0));
    }

    private static string? CanonIso(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long code))
            return null;
        return CanonNumber(100.0 * Math.Pow(2.0, CanonEv(code)) / 32.0);
    }

    private static string? CanonMeasuredEv(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType is not (TiffFieldType.Short or TiffFieldType.SShort) ||
            !ExifTags.TryInteger(value, 0, out long stored))
            return null;
        double ev = (unchecked((short)stored) / 32.0) + 5.0;
        return (Math.Round(ev * 100.0, MidpointRounding.AwayFromZero) / 100.0).ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string? CanonMeasuredEv2(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long stored))
            return null;
        return (stored / 8.0 - 6.0).ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string? CanonAperture(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType is not (TiffFieldType.Short or TiffFieldType.SShort) ||
            !ExifTags.TryInteger(value, 0, out long stored))
            return null;
        double fNumber = Math.Pow(2.0, CanonEv(unchecked((short)stored)) / 2.0);
        if (Math.Abs(fNumber - 3.5) < 0.1) fNumber = 3.5;
        return "F" + CanonNumber(fNumber, "G2");
    }

    private static string? CanonShutter(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long stored))
            return null;
        double exposureDenominator = Math.Pow(2.0, CanonEv(stored));
        if (double.IsNaN(exposureDenominator)) return null;
        if (double.IsInfinity(exposureDenominator) || exposureDenominator <= 0) return "1 s";
        const double maxRationalPart = uint.MaxValue;
        if (exposureDenominator > 1)
        {
            double denominator = Math.Round(exposureDenominator, MidpointRounding.AwayFromZero);
            return denominator > maxRationalPart
                ? "1 s"
                : "1/" + denominator.ToString(CultureInfo.InvariantCulture) + " s";
        }
        double numerator = Math.Round(1.0 / exposureDenominator, MidpointRounding.AwayFromZero);
        return numerator > maxRationalPart ? "1 s" : numerator.ToString(CultureInfo.InvariantCulture) + " s";
    }

    private static string? CanonFlashGuideNumber(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long stored))
            return null;
        return stored == ushort.MaxValue ? "--" : (stored / 32).ToString(CultureInfo.InvariantCulture);
    }

    private static string? CanonAfPointUsed(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long stored))
            return null;
        long packed = (ushort)stored;
        int focusPoints = (int)((packed & 0xF000) >> 12);
        long mask = packed & 0x0FFF;
        if (mask == 0)
            return focusPoints.ToString(CultureInfo.InvariantCulture) + " focus points; none used";
        var labels = new List<string>();
        if ((mask & 0x0004) != 0) labels.Add("left");
        if ((mask & 0x0002) != 0) labels.Add("center");
        if ((mask & 0x0001) != 0) labels.Add("right");
        return focusPoints.ToString(CultureInfo.InvariantCulture) + " focus points; " + string.Join(", ", labels) + " used";
    }

    private static string? CanonLens(ExifValue value)
    {
        if (value.Count < 3 || value.FieldType != TiffFieldType.Short ||
            !ExifTags.TryInteger(value, 0, out long max) || !ExifTags.TryInteger(value, 1, out long min) ||
            !ExifTags.TryInteger(value, 2, out long units) || units == 0)
            return null;
        double first = (double)max / units;
        double second = (double)min / units;
        return first == second
            ? first.ToString("F1", CultureInfo.InvariantCulture) + " mm"
            : second.ToString("F1", CultureInfo.InvariantCulture) + " - " + first.ToString("F1", CultureInfo.InvariantCulture) + " mm";
    }

    private static string? CanonFocalLength(ExifValue value, MakerNote? note)
    {
        if (value.Count < 4 || value.FieldType != TiffFieldType.Short || note is null ||
            !ExifTags.TryInteger(value, 1, out long focal) ||
            note.Entries.FirstOrDefault(entry => entry.TagId == 0x0001)?.Fields.FirstOrDefault(field => field.Name == "Lens") is not MakerNoteField lens ||
            !ExifTags.TryInteger(lens.Value, 2, out long units) || units == 0)
            return null;
        return ((double)focal / units).ToString("F1", CultureInfo.InvariantCulture) + " mm";
    }

    private static string CanonNumber(double value, string format = "G6") =>
        double.IsPositiveInfinity(value) ? "inf"
        : double.IsNegativeInfinity(value) ? "-inf"
        : double.IsNaN(value) ? "nan"
        : value.ToString(format, CultureInfo.InvariantCulture).Replace('E', 'e');

    private static double CanonEv(long value)
    {
        long sign = value < 0 ? -1 : 1;
        long absolute = Math.Abs(value);
        long whole = absolute & ~31L;
        long remainder = absolute & 31;
        double fraction = remainder switch
        {
            12 => 32.0 / 3.0,
            20 => 64.0 / 3.0,
            8 when whole == 160 => 10.0,
            _ => remainder,
        };
        return sign * (whole + fraction) / 32.0;
    }

    private static string? BitMask(Rule rule, ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short ||
            !ExifTags.TryInteger(value, 0, out long mask) || mask == 0)
            return null;
        var labels = new List<string>();
        long known = 0;
        foreach ((long flag, string label) in rule.Labels)
        {
            known |= flag;
            if ((mask & flag) != 0)
                labels.Add(label);
        }
        return (mask & ~known) == 0 && labels.Count != 0 ? string.Join(", ", labels) : null;
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
