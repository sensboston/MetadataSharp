namespace MetadataSharp.Core.MakerNotes;

/// <summary>
/// Canon array tags whose elements are separate fields: element <c>i</c> of a SHORT array sits at
/// byte <c>2 * i</c>. Unlisted elements are not reported. A field can span more than one element
/// (a LONG, or a run of SHORTs) and then consumes the elements it covers.
/// </summary>
internal static class CanonMakerNoteArrays
{
    private readonly record struct Field(ushort Index, string Name, TiffFieldType Type = 0, uint Count = 1);

    private sealed record Layout(TiffFieldType ElementType, Field[] Fields);

    internal static IReadOnlyList<MakerNoteField> Decompose(ushort tag, ExifValue value)
    {
        // Canon LensInfo is commonly stored as SLong[7], but Exiv2 exposes its first five raw bytes
        // as CanonLe.LensSerialNumber. Preserve that byte view rather than requiring a TIFF BYTE type.
        if (tag == 0x4019)
        {
            ReadOnlySpan<byte> lensInfo = value.RawBytes;
            if (lensInfo.Length < 5)
                return [];
            return [new MakerNoteField(0, "LensSerialNumber",
                ExifValue.FromRaw(TiffFieldType.Byte, 5, value.ByteOrder, lensInfo[..5]), null, 0)];
        }
        if (tag == 0x0026)
            return value.FieldType is TiffFieldType.Short or TiffFieldType.SShort ? AFInfo(value) : [];
        if (Find(tag) is not Layout layout)
            return [];
        // Element i of the array sits at i * the element width; the tag's own type must have that width.
        int element = ExifValue.GetWidth(layout.ElementType);
        if (value.FieldType is TiffFieldType.Ascii or TiffFieldType.Rational or TiffFieldType.SRational or
                TiffFieldType.Float or TiffFieldType.Double || ExifValue.GetWidth(value.FieldType) != element)
            return [];
        ReadOnlySpan<byte> raw = value.RawBytes;
        int elements = raw.Length / element;
        var fields = new List<MakerNoteField>(layout.Fields.Length);
        int consumedUntil = 0;
        foreach (Field field in layout.Fields)
        {
            if (field.Index < consumedUntil)
                continue;
            TiffFieldType type = field.Type == 0 ? layout.ElementType : field.Type;
            int width = ExifValue.GetWidth(type);
            int start = field.Index * element;
            int length = width * (int)field.Count;
            if (start + length > elements * element)
                break; // Fields are ascending, so nothing later fits either.
            fields.Add(new MakerNoteField(field.Index, field.Name,
                ExifValue.FromRaw(type, field.Count, value.ByteOrder, raw.Slice(start, length)), null, start));
            consumedUntil = field.Index + (length + element - 1) / element;
        }
        return fields;
    }

    /// <summary>
    /// AFInfo: eight SSHORT header fields, of which the third is the AF point count N; then N area
    /// widths, N heights, N X positions and N Y positions (SSHORT); then three SHORT bit masks of
    /// ceil(N / 16) elements each: points in focus, selected and unusable.
    /// </summary>
    private static IReadOnlyList<MakerNoteField> AFInfo(ExifValue value)
    {
        ReadOnlySpan<byte> raw = value.RawBytes;
        int elements = raw.Length / 2;
        if (elements < 8)
            return [];
        int n = value.ByteOrder == Endianness.LittleEndian
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw[4..])
            : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(raw[4..]);
        int masks = (n + 15) / 16;
        var fields = new List<MakerNoteField>(AFInfoFields.Length);
        int index = 0;
        foreach ((string name, TiffFieldType type, int kind) in AFInfoFields)
        {
            int count = kind switch { 0 => 1, 1 => n, _ => masks };
            if (count == 0 || index + count > elements)
                break;
            fields.Add(new MakerNoteField((ushort)index, name,
                ExifValue.FromRaw(type, (uint)count, value.ByteOrder, raw.Slice(index * 2, count * 2)), null, index * 2));
            index += count;
        }
        return fields;
    }

    /// <summary>Name, type, and count kind: 0 = one element, 1 = N elements, 2 = ceil(N / 16).</summary>
    private static readonly (string Name, TiffFieldType Type, int Kind)[] AFInfoFields =
    [
        ("AFInfoSize", TiffFieldType.SShort, 0), ("AFAreaMode", TiffFieldType.SShort, 0),
        ("AFNumPoints", TiffFieldType.SShort, 0), ("AFValidPoints", TiffFieldType.SShort, 0),
        ("AFCanonImageWidth", TiffFieldType.SShort, 0), ("AFCanonImageHeight", TiffFieldType.SShort, 0),
        ("AFImageWidth", TiffFieldType.SShort, 0), ("AFImageHeight", TiffFieldType.SShort, 0),
        ("AFAreaWidths", TiffFieldType.SShort, 1), ("AFAreaHeights", TiffFieldType.SShort, 1),
        ("AFXPositions", TiffFieldType.SShort, 1), ("AFYPositions", TiffFieldType.SShort, 1),
        ("AFPointsInFocus", TiffFieldType.Short, 2), ("AFPointsSelected", TiffFieldType.Short, 2),
        ("AFPointsUnusable", TiffFieldType.Short, 2),
    ];

    private static Layout? Find(ushort tag) => tag switch
    {
        0x0001 => CameraSettings,
        0x0004 => ShotInfo,
        0x0005 => Panorama,
        0x000F => CustomFunctions,
        0x0012 => PictureInfo,
        0x0093 => FileInfo,
        0x00A0 => ProcessingInfo,
        0x0035 => TimeInfo,
        0x4013 => AFMicroAdj,
        0x4016 => VignettingCorr2,
        0x4018 => LightingOpt,
        0x4020 => AmbienceInfo,
        0x4021 => MultiExp,
        0x4025 => HDRInfo,
        0x4028 => AFConfig,
        _ => null
    };

    private static readonly Layout CameraSettings = new(TiffFieldType.Short,
    [
        new(1, "Macro"), new(2, "Selftimer"), new(3, "Quality"), new(4, "FlashMode"), new(5, "DriveMode"),
        new(7, "FocusMode"), new(9, "RecordMode"), new(10, "ImageSize"), new(11, "EasyMode"),
        new(12, "DigitalZoom"), new(13, "Contrast"), new(14, "Saturation"), new(15, "Sharpness"),
        new(16, "ISOSpeed"), new(17, "MeteringMode"), new(18, "FocusType"), new(19, "AFPoint"),
        new(20, "ExposureProgram"), new(22, "LensType"), new(23, "Lens", TiffFieldType.Short, 3),
        new(24, "ShortFocal"), new(25, "FocalUnits"), new(26, "MaxAperture"), new(27, "MinAperture"),
        new(28, "FlashActivity"), new(29, "FlashDetails"), new(32, "FocusContinuous"), new(33, "AESetting"),
        new(34, "ImageStabilization"), new(35, "DisplayAperture"), new(36, "ZoomSourceWidth"),
        new(37, "ZoomTargetWidth"), new(39, "SpotMeteringMode"), new(40, "PhotoEffect"),
        new(41, "ManualFlashOutput"), new(42, "ColorTone"), new(46, "SRAWQuality"),
    ]);

    private static readonly Layout ShotInfo = new(TiffFieldType.Short,
    [
        new(1, "AutoISO"), new(2, "ISOSpeed"), new(3, "MeasuredEV"), new(4, "TargetAperture"),
        new(5, "TargetShutterSpeed"), new(6, "ExposureCompensation"), new(7, "WhiteBalance"),
        new(8, "SlowShutter"), new(9, "Sequence"), new(10, "OpticalZoomCode"), new(12, "CameraTemperature"),
        new(13, "FlashGuideNumber"), new(14, "AFPointUsed"), new(15, "FlashBias"),
        new(16, "AutoExposureBracketing"), new(17, "AEBBracketValue"), new(18, "ControlMode"),
        new(19, "SubjectDistance"), new(21, "ApertureValue"), new(22, "ShutterSpeedValue"),
        new(23, "MeasuredEV2"), new(24, "BulbDuration"), new(26, "CameraType"), new(27, "AutoRotate"),
        new(28, "NDFilter"), new(29, "SelfTimer2"), new(33, "FlashOutput"),
    ]);

    private static readonly Layout Panorama = new(TiffFieldType.Short,
    [
        new(2, "PanoramaFrame"), new(5, "PanoramaDirection"),
    ]);

    private static readonly Layout CustomFunctions = new(TiffFieldType.Short,
    [
        new(1, "NoiseReduction"), new(2, "ShutterAeLock"), new(3, "MirrorLockup"),
        new(4, "ExposureLevelIncrements"), new(5, "AFAssist"), new(6, "FlashSyncSpeedAv"),
        new(7, "AEBSequence"), new(8, "ShutterCurtainSync"), new(9, "LensAFStopButton"),
        new(10, "FillFlashAutoReduction"), new(11, "MenuButtonReturn"), new(12, "SetButtonFunction"),
        new(13, "SensorCleaning"), new(14, "SuperimposedDisplay"), new(15, "ShutterReleaseNoCFCard"),
    ]);

    private static readonly Layout PictureInfo = new(TiffFieldType.Short,
    [
        new(2, "ImageWidth"), new(3, "ImageHeight"), new(4, "ImageWidthAsShot"), new(5, "ImageHeightAsShot"),
        new(22, "AFPointsUsed"), new(26, "AFPointsUsed20D"),
    ]);

    private static readonly Layout FileInfo = new(TiffFieldType.SShort,
    [
        new(1, "FileNumber", TiffFieldType.Long), new(3, "BracketMode"), new(4, "BracketValue"),
        new(5, "BracketShotNumber"), new(6, "RawJpgQuality"), new(7, "RawJpgSize"), new(8, "NoiseReduction"),
        new(9, "WBBracketMode"), new(12, "WBBracketValueAB"), new(13, "WBBracketValueGM"),
        new(14, "FilterEffect"), new(15, "ToningEffect"), new(16, "MacroMagnification"),
        new(19, "LiveViewShooting"), new(20, "FocusDistanceUpper"), new(21, "FocusDistanceLower"),
        new(23, "ShutterMode"), new(25, "FlashExposureLock"), new(32, "AntiFlicker"),
        new(61, "RFLensType", TiffFieldType.Short),
    ]);

    private static readonly Layout TimeInfo = new(TiffFieldType.SLong,
        [new(1, "TimeZone"), new(2, "TimeZoneCity"), new(3, "DaylightSavings")]);

    private static readonly Layout AFMicroAdj = new(TiffFieldType.SLong,
        [new(1, "AFMicroAdjMode"), new(2, "AFMicroAdjValue")]);

    private static readonly Layout VignettingCorr2 = new(TiffFieldType.SLong,
        [new(5, "PeripheralLightingSetting"), new(6, "ChromaticAberrationSetting"), new(7, "DistortionCorrectionSetting")]);

    private static readonly Layout LightingOpt = new(TiffFieldType.SLong,
    [
        new(1, "PeripheralIlluminationCorr"), new(2, "AutoLightingOptimizer"), new(3, "HighlightTonePriority"),
        new(4, "LongExposureNoiseReduction"), new(5, "HighISONoiseReduction"), new(10, "DigitalLensOptimizer"),
        new(11, "DualPixelRaw"),
    ]);

    private static readonly Layout AmbienceInfo = new(TiffFieldType.SLong, [new(1, "AmbienceSelection")]);

    private static readonly Layout MultiExp = new(TiffFieldType.SLong,
        [new(1, "MultiExposure"), new(2, "MultiExposureControl"), new(3, "MultiExposureShots")]);

    private static readonly Layout HDRInfo = new(TiffFieldType.SLong, [new(1, "HDR"), new(2, "HDREffect")]);

    private static readonly Layout AFConfig = new(TiffFieldType.SLong,
    [
        new(1, "AFConfigTool"), new(2, "AFTrackingSensitivity"), new(3, "AFAccelDecelTracking"),
        new(4, "AFPointSwitching"), new(5, "AIServoFirstImage"), new(6, "AIServoSecondImage"),
        new(7, "USMLensElectronicMF"), new(8, "AFAssistBeam"), new(9, "OneShotAFRelease"),
        new(10, "AutoAFPointSelEOSiTRAF"), new(11, "LensDriveWhenAFImpossible"), new(12, "SelectAFAreaSelectionMode"),
        new(13, "AFAreaSelectionMethod"), new(14, "OrientationLinkedAF"), new(15, "ManualAFPointSelPattern"),
        new(16, "AFPointDisplayDuringFocus"), new(17, "VFDisplayIllumination"), new(18, "AFStatusViewfinder"),
        new(19, "InitialAFPointInServo"),
    ]);

    private static readonly Layout ProcessingInfo = new(TiffFieldType.SShort,
    [
        new(1, "ToneCurve"), new(2, "Sharpness"), new(3, "SharpnessFrequency"), new(4, "SensorRedLevel"),
        new(5, "SensorBlueLevel"), new(6, "WhiteBalanceRed"), new(7, "WhiteBalanceBlue"),
        new(8, "WhiteBalance"), new(9, "ColorTemperature"), new(10, "PictureStyle"), new(11, "DigitalGain"),
        new(12, "WBShiftAB"), new(13, "WBShiftGM"),
    ]);

    internal static IEnumerable<(ushort Tag, IReadOnlyList<ushort> Indexes)> LayoutsForTests()
    {
        foreach (ushort tag in new ushort[] { 0x0001, 0x0004, 0x0005, 0x000F, 0x0012, 0x0093, 0x00A0, 0x0035, 0x4013, 0x4016, 0x4018, 0x4020, 0x4021, 0x4025, 0x4028 })
            yield return (tag, Find(tag)!.Fields.Select(f => f.Index).ToArray());
    }
}
