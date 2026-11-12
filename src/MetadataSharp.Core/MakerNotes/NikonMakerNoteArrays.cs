namespace MetadataSharp.Core.MakerNotes;

/// <summary>
/// Nikon3 array tags whose bytes are separate fields at fixed byte offsets. Several tags start with
/// a four-character version that selects the layout. Encrypted layouts (ShotInfo and ColorBalance
/// from version 02xx, LensData from version 0201) need the note's key. Unlisted bytes are not reported.
/// </summary>
internal static class NikonMakerNoteArrays
{
    private readonly record struct Field(ushort Offset, string Name, TiffFieldType Type = TiffFieldType.Byte, uint Count = 1);

    /// <summary>A layout; <paramref name="BigEndian"/> forces the field byte order regardless of the MakerNote's.</summary>
    private sealed record Layout(Field[] Fields, bool BigEndian = false);

    internal static IReadOnlyList<MakerNoteField> Decompose(ushort tag, ExifValue value)
    {
        if (value.FieldType is not (TiffFieldType.Undefined or TiffFieldType.Byte) || Select(tag, value.RawBytes) is not Layout layout)
            return [];
        return Split(layout, value.RawBytes, value.ByteOrder);
    }

    /// <summary>
    /// Splits an encrypted array tag after decrypting it with the camera's serial key and shutter
    /// count; without a serial key the array is taken to be stored in the clear. Unknown versions, and
    /// versions whose layout differs by camera, are not split.
    /// </summary>
    internal static IReadOnlyList<MakerNoteField> DecomposeEncrypted(ushort tag, ExifValue value, uint shutterCount, uint? serialKey)
    {
        if (value.FieldType is not (TiffFieldType.Undefined or TiffFieldType.Byte))
            return [];
        ReadOnlySpan<byte> raw = value.RawBytes;
        (Layout? layout, int start) = SelectEncrypted(tag, raw);
        if (layout is null || raw.Length <= start)
            return [];
        return serialKey is uint key
            ? Split(layout, NikonMakerNoteCipher.Decrypt(raw, start, shutterCount, key), value.ByteOrder)
            : Split(layout, raw, value.ByteOrder);
    }

    /// <summary>How many bytes at the start of an encrypted array are stored in the clear.</summary>
    internal static int EncryptedStart(ushort tag, ExifValue value) => SelectEncrypted(tag, value.RawBytes).Item2;

    private static bool Major02(ReadOnlySpan<byte> raw) => raw.Length >= 4 && raw[0] == (byte)'0' && raw[1] == (byte)'2';

    private static (Layout?, int) SelectEncrypted(ushort tag, ReadOnlySpan<byte> raw) => tag switch
    {
        0x0098 when Version(raw, "0201") || Version(raw, "0202") || Version(raw, "0203") => (LensData0101, 4),
        0x0098 when Version(raw, "0204") => (LensData0204, 4),
        0x0098 when Version(raw, "0800") || Version(raw, "0801") || Version(raw, "0802") => (LensData0800, 4),
        // Version 02xx ShotInfo, except the D80, D40 and D300 layouts.
        0x0091 when Major02(raw) && !Version(raw, "0208") && !Version(raw, "0209") && !Version(raw, "0210") => (ShotInfo02, 4),
        0x0097 when Version(raw, "0212") => (ColorBalance0212, 284),
        // Version 02xx ColorBalance, except the layouts of 0205, 0209, 0212 and 0214.
        0x0097 when Major02(raw) && !Version(raw, "0205") && !Version(raw, "0209") && !Version(raw, "0212") &&
            !Version(raw, "0214") => (ColorBalance02, 284),
        _ => (null, 0)
    };

    private static IReadOnlyList<MakerNoteField> Split(Layout layout, ReadOnlySpan<byte> raw, Endianness parentOrder)
    {
        Endianness order = layout.BigEndian ? Endianness.BigEndian : parentOrder;
        var fields = new List<MakerNoteField>(layout.Fields.Length);
        foreach (Field field in layout.Fields)
        {
            int length = ExifValue.GetWidth(field.Type) * (int)field.Count;
            if (field.Offset + length > raw.Length)
                break; // Fields are ascending: nothing later fits either.
            fields.Add(new MakerNoteField(field.Offset, field.Name,
                ExifValue.FromRaw(field.Type, field.Count, order, raw.Slice(field.Offset, length)), null, field.Offset));
        }
        return fields;
    }

    private static bool Version(ReadOnlySpan<byte> raw, string version) =>
        raw.Length >= 4 && raw[0] == version[0] && raw[1] == version[1] && raw[2] == version[2] && raw[3] == version[3];

    private static Layout? Select(ushort tag, ReadOnlySpan<byte> raw) => tag switch
    {
        0x001F => VRInfo,
        0x0023 => PictureControl,
        0x0024 => WorldTime,
        0x0025 => ISOInfo,
        0x0088 => AFInfo,
        // ShotInfo and ColorBalance of the first generation are stored in the clear.
        0x0091 when raw.Length >= 4 && raw[0] == (byte)'0' && raw[1] == (byte)'1' => ShotInfo01,
        0x0097 when Version(raw, "0103") => ColorBalance0103,
        0x0098 when Version(raw, "0100") => LensData0100,
        0x0098 when Version(raw, "0101") => LensData0101,
        0x00A8 when Version(raw, "0100") || Version(raw, "0101") => FlashInfo0100,
        0x00A8 when Version(raw, "0102") => FlashInfo0102,
        0x00A8 when Version(raw, "0103") || Version(raw, "0104") || Version(raw, "0105") => FlashInfo0103,
        0x00A8 when Version(raw, "0106") => FlashInfo0106,
        0x00A8 when Version(raw, "0107") || Version(raw, "0108") => FlashInfo0107,
        0x00B0 => MultiExposure,
        0x00B7 when Version(raw, "0100") && raw.Length == 30 => AFInfo2V0100,
        0x00B7 when Version(raw, "0101") && raw.Length == 84 => AFInfo2V0101,
        0x00B8 => FileInfo,
        0x00B9 => AFTune,
        _ => null
    };

    private const TiffFieldType U = TiffFieldType.Undefined;
    private const TiffFieldType S = TiffFieldType.Short;

    private static readonly Layout VRInfo = new([new(0, "Version", U, 4), new(4, "VibrationReduction")]);

    private static readonly Layout PictureControl = new(
    [
        new(0, "Version", U, 4), new(4, "Name", TiffFieldType.Ascii, 20), new(24, "Base", TiffFieldType.Ascii, 20),
        new(48, "Adjust"), new(49, "QuickAdjust"), new(50, "Sharpness"), new(51, "Contrast"), new(52, "Brightness"),
        new(53, "Saturation"), new(54, "HueAdjustment"), new(55, "FilterEffect"), new(56, "ToningEffect"),
        new(57, "ToningSaturation"),
    ]);

    private static readonly Layout WorldTime = new(
        [new(0, "Timezone", TiffFieldType.SShort), new(2, "DaylightSavings"), new(3, "DateDisplayFormat")]);

    private static readonly Layout ISOInfo = new(
        [new(0, "ISO"), new(4, "ISOExpansion", S), new(6, "ISO2"), new(10, "ISOExpansion2", S)], BigEndian: true);

    private static readonly Layout AFInfo = new([new(0, "AFAreaMode"), new(1, "AFPoint"), new(2, "AFPointsInFocus", S)]);

    private static readonly Layout LensData0100 = new(
    [
        new(0, "Version", U, 4), new(6, "LensIDNumber"), new(7, "LensFStops"), new(8, "MinFocalLength"),
        new(9, "MaxFocalLength"), new(10, "MaxApertureAtMinFocal"), new(11, "MaxApertureAtMaxFocal"), new(12, "MCUVersion"),
    ]);

    private static readonly Layout LensData0101 = new(
    [
        new(0, "Version", U, 4), new(4, "ExitPupilPosition"), new(5, "AFAperture"), new(8, "FocusPosition"),
        new(9, "FocusDistance"), new(10, "FocalLength"), new(11, "LensIDNumber"), new(12, "LensFStops"),
        new(13, "MinFocalLength"), new(14, "MaxFocalLength"), new(15, "MaxApertureAtMinFocal"),
        new(16, "MaxApertureAtMaxFocal"), new(17, "MCUVersion"), new(18, "EffectiveMaxAperture"),
    ]);

    private static readonly Layout LensData0204 = new(
    [
        new(0, "Version", U, 4), new(4, "ExitPupilPosition"), new(5, "AFAperture"), new(8, "FocusPosition"),
        new(10, "FocusDistance"), new(11, "FocalLength"), new(12, "LensIDNumber"), new(13, "LensFStops"),
        new(14, "MinFocalLength"), new(15, "MaxFocalLength"), new(16, "MaxApertureAtMinFocal"),
        new(17, "MaxApertureAtMaxFocal"), new(18, "MCUVersion"), new(19, "EffectiveMaxAperture"),
    ]);

    private static readonly Layout LensData0800 = new(
    [
        new(0, "Version", U, 4), new(4, "ExitPupilPosition"), new(5, "AFAperture"), new(9, "FocusPosition"),
        new(11, "FocusDistance"), new(12, "FocalLength"), new(13, "LensIDNumber"), new(14, "LensFStops"),
        new(15, "MinFocalLength"), new(16, "MaxFocalLength"), new(17, "MaxApertureAtMinFocal"),
        new(18, "MaxApertureAtMaxFocal"), new(19, "MCUVersion"), new(20, "EffectiveMaxAperture"),
        new(48, "LensID", S), new(54, "MaxAperture", S), new(56, "FNumber", S), new(60, "FocalLength2", S),
        new(78, "FocusDistance2", S),
    ]);

    private static readonly Layout ShotInfo02 = new(
    [
        new(0, "Version", U, 4), new(106, "ShutterCount1", TiffFieldType.Long), new(110, "DeletedImageCount", TiffFieldType.Long),
        new(117, "VibrationReduction"), new(130, "VibrationReduction1"), new(343, "ShutterCount2", U, 2),
        new(430, "VibrationReduction2"), new(598, "ISO"), new(630, "ShutterCount", TiffFieldType.Long),
    ], BigEndian: true);

    private static readonly Layout ShotInfo01 = new(
    [
        new(0, "Version", U, 4), new(106, "ShutterCount1", TiffFieldType.Long), new(110, "DeletedImageCount", TiffFieldType.Long),
        new(117, "VibrationReduction"), new(130, "VibrationReduction1"), new(343, "ShutterCount2", U, 2),
        new(430, "VibrationReduction2"),
    ], BigEndian: true);

    private static readonly Layout ColorBalance0103 = new([new(0, "Version", U, 4), new(20, "WB_RGBGLevels", S, 4)]);

    private static readonly Layout ColorBalance0212 = new([new(0, "Version", U, 4), new(294, "WB_GRBGLevels", S, 4)]);

    private static readonly Layout ColorBalance02 = new([new(0, "Version", U, 4), new(290, "WB_RGGBLevels", S, 4)]);

    private static readonly Layout FlashInfo0100 = new(
    [
        new(0, "Version", U, 4), new(4, "FlashSource"), new(6, "ExternalFlashFirmware", S), new(8, "ExternalFlashFlags"),
        new(11, "FlashFocalLength"), new(12, "RepeatingFlashRate"), new(13, "RepeatingFlashCount"),
        new(14, "FlashGNDistance"), new(15, "FlashGroupAControlMode"), new(16, "FlashGroupBControlMode"),
    ]);

    private static readonly Layout FlashInfo0102 = new(
    [
        new(0, "Version", U, 4), new(4, "FlashSource"), new(6, "ExternalFlashFirmware", S), new(8, "ExternalFlashFlags"),
        new(12, "FlashFocalLength"), new(13, "RepeatingFlashRate"), new(14, "RepeatingFlashCount"),
        new(15, "FlashGNDistance"),
    ]);

    private static readonly Layout FlashInfo0103 = new(
    [
        new(0, "Version", U, 4), new(4, "FlashSource"), new(6, "ExternalFlashFirmware", S), new(8, "ExternalFlashFlags"),
        new(12, "FlashFocalLength"), new(13, "RepeatingFlashRate"), new(14, "RepeatingFlashCount"),
        new(15, "FlashGNDistance"), new(16, "FlashColorFilter"),
    ]);

    private static readonly Layout FlashInfo0106 = new(
    [
        new(0, "Version", U, 4), new(4, "FlashSource"), new(6, "ExternalFlashFirmware", S), new(8, "ExternalFlashData1"),
        new(9, "ExternalFlashData2"), new(10, "FlashCompensationMaster"), new(12, "FlashFocalLength"),
        new(13, "RepeatingFlashRate"), new(14, "RepeatingFlashCount"), new(15, "FlashGNDistance"),
        new(16, "FlashColorFilter"), new(17, "FlashGroupAControlData"), new(18, "FlashGroupBCControlData"),
        new(19, "FlashCompensationGroupA"), new(20, "FlashCompensationGroupB"), new(21, "FlashCompensationGroupC"),
        new(28, "CameraExposureCompensation"), new(29, "CameraFlashCompensation"), new(39, "FlashMasterOutput"),
        new(40, "FlashGroupAOutput"), new(41, "FlashGroupBOutput"), new(42, "FlashGroupCOutput"),
    ], BigEndian: true);

    private static readonly Layout FlashInfo0107 = new(
    [
        new(0, "Version", U, 4), new(4, "FlashSource"), new(6, "ExternalFlashFirmware", S), new(8, "ExternalFlashData1"),
        new(9, "ExternalFlashData2"), new(10, "FlashCompensationMaster"), new(12, "FlashFocalLength"),
        new(13, "RepeatingFlashRate"), new(14, "RepeatingFlashCount"), new(15, "FlashGNDistance"),
        new(16, "FlashColorFilter"), new(17, "FlashGroupAControlData"), new(18, "FlashGroupBCControlData"),
        new(19, "FlashCompensationGroupA"), new(20, "FlashCompensationGroupB"), new(21, "FlashCompensationGroupC"),
        new(22, "ExternalFlashData3"), new(27, "CameraFlashOutput"), new(28, "CameraExposureCompensation"),
        new(29, "CameraFlashCompensation"), new(37, "ExternalFlashData4"), new(38, "FlashZoomHeadPosition"),
        new(39, "FlashMasterOutput"), new(40, "FlashGroupAOutput"), new(41, "FlashGroupBOutput"),
        new(42, "FlashGroupCOutput"), new(67, "WirelessFlashData"),
    ], BigEndian: true);

    private static readonly Layout MultiExposure = new(
    [
        new(0, "Version", U, 4), new(4, "MultiExposureMode", TiffFieldType.Long),
        new(8, "MultiExposureShots", TiffFieldType.Long), new(12, "MultiExposureAutoGain", TiffFieldType.Long),
    ]);

    private static readonly Layout AFInfo2V0100 = new(
    [
        new(0, "Version", U, 4), new(4, "ContrastDetectAF"), new(5, "AFAreaMode"), new(6, "PhaseDetectAF"),
        new(7, "PrimaryAFPoint"), new(8, "AFPointsUsed", TiffFieldType.Byte, 7), new(16, "AFImageWidth", S),
        new(18, "AFImageHeight", S), new(20, "AFAreaXPosition", S), new(22, "AFAreaYPosition", S),
        new(24, "AFAreaWidth", S), new(26, "AFAreaHeight", S), new(28, "ContrastDetectAFInFocus"),
    ]);

    private static readonly Layout AFInfo2V0101 = new(
    [
        new(0, "Version", U, 4), new(4, "ContrastDetectAF"), new(5, "AFAreaMode"), new(6, "PhaseDetectAF"),
        new(7, "PrimaryAFPoint"), new(8, "AFPointsUsed", TiffFieldType.Byte, 7), new(70, "AFImageWidth", S),
        new(72, "AFImageHeight", S), new(74, "AFAreaXPosition", S), new(76, "AFAreaYPosition", S),
        new(78, "AFAreaWidth", S), new(80, "AFAreaHeight", S), new(82, "ContrastDetectAFInFocus"),
    ]);

    private static readonly Layout FileInfo = new(
        [new(0, "Version", U, 4), new(6, "DirectoryNumber", S), new(8, "FileNumber", S)]);

    private static readonly Layout AFTune = new([new(0, "AFFineTune"), new(1, "AFFineTuneIndex"), new(2, "AFFineTuneAdj")]);
}
