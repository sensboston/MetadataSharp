using System.Globalization;
using System.Text;
using MetadataSharp.Core;

namespace MetadataSharp.Core.MakerNotes;

internal static partial class MakerNoteLabels
{
    // Hand-authored display rules for Pentax's AOC and DNG layouts. Their shared fields use the same
    // renderings; LensType deliberately has no rule because Exiv2 resolves it through an external
    // lens-name database that this project does not reproduce.
    private static readonly Rule[] PentaxDng =
    [
        new(Top, "Mode", [(0, "Auto"), (1, "Night-Scene"), (2, "Manual")], Form.PentaxFirst),
        new(Top, "ModelID", [(0x13222, "K-70"), (0x12b9c, "K100D"), (0x12c1e, "K10D"),
            (0x12cd2, "K20D"), (0x12f52, "K-30"), (0x12fc0, "K-3"), (0x12b1a, "*ist DL")], Form.PentaxFirst),
        new(Top, "Version", [], Form.PentaxVersion),
        new(Top, "PreviewResolution", [], Form.PentaxResolution),
        new(Top, "Date", [], Form.PentaxDate),
        new(Top, "Time", [], Form.PentaxTime),
        new(Top, "Quality", [(0, "Good"), (1, "Better"), (2, "Best"), (3, "TIFF"), (4, "RAW"), (5, "Premium"), (65535, "n/a")], Form.PentaxFirst),
        new(Top, "Size", [(0, "640x480"), (1, "Full"), (2, "1024x768"), (3, "1280x960"), (4, "1600x1200"),
            (5, "2048x1536"), (8, "2560x1920 or 2304x1728"), (9, "3072x2304"), (10, "3264x2448"),
            (19, "320x240"), (20, "2288x1712"), (21, "2592x1944"), (22, "2304x1728 or 2592x1944"),
            (23, "3056x2296"), (25, "2816x2212 or 2816x2112"), (27, "3648x2736"), (29, "4000x3000"),
            (30, "4288x3216"), (31, "4608x3456"), (129, "1920x1080"), (135, "4608x2592"), (257, "3216x3216")], Form.PentaxFirst),
        new(Top, "Flash", [(0, "Auto, Did not fire"), (1, "Off, Did not fire"), (2, "Off, Did not fire"),
            (3, "Auto, Did not fire, Red-eye reduction"), (5, "On. Did not fire. Wireless (Master)"),
            (256, "Auto, Fired"), (258, "On, Fired"), (259, "Auto, Fired, Red-eye reduction"),
            (260, "On, Red-eye reduction"), (261, "On, Wireless (Master)"), (262, "On, Wireless (Control)"),
            (264, "On, Soft"), (265, "On, Slow-sync"), (266, "On, Slow-sync, Red-eye reduction"),
            (267, "On, Trailing-curtain Sync")], Form.PentaxFirst),
        new(Top, "Focus", [(0, "Normal"), (1, "Macro"), (2, "Infinity"), (3, "Manual"), (4, "Super Macro"),
            (5, "Pan Focus"), (16, "AF-S"), (17, "AF-C"), (18, "AF-A"), (32, "Contrast-detect"),
            (33, "Tracking Contrast-detect"), (288, "Face Detect")], Form.PentaxFirst),
        new(Top, "AFPoint", [(65535, "Auto"), (65534, "Fixed Center"), (65533, "Automatic Tracking AF"),
            (65532, "Face Recognition AF"), (65531, "AF Select"), (0, "None"), (1, "Upper-left"), (2, "Top"),
            (3, "Upper-right"), (4, "Left"), (5, "Mid-left"), (6, "Center"), (7, "Mid-right"), (8, "Right"),
            (9, "Lower-left"), (10, "Bottom"), (11, "Lower-right")], Form.PentaxFirst),
        new(Top, "AFPointInFocus", [(65535, "None"), (0, "Fixed Center or multiple"), (1, "Top-left"),
            (2, "Top-center"), (3, "Top-right"), (4, "Left"), (5, "Center"), (6, "Right"),
            (7, "Bottom-left"), (8, "Bottom-center"), (9, "Bottom-right")], Form.PentaxFirst),
        new(Top, "ExposureTime", [], Form.PentaxExposure),
        new(Top, "FNumber", [], Form.PentaxFNumber),
        new(Top, "ISO", [(3, "50"), (4, "64"), (5, "80"), (6, "100"), (7, "125"), (8, "160"), (9, "200"),
            (10, "250"), (11, "320"), (12, "400"), (13, "500"), (14, "640"), (15, "800"), (16, "1000"),
            (17, "1250"), (18, "1600"), (19, "2000"), (20, "2500"), (21, "3200"), (22, "4000"),
            (23, "5000"), (24, "6400"), (25, "8000"), (26, "10000"), (27, "12800"), (28, "16000"),
            (29, "20000"), (30, "25600"), (31, "32000"), (32, "40000"), (33, "51200"), (34, "64000"),
            (35, "80000"), (36, "102400"), (37, "128000"), (38, "160000"), (39, "204800"), (40, "256000"),
            (41, "320000"), (42, "409600"), (43, "512000"), (44, "640000"), (45, "819200"),
            (50, "50"), (100, "100"), (200, "200"), (258, "50"), (259, "70"), (260, "100"), (261, "140"),
            (262, "200"), (263, "280"), (264, "400"), (265, "560"), (266, "800"), (267, "1100"),
            (268, "1600"), (269, "2200"), (270, "3200"), (271, "4500"), (272, "6400"), (273, "9000"),
            (274, "12800"), (275, "18000"), (276, "25600"), (277, "36000"), (278, "51200"), (279, "72000"),
            (280, "102400"), (281, "144000"), (282, "204800"), (283, "288000"), (284, "409600"),
            (285, "576000"), (286, "819200")], Form.PentaxFirst),
        new(Top, "ExposureCompensation", [], Form.PentaxExposureCompensation),
        new(Top, "MeteringMode", [(0, "Multi Segment"), (1, "Center Weighted"), (2, "Spot")], Form.PentaxFirst),
        new(Top, "AutoBracketing", [], Form.PentaxBracketing),
        new(Top, "WhiteBalance", [(0, "Auto"), (1, "Daylight"), (2, "Shade"), (3, "Fluorescent"), (4, "Tungsten"),
            (5, "Manual"), (6, "DaylightFluorescent"), (7, "DaywhiteFluorescent"), (8, "WhiteFluorescent"),
            (9, "Flash"), (10, "Cloudy"), (15, "Color Temperature Enhancement"), (17, "Kelvin"),
            (65534, "Unknown"), (65535, "User Selected")], Form.PentaxFirst),
        new(Top, "WhiteBalanceMode", [(1, "Auto (Daylight)"), (2, "Auto (Shade)"), (3, "Auto (Flash)"),
            (4, "Auto (Tungsten)"), (6, "Auto (DaylightFluorescent)"), (7, "Auto (DaywhiteFluorescent)"),
            (8, "Auto (WhiteFluorescent)"), (10, "Auto (Cloudy)"), (65535, "User-Selected"),
            (65534, "Preset (Fireworks?)")], Form.PentaxFirst),
        new(Top, "FocalLength", [], Form.PentaxFocalLength),
        new(Top, "Saturation", [(0, "Low"), (1, "Normal"), (2, "High"), (3, "Med Low"), (4, "Med High"),
            (5, "Very Low"), (6, "Very High"), (7, "-4"), (8, "+4"), (65535, "None")], Form.PentaxFirst),
        new(Top, "Contrast", [(0, "Low"), (1, "Normal"), (2, "High"), (3, "Med Low"), (4, "Med High"),
            (5, "Very Low"), (6, "Very High"), (7, "-4"), (8, "+4")], Form.PentaxFirst),
        new(Top, "Sharpness", [(0, "Soft"), (1, "Normal"), (2, "Hard"), (3, "Med Soft"), (4, "Med Hard"),
            (5, "Very Soft"), (6, "Very Hard"), (7, "-4"), (8, "+4")], Form.PentaxFirst),
        new(Top, "Location", [(0, "Home town"), (1, "Destination")], Form.PentaxFirst),
        new(Top, "Hometown", [(0, "Pago Pago"), (1, "Honolulu"), (2, "Anchorage"), (3, "Vancouver"),
            (4, "San Fransisco"), (5, "Los Angeles"), (6, "Calgary"), (7, "Denver"), (8, "Mexico City"),
            (9, "Chicago"), (10, "Miami"), (11, "Toronto"), (12, "New York"), (13, "Santiago"),
            (14, "Caracus"), (15, "Halifax"), (16, "Buenos Aires"), (17, "Sao Paulo"), (18, "Rio de Janeiro"),
            (19, "Madrid"), (20, "London"), (21, "Paris"), (22, "Milan"), (23, "Rome"), (24, "Berlin"),
            (25, "Johannesburg"), (26, "Istanbul"), (27, "Cairo"), (28, "Jerusalem"), (29, "Moscow"),
            (30, "Jeddah"), (31, "Tehran"), (32, "Dubai"), (33, "Karachi"), (34, "Kabul"), (35, "Male"),
            (36, "Delhi"), (37, "Colombo"), (38, "Kathmandu"), (39, "Dacca"), (40, "Yangon"),
            (41, "Bangkok"), (42, "Kuala Lumpur"), (43, "Vientiane"), (44, "Singapore"), (45, "Phnom Penh"),
            (46, "Ho Chi Minh"), (47, "Jakarta"), (48, "Hong Kong"), (49, "Perth"), (50, "Beijing"),
            (51, "Shanghai"), (52, "Manila"), (53, "Taipei"), (54, "Seoul"), (55, "Adelaide"), (56, "Tokyo"),
            (57, "Guam"), (58, "Sydney"), (59, "Noumea"), (60, "Wellington"), (61, "Auckland"), (62, "Lima"),
            (63, "Dakar"), (64, "Algiers"), (65, "Helsinki"), (66, "Athens"), (67, "Nairobi"),
            (68, "Amsterdam"), (69, "Stockholm"), (70, "Lisbon"), (71, "Copenhagen"), (72, "Warsaw"),
            (73, "Prague"), (74, "Budapest")], Form.PentaxFirst),
        new(Top, "Destination", [], Form.PentaxFirst),
        new(Top, "HometownDST", [(0, "No"), (1, "Yes")], Form.PentaxFirst),
        new(Top, "DestinationDST", [(0, "No"), (1, "Yes")], Form.PentaxFirst),
        new(Top, "ColorSpace", [(0, "sRGB"), (1, "Adobe RGB")], Form.PentaxFirst),
        new(Top, "ImageProcessing", [(0x00000000, "Unprocessed"), (0x00000004, "Digital Filter"),
            (0x01000000, "Resized"), (0x02000000, "Cropped"), (0x04000000, "Color Filter"),
            (0x06000000, "Digital Filter 6"), (0x10000000, "Frame Synthesis?")], Form.PentaxFourByteEnum),
        new(Top, "PictureMode", [(0x000000, "Program"), (0x000100, "Hi-speed Program"), (0x000200, "DOF Program"),
            (0x000300, "MTF Program"), (0x000400, "Standard"), (0x000500, "Portrait"), (0x000600, "Landscape"),
            (0x000700, "Macro"), (0x000800, "Sport"), (0x000900, "Night Scene Portrait"), (0x000a00, "No Flash"),
            (0x000b00, "Night Scene"), (0x000c00, "Surf & Snow"), (0x000d00, "Text"), (0x000e00, "Sunset"),
            (0x000f00, "Kids"), (0x001000, "Pet"), (0x001100, "Candlelight"), (0x001200, "Museum"),
            (0x001300, "Food"), (0x001400, "Stage Lighting"), (0x001500, "Night Snap"), (0x001700, "Blue Sky"),
            (0x001800, "Sunset"), (0x001a00, "Night Scene HDR"), (0x001b00, "HDR"), (0x001c00, "Quick Macro"),
            (0x001d00, "Forest"), (0x001e00, "Backlight Silhouette"), (0x010400, "Auto PICT (Standard)"),
            (0x010500, "Auto PICT (Portrait)"), (0x010600, "Auto PICT (Landscape)"), (0x010700, "Auto PICT (Macro)"),
            (0x010800, "Auto PICT (Sport)"), (0x020000, "Program AE"), (0x030000, "Green Mode"),
            (0x040000, "Shutter Speed Priority"), (0x050000, "Aperture Priority"), (0x060000, "Shutter Priority"),
            (0x080000, "Manual"), (0x090000, "Bulb"), (0x0d0000, "Shutter & Aperture Priority AE"),
            (0x0f0000, "Sensitivity Priority AE"), (0x100000, "Flash X-Sync Speed AE"),
            (0x020001, "Program AE"), (0x020101, "Hi-speed Program"), (0x020201, "DOF Program"),
            (0x020301, "MTF Program"), (0x021601, "Shallow DOF"), (0x030001, "Green Mode"),
            (0x040001, "Shutter Speed Priority"), (0x050001, "Aperture Priority"),
            (0x060001, "Program Tv Shift"), (0x070001, "Program Av Shift"), (0x080001, "Manual"),
            (0x090001, "Bulb"), (0x0a0001, "Aperture Priority (Off-Auto-Aperture)"),
            (0x0b0001, "Manual (Off-Auto-Aperture)"), (0x0c0001, "Bulb (Off-Auto-Aperture)"),
            (0x100001, "Flash X-Sync Speed AE (1)"), (0x000001, "Program"),
            (0xfe0000, "Video (30 fps)"), (0xff0004, "Video (24 fps)")], Form.PentaxThreeByteEnum),
        new(Top, "DriveMode", [(0x00000000, "Single-frame"), (0x01000000, "Continuous"),
            (0x02000000, "Continuous (Hi)"), (0x03000000, "Burst"), (0xff000000, "Video"),
            (0x00010000, "Self-timer (12 sec)"), (0x00020000, "Self-timer (2 sec)"),
            (0x000f0000, "Video"), (0x00000100, "Remote Control (3 sec)"), (0x00000200, "Remote Control"),
            (0x00000400, "Remote Continuous Shooting"), (0x00000001, "Multiple Exposure"),
            (0x00000010, "HDR"), (0x00000020, "HDR Strong 1"), (0x00000030, "HDR Strong 2"),
            (0x00000040, "HDR Strong 3"), (0x000000e0, "HDR Auto"), (0x000000ff, "Video")], Form.PentaxFourByteEnum),
        new(Top, "DigitalFilter", [(0, "Off"), (1, "On")], Form.PentaxFirst),
        new(Top, "Temperature", [], Form.PentaxTemperature),
        new(Top, "AELock", [(0, "Off"), (1, "On")], Form.PentaxFirst),
        new(Top, "NoiseReduction", [(0, "Off"), (1, "On")], Form.PentaxFirst),
        new(Top, "FlashExposureCompensation", [], Form.PentaxFlashCompensation),
        new(Top, "ImageTone", [(0, "Natural"), (1, "Bright"), (2, "Portrait"), (3, "Landscape"), (4, "Vibrant"),
            (5, "Monochrome"), (6, "Muted"), (7, "Reversal film"), (8, "Bleach bypass"), (9, "Radiant")], Form.PentaxFirst),
        new(Top, "ShutterCount", [], Form.PentaxFirst),
        new(Top, "DynamicRangeExpansion", [(0, "Off"), (0x01000000, "On")], Form.PentaxDynamicRange),
        new(Top, "HighISONoiseReduction", [(0, "Off"), (1, "Weakest"), (2, "Weak"), (3, "Strong"), (4, "Custom")], Form.PentaxFirst),
        new(Top, "HDF", [(0, "Off"), (1, "On")], Form.PentaxFirst),
    ];

    private static string? PentaxDngInterpret(Rule rule, ExifValue value, MakerNote? note)
    {
        switch (rule.Reading)
        {
            case Form.PentaxVersion:
                return (value.FieldType is TiffFieldType.Byte or TiffFieldType.Undefined) && value.Count > 0
                    ? string.Join(".", value.RawBytes.ToArray().Select(number => number.ToString(CultureInfo.InvariantCulture)))
                    : null;
            case Form.PentaxResolution:
                return value.Count == 2 && Number(value, 0) is long width && Number(value, 1) is long height
                    ? Whole(width) + "x" + Whole(height) : null;
            case Form.PentaxDate:
                if (value.Count != 4 || value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined)) return null;
                ReadOnlySpan<byte> date = value.RawBytes;
                return ((date[0] << 8) | date[1]).ToString(CultureInfo.InvariantCulture) + ":" +
                       date[2].ToString("D2", CultureInfo.InvariantCulture) + ":" + date[3].ToString("D2", CultureInfo.InvariantCulture);
            case Form.PentaxTime:
                if (value.Count != 3 || value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined)) return null;
                ReadOnlySpan<byte> time = value.RawBytes;
                return time[0].ToString("D2", CultureInfo.InvariantCulture) + ":" +
                       time[1].ToString("D2", CultureInfo.InvariantCulture) + ":" + time[2].ToString("D2", CultureInfo.InvariantCulture);
            case Form.PentaxExposure:
                return Number(value, 0) is long exposure ? (exposure / 100d).ToString("0.######", CultureInfo.InvariantCulture) + " ms" : null;
            case Form.PentaxFNumber:
                return Number(value, 0) is long aperture ? "F" + (aperture / 10d).ToString("G2", CultureInfo.InvariantCulture) : null;
            case Form.PentaxFocalLength:
                return Number(value, 0) is long focal ? (focal / 100d).ToString("0.0", CultureInfo.InvariantCulture) + " mm" : null;
            case Form.PentaxExposureCompensation:
                return Number(value, 0) is long compensation
                    ? ((compensation - 50) / 10d).ToString("0.######", CultureInfo.InvariantCulture) + " EV" : null;
            case Form.PentaxTemperature:
                return Number(value, 0) is long temperature ? Whole(temperature) + " C" : null;
            case Form.PentaxFlashCompensation:
                return Number(value, 0) is long flash ? (flash / 256d).ToString("0.######", CultureInfo.InvariantCulture) + " EV" : null;
            case Form.PentaxBracketing:
                return Bracketing(value);
            case Form.PentaxThreeByteEnum:
                return Composite(rule, value, 3);
            case Form.PentaxFourByteEnum:
                return Composite(rule, value, 4);
            case Form.PentaxDynamicRange:
                return DynamicRange(rule, value);
            case Form.PentaxFirst:
                if (rule.Name == "ShutterCount") return ShutterCount(value, note);
                if (Number(value, 0) is not long first || value.Count == 0) return null;
                IEnumerable<(long Number, string Label)> labels = rule.Name == "Destination"
                    ? PentaxDng.First(candidateRule => candidateRule.Name == "Hometown").Labels
                    : rule.Labels;
                foreach ((long candidate, string label) in labels)
                    if (candidate == first) return label;
                return Stored(first);
            default:
                return null;
        }
    }

    private static long? Number(ExifValue value, int index)
    {
        if (index >= value.Count || value.FieldType == TiffFieldType.Ascii) return null;
        if (value.FieldType == TiffFieldType.Undefined)
            return value.RawBytes.Length > index ? value.RawBytes[index] : null;
        return ExifTags.TryInteger(value, index, out long number) ? number : null;
    }

    private static string? Composite(Rule rule, ExifValue value, int count)
    {
        if (value.Count != count || value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined)) return null;
        long code = 0;
        foreach (byte part in value.RawBytes) code = (code << 8) | part;
        foreach ((long candidate, string label) in rule.Labels)
            if (candidate == code) return label;
        return "Unknown (0x" + code.ToString(count == 3 ? "x6" : "x8", CultureInfo.InvariantCulture) + ")";
    }

    private static string? DynamicRange(Rule rule, ExifValue value)
    {
        if (value.Count != 4 || value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined)) return null;
        long code = 0;
        foreach (byte part in value.RawBytes) code = (code << 8) | part;
        foreach ((long candidate, string label) in rule.Labels)
            if (candidate == code) return label;
        return "Unknown (0x" + code.ToString("x8", CultureInfo.InvariantCulture) + ")";
    }

    private static string? Bracketing(ExifValue value)
    {
        if (value.Count != 2 || value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Short or TiffFieldType.Long) ||
            Number(value, 0) is not long first || Number(value, 1) is not long second) return null;
        double ev = first < 10 ? first / 3d : first - 9.5d;
        string text = ev.ToString("G2", CultureInfo.InvariantCulture) + " EV";
        if (second == 0) return text + " (No extended bracketing)";
        long kind = second >> 8, range = second & 0xff;
        string label = kind switch { 1 => "WB-BA", 2 => "WB-GM", 3 => "Saturation", 4 => "Sharpness", 5 => "Contrast", _ => "Unknown " + kind };
        return text + " (" + label + " " + Whole(range) + ")";
    }

    private static string? ShutterCount(ExifValue value, MakerNote? note)
    {
        if (value.Count != 4 || value.RawBytes.Length != 4 || note is null) return null;
        MakerNoteEntry? dateEntry = note.Entries.FirstOrDefault(entry => entry.Name == "Date");
        MakerNoteEntry? timeEntry = note.Entries.FirstOrDefault(entry => entry.Name == "Time");
        if (dateEntry is null || timeEntry is null || dateEntry.Value.RawBytes.Length != 4 || timeEntry.Value.RawBytes.Length != 3)
            return null;
        uint date = ((uint)dateEntry.Value.RawBytes[0] << 24) | ((uint)dateEntry.Value.RawBytes[1] << 16) |
                    ((uint)dateEntry.Value.RawBytes[2] << 8) | dateEntry.Value.RawBytes[3];
        uint time = ((uint)timeEntry.Value.RawBytes[0] << 24) | ((uint)timeEntry.Value.RawBytes[1] << 16) |
                    ((uint)timeEntry.Value.RawBytes[2] << 8);
        ReadOnlySpan<byte> encoded = value.RawBytes;
        uint count = ((uint)encoded[0] << 24) | ((uint)encoded[1] << 16) | ((uint)encoded[2] << 8) | encoded[3];
        return Whole(count ^ date ^ ~time);
    }
}
