namespace MetadataSharp.Core.MakerNotes;

// Sony (both layouts share one vocabulary): the fields of the note and of its arrays that occur in the
// comparison corpus, with the numbers that occur there. The fields whose reading depends on the camera
// model (the autofocus settings and points, FocusMode, lens identifiers, temperatures) and LensSpec are
// left without an interpretation.
internal static partial class MakerNoteLabels
{
    private static (long Number, string Label)[] OffAutoNotAvailable => [(0, "Off"), (2, "Auto"), (0xFFFFFFFF, "n/a")];

    private static (long Number, string Label)[] OffNotAvailable => [(0, "Off"), (65535, "n/a")];

    private static readonly Rule[] Sony =
    [
        // The note's own directory.
        new(Top, "Quality", [(2, "Fine"), (6, "RAW + JPEG/HEIF")]),
        new(Top, "FlashLevel", [(0, "Normal"), (-1, "-0.3 EV")]),
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
        new(Top, "FullImageSize", [], Form.HeightThenWidth),
        new(Top, "PreviewImageSize", [], Form.HeightThenWidth),
        // The file type and its version, a byte each.
        new(Top, "FileFormat",
        [
            (0x00000002, "JPEG"), (0x03020000, "ARW 2.2"), (0x03030000, "ARW 2.3"), (0x03030100, "ARW 2.3.1"),
            (0x03030200, "ARW 2.3.2"), (0x03030300, "ARW 2.3.3"), (0x03030500, "ARW 2.3.5"), (0x04000000, "ARW 4.0"),
        ], Form.FourBytes),
        new(Top, "SonyModelID",
        [
            (0, "Multiple camera models"), (2, "DSC-R1"), (256, "DSLR-A100"), (286, "SLT-A65 / SLT-A65V"),
            (287, "SLT-A77 / SLT-A77V"), (288, "NEX-5N"), (295, "NEX-6"), (297, "DSC-RX100"), (302, "ILCE-3000 / ILCE-3500"),
            (303, "SLT-A58"), (312, "ILCE-6000"), (353, "ILCA-68"), (354, "ILCA-99M2"), (363, "ILCE-7M3"),
            (369, "DSC-RX100M5A"), (383, "ILCE-7SM3"),
        ]),

        // The array of tag 0x9400.
        new(0x9400, "ReleaseMode2", [(0, "Normal"), (2, "Continuous - Exposure Bracketing")]),
        new(0x9400, "CameraOrientation", [(1, "Horizontal (normal)")]),
        new(0x9400, "SequenceImageNumber", [], Form.OneBased),
        new(0x9400, "SequenceFileNumber", [], Form.OneBased),
        new(0x9400, "SequenceLength1", [], Form.Shots),
        new(0x9400, "SequenceLength2", [], Form.Files),

        // The array of tag 0x9402.
        new(0x9402, "AFAreaMode",
        [
            (0, "Multi"), (1, "Center"), (3, "Flexible Spot"), (12, "Expanded Flexible Spot"), (14, "Tracking"),
            (15, "Face Tracking"),
        ]),

        // The array of tag 0x9404.
        new(0x9404, "ExposureProgram",
        [
            (0, "Program AE"), (1, "Aperture-priority AE"), (2, "Shutter speed priority AE"), (3, "Manual"),
            (7, "iAuto+"),
        ]),
        new(0x9404, "IntelligentAuto", OffOn),
    ];
}
