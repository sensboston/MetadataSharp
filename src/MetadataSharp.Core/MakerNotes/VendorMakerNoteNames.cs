namespace MetadataSharp.Core.MakerNotes;

/// <summary>MakerNote tag names of further vendors, ascending by ID.</summary>
internal static class VendorMakerNoteNames
{
    internal static string? Fujifilm(ushort tag) => MakerNoteReader.Lookup(FujifilmNames, tag);

    internal static readonly (ushort Id, string Name)[] FujifilmNames =
    [
        (0x0000, "Version"), (0x0010, "SerialNumber"), (0x1000, "Quality"),
        (0x1001, "Sharpness"), (0x1002, "WhiteBalance"), (0x1003, "Color"),
        (0x1004, "Tone"), (0x1005, "ColorTemperature"), (0x1006, "Contrast"),
        (0x100A, "WhiteBalanceFineTune"), (0x100B, "NoiseReduction"), (0x100E, "HighIsoNoiseReduction"),
        (0x100F, "Clarity"), (0x1010, "FlashMode"), (0x1011, "FlashStrength"),
        (0x1020, "Macro"), (0x1021, "FocusMode"), (0x1022, "FocusArea"),
        (0x1023, "FocusPoint"), (0x102B, "FocusPrioritySetting"), (0x102D, "FocusSetting"),
        (0x102E, "ContinuousFocusSetting"), (0x1030, "SlowSync"), (0x1031, "PictureMode"),
        (0x1032, "ExposureCount"), (0x1033, "EXRAuto"), (0x1034, "EXRMode"),
        (0x1040, "ShadowTone"), (0x1041, "HighlightTone"), (0x1044, "DigitalZoom"),
        (0x1045, "LensModulationOptimizer"), (0x1047, "GrainEffectRoughness"), (0x1048, "ColorChromeEffect"),
        (0x1049, "MonochromaticColorWC"), (0x104B, "MonochromaticColorMG"), (0x104C, "GrainEffectSize"),
        (0x104D, "CropMode"), (0x104E, "ColorChromeFXBlue"), (0x1050, "ShutterType"),
        (0x1100, "Continuous"), (0x1101, "SequenceNumber"), (0x1103, "DriveSetting"),
        (0x1105, "PixelShiftShots"), (0x1153, "PanoramaAngle"), (0x1154, "PanoramaDirection"),
        (0x1201, "AdvancedFilter"), (0x1210, "FinePixColor"), (0x1300, "BlurWarning"),
        (0x1301, "FocusWarning"), (0x1302, "ExposureWarning"), (0x1400, "DynamicRange"),
        (0x1401, "FilmMode"), (0x1402, "DynamicRangeSetting"), (0x1403, "DevelopmentDynamicRange"),
        (0x1404, "MinFocalLength"), (0x1405, "MaxFocalLength"), (0x1406, "MaxApertureAtMinFocal"),
        (0x1407, "MaxApertureAtMaxFocal"), (0x140B, "AutoDynamicRange"), (0x1422, "ImageStabilization"),
        (0x1425, "SceneRecognition"), (0x1431, "Rating"), (0x1436, "ImageGeneration"),
        (0x1438, "ImageNumber"), (0x1443, "DRangePriority"), (0x1444, "DRangePriorityFixed"),
        (0x1445, "DRangePriorityAuto"), (0x4005, "FaceElementSelected"), (0x4100, "FacesDetected"),
        (0x4103, "FacePositions"), (0x4200, "NumberFaceElements"), (0x4201, "FaceElementTypes"),
        (0x4203, "FaceElementPositions"), (0x4282, "FaceRecInfo"), (0x8000, "FileSource"),
        (0x8002, "OrderNumber"), (0x8003, "FrameNumber"), (0xF000, "FujiIFD"),
        (0xF001, "RawImageFullWidth"), (0xF002, "RawImageFullHeight"), (0xF003, "BitsPerSample"),
        (0xF007, "StripOffsets"), (0xF008, "StripByteCounts"), (0xF00A, "BlackLevel"),
        (0xF00B, "GeometricDistortionParams"), (0xF00C, "WB_GRBLevelsStandard"), (0xF00D, "WB_GRBLevelsAuto"),
        (0xF00E, "WB_GRBLevels"), (0xF00F, "ChromaticAberrationParams"), (0xF010, "VignettingParams"),
    ];

    internal static string? Pentax(ushort tag) => MakerNoteReader.Lookup(PentaxNames, tag);

    internal static readonly (ushort Id, string Name)[] PentaxNames =
    [
        (0x0000, "Version"), (0x0001, "Mode"), (0x0002, "PreviewResolution"),
        (0x0003, "PreviewLength"), (0x0004, "PreviewOffset"), (0x0005, "ModelID"),
        (0x0006, "Date"), (0x0007, "Time"), (0x0008, "Quality"),
        (0x0009, "Size"), (0x000C, "Flash"), (0x000D, "Focus"),
        (0x000E, "AFPoint"), (0x000F, "AFPointInFocus"), (0x0012, "ExposureTime"),
        (0x0013, "FNumber"), (0x0014, "ISO"), (0x0016, "ExposureCompensation"),
        (0x0017, "MeteringMode"), (0x0018, "AutoBracketing"), (0x0019, "WhiteBalance"),
        (0x001A, "WhiteBalanceMode"), (0x001B, "BlueBalance"), (0x001C, "RedBalance"),
        (0x001D, "FocalLength"), (0x001E, "DigitalZoom"), (0x001F, "Saturation"),
        (0x0020, "Contrast"), (0x0021, "Sharpness"), (0x0022, "Location"),
        (0x0023, "Hometown"), (0x0024, "Destination"), (0x0025, "HometownDST"),
        (0x0026, "DestinationDST"), (0x0027, "DSPFirmwareVersion"), (0x0028, "CPUFirmwareVersion"),
        (0x0029, "FrameNumber"), (0x002D, "EffectiveLV"), (0x0032, "ImageProcessing"),
        (0x0033, "PictureMode"), (0x0034, "DriveMode"), (0x0037, "ColorSpace"),
        (0x0038, "ImageAreaOffset"), (0x0039, "RawImageSize"), (0x003E, "PreviewImageBorders"),
        (0x003F, "LensType"), (0x0040, "SensitivityAdjust"), (0x0041, "DigitalFilter"),
        (0x0047, "Temperature"), (0x0048, "AELock"), (0x0049, "NoiseReduction"),
        (0x004D, "FlashExposureCompensation"), (0x004F, "ImageTone"), (0x0050, "ColorTemperature"),
        (0x005C, "ShakeReduction"), (0x005D, "ShutterCount"), (0x0069, "DynamicRangeExpansion"),
        (0x0071, "HighISONoiseReduction"), (0x0072, "AFAdjustment"), (0x009E, "HDF"),
        (0x0200, "BlackPoint"), (0x0201, "WhitePoint"), (0x0205, "ShotInfo"),
        (0x0206, "AEInfo"), (0x0207, "LensInfo"), (0x0208, "FlashInfo"),
        (0x0209, "AEMeteringSegments"), (0x020A, "FlashADump"), (0x020B, "FlashBDump"),
        (0x020D, "WB_RGGBLevelsDaylight"), (0x020E, "WB_RGGBLevelsShade"), (0x020F, "WB_RGGBLevelsCloudy"),
        (0x0210, "WB_RGGBLevelsTungsten"), (0x0211, "WB_RGGBLevelsFluorescentD"), (0x0212, "WB_RGGBLevelsFluorescentN"),
        (0x0213, "WB_RGGBLevelsFluorescentW"), (0x0214, "WB_RGGBLevelsFlash"), (0x0215, "CameraInfo"),
        (0x0216, "BatteryInfo"), (0x021F, "AFInfo"), (0x0222, "ColorInfo"),
        (0x0229, "SerialNumber"),
    ];

    internal static string? Sony(ushort tag) => MakerNoteReader.Lookup(SonyNames, tag);

    internal static readonly (ushort Id, string Name)[] SonyNames =
    [
        (0x0102, "Quality"), (0x0104, "FlashExposureComp"), (0x0105, "Teleconverter"),
        (0x0112, "WhiteBalanceFineTune"), (0x0115, "WhiteBalance"), (0x1000, "MultiBurstMode"),
        (0x1001, "MultiBurstImageWidth"), (0x1002, "MultiBurstImageHeight"), (0x2001, "PreviewImage"),
        (0x2002, "Rating"), (0x2004, "Contrast"), (0x2005, "Saturation"),
        (0x2006, "Sharpness"), (0x2007, "Brightness"), (0x2008, "LongExposureNoiseReduction"),
        (0x2009, "HighISONoiseReduction"), (0x200A, "AutoHDR"), (0x200B, "MultiFrameNoiseReduction"),
        (0x200E, "PictureEffect"), (0x200F, "SoftSkinEffect"), (0x2011, "VignettingCorrection"),
        (0x2012, "LateralChromaticAberration"), (0x2013, "DistortionCorrectionSetting"), (0x2014, "WBShiftABGM"),
        (0x2016, "AutoPortraitFramed"), (0x2017, "FlashAction"), (0x201A, "ElectronicFrontCurtainShutter"),
        (0x201B, "FocusMode2"), (0x201C, "AFAreaModeSetting"), (0x201D, "FlexibleSpotPosition"),
        (0x201E, "AFPointSelected"), (0x2020, "AFPointsUsed"), (0x2021, "AFTracking"),
        (0x2022, "FocalPlaneAFPointsUsed"), (0x2023, "MultiFrameNREffect"), (0x2026, "WBShiftABGMPrecise"),
        (0x2027, "FocusLocation"), (0x2028, "VariableLowPassFilter"), (0x2029, "RAWFileType"),
        (0x202B, "PrioritySetInAWB"), (0x202C, "MeteringMode2"), (0x202D, "ExposureStandardAdjustment"),
        (0x202E, "Quality2"), (0x202F, "PixelShiftInfo"), (0x2031, "SerialNumber"),
        (0x2032, "Shadows"), (0x2033, "Highlights"), (0x2034, "Fade"),
        (0x2035, "SharpnessRange"), (0x2036, "Clarity"), (0x2037, "FocusFrameSize"),
        (0x2039, "JPEGHEIFSwitch"), (0xB000, "FileFormat"), (0xB001, "SonyModelID"),
        (0xB020, "CreativeStyle"), (0xB021, "ColorTemperature"), (0xB022, "ColorCompensationFilter"),
        (0xB023, "SceneMode"), (0xB024, "ZoneMatching"), (0xB025, "DynamicRangeOptimizer"),
        (0xB026, "ImageStabilization"), (0xB027, "LensID"), (0xB029, "ColorMode"),
        (0xB02A, "LensSpec"), (0xB02B, "FullImageSize"), (0xB02C, "PreviewImageSize"),
        (0xB040, "Macro"), (0xB041, "ExposureMode"), (0xB042, "FocusMode"),
        (0xB043, "AFMode"), (0xB044, "AFIlluminator"), (0xB047, "JPEGQuality"),
        (0xB048, "FlashLevel"), (0xB049, "ReleaseMode"), (0xB04A, "SequenceNumber"),
        (0xB04B, "AntiBlur"), (0xB04E, "FocusMode3"), (0xB04F, "DynamicRangeOptimizer2"),
        (0xB050, "HighISONoiseReduction2"), (0xB052, "IntelligentAuto"), (0xB054, "WhiteBalance2"),
    ];

    internal static string? Panasonic(ushort tag) => MakerNoteReader.Lookup(PanasonicNames, tag);

    internal static readonly (ushort Id, string Name)[] PanasonicNames =
    [
        (0x0001, "Quality"), (0x0002, "FirmwareVersion"), (0x0003, "WhiteBalance"),
        (0x0007, "FocusMode"), (0x000F, "AFMode"), (0x001A, "ImageStabilization"),
        (0x001C, "Macro"), (0x001F, "ShootingMode"), (0x0020, "Audio"),
        (0x0021, "DataDump"), (0x0023, "WhiteBalanceBias"), (0x0024, "FlashBias"),
        (0x0025, "InternalSerialNumber"), (0x0026, "ExifVersion"), (0x0028, "ColorEffect"),
        (0x0029, "TimeSincePowerOn"), (0x002A, "BurstMode"), (0x002B, "SequenceNumber"),
        (0x002C, "Contrast"), (0x002D, "NoiseReduction"), (0x002E, "SelfTimer"),
        (0x0030, "Rotation"), (0x0031, "AFAssistLamp"), (0x0032, "ColorMode"),
        (0x0033, "BabyAge1"), (0x0034, "OpticalZoomMode"), (0x0035, "ConversionLens"),
        (0x0036, "TravelDay"), (0x0039, "Contrast2"), (0x003A, "WorldTimeLocation"),
        (0x003B, "TextStamp1"), (0x003C, "ProgramISO"), (0x003D, "AdvancedSceneType"),
        (0x003E, "TextStamp2"), (0x003F, "FacesDetected"), (0x0040, "Saturation"),
        (0x0041, "Sharpness"), (0x0042, "FilmMode"), (0x0044, "ColorTempKelvin"),
        (0x0045, "BracketSettings"), (0x0046, "WBAdjustAB"), (0x0047, "WBAdjustGM"),
        (0x0048, "FlashCurtain"), (0x0049, "LongShutterNoiseReduction"), (0x004B, "ImageWidth"),
        (0x004C, "ImageHeight"), (0x004D, "AFPointPosition"), (0x004E, "FaceDetInfo"),
        (0x0051, "LensType"), (0x0052, "LensSerialNumber"), (0x0053, "AccessoryType"),
        (0x0054, "AccessorySerialNumber"), (0x0059, "Transform1"), (0x005D, "IntelligentExposure"),
        (0x0060, "LensFirmwareVersion"), (0x0061, "FaceRecInfo"), (0x0062, "FlashWarning"),
        (0x0065, "Title"), (0x0066, "BabyName"), (0x0067, "Location"),
        (0x0069, "Country"), (0x006B, "State"), (0x006D, "City"),
        (0x006F, "Landmark"), (0x0070, "IntelligentResolution"), (0x0077, "BurstSpeed"),
        (0x0079, "IntelligentDRange"), (0x007C, "ClearRetouch"), (0x0080, "City2"),
        (0x0086, "ManometerPressure"), (0x0089, "PhotoStyle"), (0x008A, "ShadingCompensation"),
        (0x008C, "AccelerometerZ"), (0x008D, "AccelerometerX"), (0x008E, "AccelerometerY"),
        (0x008F, "CameraOrientation"), (0x0090, "RollAngle"), (0x0091, "PitchAngle"),
        (0x0093, "SweepPanoramaDirection"), (0x0094, "PanoramaFieldOfView"), (0x0096, "TimerRecording"),
        (0x009D, "InternalNDFilter"), (0x009E, "HDR"), (0x009F, "ShutterType"),
        (0x00A3, "ClearRetouchValue"), (0x00AB, "TouchAE"), (0x0E00, "PrintIM"),
        (0x8000, "MakerNoteVersion"), (0x8001, "SceneMode"), (0x8004, "WBRedLevel"),
        (0x8005, "WBGreenLevel"), (0x8006, "WBBlueLevel"), (0x8007, "FlashFired"),
        (0x8008, "TextStamp3"), (0x8009, "TextStamp4"), (0x8010, "BabyAge2"),
        (0x8012, "Transform2"),
    ];

    /// <summary>Minolta and Konica Minolta.</summary>
    internal static string? Minolta(ushort tag) => MakerNoteReader.Lookup(MinoltaNames, tag);

    internal static readonly (ushort Id, string Name)[] MinoltaNames =
    [
        (0x0000, "Version"), (0x0001, "CameraSettingsStdOld"), (0x0003, "CameraSettingsStdNew"),
        (0x0004, "CameraSettings7D"), (0x0018, "ImageStabilizationData"), (0x0020, "WBInfoA100"),
        (0x0040, "CompressedImageSize"), (0x0081, "Thumbnail"), (0x0088, "ThumbnailOffset"),
        (0x0089, "ThumbnailLength"), (0x0100, "SceneMode"), (0x0101, "ColorMode"), (0x0102, "Quality"),
        (0x0104, "FlashExposureComp"), (0x0105, "Teleconverter"), (0x0107, "ImageStabilization"),
        (0x0109, "RawAndJpgRecording"), (0x010A, "ZoneMatching"), (0x010B, "ColorTemperature"),
        (0x010C, "LensID"), (0x0111, "ColorCompensationFilter"), (0x0112, "WhiteBalanceFineTune"),
        (0x0113, "ImageStabilizationA100"), (0x0114, "CameraSettings5D"), (0x0115, "WhiteBalance"),
        (0x0E00, "PrintIM"), (0x0F00, "CameraSettingsZ1"),
    ];

    /// <summary>Casio, the early layout.</summary>
    internal static string? Casio(ushort tag) => MakerNoteReader.Lookup(CasioNames, tag);

    internal static readonly (ushort Id, string Name)[] CasioNames =
    [
        (0x0001, "RecodingMode"), (0x0002, "Quality"), (0x0003, "FocusMode"), (0x0004, "FlashMode"),
        (0x0005, "FlashIntensity"), (0x0006, "ObjectDistance"), (0x0007, "WhiteBalance"),
        (0x000A, "DigitalZoom"), (0x000B, "Sharpness"), (0x000C, "Contrast"), (0x000D, "Saturation"),
        (0x0014, "ISO"), (0x0015, "FirmwareDate"), (0x0016, "Enhancement"), (0x0017, "ColorFilter"),
        (0x0018, "AFPoint"), (0x0019, "FlashIntensity2"), (0x0020, "CCDSensitivity"), (0x0E00, "PrintIM"),
    ];

    /// <summary>Casio, the layout behind the "QVC" signature.</summary>
    internal static string? Casio2(ushort tag) => MakerNoteReader.Lookup(Casio2Names, tag);

    internal static readonly (ushort Id, string Name)[] Casio2Names =
    [
        (0x0002, "PreviewImageSize"), (0x0003, "PreviewImageLength"), (0x0004, "PreviewImageStart"),
        (0x0008, "QualityMode"), (0x0009, "ImageSize"), (0x000D, "FocusMode"), (0x0014, "ISOSpeed"),
        (0x0019, "WhiteBalance"), (0x001D, "FocalLength"), (0x001F, "Saturation"), (0x0020, "Contrast"),
        (0x0021, "Sharpness"), (0x0E00, "PrintIM"), (0x2000, "PreviewImage"), (0x2001, "FirmwareDate"),
        (0x2011, "WhiteBalanceBias"), (0x2012, "WhiteBalance2"), (0x2021, "AFPointPosition"),
        (0x2022, "ObjectDistance"), (0x2034, "FlashDistance"), (0x2076, "SpecialEffectMode"),
        (0x2089, "FaceInfo"), (0x211C, "FacesDetected"), (0x3000, "RecordMode"), (0x3001, "ReleaseMode"),
        (0x3002, "Quality"), (0x3003, "FocusMode2"), (0x3006, "HometownCity"), (0x3007, "BestShotMode"),
        (0x3008, "AutoISO"), (0x3009, "AFMode"), (0x3011, "Sharpness2"), (0x3012, "Contrast2"),
        (0x3013, "Saturation2"), (0x3014, "ISO"), (0x3015, "ColorMode"), (0x3016, "Enhancement"),
        (0x3017, "ColorFilter"), (0x301B, "ArtMode"), (0x301C, "SequenceNumber"),
        (0x3020, "ImageStabilization"), (0x302A, "LightingMode"), (0x302B, "PortraitRefiner"),
        (0x3030, "SpecialEffectLevel"), (0x3031, "SpecialEffectSetting"), (0x3103, "DriveMode"),
        (0x310B, "ArtModeParameters"), (0x4001, "CaptureFrameRate"), (0x4003, "VideoQuality"),
    ];

    /// <summary>Samsung, the plain directory.</summary>
    internal static string? Samsung2(ushort tag) => MakerNoteReader.Lookup(Samsung2Names, tag);

    internal static readonly (ushort Id, string Name)[] Samsung2Names =
    [
        (0x0001, "Version"), (0x0021, "PictureWizard"), (0x0030, "LocalLocationName"), (0x0031, "LocationName"),
        (0x0035, "Preview"), (0x0043, "CameraTemperature"), (0xA001, "FirmwareName"), (0xA003, "LensType"),
        (0xA004, "LensFirmware"), (0xA010, "SensorAreas"), (0xA011, "ColorSpace"), (0xA012, "SmartRange"),
        (0xA013, "ExposureBiasValue"), (0xA014, "ISO"), (0xA018, "ExposureTime"), (0xA019, "FNumber"),
        (0xA01A, "FocalLengthIn35mmFormat"), (0xA020, "EncryptionKey"), (0xA021, "WB_RGGBLevelsUncorrected"),
        (0xA022, "WB_RGGBLevelsAuto"), (0xA023, "WB_RGGBLevelsIlluminator1"),
        (0xA024, "WB_RGGBLevelsIlluminator2"), (0xA028, "WB_RGGBLevelsBlack"), (0xA030, "ColorMatrix"),
        (0xA031, "ColorMatrixSRGB"), (0xA032, "ColorMatrixAdobeRGB"), (0xA040, "ToneCurve1"),
        (0xA041, "ToneCurve2"), (0xA042, "ToneCurve3"), (0xA043, "ToneCurve4"),
    ];

    /// <summary>Sigma and Foveon.</summary>
    internal static string? Sigma(ushort tag) => MakerNoteReader.Lookup(SigmaNames, tag);

    internal static readonly (ushort Id, string Name)[] SigmaNames =
    [
        (0x0002, "SerialNumber"), (0x0003, "DriveMode"), (0x0004, "ResolutionMode"), (0x0005, "AutofocusMode"),
        (0x0006, "FocusSetting"), (0x0007, "WhiteBalance"), (0x0008, "ExposureMode"), (0x0009, "MeteringMode"),
        (0x000A, "LensRange"), (0x000B, "ColorSpace"), (0x000C, "Exposure"), (0x000D, "Contrast"),
        (0x000E, "Shadow"), (0x000F, "Highlight"), (0x0010, "Saturation"), (0x0011, "Sharpness"),
        (0x0012, "FillLight"), (0x0014, "ColorAdjustment"), (0x0015, "AdjustmentMode"), (0x0016, "Quality"),
        (0x0017, "Firmware"), (0x0018, "Software"), (0x0019, "AutoBracket"),
    ];
}
