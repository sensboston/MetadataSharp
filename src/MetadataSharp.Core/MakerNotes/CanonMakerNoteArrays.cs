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
        if (value.FieldType is not (TiffFieldType.Short or TiffFieldType.SShort) || Find(tag) is not Layout layout)
            return [];
        ReadOnlySpan<byte> raw = value.RawBytes;
        int elements = raw.Length / 2;
        var fields = new List<MakerNoteField>(layout.Fields.Length);
        int consumedUntil = 0;
        foreach (Field field in layout.Fields)
        {
            if (field.Index < consumedUntil)
                continue;
            TiffFieldType type = field.Type == 0 ? layout.ElementType : field.Type;
            int width = ExifValue.GetWidth(type);
            int start = field.Index * 2;
            int length = width * (int)field.Count;
            if (start + length > elements * 2)
                break; // Fields are ascending, so nothing later fits either.
            fields.Add(new MakerNoteField(field.Index, field.Name,
                ExifValue.FromRaw(type, field.Count, value.ByteOrder, raw.Slice(start, length))));
            consumedUntil = field.Index + (length + 1) / 2;
        }
        return fields;
    }

    private static Layout? Find(ushort tag) => tag switch
    {
        0x0001 => CameraSettings,
        0x0004 => ShotInfo,
        0x0005 => Panorama,
        0x000F => CustomFunctions,
        0x0012 => PictureInfo,
        0x0093 => FileInfo,
        0x00A0 => ProcessingInfo,
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
        foreach (ushort tag in new ushort[] { 0x0001, 0x0004, 0x0005, 0x000F, 0x0012, 0x0093, 0x00A0 })
            yield return (tag, Find(tag)!.Fields.Select(f => f.Index).ToArray());
    }
}
