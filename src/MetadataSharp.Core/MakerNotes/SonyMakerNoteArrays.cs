namespace MetadataSharp.Core.MakerNotes;

/// <summary>
/// Sony array tags split into fields at fixed byte offsets. Most of them are stored enciphered with a
/// byte substitution: a stored byte <c>b</c> below 249 is <c>p³ mod 249</c> of the plain byte <c>p</c>,
/// and bytes 249-255 are stored as they are. Which layout applies depends on the camera model or on
/// the tag's own leading bytes; anything else is not split.
/// </summary>
internal static class SonyMakerNoteArrays
{
    // Describe one Sony array element, including its stored range and nested decoding rules.
    private readonly record struct Field(ushort Offset, string Name, TiffFieldType Type = TiffFieldType.Byte, uint Count = 1);

    /// <summary>A layout; a null byte order means the MakerNote's.</summary>
    private sealed record Layout(Field[] Fields, Endianness? Order, bool Enciphered);

    private static readonly byte[] Decipher = BuildDecipher();

    // Construct the inverse permutation for p³ mod 249, leaving the reserved high byte values unchanged.
    private static byte[] BuildDecipher()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
            table[i] = (byte)i;
        for (int p = 0; p < 249; p++)
            table[p * p * p % 249] = (byte)p;
        return table;
    }

    // Apply the inverse substitution to a copy so callers never mutate the packet-owned bytes.
    internal static byte[] Plain(ReadOnlySpan<byte> stored)
    {
        byte[] plain = new byte[stored.Length];
        for (int i = 0; i < plain.Length; i++)
            plain[i] = Decipher[stored[i]];
        return plain;
    }

    private static readonly byte[] Encipher = BuildEncipher();

    private static byte[] BuildEncipher()
    {
        // The substitution is a permutation of the byte values, so it has an inverse.
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
            table[Decipher[i]] = (byte)i;
        return table;
    }

    /// <summary>Plain bytes as they are stored: the inverse of <see cref="Plain"/>.</summary>
    internal static byte[] Stored(ReadOnlySpan<byte> plain)
    {
        byte[] stored = new byte[plain.Length];
        for (int i = 0; i < stored.Length; i++)
            stored[i] = Encipher[plain[i]];
        return stored;
    }

    /// <summary>True when the array of this tag is split from deciphered bytes.</summary>
    internal static bool IsEnciphered(ushort tag, ExifValue value, ReadOnlySpan<byte> model) =>
        value.FieldType is TiffFieldType.Undefined or TiffFieldType.Byte && Select(tag, value.RawBytes, model) is { Enciphered: true };

    // Split only recognized byte arrays, honoring each layout’s byte order and stopping at truncated fields.
    internal static IReadOnlyList<MakerNoteField> Decompose(ushort tag, ExifValue value, ReadOnlySpan<byte> model)
    {
        if (value.FieldType is not (TiffFieldType.Undefined or TiffFieldType.Byte) || Select(tag, value.RawBytes, model) is not Layout layout)
            return [];
        byte[] raw = layout.Enciphered ? Plain(value.RawBytes) : value.RawBytes.ToArray();
        Endianness order = layout.Order ?? value.ByteOrder;
        var fields = new List<MakerNoteField>(layout.Fields.Length);
        foreach (Field field in layout.Fields)
        {
            int length = ExifValue.GetWidth(field.Type) * (int)field.Count;
            if (field.Offset + length > raw.Length)
                break;
            fields.Add(new MakerNoteField(field.Offset, field.Name,
                ExifValue.FromRaw(field.Type, field.Count, order, raw.AsSpan(field.Offset, length)), null, field.Offset));
        }
        return fields;
    }

    private static Layout? Select(ushort tag, ReadOnlySpan<byte> stored, ReadOnlySpan<byte> model) => tag switch
    {
        // The camera settings come in several lengths, one per camera generation; this is the 280-byte one.
        0x0114 when stored.Length == 280 => CameraSettings,
        0x2010 when Is2010eModel(model) => Info2010e,
        0x3000 => ShotInfo,
        // Misc3c is chosen by its first byte as stored (not deciphered).
        // Misc3c is chosen by its first byte as stored, Misc2b by its deciphered bytes.
        0x9400 when stored.Length >= 1 && stored[0] is 35 or 36 or 38 or 40 or 49 or 50 or 51 or 52 or 65 or 67 => Misc3c,
        0x9402 when !model.StartsWith("SLT-"u8) && !model.StartsWith("HV"u8) && !model.StartsWith("ILCA-"u8) => FocusPosition,
        0x9403 => Misc1,
        0x9404 when stored.Length >= 4 && Decipher[stored[0]] is 9 or 12 or 13 or 15 or 16 && Decipher[stored[3]] == 2 => Misc2b,
        _ => null
    };

    // Match the explicit camera set that uses the large 2010e array layout; do not infer by model prefix.
    private static bool Is2010eModel(ReadOnlySpan<byte> model)
    {
        string name = System.Text.Encoding.ASCII.GetString(model).TrimEnd('\0', ' ');
        return name is "SLT-A58" or "SLT-A99" or "ILCE-3000" or "ILCE-3500" or "NEX-3N" or "NEX-5R" or "NEX-5T" or
            "NEX-6" or "VG30E" or "VG900" or "DSC-RX100" or "DSC-RX1" or "DSC-RX1R" or "DSC-HX300" or "DSC-HX50V" or
            "DSC-TX30" or "DSC-WX60" or "DSC-WX200" or "DSC-WX300";
    }

    private const TiffFieldType S = TiffFieldType.Short;
    private const TiffFieldType L = TiffFieldType.Long;

    /// <summary>Big-endian SHORTs, whatever the MakerNote's byte order; the offsets are in bytes.</summary>
    // This generation’s 280-byte array is explicitly big-endian regardless of the enclosing note.
    private static readonly Layout CameraSettings = new(
    [
        new(8, "DriveMode", S), new(12, "WhiteBalanceFineTune", TiffFieldType.SShort), new(32, "FocusMode", S),
        new(34, "AFAreaMode", S), new(36, "LocalAFAreaPoint", S), new(42, "MeteringMode", S),
        new(44, "ISOSetting", S), new(48, "DynamicRangeOptimizerMode", S),
        new(50, "DynamicRangeOptimizerLevel", S), new(52, "CreativeStyle", S), new(56, "Sharpness", S),
        new(58, "Contrast", S), new(60, "Saturation", S), new(62, "ZoneMatchingValue", S),
        new(68, "Brightness", S), new(70, "FlashMode", S), new(80, "PrioritySetupShutterRelease", S),
        new(82, "AFIlluminator", S), new(84, "AFWithShutter", S), new(86, "LongExposureNoiseReduction", S),
        new(88, "HighISONoiseReduction", S), new(90, "ImageStyle", S), new(120, "ExposureProgram", S),
        new(122, "ImageStabilization", S), new(126, "Rotation", S), new(168, "SonyImageSize", S),
        new(170, "AspectRatio", S), new(172, "Quality", S), new(176, "ExposureLevelIncrements", S),
    ], Endianness.BigEndian, Enciphered: false);

    private static readonly Layout ShotInfo = new(
    [
        new(6, "SonyDateTime", TiffFieldType.Ascii, 20), new(26, "SonyImageHeight", S), new(28, "SonyImageWidth", S),
        new(48, "FacesDetected", S), new(52, "MetaVersion", TiffFieldType.Ascii, 16),
    ], Endianness.LittleEndian, Enciphered: false);

    // Sony Misc3c offsets are decoded from the encrypted variant selected by the stored leading byte.
    private static readonly Layout Misc3c = new(
    [
        new(9, "ReleaseMode2"), new(10, "ShotNumberSincePowerUp", L), new(18, "SequenceImageNumber", L),
        new(22, "SequenceLength1"), new(26, "SequenceFileNumber", L), new(30, "SequenceLength2"),
        new(41, "CameraOrientation"), new(42, "Quality2"), new(71, "SonyImageHeight", S), new(83, "ModelReleaseYear"),
    ], Endianness.LittleEndian, Enciphered: true);

    private static readonly Layout FocusPosition = new(
    [
        new(4, "AmbientTemperature", TiffFieldType.SByte), new(22, "FocusMode"), new(23, "AFAreaMode"),
        new(45, "FocusPosition2"),
    ], Endianness.BigEndian, Enciphered: true);

    private static readonly Layout Misc1 = new(
        [new(5, "CameraTemperature", TiffFieldType.SByte)],
        Endianness.BigEndian, Enciphered: true);

    private static readonly Layout Misc2b = new(
    [
        new(12, "ExposureProgram"), new(14, "IntelligentAuto"), new(30, "LensZoomPosition", S), new(32, "FocusPosition2"),
    ], Endianness.LittleEndian, Enciphered: true);

    // The model-gated 2010e layout contains sparse fields at vendor-defined offsets in a large encrypted block.
    private static readonly Layout Info2010e = new(
    [
        new(0, "SequenceImageNumber", L), new(4, "SequenceFileNumber", L), new(8, "ReleaseMode2", L),
        new(540, "DigitalZoomRatio"), new(556, "SonyDateTime", TiffFieldType.Undefined), new(808, "DynamicRangeOptimizer"),
        new(1208, "MeterInfo", TiffFieldType.Undefined), new(4444, "ReleaseMode3"), new(4448, "ReleaseMode4"),
        new(4456, "SelfTimer"), new(4460, "FlashMode"), new(4466, "StopsAboveBaseISO", S), new(4468, "BrightnessValue", S),
        new(4472, "DynamicRangeOptimizer2"), new(4476, "HDRSetting"), new(4480, "ExposureCompensation", TiffFieldType.SShort),
        new(4502, "PictureProfile"), new(4503, "PictureProfile2"), new(4507, "PictureEffect2"), new(4520, "Quality2"),
        new(4524, "MeteringMode"), new(4525, "ExposureProgram"), new(4532, "WB_RGBLevels", S, 3), new(4692, "SonyISO", S),
        new(4696, "SonyISO2", S), new(4728, "FocalLength", S), new(4730, "MinFocalLength", S), new(4732, "MaxFocalLength", S),
        new(4736, "SonyISO3", S), new(6256, "DistortionCorrParams", TiffFieldType.SShort, 16), new(6289, "LensFormat"),
        new(6290, "LensMount"), new(6291, "LensType2", S), new(6294, "LensType", S),
        new(6296, "DistortionCorrParamsPresent"), new(6297, "DistortionCorrParamsNumber"),
    ], null, Enciphered: true);
}
