using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Values;

/// <summary>
/// The interpreted form of a standard EXIF value, one family of tags at a time. Each expectation is the
/// text a person would read in a property sheet: a short label for an enumerated value, a decimal with
/// its unit for a rational, the stored value in parentheses where it cannot be read as either, and null
/// wherever the raw value is all there is to say.
/// </summary>
public sealed class ExifInterpretationTests
{
    private static ExifTagKey Image(ushort id) => new(ExifDirectoryKind.Ifd0, id);

    private static ExifTagKey Photo(ushort id) => new(ExifDirectoryKind.Exif, id);

    private static ExifTagKey Gps(ushort id) => new(ExifDirectoryKind.Gps, id);

    /// <summary>"1/250" or "16/1 50/1 35/10 56/10" as an unsigned RATIONAL value.</summary>
    private static ExifValue Rational(string text, Endianness order = Endianness.LittleEndian)
    {
        string[] parts = text.Split(' ');
        var bytes = new byte[parts.Length * 8];
        for (int i = 0; i < parts.Length; i++)
        {
            string[] pair = parts[i].Split('/');
            uint numerator = uint.Parse(pair[0], CultureInfo.InvariantCulture);
            uint denominator = uint.Parse(pair[1], CultureInfo.InvariantCulture);
            if (order == Endianness.LittleEndian)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 8, 4), numerator);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((i * 8) + 4, 4), denominator);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 8, 4), numerator);
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan((i * 8) + 4, 4), denominator);
            }
        }
        return ExifValue.FromRaw(TiffFieldType.Rational, (uint)parts.Length, order, bytes);
    }

    /// <summary>"-4/6" as a signed SRATIONAL value.</summary>
    private static ExifValue SignedRational(string text, Endianness order = Endianness.LittleEndian)
    {
        string[] parts = text.Split(' ');
        var bytes = new byte[parts.Length * 8];
        for (int i = 0; i < parts.Length; i++)
        {
            string[] pair = parts[i].Split('/');
            int numerator = int.Parse(pair[0], CultureInfo.InvariantCulture);
            int denominator = int.Parse(pair[1], CultureInfo.InvariantCulture);
            if (order == Endianness.LittleEndian)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 8, 4), numerator);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan((i * 8) + 4, 4), denominator);
            }
            else
            {
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(i * 8, 4), numerator);
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan((i * 8) + 4, 4), denominator);
            }
        }
        return ExifValue.FromRaw(TiffFieldType.SRational, (uint)parts.Length, order, bytes);
    }

    private static ExifValue Short(ushort number, Endianness order = Endianness.LittleEndian)
    {
        var bytes = new byte[2];
        if (order == Endianness.LittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, number);
        else
            BinaryPrimitives.WriteUInt16BigEndian(bytes, number);
        return ExifValue.FromRaw(TiffFieldType.Short, 1, order, bytes);
    }

    private static ExifValue Bytes(TiffFieldType type, params byte[] bytes) =>
        ExifValue.FromRaw(type, (uint)bytes.Length, Endianness.LittleEndian, bytes);

    /// <summary>ASCII text with the terminator the standard counts as part of the value.</summary>
    private static ExifValue Ascii(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
        return ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes);
    }

    /// <summary>A UserComment: the eight-byte character code, then the comment bytes.</summary>
    private static ExifValue Comment(string code, byte[] body, Endianness order = Endianness.LittleEndian)
    {
        var bytes = new byte[8 + body.Length];
        Encoding.ASCII.GetBytes(code).CopyTo(bytes, 0);
        body.CopyTo(bytes, 8);
        return ExifValue.FromRaw(TiffFieldType.Undefined, (uint)bytes.Length, order, bytes);
    }

    private static ExifValue Integer(TiffFieldType type, ushort number)
    {
        if (type == TiffFieldType.Short)
            return Short(number);
        Assert.True(number <= byte.MaxValue, $"{number} does not fit the one-byte type {type}.");
        return Bytes(type, (byte)number);
    }

    // ---- Enumerated tags -------------------------------------------------------------------------------

    [Fact]
    public void EveryNumberTheVocabularyListsHasALabelAndAnyOtherIsShownAsStored()
    {
        int meanings = 0;
        foreach (ExifDirectoryKind directory in Enum.GetValues<ExifDirectoryKind>())
        {
            foreach (ExifTagDefinition definition in ExifTags.Definitions(directory))
            {
                // ComponentsConfiguration enumerates the four positions of one value and has its own case.
                if (definition.ValueMeanings.IsEmpty || definition.Name == "ComponentsConfiguration")
                    continue;
                var key = new ExifTagKey(directory, definition.Id);
                TiffFieldType type = definition.Types[0];
                foreach (ExifTagValueMeaning meaning in definition.ValueMeanings)
                {
                    string? label = ExifTags.Interpret(key, Integer(type, meaning.Number));
                    Assert.False(string.IsNullOrWhiteSpace(label), $"{definition.Name} {meaning.Number} has no label.");
                    Assert.False(label!.StartsWith('('), $"{definition.Name} {meaning.Number} reads {label}.");
                    meanings++;
                }

                // A number nobody assigns is shown as stored, in parentheses, so it cannot pass for a label.
                Assert.Equal("(200)", ExifTags.Interpret(key, Integer(type, 200)));
            }
        }

        // 124 meanings are listed, 7 of them ComponentsConfiguration's; the thumbnail shares the 0th IFD's.
        Assert.True(meanings >= 117, $"Only {meanings} enumerated meanings were exercised.");
    }

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0112, 1, "top, left")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0112, 3, "bottom, right")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0112, 6, "right, top")]
    [InlineData(ExifDirectoryKind.Thumbnail, 0x0112, 8, "left, bottom")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0128, 2, "inch")]
    [InlineData(ExifDirectoryKind.Thumbnail, 0x0128, 3, "cm")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0103, 1, "Uncompressed")]
    [InlineData(ExifDirectoryKind.Thumbnail, 0x0103, 6, "JPEG (old-style)")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x011C, 1, "Chunky")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0213, 1, "Centered")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0213, 2, "Co-sited")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0106, 2, "RGB")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0106, 6, "YCbCr")]
    [InlineData(ExifDirectoryKind.Exif, 0x8822, 3, "Aperture priority")]
    [InlineData(ExifDirectoryKind.Exif, 0x8822, 5, "Creative program")]
    [InlineData(ExifDirectoryKind.Exif, 0x8822, 8, "Landscape mode")]
    [InlineData(ExifDirectoryKind.Exif, 0x9207, 0, "Unknown")]
    [InlineData(ExifDirectoryKind.Exif, 0x9207, 2, "Center weighted average")]
    [InlineData(ExifDirectoryKind.Exif, 0x9207, 3, "Spot")]
    [InlineData(ExifDirectoryKind.Exif, 0x9207, 255, "Other")]
    [InlineData(ExifDirectoryKind.Exif, 0x9208, 0, "Unknown")]
    [InlineData(ExifDirectoryKind.Exif, 0x9208, 10, "Cloudy weather")]
    [InlineData(ExifDirectoryKind.Exif, 0x9208, 255, "Other light source")]
    [InlineData(ExifDirectoryKind.Exif, 0xA001, 1, "sRGB")]
    [InlineData(ExifDirectoryKind.Exif, 0xA217, 2, "One-chip color area")]
    [InlineData(ExifDirectoryKind.Exif, 0xA217, 7, "Trilinear")]
    [InlineData(ExifDirectoryKind.Exif, 0xA401, 0, "Normal process")]
    [InlineData(ExifDirectoryKind.Exif, 0xA402, 0, "Auto")]
    [InlineData(ExifDirectoryKind.Exif, 0xA402, 1, "Manual")]
    [InlineData(ExifDirectoryKind.Exif, 0xA402, 2, "Auto bracket")]
    [InlineData(ExifDirectoryKind.Exif, 0xA403, 0, "Auto")]
    [InlineData(ExifDirectoryKind.Exif, 0xA403, 1, "Manual")]
    [InlineData(ExifDirectoryKind.Exif, 0xA406, 0, "Standard")]
    [InlineData(ExifDirectoryKind.Exif, 0xA407, 2, "High gain up")]
    [InlineData(ExifDirectoryKind.Exif, 0xA408, 2, "Hard")]
    [InlineData(ExifDirectoryKind.Exif, 0xA409, 0, "Normal")]
    [InlineData(ExifDirectoryKind.Exif, 0xA409, 1, "Low")]
    [InlineData(ExifDirectoryKind.Exif, 0xA409, 2, "High")]
    [InlineData(ExifDirectoryKind.Exif, 0xA40C, 0, "Unknown")]
    [InlineData(ExifDirectoryKind.Exif, 0xA40C, 2, "Close view")]
    [InlineData(ExifDirectoryKind.Exif, 0xA460, 0, "Unknown")]
    [InlineData(ExifDirectoryKind.Gps, 0x001E, 0, "Without correction")]
    [InlineData(ExifDirectoryKind.Gps, 0x001E, 1, "Correction applied")]
    public void AnEnumeratedValueReadsAsAShortLabel(ExifDirectoryKind directory, ushort tag, ushort number, string expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(new ExifTagKey(directory, tag), Short(number)));
    }

    [Fact]
    public void WhereTheStandardHasItsOwnTermTheLabelKeepsIt()
    {
        // These are the standard's words; tools that print "Multi-segment", "Auto" or "Above sea level"
        // for the same numbers are using terms of their own.
        Assert.Equal("Pattern", ExifTags.Interpret(Photo(0x9207), Short(5)));
        Assert.Equal("Normal program", ExifTags.Interpret(Photo(0x8822), Short(2)));
        Assert.Equal("Recommended exposure index (REI)", ExifTags.Interpret(Photo(0x8830), Short(2)));
        Assert.Equal("General composite image", ExifTags.Interpret(Photo(0xA460), Short(2)));
        Assert.Equal("Positive ellipsoidal height (at or above ellipsoidal surface)",
            ExifTags.Interpret(Gps(0x0005), Bytes(TiffFieldType.Byte, 0)));
    }

    [Fact]
    public void TheVocabularyKeepsThePublicationsWordingBesideTheLabel()
    {
        ExifTagDefinition orientation = ExifTags.Find(Image(0x0112))!;
        Assert.StartsWith("The 0th row is at the visual top", orientation.ValueMeanings[0].Meaning, StringComparison.Ordinal);
        Assert.Equal("top, left", ExifTags.Interpret(Image(0x0112), Short(1)));
    }

    [Fact]
    public void ALabelDoesNotDependOnTheIntegerTypeTheWriterChose()
    {
        Assert.Equal("Digital still camera", ExifTags.Interpret(Photo(0xA300), Bytes(TiffFieldType.Undefined, 3)));
        Assert.Equal("Directly photographed", ExifTags.Interpret(Photo(0xA301), Bytes(TiffFieldType.Byte, 1)));
        Assert.Equal("One-chip color area", ExifTags.Interpret(Photo(0xA217), Bytes(TiffFieldType.Byte, 2)));
        byte[] one = [1, 0, 0, 0];
        Assert.Equal("top, left", ExifTags.Interpret(Image(0x0112), ExifValue.FromRaw(TiffFieldType.SLong, 1, Endianness.LittleEndian, one)));
        Assert.Equal("top, left", ExifTags.Interpret(Image(0x0112), ExifValue.FromRaw(TiffFieldType.Long, 1, Endianness.LittleEndian, one)));
    }

    [Fact]
    public void AOneByteValuePaddedWithZerosStillHasOneNumber()
    {
        // FileSource is one UNDEFINED byte; some writers store four.
        Assert.Equal("Digital still camera", ExifTags.Interpret(Photo(0xA300), Bytes(TiffFieldType.Undefined, 3, 0, 0, 0)));
        Assert.Null(ExifTags.Interpret(Photo(0xA300), Bytes(TiffFieldType.Undefined, 3, 0, 1, 0)));
    }

    [Fact]
    public void AnEnumeratedTagWithMoreThanOneValueHasNoSingleMeaning()
    {
        byte[] two = [1, 0, 1, 0];
        ExifValue repeated = ExifValue.FromRaw(TiffFieldType.Short, 2, Endianness.LittleEndian, two);
        Assert.Null(ExifTags.Interpret(Image(0x0112), repeated));
        Assert.Null(ExifTags.Interpret(Image(0x0112), Ascii("1")));
    }

    [Theory]
    [InlineData(0, "(0)")]
    [InlineData(9, "(9)")]
    [InlineData(256, "(256)")]
    [InlineData(65535, "(65535)")]
    public void ANumberTheTagDoesNotAssignIsShownAsStored(ushort number, string expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Image(0x0213), Short(number)));       // YCbCrPositioning
        Assert.Equal(expected, ExifTags.Interpret(Photo(0xA301), Short(number)));       // SceneType
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 0 }, "YCbCr")]
    [InlineData(new byte[] { 4, 5, 6, 0 }, "RGB")]
    [InlineData(new byte[] { 1, 0, 0, 0 }, "Y")]
    [InlineData(new byte[] { 1, 2, 3 }, "YCbCr")]
    [InlineData(new byte[] { 1 }, "Y")]
    [InlineData(new byte[] { 0, 0, 0, 0 }, null)]
    [InlineData(new byte[] { 1, 2, 3, 9 }, null)]
    [InlineData(new byte[] { 1, 2, 3, 0, 0 }, null)]
    public void ComponentsConfigurationNamesTheChannelsInOrder(byte[] components, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0x9101), Bytes(TiffFieldType.Undefined, components)));
    }

    [Fact]
    public void TheTwoValuesTheVocabularyDoesNotEnumerateStillReadAsTheStandardSays()
    {
        // ColorSpace FFFF.H is Uncalibrated; FocalPlaneResolutionUnit is "the same as ResolutionUnit".
        Assert.Equal("Uncalibrated", ExifTags.Interpret(Photo(0xA001), Short(0xFFFF)));
        Assert.Equal("(2)", ExifTags.Interpret(Photo(0xA001), Short(2)));
        Assert.Equal(ExifTags.Interpret(Image(0x0128), Short(2)), ExifTags.Interpret(Photo(0xA210), Short(2)));
        Assert.Equal("inch", ExifTags.Interpret(Photo(0xA210), Short(2)));
        Assert.Equal("cm", ExifTags.Interpret(Photo(0xA210), Short(3)));
        Assert.Equal("(9)", ExifTags.Interpret(Photo(0xA210), Short(9)));
    }

    [Theory]
    [InlineData(0x0103, 5, "LZW")]
    [InlineData(0x0103, 7, "JPEG")]
    [InlineData(0x0103, 3, "T4/Group 3 Fax")]
    [InlineData(0x0103, 32773, "PackBits (Macintosh RLE)")]
    [InlineData(0x0103, 65535, "(65535)")]
    [InlineData(0x0106, 0, "White Is Zero")]
    [InlineData(0x0106, 1, "Black Is Zero")]
    [InlineData(0x0106, 5, "CMYK")]
    [InlineData(0x013D, 1, "No prediction scheme used")]
    [InlineData(0x013D, 2, "Horizontal differencing")]
    [InlineData(0x013D, 9, "(9)")]
    public void TheTiffNumbersTheExifPublicationDoesNotListAreNamedFromTiff(ushort tag, ushort number, string expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Image(tag), Short(number)));
    }

    [Fact]
    public void NewSubfileTypeAndSampleFormatAreNamed()
    {
        byte[] primary = [0, 0, 0, 0];
        byte[] reduced = [1, 0, 0, 0];
        byte[] unassigned = [3, 0, 0, 0];
        Assert.Equal("Primary image", ExifTags.Interpret(Image(0x00FE), ExifValue.FromRaw(TiffFieldType.Long, 1, Endianness.LittleEndian, primary)));
        Assert.Equal("Thumbnail/Preview image",
            ExifTags.Interpret(Image(0x00FE), ExifValue.FromRaw(TiffFieldType.Long, 1, Endianness.LittleEndian, reduced)));
        Assert.Equal("(3)", ExifTags.Interpret(Image(0x00FE), ExifValue.FromRaw(TiffFieldType.Long, 1, Endianness.LittleEndian, unassigned)));

        // SampleFormat has a value per sample; an image's samples share one format.
        byte[] three = [1, 0, 1, 0, 1, 0];
        byte[] mixed = [1, 0, 3, 0, 1, 0];
        Assert.Equal("Unsigned integer data", ExifTags.Interpret(Image(0x0153), Short(1)));
        Assert.Equal("Unsigned integer data", ExifTags.Interpret(Image(0x0153), ExifValue.FromRaw(TiffFieldType.Short, 3, Endianness.LittleEndian, three)));
        Assert.Equal("IEEE floating point data", ExifTags.Interpret(Image(0x0153), Short(3)));
        Assert.Null(ExifTags.Interpret(Image(0x0153), ExifValue.FromRaw(TiffFieldType.Short, 3, Endianness.LittleEndian, mixed)));
        Assert.Equal("(7)", ExifTags.Interpret(Image(0x0153), Short(7)));
    }

    // ---- Rationals with units --------------------------------------------------------------------------

    [Theory]
    [InlineData("1/250", "1/250 s")]
    [InlineData("10/2500", "1/250 s")]
    [InlineData("1/1", "1 s")]
    [InlineData("40/10", "4 s")]
    [InlineData("25/10", "2.5 s")]
    [InlineData("16/5", "3.2 s")]
    [InlineData("10/13", "0.769231 s")]
    [InlineData("9974/1000000", "0.009974 s")]
    [InlineData("0/1", null)]
    [InlineData("1/0", "(1/0)")]
    public void ExposureTimeIsAFractionOfASecondOrSeconds(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0x829A), Rational(stored)));
    }

    [Theory]
    [InlineData("28/10", "F2.8")]
    [InlineData("4/1", "F4")]
    [InlineData("11/1", "F11")]
    [InlineData("56/10", "F5.6")]
    [InlineData("9/5", "F1.8")]
    [InlineData("730459/250000", "F2.9")]
    [InlineData("5/4", "F1.2")]         // 1.25 and 1.75 are exact ties, and ties go to the even digit
    [InlineData("7/4", "F1.8")]
    [InlineData("1/0", "(1/0)")]
    [InlineData("0/10", null)]
    public void FNumberIsWrittenWithTwoSignificantDigits(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0x829D), Rational(stored)));
    }

    [Theory]
    [InlineData("500/10", "50.0 mm")]
    [InlineData("237/32", "7.4 mm")]
    [InlineData("1851/100", "18.5 mm")]
    [InlineData("17/4", "4.2 mm")]      // 4.25 is a tie, and ties go to the even digit
    [InlineData("255/100", "2.5 mm")]   // not ties in single precision: 2.55 is stored just below,
    [InlineData("1005/100", "10.1 mm")] // and 10.05 just above
    [InlineData("50/0", "(50/0)")]
    public void FocalLengthIsMillimetresToOneDecimal(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0x920A), Rational(stored)));
    }

    [Theory]
    [InlineData(75, "75.0 mm")]
    [InlineData(27, "27.0 mm")]
    [InlineData(0, "Unknown")]
    public void FocalLengthIn35mmFilmIsMillimetresAndZeroIsUnknown(ushort stored, string expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0xA405), Short(stored)));
    }

    [Theory]
    [InlineData("0/10", "0 EV")]
    [InlineData("1/3", "+1/3 EV")]
    [InlineData("-4/6", "-2/3 EV")]
    [InlineData("100/100", "+1 EV")]
    [InlineData("-200/100", "-2 EV")]
    [InlineData("33/100", "+33/100 EV")]
    [InlineData("1/-3", "-1/3 EV")]
    [InlineData("1/0", "(1/0)")]
    public void ExposureBiasIsASignedFractionOfAStop(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0x9204), SignedRational(stored)));
    }

    [Theory]
    [InlineData(0x9202, "200/100", "F2")]
    [InlineData(0x9202, "327680/65536", "F5.7")]
    [InlineData(0x9202, "3/1", "F2.8")]
    [InlineData(0x9202, "0/1", "F1")]
    [InlineData(0x9202, "4676053/524288", "F22")]
    [InlineData(0x9205, "30/10", "F2.8")]
    [InlineData(0x9205, "43/10", "F4.4")]
    [InlineData(0x9205, "3/0", "(3/0)")]
    public void AnApexApertureIsConvertedToAnFNumber(ushort tag, string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(tag), Rational(stored)));
    }

    [Theory]
    [InlineData("1300/100", "1/8192 s")]
    [InlineData("393216/65536", "1/64 s")]
    [InlineData("6965784/1000000", "1/125 s")]
    [InlineData("319/32", "1/1002 s")]
    [InlineData("0/32", "1 s")]
    [InlineData("-200/100", "4 s")]
    [InlineData("-90112/65536", "3 s")]
    [InlineData("5/0", "(5/0)")]
    [InlineData("4000/1", null)]
    public void AnApexShutterSpeedIsConvertedToAnExposureTime(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0x9201), SignedRational(stored)));
    }

    [Theory]
    [InlineData("12024/2560", "4.69688")]
    [InlineData("-1264/100", "-12.64")]
    [InlineData("500/100", "5")]
    [InlineData("-61585/686999", "-0.0896435")]
    [InlineData("0/1", "0")]
    [InlineData("19944/2560", "7.79063")]   // 7.790625 in decimal, just above it in single precision
    [InlineData("12345678/1", "12345700")]
    [InlineData("1/1000000", "0.000001")]
    [InlineData("-5/100000", "-0.00005")]
    [InlineData("1/0", "(1/0)")]
    public void BrightnessStaysAnApexValueWithSixSignificantDigits(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0x9203), SignedRational(stored)));
    }

    [Theory]
    [InlineData("3350/100", "33.50 m")]
    [InlineData("7368/1000", "7.37 m")]
    [InlineData("0/1", "Unknown")]
    [InlineData("4294967295/1", "Infinity")]
    [InlineData("5/0", "(5/0)")]
    public void SubjectDistanceIsMetresWithItsTwoReservedNumerators(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0x9206), Rational(stored)));
    }

    [Theory]
    [InlineData("16/16", "1.0")]
    [InlineData("153/100", "1.5")]
    [InlineData("0/1", "Digital zoom not used")]
    [InlineData("0/0", "Digital zoom not used")]
    [InlineData("2/0", "(2/0)")]
    public void DigitalZoomRatioIsADecimalAndAZeroNumeratorMeansUnused(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0xA404), Rational(stored)));
    }

    [Theory]
    [InlineData("160/10 500/10 35/10 56/10", "16-50mm F3.5-5.6")]
    [InlineData("1040/100 3710/100 18/10 49/10", "10.4-37.1mm F1.8-4.9")]
    [InlineData("5000/100 5000/100 200/100 200/100", "50mm F2")]
    [InlineData("17/4 17/4 9/5 9/5", "4.25mm F1.8")]
    [InlineData("35/1 80/1 0/1 0/1", "35-80mm")]
    [InlineData("90/1 90/1 0/0 0/0", "90mm")]
    [InlineData("0/10 0/10 0/10 0/10", "n/a")]
    [InlineData("80/1 35/1 0/1 0/1", null)]
    [InlineData("35/1 80/1 0/1", null)]
    public void LensSpecificationIsAFocalRangeAndAnApertureRange(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0xA432), Rational(stored)));
    }

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x011A, "72/1", "72")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x011B, "300/1", "300")]
    [InlineData(ExifDirectoryKind.Thumbnail, 0x011A, "358088704/2097152", "170.75")]
    [InlineData(ExifDirectoryKind.Exif, 0xA20E, "5472000/1436", "3810.58")]
    [InlineData(ExifDirectoryKind.Exif, 0x9102, "5/2", "2.5")]
    [InlineData(ExifDirectoryKind.Exif, 0xA500, "22/10", "2.2")]
    [InlineData(ExifDirectoryKind.Gps, 0x0011, "825189/3865", "213.503")]
    [InlineData(ExifDirectoryKind.Gps, 0x000B, "12/10", "1.2")]
    [InlineData(ExifDirectoryKind.Gps, 0x001F, "65/1", "65")]
    [InlineData(ExifDirectoryKind.Ifd0, 0x011A, "72/0", "(72/0)")]
    [InlineData(ExifDirectoryKind.Exif, 0x9102, "0/0", "(0/0)")]
    public void ARationalWithoutAUnitIsItsDecimalValue(ExifDirectoryKind directory, ushort tag, string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(new ExifTagKey(directory, tag), Rational(stored)));
    }

    [Fact]
    public void TheEnvironmentalTagsCarryTheirUnitsAndTheirUnknownMarker()
    {
        Assert.Equal("24.5 \u00B0C", ExifTags.Interpret(Photo(0x9400), SignedRational("245/10")));
        Assert.Equal("55 %", ExifTags.Interpret(Photo(0x9401), Rational("55/1")));
        Assert.Equal("1013.2 hPa", ExifTags.Interpret(Photo(0x9402), Rational("10132/10")));
        Assert.Equal("-3.5 m", ExifTags.Interpret(Photo(0x9403), SignedRational("-35/10")));
        Assert.Equal("980 mGal", ExifTags.Interpret(Photo(0x9404), Rational("980/1")));
        Assert.Equal("-12.5 deg", ExifTags.Interpret(Photo(0x9405), SignedRational("-125/10")));
        Assert.Equal("Unknown", ExifTags.Interpret(Photo(0x9400), SignedRational("0/-1")));
        Assert.Equal("Unknown", ExifTags.Interpret(Photo(0x9402), Rational("0/4294967295")));
        Assert.Equal("(1/0)", ExifTags.Interpret(Photo(0x9401), Rational("1/0")));
    }

    [Fact]
    public void ByteOrderAndCultureDoNotChangeTheText()
    {
        Assert.Equal("F2.8", ExifTags.Interpret(Photo(0x829D), Rational("28/10", Endianness.BigEndian)));
        Assert.Equal("-2/3 EV", ExifTags.Interpret(Photo(0x9204), SignedRational("-4/6", Endianness.BigEndian)));

        // A culture that writes a decimal comma and an unusual minus sign must not reach the output.
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NegativeSign = "~";
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            Assert.Equal("F2.8", ExifTags.Interpret(Photo(0x829D), Rational("28/10")));
            Assert.Equal("50.0 mm", ExifTags.Interpret(Photo(0x920A), Rational("500/10")));
            Assert.Equal("-12.64", ExifTags.Interpret(Photo(0x9203), SignedRational("-1264/100")));
            Assert.Equal("14:38:55.9", ExifTags.Interpret(Gps(0x0007), Rational("14/1 38/1 5588/100")));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // ---- Dates -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ExifDirectoryKind.Ifd0, 0x0132, "2005:04:10 12:00:00")]
    [InlineData(ExifDirectoryKind.Exif, 0x9003, "2011:06:27 21:35:33")]
    [InlineData(ExifDirectoryKind.Exif, 0x9004, "    :  :     :  :  ")]
    [InlineData(ExifDirectoryKind.Gps, 0x001D, "2019:06:27")]
    [InlineData(ExifDirectoryKind.Thumbnail, 0x0132, "2005-04-10T12:00:00")]
    public void ADateIsReturnedAsWritten(ExifDirectoryKind directory, ushort tag, string written)
    {
        Assert.Equal(written, ExifTags.Interpret(new ExifTagKey(directory, tag), Ascii(written)));
    }

    [Fact]
    public void ADateOfTheWrongTypeIsNotADate()
    {
        Assert.Null(ExifTags.Interpret(Photo(0x9003), Bytes(TiffFieldType.Undefined, 0x32, 0x30)));
    }

    // ---- GPS -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("51/1 106969/10000 0/1", "51 deg 10' 41.81\"")]
    [InlineData("36/1 26/1 54/1", "36 deg 26' 54\"")]
    [InlineData("7/1 0/1 0/1000", "7 deg 0' 0.00\"")]
    [InlineData("1/1 31/1 47/50", "1 deg 31' 0.94\"")]
    [InlineData("12/1 592834/10000 0/1", "12 deg 59' 17.00\"")]
    [InlineData("515/10 0/1 0/1", "51 deg 30' 0.00\"")]
    [InlineData("1/3 0/1 0/1", "0 deg 20' 0.00\"")]    // a third of a degree is exactly twenty minutes
    [InlineData("51/1 10/0 0/1", null)]
    [InlineData("51/1 10/1", null)]
    public void ACoordinateIsDegreesMinutesAndSeconds(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Gps(0x0002), Rational(stored)));
        Assert.Equal(expected, ExifTags.Interpret(Gps(0x0004), Rational(stored)));
        Assert.Equal(expected, ExifTags.Interpret(Gps(0x0014), Rational(stored)));
        Assert.Equal(expected, ExifTags.Interpret(Gps(0x0016), Rational(stored)));
    }

    [Theory]
    [InlineData(0x0001, "N", "North")]
    [InlineData(0x0001, "S", "South")]
    [InlineData(0x0003, "E", "East")]
    [InlineData(0x0003, "W", "West")]
    [InlineData(0x0013, "S", "South")]
    [InlineData(0x0015, "W", "West")]
    [InlineData(0x0009, "A", "Measurement in progress")]
    [InlineData(0x0009, "V", "Measurement interrupted")]
    [InlineData(0x000A, "3", "3-dimensional measurement")]
    [InlineData(0x000C, "K", "km/h")]
    [InlineData(0x000C, "N", "knots")]
    [InlineData(0x0010, "T", "True direction")]
    [InlineData(0x0010, "M", "Magnetic direction")]
    [InlineData(0x0019, "M", "miles")]
    [InlineData(0x0001, "E", null)]
    [InlineData(0x0003, "EW", null)]
    [InlineData(0x0012, "WGS-84", null)]
    public void AGpsReferenceLetterNamesWhatItStandsFor(ushort tag, string letter, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Gps(tag), Ascii(letter)));
    }

    [Fact]
    public void ACoordinateReadFromASnapshotCarriesItsReference()
    {
        ExifValue latitude = Rational("51/1 106969/10000 0/1");
        ExifValue longitude = Rational("1/1 495984/10000 0/1");
        var metadata = new ExifMetadata(Endianness.LittleEndian,
        [
            new ExifEntry(Gps(0x0001), Ascii("N")),
            new ExifEntry(Gps(0x0002), latitude),
            new ExifEntry(Gps(0x0003), Ascii("W")),
            new ExifEntry(Gps(0x0004), longitude),
            new ExifEntry(Gps(0x0006), Rational("411/10")),
            new ExifEntry(Photo(0x829D), Rational("28/10")),
        ]);

        Assert.Equal("51 deg 10' 41.81\" N", ExifTags.Interpret(metadata, Gps(0x0002)));
        Assert.Equal("1 deg 49' 35.90\" W", ExifTags.Interpret(metadata, Gps(0x0004)));
        // The single-value form cannot know the hemisphere, and says only what the value says.
        Assert.Equal("51 deg 10' 41.81\"", ExifTags.Interpret(Gps(0x0002), latitude));
        // Every other tag reads exactly as it does on its own, and an absent tag has nothing to read.
        Assert.Equal("North", ExifTags.Interpret(metadata, Gps(0x0001)));
        Assert.Equal("41.1 m", ExifTags.Interpret(metadata, Gps(0x0006)));
        Assert.Equal("F2.8", ExifTags.Interpret(metadata, Photo(0x829D)));
        Assert.Null(ExifTags.Interpret(metadata, Gps(0x0014)));
    }

    [Fact]
    public void ACoordinateWithoutAUsableReferenceStaysBare()
    {
        var missing = new ExifMetadata(Endianness.LittleEndian, [new ExifEntry(Gps(0x0002), Rational("36/1 26/1 54/1"))]);
        Assert.Equal("36 deg 26' 54\"", ExifTags.Interpret(missing, Gps(0x0002)));

        var wrong = new ExifMetadata(Endianness.LittleEndian,
        [
            new ExifEntry(Gps(0x0001), Ascii("E")),
            new ExifEntry(Gps(0x0002), Rational("36/1 26/1 54/1")),
        ]);
        Assert.Equal("36 deg 26' 54\"", ExifTags.Interpret(wrong, Gps(0x0002)));
    }

    [Theory]
    [InlineData("97/1", "97 m")]
    [InlineData("411/10", "41.1 m")]
    [InlineData("0/100", "0.0 m")]
    [InlineData("353700/100", "3537.0 m")]
    [InlineData("5/0", "(5/0)")]
    public void GpsAltitudeIsMetres(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Gps(0x0006), Rational(stored)));
    }

    [Theory]
    [InlineData("14/1 38/1 5588/100", "14:38:55.9")]
    [InlineData("2/1 49/1 33000/1000", "02:49:33")]
    [InlineData("9/1 54/1 28/1", "09:54:28")]
    [InlineData("0/1 0/1 0/1", "00:00:00")]
    [InlineData("1/3 0/1 0/1", "00:20:00")]             // a third of an hour, with no fraction left over
    [InlineData("25/1 0/1 0/1", null)]
    [InlineData("9/1 54/0 28/1", null)]
    [InlineData("9/1 54/1", null)]
    public void GpsTimeIsHoursMinutesAndSeconds(string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Gps(0x0007), Rational(stored)));
    }

    // ---- Flash -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0x00, "No flash")]
    [InlineData(0x01, "Fired")]
    [InlineData(0x05, "Fired, return light not detected")]
    [InlineData(0x07, "Fired, return light detected")]
    [InlineData(0x09, "Yes, compulsory")]
    [InlineData(0x0D, "Yes, compulsory, return light not detected")]
    [InlineData(0x0F, "Yes, compulsory, return light detected")]
    [InlineData(0x10, "No, compulsory")]
    [InlineData(0x18, "No, auto")]
    [InlineData(0x19, "Yes, auto")]
    [InlineData(0x1D, "Yes, auto, return light not detected")]
    [InlineData(0x1F, "Yes, auto, return light detected")]
    [InlineData(0x20, "No flash function")]
    [InlineData(0x41, "Fired, red-eye reduction")]
    [InlineData(0x45, "Fired, red-eye reduction, return light not detected")]
    [InlineData(0x49, "Yes, compulsory, red-eye reduction")]
    [InlineData(0x4D, "Yes, compulsory, red-eye reduction, return light not detected")]
    [InlineData(0x59, "Yes, auto, red-eye reduction")]
    [InlineData(0x5F, "Yes, auto, red-eye reduction, return light detected")]
    [InlineData(0x08, "No, compulsory")]    // compulsory firing that did not fire: not in the standard's list
    [InlineData(0x03, "(3)")]               // return bits 01 are reserved
    [InlineData(0x21, "(33)")]              // no flash function, and yet it fired
    [InlineData(0x80, "(128)")]             // bit 7 is not assigned
    public void FlashIsDescribedBitFieldByBitField(ushort stored, string expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(0x9209), Short(stored)));
    }

    // ---- Versions --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0x9000, "0230", "2.30")]
    [InlineData(0x9000, "0232", "2.32")]
    [InlineData(0x9000, "0300", "3.00")]
    [InlineData(0xA000, "0100", "1.00")]
    [InlineData(0x9000, "1005", "10.05")]
    [InlineData(0x9000, "02.3", null)]
    [InlineData(0x9000, "023", null)]
    [InlineData(0xA000, "01000", null)]
    public void AVersionIsMajorDotMinor(ushort tag, string stored, string? expected)
    {
        Assert.Equal(expected, ExifTags.Interpret(Photo(tag), Bytes(TiffFieldType.Undefined, Encoding.ASCII.GetBytes(stored))));
    }

    [Fact]
    public void TheGpsVersionIsItsFourBytesDotted()
    {
        Assert.Equal("2.3.0.0", ExifTags.Interpret(Gps(0x0000), Bytes(TiffFieldType.Byte, 2, 3, 0, 0)));
        Assert.Equal("2.2.0.0", ExifTags.Interpret(Gps(0x0000), Bytes(TiffFieldType.Byte, 2, 2, 0, 0)));
        Assert.Null(ExifTags.Interpret(Gps(0x0000), Bytes(TiffFieldType.Byte, 2, 3, 0)));
        Assert.Null(ExifTags.Interpret(Gps(0x0000), Bytes(TiffFieldType.Undefined, 2, 3, 0, 0)));
    }

    // ---- UserComment -----------------------------------------------------------------------------------

    [Fact]
    public void AnAsciiCommentIsTheTextBehindItsPrefix()
    {
        Assert.Equal("Classic View", ExifTags.Interpret(Photo(0x9286), Comment("ASCII\0\0\0", Encoding.ASCII.GetBytes("Classic View"))));
        // The standard pads the comment area with spaces, and writers also pad or terminate with NUL.
        Assert.Equal("padded", ExifTags.Interpret(Photo(0x9286), Comment("ASCII\0\0\0", Encoding.ASCII.GetBytes("padded      "))));
        Assert.Equal("ended", ExifTags.Interpret(Photo(0x9286), Comment("ASCII\0\0\0", Encoding.ASCII.GetBytes("ended\0\0\0junk"))));
        Assert.Equal(string.Empty, ExifTags.Interpret(Photo(0x9286), Comment("ASCII\0\0\0", Encoding.ASCII.GetBytes("        "))));
        Assert.Equal(string.Empty, ExifTags.Interpret(Photo(0x9286), Comment("ASCII\0\0\0", [])));
    }

    [Fact]
    public void AUnicodeCommentIsReadInTheByteOrderOfTheFileOrOfItsMark()
    {
        const string text = "\u0423\u043A\u0440\u0430\u0457\u043D\u0430";
        Assert.Equal(text, ExifTags.Interpret(Photo(0x9286), Comment("UNICODE\0", Encoding.Unicode.GetBytes(text))));
        Assert.Equal(text, ExifTags.Interpret(Photo(0x9286),
            Comment("UNICODE\0", Encoding.BigEndianUnicode.GetBytes(text), Endianness.BigEndian)));

        // A byte order mark wins over the file: big-endian text in a little-endian file.
        byte[] marked = [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("AB")];
        Assert.Equal("AB", ExifTags.Interpret(Photo(0x9286), Comment("UNICODE\0", marked)));

        // An unpaired surrogate is not text, so there is nothing to show.
        byte[] broken = [0x00, 0xD8, 0x41, 0x00];
        Assert.Null(ExifTags.Interpret(Photo(0x9286), Comment("UNICODE\0", broken)));
    }

    [Fact]
    public void ACommentWithAnUndefinedCodeIsReadOnlyWhenItIsText()
    {
        Assert.Equal("Created with GIMP",
            ExifTags.Interpret(Photo(0x9286), Comment("\0\0\0\0\0\0\0\0", Encoding.ASCII.GetBytes("Created with GIMP"))));
        Assert.Equal("caf\u00E9", ExifTags.Interpret(Photo(0x9286), Comment("\0\0\0\0\0\0\0\0", Encoding.UTF8.GetBytes("caf\u00E9"))));
        byte[] binary = [0xFF, 0xFE, 0xFD, 0x80];
        Assert.Null(ExifTags.Interpret(Photo(0x9286), Comment("\0\0\0\0\0\0\0\0", binary)));
    }

    [Fact]
    public void ACommentThatCannotBeDecodedHasNoInterpretation()
    {
        byte[] jis = [0x30, 0x21, 0x30, 0x22];
        Assert.Null(ExifTags.Interpret(Photo(0x9286), Comment("JIS\0\0\0\0\0", jis)));
        Assert.Null(ExifTags.Interpret(Photo(0x9286), Comment("LATIN1\0\0", Encoding.ASCII.GetBytes("text"))));
        byte[] highBit = [0x41, 0xE9];
        Assert.Null(ExifTags.Interpret(Photo(0x9286), Comment("ASCII\0\0\0", highBit)));
        // Shorter than the character code itself, and a comment stored under the wrong type.
        Assert.Null(ExifTags.Interpret(Photo(0x9286), Bytes(TiffFieldType.Undefined, 0x41, 0x53, 0x43)));
        Assert.Null(ExifTags.Interpret(Photo(0x9286), Ascii("ASCII   text")));
    }

    // ---- Nothing to interpret --------------------------------------------------------------------------

    [Fact]
    public void ATagThatIsFreeTextOrAPlainNumberHasNoInterpretation()
    {
        Assert.Null(ExifTags.Interpret(Image(0x010F), Ascii("NIKON")));                             // Make
        Assert.Null(ExifTags.Interpret(Image(0x0100), Short(4000)));                                // ImageWidth
        Assert.Null(ExifTags.Interpret(Photo(0x927C), Bytes(TiffFieldType.Undefined, 1, 2, 3)));    // MakerNote
        Assert.Null(ExifTags.Interpret(Photo(0x9290), Ascii("123")));                               // SubSecTime
        Assert.Null(ExifTags.Interpret(Image(0x013E), Rational("313/1000 329/1000")));              // WhitePoint
        Assert.Null(ExifTags.Interpret(new ExifTagKey(ExifDirectoryKind.Interop, 0x0001), Ascii("R98")));
    }

    [Fact]
    public void ATagOutsideTheVocabularyOrItsDirectoryHasNoInterpretation()
    {
        Assert.Null(ExifTags.Interpret(Photo(0xFFF0), Short(1)));
        // ExposureTime belongs to the Exif IFD; the same number in the 0th IFD is not that tag.
        Assert.Null(ExifTags.Interpret(Image(0x829A), Rational("1/250")));
        Assert.Null(ExifTags.Interpret(new ExifTagKey(ExifDirectoryKind.Interop, 0x829A), Rational("1/250")));
    }

    [Fact]
    public void AValueOfTheWrongShapeHasNoInterpretation()
    {
        Assert.Null(ExifTags.Interpret(Photo(0x829A), Short(250)));                         // not a rational
        Assert.Null(ExifTags.Interpret(Photo(0x829A), Rational("1/250 1/125")));            // two values
        Assert.Null(ExifTags.Interpret(Photo(0x9209), Rational("16/1")));                   // Flash is an integer
        Assert.Null(ExifTags.Interpret(Photo(0xA405), Rational("75/1")));
        Assert.Null(ExifTags.Interpret(Gps(0x0001), Bytes(TiffFieldType.Byte, 0x4E)));
        ExifValue empty = ExifValue.FromRaw(TiffFieldType.Rational, 0, Endianness.LittleEndian, []);
        Assert.Null(ExifTags.Interpret(Photo(0x829D), empty));
        Assert.Null(ExifTags.Interpret(Image(0x0132), ExifValue.FromRaw(TiffFieldType.Ascii, 0, Endianness.LittleEndian, [])));
    }

    [Fact]
    public void AMissingArgumentIsRejectedAndAnAbsentTagIsNot()
    {
        Assert.Throws<ArgumentNullException>(() => ExifTags.Interpret(Photo(0x829D), null!));
        Assert.Throws<ArgumentNullException>(() => ExifTags.Interpret((ExifMetadata)null!, Photo(0x829D)));
        var empty = new ExifMetadata(Endianness.LittleEndian, []);
        Assert.Null(ExifTags.Interpret(empty, Photo(0x829D)));
    }
}
