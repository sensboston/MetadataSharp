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
        NikonShootingMode,
        NikonFlashComp,
        Nikon3LensFStops,
        NikonLd4Aperture,
        NikonLd4FocalLength,
        NikonLd4FocusDistance,
        SonyExposureBias,
        SonyExposureStandardAdjustment,
        SonyAmbientTemperature,
        SonyMisc1Temperature,
        SonyFocusPosition,
        SonyAFAreaModeSetting,
        SonyFocusMode2,
        SonyFlexibleSpotPosition,
        SonyHighIso2,
        SonyAFTracking,
        SonyAFPointSelected,
        SonyAFPointsUsed,
        SonyFocalPlaneAFPointsUsed,
        SonyFocusMode3,
        SonyLensSpec,
        SonyMisc2bFocusPosition,
        SonyAFMode,
        SonyFocusMode,
        SonyQuality2,
        SonyModelId,
        SonyVariableLowPass,
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
        if (format is MakerNoteFormat.Sony1 or MakerNoteFormat.Sony2)
            cameraModel ??= SonyModelFromNote(note);
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
            case Form.NikonFlashComp:
                return NikonFlashComp(value);
            case Form.Nikon3LensFStops:
                return Nikon3LensFStops(value);
            case Form.NikonLd4Aperture:
                return NikonLd4Aperture(value);
            case Form.NikonLd4FocalLength:
                return NikonLd4FocalLength(value);
            case Form.NikonLd4FocusDistance:
                return NikonLd4FocusDistance(value);
            case Form.SonyExposureBias:
                return SonyExposureBias(value);
            case Form.SonyExposureStandardAdjustment:
                return SonyExposureStandardAdjustment(value);
            case Form.SonyAmbientTemperature:
                return SonyAmbientTemperature(value, note);
            case Form.SonyMisc1Temperature:
                return SonyMisc1Temperature(value, note);
            case Form.SonyFocusPosition:
                return SonyFocusPosition(value, cameraModel);
            case Form.SonyMisc2bFocusPosition:
                return SonyMisc2bFocusPosition(value, cameraModel);
            case Form.SonyAFMode:
                return SonyAFMode(value, note, cameraModel);
            case Form.SonyFocusMode:
                return SonyFocusMode(value, note);
            case Form.SonyQuality2:
                return SonyQuality2(value);
            case Form.SonyModelId:
                return SonyModelId(value);
            case Form.SonyVariableLowPass:
                return SonyVariableLowPass(value);
            case Form.SonyAFAreaModeSetting:
                return SonyAFAreaModeSetting(value, cameraModel);
            case Form.SonyFocusMode2:
                return SonyFocusMode2(value, cameraModel);
            case Form.SonyFlexibleSpotPosition:
                return SonyFlexibleSpotPosition(value, cameraModel);
            case Form.SonyHighIso2:
                return SonyHighIso2(value, cameraModel);
            case Form.SonyAFTracking:
                return SonyAFTracking(value, cameraModel);
            case Form.SonyAFPointSelected:
                return SonyAFPointSelected(value, cameraModel, note);
            case Form.SonyAFPointsUsed:
                return SonyAFPointsUsed(value, cameraModel);
            case Form.SonyFocalPlaneAFPointsUsed:
                return SonyFocalPlaneAFPointsUsed(value);
            case Form.SonyFocusMode3:
                return SonyFocusMode3(value, note);
            case Form.SonyLensSpec:
                return SonyLensSpec(value);
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
        if (rule.Reading == Form.NikonShootingMode)
            return number == 48 ? "Single-frame, Exposure bracketing, Unused LE-NR slowdown" : Stored(number);

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

    private static readonly (long Number, string Label)[] SonyModels =
    [
        (0, "Multiple camera models"),
        (2, "DSC-R1"),
        (256, "DSLR-A100"),
        (257, "DSLR-A900"),
        (258, "DSLR-A700"),
        (259, "DSLR-A200"),
        (260, "DSLR-A350"),
        (261, "DSLR-A300"),
        (262, "DSLR-A900 (APS-C mode)"),
        (263, "DSLR-A380 / DSLR-A390"),
        (264, "DSLR-A330"),
        (265, "DSLR-A230"),
        (266, "DSLR-A290"),
        (269, "DSLR-A850"),
        (270, "DSLR-A850 (APS-C mode)"),
        (273, "DSLR-A550"),
        (274, "DSLR-A500"),
        (275, "DSLR-A450"),
        (278, "NEX-5"),
        (279, "NEX-3"),
        (280, "SLT-A33"),
        (281, "SLT-A55 / SLT-A55V"),
        (282, "DSLR-A560"),
        (283, "DSLR-A580"),
        (284, "NEX-C3"),
        (285, "SLT-A35"),
        (286, "SLT-A65 / SLT-A65V"),
        (287, "SLT-A77 / SLT-A77V"),
        (288, "NEX-5N"),
        (289, "NEX-7"),
        (290, "NEX-VG20E"),
        (291, "SLT-A37"),
        (292, "SLT-A57"),
        (293, "NEX-F3"),
        (294, "SLT-A99 / SLT-A99V"),
        (295, "NEX-6"),
        (296, "NEX-5R"),
        (297, "DSC-RX100"),
        (298, "DSC-RX1"),
        (299, "NEX-VG900"),
        (300, "NEX-VG30E"),
        (302, "ILCE-3000 / ILCE-3500"),
        (303, "SLT-A58"),
        (305, "NEX-3N"),
        (306, "ILCE-7"),
        (307, "NEX-5T"),
        (308, "DSC-RX100M2"),
        (309, "DSC-RX10"),
        (310, "DSC-RX1R"),
        (311, "ILCE-7R"),
        (312, "ILCE-6000"),
        (313, "ILCE-5000"),
        (317, "DSC-RX100M3"),
        (318, "ILCE-7S"),
        (319, "ILCA-77M2"),
        (339, "ILCE-5100"),
        (340, "ILCE-7M2"),
        (341, "DSC-RX100M4"),
        (342, "DSC-RX10M2"),
        (344, "DSC-RX1RM2"),
        (346, "ILCE-QX1"),
        (347, "ILCE-7RM2"),
        (350, "ILCE-7SM2"),
        (353, "ILCA-68"),
        (354, "ILCA-99M2"),
        (355, "DSC-RX10M3"),
        (356, "DSC-RX100M5"),
        (357, "ILCE-6300"),
        (358, "ILCE-9"),
        (360, "ILCE-6500"),
        (362, "ILCE-7RM3"),
        (363, "ILCE-7M3"),
        (364, "DSC-RX0"),
        (365, "DSC-RX10M4"),
        (366, "DSC-RX100M6"),
        (367, "DSC-HX99"),
        (369, "DSC-RX100M5A"),
        (371, "ILCE-6400"),
        (372, "DSC-RX0M2"),
        (373, "DSC-HX95"),
        (374, "DSC-RX100M7"),
        (375, "ILCE-7RM4"),
        (376, "ILCE-9M2"),
        (378, "ILCE-6600"),
        (379, "ILCE-6100"),
        (380, "ZV-1"),
        (381, "ILCE-7C"),
        (382, "ZV-E10"),
        (383, "ILCE-7SM3"),
        (384, "ILCE-1"),
        (385, "ILME-FX3"),
        (386, "ILCE-7RM3A"),
        (387, "ILCE-7RM4A"),
        (388, "ILCE-7M4"),
        (389, "ZV-1F"),
        (390, "ILCE-7RM5"),
        (391, "ILME-FX30"),
        (392, "ILCE-9M3"),
        (393, "ZV-E1"),
        (394, "ILCE-6700"),
        (395, "ZV-1M2"),
        (396, "ILCE-7CR"),
        (397, "ILCE-7CM2"),
        (398, "ILX-LR1"),
        (399, "ZV-E10M2"),
        (400, "ILCE-1M2"),
        (401, "DSC-RX1RM3"),
        (402, "ILCE-6400A"),
        (403, "ILCE-6100A"),
        (404, "DSC-RX100M7A"),
        (406, "ILME-FX2"),
        (407, "ILCE-7M5"),
        (408, "ZV-1A"),
        (410, "ILCE-7RM6"),
        (411, "DSC-RX10M5"),
    ];

    private static string? SonyModelId(ExifValue value)
    {
        long? number = SonyInteger(value);
        if (number is null || value.FieldType != TiffFieldType.Short)
            return null;
        return SonyModels.FirstOrDefault(model => model.Number == number).Label ?? Stored(number.Value);
    }

    private static string? SonyModelFromNote(MakerNote? note)
    {
        long? number = SonyTopInteger(note, "SonyModelID");
        if (number is null)
            return null;
        string? label = SonyModels.FirstOrDefault(model => model.Number == number).Label;
        return label is not null && !label.Contains(' ') ? label : null;
    }

    private static ExifValue? SonyField(MakerNote? note, ushort entryTag, string fieldName)
    {
        if (note is null || !note.TryGetEntry(entryTag, out MakerNoteEntry? entry))
            return null;
        foreach (MakerNoteField field in entry.Fields)
            if (field.Name == fieldName)
                return field.Value;
        return null;
    }

    private static long? SonyInteger(ExifValue? value)
    {
        if (value is null || value.Count != 1 || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        return number;
    }

    private static string? SonyExposureBias(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.SRational ||
            !ExifTags.TryRational(value, 0, out long numerator, out long denominator))
            return null;
        if (numerator == 0 || numerator == int.MinValue)
            return "0 EV";
        if (denominator <= 0)
            return "(" + Whole(numerator) + "/" + Whole(denominator) + ")";
        long gcd = Gcd(Math.Abs(numerator), denominator);
        long reducedNumerator = Math.Abs(numerator) / gcd;
        long reducedDenominator = denominator / gcd;
        return (numerator < 0 ? "-" : "+") + Whole(reducedNumerator) +
               (reducedDenominator == 1 ? "" : "/" + Whole(reducedDenominator)) + " EV";
    }

    private static string? SonyExposureStandardAdjustment(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.SRational ||
            !ExifTags.TryRational(value, 0, out long numerator, out long denominator) || denominator == 0)
            return null;
        return ((decimal)numerator / denominator).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static long Gcd(long left, long right)
    {
        while (right != 0)
            (left, right) = (right, left % right);
        return left;
    }

    private static byte? SonyArrayByte(MakerNote? note, ushort tag, int offset)
    {
        if (note is null)
            return null;
        foreach (MakerNoteEntry entry in note.Entries)
        {
            if (entry.TagId != tag || entry.Value.RawBytes.Length <= offset)
                continue;
            byte[] plain = SonyMakerNoteArrays.Plain(entry.Value.RawBytes);
            return plain[offset];
        }
        return null;
    }

    private static string? SonyAmbientTemperature(ExifValue value, MakerNote? note)
    {
        byte? marker = SonyArrayByte(note, 0x9402, 2);
        if (value.Count != 1 || value.FieldType != TiffFieldType.SByte || !ExifTags.TryInteger(value, 0, out long temperature))
            return null;
        return marker == 255 ? Whole(unchecked((byte)temperature)) + " \u00B0C" : "n/a";
    }

    private static string? SonyMisc1Temperature(ExifValue value, MakerNote? note)
    {
        byte? marker = SonyArrayByte(note, 0x9403, 4);
        if (value.Count != 1 || value.FieldType != TiffFieldType.SByte || !ExifTags.TryInteger(value, 0, out long temperature))
            return null;
        return marker is > 0 and < 100 ? Whole(temperature) + " \u00B0C" : "n/a";
    }

    private static bool SonyModelIs(string? model, params string[] prefixes) =>
        model is not null && prefixes.Any(prefix => model.StartsWith(prefix, StringComparison.Ordinal));

    private static string? SonyFocusPosition(ExifValue value, string? model)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Byte || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        if (model is null)
            return Stored(number);
        if (SonyModelIs(model, "DSC-", "Stellar"))
            return "n/a";
        return number == 255 ? "Infinity" : Whole(number);
    }

    private static string? SonyMisc2bFocusPosition(ExifValue value, string? model)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Byte || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        if (model is null)
            return Stored(number);
        return SonyModelIs(model, "SLT-", "HV", "ILCA-") ? "n/a" : Whole(number);
    }

    private static string? SonyAFAreaModeSetting(ExifValue value, string? model)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Byte || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        if (model is null)
            return Stored(number);
        (string Prefix, (long Number, string Label)[] Labels)[] sets =
        [
            ("SLT-", [(0, "Wide"), (4, "Local"), (8, "Zone"), (9, "Spot")]),
            ("HV", [(0, "Wide"), (4, "Local"), (8, "Zone"), (9, "Spot")]),
            ("ILCA-", [(0, "Wide"), (4, "Flexible spot"), (8, "Zone"), (9, "Center"), (12, "Expanded flexible spot")]),
            ("NEX-", [(0, "Wide"), (1, "Center"), (3, "Flexible Spot"), (4, "Flexible Spot (LA-EA4)"), (9, "Center (LA-EA4)"), (11, "Zone"), (12, "Expanded flexible spot")]),
            ("ILCE-", [(0, "Wide"), (1, "Center"), (3, "Flexible Spot"), (4, "Flexible Spot (LA-EA4)"), (9, "Center (LA-EA4)"), (11, "Zone"), (12, "Expanded flexible spot")]),
            ("ILME-", [(0, "Wide"), (1, "Center"), (3, "Flexible Spot"), (4, "Flexible Spot (LA-EA4)"), (9, "Center (LA-EA4)"), (11, "Zone"), (12, "Expanded flexible spot")]),
            ("DSC-RX10M4", [(0, "Wide"), (1, "Center"), (3, "Flexible Spot"), (4, "Flexible Spot (LA-EA4)"), (9, "Center (LA-EA4)"), (11, "Zone"), (12, "Expanded flexible spot")]),
            ("DSC-RX100M6", [(0, "Wide"), (1, "Center"), (3, "Flexible Spot"), (4, "Flexible Spot (LA-EA4)"), (9, "Center (LA-EA4)"), (11, "Zone"), (12, "Expanded flexible spot")]),
            ("DSC-RX100M7", [(0, "Wide"), (1, "Center"), (3, "Flexible Spot"), (4, "Flexible Spot (LA-EA4)"), (9, "Center (LA-EA4)"), (11, "Zone"), (12, "Expanded flexible spot")]),
            ("DSC-RX100M5A", [(0, "Wide"), (1, "Center"), (3, "Flexible Spot"), (4, "Flexible Spot (LA-EA4)"), (9, "Center (LA-EA4)"), (11, "Zone"), (12, "Expanded flexible spot")]),
            ("DSC-HX99", [(0, "Wide"), (1, "Center"), (3, "Flexible Spot"), (4, "Flexible Spot (LA-EA4)"), (9, "Center (LA-EA4)"), (11, "Zone"), (12, "Expanded flexible spot")]),
            ("DSC-RX0M2", [(0, "Wide"), (1, "Center"), (3, "Flexible Spot"), (4, "Flexible Spot (LA-EA4)"), (9, "Center (LA-EA4)"), (11, "Zone"), (12, "Expanded flexible spot")]),
        ];
        foreach ((string prefix, (long Number, string Label)[] labels) in sets)
            if (model.StartsWith(prefix, StringComparison.Ordinal))
                return labels.FirstOrDefault(label => label.Number == number).Label ?? Stored(number);
        return "n/a";
    }

    private static string? SonyFocusMode2(ExifValue value, string? model)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Byte || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        if (model is null)
            return Stored(number);
        if (model.StartsWith("DSC-", StringComparison.Ordinal) &&
            !SonyModelIs(model, "DSC-RX10M4", "DSC-RX100M6", "DSC-RX100M7", "DSC-RX100M5A", "DSC-HX99", "DSC-RX0M2"))
            return "n/a";
        return number switch { 0 => "Manual", 2 => "AF-S", 3 => "AF-C", 4 => "AF-A", 6 => "DMF", 7 => "AF-D", _ => Stored(number) };
    }

    private static string? SonyFlexibleSpotPosition(ExifValue value, string? model)
    {
        if (value.Count != 2 || value.FieldType != TiffFieldType.Short ||
            !ExifTags.TryInteger(value, 0, out long x) || !ExifTags.TryInteger(value, 1, out long y))
            return null;
        if (model is null)
            return "(" + Whole(x) + " " + Whole(y) + ")";
        if (SonyModelIs(model, "NEX-", "ILCE-", "ILME-", "DSC-RX10M4", "DSC-RX100M6", "DSC-RX100M7", "DSC-RX100M5A", "DSC-HX99", "DSC-RX0M2"))
            return Whole(x) + ", " + Whole(y);
        return "n/a";
    }

    private static string? SonyHighIso2(ExifValue value, string? model)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        if (model is null)
            return Stored(number);
        if (!SonyModelIs(model, "DSC-", "Stellar"))
            return "n/a";
        return number switch { 0 => "Normal", 1 => "High", 2 => "Low", 3 => "Off", 65535 => "n/a", _ => Stored(number) };
    }

    private static string? SonyAFTracking(ExifValue value, string? model)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Byte || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        if (model is null)
            return Stored(number);
        if (model.StartsWith("DSC-", StringComparison.Ordinal) &&
            !SonyModelIs(model, "DSC-RX10M4", "DSC-RX100M6", "DSC-RX100M7", "DSC-RX100M5A", "DSC-HX99", "DSC-RX0M2"))
            return "n/a";
        return number switch { 0 => "Off", 1 => "Face tracking", 2 => "Lock on AF", _ => Stored(number) };
    }

    private static long? SonyTopInteger(MakerNote? note, string name)
    {
        if (note is null)
            return null;
        foreach (MakerNoteEntry entry in note.Entries)
            if (entry.Name == name)
                return SonyInteger(entry.Value);
        return null;
    }

    private static string? SonyAFPointSelected(ExifValue value, string? model, MakerNote? note)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Byte || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        if (model is null)
            return Stored(number);
        long? area = SonyTopInteger(note, "AFAreaModeSetting");
        if (SonyModelIs(model, "SLT-", "HV-"))
            return SonyAfPointSet1(number);
        if (SonyModelIs(model, "ILCE-", "ILME-"))
            return area == 4 ? SonyAfPointSet1(number) : SonyAfPointSet5(number);
        if (SonyModelIs(model, "ILCA-68", "ILCA-77M2"))
            return area is null ? "n/a" : area == 8 ? SonyAfPointSet4(number) : SonyAfPointSet2(number);
        if (model.StartsWith("ILCA-99M2", StringComparison.Ordinal))
            return area is null ? "n/a" : area == 8 ? SonyAfPointSet4(number) : SonyAfPointSet3(number);
        if (SonyModelIs(model, "ILCA-"))
            return area == 8 ? SonyAfPointSet4(number) : "n/a";
        if (SonyModelIs(model, "NEX-"))
            return SonyAfPointSet5(number);
        return "n/a";
    }

    private static string SonyAfPointSet1(long number) => number switch
    {
        0 => "Auto", 1 => "Center", 2 => "Top", 3 => "Upper-right", 4 => "Right", 5 => "Lower-right",
        6 => "Bottom", 7 => "Lower-left", 8 => "Left", 9 => "Upper-left", 10 => "Far Right", 11 => "Far Left",
        12 => "Upper-middle", 13 => "Near Right", 14 => "Lower-middle", 15 => "Near Left", 16 => "Upper Far Right",
        17 => "Lower Far Right", 18 => "Lower Far Left", 19 => "Upper Far Left", _ => Stored(number),
    };

    private static string SonyAfPointSet2(long number)
    {
        string[] labels = ["Auto", "A6", "A7", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10",
            "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10", "C11", "D1", "D2", "D3",
            "D4", "D5", "D6", "D7", "D8", "D9", "D10", "D11", "E1", "E2", "E3", "E4", "E5", "E6 (Center)",
            "E7", "E8", "E9", "E10", "E11", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10",
            "F11", "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8", "G9", "G10", "G11", "H2", "H3", "H4",
            "H5", "H6", "H7", "H8", "H9", "H10", "I5", "I6", "I7"];
        return number == 128 ? "Auto" : number >= 0 && number < labels.Length ? labels[number] : Stored(number);
    }

    private static string SonyAfPointSet3(long number) => number switch
    {
        0 => "Auto", 93 => "A5", 94 => "A6", 95 => "A7", 106 => "B2", 107 => "B3", 108 => "B4", 110 => "B5",
        111 => "B6", 112 => "B7", 114 => "B8", 115 => "B9", 116 => "B10", 122 => "C1", 123 => "C2", 124 => "C3",
        215 => "C4", 127 => "C5", 128 => "C6", 129 => "C7", 131 => "C8", 132 => "C9", 133 => "C10", 134 => "C11",
        139 => "D1", 140 => "D2", 141 => "D3", 142 => "D4", 144 => "D5", 145 => "D6", 146 => "D7", 148 => "D8",
        149 => "D9", 150 => "D10", 151 => "D11", 156 => "E1", 157 => "E2", 158 => "E3", 159 => "E4", 161 => "E5",
        162 => "E6 (Center", 163 => "E7", 165 => "E8", 166 => "E9", 167 => "E10", 168 => "E11", 173 => "F1",
        174 => "F2", 175 => "F3", 176 => "F4", 178 => "F5", 179 => "F6", 180 => "F7", 182 => "F8", 183 => "F9",
        184 => "F10", 185 => "F11", 190 => "G1", 191 => "G2", 192 => "G3", 193 => "G4", 195 => "G5", 196 => "G6",
        197 => "G7", 199 => "G8", 200 => "G9", 201 => "G10", 202 => "G11", 208 => "H2", 209 => "H3", 210 => "H4",
        212 => "H5", 213 => "H6", 214 => "H7", 216 => "H8", 217 => "H9", 218 => "H10", 229 => "I5", 230 => "I6",
        231 => "I7", _ => Stored(number),
    };

    private static string SonyAfPointSet4(long number) => number switch
    {
        0 => "n/a", 1 => "Top Left Zone", 2 => "Top Zone", 3 => "Top Right Zone", 4 => "Left Zone", 5 => "Center Zone",
        6 => "Right Zone", 7 => "Bottom Left Zone", 8 => "Bottom Zone", 9 => "Bottom Right Zone", _ => Stored(number),
    };

    private static string SonyAfPointSet5(long number) => number switch
    {
        0 => "n/a", 1 => "Center Zone", 2 => "Top Zone", 3 => "Right Zone", 4 => "Left Zone", 5 => "Bottom Zone",
        6 => "Bottom Right Zone", 7 => "Bottom Left Zone", 8 => "Top Left Zone", 9 => "Top Right Zone", _ => Stored(number),
    };

    private static string? SonyAFPointsUsed(ExifValue value, string? model)
    {
        if (value.FieldType != TiffFieldType.Byte || value.Count == 0)
            return null;
        if (model is null)
            return "(" + string.Join(" ", value.RawBytes.ToArray().Select(item => Whole(item))) + ")";
        bool set2 = SonyModelIs(model, "ILCA-68", "ILCA-77M2");
        if (!set2 && SonyModelIs(model, "ILCA-", "DSC-"))
            return "n/a";
        string[] names1 = ["Center", "Top", "Upper-right", "Right", "Lower-right", "Bottom", "Lower-left", "Left", "Upper-left", "Far right", "Far left", "Upper-middle", "Near right", "Lower-middle", "Near left", "Upper far right", "Lower far right", "Lower far left", "Upper far left"];
        string[] names2 = ["A5", "A6", "A7", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10", "C11", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D10", "D11", "E1", "E2", "E3", "E4", "E5", "E6", "E7", "E8", "E9", "E10", "E11", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8", "G9", "G10", "G11", "H2", "H3", "H4", "H5", "H6", "H7", "H8", "H9", "H10", "I5", "I6", "I7"];
        var found = new List<string>();
        for (int i = 0; i < value.Count; i++)
        {
            long bits = value.RawBytes[i];
            for (int bit = 0; bit < 8; bit++)
            {
                if ((bits & (1 << bit)) == 0)
                    continue;
                int index = i * 8 + bit;
                if (set2 && index < names2.Length)
                    found.Add(names2[index]);
                else if (!set2 && index < names1.Length)
                    found.Add(names1[index]);
                else
                    found.Add("[" + Whole(index) + "]");
            }
        }
        return found.Count == 0 ? "None" : string.Join(", ", found);
    }

    private static string? SonyFocalPlaneAFPointsUsed(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Byte || value.Count == 0)
            return null;
        return value.RawBytes[0] == 0 ? "None" : "(" + string.Join(" ", value.RawBytes.ToArray().Select(item => Whole(item))) + ")";
    }

    private static string? SonyMetaVersion(MakerNote? note)
    {
        if (SonyField(note, 0x3000, "MetaVersion") is not ExifValue versionValue || versionValue.FieldType != TiffFieldType.Ascii)
            return null;
        return Encoding.Latin1.GetString(versionValue.RawBytes).TrimEnd('\0', ' ');
    }

    private static string? SonyQuality2(ExifValue value)
    {
        if (value.Count != 2 || value.FieldType != TiffFieldType.Short ||
            !ExifTags.TryInteger(value, 0, out long first) || !ExifTags.TryInteger(value, 1, out long second))
            return null;
        string[] labels = ["n/a", "Standard", "Fine", "Extra fine", "Light"];
        if (first == 0 && second >= 0 && second < labels.Length)
            return labels[second];
        if (first is >= 1 and <= 5 && second is >= 0 and <= 4)
        {
            string[] suffix = ["", "+ standard", "+ fine", "+ extra fine", "+ light"];
            string prefix = first switch
            {
                1 => "RAW", 2 => "S-size RAW", 3 => "M-size RAW", 4 => "Compressed RAW", _ => "Compressed (HQ) RAW",
            };
            return second == 0 ? prefix : prefix + " " + suffix[second];
        }
        return "(" + Whole(first) + " " + Whole(second) + ")";
    }

    private static string? SonyVariableLowPass(ExifValue value)
    {
        if (value.Count != 2 || value.FieldType != TiffFieldType.Short ||
            !ExifTags.TryInteger(value, 0, out long enabled) || !ExifTags.TryInteger(value, 1, out long level))
            return null;
        return (enabled, level) switch
        {
            (0, 0) or (65535, 65535) => "n/a", (1, 0) => "Off", (1, 1) => "Standard", (1, 2) => "High",
            _ => "(" + Whole(enabled) + " " + Whole(level) + ")",
        };
    }

    private static string? SonyFocusMode(ExifValue value, MakerNote? note)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        string? version = SonyMetaVersion(note);
        if (number == 65535)
            return "n/a";
        if (version is null)
            return Stored(number);
        if (version == "DC7303320222000")
            return "n/a";
        return number switch { 1 => "AF-S", 2 => "AF-C", 4 => "Permanent-AF", 65535 => "n/a", _ => Stored(number) };
    }

    private static string? SonyAFMode(ExifValue value, MakerNote? note, string? model)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        string? version = SonyMetaVersion(note);
        if (version is null)
            return number switch { 0 => "Default", 1 => "Multi", 2 => "Center", 3 => "Spot", 4 => "Flexible Spot", 6 => "Touch", 14 => "Tracking", 15 => "Face Detected", 65535 => "n/a", _ => Stored(number) };
        if (version != "DC7303320222000")
            return number switch { 0 => "Default", 1 => "Multi", 2 => "Center", 3 => "Spot", 4 => "Flexible Spot", 6 => "Touch", 14 => "Tracking", 15 => "Face Detected", 65535 => "n/a", _ => Stored(number) };
        long? focusMode = SonyTopInteger(note, "FocusMode2");
        if (focusMode is null || focusMode == 0 || SonyFocusMode2Value(focusMode.Value, model) == "n/a")
            return "n/a";
        return number switch { 0 => "Multi", 1 => "Center", 2 => "Spot", 3 => "Flexible spot", 10 => "Selective (for miniature effect)", 14 => "Tracking", 15 => "Face tracking", 255 => "Manual", _ => Stored(number) };
    }

    private static string SonyFocusMode2Value(long number, string? model)
    {
        if (model is null || model.StartsWith("DSC-", StringComparison.Ordinal) &&
            !SonyModelIs(model, "DSC-RX10M4", "DSC-RX100M6", "DSC-RX100M7", "DSC-RX100M5A", "DSC-HX99", "DSC-RX0M2"))
            return "n/a";
        return number switch { 0 => "Manual", 2 => "AF-S", 3 => "AF-C", 4 => "AF-A", 6 => "DMF", 7 => "AF-D", _ => Stored(number) };
    }

    private static string? SonyFocusMode3(ExifValue value, MakerNote? note)
    {
        string? version = SonyMetaVersion(note);
        if (version != "DC7303320222000")
            return "n/a";
        if (value.Count != 1 || value.FieldType != TiffFieldType.Short || !ExifTags.TryInteger(value, 0, out long number))
            return null;
        return number switch { 0 => "Manual", 2 => "AF-S", 3 => "AF-C", 5 => "Semi-manual", 6 => "DMF", _ => Stored(number) };
    }

    private static string? SonyLensSpec(ExifValue value)
    {
        if (value.Count != 8 || value.FieldType != TiffFieldType.Byte)
            return null;
        ReadOnlySpan<byte> b = value.RawBytes;
        if (b.Length != 8 || (b.ToArray().All(x => x == 0)) || b[2] == 0 || b[5] == 0)
            return "Unknown";
        int joined = b[0] << 8 | b[7];
        string start = "", end = "";
        void AddStart(string label) => start = start.Length == 0 ? label : label + " " + start;
        void AddEnd(string label) => end = end.Length == 0 ? label : end + " " + label;
        if ((joined & 0x4000) != 0) AddStart("PZ");
        int type = joined & 0x0300;
        if (type != 0) AddStart(type == 0x0100 ? "DT" : type == 0x0200 ? "FE" : "E");
        if ((joined & 0x00e0) != 0) AddEnd((joined & 0x00e0) switch { 0x20 => "STF", 0x40 => "Reflex", 0x60 => "Macro", _ => "Fisheye" });
        if ((joined & 0x000c) != 0) AddEnd((joined & 0x000c) == 4 ? "ZA" : "G");
        if ((joined & 0x0003) != 0) AddEnd((joined & 0x0003) == 1 ? "SSM" : "SAM");
        if ((joined & 0x8000) != 0) AddEnd("OSS");
        if ((joined & 0x2000) != 0) AddEnd("LE");
        if ((joined & 0x0800) != 0) AddEnd("II");
        string focal = NibblePair(b[2]);
        string maxFocal = b[3] == 0 && b[4] == 0 ? "" : "-" + (b[3] == 0 ? "" : NibblePair(b[3])) + NibblePairPadded(b[4]);
        string result = (start.Length == 0 ? "" : start + " ") + focal + maxFocal + "mm F" + Aperture(b[5]);
        if (b[6] != 0) result += "-" + Aperture(b[6]);
        return result + (end.Length == 0 ? "" : " " + end);
    }

    private static string NibblePair(byte value) =>
        (value >> 4 == 0 ? "" : (value >> 4).ToString(CultureInfo.InvariantCulture)) +
        (value & 15).ToString(CultureInfo.InvariantCulture);

    private static string NibblePairPadded(byte value) =>
        ((value >> 4) & 15).ToString(CultureInfo.InvariantCulture) + (value & 15).ToString(CultureInfo.InvariantCulture);

    private static string Aperture(byte value) =>
        ((value >> 4) & 15).ToString(CultureInfo.InvariantCulture) + "." + (value & 15).ToString(CultureInfo.InvariantCulture);

    private static string? NikonFocusDistance(ExifValue value)
    {
        if (value.Count != 1 || value.FieldType != TiffFieldType.Rational ||
            !ExifTags.TryRational(value, 0, out long numerator, out long denominator))
            return null;
        if (numerator == 0)
            return "Unknown";
        if (denominator == 0)
            return null;
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

    private static string? NikonFlashComp(ExifValue value)
    {
        if (value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined) || value.Count == 0 || value.RawBytes.IsEmpty)
            return null;
        long number = value.RawBytes[0];
        return number switch
        {
            0x06 => "+1.0 EV", 0x04 => "+0.7 EV", 0x03 => "+0.5 EV", 0x02 => "+0.3 EV",
            0x00 => "0.0 EV", 0xFE => "-0.3 EV", 0xFD => "-0.5 EV", 0xFC => "-0.7 EV",
            0xFA => "-1.0 EV", 0xF8 => "-1.3 EV", 0xF7 => "-1.5 EV", 0xF6 => "-1.7 EV",
            0xF4 => "-2.0 EV", 0xF2 => "-2.3 EV", 0xF1 => "-2.5 EV", 0xF0 => "-2.7 EV",
            0xEE => "-3.0 EV", _ => Stored(number),
        };
    }

    private static string? Nikon3LensFStops(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Undefined || value.Count != 4 || value.RawBytes.Length != 4)
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        if (bytes[2] == 0)
            return null;
        float fstops = (float)bytes[0] * bytes[1] / bytes[2];
        return fstops.ToString("G6", CultureInfo.InvariantCulture);
    }

    private static bool NikonLd4Short(ExifValue value, out long number)
    {
        number = 0;
        return value.Count == 1 && value.FieldType == TiffFieldType.Short && ExifTags.TryInteger(value, 0, out number);
    }

    private static string? NikonLd4Aperture(ExifValue value)
    {
        if (!NikonLd4Short(value, out long number))
            return null;
        return number == 0 ? "n/a" : "F" + Math.Pow(2, number / 384.0 - 1).ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string? NikonLd4FocalLength(ExifValue value)
    {
        if (!NikonLd4Short(value, out long number))
            return null;
        return number == 0 ? "n/a" : number.ToString("F1", CultureInfo.InvariantCulture) + " mm";
    }

    private static string? NikonLd4FocusDistance(ExifValue value)
    {
        if (!NikonLd4Short(value, out long number))
            return null;
        return number == 0 ? "n/a" : (0.01 * Math.Pow(10, number / 10240.0)).ToString("F2", CultureInfo.InvariantCulture) + " m";
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
