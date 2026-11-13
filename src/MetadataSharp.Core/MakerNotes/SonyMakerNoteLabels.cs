namespace MetadataSharp.Core.MakerNotes;

// Sony (both layouts share one vocabulary): fields from the comparison corpus. Model-dependent autofocus
// and temperature readings use the enclosing model and the sibling values that establish their validity;
// lens identifiers remain raw because their names come from an external lens database.
internal static partial class MakerNoteLabels
{
    private static (long Number, string Label)[] OffAutoNotAvailable => [(0, "Off"), (2, "Auto"), (0xFFFFFFFF, "n/a")];

    private static (long Number, string Label)[] OffNotAvailable => [(0, "Off"), (65535, "n/a")];

    private static readonly Rule[] Sony =
    [
        // The note's own directory.
        new(Top, "Quality", [(2, "Fine"), (6, "RAW + JPEG/HEIF")]),
        new(Top, "CreativeStyle", [], Form.Abbreviation)
        {
            Texts =
            [
                ("AdobeRGB", "Adobe RGB"), ("Autumnleaves", "Autumn leaves"), ("BW", "Black and White"),
                ("Clear", "Clear"), ("Deep", "Deep"), ("FL", "FL"), ("IN", "IN"),
                ("Landscape", "Landscape"), ("Light", "Light"), ("Neutral", "Neutral"),
                ("None", "None"), ("Portrait", "Portrait"), ("Real", "Real"), ("SH", "SH"),
                ("Sepia", "Sepia"), ("Standard", "Standard"), ("Sunset", "Sunset"),
                ("Vivid", "Vivid"), ("VV2", "VV2"),
            ],
        },
        new(Top, "FlashLevel", [(0, "Normal"), (-1, "-0.3 EV")]),
        new(Top, "FlashExposureComp", [], Form.SonyExposureBias),
        new(Top, "ExposureStandardAdjustment", [], Form.SonyExposureStandardAdjustment),
        new(Top, "FlexibleSpotPosition", [], Form.SonyFlexibleSpotPosition),
        new(Top, "FocusMode2", [], Form.SonyFocusMode2),
        new(Top, "AFAreaModeSetting", [], Form.SonyAFAreaModeSetting),
        new(Top, "HighISONoiseReduction2", [], Form.SonyHighIso2),
        new(Top, "AFTracking", [], Form.SonyAFTracking),
        new(Top, "AFMode", [], Form.SonyAFMode),
        new(Top, "FocusMode", [], Form.SonyFocusMode),
        new(Top, "AFPointSelected", [], Form.SonyAFPointSelected),
        new(Top, "AFPointsUsed", [], Form.SonyAFPointsUsed),
        new(Top, "FocalPlaneAFPointsUsed", [], Form.SonyFocalPlaneAFPointsUsed),
        new(Top, "FocusMode3", [], Form.SonyFocusMode3),
        new(Top, "LensSpec", [], Form.SonyLensSpec),
        new(Top, "LensID", []),
        new(Top, "Quality2", [], Form.SonyQuality2),
        new(Top, "VariableLowPassFilter", [], Form.SonyVariableLowPass),
        new(Top, "ReleaseMode", [(0, "Normal"), (2, "Continuous"), (5, "Exposure Bracketing"), (65535, "n/a")]),
        new(Top, "SequenceNumber", [(0, "Single"), (65535, "n/a")]),
        new(Top, "AntiBlur", [(0, "Off"), (2, "On (Shooting)"), (65535, "n/a")]),
        new(Top, "DynamicRangeOptimizer", [(0, "Off"), (1, "Standard"), (3, "Auto")]),
        new(Top, "DynamicRangeOptimizer2", [(0, "Off"), (1, "Standard")]),
        new(Top, "ImageStabilization", OffOn),
        new(Top, "ColorMode", [(0, "Standard")]),
        new(Top, "SceneMode", [(0, "Standard"), (16, "Auto"), (22, "Auto+")]),
        new(Top, "ZoneMatching", [(0, "ISO Setting Used")]),
        new(Top, "Teleconverter", [(0, "None")]),
        new(Top, "WhiteBalance", [(0, "Auto"), (112, "Custom")]),
        new(Top, "WhiteBalance2", [(0, "Auto"), (4, "Manual")]),
        new(Top, "ExposureMode",
        [
            (0, "Program AE"), (6, "Auto"), (7, "Aperture-priority AE"), (8, "Shutter speed priority AE"),
            (15, "Manual"), (65535, "n/a"),
        ]),
        new(Top, "JPEGQuality", [(1, "Fine"), (65535, "n/a")]),
        new(Top, "JPEGHEIFSwitch", [(0, "JPEG")]),
        new(Top, "RAWFileType", [(0, "Compressed RAW"), (1, "Uncompressed RAW")]),
        new(Top, "PrioritySetInAWB", [(0, "Standard")]),
        new(Top, "MeteringMode2", [(256, "Multi-segment")]),
        new(Top, "AFIlluminator", OffNotAvailable),
        new(Top, "Macro", OffNotAvailable),
        new(Top, "MultiBurstMode", [(0, "Off")]),
        new(Top, "FlashAction", [(0, "Did not fire")]),
        new(Top, "ElectronicFrontCurtainShutter", OffOn),
        new(Top, "IntelligentAuto", OffOn),
        new(Top, "AutoPortraitFramed", [(0, "No")]),
        new(Top, "PictureEffect", [(0, "Off")]),
        new(Top, "SoftSkinEffect", [(0, "Off")]),
        new(Top, "AutoHDR", [(0, "Off, Uncorrected image")]),
        new(Top, "MultiFrameNoiseReduction", [(0, "Off")]),
        new(Top, "MultiFrameNREffect", [(0, "Normal")]),
        new(Top, "HighISONoiseReduction", [(0, "Off"), (1, "Low"), (2, "Normal")]),
        new(Top, "LongExposureNoiseReduction", [(0, "Off"), (1, "On (unused)")]),
        new(Top, "VignettingCorrection", OffAutoNotAvailable),
        new(Top, "LateralChromaticAberration", OffAutoNotAvailable),
        new(Top, "DistortionCorrectionSetting", OffAutoNotAvailable),
        new(Top, "ColorTemperature", [(0, "Auto")], Form.Kelvin),
        new(Top, "ColorCompensationFilter", [], Form.GreenMagenta),
        new(Top, "WBShiftABGM", [], Form.AmberBlueGreenMagenta),
        new(Top, "WBShiftABGMPrecise", [], Form.AmberBlueGreenMagentaThousandths),
        new(Top, "FullImageSize", [], Form.HeightThenWidth),
        new(Top, "PreviewImageSize", [], Form.HeightThenWidth),
        // The file type and its version, a byte each.
        new(Top, "FileFormat",
        [
            (0x00000002, "JPEG"), (0x03020000, "ARW 2.2"), (0x03030000, "ARW 2.3"), (0x03030100, "ARW 2.3.1"),
            (0x03030200, "ARW 2.3.2"), (0x03030300, "ARW 2.3.3"), (0x03030500, "ARW 2.3.5"), (0x04000000, "ARW 4.0"),
        ], Form.FourBytes),
        new(Top, "SonyModelID", [], Form.SonyModelId),

        // The array of tag 0x9400.
        new(0x9400, "ReleaseMode2", [(0, "Normal"), (2, "Continuous - Exposure Bracketing")]),
        new(0x9400, "CameraOrientation", [(1, "Horizontal (normal)")]),
        new(0x9400, "SequenceImageNumber", [], Form.OneBased),
        new(0x9400, "SequenceFileNumber", [], Form.OneBased),
        new(0x9400, "SequenceLength1", [], Form.Shots),
        new(0x9400, "SequenceLength2", [], Form.Files),
        new(0x9400, "Quality2", [(0, "JPEG"), (2, "Raw + JPEG")]),
        new(0x9400, "SonyImageHeight", [], Form.TimesEight),
        new(0x9400, "ModelReleaseYear", [(255, "n/a")], Form.YearSince2000),

        // The array of tag 0x9402. The top bit of the focus mode is not part of the mode.
        new(0x9402, "FocusMode", [(0, "Manual"), (2, "AF-S"), (3, "AF-C"), (4, "AF-A"), (6, "DMF")],
            Form.LowSevenBits),
        new(0x9402, "AFAreaMode",
        [
            (0, "Multi"), (1, "Center"), (2, "Spot"), (3, "Flexible Spot"), (10, "Selective (for Miniature effect)"),
            (11, "Zone"), (12, "Expanded Flexible Spot"), (14, "Tracking"), (15, "Face Tracking"),
            (20, "Animal Eye Tracking"), (255, "Manual"),
        ]),
        new(0x9402, "AmbientTemperature", [], Form.SonyAmbientTemperature),
        new(0x9402, "FocusPosition2", [], Form.SonyFocusPosition),

        // The array of tag 0x9403.
        new(0x9403, "CameraTemperature", [], Form.SonyMisc1Temperature),

        // The array of tag 0x9404.
        new(0x9404, "ExposureProgram",
        [
            (0, "Program AE"), (1, "Aperture-priority AE"), (2, "Shutter speed priority AE"), (3, "Manual"),
            (7, "iAuto+"),
        ]),
        new(0x9404, "IntelligentAuto", OffOn),
        new(0x9404, "LensZoomPosition", [], Form.PercentOf1024),
        new(0x9404, "FocusPosition2", [], Form.SonyMisc2bFocusPosition),
    ];
}
