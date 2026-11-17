namespace MetadataSharp.Core.MakerNotes;

/// <summary>
/// The camera-settings arrays of Minolta and Konica Minolta MakerNotes, split into their named elements.
/// Each array is a run of equally wide integers, and it is big-endian whatever the byte order of the
/// MakerNote and whatever field type the directory gives it: the cameras store it as UNDEFINED bytes.
/// </summary>
internal static class MinoltaMakerNoteArrays
{
    /// <summary>One named element; its index counts elements, not bytes.</summary>
    private readonly record struct Element(ushort Index, string Name, bool Signed = false);

    internal static IReadOnlyList<MakerNoteField> Decompose(ushort tag, ExifValue value) => tag switch
    {
        // The old and the new standard settings differ in length, not in the elements they share.
        0x0001 or 0x0003 => Split(value, Standard, TiffFieldType.Long, TiffFieldType.SLong),
        0x0004 => Split(value, Settings7D, TiffFieldType.Short, TiffFieldType.SShort),
        0x0114 => Split(value, Settings5D, TiffFieldType.Short, TiffFieldType.SShort),
        _ => []
    };

    /// <summary>
    /// The camera settings (0x0114) of the Minolta directory inside a Sony DSLR-A100 MakerNote. The tag
    /// number is the 5D's; the elements are the A100's own.
    /// </summary>
    internal static IReadOnlyList<MakerNoteField> DecomposeSonyA100(ushort tag, ExifValue value) =>
        tag == 0x0114 ? Split(value, SettingsA100, TiffFieldType.Short, TiffFieldType.SShort) : [];

    private static IReadOnlyList<MakerNoteField> Split(ExifValue value, Element[] elements, TiffFieldType unsigned,
        TiffFieldType signed)
    {
        ReadOnlySpan<byte> raw = value.RawBytes;
        int width = ExifValue.GetWidth(unsigned);
        var fields = new List<MakerNoteField>(elements.Length);
        foreach (Element element in elements)
        {
            int at = element.Index * width;
            if (at + width > raw.Length)
                break; // The table is ascending: nothing later fits either.
            fields.Add(new MakerNoteField(element.Index, element.Name,
                ExifValue.FromRaw(element.Signed ? signed : unsigned, 1, Endianness.BigEndian, raw.Slice(at, width)), null, at));
        }
        return fields;
    }

    /// <summary>CameraSettingsStdOld (0x0001) and CameraSettingsStdNew (0x0003): LONGs.</summary>
    private static readonly Element[] Standard =
    [
        new(1, "ExposureMode"), new(2, "FlashMode"), new(3, "WhiteBalance"), new(4, "ImageSize"),
        new(5, "Quality"), new(6, "DriveMode"), new(7, "MeteringMode"), new(8, "ISO"), new(9, "ExposureTime"),
        new(10, "FNumber"), new(11, "MacroMode"), new(12, "DigitalZoom"), new(13, "ExposureCompensation"),
        new(14, "BracketStep"), new(16, "IntervalLength"), new(17, "IntervalNumber"), new(18, "FocalLength"),
        new(19, "FocusDistance"), new(20, "FlashFired"), new(21, "MinoltaDate"), new(22, "MinoltaTime"),
        new(23, "MaxAperture"), new(26, "FileNumberMemory"), new(27, "LastFileNumber"),
        new(28, "ColorBalanceRed"), new(29, "ColorBalanceGreen"), new(30, "ColorBalanceBlue"),
        new(31, "Saturation"), new(32, "Contrast"), new(33, "Sharpness"), new(34, "SubjectProgram"),
        new(35, "FlashExposureComp"), new(36, "ISOSetting"), new(37, "MinoltaModel"), new(38, "IntervalMode"),
        new(39, "FolderName"), new(40, "ColorMode"), new(41, "ColorFilter"), new(42, "BWFilter"),
        new(43, "Internal Flash"), new(44, "Brightness"), new(45, "SpotFocusPointX"),
        new(46, "SpotFocusPointY"), new(47, "WideFocusZone"), new(48, "FocusMode"), new(49, "FocusArea"),
        new(50, "DECPosition"), new(51, "ColorProfile"), new(52, "DataImprint"), new(63, "FlashMetering"),
    ];

    /// <summary>CameraSettings7D (0x0004): SHORTs.</summary>
    private static readonly Element[] Settings7D =
    [
        new(0, "ExposureMode"), new(2, "ImageSize"), new(3, "Quality"), new(4, "WhiteBalance"),
        new(14, "FocusMode"), new(16, "AFPoints"), new(21, "FlashFired"), new(22, "FlashMode"),
        new(28, "ISOSpeed"), new(30, "ExposureCompensation", Signed: true), new(37, "ColorSpace"),
        new(38, "Sharpness"), new(39, "Contrast"), new(40, "Saturation"), new(45, "FreeMemoryCardImages"),
        new(63, "ColorTemperature", Signed: true), new(64, "Hue"), new(70, "Rotation"), new(71, "FNumber"),
        new(72, "ExposureTime"), new(74, "FreeMemoryCardImages2"), new(94, "ImageNumber"),
        new(96, "NoiseReduction"), new(98, "ImageNumber2"), new(113, "ImageStabilization"),
        new(117, "ZoneMatchingOn"),
    ];

    /// <summary>CameraSettings5D (0x0114): SHORTs. Two elements carry a colour temperature, and the reference
    /// tool tells them apart only by a space in the second name; the names are kept as it prints them.</summary>
    private static readonly Element[] Settings5D =
    [
        new(10, "ExposureMode"), new(12, "ImageSize"), new(13, "Quality"), new(14, "WhiteBalance"),
        new(26, "FocusPosition"), new(27, "FocusArea"), new(31, "FlashFired"), new(37, "MeteringMode"),
        new(38, "ISOSpeed"), new(47, "ColorSpace"), new(48, "Sharpness"), new(49, "Contrast"),
        new(50, "Saturation"), new(53, "ExposureTime"), new(54, "FNumber"), new(55, "FreeMemoryCardImages"),
        new(56, "ExposureRevision"), new(72, "FocusMode"), new(73, "ColorTemperature", Signed: true),
        new(80, "Rotation"), new(83, "ExposureCompensation"), new(84, "FreeMemoryCardImages2"),
        new(101, "Rotation2"), new(110, "Color Temperature"), new(113, "PictureFinish"),
        new(145, "ExposureManualBias"), new(158, "AFMode"), new(174, "ImageNumber"), new(176, "NoiseReduction"),
        new(189, "ImageStabilization"),
    ];

    /// <summary>The camera settings of the Sony DSLR-A100: SHORTs.</summary>
    private static readonly Element[] SettingsA100 =
    [
        new(0, "ExposureMode"), new(1, "ExposureCompensationSetting"), new(5, "HighSpeedSync"),
        new(6, "ManualExposureTime"), new(7, "ManualFNumber"), new(8, "ExposureTime"), new(9, "FNumber"),
        new(10, "DriveMode2"), new(11, "WhiteBalance"), new(12, "FocusMode"), new(13, "LocalAFAreaPoint"),
        new(14, "AFAreaMode"), new(15, "FlashMode"), new(16, "FlashExposureCompSetting"),
        new(18, "MeteringMode"), new(19, "ISOSetting"), new(20, "ZoneMatchingMode"),
        new(21, "DynamicRangeOptimizerMode"), new(22, "ColorMode"), new(23, "ColorSpace"),
        new(24, "Sharpness"), new(25, "Contrast"), new(26, "Saturation"), new(28, "FlashMetering"),
        new(29, "PrioritySetupShutterRelease"), new(30, "DriveMode"), new(31, "SelfTimerTime"),
        new(32, "ContinuousBracketing"), new(33, "SingleFrameBracketing"), new(34, "WhiteBalanceBracketing"),
        new(35, "WhiteBalanceSetting"), new(36, "PresetWhiteBalance"), new(37, "ColorTemperatureSetting"),
        new(38, "CustomWBSetting"), new(39, "DynamicRangeOptimizerSettings"), new(50, "FreeMemoryCardImages"),
        new(52, "CustomWBRedLevel"), new(53, "CustomWBGreenLevel"), new(54, "CustomWBBlueLevel"),
        new(55, "CustomWBError"), new(56, "WhiteBalanceFineTune", Signed: true), new(57, "ColorTemperature"),
        new(58, "ColorCompensationFilter", Signed: true), new(59, "SonyImageSize"), new(60, "Quality"),
        new(61, "InstantPlaybackTime"), new(62, "InstantPlaybackSetup"), new(63, "NoiseReduction"),
        new(64, "EyeStartAF"), new(65, "RedEyeReduction"), new(66, "FlashDefault"),
        new(67, "AutoBracketOrder"), new(68, "FocusHoldButton"), new(69, "AELButton"),
        new(70, "ControlDialSet"), new(71, "ExposureCompensationMode"), new(72, "AFAssist"),
        new(73, "CardShutterLock"), new(74, "LensShutterLock"), new(75, "AFAreaIllumination"),
        new(76, "MonitorDisplayOff"), new(77, "RecordDisplay"), new(78, "PlayDisplay"),
        new(80, "ExposureIndicator"), new(81, "AELExposureIndicator"),
        new(82, "ExposureBracketingIndicatorLast"), new(83, "MeteringOffScaleIndicator"),
        new(84, "FlashExposureIndicator"), new(85, "FlashExposureIndicatorNext"),
        new(86, "FlashExposureIndicatorLast"), new(87, "ImageStabilization"), new(88, "FocusModeSwitch"),
        new(89, "FlashType"), new(90, "Rotation"), new(94, "ColorTemperature2"),
        new(95, "ColorCompensationFilter2", Signed: true), new(96, "BatteryLevel"),
    ];
}
