using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.WebP;
using Xunit;

namespace MetadataSharp.Tests.Values;

/// <summary>
/// The typed convenience properties of an EXIF block: on files authored here in both byte orders and
/// three containers, and one property at a time on values that are absent, of another type or count,
/// or not readable as what the property promises. A property answers null for all of those.
/// </summary>
public sealed class ExifMetadataPropertyTests
{
    private const ExifDirectoryKind Ifd0 = ExifDirectoryKind.Ifd0;
    private const ExifDirectoryKind Photo = ExifDirectoryKind.Exif;
    private const ExifDirectoryKind Gps = ExifDirectoryKind.Gps;

    // ---- Values ---------------------------------------------------------------------------------------

    private static ExifValue Ascii(string text, Endianness order) =>
        ExifValue.FromRaw(TiffFieldType.Ascii, (uint)text.Length + 1, order, [.. Encoding.ASCII.GetBytes(text), 0]);

    private static ExifValue Integers(TiffFieldType type, Endianness order, params uint[] numbers)
    {
        int width = type is TiffFieldType.Byte or TiffFieldType.SByte ? 1 : type is TiffFieldType.Short or TiffFieldType.SShort ? 2 : 4;
        var bytes = new byte[numbers.Length * width];
        bool little = order == Endianness.LittleEndian;
        for (int i = 0; i < numbers.Length; i++)
        {
            Span<byte> at = bytes.AsSpan(i * width, width);
            if (width == 1) at[0] = (byte)numbers[i];
            else if (width == 2 && little) BinaryPrimitives.WriteUInt16LittleEndian(at, (ushort)numbers[i]);
            else if (width == 2) BinaryPrimitives.WriteUInt16BigEndian(at, (ushort)numbers[i]);
            else if (little) BinaryPrimitives.WriteUInt32LittleEndian(at, numbers[i]);
            else BinaryPrimitives.WriteUInt32BigEndian(at, numbers[i]);
        }
        return ExifValue.FromRaw(type, (uint)numbers.Length, order, bytes);
    }

    private static ExifValue Short(Endianness order, params uint[] numbers) => Integers(TiffFieldType.Short, order, numbers);

    private static ExifValue Long(Endianness order, params uint[] numbers) => Integers(TiffFieldType.Long, order, numbers);

    /// <summary>Unsigned rationals: numerator, denominator, numerator, denominator and so on.</summary>
    private static ExifValue Rational(Endianness order, params uint[] halves) =>
        ExifValue.FromRaw(TiffFieldType.Rational, (uint)halves.Length / 2, order, Integers(TiffFieldType.Long, order, halves).Raw());

    private static ExifValue SignedRational(Endianness order, params uint[] halves) =>
        ExifValue.FromRaw(TiffFieldType.SRational, (uint)halves.Length / 2, order, Integers(TiffFieldType.Long, order, halves).Raw());

    /// <summary>A block holding exactly the given tags, as the reader would hand it over.</summary>
    private static ExifMetadata Block(Endianness order, params (ExifDirectoryKind Directory, ushort Tag, ExifValue Value)[] tags) =>
        new(order, tags.Select(t => new ExifEntry(new ExifTagKey(t.Directory, t.Tag), t.Value)).ToArray());

    private static ExifMetadata Block(params (ExifDirectoryKind Directory, ushort Tag, ExifValue Value)[] tags) =>
        Block(Endianness.LittleEndian, tags);

    private const Endianness II = Endianness.LittleEndian;

    // ---- An authored camera file ----------------------------------------------------------------------

    /// <summary>The tags of one picture: every property has something to read.</summary>
    private static (ExifDirectoryKind Directory, ushort Tag, ExifValue Value)[] Picture(Endianness order) =>
    [
        (Ifd0, 0x010F, Ascii("Canon", order)),
        (Ifd0, 0x0110, Ascii("Canon EOS R6", order)),
        (Ifd0, 0x0112, Short(order, 6)),
        (Photo, 0x829A, Rational(order, 1, 250)),
        (Photo, 0x829D, Rational(order, 28, 10)),
        (Photo, 0x8827, Short(order, 400)),
        (Photo, 0x9003, Ascii("2024:02:29 13:14:15", order)),
        (Photo, 0x9011, Ascii("+05:30", order)),
        (Photo, 0x920A, Rational(order, 500, 10)),
        (Photo, 0xA002, Long(order, 6000)),
        (Photo, 0xA003, Short(order, 4000)),
        (Photo, 0xA434, Ascii("RF24-105mm F4 L IS USM", order)),
        (Gps, 0x0001, Ascii("N", order)),
        (Gps, 0x0002, Rational(order, 51, 1, 10, 1, 4181, 100)),
        (Gps, 0x0003, Ascii("W", order)),
        (Gps, 0x0004, Rational(order, 0, 1, 7, 1, 3930, 100)),
        (Gps, 0x0005, Integers(TiffFieldType.Byte, order, 1)),
        (Gps, 0x0006, Rational(order, 1235, 10)),
    ];

    /// <summary>
    /// A TIFF block with a primary, an Exif and a GPS directory, laid out the plain way: the three
    /// directories one after another, then every value that does not fit its entry, each on an even
    /// offset, in the order of the entries.
    /// </summary>
    private static byte[] Tiff(Endianness order, (ExifDirectoryKind Directory, ushort Tag, ExifValue Value)[] tags)
    {
        bool little = order == Endianness.LittleEndian;
        var directories = new[] { Ifd0, Photo, Gps }
            .Select(kind => tags.Where(t => t.Directory == kind).OrderBy(t => t.Tag).Select(t => (t.Tag, t.Value)).ToList())
            .ToArray();
        // The primary directory points at the other two.
        directories[0].Add((0x8769, Long(order, 0)));
        directories[0].Add((0x8825, Long(order, 0)));
        int[] starts = new int[3];
        int at = 8;
        for (int d = 0; d < 3; d++)
        {
            starts[d] = at;
            at += 2 + (directories[d].Count * 12) + 4;
        }
        directories[0][^2] = (0x8769, Long(order, (uint)starts[1]));
        directories[0][^1] = (0x8825, Long(order, (uint)starts[2]));

        var file = new List<byte>(little ? [0x49, 0x49, 42, 0] : [0x4D, 0x4D, 0, 42]);
        void U16(List<byte> to, int number) { if (little) { to.Add((byte)number); to.Add((byte)(number >> 8)); } else { to.Add((byte)(number >> 8)); to.Add((byte)number); } }
        void U32(List<byte> to, long number) { if (little) { U16(to, (int)(number & 0xFFFF)); U16(to, (int)(number >> 16)); } else { U16(to, (int)(number >> 16)); U16(to, (int)(number & 0xFFFF)); } }
        U32(file, 8);
        var values = new List<byte>();
        foreach (var directory in directories)
        {
            U16(file, directory.Count);
            foreach ((ushort tag, ExifValue value) in directory)
            {
                U16(file, tag);
                U16(file, (int)value.FieldType);
                U32(file, value.Count);
                byte[] raw = value.Raw();
                if (raw.Length <= 4)
                {
                    file.AddRange(raw);
                    file.AddRange(new byte[4 - raw.Length]);
                }
                else
                {
                    U32(file, at + values.Count);
                    values.AddRange(raw);
                    if (values.Count % 2 != 0) values.Add(0);
                }
            }
            U32(file, 0);
        }
        Assert.Equal(at, file.Count);
        file.AddRange(values);
        return file.ToArray();
    }

    private static byte[] Container(string name, byte[] tiff) => name switch
    {
        "jpeg" => JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff))),
        "png" => PngFixture.Build(PngFixture.Chunk("eXIf", tiff)),
        "webp" => WebPFixture.Extended(0x08, WebPFixture.Image(0)[12..], WebPFixture.Chunk("EXIF", tiff)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [InlineData("jpeg", Endianness.LittleEndian)]
    [InlineData("jpeg", Endianness.BigEndian)]
    [InlineData("png", Endianness.LittleEndian)]
    [InlineData("png", Endianness.BigEndian)]
    [InlineData("webp", Endianness.LittleEndian)]
    [InlineData("webp", Endianness.BigEndian)]
    public void AnAuthoredPictureAnswersEveryProperty(string container, Endianness order)
    {
        ImageMetadata image = ImageMetadata.Read(Container(container, Tiff(order, Picture(order))));
        Assert.Empty(image.Errors);
        ExifMetadata exif = image.Exif!;
        Assert.NotNull(exif);
        Assert.Equal(order, exif.ByteOrder);

        Assert.Equal("Canon", exif.Make);
        Assert.Equal("Canon EOS R6", exif.Model);
        Assert.Equal("RF24-105mm F4 L IS USM", exif.LensModel);
        Assert.Equal(new DateTime(2024, 2, 29, 13, 14, 15), exif.DateTimeOriginal);
        Assert.Equal(DateTimeKind.Unspecified, exif.DateTimeOriginal!.Value.Kind);
        Assert.Equal(new DateTimeOffset(2024, 2, 29, 13, 14, 15, new TimeSpan(5, 30, 0)), exif.DateTimeOriginalWithOffset);
        Assert.Equal(new TimeSpan(5, 30, 0), exif.DateTimeOriginalWithOffset!.Value.Offset);
        Assert.Equal(ExifOrientation.RightTop, exif.Orientation);
        Assert.Equal(new UnsignedRational(1, 250), exif.ExposureTime);
        Assert.Equal(2.8, exif.FNumber);
        Assert.Equal(400, exif.IsoSpeed);
        Assert.Equal(50.0, exif.FocalLength);
        Assert.Equal(6000, exif.PixelWidth);
        Assert.Equal(4000, exif.PixelHeight);

        GpsPosition position = exif.GpsPosition!;
        Assert.NotNull(position);
        Assert.Equal(51 + (10 / 60.0) + (41.81 / 3600.0), position.Latitude, 12);
        Assert.Equal(-((7 / 60.0) + (39.3 / 3600.0)), position.Longitude, 12);
        Assert.Equal(-123.5, position.Altitude);
        Assert.Equal(GpsAltitudeReference.BelowEllipsoid, position.AltitudeReference);
    }

    [Fact]
    public void ThePropertiesAgreeWithTheInterpretedTextOfTheSameBlock()
    {
        // The interpreted text is what parity measures against the reference; a property reads the same value.
        foreach (Endianness order in Enum.GetValues<Endianness>())
        {
            ExifMetadata exif = ImageMetadata.Read(Container("jpeg", Tiff(order, Picture(order)))).Exif!;
            Assert.Equal("1/250 s", ExifTags.Interpret(exif, new ExifTagKey(Photo, 0x829A)));
            Assert.Equal(exif.ExposureTime + " s", ExifTags.Interpret(exif, new ExifTagKey(Photo, 0x829A)));
            Assert.Equal("F2.8", ExifTags.Interpret(exif, new ExifTagKey(Photo, 0x829D)));
            Assert.Equal("50.0 mm", ExifTags.Interpret(exif, new ExifTagKey(Photo, 0x920A)));
            Assert.Equal("right, top", ExifTags.Interpret(exif, new ExifTagKey(Ifd0, 0x0112)));
            Assert.Equal("51 deg 10' 41.81\" N", ExifTags.Interpret(exif, new ExifTagKey(Gps, 0x0002)));
            Assert.Equal("0 deg 7' 39.30\" W", ExifTags.Interpret(exif, new ExifTagKey(Gps, 0x0004)));
            Assert.Equal("123.5 m", ExifTags.Interpret(exif, new ExifTagKey(Gps, 0x0006)));
        }
    }

    [Fact]
    public void ABlockWithoutTheTagsAnswersNullEverywhere()
    {
        foreach (ExifMetadata exif in new[]
        {
            Block(),
            ImageMetadata.Read(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(Tiff(II, []))))).Exif!,
        })
        {
            Assert.Null(exif.Make);
            Assert.Null(exif.Model);
            Assert.Null(exif.LensModel);
            Assert.Null(exif.DateTimeOriginal);
            Assert.Null(exif.DateTimeOriginalWithOffset);
            Assert.Null(exif.Orientation);
            Assert.Null(exif.ExposureTime);
            Assert.Null(exif.FNumber);
            Assert.Null(exif.IsoSpeed);
            Assert.Null(exif.FocalLength);
            Assert.Null(exif.GpsPosition);
            Assert.Null(exif.PixelWidth);
            Assert.Null(exif.PixelHeight);
        }
    }

    [Fact]
    public void EveryPropertyAnswersNullForAValueOfAnotherFieldType()
    {
        // Each tag holds a well-formed value of a type its property does not read.
        ExifValue text = Ascii("6", II), number = Short(II, 6), signed = SignedRational(II, 1, 250);
        ExifMetadata exif = Block(
            (Ifd0, 0x010F, number), (Ifd0, 0x0110, number), (Photo, 0xA434, number),
            (Photo, 0x9003, number), (Photo, 0x9011, number),
            (Ifd0, 0x0112, text), (Photo, 0x829A, signed), (Photo, 0x829D, signed), (Photo, 0x920A, signed),
            (Photo, 0x8827, text), (Photo, 0x8833, text), (Photo, 0x8832, signed), (Photo, 0x8831, text),
            (Photo, 0xA002, text), (Photo, 0xA003, signed), (Ifd0, 0x0100, text), (Ifd0, 0x0101, text),
            (Gps, 0x0001, number), (Gps, 0x0002, signed), (Gps, 0x0003, number), (Gps, 0x0004, signed));
        Assert.Null(exif.Make);
        Assert.Null(exif.Model);
        Assert.Null(exif.LensModel);
        Assert.Null(exif.DateTimeOriginal);
        Assert.Null(exif.DateTimeOriginalWithOffset);
        Assert.Null(exif.Orientation);
        Assert.Null(exif.ExposureTime);
        Assert.Null(exif.FNumber);
        Assert.Null(exif.IsoSpeed);
        Assert.Null(exif.FocalLength);
        Assert.Null(exif.GpsPosition);
        Assert.Null(exif.PixelWidth);
        Assert.Null(exif.PixelHeight);
    }

    // ---- Text -----------------------------------------------------------------------------------------

    [Fact]
    public void TextPropertiesReturnTheStringAsStored()
    {
        ExifMetadata exif = Block(
            (Ifd0, 0x010F, ExifValue.FromRaw(TiffFieldType.Ascii, 8, II, "NIKON\0\0\0"u8)),
            (Ifd0, 0x0110, ExifValue.FromRaw(TiffFieldType.Ascii, 6, II, "E990  "u8)),          // no terminator, padded
            (Photo, 0xA434, ExifValue.FromRaw(TiffFieldType.Ascii, 0, II, [])));
        Assert.Equal("NIKON", exif.Make);
        Assert.Equal("E990  ", exif.Model);
        Assert.Equal(string.Empty, exif.LensModel);
    }

    // ---- Dates ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("2024:02:29 13:14:15", 2024, 2, 29, 13, 14, 15)]
    [InlineData("0001:01:01 00:00:00", 1, 1, 1, 0, 0, 0)]
    [InlineData("9999:12:31 23:59:59", 9999, 12, 31, 23, 59, 59)]
    public void ADateIsReadFromTheStandardForm(string stored, int year, int month, int day, int hour, int minute, int second)
    {
        DateTime? read = Block((Photo, 0x9003, Ascii(stored, II))).DateTimeOriginal;
        Assert.Equal(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified), read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    :  :     :  :  ")]            // the standard's form for an unknown date
    [InlineData("0000:00:00 00:00:00")]
    [InlineData("2023:02:29 13:14:15")]            // not a leap year
    [InlineData("2024:13:01 00:00:00")]
    [InlineData("2024:02:29 24:00:00")]
    [InlineData("2024:02:29 13:14:60")]
    [InlineData("2024-02-29 13:14:15")]
    [InlineData("2024:02:29T13:14:15")]
    [InlineData("2024:02:29 13:14")]
    [InlineData("2024:02:29 13:14:15 ")]
    [InlineData(" 2024:02:29 13:14:15")]
    [InlineData("2024:02:29 13:14:15.50")]
    [InlineData("2024:2:29 13:14:15")]
    [InlineData("yesterday")]
    public void TextThatIsNotADateIsNull(string stored)
    {
        ExifMetadata exif = Block((Photo, 0x9003, Ascii(stored, II)), (Photo, 0x9011, Ascii("+01:00", II)));
        Assert.Null(exif.DateTimeOriginal);
        Assert.Null(exif.DateTimeOriginalWithOffset);
    }

    [Theory]
    [InlineData("+00:00", 0)]
    [InlineData("-00:00", 0)]
    [InlineData("+05:30", 330)]
    [InlineData("-08:00", -480)]
    [InlineData("+14:00", 840)]
    [InlineData("-14:00", -840)]
    [InlineData("+12:45", 765)]
    public void AnOffsetGivesTheDateItsPlaceInTime(string stored, int minutes)
    {
        ExifMetadata exif = Block((Photo, 0x9003, Ascii("2024:02:29 13:14:15", II)), (Photo, 0x9011, Ascii(stored, II)));
        DateTimeOffset? read = exif.DateTimeOriginalWithOffset;
        Assert.Equal(new DateTimeOffset(2024, 2, 29, 13, 14, 15, TimeSpan.FromMinutes(minutes)), read);
        Assert.Equal(TimeSpan.FromMinutes(minutes), read!.Value.Offset);
        Assert.Equal(exif.DateTimeOriginal, read.Value.DateTime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   :  ")]                 // the standard's form for an unknown offset
    [InlineData("+14:01")]
    [InlineData("-15:00")]
    [InlineData("+99:99")]
    [InlineData("+05:60")]
    [InlineData("+5:30")]
    [InlineData("05:30")]
    [InlineData("+0530")]
    [InlineData("+05:30:00")]
    [InlineData("+05.30")]
    [InlineData("Z")]
    [InlineData("+ 5:30")]
    [InlineData("+05:3x")]
    public void AnOffsetThatCannotBeReadLeavesOnlyTheLocalDate(string stored)
    {
        ExifMetadata exif = Block((Photo, 0x9003, Ascii("2024:02:29 13:14:15", II)), (Photo, 0x9011, Ascii(stored, II)));
        Assert.Equal(new DateTime(2024, 2, 29, 13, 14, 15), exif.DateTimeOriginal);
        Assert.Null(exif.DateTimeOriginalWithOffset);
    }

    [Fact]
    public void AnOffsetNeedsADateAndHasToLeaveItRepresentable()
    {
        Assert.Null(Block((Photo, 0x9011, Ascii("+01:00", II))).DateTimeOriginalWithOffset);
        Assert.Null(Block((Photo, 0x9003, Ascii("2024:02:29 13:14:15", II))).DateTimeOriginalWithOffset);
        // The first and the last second a date can hold, pushed past the range by the offset.
        Assert.Null(Block((Photo, 0x9003, Ascii("0001:01:01 00:00:00", II)), (Photo, 0x9011, Ascii("+01:00", II))).DateTimeOriginalWithOffset);
        Assert.Null(Block((Photo, 0x9003, Ascii("9999:12:31 23:59:59", II)), (Photo, 0x9011, Ascii("-01:00", II))).DateTimeOriginalWithOffset);
        Assert.NotNull(Block((Photo, 0x9003, Ascii("0001:01:01 00:00:00", II)), (Photo, 0x9011, Ascii("-01:00", II))).DateTimeOriginalWithOffset);
        Assert.NotNull(Block((Photo, 0x9003, Ascii("9999:12:31 23:59:59", II)), (Photo, 0x9011, Ascii("+01:00", II))).DateTimeOriginalWithOffset);
    }

    // ---- Orientation ----------------------------------------------------------------------------------

    [Fact]
    public void OrientationNamesTheEightValuesOfTheStandardAndNoOther()
    {
        ExifOrientation[] names =
        [
            ExifOrientation.TopLeft, ExifOrientation.TopRight, ExifOrientation.BottomRight, ExifOrientation.BottomLeft,
            ExifOrientation.LeftTop, ExifOrientation.RightTop, ExifOrientation.RightBottom, ExifOrientation.LeftBottom,
        ];
        Assert.Equal(names, Enum.GetValues<ExifOrientation>());
        for (uint number = 1; number <= 8; number++)
        {
            Assert.Equal(names[number - 1], Block((Ifd0, 0x0112, Short(II, number))).Orientation);
            Assert.Equal((int)number, (int)names[number - 1]);
            // The enum names the edges in the order the interpreted text gives them.
            string label = ExifTags.Interpret(new ExifTagKey(Ifd0, 0x0112), Short(II, number))!;
            Assert.Equal(label.Replace(", ", string.Empty), names[number - 1].ToString().ToLowerInvariant());
        }
        foreach (uint number in new uint[] { 0, 9, 255, 65535 })
            Assert.Null(Block((Ifd0, 0x0112, Short(II, number))).Orientation);
    }

    [Fact]
    public void OrientationReadsAnyUnsignedWidthAndOnlyOneNumber()
    {
        Assert.Equal(ExifOrientation.RightTop, Block((Ifd0, 0x0112, Long(II, 6))).Orientation);
        Assert.Equal(ExifOrientation.RightTop, Block((Ifd0, 0x0112, Integers(TiffFieldType.Byte, II, 6))).Orientation);
        Assert.Equal(ExifOrientation.BottomRight, Block(Endianness.BigEndian, (Ifd0, 0x0112, Short(Endianness.BigEndian, 3))).Orientation);
        Assert.Null(Block((Ifd0, 0x0112, Integers(TiffFieldType.SShort, II, 6))).Orientation);
        Assert.Null(Block((Ifd0, 0x0112, Short(II, 6, 6))).Orientation);
        Assert.Null(Block((Ifd0, 0x0112, Short(II))).Orientation);
        Assert.Null(Block((Ifd0, 0x0112, Long(II, 0x00010006))).Orientation);
        // The tag of the thumbnail directory is another tag.
        Assert.Null(Block((ExifDirectoryKind.Thumbnail, 0x0112, Short(II, 6))).Orientation);
    }

    // ---- Exposure -------------------------------------------------------------------------------------

    [Fact]
    public void ExposureTimeIsTheStoredFraction()
    {
        Assert.Equal(new UnsignedRational(1, 250), Block((Photo, 0x829A, Rational(II, 1, 250))).ExposureTime);
        Assert.Equal(new UnsignedRational(10, 2500), Block((Photo, 0x829A, Rational(II, 10, 2500))).ExposureTime);
        Assert.Equal(new UnsignedRational(30, 1), Block((Photo, 0x829A, Rational(II, 30, 1))).ExposureTime);
        Assert.Equal(new UnsignedRational(0, 1), Block((Photo, 0x829A, Rational(II, 0, 1))).ExposureTime);
        Assert.Equal("1/250", Block((Photo, 0x829A, Rational(II, 1, 250))).ExposureTime.ToString());
        Assert.Null(Block((Photo, 0x829A, Rational(II, 1, 0))).ExposureTime);
        Assert.Null(Block((Photo, 0x829A, Rational(II, 0, 0))).ExposureTime);
        Assert.Null(Block((Photo, 0x829A, Rational(II, 1, 250, 1, 125))).ExposureTime);
        Assert.Null(Block((Photo, 0x829A, Rational(II))).ExposureTime);
        Assert.Null(Block((Photo, 0x829A, Long(II, 250))).ExposureTime);
    }

    [Fact]
    public void FNumberAndFocalLengthAreDividedOut()
    {
        ExifMetadata exif = Block((Photo, 0x829D, Rational(II, 28, 10)), (Photo, 0x920A, Rational(II, 1050, 10)));
        Assert.Equal(2.8, exif.FNumber);
        Assert.Equal(105.0, exif.FocalLength);
        exif = Block((Photo, 0x829D, Rational(II, 0, 1)), (Photo, 0x920A, Rational(II, uint.MaxValue, 1)));
        Assert.Equal(0.0, exif.FNumber);
        Assert.Equal(4294967295.0, exif.FocalLength);
        foreach (ExifValue unreadable in new[] { Rational(II, 28, 0), Rational(II, 0, 0), Rational(II, 28, 10, 28, 10), Rational(II), Short(II, 3) })
        {
            exif = Block((Photo, 0x829D, unreadable), (Photo, 0x920A, unreadable));
            Assert.Null(exif.FNumber);
            Assert.Null(exif.FocalLength);
        }
    }

    [Fact]
    public void IsoSpeedIsTheFirstSensitivityAndFallsBackToTheWideTags()
    {
        Assert.Equal(400, Block((Photo, 0x8827, Short(II, 400))).IsoSpeed);
        Assert.Equal(200, Block((Photo, 0x8827, Short(II, 200, 400))).IsoSpeed);
        Assert.Equal(0, Block((Photo, 0x8827, Short(II, 0))).IsoSpeed);
        Assert.Equal(400, Block((Photo, 0x8827, Long(II, 400))).IsoSpeed);
        // The wide tags are not asked while the sensitivity says something.
        Assert.Equal(400, Block((Photo, 0x8827, Short(II, 400)), (Photo, 0x8833, Long(II, 102400))).IsoSpeed);

        // 65535 is the standard's "too large for this tag": ISOSpeed, then the index, then the output sensitivity.
        Assert.Equal(102400, Block((Photo, 0x8827, Short(II, 65535)), (Photo, 0x8833, Long(II, 102400)),
            (Photo, 0x8832, Long(II, 2)), (Photo, 0x8831, Long(II, 3))).IsoSpeed);
        Assert.Equal(204800, Block((Photo, 0x8827, Short(II, 65535)), (Photo, 0x8832, Long(II, 204800)),
            (Photo, 0x8831, Long(II, 3))).IsoSpeed);
        Assert.Equal(409600, Block((Photo, 0x8827, Short(II, 65535)), (Photo, 0x8831, Long(II, 409600))).IsoSpeed);
        Assert.Equal(65535, Block((Photo, 0x8827, Short(II, 65535))).IsoSpeed);
        Assert.Equal(65535, Block((Photo, 0x8827, Short(II, 65535, 100))).IsoSpeed);

        // The same order when the sensitivity is absent or holds nothing.
        Assert.Equal(800, Block((Photo, 0x8833, Long(II, 800)), (Photo, 0x8832, Long(II, 2))).IsoSpeed);
        Assert.Equal(1600, Block((Photo, 0x8832, Long(II, 1600))).IsoSpeed);
        Assert.Equal(3200, Block((Photo, 0x8827, Short(II)), (Photo, 0x8831, Long(II, 3200))).IsoSpeed);
        // A wide tag that cannot be read is passed over.
        Assert.Equal(1600, Block((Photo, 0x8833, Long(II, 1, 2)), (Photo, 0x8832, Long(II, 1600))).IsoSpeed);

        Assert.Null(Block((Photo, 0x8827, Short(II))).IsoSpeed);
        Assert.Null(Block((Photo, 0x8833, Long(II, 0x80000000))).IsoSpeed);
        Assert.Equal(int.MaxValue, Block((Photo, 0x8833, Long(II, int.MaxValue))).IsoSpeed);
    }

    // ---- Size -----------------------------------------------------------------------------------------

    [Fact]
    public void PixelSizeComesFromTheExifDirectoryAndOtherwiseFromThePrimaryImage()
    {
        ExifMetadata exif = Block((Photo, 0xA002, Short(II, 640)), (Photo, 0xA003, Long(II, 480)),
            (Ifd0, 0x0100, Long(II, 160)), (Ifd0, 0x0101, Long(II, 120)));
        Assert.Equal(640, exif.PixelWidth);
        Assert.Equal(480, exif.PixelHeight);

        exif = Block((Ifd0, 0x0100, Short(II, 160)), (Ifd0, 0x0101, Long(II, 120)));
        Assert.Equal(160, exif.PixelWidth);
        Assert.Equal(120, exif.PixelHeight);

        // A zero in the Exif directory says nothing, so the primary image is asked.
        exif = Block((Photo, 0xA002, Long(II, 0)), (Photo, 0xA003, Long(II, 0)), (Ifd0, 0x0100, Long(II, 160)), (Ifd0, 0x0101, Long(II, 120)));
        Assert.Equal(160, exif.PixelWidth);
        Assert.Equal(120, exif.PixelHeight);

        // Each side is read on its own.
        exif = Block((Photo, 0xA002, Long(II, 640)), (Ifd0, 0x0101, Long(II, 120)));
        Assert.Equal(640, exif.PixelWidth);
        Assert.Equal(120, exif.PixelHeight);
    }

    [Fact]
    public void APrimaryDirectoryMarkedAsAReducedCopyDoesNotGiveTheSize()
    {
        // A raw file whose first directory is the preview: its size is not the size of the picture.
        ExifMetadata exif = Block((Ifd0, 0x00FE, Long(II, 1)), (Ifd0, 0x0100, Long(II, 160)), (Ifd0, 0x0101, Long(II, 120)));
        Assert.Null(exif.PixelWidth);
        Assert.Null(exif.PixelHeight);
        // The Exif directory still answers.
        exif = Block((Ifd0, 0x00FE, Long(II, 1)), (Ifd0, 0x0100, Long(II, 160)), (Photo, 0xA002, Long(II, 6000)));
        Assert.Equal(6000, exif.PixelWidth);
        Assert.Null(exif.PixelHeight);
        // Bit 0 is the mark; a page of a document (bit 1) or a full image (0) is the image itself.
        foreach (uint kind in new uint[] { 0, 2, 4 })
            Assert.Equal(160, Block((Ifd0, 0x00FE, Long(II, kind)), (Ifd0, 0x0100, Long(II, 160))).PixelWidth);
        Assert.Null(Block((Ifd0, 0x00FE, Long(II, 3)), (Ifd0, 0x0100, Long(II, 160))).PixelWidth);
    }

    [Fact]
    public void ASizeThatIsNotOnePositiveNumberIsNull()
    {
        foreach (ExifValue unreadable in new[] { Long(II, 0), Long(II, 0x80000000), Long(II, 640, 480), Long(II), Ascii("640", II), Rational(II, 640, 1) })
        {
            ExifMetadata exif = Block((Photo, 0xA002, unreadable), (Photo, 0xA003, unreadable), (Ifd0, 0x0100, unreadable), (Ifd0, 0x0101, unreadable));
            Assert.Null(exif.PixelWidth);
            Assert.Null(exif.PixelHeight);
        }
        Assert.Equal(int.MaxValue, Block((Photo, 0xA002, Long(II, int.MaxValue))).PixelWidth);
    }

    // ---- GPS ------------------------------------------------------------------------------------------

    private static ExifMetadata Place(string latitudeRef, uint[] latitude, string longitudeRef, uint[] longitude,
        params (ExifDirectoryKind Directory, ushort Tag, ExifValue Value)[] more) =>
        Block([
            (Gps, 0x0001, Ascii(latitudeRef, II)), (Gps, 0x0002, Rational(II, latitude)),
            (Gps, 0x0003, Ascii(longitudeRef, II)), (Gps, 0x0004, Rational(II, longitude)), .. more]);

    private static readonly uint[] Lat = [51, 1, 30, 1, 0, 1];      // 51.5
    private static readonly uint[] Lon = [0, 1, 7, 1, 30, 1];       // 0.125

    [Theory]
    [InlineData("N", "E", 51.5, 0.125)]
    [InlineData("N", "W", 51.5, -0.125)]
    [InlineData("S", "E", -51.5, 0.125)]
    [InlineData("S", "W", -51.5, -0.125)]
    public void TheReferenceLettersGiveTheSigns(string latitudeRef, string longitudeRef, double latitude, double longitude)
    {
        GpsPosition? position = Place(latitudeRef, Lat, longitudeRef, Lon).GpsPosition;
        Assert.Equal(new GpsPosition(latitude, longitude, null, null), position);
    }

    [Fact]
    public void ACoordinateIsTheSumOfItsThreeParts()
    {
        // Seconds folded into decimal minutes, which the standard allows: 51 degrees 30.5 minutes.
        Assert.Equal(51 + (30.5 / 60), Place("N", [51, 1, 3050, 100, 0, 1], "E", Lon).GpsPosition!.Latitude, 12);
        // Decimal degrees alone.
        Assert.Equal(51.123456, Place("N", [51123456, 1000000, 0, 1, 0, 1], "E", Lon).GpsPosition!.Latitude, 12);
        // Degrees, minutes, fractional seconds.
        Assert.Equal(-(122 + (25 / 60.0) + (9.84 / 3600)), Place("N", Lat, "W", [122, 1, 25, 1, 984, 100]).GpsPosition!.Longitude, 12);
        // The edges of the map are on it.
        Assert.Equal(new GpsPosition(90, 180, null, null), Place("N", [90, 1, 0, 1, 0, 1], "E", [180, 1, 0, 1, 0, 1]).GpsPosition);
        Assert.Equal(new GpsPosition(-90, -180, null, null), Place("S", [90, 1, 0, 1, 0, 1], "W", [180, 1, 0, 1, 0, 1]).GpsPosition);
        Assert.Equal(new GpsPosition(0, 0, null, null), Place("N", [0, 1, 0, 1, 0, 1], "E", [0, 1, 0, 1, 0, 1]).GpsPosition);
    }

    [Fact]
    public void ACoordinateIsTheDoubleNearestToWhatItsRationalsState()
    {
        static double Longitude(params uint[] parts) => Place("N", Lat, "E", parts).GpsPosition!.Longitude;

        // 1 degree 49.5984 minutes is 1.82664 exactly; the three quotients added as doubles give 1.8266399999999998.
        Assert.Equal(1.82664, Longitude(1, 1, 495984, 10000, 0, 1));
        Assert.Equal(-1.82664, Place("N", Lat, "W", [1, 1, 495984, 10000, 0, 1]).GpsPosition!.Longitude);
        Assert.Equal(18424181 / 360000.0, Longitude(51, 1, 10, 1, 4181, 100));
        // Far below one degree, where the leading bits come late: a third of a second, and the smallest parts there are.
        Assert.Equal(1 / 10800.0, Longitude(0, 1, 0, 1, 1, 3));
        Assert.Equal(1 / 15461882262000.0, Longitude(0, 1, 0, 1, 1, 4294967295));
        Assert.Equal(3661 / 15461882262000.0, Longitude(1, 4294967295, 1, 4294967295, 1, 4294967295));
        // Parts whose common denominator has more than a hundred bits.
        Assert.Equal(1.016944445562549, Longitude(4294967295, 4294967291, 4294967279, 4294967231, 4294967197, 4294967189));
    }

    [Fact]
    public void APositionNeedsBothCoordinatesAndBothLetters()
    {
        Assert.NotNull(Place("N", Lat, "E", Lon).GpsPosition);
        Assert.Null(Block((Gps, 0x0002, Rational(II, Lat)), (Gps, 0x0003, Ascii("E", II)), (Gps, 0x0004, Rational(II, Lon))).GpsPosition);
        Assert.Null(Block((Gps, 0x0001, Ascii("N", II)), (Gps, 0x0003, Ascii("E", II)), (Gps, 0x0004, Rational(II, Lon))).GpsPosition);
        Assert.Null(Block((Gps, 0x0001, Ascii("N", II)), (Gps, 0x0002, Rational(II, Lat)), (Gps, 0x0004, Rational(II, Lon))).GpsPosition);
        Assert.Null(Block((Gps, 0x0001, Ascii("N", II)), (Gps, 0x0002, Rational(II, Lat)), (Gps, 0x0003, Ascii("E", II))).GpsPosition);
        // The destination tags are another position.
        Assert.Null(Block((Gps, 0x0013, Ascii("N", II)), (Gps, 0x0014, Rational(II, Lat)), (Gps, 0x0015, Ascii("E", II)), (Gps, 0x0016, Rational(II, Lon))).GpsPosition);
    }

    [Theory]
    [InlineData("")]
    [InlineData("n")]
    [InlineData("E")]             // a letter of the other axis
    [InlineData("X")]
    [InlineData("NS")]
    [InlineData("North")]
    [InlineData(" N")]
    public void ALetterTheTagDoesNotAllowLeavesNoPosition(string letter)
    {
        Assert.Null(Place(letter, Lat, "E", Lon).GpsPosition);
        Assert.Null(Place("N", Lat, letter == "E" ? "N" : letter, Lon).GpsPosition);
    }

    [Fact]
    public void ACoordinateThatCannotBeReadLeavesNoPosition()
    {
        uint[][] unreadable =
        [
            [51, 0, 30, 1, 0, 1],            // a zero denominator in each part
            [51, 1, 30, 0, 0, 1],
            [51, 1, 30, 1, 0, 0],
            [0, 0, 0, 0, 0, 0],
            [51, 1, 30, 1],                  // two parts
            [51, 1],
            [],
            [51, 1, 30, 1, 0, 1, 0, 1],      // four parts
            [181, 1, 0, 1, 0, 1],            // off the map on either axis
            [180, 1, 0, 1, 1, 100],
            [uint.MaxValue, 1, 0, 1, 0, 1],
        ];
        foreach (uint[] parts in unreadable)
        {
            Assert.Null(Place("N", parts, "E", Lon).GpsPosition);
            Assert.Null(Place("N", Lat, "E", parts).GpsPosition);
        }
        // Past the pole, but still a longitude.
        Assert.Null(Place("N", [90, 1, 0, 1, 1, 100], "E", Lon).GpsPosition);
        Assert.NotNull(Place("N", Lat, "E", [90, 1, 0, 1, 1, 100]).GpsPosition);
        // Signed rationals are not what the tag is defined with.
        Assert.Null(Block((Gps, 0x0001, Ascii("N", II)), (Gps, 0x0002, SignedRational(II, Lat)), (Gps, 0x0003, Ascii("E", II)), (Gps, 0x0004, Rational(II, Lon))).GpsPosition);
    }

    [Theory]
    [InlineData(0, 123.5, GpsAltitudeReference.AboveEllipsoid)]
    [InlineData(1, -123.5, GpsAltitudeReference.BelowEllipsoid)]
    [InlineData(2, 123.5, GpsAltitudeReference.AboveSeaLevel)]
    [InlineData(3, -123.5, GpsAltitudeReference.BelowSeaLevel)]
    public void TheAltitudeTakesItsSignFromItsReference(int stored, double altitude, GpsAltitudeReference reference)
    {
        GpsPosition? position = Place("N", Lat, "E", Lon,
            (Gps, 0x0005, Integers(TiffFieldType.Byte, II, (uint)stored)), (Gps, 0x0006, Rational(II, 1235, 10))).GpsPosition;
        Assert.Equal(new GpsPosition(51.5, 0.125, altitude, reference), position);
        Assert.Equal(stored, (int)reference);
    }

    [Fact]
    public void AnAltitudeWithoutAReadableReferenceIsReturnedAsStored()
    {
        var expected = new GpsPosition(51.5, 0.125, 123.5, null);
        Assert.Equal(expected, Place("N", Lat, "E", Lon, (Gps, 0x0006, Rational(II, 1235, 10))).GpsPosition);
        foreach (ExifValue reference in new[] { Integers(TiffFieldType.Byte, II, 4), Integers(TiffFieldType.Byte, II, 255), Integers(TiffFieldType.Byte, II, 1, 1), Ascii("1", II) })
            Assert.Equal(expected, Place("N", Lat, "E", Lon, (Gps, 0x0005, reference), (Gps, 0x0006, Rational(II, 1235, 10))).GpsPosition);
    }

    [Fact]
    public void AnAltitudeThatCannotBeReadLeavesThePositionWithoutOne()
    {
        var expected = new GpsPosition(51.5, 0.125, null, null);
        foreach (ExifValue altitude in new[] { Rational(II, 1235, 0), Rational(II, 1, 1, 2, 1), Rational(II), Long(II, 123), SignedRational(II, 1235, 10) })
            Assert.Equal(expected, Place("N", Lat, "E", Lon, (Gps, 0x0005, Integers(TiffFieldType.Byte, II, 1)), (Gps, 0x0006, altitude)).GpsPosition);
        // A reference without an altitude says nothing.
        Assert.Equal(expected, Place("N", Lat, "E", Lon, (Gps, 0x0005, Integers(TiffFieldType.Byte, II, 1))).GpsPosition);
        // An altitude without a position is not a position.
        Assert.Null(Block((Gps, 0x0005, Integers(TiffFieldType.Byte, II, 0)), (Gps, 0x0006, Rational(II, 1235, 10))).GpsPosition);
    }

    [Fact]
    public void APositionIsAValue()
    {
        var position = new GpsPosition(51.5, -0.125, 12, GpsAltitudeReference.AboveSeaLevel);
        Assert.Equal(position, new GpsPosition(51.5, -0.125, 12, GpsAltitudeReference.AboveSeaLevel));
        Assert.NotEqual(position, position with { Altitude = null });
        Assert.Equal(51.5, position.Latitude);
        Assert.Equal(-0.125, position.Longitude);
        Assert.Equal(12.0, position.Altitude);
        Assert.Equal(GpsAltitudeReference.AboveSeaLevel, position.AltitudeReference);
        // Each read of the property builds its own record; two reads are equal.
        ExifMetadata exif = Place("N", Lat, "E", Lon);
        Assert.Equal(exif.GpsPosition, exif.GpsPosition);
    }

    [Fact]
    public void ThePropertiesReadEitherByteOrder()
    {
        foreach (Endianness order in Enum.GetValues<Endianness>())
        {
            ExifMetadata exif = Block(order, Picture(order));
            Assert.Equal(ExifOrientation.RightTop, exif.Orientation);
            Assert.Equal(new UnsignedRational(1, 250), exif.ExposureTime);
            Assert.Equal(400, exif.IsoSpeed);
            Assert.Equal(6000, exif.PixelWidth);
            Assert.Equal(4000, exif.PixelHeight);
            Assert.Equal(-123.5, exif.GpsPosition!.Altitude);
        }
    }
}

internal static class ExifValueTestExtensions
{
    /// <summary>The raw bytes of a value, in a new array.</summary>
    internal static byte[] Raw(this ExifValue value)
    {
        var raw = new byte[value.ByteLength];
        value.CopyRawTo(raw);
        return raw;
    }
}
