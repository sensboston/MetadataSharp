using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>Corpus-backed Olympus and OM System MakerNote renderings.</summary>
internal static class OlympusMakerNoteInterpretations
{
    internal static string? Interpret(int scope, string name, ExifValue value)
    {
        if (name == "CameraID" && value.FieldType is TiffFieldType.Ascii or TiffFieldType.Undefined)
        {
            ReadOnlySpan<byte> bytes = value.RawBytes;
            int length = bytes.IndexOf((byte)0);
            if (length < 0) length = bytes.Length;
            return length == 0 ? null : Encoding.ASCII.GetString(bytes[..length]);
        }
        if (name == "SpecialMode")
            return SpecialMode(value);
        if (scope == 0x2010) return Equipment(name, value);
        if (scope == 0x2020) return CameraSettings(name, value);
        if (scope == 0x2030) return RawDevelopment(name, value);
        if (scope == 0x2031) return RawDevelopment2(name, value);
        if (scope == 0x2040) return ImageProcessing(name, value);
        if (scope == 0x2050) return FocusInfo(name, value);
        if (name == "DigitalZoom")
            return DigitalZoom(value);

        if (!ExifTags.TryInteger(value, 0, out long number))
            return name == "WhiteBalance" ? WhiteBalance(value) : null;

        return name switch
        {
            "Quality" => LookupOrStored(number, (1, "Standard Quality (SQ)"), (2, "High Quality (HQ)"),
                (3, "Super High Quality (SHQ)"), (6, "Raw")),
            "Macro" => LookupOrStored(number, (0, "Off"), (1, "On"), (2, "Super macro")),
            "BWMode" or "FlashMode" or "ExternalFlashBounce" => LookupOrStored(number, (0, "Off"), (1, "On")),
            "OneTouchWB" => LookupOrStored(number, (0, "Off"), (1, "On"), (2, "On (preset)")),
            "FlashDevice" => LookupOrStored(number, (0, "None"), (1, "Internal"), (4, "External"), (5, "Internal + External")),
            "FocusRange" => LookupOrStored(number, (0, "Normal"), (1, "Macro")),
            "FocusMode" => LookupOrStored(number, (0, "Auto"), (1, "Manual")),
            "SharpnessFactor" => LookupOrStored(number, (0, "Normal"), (1, "Hard"), (2, "Soft")),
            "Contrast" => LookupOrStored(number, (0, "High"), (1, "Normal"), (2, "Low")),
            _ => name == "WhiteBalance" ? WhiteBalance(value) : null,
        };
    }

    private static string? Equipment(string name, ExifValue value)
    {
        if (name is "EquipmentVersion") return Version(value);
        if (name == "LensType" && value.Count == 6 && value.RawBytes.Length >= 6)
        {
            ReadOnlySpan<byte> b = value.RawBytes;
            return (b[0], b[2], b[3]) switch
            {
                (0, 19, 16) => "Olympus M.Zuiko Digital 14-42mm F3.5-5.6 II R",
                (0, 33, 16) => "Olympus M.Zuiko Digital ED 14-42mm F3.5-5.6 EZ",
                (0, 38, 16) => "Olympus M.Zuiko Digital ED 12-100mm F4.0 IS Pro",
                (0, 41, 16) => "Olympus M.Zuiko Digital ED 17mm F1.2 Pro",
                (0, 52, 16) => "Olympus M.Zuiko Digital ED 12-45mm F4.0 Pro",
                _ => null,
            };
        }
        if (name == "Extender" && value.Count == 6 && value.RawBytes.Length >= 3)
            return value.RawBytes[0] == 0 ? value.RawBytes[2] switch
            {
                0 => "None", 4 => "Olympus Zuiko Digital EC-14 1.4x Teleconverter",
                8 => "Olympus EX-25 Extension Tube", 16 => "Olympus Zuiko Digital EC-20 2.0x Teleconverter", _ => null,
            } : null;
        if (!ExifTags.TryInteger(value, 0, out long number)) return null;
        return name switch
        {
            "FlashType" => LookupOrStored(number, (0, "None"), (2, "Simple E-System"), (3, "E-System")),
            "FlashModel" => LookupOrStored(number, (0, "None"), (1, "FL-20"), (2, "FL-50"), (3, "RF-11"),
                (4, "TF-22"), (5, "FL-36"), (6, "FL-50R"), (7, "FL-36R"), (9, "FL-14"), (11, "FL-600R")),
            _ => null,
        };
    }

    private static string? RawDevelopment(string name, ExifValue value)
    {
        if (name == "RawDevVersion") return Version(value);
        if (!ExifTags.TryInteger(value, 0, out long number)) return null;
        return name switch
        {
            "ColorSpace" => LookupOrStored(number, (0, "sRGB"), (1, "Adobe RGB"), (2, "Pro Photo RGB")),
            "Engine" => LookupOrStored(number, (0, "High Speed"), (1, "High Function"),
                (2, "Advanced High Speed"), (3, "Advanced High Function")),
            "EditStatus" => LookupOrStored(number, (0, "Original"), (1, "Edited (Landscape)"),
                (6, "Edited (Portrait)"), (8, "Edited (Portrait)")),
            "NoiseReduction" => BitMask(number, (1, "Noise Reduction"), (2, "Noise Filter"), (4, "Noise Filter (ISO Boost)"), (8, "Auto")),
            "Settings" => BitMask(number, (1, "WB Color Temp"), (4, "WB Gray Point"), (8, "Saturation"),
                (16, "Contrast"), (32, "Sharpness"), (64, "Color Space"), (128, "High Function"), (256, "Noise Reduction")),
            _ => null,
        };
    }

    private static string? RawDevelopment2(string name, ExifValue value)
    {
        if (name == "RawDev2Version") return Version(value);
        if (!ExifTags.TryInteger(value, 0, out long number)) return null;
        return name switch
        {
            "WhiteBalance" => LookupOrStored(number, (1, "Color Temperature"), (2, "Gray Point")),
            "ColorSpace" => LookupOrStored(number, (0, "sRGB"), (1, "Adobe RGB"), (2, "Pro Photo RGB")),
            "Engine" => LookupOrStored(number, (0, "High Speed"), (1, "High Function")),
            "PictureMode" => LookupOrStored(number, (1, "Vivid"), (2, "Natural"), (3, "Muted"), (256, "Monotone"), (512, "Sepia")),
            "PM_BWFilter" => LookupOrStored(number, (1, "Neutral"), (2, "Yellow"), (3, "Orange"), (4, "Red"), (5, "Green")),
            "PMPictureTone" => LookupOrStored(number, (1, "Neutral"), (2, "Sepia"), (3, "Blue"), (4, "Purple"), (5, "Green")),
            "NoiseReduction" => BitMask(number, (1, "Noise Reduction"), (2, "Noise Filter"), (4, "Noise Filter (ISO Boost)"), (8, "Auto")),
            "AutoGradation" => LookupOrStored(number, (0, "Off"), (1, "On")),
            _ => null,
        };
    }

    private static string? ImageProcessing(string name, ExifValue value)
    {
        if (name == "ImageProcessingVersion") return Version(value);
        if (name == "MultipleExposureMode" && ExifTags.TryInteger(value, 0, out long exposure))
            return LookupOrStored(exposure, (0, "Off"), (2, "On (2 frames)"), (3, "On (3 frames)"));
        if (!ExifTags.TryInteger(value, 0, out long number)) return null;
        return name switch
        {
            "DistortionCorrection" or "ShadingCompensation" => LookupOrStored(number, (0, "Off"), (1, "On")),
            "AspectRatio" => LookupOrStored(number, (1, "4:3"), (2, "3:2"), (3, "16:9"), (4, "6:6"),
                (5, "5:4"), (6, "7:6"), (7, "6:5"), (8, "7:5"), (9, "3:4")),
            "FaceDetect" => LookupOrStored(number, (0, "Off"), (1, "On")),
            "NoiseReduction" => BitMask(number, (1, "Noise Reduction"), (2, "Noise Filter"),
                (4, "Noise Filter (ISO Boost)"), (8, "Auto")),
            _ => null,
        };
    }

    private static string? FocusInfo(string name, ExifValue value)
    {
        if (name == "FocusInfoVersion") return Version(value);
        if (name == "FocusDistance") return FocusDistance(value);
        if (name == "AFPoint" && ExifTags.TryInteger(value, 0, out long point))
            return LookupOrStored(point, (0, "Left (or n/a)"), (1, "Center (horizontal)"), (2, "Right"),
                (3, "Center (vertical)"), (255, "None"));
        if (name == "ExternalFlashBounce" && ExifTags.TryInteger(value, 0, out long bounce))
            return LookupOrStored(bounce, (0, "Bounce or Off"), (1, "Direct"));
        if (name == "ManualFlash" && value.Count == 2 && ExifTags.TryInteger(value, 0, out long flash) &&
            ExifTags.TryInteger(value, 1, out long power))
            return (flash == 0 ? "Off" : flash == 1 ? "On" : flash.ToString(CultureInfo.InvariantCulture)) + " " +
                power.ToString(CultureInfo.InvariantCulture);
        if (!ExifTags.TryInteger(value, 0, out long number)) return null;
        return name switch
        {
            "AutoFocus" or "ExternalFlash" or "InternalFlash" => LookupOrStored(number, (0, "Off"), (1, "On")),
            _ => null,
        };
    }

    private static string? Version(ExifValue value)
    {
        ReadOnlySpan<byte> bytes = value.RawBytes;
        if (bytes.Length != 4) return null;
        string version = Encoding.ASCII.GetString(bytes);
        return version.All(char.IsAsciiDigit) ? version[..2] + "." + version[2..] : null;
    }

    private static string? FocusDistance(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Rational || value.Count != 1 || value.RawBytes.Length < 8) return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        bool little = value.ByteOrder == Endianness.LittleEndian;
        uint numerator = little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
        uint denominator = little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) : BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
        if (numerator == uint.MaxValue) return "Infinity";
        return ((float)numerator / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " m";
    }

    private static string? CameraSettings(string name, ExifValue value)
    {
        if (name == "CameraSettingsVersion")
        {
            string version = Encoding.ASCII.GetString(value.RawBytes);
            return version.Length == 4 && version.All(char.IsAsciiDigit) ? version[..2] + "." + version[2..] : null;
        }
        if (name == "FocusMode") return CameraFocusMode(value);
        if (name == "Gradation") return Gradation(value);
        if (name == "NoiseFilter") return NoiseFilter(value);
        if (name is "ArtFilter" or "MagicFilter") return ArtFilter(value);
        if (!ExifTags.TryInteger(value, 0, out long number)) return null;
        return name switch
        {
            "PreviewImageValid" => LookupOrStored(number, (0, "No"), (1, "Yes")),
            "ExposureMode" => LookupOrStored(number, (1, "Manual"), (2, "Program"), (3, "Aperture-priority AE"),
                (4, "Shutter speed priority AE"), (5, "Program-shift")),
            "AELock" or "DistortionCorrection" or "ShadingCompensation" or "ExtendedWBDetect" =>
                LookupOrStored(number, (0, "Off"), (1, "On")),
            "MeteringMode" => LookupOrStored(number, (2, "Center-weighted average"), (3, "Spot"), (5, "ESP"),
                (261, "Pattern+AF"), (515, "Spot+Highlight control"), (1027, "Spot+Shadow control")),
            "MacroMode" => LookupOrStored(number, (0, "Off"), (1, "On"), (2, "Super Macro")),
            "FocusProcess" => LookupOrStored(number, (0, "AF Not Used"), (1, "AF Used")),
            "AFSearch" => LookupOrStored(number, (0, "Not Ready"), (1, "Ready")),
            "FlashMode" => BitMask(number, "Off", (0x01, "On"), (0x02, "Fill-in"), (0x04, "Red-eye"),
                (0x08, "Slow-sync"), (0x10, "Forced On"), (0x20, "2nd Curtain")),
            "FlashRemoteControl" => LookupOrStored(number, (0, "Off"), (1, "Channel 1, Low"), (2, "Channel 2, Low"),
                (3, "Channel 3, Low"), (4, "Channel 4, Low"), (9, "Channel 1, Mid"), (10, "Channel 2, Mid"),
                (11, "Channel 3, Mid"), (12, "Channel 4, Mid"), (17, "Channel 1, High"), (18, "Channel 2, High"),
                (19, "Channel 3, High"), (20, "Channel 4, High")),
            "FlashControlMode" => LookupOrStored(number, (0, "Off"), (3, "TTL"), (4, "Auto"), (5, "Manual")),
            "WhiteBalance" => LookupOrStored(number, (0, "Auto"), (1, "Auto (Keep Warm Color Off)"),
                (16, "7500K (Fine Weather with Shade)"), (17, "6000K (Cloudy)"), (18, "5300K (Fine Weather)"),
                (20, "3000K (Tungsten light)"), (21, "3600K (Tungsten light-like)"), (22, "Auto Setup"),
                (23, "5500K (Flash)"), (33, "6600K (Daylight fluorescent)"), (34, "4500K (Neutral white fluorescent)"),
                (35, "4000K (Cool white fluorescent)"), (36, "White Fluorescent"), (48, "3600K (Tungsten light-like)"),
                (67, "Underwater"), (256, "One Touch WB 1"), (257, "One Touch WB 2"), (258, "One Touch WB 3"),
                (259, "One Touch WB 4"), (512, "Custom WB 1"), (513, "Custom WB 2"), (514, "Custom WB 3"),
                (515, "Custom WB 4")),
            "ModifiedSaturation" => LookupOrStored(number, (0, "Off"), (1, "CM1 (Red Enhance)"),
                (2, "CM2 (Green Enhance)"), (3, "CM3 (Blue Enhance)"), (4, "CM4 (Skin Tones)")),
            "ColorSpace" => LookupOrStored(number, (0, "sRGB"), (1, "Adobe RGB"), (2, "Pro Photo RGB")),
            "SceneMode" => LookupOrStored(number, (0, "Standard"), (6, "Auto"), (7, "Sport"), (8, "Portrait"),
                (9, "Landscape+Portrait"), (10, "Landscape"), (11, "Night Scene"), (12, "Self Portrait"),
                (13, "Panorama"), (14, "2 in 1"), (15, "Movie"), (16, "Landscape+Portrait"), (17, "Night+Portrait"),
                (18, "Indoor"), (19, "Fireworks"), (20, "Sunset"), (22, "Macro"), (23, "Super Macro"),
                (24, "Food"), (25, "Documents"), (26, "Museum"), (27, "Shoot & Select"), (28, "Beach & Snow"),
                (29, "Self Portrait+Timer"), (30, "Candle"), (31, "Available Light"), (32, "Behind Glass"),
                (33, "My Mode"), (34, "Pet"), (35, "Underwater Wide1"), (36, "Underwater Macro"),
                (37, "Shoot & Select1"), (38, "Shoot & Select2"), (39, "High Key"),
                (40, "Digital Image Stabilization"), (41, "Auction"), (42, "Beach"), (43, "Snow"),
                (44, "Underwater Wide2"), (45, "Low Key"), (46, "Children"), (47, "Vivid"),
                (48, "Nature Macro"), (49, "Underwater Snapshot"), (50, "Shooting Guide")),
            "NoiseReduction" => BitMask(number, (1, "Noise Reduction"), (2, "Noise Filter"),
                (4, "Noise Filter (ISO Boost)"), (8, "Auto")),
            "PictureMode" => LookupOrStored(number, (1, "Vivid"), (2, "Natural"), (3, "Muted"), (4, "Portrait"),
                (5, "i-Enhance"), (6, "e-Portrait"), (7, "Color Creator"), (9, "Color Profile 1"),
                (10, "Color Profile 2"), (11, "Color Profile 3"), (12, "Monochrome Profile 1"),
                (13, "Monochrome Profile 2"), (14, "Monochrome Profile 3"), (256, "Monotone"), (512, "Sepia")),
            "PictureModeBWFilter" => LookupOrStored(number, (0, "n/a"), (1, "Neutral"), (2, "Yellow"),
                (3, "Orange"), (4, "Red"), (5, "Green")),
            "PictureModeTone" => LookupOrStored(number, (0, "n/a"), (1, "Neutral"), (2, "Sepia"),
                (3, "Blue"), (4, "Purple"), (5, "Green")),
            "ImageStabilization" => LookupOrStored(number, (0, "Off"), (1, "S-IS 1"), (2, "S-IS 2"),
                (3, "S-IS 3"), (4, "S-IS AUTO")),
            _ => null,
        };
    }

    private static string? CameraFocusMode(ExifValue value)
    {
        if (!ExifTags.TryInteger(value, 0, out long first)) return null;
        if (value.Count < 2)
            return LookupOrStored(first, (0, "Single AF"), (1, "Sequential shooting AF"), (2, "Continuous AF"),
                (3, "Multi AF"), (4, "Face detect"), (10, "MF"));
        if (!ExifTags.TryInteger(value, 1, out long mask)) return null;
        var labels = new List<string>();
        foreach ((long bit, string label) in new (long, string)[] { (1, "S-AF"), (4, "C-AF"), (16, "MF"),
                     (32, "Face detect"), (64, "Imager AF"), (256, "AF sensor") })
            if ((mask & bit) != 0) labels.Add(label);
        return string.Join(", ", labels) + mask.ToString(CultureInfo.InvariantCulture);
    }

    private static string? Gradation(ExifValue value)
    {
        if (value.Count is not (3 or 4) || !ExifTags.TryInteger(value, 0, out long first) ||
            !ExifTags.TryInteger(value, 1, out long second) || !ExifTags.TryInteger(value, 2, out long third)) return null;
        string result = (first, second, third) switch
        {
            (-1, -1, 1) => "Low Key",
            (0, -1, 1) => "Normal",
            (1, -1, 1) => "High Key",
            _ => string.Join(" ", first, second, third),
        };
        if (value.Count == 4 && ExifTags.TryInteger(value, 3, out long mode) && mode is 0 or 1)
            result += mode == 0 ? ", User-Selected" : ", Auto-Override";
        return result;
    }

    private static string? NoiseFilter(ExifValue value)
    {
        if (value.Count != 3 || !ExifTags.TryInteger(value, 0, out long level) ||
            !ExifTags.TryInteger(value, 1, out long step) || !ExifTags.TryInteger(value, 2, out long scale) ||
            step != -2 || scale != 1) return null;
        return level switch { -2 => "Off", -1 => "Low", 0 => "Standard", 1 => "High", _ => level.ToString(CultureInfo.InvariantCulture) };
    }

    private static string? ArtFilter(ExifValue value)
    {
        if (value.Count != 4 || !ExifTags.TryInteger(value, 0, out long code) ||
            !ExifTags.TryInteger(value, 3, out long position)) return null;
        string? label = Lookup(code, (0, "Off"), (1, "Soft Focus"), (2, "Pop Art"), (3, "Pale & Light Color"),
            (4, "Light Tone"), (5, "Pin Hole"), (6, "Grainy Film"), (9, "Diorama"), (10, "Cross Process"),
            (12, "Fish Eye"), (13, "Drawing"), (14, "Gentle Sepia"), (15, "Pale & Light Color II"),
            (16, "Pop Art II"), (17, "Pin Hole II"), (18, "Pin Hole III"), (19, "Grainy Film II"),
            (20, "Dramatic Tone"), (21, "Punk"), (22, "Soft Focus 2"), (23, "Sparkle"), (24, "Watercolor"),
            (25, "Key Line"), (26, "Key Line II"), (27, "Miniature"), (28, "Reflection"), (29, "Fragmented"),
            (31, "Cross Process II"), (32, "Dramatic Tone II"), (33, "Watercolor I"), (34, "Watercolor II"),
            (35, "Diorama II"), (36, "Vintage"), (37, "Vintage II"), (38, "Vintage III"),
            (39, "Partial Color"), (40, "Partial Color II"), (41, "Partial Color III"));
        if (label is null) return null;
        return code == 39 ? label + " (position " + (position + 1).ToString(CultureInfo.InvariantCulture) + ")" : label;
    }

    private static string? BitMask(long number, params (long Bit, string Label)[] labels) =>
        BitMask(number, null, labels);

    private static string? BitMask(long number, string? zeroLabel, params (long Bit, string Label)[] labels)
    {
        if (number == 0) return zeroLabel;
        var matches = new List<string>();
        foreach ((long bit, string label) in labels)
            if ((number & bit) == bit) matches.Add(label);
        return matches.Count == 0 ? null : string.Join(", ", matches);
    }

    private static string? SpecialMode(ExifValue value)
    {
        if (value.Count != 3 || !ExifTags.TryInteger(value, 0, out long mode) ||
            !ExifTags.TryInteger(value, 1, out long sequence) || !ExifTags.TryInteger(value, 2, out long direction))
            return null;
        string result = mode switch { 0 => "Normal", 2 => "Fast", 3 => "Panorama", _ => "(" + mode.ToString(CultureInfo.InvariantCulture) + ")" };
        if (mode == 0)
            return result;
        result += ", Sequence number " + sequence.ToString(CultureInfo.InvariantCulture);
        if (mode != 2)
            result += ", " + (direction switch
            {
                1 => "Left to right",
                2 => "Right to left",
                3 => "Bottom to top",
                4 => "Top to bottom",
                _ => "(" + direction.ToString(CultureInfo.InvariantCulture) + ")",
            });
        return result;
    }

    private static string? WhiteBalance(ExifValue value)
    {
        if (!ExifTags.TryInteger(value, 0, out long mode)) return null;
        if (value.Count == 1)
            return mode == 1 ? "Auto" : null;
        if (value.Count != 2 || !ExifTags.TryInteger(value, 1, out long setting)) return null;
        if (mode == 1)
            return setting == 0 ? "Auto" : "Auto (" + setting.ToString(CultureInfo.InvariantCulture) + ")";
        if (mode == 3 && setting == 0) return "One-touch";
        if (mode == 2)
            return setting switch
            {
                2 => "3000 Kelvin", 3 => "3700 Kelvin", 4 => "4000 Kelvin", 5 => "4500 Kelvin",
                6 => "5500 Kelvin", 7 => "6500 Kelvin", 8 => "7500 Kelvin", _ => null,
            };
        return null;
    }

    private static string? DigitalZoom(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Rational || value.Count != 1 || value.RawBytes.Length < 8)
            return null;
        ReadOnlySpan<byte> raw = value.RawBytes;
        bool little = value.ByteOrder == Endianness.LittleEndian;
        uint numerator = little ? BinaryPrimitives.ReadUInt32LittleEndian(raw) : BinaryPrimitives.ReadUInt32BigEndian(raw);
        uint denominator = little ? BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]) : BinaryPrimitives.ReadUInt32BigEndian(raw[4..]);
        if (denominator == 0) return null;
        float ratio = (float)numerator / denominator;
        if (ratio is 0 or 1) return "None";
        return ratio.ToString("0.0", CultureInfo.InvariantCulture) + "x";
    }

    private static string? Lookup(long number, params (long Number, string Label)[] entries)
    {
        foreach ((long key, string label) in entries)
            if (key == number)
                return label;
        return null;
    }

    private static string LookupOrStored(long number, params (long Number, string Label)[] entries)
    {
        foreach ((long key, string label) in entries)
            if (key == number)
                return label;
        return "(" + number.ToString(CultureInfo.InvariantCulture) + ")";
    }

}
