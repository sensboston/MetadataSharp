using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using Xunit;

namespace MetadataSharp.Tests.Values;

/// <summary>
/// The interpreted form of a MakerNote value for the vendors whose checked fields have a vocabulary:
/// a short label for an enumerated number, a unit for a counted one, the stored number in parentheses where the
/// vocabulary has no label for it, and null wherever there is nothing to add to the raw value.
/// </summary>
public sealed class MakerNoteInterpretationTests
{
    private const Endianness Little = Endianness.LittleEndian;

    private static ExifValue Bytes(params byte[] bytes) => ExifValue.FromRaw(TiffFieldType.Byte, (uint)bytes.Length, Little, bytes);

    private static ExifValue Undefined(params byte[] bytes) =>
        ExifValue.FromRaw(TiffFieldType.Undefined, (uint)bytes.Length, Little, bytes);

    private static ExifValue Ascii(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
        return ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Little, bytes);
    }

    private static ExifValue Shorts(params int[] numbers)
    {
        var bytes = new byte[numbers.Length * 2];
        for (int i = 0; i < numbers.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), (ushort)numbers[i]);
        return ExifValue.FromRaw(TiffFieldType.Short, (uint)numbers.Length, Little, bytes);
    }

    private static ExifValue SignedShorts(params int[] numbers)
    {
        var bytes = new byte[numbers.Length * 2];
        for (int i = 0; i < numbers.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)numbers[i]);
        return ExifValue.FromRaw(TiffFieldType.SShort, (uint)numbers.Length, Little, bytes);
    }

    private static ExifValue Longs(params long[] numbers)
    {
        var bytes = new byte[numbers.Length * 4];
        for (int i = 0; i < numbers.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), (uint)numbers[i]);
        return ExifValue.FromRaw(TiffFieldType.Long, (uint)numbers.Length, Little, bytes);
    }

    private static ExifValue SignedLongs(params int[] numbers)
    {
        var bytes = new byte[numbers.Length * 4];
        for (int i = 0; i < numbers.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), numbers[i]);
        return ExifValue.FromRaw(TiffFieldType.SLong, (uint)numbers.Length, Little, bytes);
    }

    private static ExifValue SignedRational(int numerator, int denominator)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, numerator);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), denominator);
        return ExifValue.FromRaw(TiffFieldType.SRational, 1, Little, bytes);
    }

    private static ExifValue Rationals(params (uint Numerator, uint Denominator)[] numbers)
    {
        var bytes = new byte[numbers.Length * 8];
        for (int i = 0; i < numbers.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 8), numbers[i].Numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((i * 8) + 4), numbers[i].Denominator);
        }
        return ExifValue.FromRaw(TiffFieldType.Rational, (uint)numbers.Length, Little, bytes);
    }

    /// <summary>What a note of the format makes of a tag of its own directory with this name and value.</summary>
    private static string? Top(MakerNoteFormat format, string? name, ExifValue value, string? cameraModel = null)
    {
        var entry = new MakerNoteEntry(0x7001, name, value, []);
        var note = new MakerNote(format, Little, [entry]);
        return cameraModel is null ? note.Interpret(entry) : note.InterpretForModel(entry, cameraModel);
    }

    /// <summary>What a note of the format makes of a named field of the array tag.</summary>
    private static string? Field(MakerNoteFormat format, int tag, string name, ExifValue value, int index = 1,
        string? cameraModel = null)
    {
        var part = new MakerNoteField((ushort)index, name, value);
        var entry = new MakerNoteEntry((ushort)tag, "Array", Undefined(0), [part]);
        var note = new MakerNote(format, Little, [entry]);
        return cameraModel is null ? note.Interpret(entry, part) : note.InterpretFieldForModel(entry, part, cameraModel);
    }

    private static string? NikonFlashField(string version, string name, byte value, params (string Name, byte Value)[] siblings)
    {
        var fields = new List<MakerNoteField>
        {
            new(0, "Version", Undefined(Encoding.ASCII.GetBytes(version))),
        };
        foreach ((string siblingName, byte siblingValue) in siblings)
            fields.Add(new MakerNoteField((ushort)fields.Count, siblingName, Bytes(siblingValue)));
        if (!fields.Any(field => field.Name == name))
            fields.Add(new MakerNoteField((ushort)fields.Count, name, Bytes(value)));
        var entry = new MakerNoteEntry(0x00A8, "FlashInfo", Undefined(0), fields);
        var note = new MakerNote(MakerNoteFormat.Nikon3, Little, [entry]);
        return note.Interpret(entry, fields.Single(field => field.Name == name));
    }

    // ---- Canon -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0x0001, "Quality", 4, "RAW")]
    [InlineData(0x0001, "ISOSpeed", 16484, "100")]
    [InlineData(0x0001, "DriveMode", 5, "Continuous, high")]
    [InlineData(0x0001, "AESetting", 65535, "n/a")]
    [InlineData(0x0001, "AFPoint", 0x3003, "Center")]
    [InlineData(0x0001, "Quality", 99, "(99)")]            // no label: shown as stored
    [InlineData(0x0001, "Macro", 0, "(0)")]
    [InlineData(0x0004, "WhiteBalance", 9, "Manual Temperature (Kelvin)")]
    [InlineData(0x0004, "CameraType", 248, "EOS High-end")]
    [InlineData(0x0004, "SelfTimer2", 65535, "n/a")]
    [InlineData(0x0004, "SelfTimer2", 0, "Selftimer 2s")]
    [InlineData(0x0004, "SelfTimer2", 1, "Selftimer 10s")]
    public void ACanonArrayFieldReadsAsItsLabel(int tag, string name, int number, string expected) =>
        Assert.Equal(expected, Field(MakerNoteFormat.Canon, tag, name, Shorts(number)));

    [Theory]
    [InlineData(0x0093, "RawJpgSize", -1, "n/a")]
    [InlineData(0x0093, "RawJpgQuality", 0, "unkown")]
    [InlineData(0x0093, "RFLensType", 0, "n/a")]
    [InlineData(0x0093, "RFLensType", 274, "Canon RF 24-105mm F4-7.1 IS STM")]
    [InlineData(0x0093, "NoiseReduction", 4, "Auto")]
    [InlineData(0x0093, "FilterEffect", -1, "(-1)")]
    [InlineData(0x00A0, "PictureStyle", 131, "Landscape")]
    [InlineData(0x00A0, "WhiteBalance", 6, "Custom")]
    [InlineData(0x00A0, "WhiteBalance", -1, "(-1)")]
    public void ASignedCanonFieldReadsAsItsLabel(int tag, string name, int number, string expected) =>
        Assert.Equal(expected, Field(MakerNoteFormat.Canon, tag, name, SignedShorts(number)));

    [Theory]
    [InlineData(0x4018, "HighISONoiseReduction", 2, "Strong")]
    [InlineData(0x4028, "OneShotAFRelease", 1, "Release Priority")]
    [InlineData(0x0035, "TimeZoneCity", 32766, "(not set)")]
    [InlineData(0x4016, "DistortionCorrectionSetting", 1, "On")]
    public void ALongCanonFieldReadsAsItsLabel(int tag, string name, int number, string expected) =>
        Assert.Equal(expected, Field(MakerNoteFormat.Canon, tag, name, SignedLongs(number)));

    [Theory]
    [InlineData(0x0001, "Selftimer", 0, "Off")]
    [InlineData(0x0001, "Selftimer", 20, "2 s")]
    [InlineData(0x0001, "Selftimer", 25, "2.5 s")]
    [InlineData(0x0004, "CameraTemperature", 151, "23 \u00B0C")]
    [InlineData(0x0004, "CameraTemperature", 120, "-8 \u00B0C")]
    [InlineData(0x0004, "CameraTemperature", 0, "--")]
    [InlineData(0x0004, "SubjectDistance", 0, "0 m")]
    [InlineData(0x0004, "SubjectDistance", 3, "0.03 m")]
    [InlineData(0x0004, "SubjectDistance", 107, "1.07 m")]
    [InlineData(0x0004, "SubjectDistance", 150, "1.5 m")]
    [InlineData(0x0004, "SubjectDistance", 6553, "65.53 m")]
    [InlineData(0x0004, "SubjectDistance", 65535, "Infinite")]
    public void ACountedCanonFieldReadsWithItsUnit(int tag, string name, int number, string expected) =>
        Assert.Equal(expected, Field(MakerNoteFormat.Canon, tag, name, Shorts(number)));

    [Theory]
    [InlineData("FocusDistanceUpper", 37, "0.37 m")]
    [InlineData("FocusDistanceUpper", 0, "0.00 m")]
    [InlineData("FocusDistanceUpper", -1, "Infinite")]
    [InlineData("FocusDistanceLower", 8191, "81.91 m")]
    [InlineData("FocusDistanceLower", 302, "3.02 m")]
    [InlineData("FocusDistanceLower", -1, "(-1)")]
    public void ACanonFocusDistanceIsMetresWithTwoDecimals(string name, int number, string expected) =>
        Assert.Equal(expected, Field(MakerNoteFormat.Canon, 0x0093, name, SignedShorts(number)));

    [Theory]
    [InlineData(0, "Left to right")]
    [InlineData(1, "Right to left")]
    [InlineData(2, "Bottom to top")]
    [InlineData(3, "Top to bottom")]
    [InlineData(4, "2x2 matrix (Clockwise)")]
    public void CanonPanoramaDirectionUsesTheReferenceLabels(int value, string expected) =>
        Assert.Equal(expected, Field(MakerNoteFormat.Canon, 0x0005, "PanoramaDirection", Shorts(value), index: 5));

    [Fact]
    public void CanonShotInfoUsesExiv2Conversions()
    {
        Assert.Equal("100", Field(MakerNoteFormat.Canon, 0x0004, "AutoISO", Shorts(0), index: 1));
        Assert.Equal("100", Field(MakerNoteFormat.Canon, 0x0004, "ISOSpeed", Shorts(160), index: 2));
        Assert.Equal("5.00", Field(MakerNoteFormat.Canon, 0x0004, "MeasuredEV", Shorts(0), index: 3));
        Assert.Equal("0.00", Field(MakerNoteFormat.Canon, 0x0004, "MeasuredEV2", Shorts(48), index: 23));
        Assert.Equal("F1", Field(MakerNoteFormat.Canon, 0x0004, "TargetAperture", Shorts(0), index: 4));
        Assert.Equal("1 s", Field(MakerNoteFormat.Canon, 0x0004, "TargetShutterSpeed", Shorts(0), index: 5));
        Assert.Equal("--", Field(MakerNoteFormat.Canon, 0x0004, "FlashGuideNumber", Shorts(65535), index: 13));
        Assert.Equal("inf", Field(MakerNoteFormat.Canon, 0x0004, "ISOSpeed", Shorts(65535), index: 2));
        Assert.Equal("1 s", Field(MakerNoteFormat.Canon, 0x0004, "TargetShutterSpeed", Shorts(65535), index: 5));
        Assert.Equal("2075", Field(MakerNoteFormat.Canon, 0x0004, "BulbDuration", Shorts(20755), index: 24));
        Assert.Equal("3 focus points; center used",
            Field(MakerNoteFormat.Canon, 0x0004, "AFPointUsed", Shorts(0x3002), index: 14));
    }

    [Fact]
    public void CanonSettingsAndFileInfoUseReferenceFormatting()
    {
        Assert.Equal("35.0 - 80.0 mm", Field(MakerNoteFormat.Canon, 0x0001, "Lens", Shorts(80, 35, 1), index: 23));
        Assert.Equal("F2.8", Field(MakerNoteFormat.Canon, 0x0001, "MaxAperture", Shorts(96), index: 26));
        Assert.Equal("Internal flash, E-TTL",
            Field(MakerNoteFormat.Canon, 0x0001, "FlashDetails", Shorts(0x2008), index: 29));
        Assert.Equal("(0)", Field(MakerNoteFormat.Canon, 0x0093, "FileNumber", Longs(0), index: 1));
        Assert.Equal("Standard", Field(MakerNoteFormat.Canon, 0x00A0, "PictureStyle", Shorts(129, 129, 129), index: 5));
        Assert.Equal("Auto", Top(MakerNoteFormat.Canon, "PictureStyleUserDef", Shorts(135, 135, 135)));
        Assert.Equal("(16191)", Field(MakerNoteFormat.Canon, 0x4028, "SelectAFAreaSelectionMode", SignedLongs(16191), index: 12));
    }

    [Theory]
    [InlineData(4231518, "Canon EOS 30D", "100-5604")]
    [InlineData(2146202, "Canon EOS 20D", "766-6688")]
    public void CanonFileNumberUsesTheEnclosingModelLayout(uint raw, string modelName, string expected)
    {
        var field = new MakerNoteField(1, "FileNumber", Longs(raw));
        var entry = new MakerNoteEntry(0x0093, "CanonFileInfo", Undefined(0), [field]);
        var note = new MakerNote(MakerNoteFormat.Canon, Little, [entry]);
        Assert.Equal(expected, note.InterpretFieldForModel(entry, field, modelName));
    }

    [Fact]
    public void CanonD30SerialNumberUsesTheModelSpecificReferenceFormat()
    {
        var model = new MakerNoteEntry(0x0010, "ModelID", Longs(0x01140000), []);
        var serial = new MakerNoteEntry(0x000C, "SerialNumber", Longs(154213267), []);
        var note = new MakerNote(MakerNoteFormat.Canon, Little, [model, serial]);
        Assert.Equal("093107059", note.Interpret(serial));
    }

    [Fact]
    public void CanonFocalLengthUsesItsLensUnitSibling()
    {
        var lensField = new MakerNoteField(23, "Lens", Shorts(80, 35, 1));
        var settings = new MakerNoteEntry(0x0001, "CameraSettings", Undefined(0), [lensField]);
        var focal = new MakerNoteEntry(0x0002, "FocalLength", Shorts(0, 95, 0, 0), []);
        var note = new MakerNote(MakerNoteFormat.Canon, Little, [settings, focal]);
        Assert.Equal("95.0 mm", note.Interpret(focal));
    }

    [Fact]
    public void CanonPictureInfoBitmasksUseTheReferenceLabels()
    {
        Assert.Equal("bottom", Field(MakerNoteFormat.Canon, 0x0012, "AFPointsUsed", Shorts(4), index: 22));
        Assert.Equal("upper-right, center, lower-right",
            Field(MakerNoteFormat.Canon, 0x0012, "AFPointsUsed20D", Shorts(148), index: 26));
        Assert.Null(Field(MakerNoteFormat.Canon, 0x0012, "AFPointsUsed", Shorts(0), index: 22));
    }

    [Fact]
    public void CanonLensSerialNumberIsRenderedAsTwoHexDigitsPerByte() =>
        Assert.Equal("1c00000000", Field(MakerNoteFormat.Canon, 0x4019, "LensSerialNumber", Bytes(0x1C, 0, 0, 0, 0), index: 0));

    [Fact]
    public void CanonTagsOfTheNotesOwnDirectoryAreRead()
    {
        Assert.Equal("Adobe RGB", Top(MakerNoteFormat.Canon, "ColorSpace", Shorts(2)));
        Assert.Equal("EOS 6D", Top(MakerNoteFormat.Canon, "ModelID", Longs(0x80000302)));
        Assert.Equal("PowerShot S40", Top(MakerNoteFormat.Canon, "ModelID", Longs(0x01110000)));
        Assert.Equal("(7)", Top(MakerNoteFormat.Canon, "ModelID", Longs(7)));
        Assert.Equal("Format 2", Top(MakerNoteFormat.Canon, "SerialNumberFormat", Longs(0xA0000000)));
        Assert.Equal("100-0054", Top(MakerNoteFormat.Canon, "FileNumber", Longs(1000054)));
        Assert.Equal("889-8982", Top(MakerNoteFormat.Canon, "FileNumber", Longs(8898982)));
        Assert.Equal("(0)", Top(MakerNoteFormat.Canon, "FileNumber", Longs(0)));
    }

    [Fact]
    public void ACanonLensSerialNumberIsTheHexadecimalDigitsOfItsBytes()
    {
        Assert.Equal("1c00000000", Field(MakerNoteFormat.Canon, 0x4019, "LensSerialNumber", Bytes(0x1C, 0, 0, 0, 0)));
        Assert.Equal("3490009075", Field(MakerNoteFormat.Canon, 0x4019, "LensSerialNumber", Bytes(0x34, 0x90, 0, 0x90, 0x75)));
        Assert.Null(Field(MakerNoteFormat.Canon, 0x4019, "LensSerialNumber", Shorts(1, 2)));
    }

    [Fact]
    public void CanonAutofocusPointMasksListTheSetBits()
    {
        Assert.Equal("6,9", Field(MakerNoteFormat.Canon, 0x0026, "AFPointsInFocus", Shorts(576, 0)));
        Assert.Equal("40,41", Field(MakerNoteFormat.Canon, 0x0026, "AFPointsInFocus", Shorts(0, 0, 768)));
        Assert.Equal("24", Field(MakerNoteFormat.Canon, 0x0026, "AFPointsSelected", Shorts(0, 256, 0, 0)));
        Assert.Equal("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18",
            Field(MakerNoteFormat.Canon, 0x0026, "AFPointsSelected", Shorts(65535, 7)));
        Assert.Equal("(none)", Field(MakerNoteFormat.Canon, 0x0026, "AFPointsUnusable", Shorts(0, 0, 0, 0)));
        Assert.Equal("Zone AF", Field(MakerNoteFormat.Canon, 0x0026, "AFAreaMode", SignedShorts(7)));
        // The masks are SHORTs; anything else is not a mask.
        Assert.Null(Field(MakerNoteFormat.Canon, 0x0026, "AFPointsInFocus", Bytes(1, 2)));
    }

    // ---- Nikon -------------------------------------------------------------------------------------------

    [Fact]
    public void ANikonVersionIsTwoDigitsForEachPart()
    {
        Assert.Equal("2.11", Top(MakerNoteFormat.Nikon3, "Version", Undefined(0x30, 0x32, 0x31, 0x31)));
        Assert.Equal("1.00", Field(MakerNoteFormat.Nikon3, 0x001F, "Version", Undefined(0x30, 0x31, 0x30, 0x30)));
        Assert.Equal("8.01", Field(MakerNoteFormat.Nikon3, 0x0098, "Version", Undefined(0x30, 0x38, 0x30, 0x31)));
        Assert.Equal("12.34", Field(MakerNoteFormat.Nikon3, 0x0091, "Version", Undefined(0x31, 0x32, 0x33, 0x34)));
        // Not four digits: nothing to read.
        Assert.Null(Top(MakerNoteFormat.Nikon3, "Version", Undefined(0, 2, 0, 0)));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "Version", Undefined(0x30, 0x32, 0x31)));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "Version", Ascii("0211")));
        // The rule is Nikon's.
        Assert.Null(Top(MakerNoteFormat.Canon, "Version", Undefined(0x30, 0x32, 0x31, 0x31)));
    }

    [Theory]
    [InlineData("ActiveDLighting", 65535, "Auto")]
    [InlineData("NEFCompression", 3, "Lossless")]
    [InlineData("ShutterMode", 81, "Auto (Electronic Front Curtain)")]
    [InlineData("ShootingMode", 33, "Continuous, Auto ISO")]
    [InlineData("VignetteControl", 3, "Normal")]
    [InlineData("ColorSpace", 2, "Adobe RGB")]
    [InlineData("ColorSpace", 5, "(5)")]
    public void ANikonTagReadsAsItsLabel(string name, int number, string expected) =>
        Assert.Equal(expected, Top(MakerNoteFormat.Nikon3, name, Shorts(number)));

    [Theory]
    [InlineData(0x001F, "VibrationReduction", 1, "On")]
    [InlineData(0x001F, "VibrationReduction", 2, "Off")]
    [InlineData(0x001F, "VibrationReduction", 0, "(0)")]
    [InlineData(0x0023, "Contrast", 0, "Auto")]
    [InlineData(0x0023, "Contrast", 128, "Normal")]
    [InlineData(0x0023, "Contrast", 132, "4")]
    [InlineData(0x0023, "Contrast", 255, "n/a")]
    [InlineData(0x0023, "Brightness", 4, "-124")]
    [InlineData(0x0023, "Sharpness", 1, "User")]
    [InlineData(0x0023, "Sharpness", 133, "5")]
    [InlineData(0x0023, "FilterEffect", 128, "Off")]
    [InlineData(0x0023, "FilterEffect", 0, "(0)")]
    [InlineData(0x0024, "DateDisplayFormat", 2, "D/M/Y")]
    [InlineData(0x00A8, "FlashFocalLength", 27, "27 mm")]
    [InlineData(0x00A8, "FlashFocalLength", 0, "n/a")]
    [InlineData(0x00A8, "FlashFocalLength", 255, "n/a")]
    [InlineData(0x00A8, "RepeatingFlashRate", 100, "100 Hz")]
    [InlineData(0x00A8, "FlashGroupBCControlData", 0x66, "Manual, Manual")]
    [InlineData(0x00A8, "CameraExposureCompensation", 0, "0.0 EV")]
    [InlineData(0x00A8, "CameraExposureCompensation", 252, "+0.7 EV")]
    [InlineData(0x00A8, "CameraFlashCompensation", 2, "-0.3 EV")]
    [InlineData(0x00A8, "CameraFlashOutput", 6, "-1.0 EV")]
    [InlineData(0x00A8, "CameraFlashOutput", 238, "+3.0 EV")]
    [InlineData(0x00B7, "PhaseDetectAF", 7, "On (153-point)")]
    [InlineData(0x00B9, "AFFineTune", 2, "On")]
    public void ANikonArrayFieldReadsAsItsLabelOrWithItsUnit(int tag, string name, int number, string expected) =>
        Assert.Equal(expected, Field(MakerNoteFormat.Nikon3, tag, name, Bytes((byte)number)));

    [Theory]
    [InlineData(0, "UTC +00:00")]
    [InlineData(60, "UTC +01:00")]
    [InlineData(-300, "UTC -05:00")]
    [InlineData(345, "UTC +05:45")]
    [InlineData(-210, "UTC -03:30")]
    public void ANikonTimezoneIsAnOffsetFromUtc(int minutes, string expected) =>
        Assert.Equal(expected, Field(MakerNoteFormat.Nikon3, 0x0024, "Timezone", SignedShorts(minutes)));

    [Theory]
    [InlineData(0x00, "0.0 EV")]
    [InlineData(0x02, "+0.3 EV")]
    [InlineData(0x06, "+1.0 EV")]
    [InlineData(0xFE, "-0.3 EV")]
    [InlineData(0xFA, "-1.0 EV")]
    [InlineData(0xEE, "-3.0 EV")]
    [InlineData(0x80, "(128)")]
    public void NikonFlashCompUsesItsFirstByteAsAnEnumeration(int code, string expected) =>
        Assert.Equal(expected, Top(MakerNoteFormat.Nikon3, "FlashComp", Undefined((byte)code, 1, 6, 0)));

    [Fact]
    public void NikonFlashCompRequiresAByteLikeValue()
    {
        Assert.Null(Top(MakerNoteFormat.Nikon3, "ProgramShift", Undefined()));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "ProgramShift", Shorts(6, 1, 6, 0)));
        Assert.Equal("+1.0 EV", Top(MakerNoteFormat.Nikon3, "ProgramShift", Undefined(6)));
    }

    [Fact]
    public void TheNikonShootingModeNamesTheLegacyCompositeValue()
    {
        Assert.Equal("Single-frame, Exposure bracketing, Unused LE-NR slowdown",
            Top(MakerNoteFormat.Nikon3, "ShootingMode", Shorts(48)));
    }

    [Fact]
    public void TheNikonExposureDifferenceUsesItsFirstByteMapping()
    {
        Assert.Equal("0.0 EV", Top(MakerNoteFormat.Nikon3, "ExposureDiff", Undefined(0, 1, 12, 0)));
        Assert.Equal("+0.3 EV", Top(MakerNoteFormat.Nikon3, "ExposureDiff", Undefined(2, 1, 12, 0)));
        Assert.Equal("(128)", Top(MakerNoteFormat.Nikon3, "ExposureDiff", Undefined(128, 1, 12, 0)));
    }

    [Theory]
    [InlineData("ColorMode", 1, "Color")]
    [InlineData("ColorMode", 2, "Monochrome")]
    [InlineData("ImageAdjustment", 3, "Contrast+")]
    [InlineData("ISOSpeed", 0, "80")]
    [InlineData("ISOSpeed", 5, "100")]
    [InlineData("WhiteBalance", 6, "Speedlight")]
    public void NikonTwoEnumsAreNamed(string name, int number, string expected) =>
        Assert.Equal(expected, Top(MakerNoteFormat.Nikon2, name, Shorts(number)));

    [Fact]
    public void NikonTwoUnknownQualityRemainsRaw()
    {
        Assert.Equal("(12)", Top(MakerNoteFormat.Nikon2, "Quality", Shorts(12)));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "Quality", Shorts(12)));
    }

    [Fact]
    public void NikonOneFocusAndDistanceFieldsAreRead()
    {
        Assert.Equal("Continuous autofocus", Top(MakerNoteFormat.Nikon1, "Focus", Ascii("AF-C  ")));
        Assert.Equal("Unknown", Top(MakerNoteFormat.Nikon1, "FocusDistance", Rationals((0, 1))));
        Assert.Equal("1.50 m", Top(MakerNoteFormat.Nikon1, "FocusDistance", Rationals((3, 2))));
        Assert.Equal("Not used", Top(MakerNoteFormat.Nikon1, "DigitalZoom", Rationals((0, 1))));
        Assert.Equal("1.0x", Top(MakerNoteFormat.Nikon1, "DigitalZoom", Rationals((1, 1))));
        Assert.Equal("Dynamic area; Center", Top(MakerNoteFormat.Nikon1, "AFFocusPos", Undefined(1, 0, 0, 0)));
    }

    [Fact]
    public void NikonThreeTopLevelValuesAndAutofocusFlagsAreInterpreted()
    {
        Assert.Equal("Unknown", Top(MakerNoteFormat.Nikon3, "FocusDistance", Rationals((0, 1))));
        Assert.Equal("1.0x", Top(MakerNoteFormat.Nikon3, "DigitalZoom", Rationals((1, 1))));
        Assert.Equal("7.8 x 7.8 um", Top(MakerNoteFormat.Nikon3, "SensorPixelSize", Rationals((78, 10), (78, 10))));
        Assert.Equal("Lower-right", Field(MakerNoteFormat.Nikon3, 0x0088, "AFPointsInFocus", Shorts(0x0100), 2));
        Assert.Equal("Lower-right", Field(MakerNoteFormat.Nikon3, 0x0088, "AFPointsInFocus", Shorts(1), 2, "NIKON D70"));
        Assert.Equal("Fired", Field(MakerNoteFormat.Nikon3, 0x00A8, "ExternalFlashFlags", Bytes(0), 8));
        Assert.Equal("Off", Field(MakerNoteFormat.Nikon3, 0x00A8, "FlashGroupAControlMode", Bytes(0), 15));
    }

    [Fact]
    public void NikonIsoInfoUsesItsThirdStopEncodingAndExpansionLabels()
    {
        Assert.Equal("100", Field(MakerNoteFormat.Nikon3, 0x0025, "ISO", Bytes(60)));
        Assert.Equal("12800", Field(MakerNoteFormat.Nikon3, 0x0025, "ISO2", Bytes(144)));
        Assert.Equal("3", Field(MakerNoteFormat.Nikon3, 0x0091, "ISO", Bytes(0), 598));
        Assert.Equal("Off", Field(MakerNoteFormat.Nikon3, 0x0025, "ISOExpansion", Shorts(0)));
        Assert.Equal("Hi 2.0", Field(MakerNoteFormat.Nikon3, 0x0025, "ISOExpansion2", Shorts(0x108)));
        Assert.Null(Field(MakerNoteFormat.Nikon3, 0x0025, "ISO", Shorts(60)));
    }

    [Fact]
    public void NikonFlashInfoUsesVersionAndSiblingControlModes()
    {
        Assert.Equal("Did not fire", NikonFlashField("0106", "ExternalFlashData1", 0));
        Assert.Equal("Fired, Bounce Flash adaptor, Wide Flash adaptor", NikonFlashField("0106", "ExternalFlashData1", 0x15));
        Assert.Equal("External flash off", NikonFlashField("0106", "ExternalFlashData2", 0));
        Assert.Equal("External flash on, Manual", NikonFlashField("0106", "ExternalFlashData2", 0x86));
        Assert.Equal("n/a", NikonFlashField("0106", "FlashCompensationMaster", 0, ("ExternalFlashData1", (byte)0)));
        Assert.Equal("1/1", NikonFlashField("0106", "FlashCompensationMaster", 0, ("ExternalFlashData1", (byte)6)));
        Assert.Equal("External flash off", NikonFlashField("0107", "ExternalFlashData1", 0));
        Assert.Equal("External flash on, External flash zoom override, Bounce Flash adaptor",
            NikonFlashField("0108", "ExternalFlashData1", 0x85));
        Assert.Equal("Manual", NikonFlashField("0107", "ExternalFlashData2", 6));
        Assert.Equal("Exposure Comp.: Background Only", NikonFlashField("0107", "ExternalFlashData3", 4));
    }

    [Fact]
    public void NikonLensDataThreeNumbersUseTheirEncodedUnits()
    {
        Assert.Equal("F2.0", Field(MakerNoteFormat.Nikon3, 0x0098, "AFAperture", Bytes(24)));
        Assert.Equal("n/a", Field(MakerNoteFormat.Nikon3, 0x0098, "FocusDistance", Bytes(0)));
        Assert.Equal("1.00 m", Field(MakerNoteFormat.Nikon3, 0x0098, "FocusDistance", Bytes(80)));
        Assert.Equal("10.0 mm", Field(MakerNoteFormat.Nikon3, 0x0098, "FocalLength", Bytes(24)));
        Assert.Equal("F5.0", Field(MakerNoteFormat.Nikon3, 0x0098, "LensFStops", Bytes(60)));
        Assert.Equal("32.0 mm", Field(MakerNoteFormat.Nikon3, 0x0098, "ExitPupilPosition", Bytes(64)));
        Assert.Null(Field(MakerNoteFormat.Nikon3, 0x0098, "FocalLength", Shorts(24)));
        Assert.Equal("6", Top(MakerNoteFormat.Nikon3, "LensFStops", Undefined(72, 1, 12, 0)));
    }

    [Fact]
    public void NikonLensDataFourAddsItsHighResolutionValues()
    {
        Assert.Equal("F2.0", Field(MakerNoteFormat.Nikon3, 0x0098, "FNumber", Shorts(768)));
        Assert.Equal("32.0 mm", Field(MakerNoteFormat.Nikon3, 0x0098, "ExitPupilPosition", Bytes(64)));
        Assert.Equal("(0)", Field(MakerNoteFormat.Nikon3, 0x0098, "ExitPupilPosition", Bytes(0)));
        Assert.Equal("(64)", Field(MakerNoteFormat.Nikon3, 0x0098, "ExitPupilPosition", Shorts(64)));
        Assert.Equal("50 mm", Field(MakerNoteFormat.Nikon3, 0x0098, "FocalLength2", Shorts(50)));
        Assert.Equal("1.00 m", Field(MakerNoteFormat.Nikon3, 0x0098, "FocusDistance2", Shorts(20480)));
        Assert.Equal("n/a", Field(MakerNoteFormat.Nikon3, 0x0098, "FocusDistance2", Shorts(0)));
    }

    [Fact]
    public void TheNikonSensitivityIsTheSecondOfItsTwoNumbers()
    {
        Assert.Equal("200", Top(MakerNoteFormat.Nikon3, "ISOSpeed", Shorts(0, 200)));
        Assert.Equal("6400", Top(MakerNoteFormat.Nikon3, "ISOSettings", Shorts(0, 6400)));
        Assert.Equal("0", Top(MakerNoteFormat.Nikon3, "ISOSpeed", Shorts(0, 0)));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "ISOSpeed", Shorts(200)));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "ISOSpeed", Bytes(0, 200)));
        // The early Coolpix layouts number their tags differently and are not read.
        Assert.Null(Top(MakerNoteFormat.Nikon2, "ISOSpeed", Shorts(0, 200)));
    }

    [Fact]
    public void ANikonRetouchHistoryWithNothingInItSaysSo()
    {
        Assert.Equal("None", Top(MakerNoteFormat.Nikon3, "RetouchHistory", Shorts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0)));
        // A retouch this release cannot name leaves the list unread rather than half read.
        Assert.Null(Top(MakerNoteFormat.Nikon3, "RetouchHistory", Shorts(0, 3, 0, 0, 0, 0, 0, 0, 0, 0)));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "RetouchHistory", Ascii("0")));
    }

    [Fact]
    public void ANikonLensIsItsFocalLengthsAndItsWidestApertures()
    {
        Assert.Equal("18-250mm F3.5-6.3",
            Top(MakerNoteFormat.Nikon3, "Lens", Rationals((180, 10), (2500, 10), (35, 10), (63, 10))));
        Assert.Equal("50mm F1.4", Top(MakerNoteFormat.Nikon3, "Lens", Rationals((500, 10), (500, 10), (14, 10), (14, 10))));
        Assert.Equal("70-200mm F2.8",
            Top(MakerNoteFormat.Nikon3, "Lens", Rationals((700, 10), (2000, 10), (280, 100), (280, 100))));
        Assert.Equal("10.5mm F2.8", Top(MakerNoteFormat.Nikon3, "Lens", Rationals((105, 10), (105, 10), (28, 10), (28, 10))));
        Assert.Equal("24-70mm F4", Top(MakerNoteFormat.Nikon3, "Lens", Rationals((240, 10), (700, 10), (400, 100), (400, 100))));
        // A number that is not a decimal with two places, or has no denominator, is not guessed at.
        Assert.Null(Top(MakerNoteFormat.Nikon3, "Lens", Rationals((180, 10), (2500, 10), (10, 3), (63, 10))));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "Lens", Rationals((180, 10), (2500, 10), (35, 0), (63, 10))));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "Lens", Rationals((180, 10), (2500, 10), (35, 10))));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "Lens", Longs(18, 250, 3, 6)));
    }

    [Fact]
    public void TheNikonPreviewDirectoryIsReadWithTheExifVocabulary()
    {
        ExifValue compression = Shorts(6), resolution = Rationals((300, 1)), unit = Shorts(2);
        string? expected = ExifTags.Interpret(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0103), compression);
        Assert.NotNull(expected);
        Assert.Equal(expected, Field(MakerNoteFormat.Nikon3, 0x0011, "Compression", compression, 0x0103));
        Assert.Equal("300", Field(MakerNoteFormat.Nikon3, 0x0011, "XResolution", resolution, 0x011A));
        Assert.Equal("inch", Field(MakerNoteFormat.Nikon3, 0x0011, "ResolutionUnit", unit, 0x0128));
        // The tag number decides, not the name; and only Nikon has this directory.
        Assert.Equal("inch", Field(MakerNoteFormat.Nikon3, 0x0011, "0x0128", unit, 0x0128));
        Assert.Null(Field(MakerNoteFormat.Nikon3, 0x0011, "ResolutionUnit", unit, 0xFFF0));
        Assert.Null(Field(MakerNoteFormat.Canon, 0x0011, "ResolutionUnit", unit, 0x0128));
    }

    [Fact]
    public void TheNikonFocusAbbreviationIsSpelledOut()
    {
        Assert.Equal("Single autofocus", Top(MakerNoteFormat.Nikon3, "Focus", Ascii("AF-S  ")));
        Assert.Equal("Continuous autofocus", Top(MakerNoteFormat.Nikon3, "Focus", Ascii("AF-C")));
        Assert.Equal("Automatic", Top(MakerNoteFormat.Nikon3, "Focus", Ascii("AF-A  ")));
        Assert.Equal("(MANUAL)", Top(MakerNoteFormat.Nikon3, "Focus", Ascii("MANUAL")));
        Assert.Null(Top(MakerNoteFormat.Nikon3, "Focus", Shorts(1)));
    }

    // ---- Sony --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(MakerNoteFormat.Sony1)]
    [InlineData(MakerNoteFormat.Sony2)]
    public void BothSonyLayoutsShareOneVocabulary(MakerNoteFormat format)
    {
        Assert.Equal("RAW + JPEG/HEIF", Top(format, "Quality", Longs(6)));
        Assert.Equal("Off", Top(format, "MultiBurstMode", Undefined(0)));
        Assert.Equal("On", Top(format, "MultiBurstMode", Undefined(1)));
        Assert.Equal("(1)", Top(format, "MultiBurstMode", Shorts(1)));
        Assert.Equal("Aperture-priority AE", Top(format, "ExposureMode", Shorts(7)));
        Assert.Equal("n/a", Top(format, "DistortionCorrectionSetting", Longs(0xFFFFFFFF)));
        Assert.Equal("-0.3 EV", Top(format, "FlashLevel", SignedShorts(-1)));
        Assert.Equal("ILCE-6000", Top(format, "SonyModelID", Shorts(312)));
        Assert.Equal("(9999)", Top(format, "SonyModelID", Shorts(9999)));
        Assert.Equal("Tracking", Field(format, 0x9402, "AFAreaMode", Bytes(14)));
        Assert.Equal("iAuto+", Field(format, 0x9404, "ExposureProgram", Bytes(7)));
        Assert.Equal("n/a", Top(format, "HighISONoiseReduction2", Shorts(65535), "DSC-RX100M7"));
        Assert.Equal("DMF", Top(format, "FocusMode2", Bytes(6), "ILCE-6000"));
        Assert.Equal("Raw + JPEG", Field(format, 0x9400, "Quality2", Bytes(2), cameraModel: "ILCE-6000"));
    }

    [Fact]
    public void SonyModelIdSuppliesContextForModelSpecificLabels()
    {
        var model = new MakerNoteEntry(0x7001, "SonyModelID", Shorts(312), []);
        var area = new MakerNoteEntry(0x7002, "AFAreaModeSetting", Bytes(11), []);
        var point = new MakerNoteEntry(0x7003, "AFPointSelected", Bytes(1), []);
        var note = new MakerNote(MakerNoteFormat.Sony2, Little, [model, area, point]);
        Assert.Equal("ILCE-6000", note.InterpretForModel(model, null));
        Assert.Equal("Center Zone", note.InterpretForModel(point, null));
        Assert.Equal("n/a", Top(MakerNoteFormat.Sony2, "AFPointsUsed", Bytes(0), "DSC-RX100M5A"));
    }

    [Fact]
    public void TheSonyFocusModeIsInTheLowSevenBitsOfItsByte()
    {
        Assert.Equal("AF-A", Field(MakerNoteFormat.Sony2, 0x9402, "FocusMode", Bytes(132)));
        Assert.Equal("AF-A", Field(MakerNoteFormat.Sony2, 0x9402, "FocusMode", Bytes(4)));
        Assert.Equal("AF-C", Field(MakerNoteFormat.Sony2, 0x9402, "FocusMode", Bytes(131)));
        Assert.Equal("Manual", Field(MakerNoteFormat.Sony2, 0x9402, "FocusMode", Bytes(0)));
        Assert.Equal("(77)", Field(MakerNoteFormat.Sony2, 0x9402, "FocusMode", Bytes(205)));
    }

    [Fact]
    public void CountedSonyTagsReadWithTheirUnit()
    {
        Assert.Equal("Auto", Top(MakerNoteFormat.Sony1, "ColorTemperature", Longs(0)));
        Assert.Equal("5400 K", Top(MakerNoteFormat.Sony1, "ColorTemperature", Longs(5400)));
        Assert.Equal("6000 x 4000", Top(MakerNoteFormat.Sony2, "FullImageSize", Longs(4000, 6000)));
        Assert.Equal("0 x 0", Top(MakerNoteFormat.Sony2, "PreviewImageSize", Longs(0, 0)));
        Assert.Null(Top(MakerNoteFormat.Sony2, "FullImageSize", Longs(4000)));
        Assert.Equal("1", Field(MakerNoteFormat.Sony1, 0x9400, "SequenceImageNumber", Longs(0)));
        Assert.Equal("1 shot", Field(MakerNoteFormat.Sony1, 0x9400, "SequenceLength1", Bytes(1)));
        Assert.Equal("3 shots", Field(MakerNoteFormat.Sony1, 0x9400, "SequenceLength1", Bytes(3)));
        Assert.Equal("1 file", Field(MakerNoteFormat.Sony1, 0x9400, "SequenceLength2", Bytes(1)));
        Assert.Equal("3 files", Field(MakerNoteFormat.Sony1, 0x9400, "SequenceLength2", Bytes(3)));
        Assert.Equal("4000", Field(MakerNoteFormat.Sony1, 0x9400, "SonyImageHeight", Shorts(500), cameraModel: "ILCE-6000"));
        Assert.Equal("2014", Field(MakerNoteFormat.Sony1, 0x9400, "ModelReleaseYear", Bytes(14), cameraModel: "ILCE-6000"));
        Assert.Equal("(255)", Field(MakerNoteFormat.Sony1, 0x9400, "ModelReleaseYear", Bytes(255), cameraModel: "ILCE-6000"));
        Assert.Equal("n/a", Field(MakerNoteFormat.Sony1, 0x9400, "SonyImageHeight", Shorts(500), cameraModel: "ILCE-7SM3"));
        Assert.Equal("n/a", Field(MakerNoteFormat.Sony1, 0x9400, "ModelReleaseYear", Bytes(14), cameraModel: "ILCE-7SM3"));
        Assert.Equal("Raw + JPEG", Field(MakerNoteFormat.Sony1, 0x9400, "Quality2", Bytes(3), cameraModel: "ILCE-7SM3"));
        Assert.Equal("Raw + MPO", Field(MakerNoteFormat.Sony1, 0x9400, "Quality2", Bytes(3), cameraModel: "ILCE-6000"));
        Assert.Equal("n/a", Field(MakerNoteFormat.Sony1, 0x9400, "ShotNumberSincePowerUp", Longs(7), cameraModel: "ILCE-7SM3"));
        Assert.Equal("176, 242", Top(MakerNoteFormat.Sony2, "FlexibleSpotPosition", Shorts(176, 242), "ILCE-6000"));
        Assert.Equal("n/a", Top(MakerNoteFormat.Sony2, "FlexibleSpotPosition", Shorts(176, 242), "DSC-R1"));
        Assert.Null(Top(MakerNoteFormat.Sony2, "FlexibleSpotPosition", Shorts(176)));
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(130, "13%")]
    [InlineData(506, "49%")]
    [InlineData(1013, "99%")]
    [InlineData(1023, "100%")]
    [InlineData(128, "13%")]     // 12.5 rounds away from zero
    [InlineData(384, "38%")]     // 37.5 rounds away from zero
    public void TheSonyZoomPositionIsAWholePercentage(int position, string expected) =>
        Assert.Equal(expected, Field(MakerNoteFormat.Sony1, 0x9404, "LensZoomPosition", Shorts(position), cameraModel: "ILCE-6000"));

    [Fact]
    public void SonyZoomPositionRefusesModelsThatDoNotUseIt() =>
        Assert.Equal("n/a", Field(MakerNoteFormat.Sony1, 0x9404, "LensZoomPosition", Shorts(0), cameraModel: "ILCA-68"));

    [Fact]
    public void ThePreciseSonyColourShiftIsInThousandths()
    {
        Assert.Equal("A/B: 0, G/M: 0", Top(MakerNoteFormat.Sony1, "WBShiftABGMPrecise", SignedLongs(0, 0)));
        Assert.Equal("A/B: A1.00, G/M: G1.00", Top(MakerNoteFormat.Sony1, "WBShiftABGMPrecise", SignedLongs(-1000, -1000)));
        Assert.Equal("A/B: B1.00, G/M: M2.50", Top(MakerNoteFormat.Sony1, "WBShiftABGMPrecise", SignedLongs(1000, 2500)));
    }

    [Fact]
    public void SonyBinaryContextTagsUseTheirByteLayout()
    {
        Assert.Equal("450x359", Top(MakerNoteFormat.Sony1, "FocusFrameSize", Undefined(194, 1, 103, 1, 1, 1)));
        Assert.Equal("n/a", Top(MakerNoteFormat.Sony2, "FocusFrameSize", Undefined(194, 1, 103, 1, 0, 0)));
        Assert.Equal("n/a", Top(MakerNoteFormat.Sony2, "PixelShiftInfo", Undefined(0, 0, 0, 0, 0, 0)));
    }

    [Fact]
    public void SonyLensSpecificationSeparatesApertureNibbles()
    {
        Assert.Equal("24-70mm F1.8-2.8", Top(MakerNoteFormat.Sony1, "LensSpec", Bytes(0, 0, 0x24, 0, 0x70, 0x18, 0x28, 0)));
        Assert.Equal("Unknown", Top(MakerNoteFormat.Sony2, "LensSpec", Bytes(0, 0, 0, 0, 0, 0, 0, 0)));
        Assert.Equal("1.5", Top(MakerNoteFormat.Sony1, "ExposureStandardAdjustment", SignedRational(3, 2)));
        Assert.Equal("+3/2 EV", Top(MakerNoteFormat.Sony1, "FlashExposureComp", SignedRational(3, 2)));
    }

    [Fact]
    public void SonyColourShiftsNameTheirDirection()
    {
        Assert.Equal("G/M: 0", Top(MakerNoteFormat.Sony1, "ColorCompensationFilter", Longs(0)));
        Assert.Equal("G/M: G1", Top(MakerNoteFormat.Sony1, "ColorCompensationFilter", Longs(0xFFFFFFFF)));
        Assert.Equal("G/M: M1", Top(MakerNoteFormat.Sony2, "ColorCompensationFilter", Longs(1)));
        Assert.Equal("A/B: 0, G/M: 0", Top(MakerNoteFormat.Sony1, "WBShiftABGM", SignedLongs(0, 0)));
        Assert.Equal("A/B: 0, G/M: G1", Top(MakerNoteFormat.Sony1, "WBShiftABGM", SignedLongs(0, -1)));
        Assert.Equal("A/B: A1, G/M: G1", Top(MakerNoteFormat.Sony1, "WBShiftABGM", SignedLongs(-1, -1)));
        Assert.Equal("A/B: B2, G/M: M3", Top(MakerNoteFormat.Sony1, "WBShiftABGM", SignedLongs(2, 3)));
        Assert.Null(Top(MakerNoteFormat.Sony1, "WBShiftABGM", SignedLongs(0)));
    }

    [Theory]
    [InlineData("AdobeRGB", "Adobe RGB")]
    [InlineData("Autumnleaves", "Autumn leaves")]
    [InlineData("BW", "Black and White")]
    [InlineData("Standard", "Standard")]
    [InlineData("Unlisted", "(Unlisted)")]
    [InlineData("", "()")]
    public void SonyCreativeStyleUsesItsStringLabels(string text, string expected) =>
        Assert.Equal(expected, Top(MakerNoteFormat.Sony1, "CreativeStyle", Ascii(text)));

    [Fact]
    public void TheSonyFileFormatIsReadFromItsFourBytes()
    {
        Assert.Equal("JPEG", Top(MakerNoteFormat.Sony1, "FileFormat", Bytes(0, 0, 0, 2)));
        Assert.Equal("ARW 2.3.1", Top(MakerNoteFormat.Sony1, "FileFormat", Bytes(3, 3, 1, 0)));
        Assert.Equal("ARW 4.0", Top(MakerNoteFormat.Sony2, "FileFormat", Bytes(4, 0, 0, 0)));
        Assert.Null(Top(MakerNoteFormat.Sony2, "FileFormat", Bytes(9, 9, 9, 9)));
        Assert.Null(Top(MakerNoteFormat.Sony2, "FileFormat", Bytes(3, 3, 1)));
    }

    [Fact]
    public void FujifilmSettingsReadAsTheirLabels()
    {
        Assert.Equal("Daylight", Top(MakerNoteFormat.Fujifilm, "WhiteBalance", Shorts(256)));
        Assert.Equal("CLASSIC CHROME", Top(MakerNoteFormat.Fujifilm, "FilmMode", Shorts(1536)));
        Assert.Equal("(999)", Top(MakerNoteFormat.Fujifilm, "WhiteBalance", Shorts(999)));
        Assert.Equal("On", Top(MakerNoteFormat.Fujifilm, "Macro", Shorts(1)));
        Assert.Null(Top(MakerNoteFormat.Fujifilm, "ColorSpace", Shorts(4)));
        Assert.Equal("R: 1 B: -2", Top(MakerNoteFormat.Fujifilm, "WhiteBalanceFineTune", SignedLongs(20, -40)));
        Assert.Equal("1.5(12)", Top(MakerNoteFormat.Fujifilm, "DigitalZoom", Longs(12)));
        Assert.Equal("Continuous High, 8 fps, (3, 4)", Top(MakerNoteFormat.Fujifilm, "DriveSetting", Longs(0x08040302)));
        Assert.Equal("Face Left Eye Right Eye", Top(MakerNoteFormat.Fujifilm, "FaceElementTypes", Ascii("123")));
    }

    // ---- What has no interpretation ----------------------------------------------------------------------

    [Fact]
    public void ThereIsNothingToAddOutsideTheVocabulary()
    {
        // Another vendor.
        Assert.Null(Top(MakerNoteFormat.Nikon1, "ColorSpace", Shorts(1)));
        Assert.Null(Top(MakerNoteFormat.Nikon1, "ColorSpace", Shorts(1)));
        // A tag the vocabulary does not have, and one this release cannot name.
        Assert.Null(Top(MakerNoteFormat.Canon, "OwnerName", Ascii("someone")));
        Assert.Null(Top(MakerNoteFormat.Canon, null, Shorts(1)));
        // A field name belongs to its array: Quality is a CameraSettings field, not a ShotInfo one.
        Assert.Null(Field(MakerNoteFormat.Canon, 0x0004, "Quality", Shorts(4)));
        Assert.Null(Top(MakerNoteFormat.Canon, "Quality", Shorts(4)));
        // A value that does not have the type and count the interpretation needs.
        Assert.Null(Field(MakerNoteFormat.Canon, 0x0001, "Quality", Shorts(4, 4)));
        Assert.Null(Field(MakerNoteFormat.Canon, 0x0001, "Quality", Ascii("4")));
        Assert.Null(Field(MakerNoteFormat.Canon, 0x0001, "Quality", Undefined(4)));
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        var part = new MakerNoteField(1, "Quality", Shorts(4));
        var entry = new MakerNoteEntry(0x0001, "CameraSettings", Undefined(0), [part]);
        var note = new MakerNote(MakerNoteFormat.Canon, Little, [entry]);
        Assert.Throws<ArgumentNullException>(() => note.Interpret(null!));
        Assert.Throws<ArgumentNullException>(() => note.Interpret(null!, part));
        Assert.Throws<ArgumentNullException>(() => note.Interpret(entry, null!));
    }

    // ---- Pentax DNG --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Quality", "RAW")]
    [InlineData("Focus", "Manual")]
    [InlineData("MeteringMode", "Multi Segment")]
    [InlineData("WhiteBalance", "Auto")]
    [InlineData("ColorSpace", "sRGB")]
    [InlineData("ImageTone", "Bright")]
    public void PentaxDngEnumerationsUseThePrintedLabels(string name, string expected) =>
        Assert.Equal(expected, Top(MakerNoteFormat.PentaxDng, name, Shorts(name == "Quality" ? 4 : name == "Focus" ? 3 : name == "MeteringMode" ? 0 : name == "WhiteBalance" || name == "ColorSpace" ? 0 : 1)));

    [Fact]
    public void PentaxDngFormatsTheDocumentedNumericAndCompositeFields()
    {
        Assert.Equal("12.1.3.0", Top(MakerNoteFormat.PentaxDng, "Version", Bytes(12, 1, 3, 0)));
        Assert.Equal("K-70", Top(MakerNoteFormat.PentaxDng, "ModelID", Longs(78370)));
        Assert.Equal("K-3", Top(MakerNoteFormat.PentaxDng, "ModelID", Longs(77760)));
        Assert.Equal("K-30", Top(MakerNoteFormat.PentaxDng, "ModelID", Longs(77650)));
        Assert.Equal("640x480", Top(MakerNoteFormat.PentaxDng, "PreviewResolution", Shorts(640, 480)));
        Assert.Equal("2020:01:10", Top(MakerNoteFormat.PentaxDng, "Date", Undefined(7, 228, 1, 10)));
        Assert.Equal("15:08:38", Top(MakerNoteFormat.PentaxDng, "Time", Undefined(15, 8, 38)));
        Assert.Equal("25 ms", Top(MakerNoteFormat.PentaxDng, "ExposureTime", Longs(2500)));
        Assert.Equal("F5", Top(MakerNoteFormat.PentaxDng, "FNumber", Shorts(50)));
        Assert.Equal("18.0 mm", Top(MakerNoteFormat.PentaxDng, "FocalLength", Longs(1800)));
        Assert.Equal("0.3 EV", Top(MakerNoteFormat.PentaxDng, "ExposureCompensation", SignedShorts(53, 0)));
        Assert.Equal("0 EV", Top(MakerNoteFormat.Pentax, "AutoBracketing", Longs(0)));
        Assert.Equal("Off, Did not fire", Top(MakerNoteFormat.PentaxDng, "Flash", Shorts(1, 63)));
        Assert.Equal("Center", Top(MakerNoteFormat.PentaxDng, "AFPoint", Shorts(6, 0)));
        Assert.Equal("Aperture Priority", Top(MakerNoteFormat.PentaxDng, "PictureMode", Bytes(5, 0, 1)));
        Assert.Equal("Flash X-Sync Speed AE (1)", Top(MakerNoteFormat.PentaxDng, "PictureMode", Bytes(0x10, 0, 1)));
        Assert.Equal("Single-frame", Top(MakerNoteFormat.PentaxDng, "DriveMode", Bytes(0, 0, 0, 0)));
        Assert.Equal("Unknown (0x00020000)", Top(MakerNoteFormat.PentaxDng, "DynamicRangeExpansion", Undefined(0, 2, 0, 0)));
    }

    [Fact]
    public void PentaxDngShutterCountUsesItsDateAndTimeSiblings()
    {
        var date = new MakerNoteEntry(0x0006, "Date", Undefined(7, 228, 1, 10), []);
        var time = new MakerNoteEntry(0x0007, "Time", Undefined(15, 8, 38), []);
        var shutter = new MakerNoteEntry(0x005d, "ShutterCount", Undefined(247, 19, 211, 249), []);
        var note = new MakerNote(MakerNoteFormat.PentaxDng, Little, [date, time, shutter]);
        Assert.Equal("2828", note.Interpret(shutter));
    }

    [Fact]
    public void PentaxAndPentaxDngKeepTheirInterpretationScopesSeparate()
    {
        Assert.Equal("RAW", Top(MakerNoteFormat.Pentax, "Quality", Shorts(4)));
        Assert.Equal("RAW", Top(MakerNoteFormat.PentaxDng, "Quality", Shorts(4)));
        Assert.Null(Top(MakerNoteFormat.PentaxDng, "LensType", Bytes(8, 196, 0, 0)));
    }

    // ---- The tables themselves ---------------------------------------------------------------------------

    [Fact]
    public void EveryFieldIsListedOnceAndEveryNumberOnceInItsField()
    {
        var seen = new HashSet<(string, int, string)>();
        foreach ((string vendor, int scope, string name, IReadOnlyList<long> numbers, IReadOnlyList<string> labels) in MakerNoteLabels.RulesForTests())
        {
            Assert.True(seen.Add((vendor, scope, name)), $"{vendor} {scope:X4} {name} is listed twice.");
            Assert.True(numbers.Distinct().Count() == numbers.Count, $"{vendor} {scope:X4} {name} lists a number twice.");
            foreach (string label in labels)
            {
                Assert.False(string.IsNullOrWhiteSpace(label), $"{vendor} {scope:X4} {name} has an empty label.");
                Assert.True(label == label.Trim(), $"{vendor} {scope:X4} {name} has a label with blanks around it.");
                Assert.True(label.Length <= 48, $"{vendor} {scope:X4} {name}: a label is a few words, not a description.");
            }
        }
        Assert.True(seen.Count > 150);
    }
}
