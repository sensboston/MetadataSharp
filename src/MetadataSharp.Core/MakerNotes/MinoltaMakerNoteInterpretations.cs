using System.Globalization;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>Corpus-backed renderings for Minolta/Konica Minolta camera settings.</summary>
internal static class MinoltaMakerNoteInterpretations
{
    private const int Standard = -1;
    private const int Settings7D = 0x0004;
    private const int Settings5D = 0x0114;

    // Decode known Minolta values by field name and retain the stored representation for unknown codes.
    internal static string? Interpret(int scope, string name, ExifValue value)
    {
        if (scope == Standard && name == "FlashExposureComp" && value.FieldType == TiffFieldType.SRational &&
            value.Count == 1 && ExifTags.TryRational(value, 0, out long numerator, out long denominator))
        {
            if (numerator == 0 || numerator == int.MinValue)
                return "0 EV";
            if (denominator <= 0)
                return "(" + numerator.ToString(CultureInfo.InvariantCulture) + "/" +
                       denominator.ToString(CultureInfo.InvariantCulture) + ")";
            long absoluteNumerator = Math.Abs(numerator);
            long divisor = GreatestCommonDivisor(absoluteNumerator, denominator);
            string sign = numerator < 0 ? "-" : "+";
            long reducedNumerator = absoluteNumerator / divisor;
            long reducedDenominator = denominator / divisor;
            return sign + reducedNumerator.ToString(CultureInfo.InvariantCulture) +
                   (reducedDenominator == 1 ? "" : "/" + reducedDenominator.ToString(CultureInfo.InvariantCulture)) + " EV";
        }

        if (!ExifTags.TryInteger(value, 0, out long number))
            return null;

        if (scope == Standard)
        {
            string? label = name switch
            {
                "SceneMode" => Lookup(number, (0, "Standard"), (1, "Portrait"), (2, "Text"), (3, "Night Scene"),
                    (4, "Sunset"), (5, "Sports"), (6, "Landscape"), (7, "Night Portrait"), (8, "Macro"),
                    (9, "Super Macro"), (16, "Auto"), (17, "Night View/Portrait"), (18, "Sweep Panorama"),
                    (19, "Handheld Night Shot"), (20, "Anti Motion Blur"), (21, "Cont. Priority AE"),
                    (22, "Auto+"), (23, "3D Sweep Panorama"), (24, "Superior Auto"), (25, "High Sensitivity"),
                    (26, "Fireworks"), (27, "Food"), (28, "Pet"), (33, "HDR"), (65535, "n/a")),
                "ColorMode" => Lookup(number, (0, "Natural Color"), (1, "Black & White"), (2, "Vivid Color"),
                    (3, "Solarization"), (4, "AdobeRGB"), (5, "Sepia"), (9, "Natural"), (12, "Portrait"),
                    (13, "Natural sRGB"), (14, "Natural+ sRGB"), (15, "Landscape"), (16, "Evening"),
                    (17, "Night Scene"), (18, "Night Portrait")),
                "Quality" => Lookup(number, (0, "Raw"), (1, "Super Fine"), (2, "Fine"), (3, "Standard"),
                    (4, "Economy"), (5, "Extra Fine")),
                "Teleconverter" => number == 0 ? "None" : null,
                "ImageStabilization" => Lookup(number, (1, "Off"), (5, "On")),
                "RawAndJpgRecording" or "ImageStabilizationA100" => Bool(number),
                "ZoneMatching" => Lookup(number, (0, "ISO Setting Used"), (1, "High Key"), (2, "Low Key")),
                "WhiteBalance" => Lookup(number, (0, "Auto"), (1, "Color Temperature/Color Filter"),
                    (16, "Daylight"), (32, "Cloudy"), (48, "Shade"), (64, "Tungsten"), (80, "Flash"),
                    (96, "Fluorescent"), (112, "Custom")),
                _ => null,
            };
            return label;
        }

        if (scope == Settings5D)
        {
            return name switch
            {
                "ExposureMode" => Lookup(number, (0, "Program"), (1, "Aperture priority"), (2, "Shutter priority"),
                    (3, "Manual"), (4, "Auto"), (5, "Program Shift A"), (6, "Program Shift S"),
                    (0x1013, "Portrait"), (0x1023, "Sports"), (0x1033, "Sunset"),
                    (0x1043, "Night View/Portrait"), (0x1053, "Landscape"), (0x1083, "Macro")),
                "ImageSize" => Lookup(number, (0, "Large"), (1, "Medium"), (2, "Small")),
                "Quality" => Lookup(number, (0, "Raw"), (16, "Fine"), (32, "Normal"), (34, "Raw+Jpeg"), (48, "Economy")),
                "WhiteBalance" => Lookup(number, (0, "Auto"), (1, "Daylight"), (2, "Cloudy"), (3, "Shade"),
                    (4, "Tungsten"), (5, "Fluorescent"), (6, "Flash"), (256, "Kelvin"), (512, "Manual")),
                "FocusPosition" => Lookup(number, (0, "Wide"), (1, "Central"), (2, "Up"), (3, "Up right"),
                    (4, "Right"), (5, "Down right"), (6, "Down"), (7, "Down left"), (8, "Left"), (9, "Up left")),
                "FocusArea" => Lookup(number, (0, "Wide"), (1, "Selection"), (2, "Spot")),
                "FlashFired" => Lookup(number, (0, "Did not fire"), (1, "Fired")),
                "MeteringMode" => Lookup(number, (0, "Multi-segment"), (1, "Center weighted"), (2, "Spot")),
                "ISOSpeed" => Lookup(number, (0, "Auto"), (1, "100"), (3, "200"), (4, "400"), (5, "800"),
                    (6, "1600"), (7, "3200"), (8, "200 (Zone Matching High)"), (10, "80 (Zone Matching Low)")),
                "ColorSpace" => Lookup(number, (0, "sRGB (Natural)"), (1, "sRGB (Natural+)"), (2, "Monochrome"),
                    (3, "Adobe RGB (ICC)"), (4, "Adobe RGB")),
                "FocusMode" => Lookup(number, (0, "Auto focus"), (1, "Manual focus")),
                "Rotation" => Lookup(number, (72, "Horizontal (normal)"), (76, "Rotate 90 CW"), (82, "Rotate 270 CW")),
                "Rotation2" => Lookup(number, (0, "Horizontal (normal)"), (1, "Rotate 90 CW"), (2, "Rotate 270 CW")),
                "PictureFinish" => Lookup(number, (0, "Natural"), (1, "Natural+"), (2, "Portrait"), (3, "Wind Scene"),
                    (4, "Evening Scene"), (5, "Night Scene"), (6, "Night Portrait"), (7, "Monochrome"),
                    (8, "Adobe RGB"), (9, "Adobe RGB (ICC)")),
                "ExposureCompensation" => ((double)(number - 300) / 100).ToString("0.00", CultureInfo.InvariantCulture),
                "ExposureManualBias" => ((double)(number - 128) / 24).ToString("0.00", CultureInfo.InvariantCulture),
                "AFMode" => Lookup(number, (0, "AF-A"), (1, "AF-S"), (2, "AF-D"), (3, "DMF")),
                "NoiseReduction" or "ImageStabilization" => Bool(number),
                _ => null,
            };
        }

        if (scope == Settings7D)
        {
            return name switch
            {
                "ExposureMode" => Lookup(number, (0, "Program"), (1, "Aperture priority"), (2, "Shutter priority"),
                    (3, "Manual"), (4, "Auto"), (5, "Program-shift A"), (6, "Program-shift S")),
                "ImageSize" => Lookup(number, (0, "Large"), (1, "Medium"), (2, "Small")),
                "Quality" => Lookup(number, (0, "Raw"), (16, "Fine"), (32, "Normal"), (34, "Raw+Jpeg"), (48, "Economy")),
                "WhiteBalance" => Lookup(number, (0, "Auto"), (1, "Daylight"), (2, "Shade"), (3, "Cloudy"),
                    (4, "Tungsten"), (5, "Fluorescent"), (256, "Kelvin"), (512, "Manual")),
                "FocusMode" => Lookup(number, (0, "Single-shot AF"), (1, "Continuous AF"), (3, "Manual"), (4, "Automatic AF")),
                "AFPoints" => Lookup(number, (1, "Center"), (2, "Top"), (4, "Top-right"), (8, "Right"),
                    (16, "Bottom-right"), (32, "Bottom"), (64, "Bottom-left"), (128, "Left"), (256, "Top-left")),
                "FlashFired" => Lookup(number, (0, "Did not fire"), (1, "Fired")),
                "ISOSpeed" => Lookup(number, (0, "Auto"), (1, "100"), (3, "200"), (4, "400"), (5, "800"),
                    (6, "1600"), (7, "3200")),
                "ColorSpace" => Lookup(number, (0, "sRGB (Natural)"), (1, "sRGB (Natural+)"), (4, "Adobe RGB")),
                "Rotation" => Lookup(number, (72, "Horizontal (normal)"), (76, "Rotate 90 CW"), (82, "Rotate 270 CW")),
                "NoiseReduction" or "ImageStabilization" or "ZoneMatchingOn" => Bool(number),
                _ => null,
            };
        }
        return null;
    }

    // Reduce rational camera values without overflowing on the sign of the operands.
    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
            (left, right) = (right, left % right);
        return left;
    }

    private static string? Bool(long number) => number switch { 0 => "Off", 1 => "On", _ => null };

    // Map a known numeric code to its label and return no interpretation for unknown codes.
    private static string? Lookup(long number, params (long Number, string Label)[] entries)
    {
        foreach ((long key, string label) in entries)
            if (key == number)
                return label;
        return null;
    }
}
