using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>Corpus-backed Panasonic MakerNote renderings.</summary>
internal static class PanasonicMakerNoteInterpretations
{
    // Decode known Panasonic fields by name while leaving unrecognized values available in raw form.
    internal static string? Interpret(string name, ExifValue value)
    {
        if (name == "AFMode" && value.Count == 2 && ExifTags.TryInteger(value, 0, out long first) &&
            ExifTags.TryInteger(value, 1, out long second))
            return (first, second) switch
            {
                (0, 1) => "Spot mode on or 9 area",
                (0, 16) => "Spot mode off or 3-area (high speed)",
                (0, 23) => "23-area",
                (0, 49) => "49-area",
                (0, 225) => "225-area",
                (1, 0) => "Spot focussing",
                (1, 1) => "5-area",
                (16, 0) => "1-area",
                (16, 16) => "1-area (high speed)",
                (32, 0) => "3-area (auto)",
                (32, 1) => "3-area (left)",
                (32, 2) => "3-area (center)",
                (32, 3) => "3-area (right)",
                (64, 0) => "Face Detect",
                (128, 0) => "Spot Focusing 2",
                (240, 0) => "Tracking",
                _ => null,
            };

        if (name is "InternalSerialNumber" or "Country" or "State" or "City" or "City2" or "Landmark")
            return Ascii(value);
        if (name is "BabyAge1" or "BabyAge2")
            return Ascii(value) == "9999:99:99 00:00:00" ? "not set" : null;
        if (name is "ExifVersion" or "MakerNoteVersion")
        {
            string? version = Ascii(value);
            if (version is { Length: 4 } && version.All(char.IsAsciiDigit))
                return int.Parse(version[..2], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "." + version[2..];
            return null;
        }
        if (!ExifTags.TryInteger(value, 0, out long number))
            return null;

        return name switch
        {
            "Quality" => Lookup(number, (1, "TIFF"), (2, "High"), (3, "Normal"), (6, "Very High"), (7, "Raw"),
                (9, "Motion Picture"), (11, "Full HD Movie"), (12, "4k Movie")),
            "WhiteBalance" => Lookup(number, (1, "Auto"), (2, "Daylight"), (3, "Cloudy"), (4, "Halogen"),
                (5, "Manual"), (8, "Flash"), (10, "Black and white"), (11, "Manual"), (12, "Shade"), (13, "Kelvin")),
            "FocusMode" => Lookup(number, (1, "Auto"), (2, "Manual"), (4, "Auto, focus button"),
                (5, "Auto, continuous"), (6, "AF-S"), (7, "AF-C"), (8, "AF-F")),
            "ImageStabilization" => Lookup(number, (2, "On, Mode 1"), (3, "Off"), (4, "On, Mode 2"),
                (5, "Panning"), (6, "On, Mode 3")),
            "Macro" => Lookup(number, (1, "On"), (2, "Off"), (257, "Tele-macro"), (513, "Macro-zoom")),
            "ShootingMode" or "SceneMode" => ShootingMode(number),
            "Audio" => Lookup(number, (1, "Yes"), (2, "No"), (3, "Stereo")),
            "WhiteBalanceBias" => (number / 3).ToString(CultureInfo.InvariantCulture) + " EV",
            "ColorEffect" => Lookup(number, (1, "Off"), (2, "Warm"), (3, "Cool"), (4, "Black and white"),
                (5, "Sepia"), (6, "Happy")),
            "TimeSincePowerOn" => TimeSincePowerOn(number),
            "BurstMode" => Lookup(number, (0, "Off"), (1, "Low/High quality"), (2, "Infinite")),
            "Contrast" => Lookup(number, (0, "Normal"), (1, "Low"), (2, "High"), (6, "Medium low"),
                (7, "Medium high"), (256, "Low"), (272, "Standard"), (288, "High")),
            "NoiseReduction" => Lookup(number, (0, "Standard"), (1, "Low (-1)"), (2, "High (+1)"),
                (3, "Lowest (-2)"), (4, "Highest (+2)")),
            "FilmMode" => Lookup(number, (1, "Standard (color)"), (2, "Dynamic (color)"), (3, "Nature (color)"),
                (4, "Smooth (color)"), (5, "Standard (B&W)"), (6, "Dynamic (B&W)"), (7, "Smooth (B&W)"),
                (10, "Nostalgic"), (11, "Vibrant")),
            "BracketSettings" => Lookup(number, (0, "No Bracket"), (1, "3 images, Sequence 0/-/+"),
                (2, "3 images, Sequence -/0/+"), (3, "5 images, Sequence 0/-/+"),
                (4, "5 images, Sequence -/0/+"), (5, "7 images, Sequence 0/-/+"),
                (6, "7 images, Sequence -/0/+")),
            "FlashCurtain" => Lookup(number, (0, "n/a"), (1, "1st"), (2, "2nd")),
            "SelfTimer" => Lookup(number, (1, "Off"), (2, "10 s"), (3, "2 s"), (4, "10 s / 3 pictures")),
            "Rotation" => Lookup(number, (1, "Horizontal (normal)"), (3, "Rotate 180"), (6, "Rotate 90 CW"),
                (8, "Rotate 270 CW")),
            "AFAssistLamp" => Lookup(number, (1, "Fired"), (2, "Enabled but Not Used"),
                (3, "Disabled but Required"), (4, "Disabled and Not Required")),
            "ColorMode" => Lookup(number, (0, "Normal"), (1, "Natural"), (2, "Vivid")),
            "OpticalZoomMode" => Lookup(number, (1, "Standard"), (2, "EX optics")),
            "ConversionLens" => Lookup(number, (1, "Off"), (2, "Wide"), (3, "Telephoto"), (4, "Macro")),
            "TravelDay" => number == 65535 ? "not set" : null,
            "WorldTimeLocation" => Lookup(number, (1, "Home"), (2, "Destination")),
            "TextStamp1" or "TextStamp2" or "TextStamp3" or "TextStamp4" => Lookup(number, (1, "Off"), (2, "On")),
            "ProgramISO" => number switch { 65534 => "Intelligent ISO", 65535 => "n/a", _ => null },
            "LongShutterNoiseReduction" => Lookup(number, (1, "Off"), (2, "On")) ??
                "(" + number.ToString(CultureInfo.InvariantCulture) + ")",
            "IntelligentExposure" => Lookup(number, (0, "Off"), (1, "Low"), (2, "Standard"), (3, "High")),
            "FlashWarning" => Lookup(number, (0, "No"), (1, "Yes (flash required but disabled")),
            "IntelligentResolution" => Lookup(number, (0, "Off"), (1, "Low"), (2, "Standard"), (3, "High"), (4, "Extended")),
            "IntelligentDRange" => Lookup(number, (0, "Off"), (1, "Low"), (2, "Standard"), (3, "High")),
            "ClearRetouch" => Lookup(number, (0, "Off"), (1, "On")),
            "ManometerPressure" => number == 65535 ? "infinite" : number.ToString(CultureInfo.InvariantCulture) + " hPa",
            "AccelerometerZ" or "AccelerometerX" or "AccelerometerY" =>
                unchecked((short)number).ToString(CultureInfo.InvariantCulture),
            "SweepPanoramaDirection" => Lookup(number, (0, "Off"), (1, "Left to Right"), (2, "Right to Left"),
                (3, "Top to Bottom"), (4, "Bottom to Top")),
            "TimerRecording" => Lookup(number, (0, "Off"), (1, "Time Lapse"), (2, "Stop-Motion Animation")),
            "HDR" => Lookup(number, (0, "Off"), (100, "1 EV"), (200, "2 EV"), (300, "3 EV"),
                (32868, "1 EV (Auto)"), (32968, "2 EV (Auto)"), (33068, "3 EV (Auto)")),
            "ShutterType" => Lookup(number, (0, "Mechanical"), (1, "Electronic"), (2, "Hybrid")),
            "TouchAE" => Lookup(number, (0, "Off"), (1, "On")),
            "FlashFired" => Lookup(number, (1, "No"), (2, "Yes")),
            "PhotoStyle" => Lookup(number, (0, "NoAuto"), (1, "Standard or Custom"), (2, "Vivid"),
                (3, "Natural"), (4, "Monochrome"), (5, "Scenery"), (6, "Portrait")),
            "ShadingCompensation" => Lookup(number, (0, "Off"), (1, "On")),
            "CameraOrientation" => Lookup(number, (0, "Normal"), (1, "Rotate CW"), (2, "Rotate 180"),
                (3, "Rotate CCW"), (4, "Tilt upwards"), (5, "Tilt downwards")),
            "RollAngle" => (SignedShort(number) / 10.0).ToString("0.0", CultureInfo.InvariantCulture),
            "PitchAngle" => (-SignedShort(number) / 10.0).ToString("0.0", CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    // Translate Panasonic shooting-mode codes, preserving unknown codes as their stored number.
    private static string? ShootingMode(long number) => Lookup(number,
        (0, "Off"), (1, "Normal"), (2, "Portrait"), (3, "Scenery"), (4, "Sports"), (5, "Night portrait"),
        (6, "Program"), (7, "Aperture priority"), (8, "Shutter-speed priority"), (9, "Macro"), (10, "Spot"),
        (11, "Manual"), (12, "Movie preview"), (13, "Panning"), (14, "Simple"), (15, "Color effects"),
        (16, "Self Portrait"), (17, "Economy"), (18, "Fireworks"), (19, "Party"), (20, "Snow"),
        (21, "Night scenery"), (22, "Food"), (23, "Baby"), (24, "Soft skin"), (25, "Candlelight"),
        (26, "Starry night"), (27, "High sensitivity"), (28, "Panorama assist"), (29, "Underwater"),
        (30, "Beach"), (31, "Aerial photo"), (32, "Sunset"), (33, "Pet"), (34, "Intelligent ISO"),
        (35, "Clipboard"), (36, "High speed continuous shooting"), (37, "Intelligent auto"),
        (39, "Multi-aspect"), (41, "Transform"), (42, "Flash Burst"), (43, "Pin Hole"), (44, "Film Grain"),
        (45, "My Color"), (46, "Photo Frame"), (51, "HDR"), (55, "Handheld Night Shot"), (57, "3D"),
        (59, "Creative Control"), (62, "Panorama"), (63, "Glass Through"), (64, "HDR"), (66, "Digital Filter"),
        (67, "Clear Portrait"), (68, "Silky Skin"), (69, "Backlit Softness"), (70, "Clear in Backlight"),
        (71, "Relaxing Tone"), (72, "Sweet Child's Face"), (73, "Distinct Scenery"), (74, "Bright Blue Sky"),
        (75, "Romantic Sunset Glow"), (76, "Vivid Sunset Glow"), (77, "Glistening Water"), (78, "Clear Nightscape"),
        (79, "Cool Night Sky"), (80, "Warm Glowing Nightscape"), (81, "Artistic Nightscape"),
        (82, "Glittering Illuminations"), (83, "Clear Night Portrait"), (84, "Soft Image of a Flower"),
        (85, "Appetizing Food"), (86, "Cute Desert"), (87, "Freeze Animal Motion"), (88, "Clear Sports Shot"),
        (89, "Monochrome"), (90, "Creative Control"));

    // Format the Panasonic elapsed-time value using its documented scale and units.
    private static string TimeSincePowerOn(long value)
    {
        long hours = value / 360000;
        long minutes = value % 360000 / 6000;
        long seconds = value % 6000 / 100;
        long centiseconds = value % 100;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}.{centiseconds:00}");
    }

    private static int SignedShort(long number) => unchecked((short)number);

    // Read Panasonic ASCII fields up to the first NUL and trim only trailing padding.
    private static string? Ascii(ExifValue value)
    {
        ReadOnlySpan<byte> raw = value.RawBytes;
        int length = raw.IndexOf((byte)0);
        if (length < 0)
            length = raw.Length;
        return length == 0 ? null : Encoding.ASCII.GetString(raw[..length]);
    }

    // Map a recognized Panasonic numeric code to its label without guessing unknown values.
    private static string? Lookup(long number, params (long Number, string Label)[] entries)
    {
        foreach ((long key, string label) in entries)
            if (key == number)
                return label;
        return null;
    }
}
