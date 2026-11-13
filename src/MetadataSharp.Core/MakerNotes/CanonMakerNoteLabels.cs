namespace MetadataSharp.Core.MakerNotes;

// Canon: the fields of the note and of its arrays that occur in the comparison corpus, with the numbers
// that occur there. Apertures, shutter speeds, exposure values and sensitivities in Canon's logarithmic
// codes, lens types and bit fields are left without an interpretation.
internal static partial class MakerNoteLabels
{
    // Properties, not fields: the tables of the three vendors are initialised in no particular order.
    private static (long Number, string Label)[] OffOn => [(0, "Off"), (1, "On")];

    private static (long Number, string Label)[] NormalHigh => [(0, "Normal"), (1, "High")];

    private static (long Number, string Label)[] CanonWhiteBalance =>
    [
        (0, "Auto"), (1, "Daylight"), (6, "Custom"), (9, "Manual Temperature (Kelvin)"),
        (23, "Auto (ambience priority)"),
    ];

    private static readonly Rule[] Canon =
    [
        // The note's own directory.
        new(Top, "FileNumber", [], Form.DirectoryAndFile),
        new(Top, "FocalLength", [], Form.CanonFocalLength),
        new(Top, "PictureStyleUserDef",
            [(0x00, "None"), (0x01, "Standard"), (0x02, "Portrait"), (0x03, "High Saturation"),
             (0x04, "Adobe RGB"), (0x05, "Low Saturation"), (0x06, "CM Set 1"), (0x07, "CM Set 2"),
             (0x21, "User Def. 1"), (0x22, "User Def. 2"), (0x23, "User Def. 3"),
             (0x41, "PC 1"), (0x42, "PC 2"), (0x43, "PC 3"), (0x81, "Standard"),
             (0x82, "Portrait"), (0x83, "Landscape"), (0x84, "Neutral"), (0x85, "Faithful"),
             (0x86, "Monochrome"), (0x87, "Auto"), (0x88, "Fine Detail")], Form.CanonPictureStyle),
        new(Top, "PictureStylePC",
            [(0x00, "None"), (0x01, "Standard"), (0x02, "Portrait"), (0x03, "High Saturation"),
             (0x04, "Adobe RGB"), (0x05, "Low Saturation"), (0x06, "CM Set 1"), (0x07, "CM Set 2"),
             (0x21, "User Def. 1"), (0x22, "User Def. 2"), (0x23, "User Def. 3"),
             (0x41, "PC 1"), (0x42, "PC 2"), (0x43, "PC 3"), (0x81, "Standard"),
             (0x82, "Portrait"), (0x83, "Landscape"), (0x84, "Neutral"), (0x85, "Faithful"),
             (0x86, "Monochrome"), (0x87, "Auto"), (0x88, "Fine Detail")], Form.CanonPictureStyle),
        new(Top, "SerialNumber", [], Form.CanonSerialNumber),
        new(Top, "ColorSpace",  [(1, "sRGB"), (2, "Adobe RGB")]),
        new(Top, "DateStampMode", [(0, "Off")]),
        new(Top, "SerialNumberFormat", [(0x90000000, "Format 1"), (0xA0000000, "Format 2")]),
        new(Top, "ModelID",
        [
            (0x00000412, "EOS M50 / Kiss M"),
            (0x01110000, "PowerShot S40"),
            (0x01140000, "EOS D30"),
            (0x01560000, "PowerShot A520"),
            (0x03230000, "PowerShot SX240 HS"),
            (0x03980000, "EOS M100"),
            (0x04150000, "PowerShot ELPH 185 / IXUS 185 / IXY 200"),
            (0x80000170, "EOS Digital Rebel / 300D / Kiss Digital"),
            (0x80000175, "EOS 20D"),
            (0x80000215, "EOS-1Ds Mark III"),
            (0x80000218, "EOS 5D Mark II"),
            (0x80000234, "EOS 30D"),
            (0x80000252, "EOS Rebel T1i / 500D / Kiss X3"),
            (0x80000285, "EOS 5D Mark III"),
            (0x80000286, "EOS Rebel T3i / 600D / Kiss X5"),
            (0x80000302, "EOS 6D"),
            (0x80000325, "EOS 70D"),
            (0x80000349, "EOS 5D Mark IV"),
            (0x80000401, "EOS 5DS R"),
            (0x80000424, "EOS R / Ra"),
        ]),

        // AFInfo (0x0026).
        new(0x0026, "AFAreaMode",
        [
            (0, "Off (Manual Focus)"), (2, "Single-point AF"), (4, "Multi-point AF"), (6, "Face + Tracking"),
            (7, "Zone AF"), (8, "AF Point Expansion (4 point)"), (9, "Spot AF"),
            (10, "AF Point Expansion (8 point)"), (11, "Flexizone Multi (49 point)"), (13, "Flexizone Single"),
        ]),
        new(0x0026, "AFPointsInFocus", [], Form.BitPositions),
        new(0x0026, "AFPointsSelected", [], Form.BitPositions),
        new(0x0026, "AFPointsUnusable", [], Form.BitPositions),

        // PictureInfo AF-point masks and Panorama direction.
        new(0x0012, "AFPointsUsed",
            [(0x01, "right"), (0x02, "mid-right"), (0x04, "bottom"), (0x08, "center"),
             (0x10, "top"), (0x20, "mid-left"), (0x40, "left")], Form.BitMask),
        new(0x0012, "AFPointsUsed20D",
            [(0x001, "top"), (0x002, "upper-left"), (0x004, "upper-right"), (0x008, "left"),
             (0x010, "center"), (0x020, "right"), (0x040, "lower-left"), (0x080, "lower-right"),
             (0x100, "bottom")], Form.BitMask),
        new(0x0005, "PanoramaDirection",
            [(0, "Left to right"), (1, "Right to left"), (2, "Bottom to top"), (3, "Top to bottom"),
             (4, "2x2 matrix (Clockwise)")]),

        // CameraSettings (0x0001) and ShotInfo (0x0004).
        new(0x0001, "Lens", [], Form.CanonLens),
        new(0x0001, "MaxAperture", [], Form.CanonAperture),
        new(0x0001, "MinAperture", [], Form.CanonAperture),
        new(0x0001, "FlashDetails",
            [(0x4000, "External flash"), (0x2000, "Internal flash"), (0x0001, "Manual"), (0x0002, "TTL"),
             (0x0004, "A-TTL"), (0x0008, "E-TTL"), (0x0010, "FP sync enabled"),
             (0x0080, "2nd-curtain sync used"), (0x0800, "FP sync used")], Form.BitMask),
        new(0x0004, "AutoISO", [], Form.CanonAutoIso),
        new(0x0004, "ISOSpeed", [], Form.CanonIso),
        new(0x0004, "MeasuredEV", [], Form.CanonMeasuredEv),
        new(0x0004, "MeasuredEV2", [], Form.CanonMeasuredEv2),
        new(0x0004, "TargetAperture", [], Form.CanonAperture),
        new(0x0004, "ApertureValue", [], Form.CanonAperture),
        new(0x0004, "TargetShutterSpeed", [], Form.CanonShutter),
        new(0x0004, "BulbDuration", [], Form.CanonBulbDuration),
        new(0x0004, "ShutterSpeedValue", [], Form.CanonShutter),
        new(0x0004, "FlashGuideNumber", [], Form.CanonFlashGuideNumber),
        new(0x0004, "AFPointUsed", [], Form.CanonAfPointUsed),
        new(0x0093, "FileNumber", [], Form.DirectoryAndFile),
        new(0x0001, "Macro", [(2, "Off")]),
        new(0x0001, "Selftimer", [(0, "Off")], Form.TenthsOfSecond),
        new(0x0001, "Quality", [(2, "Normal"), (3, "Fine"), (4, "RAW"), (5, "Superfine"), (7, "CRAW")]),
        new(0x0001, "FlashMode", [(0, "Off"), (1, "Auto"), (6, "On + red-eye"), (16, "External")]),
        new(0x0001, "DriveMode",
        [
            (0, "Single / timer"), (1, "Continuous"), (4, "Continuous, low"), (5, "Continuous, high"),
            (9, "Single, Silent"), (10, "Continuous, Silent"),
        ]),
        new(0x0001, "FocusMode",
        [
            (0, "One shot AF"), (1, "AI servo AF"), (2, "AI focus AF"), (3, "Manual focus (3)"), (4, "Single"),
            (256, "AF + MF"),
        ]),
        new(0x0001, "RecordMode", [(1, "JPEG"), (2, "CRW+THM"), (6, "CR2"), (7, "CR2+JPEG"), (12, "CR3")]),
        new(0x0001, "ImageSize",
            [(0, "Large"), (2, "Small"), (5, "Medium 1"), (14, "Small 1"), (15, "Small 2"), (16, "Small 3")]),
        new(0x0001, "EasyMode", [(0, "Full auto"), (1, "Manual")]),
        new(0x0001, "DigitalZoom", [(0, "None")]),
        new(0x0001, "Contrast", NormalHigh),
        new(0x0001, "Saturation", NormalHigh),
        new(0x0001, "Sharpness", NormalHigh),
        // 16 to 19 name a sensitivity; a number with bit 14 set carries one and is not read here.
        new(0x0001, "ISOSpeed", [(0, "n/a"), (15, "Auto"), (17, "100"), (16484, "100")]),
        new(0x0001, "MeteringMode", [(1, "Spot"), (3, "Evaluative"), (4, "Partial"), (5, "Center-weighted average")]),
        new(0x0001, "FocusType", [(1, "Auto"), (2, "Not known")]),
        new(0x0001, "AFPoint",
        [
            (0x2005, "Manual AF point selection"), (0x3001, "Auto-selected"), (0x3003, "Center"),
            (0x4001, "Auto AF point selection"), (0x4006, "Face Detect"),
        ]),
        new(0x0001, "ExposureProgram",
        [
            (0, "Easy shooting (Auto)"), (1, "Program (P)"), (2, "Shutter priority (Tv)"),
            (3, "Aperture priority (Av)"), (4, "Manual (M)"),
        ]),
        new(0x0001, "FlashActivity", [(0, "Did not fire")]),
        new(0x0001, "FocusContinuous", [(0, "Single"), (1, "Continuous")]),
        new(0x0001, "AESetting", [(0, "Normal AE"), (2, "AE lock"), (65535, "n/a")]),
        new(0x0001, "ImageStabilization", [(257, "On (2)")]),
        new(0x0001, "SpotMeteringMode", [(0, "Center"), (1, "AF Point"), (65535, "n/a")]),
        new(0x0001, "PhotoEffect", [(0, "Off"), (65535, "n/a")]),
        new(0x0001, "ManualFlashOutput", [(0, "n/a")]),
        new(0x0001, "SRAWQuality", [(0, "n/a"), (1, "sRAW1 (mRAW)"), (2, "sRAW2 (sRAW)")]),

        // ShotInfo (0x0004).
        new(0x0004, "WhiteBalance", CanonWhiteBalance),
        new(0x0004, "SlowShutter", [(0, "Off"), (3, "None")]),
        new(0x0004, "CameraTemperature", [(0, "--")], Form.CelsiusPlus128),
        new(0x0004, "FlashBias", [(0, "0 EV")]),
        new(0x0004, "AutoExposureBracketing", [(0, "Off")]),
        new(0x0004, "SubjectDistance", [(65535, "Infinite")], Form.Centimetres),
        new(0x0004, "CameraType", [(0, "n/a"), (248, "EOS High-end"), (250, "Compact"), (252, "EOS Mid-range")]),
        new(0x0004, "AutoRotate", [(0, "None"), (1, "Rotate 90 CW"), (3, "Rotate 270 CW")]),
        new(0x0004, "NDFilter", [(0, "Off"), (65535, "n/a")]),
        new(0x0004, "SelfTimer2", [(65535, "n/a"), (0, "Selftimer 2s"), (1, "Selftimer 10s")]),

        // FileInfo (0x0093).
        new(0x0093, "BracketMode", [(0, "Off"), (1, "AEB")]),
        new(0x0093, "RawJpgQuality", [(-1, "n/a"), (0, "unkown"), (1, "Economy"), (2, "Normal"),
            (3, "Fine"), (4, "RAW"), (5, "Superfine"), (7, "CRAW")]),
        new(0x0093, "RFLensType", [(0, "n/a"), (274, "Canon RF 24-105mm F4-7.1 IS STM")]),
        new(0x0093, "RawJpgSize", [(0, "Large"), (-1, "n/a")]),
        new(0x0093, "NoiseReduction", [(0, "Off"), (3, "On"), (4, "Auto")]),
        new(0x0093, "WBBracketMode", [(0, "Off")]),
        new(0x0093, "FilterEffect", [(0, "None")]),
        new(0x0093, "ToningEffect", [(0, "None")]),
        new(0x0093, "LiveViewShooting", OffOn),
        new(0x0093, "FocusDistanceUpper", [(-1, "Infinite")], Form.CentimetresFixed),
        new(0x0093, "FocusDistanceLower", [], Form.CentimetresFixed),
        new(0x0093, "ShutterMode", [(0, "Mechanical"), (1, "Electronic First Curtain")]),
        new(0x0093, "FlashExposureLock", [(0, "Off")]),
        new(0x0093, "AntiFlicker", [(0, "Off")]),

        // ProcessingInfo (0x00A0).
        new(0x00A0, "ToneCurve", [(0, "Standard")]),
        new(0x00A0, "SharpnessFrequency", [(0, "n/a")]),
        // The same numbers as ShotInfo's WhiteBalance; -1, which is what the corpus has, is none of them.
        new(0x00A0, "WhiteBalance", CanonWhiteBalance),
        new(0x00A0, "PictureStyle",
        [
            (0, "None"), (33, "User Def. 1"), (34, "User Def. 2"), (129, "Standard"), (131, "Landscape"),
            (132, "Neutral"), (133, "Faithful"), (135, "Auto"),
        ], Form.CanonPictureStyle),

        // TimeInfo (0x0035).
        new(0x0035, "TimeZoneCity", [(0, "n/a"), (19, "Paris"), (20, "London"), (30, "Los Angeles"), (32766, "(not set)")]),

        // AFMicroAdj (0x4013).
        new(0x4013, "AFMicroAdjMode", [(0, "Disable"), (2, "Adjust by lens")]),

        // VignettingCorr2 (0x4016).
        new(0x4016, "PeripheralLightingSetting", OffOn),
        new(0x4016, "ChromaticAberrationSetting", OffOn),
        new(0x4016, "DistortionCorrectionSetting", OffOn),

        // AFConfig (0x4028).
        new(0x4028, "SelectAFAreaSelectionMode", [], Form.CanonRaw),

        // LightingOpt (0x4018).
        new(0x4018, "PeripheralIlluminationCorr", OffOn),
        new(0x4018, "AutoLightingOptimizer", [(0, "Standard"), (1, "Low"), (3, "Off")]),
        new(0x4018, "HighlightTonePriority", OffOn),
        new(0x4018, "LongExposureNoiseReduction", [(0, "Off"), (1, "Auto"), (2, "On")]),
        new(0x4018, "HighISONoiseReduction", [(0, "Standard"), (1, "Low"), (2, "Strong"), (3, "Off")]),
        new(0x4018, "DigitalLensOptimizer", [(0, "Off"), (1, "Standard")]),
        new(0x4018, "DualPixelRaw", [(0, "Off")]),

        // LensInfo (0x4019): the serial number is five bytes, written as their hexadecimal digits.
        new(0x4019, "LensSerialNumber", [], Form.Hex),

        // AmbienceInfo (0x4020), MultiExp (0x4021), HDRInfo (0x4025).
        new(0x4020, "AmbienceSelection", [(0, "Standard")]),
        new(0x4021, "MultiExposure", [(0, "Off")]),
        new(0x4021, "MultiExposureControl", [(0, "Additive")]),
        new(0x4021, "MultiExposureShots", [(0, "Off")]),
        new(0x4025, "HDR", [(0, "Off")]),
        new(0x4025, "HDREffect", [(0, "Natural")]),

        // AFConfig (0x4028).
        new(0x4028, "AIServoFirstImage", [(0, "Equal Priority"), (2, "Focus Priority")]),
        new(0x4028, "AIServoSecondImage", [(0, "Equal Priority"), (4, "Focus High Priority")]),
        new(0x4028, "USMLensElectronicMF", [(0, "Enable After AF")]),
        new(0x4028, "AFAssistBeam", [(0, "Enable"), (1, "Disable")]),
        new(0x4028, "OneShotAFRelease", [(0, "Focus Priority"), (1, "Release Priority")]),
        new(0x4028, "AutoAFPointSelEOSiTRAF", [(0, "Enable")]),
        new(0x4028, "LensDriveWhenAFImpossible", [(0, "Continue Focus Search"), (1, "Stop Focus Search")]),
        new(0x4028, "AFAreaSelectionMethod", [(0, "M-Fn Button"), (1, "Main Dial")]),
        new(0x4028, "OrientationLinkedAF", [(0, "Same for Vert/Horiz Points"), (2, "Separate Area+Points")]),
        new(0x4028, "ManualAFPointSelPattern", [(0, "Stops at AF Area Edges"), (1, "Continuous")]),
        new(0x4028, "AFPointDisplayDuringFocus", [(0, "Selected (constant)")]),
        new(0x4028, "VFDisplayIllumination", [(0, "Auto")]),
        new(0x4028, "AFStatusViewfinder", [(0, "Auto"), (1, "Enable")]),
        new(0x4028, "InitialAFPointInServo", [(0, "Initial AF Point Selected"), (2, "Auto")]),
    ];
}
