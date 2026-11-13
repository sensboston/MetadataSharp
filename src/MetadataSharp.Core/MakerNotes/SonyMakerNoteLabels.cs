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
        new(Top, "MultiBurstMode", [(0, "Off"), (1, "On")]),
        new(Top, "PixelShiftInfo", [], Form.SonyPixelShiftInfo),
        new(Top, "FocusFrameSize", [], Form.SonyFocusFrameSize),
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
        new(0x9400, "ShotNumberSincePowerUp", [], Form.SonyMisc3cShotNumber),
        new(0x9400, "SequenceFileNumber", [], Form.OneBased),
        new(0x9400, "SequenceLength1", [], Form.Shots),
        new(0x9400, "SequenceLength2", [], Form.Files),
        new(0x9400, "Quality2", [], Form.SonyMisc3cQuality2),
        new(0x9400, "SonyImageHeight", [], Form.SonyMisc3cImageHeight),
        new(0x9400, "ModelReleaseYear", [], Form.SonyMisc3cReleaseYear),

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
        new(0x9404, "LensZoomPosition", [], Form.SonyMisc2bZoom),
        new(0x9404, "FocusPosition2", [], Form.SonyMisc2bFocusPosition),

        // The Minolta directory referenced by Sony's DSLR-A100 MakerNote.
        new(0xB028, "SceneMode",
        [
            (0, "Standard"), (1, "Portrait"), (2, "Text"), (3, "Night Scene"), (4, "Sunset"), (5, "Sports"),
            (6, "Landscape"), (7, "Night Portrait"), (8, "Macro"), (9, "Super Macro"), (16, "Auto"),
            (17, "Night View/Portrait"), (18, "Sweep Panorama"), (19, "Handheld Night Shot"),
            (20, "Anti Motion Blur"), (21, "Cont. Priority AE"), (22, "Auto+"), (23, "3D Sweep Panorama"),
            (24, "Superior Auto"), (25, "High Sensitivity"), (26, "Fireworks"), (27, "Food"), (28, "Pet"),
            (33, "HDR"), (65535, "n/a"),
        ]),
        new(0xB028, "ColorMode",
        [
            (0, "Natural Color"), (1, "Black & White"), (2, "Vivid Color"), (3, "Solarization"), (4, "AdobeRGB"),
            (5, "Sepia"), (9, "Natural"), (12, "Portrait"), (13, "Natural sRGB"), (14, "Natural+ sRGB"),
            (15, "Landscape"), (16, "Evening"), (17, "Night Scene"), (18, "Night Portrait"),
        ]),
        new(0xB028, "Quality", [(0, "Raw"), (1, "Super Fine"), (2, "Fine"), (3, "Standard"), (4, "Economy"), (5, "Extra Fine")]),
        new(0xB028, "FlashExposureComp", [], Form.SonyExposureBias),
        new(0xB028, "Teleconverter", [(0, "None"), (4, "Minolta/Sony AF 1.4x APO (D) (0x04)"),
            (5, "Minolta/Sony AF 2x APO (D) (0x05)"), (72, "Minolta/Sony AF 2x APO (D)"),
            (80, "Minolta AF 2x APO II"), (96, "Minolta AF 2x APO"), (136, "Minolta/Sony AF 1.4x APO (D)"),
            (144, "Minolta AF 1.4x APO II"), (160, "Minolta AF 1.4x APO")]),
        new(0xB028, "RawAndJpgRecording", [(0, "Off"), (1, "On")]),
        new(0xB028, "ZoneMatching", [(0, "ISO Setting Used"), (1, "High Key"), (2, "Low Key")]),
        new(0xB028, "ImageStabilizationA100", [(0, "Off"), (1, "On")]),
        new(0xB028, "WhiteBalance", [(0, "Auto"), (1, "Color Temperature/Color Filter"), (16, "Daylight"),
            (32, "Cloudy"), (48, "Shade"), (64, "Tungsten"), (80, "Flash"), (96, "Fluorescent"), (112, "Custom")]),

        // Sony camera settings, including the A100 layout of tag 0x0114.
        new(0x0114, "DriveMode",
        [
            (1, "Single Frame"), (2, "Continuous High"), (4, "Self-timer 10 sec"),
            (5, "Self-timer 2 sec, Mirror Lock-up"), (6, "Single-frame Bracketing"), (7, "Continuous Bracketing"),
            (10, "Remote Commander"), (11, "Mirror Lock-up"), (18, "Continuous Low"),
            (24, "White Balance Bracketing Low"), (25, "D-Range Optimizer Bracketing Low"),
            (40, "White Balance Bracketing High"), (41, "D-Range Optimizer Bracketing High"),
        ]),
        new(0x0114, "FocusMode", [(0, "Manual"), (1, "AF-S"), (2, "AF-C"), (3, "AF-A")]),
        new(0x0114, "AFAreaMode", [(0, "Wide"), (1, "Local"), (2, "Spot")]),
        new(0x0114, "LocalAFAreaPoint",
        [
            (1, "Center"), (2, "Top"), (3, "Top-Right"), (4, "Right"), (5, "Bottom-Right"), (6, "Bottom"),
            (7, "Bottom-Left"), (8, "Left"), (9, "Top-Left"), (10, "Far-Right"), (11, "Far-Left"),
        ]),
        new(0x0114, "MeteringMode", [(1, "Multi-segment"), (2, "Center weighted average"), (4, "Spot")]),
        new(0x0114, "DynamicRangeOptimizerMode", [(0, "Off"), (1, "Standard"), (2, "Advanced Auto"),
            (3, "Advanced Level"), (4097, "Auto")]),
        new(0x0114, "CreativeStyle",
        [
            (1, "Standard"), (2, "Vivid"), (3, "Portrait"), (4, "Landscape"), (5, "Sunset"),
            (6, "Night View/Portrait"), (8, "Black & White"), (9, "Adobe RGB"), (11, "Neutral"),
            (12, "Clear"), (13, "Deep"), (14, "Light"), (15, "Autumn"), (16, "Sepia"),
        ]),
        new(0x0114, "FlashMode", [(0, "ADI"), (1, "TTL")]),
        new(0x0114, "PrioritySetupShutterRelease", [(0, "AF"), (1, "Release")]),
        new(0x0114, "AFIlluminator", [(0, "Auto"), (1, "Off"), (65535, "n/a")]),
        new(0x0114, "AFWithShutter", [(0, "On"), (1, "Off")]),
        new(0x0114, "LongExposureNoiseReduction", [(0, "Off"), (1, "On")]),
        new(0x0114, "ImageStyle",
        [
            (1, "Standard"), (2, "Vivid"), (3, "Portrait"), (4, "Landscape"), (5, "Sunset"),
            (7, "Night View/Portrait"), (8, "B&W"), (9, "Adobe RGB"), (11, "Neutral"),
            (129, "StyleBox1"), (130, "StyleBox2"), (131, "StyleBox3"), (132, "StyleBox4"),
            (133, "StyleBox5"), (134, "StyleBox6"),
        ]),
        new(0x0114, "ExposureProgram",
        [
            (0, "Auto"), (1, "Manual"), (2, "Program AE"), (3, "Aperture-priority AE"),
            (4, "Shutter speed priority AE"), (8, "Program Shift A"), (9, "Program Shift S"),
            (16, "Portrait"), (17, "Sports"), (18, "Sunset"), (19, "Night Portrait"),
            (20, "Landscape"), (21, "Macro"), (35, "Auto No Flash"),
        ]),
        new(0x0114, "ImageStabilization", [(0, "Off"), (1, "On")]),
        new(0x0114, "Rotation", [(0, "Horizontal (normal)"), (1, "Rotate 90 CW"), (2, "Rotate 270 CW")]),
        new(0x0114, "SonyImageSize", [(1, "Large"), (2, "Medium"), (3, "Small")]),
        new(0x0114, "AspectRatio", [(1, "3:2"), (2, "16:9")]),
        new(0x0114, "Quality",
        [
            (0, "RAW"), (2, "CRAW"), (16, "Extra Fine"), (32, "Fine"), (34, "RAW+JPEG"), (35, "CRAW+JPEG"),
            (48, "Standard"),
        ]),
        new(0x0114, "ExposureLevelIncrements", [(33, "1/3 EV"), (50, "1/2 EV")]),
    ];
}
