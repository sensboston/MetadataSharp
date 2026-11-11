namespace MetadataSharp.Core.MakerNotes;

/// <summary>Olympus MakerNote tag names: the main directory and its sub-directories, ascending by ID.</summary>
internal static class OlympusMakerNoteNames
{
    internal static string? Main(ushort tag) => MakerNoteReader.Lookup(MainNames, tag);

    internal static readonly (ushort Id, string Name)[] MainNames =
    [
        (0x0100, "ThumbnailImage"), (0x0104, "BodyFirmwareVersion"), (0x0200, "SpecialMode"),
        (0x0201, "Quality"), (0x0202, "Macro"), (0x0203, "BWMode"),
        (0x0204, "DigitalZoom"), (0x0205, "FocalPlaneDiagonal"), (0x0206, "LensDistortionParams"),
        (0x0207, "CameraType"), (0x0208, "PictureInfo"), (0x0209, "CameraID"),
        (0x020B, "ImageWidth2"), (0x020C, "ImageHeight2"), (0x020D, "Software"),
        (0x0280, "PreviewImage"), (0x0300, "PreCaptureFrames"), (0x0301, "WhiteBoard"),
        (0x0302, "OneTouchWB"), (0x0303, "WhiteBalanceBracket"), (0x0304, "WhiteBalanceBias"),
        (0x0403, "SceneMode"), (0x0404, "Firmware"), (0x0E00, "PrintIM"),
        (0x0F00, "DataDump1"), (0x0F01, "DataDump2"), (0x1000, "ShutterSpeed"),
        (0x1001, "ISOSpeed"), (0x1002, "ApertureValue"), (0x1003, "Brightness"),
        (0x1004, "FlashMode"), (0x1005, "FlashDevice"), (0x1006, "Bracket"),
        (0x1007, "SensorTemperature"), (0x1008, "LensTemperature"), (0x1009, "LightCondition"),
        (0x100A, "FocusRange"), (0x100B, "FocusMode"), (0x100C, "FocusDistance"),
        (0x100D, "Zoom"), (0x100E, "MacroFocus"), (0x100F, "SharpnessFactor"),
        (0x1010, "FlashChargeLevel"), (0x1011, "ColorMatrix"), (0x1012, "BlackLevel"),
        (0x1015, "WhiteBalance"), (0x1017, "RedBalance"), (0x1018, "BlueBalance"),
        (0x1019, "ColorMatrixNumber"), (0x101A, "SerialNumber2"), (0x1023, "FlashBias"),
        (0x1026, "ExternalFlashBounce"), (0x1027, "ExternalFlashZoom"), (0x1028, "ExternalFlashMode"),
        (0x1029, "Contrast"), (0x102A, "SharpnessFactor2"), (0x102B, "ColorControl"),
        (0x102C, "ValidBits"), (0x102D, "CoringFilter"), (0x102E, "ImageWidth"),
        (0x102F, "ImageHeight"), (0x1034, "CompressionRatio"), (0x1035, "Thumbnail"),
        (0x1036, "ThumbnailOffset"), (0x1037, "ThumbnailLength"), (0x1039, "CCDScanMode"),
        (0x103A, "NoiseReduction"), (0x103B, "InfinityLensStep"), (0x103C, "NearLensStep"),
        (0x2010, "Equipment"), (0x2020, "CameraSettings"), (0x2030, "RawDevelopment"),
        (0x2031, "RawDevelopment2"), (0x2040, "ImageProcessing"), (0x2050, "FocusInfo"),
        (0x3000, "RawInfo"),
    ];

    internal static string? Cs(ushort tag) => MakerNoteReader.Lookup(CsNames, tag);

    internal static readonly (ushort Id, string Name)[] CsNames =
    [
        (0x0000, "CameraSettingsVersion"), (0x0100, "PreviewImageValid"), (0x0101, "PreviewImageStart"),
        (0x0102, "PreviewImageLength"), (0x0200, "ExposureMode"), (0x0201, "AELock"),
        (0x0202, "MeteringMode"), (0x0203, "ExposureShift"), (0x0300, "MacroMode"),
        (0x0301, "FocusMode"), (0x0302, "FocusProcess"), (0x0303, "AFSearch"),
        (0x0304, "AFAreas"), (0x0305, "AFPointSelected"), (0x0307, "AFFineTuneAdj"),
        (0x0400, "FlashMode"), (0x0401, "FlashExposureComp"), (0x0403, "FlashRemoteControl"),
        (0x0404, "FlashControlMode"), (0x0405, "FlashIntensity"), (0x0406, "ManualFlashStrength"),
        (0x0500, "WhiteBalance"), (0x0501, "WhiteBalanceTemperature"), (0x0502, "WhiteBalanceBracket"),
        (0x0503, "CustomSaturation"), (0x0504, "ModifiedSaturation"), (0x0505, "ContrastSetting"),
        (0x0506, "SharpnessSetting"), (0x0507, "ColorSpace"), (0x0509, "SceneMode"),
        (0x050A, "NoiseReduction"), (0x050B, "DistortionCorrection"), (0x050C, "ShadingCompensation"),
        (0x050D, "CompressionFactor"), (0x050F, "Gradation"), (0x0520, "PictureMode"),
        (0x0521, "PictureModeSaturation"), (0x0522, "PictureModeHue"), (0x0523, "PictureModeContrast"),
        (0x0524, "PictureModeSharpness"), (0x0525, "PictureModeBWFilter"), (0x0526, "PictureModeTone"),
        (0x0527, "NoiseFilter"), (0x0529, "ArtFilter"), (0x052C, "MagicFilter"),
        (0x0600, "DriveMode"), (0x0601, "PanoramaMode"), (0x0603, "Quality"),
        (0x0604, "ImageStabilization"), (0x0900, "ManometerPressure"), (0x0901, "ManometerReading"),
        (0x0902, "ExtendedWBDetect"), (0x0903, "LevelGaugeRoll"), (0x0904, "LevelGaugePitch"),
    ];

    internal static string? Eq(ushort tag) => MakerNoteReader.Lookup(EqNames, tag);

    internal static readonly (ushort Id, string Name)[] EqNames =
    [
        (0x0000, "EquipmentVersion"), (0x0100, "CameraType"), (0x0101, "SerialNumber"),
        (0x0102, "InternalSerialNumber"), (0x0103, "FocalPlaneDiagonal"), (0x0104, "BodyFirmwareVersion"),
        (0x0201, "LensType"), (0x0202, "LensSerialNumber"), (0x0203, "LensModel"),
        (0x0204, "LensFirmwareVersion"), (0x0205, "MaxApertureAtMinFocal"), (0x0206, "MaxApertureAtMaxFocal"),
        (0x0207, "MinFocalLength"), (0x0208, "MaxFocalLength"), (0x020A, "MaxApertureAtCurrentFocal"),
        (0x020B, "LensProperties"), (0x0301, "Extender"), (0x0302, "ExtenderSerialNumber"),
        (0x0303, "ExtenderModel"), (0x0304, "ExtenderFirmwareVersion"), (0x0403, "ConversionLens"),
        (0x1000, "FlashType"), (0x1001, "FlashModel"), (0x1002, "FlashFirmwareVersion"),
        (0x1003, "FlashSerialNumber"),
    ];

    internal static string? Rd(ushort tag) => MakerNoteReader.Lookup(RdNames, tag);

    internal static readonly (ushort Id, string Name)[] RdNames =
    [
        (0x0000, "RawDevVersion"), (0x0100, "ExposureBiasValue"), (0x0101, "WhiteBalanceValue"),
        (0x0102, "WBFineAdjustment"), (0x0103, "GrayPoint"), (0x0104, "SaturationEmphasis"),
        (0x0105, "MemoryColorEmphasis"), (0x0106, "ContrastValue"), (0x0107, "SharpnessValue"),
        (0x0108, "ColorSpace"), (0x0109, "Engine"), (0x010A, "NoiseReduction"),
        (0x010B, "EditStatus"), (0x010C, "Settings"),
    ];

    internal static string? Rd2(ushort tag) => MakerNoteReader.Lookup(Rd2Names, tag);

    internal static readonly (ushort Id, string Name)[] Rd2Names =
    [
        (0x0000, "RawDev2Version"), (0x0100, "ExposureBiasValue"), (0x0101, "WhiteBalance"),
        (0x0102, "WhiteBalanceValue"), (0x0103, "WBFineAdjustment"), (0x0104, "GrayPoint"),
        (0x0105, "ContrastValue"), (0x0106, "SharpnessValue"), (0x0107, "SaturationEmphasis"),
        (0x0108, "MemoryColorEmphasis"), (0x0109, "ColorSpace"), (0x010A, "NoiseReduction"),
        (0x010B, "Engine"), (0x010C, "PictureMode"), (0x010D, "PMSaturation"),
        (0x010E, "PMContrast"), (0x010F, "PMSharpness"), (0x0110, "PM_BWFilter"),
        (0x0111, "PMPictureTone"), (0x0112, "Gradation"), (0x0113, "Saturation"),
        (0x0119, "AutoGradation"), (0x0120, "PMNoiseFilter"),
    ];

    internal static string? Ip(ushort tag) => MakerNoteReader.Lookup(IpNames, tag);

    internal static readonly (ushort Id, string Name)[] IpNames =
    [
        (0x0000, "ImageProcessingVersion"), (0x0100, "WB_RBLevels"), (0x0102, "WB_RBLevels3000K"),
        (0x0103, "WB_RBLevels3300K"), (0x0104, "WB_RBLevels3600K"), (0x0105, "WB_RBLevels3900K"),
        (0x0106, "WB_RBLevels4000K"), (0x0107, "WB_RBLevels4300K"), (0x0108, "WB_RBLevels4500K"),
        (0x0109, "WB_RBLevels4800K"), (0x010A, "WB_RBLevels5300K"), (0x010B, "WB_RBLevels6000K"),
        (0x010C, "WB_RBLevels6600K"), (0x010D, "WB_RBLevels7500K"), (0x010E, "WB_RBLevelsCWB1"),
        (0x010F, "WB_RBLevelsCWB2"), (0x0110, "WB_RBLevelsCWB3"), (0x0111, "WB_RBLevelsCWB4"),
        (0x0113, "WB_GLevel3000K"), (0x0114, "WB_GLevel3300K"), (0x0115, "WB_GLevel3600K"),
        (0x0116, "WB_GLevel3900K"), (0x0117, "WB_GLevel4000K"), (0x0118, "WB_GLevel4300K"),
        (0x0119, "WB_GLevel4500K"), (0x011A, "WB_GLevel4800K"), (0x011B, "WB_GLevel5300K"),
        (0x011C, "WB_GLevel6000K"), (0x011D, "WB_GLevel6600K"), (0x011E, "WB_GLevel7500K"),
        (0x011F, "WB_GLevel"), (0x0200, "ColorMatrix"), (0x0300, "Enhancer"),
        (0x0301, "EnhancerValues"), (0x0310, "CoringFilter"), (0x0311, "CoringValues"),
        (0x0600, "BlackLevel"), (0x0610, "GainBase"), (0x0611, "ValidBits"),
        (0x0612, "CropLeft"), (0x0613, "CropTop"), (0x0614, "CropWidth"),
        (0x0615, "CropHeight"), (0x1010, "NoiseReduction"), (0x1011, "DistortionCorrection"),
        (0x1012, "ShadingCompensation"), (0x101C, "MultipleExposureMode"), (0x1112, "AspectRatio"),
        (0x1113, "AspectFrame"), (0x1200, "FaceDetect"), (0x1201, "FaceDetectArea"),
    ];

    internal static string? Fi(ushort tag) => MakerNoteReader.Lookup(FiNames, tag);

    internal static readonly (ushort Id, string Name)[] FiNames =
    [
        (0x0000, "FocusInfoVersion"), (0x0209, "AutoFocus"), (0x0210, "SceneDetect"),
        (0x0211, "SceneArea"), (0x0212, "SceneDetectData"), (0x0300, "ZoomStepCount"),
        (0x0301, "FocusStepCount"), (0x0303, "FocusStepInfinity"), (0x0304, "FocusStepNear"),
        (0x0305, "FocusDistance"), (0x0308, "AFPoint"), (0x1201, "ExternalFlash"),
        (0x1203, "ExternalFlashGuideNumber"), (0x1204, "ExternalFlashBounce"), (0x1205, "ExternalFlashZoom"),
        (0x1208, "InternalFlash"), (0x1209, "ManualFlash"), (0x1500, "SensorTemperature"),
        (0x1600, "ImageStabilization"),
    ];

    internal static string? Ri(ushort tag) => MakerNoteReader.Lookup(RiNames, tag);

    internal static readonly (ushort Id, string Name)[] RiNames =
    [
        (0x0000, "RawInfoVersion"), (0x0100, "WB_RBLevelsUsed"), (0x0110, "WB_RBLevelsAuto"),
        (0x0120, "WB_RBLevelsShade"), (0x0121, "WB_RBLevelsCloudy"), (0x0122, "WB_RBLevelsFineWeather"),
        (0x0123, "WB_RBLevelsTungsten"), (0x0124, "WB_RBLevelsEveningSunlight"), (0x0130, "WB_RBLevelsDaylightFluor"),
        (0x0131, "WB_RBLevelsDayWhiteFluor"), (0x0132, "WB_RBLevelsCoolWhiteFluor"), (0x0133, "WB_RBLevelsWhiteFluorescent"),
        (0x0200, "ColorMatrix2"), (0x0310, "CoringFilter"), (0x0311, "CoringValues"),
        (0x0600, "BlackLevel2"), (0x0601, "YCbCrCoefficients"), (0x0611, "ValidPixelDepth"),
        (0x0612, "CropLeft"), (0x0613, "CropTop"), (0x0614, "CropWidth"),
        (0x0615, "CropHeight"), (0x1000, "LightSource"), (0x1001, "WhiteBalanceComp"),
        (0x1010, "SaturationSetting"), (0x1011, "HueSetting"), (0x1012, "ContrastSetting"),
        (0x1013, "SharpnessSetting"), (0x2000, "CMExposureCompensation"), (0x2001, "CMWhiteBalance"),
        (0x2002, "CMWhiteBalanceComp"), (0x2010, "CMWhiteBalanceGrayPoint"), (0x2020, "CMSaturation"),
        (0x2021, "CMHue"), (0x2022, "CMContrast"), (0x2023, "CMSharpness"),
    ];
}
