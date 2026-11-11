namespace MetadataSharp.Core.MakerNotes;

// Nikon (the Nikon3 layout): the fields of the note and of its arrays that occur in the comparison
// corpus, with the numbers that occur there. Lens data (focal lengths, apertures and distances in
// logarithmic codes), the sensitivities of ISOInfo, lens identifiers and the flash outputs whose reading
// depends on the control mode are left without an interpretation. Two FlashInfo fields,
// ExternalFlashData1 and ExternalFlashData2, mean different things in versions 0106 and 0107 and are
// left out for that reason.
internal static partial class MakerNoteLabels
{
    private static (long Number, string Label)[] NotAvailableAtEnds => [(0, "n/a"), (255, "n/a")];

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
        ]),
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
        // The first number says how the sensitivity was set and is not read here.
        new(Top, "ISOSpeed", [], Form.SecondOfTwo),
        new(Top, "ISOSettings", [], Form.SecondOfTwo),
        // Ten places for the retouches applied in the camera; the corpus has none applied.
        new(Top, "RetouchHistory", [(0, "None")], Form.CodeList),
        new(Top, "Lens", [], Form.LensRange),
        new(Top, "ExposureDiff", [], Form.StopFraction),
        new(Top, "ProgramShift", [], Form.StopFraction),
        new(Top, "FlashExposureComp", [], Form.StopFraction),
        new(Top, "FlashBracketComp", [], Form.StopFraction),
        new(Top, "FlashComp", [], Form.StopFraction),

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

        // ISOInfo (0x0025).
        new(0x0025, "ISOExpansion", [(0, "Off")]),
        new(0x0025, "ISOExpansion2", [(0, "Off")]),

        // AFInfo (0x0088).
        new(0x0088, "AFAreaMode", [(0, "Single Area"), (1, "Dynamic Area")]),
        new(0x0088, "AFPoint", [(0, "Center")]),

        // ShotInfo (0x0091).
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
    ];
}
