namespace MetadataSharp.Core.MakerNotes;

// Nikon 1/2 and Nikon 3: selected fields of the early directories and modern note arrays from the
// comparison corpus. Lens names still require an external ID database; some version-dependent flash
// data and other camera-specific fields remain uninterpreted until their meanings are established.
internal static partial class MakerNoteLabels
{
    private static (long Number, string Label)[] NotAvailableAtEnds => [(0, "n/a"), (255, "n/a")];

    // Interpret selected tags from the early Nikon1 directory layout.
    private static readonly Rule[] Nikon1 =
    [
        new(Top, "Focus", [], Form.Abbreviation)
        {
            Texts = [("AF-C", "Continuous autofocus"), ("AF-S", "Single autofocus"), ("AF-A", "Automatic")],
        },
        new(Top, "DigitalZoom", [], Form.NikonDigitalZoom),
        new(Top, "FocusDistance", [], Form.NikonFocusDistance),
        new(Top, "ISOSpeed", [], Form.SecondOfTwo),
        new(Top, "AFFocusPos", [], Form.NikonAfFocusPosition),
    ];

    // Interpret selected tags from the Nikon2 directory layout, whose IDs differ from Nikon1.
    private static readonly Rule[] Nikon2 =
    [
        new(Top, "Quality", []),
        new(Top, "ColorMode", [(1, "Color"), (2, "Monochrome")]),
        new(Top, "ImageAdjustment", [(0, "Normal"), (1, "Bright+"), (2, "Bright-"), (3, "Contrast+"), (4, "Contrast-")]),
        new(Top, "ISOSpeed", [(0, "80"), (2, "160"), (4, "320"), (5, "100")]),
        new(Top, "WhiteBalance",
        [
            (0, "Auto"), (1, "Preset"), (2, "Daylight"), (3, "Incandescent"), (4, "Fluorescent"),
            (5, "Cloudy"), (6, "Speedlight"),
        ]),
        new(Top, "DigitalZoom", [], Form.NikonDigitalZoom),
    ];

    private static readonly Rule[] Nikon =
    [
        // The note's own directory.
        new(Top, "ColorSpace", [(1, "sRGB"), (2, "Adobe RGB")]),
        new(Top, "ActiveDLighting", [(0, "Off"), (1, "Low"), (65535, "Auto")]),
        new(Top, "AutoBracketRelease", [(0, "None"), (1, "Auto release"), (2, "Manual release")]),
        new(Top, "FlashMode", [(0, "Did not fire"), (7, "Fire, external"), (9, "Fire, TTL mode")]),
        new(Top, "HighISONoiseReduction", [(0, "Off"), (4, "Normal"), (6, "High")]),
        new(Top, "ImageAuthentication", [(0, "Off")]),
        new(Top, "NEFCompression", [(1, "Lossy (type 1)"), (3, "Lossless"), (4, "Lossy (type 2)")]),
        // Bit fields, listed as the combinations that occur.
        new(Top, "ShootingMode",
        [
            (0, "Single-frame"), (1, "Continuous"), (32, "Single-frame, Auto ISO"), (33, "Continuous, Auto ISO"),
            (49, "Continuous, Exposure Bracketing, Auto ISO"), (128, "IR Control"),
        ], Form.NikonShootingMode),
        new(Top, "LensType", [(2, "D"), (6, "D G"), (14, "D G VR"), (70, "D G"), (78, "D G VR")]),
        new(Top, "ShutterMode",
        [
            (0, "Mechanical"), (16, "Electronic"), (48, "Electronic Front Curtain"),
            (81, "Auto (Electronic Front Curtain)"),
        ]),
        new(Top, "VignetteControl", [(0, "Off"), (3, "Normal")]),
        new(Top, "Focus", [], Form.Abbreviation)
        {
            Texts = [("AF-S", "Single autofocus"), ("AF-C", "Continuous autofocus"), ("AF-A", "Automatic")],
        },
        new(Top, "FocusDistance", [], Form.NikonFocusDistance),
        new(Top, "DigitalZoom", [], Form.NikonDigitalZoom),
        // The first number says how the sensitivity was set and is not read here.
        new(Top, "ISOSpeed", [], Form.SecondOfTwo),
        new(Top, "ISOSettings", [], Form.SecondOfTwo),
        // Ten places for the retouches applied in the camera; the corpus has none applied.
        new(Top, "RetouchHistory", [(0, "None")], Form.CodeList),
        new(Top, "Lens", [], Form.LensRange),
        new(Top, "LensFStops", [], Form.Nikon3LensFStops),
        new(Top, "SensorPixelSize", [], Form.NikonSensorPixelSize),
        new(Top, "BarometerInfo", [], Form.NikonBarometerInfo),
        new(Top, "ProgramShift", [], Form.NikonFlashComp),
        new(Top, "ExposureDiff", [], Form.NikonFlashComp),
        new(Top, "FlashExposureComp", [], Form.NikonFlashComp),
        new(Top, "FlashBracketComp", [], Form.NikonFlashComp),
        new(Top, "FlashComp", [], Form.NikonFlashComp),

        // ISOInfo (0x0025). The two ISO bytes use Nikon's logarithmic third-stop encoding.
        new(0x0025, "ISO", [], Form.NikonIiIso),
        new(0x0025, "ISO2", [], Form.NikonIiIso),
        new(0x0025, "ISOExpansion",
        [
            (0x000, "Off"), (0x101, "Hi 0.3"), (0x102, "Hi 0.5"), (0x103, "Hi 0.7"), (0x104, "Hi 1.0"),
            (0x105, "Hi 1.3"), (0x106, "Hi 1.5"), (0x107, "Hi 1.7"), (0x108, "Hi 2.0"), (0x109, "Hi 2.3"),
            (0x10A, "Hi 2.5"), (0x10B, "Hi 2.7"), (0x10C, "Hi 3.0"), (0x10D, "Hi 3.3"), (0x10E, "Hi 3.5"),
            (0x10F, "Hi 3.7"), (0x110, "Hi 4.0"), (0x111, "Hi 4.3"), (0x112, "Hi 4.5"), (0x113, "Hi 4.7"),
            (0x114, "Hi 5.0"), (0x201, "Lo 0.3"), (0x202, "Lo 0.5"), (0x203, "Lo 0.7"), (0x204, "Lo 1.0"),
        ]),

        // Lens Data 3 (the LensData array of the note, tag 0x0098).
        new(0x0098, "ExitPupilPosition", [], Form.NikonLd3ExitPupil),
        new(0x0098, "AFAperture", [], Form.NikonLd3Aperture),
        new(0x0098, "FocusDistance", [], Form.NikonLd3FocusDistance),
        new(0x0098, "FocalLength", [], Form.NikonLd3FocalLength),
        new(0x0098, "LensIDNumber", []), // Lens names come from Exiv2's external lens database.
        new(0x0098, "LensFStops", [], Form.NikonLd3LensFStops),
        new(0x0098, "MinFocalLength", [], Form.NikonLd3FocalLength),
        new(0x0098, "MaxFocalLength", [], Form.NikonLd3FocalLength),
        new(0x0098, "MaxApertureAtMinFocal", [], Form.NikonLd3Aperture),
        new(0x0098, "MaxApertureAtMaxFocal", [], Form.NikonLd3Aperture),
        new(0x0098, "EffectiveMaxAperture", [], Form.NikonLd3Aperture),
        new(0x0098, "MaxAperture", [], Form.NikonLd4Aperture),
        new(0x0098, "FNumber", [], Form.NikonLd4Aperture),
        new(0x0098, "FocalLength2", [], Form.NikonLd4FocalLength),
        new(0x0098, "FocusDistance2", [], Form.NikonLd4FocusDistance),

        // The preview directory (0x0011) holds TIFF fields and is read with the EXIF vocabulary.

        // VRInfo (0x001F).
        new(0x001F, "VibrationReduction", [(1, "On"), (2, "Off")]),

        // PictureControl (0x0023): a setting is stored with 128 added, and the ends of the byte are words.
        new(0x0023, "Adjust", [(0, "Default Settings"), (2, "Full Control")]),
        new(0x0023, "QuickAdjust", [(0, "Auto"), (128, "Normal"), (255, "n/a")], Form.Plus128),
        new(0x0023, "Sharpness", [(0, "Auto"), (1, "User")], Form.Plus128),
        new(0x0023, "Contrast", [(0, "Auto"), (128, "Normal"), (255, "n/a")], Form.Plus128),
        new(0x0023, "Brightness", [(0, "Auto"), (128, "Normal")], Form.Plus128),
        new(0x0023, "Saturation", [(0, "Auto"), (128, "Normal"), (255, "n/a")], Form.Plus128),
        new(0x0023, "HueAdjustment", [(0, "Auto"), (1, "User"), (128, "Normal")], Form.Plus128),
        new(0x0023, "FilterEffect", [(128, "Off"), (255, "n/a")]),
        new(0x0023, "ToningEffect", [(255, "n/a")]),
        new(0x0023, "ToningSaturation", [(0, "Auto"), (128, "Normal"), (255, "n/a")], Form.Plus128),

        // WorldTime (0x0024).
        new(0x0024, "Timezone", [], Form.MinutesFromUtc),
        new(0x0024, "DaylightSavings", [(0, "No"), (1, "Yes")]),
        new(0x0024, "DateDisplayFormat", [(0, "Y/M/D"), (1, "M/D/Y"), (2, "D/M/Y")]),


        // AFInfo (0x0088).
        new(0x0088, "AFAreaMode", [(0, "Single Area"), (1, "Dynamic Area")]),
        new(0x0088, "AFPoint", [(0, "Center")]),
        new(0x0088, "AFPointsInFocus",
        [
            (0x0001, "Center"), (0x0002, "Top"), (0x0004, "Bottom"), (0x0008, "Mid-left"),
            (0x0010, "Mid-right"), (0x0020, "Upper-left"), (0x0040, "Upper-right"), (0x0080, "Lower-left"),
            (0x0100, "Lower-right"), (0x0200, "Far Left"), (0x0400, "Far Right"),
        ], Form.NikonAFPointsInFocus),

        // ShotInfo (0x0091). Version 02xx also contains a byte encoded like ISOInfo.ISO.
        new(0x0091, "ISO", [], Form.NikonIiIso),
        new(0x0091, "VibrationReduction", [(0, "Off"), (1, "On (1)")]),
        new(0x0091, "VibrationReduction1", OffOn),
        new(0x0091, "VibrationReduction2", [(0, "n/a")]),

        // FlashInfo (0x00A8).
        new(0x00A8, "FlashSource", [(0, "None"), (1, "External"), (2, "Internal")]),
        new(0x00A8, "ExternalFlashFirmware", [(0, "n/a"), (0x0501, "5.01 (SB-900)"), (0x0701, "7.01 (SB-910)")]),
        new(0x00A8, "FlashColorFilter", [(0, "None")]),
        new(0x00A8, "FlashFocalLength", NotAvailableAtEnds, Form.Millimetres),
        new(0x00A8, "FlashZoomHeadPosition", [(0, "n/a")], Form.Millimetres),
        new(0x00A8, "RepeatingFlashRate", NotAvailableAtEnds, Form.Hertz),
        new(0x00A8, "RepeatingFlashCount", NotAvailableAtEnds),
        new(0x00A8, "FlashGNDistance", NotAvailableAtEnds),
        new(0x00A8, "FlashGroupAControlData", [(0, "Off"), (1, "TTL"), (6, "Manual"), (7, "Repeating Flash")]),
        // Two modes in one byte, a nibble each; listed as the pairs that occur.
        new(0x00A8, "FlashGroupBCControlData",
        [
            (0x00, "Off, Off"), (0x11, "TTL, TTL"), (0x66, "Manual, Manual"),
            (0x77, "Repeating Flash, Repeating Flash"),
        ]),
        new(0x00A8, "CameraExposureCompensation", [], Form.SixthsOfStopDown),
        new(0x00A8, "CameraFlashCompensation", [], Form.SixthsOfStopDown),
        new(0x00A8, "CameraFlashOutput", [], Form.SixthsOfStopDown),
        new(0x00A8, "ExternalFlashData4", [(0, "Illumination Pat.: Standard"), (1, "Illumination Pat.: Center-weighted")]),
        new(0x00A8, "WirelessFlashData", [(0, "AWL Off"), (1, "Optical AWL")]),
        new(0x00A8, "ExternalFlashFlags", [(0, "Fired"), (2, "Bounce Flash"), (4, "Wide Flash Adapter")]),
        new(0x00A8, "FlashGroupAControlMode",
        [
            (0, "Off"), (1, "TTL"), (2, "iTTL"), (3, "Auto Aperture"), (4, "Automatic"),
            (5, "GN (distance priority)"), (6, "Manual"), (7, "Repeating Flash"),
        ]),
        new(0x00A8, "FlashGroupBControlMode",
        [
            (0, "Off"), (1, "TTL"), (2, "iTTL"), (3, "Auto Aperture"), (4, "Automatic"),
            (5, "GN (distance priority)"), (6, "Manual"), (7, "Repeating Flash"),
        ]),
        new(0x00A8, "ExternalFlashData1", [], Form.NikonFlashInfo),
        new(0x00A8, "ExternalFlashData2", [], Form.NikonFlashInfo),
        new(0x00A8, "ExternalFlashData3", [], Form.NikonFlashInfo),
        new(0x00A8, "FlashCompensationMaster", [], Form.NikonFlashInfo),
        new(0x00A8, "FlashCompensationGroupA", [], Form.NikonFlashInfo),
        new(0x00A8, "FlashCompensationGroupB", [], Form.NikonFlashInfo),
        new(0x00A8, "FlashCompensationGroupC", [], Form.NikonFlashInfo),
        new(0x00A8, "FlashMasterOutput", [], Form.NikonFlashInfo),
        new(0x00A8, "FlashGroupAOutput", [], Form.NikonFlashInfo),
        new(0x00A8, "FlashGroupBOutput", [], Form.NikonFlashInfo),
        new(0x00A8, "FlashGroupCOutput", [], Form.NikonFlashInfo),

        // MultiExposure (0x00B0).
        new(0x00B0, "MultiExposureMode", [(0, "Off")]),
        new(0x00B0, "MultiExposureAutoGain", [(0, "Off")]),

        // AFInfo2 (0x00B7).
        new(0x00B7, "ContrastDetectAF", OffOn),
        new(0x00B7, "ContrastDetectAFInFocus", [(0, "No"), (1, "Yes")]),
        new(0x00B7, "PhaseDetectAF",
        [
            (0, "Off"), (1, "On (51-point)"), (2, "On (11-point)"), (3, "On (39-point)"), (7, "On (153-point)"),
        ]),

        // AFTune (0x00B9).
        new(0x00B9, "AFFineTune", [(0, "Off"), (1, "On"), (2, "On")]),
        new(0x00B9, "AFFineTuneAdj", [], Form.NikonRawNumeric),
    ];
}
