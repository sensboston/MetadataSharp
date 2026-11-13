using System.Globalization;

namespace MetadataSharp.Core;

// Typed readings of the tags an application asks for first. Each property is derived from the entries
// on every call and adds nothing to them: it is null when its tag is absent, when the value has a field
// type or a count the ExifValue accessor for it refuses (an integer may be stored in any unsigned
// width), and when the value cannot be read as the property's type. The entry stays available through
// TryGetEntry for a caller that wants to know which.
public sealed partial class ExifMetadata
{
    /// <summary>The camera maker: the Make tag (IFD0, 0x010F) as text. Null without the tag.</summary>
    public string? Make => Text(ExifDirectoryKind.Ifd0, 0x010F);

    /// <summary>The camera model: the Model tag (IFD0, 0x0110) as text. Null without the tag.</summary>
    public string? Model => Text(ExifDirectoryKind.Ifd0, 0x0110);

    /// <summary>The lens model: the LensModel tag (Exif, 0xA434) as text. Null without the tag.</summary>
    public string? LensModel => Text(ExifDirectoryKind.Exif, 0xA434);

    /// <summary>
    /// When the picture was taken, in the camera's local time: the DateTimeOriginal tag (Exif, 0x9003),
    /// whose text is <c>YYYY:MM:DD HH:MM:SS</c>. The kind is <see cref="DateTimeKind.Unspecified" />,
    /// because the tag names no time zone. Null without the tag and for text that is not such a date,
    /// which includes the blank date the standard writes for an unknown one.
    /// </summary>
    public DateTime? DateTimeOriginal => Moment(ExifDirectoryKind.Exif, 0x9003);

    /// <summary>
    /// <see cref="DateTimeOriginal" /> with the offset from UTC that the OffsetTimeOriginal tag (Exif,
    /// 0x9011) gives as <c>+HH:MM</c> or <c>-HH:MM</c>. Null when either tag is absent or cannot be
    /// read, or when the offset is beyond the fourteen hours a <see cref="DateTimeOffset" /> holds.
    /// </summary>
    public DateTimeOffset? DateTimeOriginalWithOffset =>
        DateTimeOriginal is DateTime moment ? WithOffset(moment, 0x9011) : null;

    /// <summary>
    /// The best date the file gives for the picture, in the camera's local time: the first of the
    /// DateTimeOriginal tag (Exif, 0x9003), the DateTimeDigitized tag (Exif, 0x9004) and the DateTime
    /// tag (IFD0, 0x0132) that holds a date. A tag that is absent, or whose text is not
    /// <c>YYYY:MM:DD HH:MM:SS</c> (the blank date of the standard among them), is passed over. The
    /// kind is <see cref="DateTimeKind.Unspecified" />. Null when none of the three holds a date.
    /// </summary>
    /// <remarks>
    /// The later tags are stand-ins: DateTimeDigitized is when the picture was stored as digital data,
    /// which for a camera is the moment it was taken and for a scan is not; DateTime is when the file
    /// was last changed, which an editor may have moved. <see cref="DateTimeOriginal" /> answers for
    /// the first tag alone.
    /// </remarks>
    public DateTime? DateTaken => Taken()?.Moment;

    /// <summary>
    /// <see cref="DateTaken" /> with the offset from UTC of the tag it was read from: OffsetTimeOriginal
    /// (Exif, 0x9011) for DateTimeOriginal, OffsetTimeDigitized (Exif, 0x9012) for DateTimeDigitized,
    /// OffsetTime (Exif, 0x9010) for DateTime. Null when there is no date, when that one offset tag is
    /// absent or is not <c>+HH:MM</c> or <c>-HH:MM</c> within fourteen hours; the offset of another of
    /// the three dates is never borrowed, so a value here is always <see cref="DateTaken" /> itself.
    /// </summary>
    public DateTimeOffset? DateTakenWithOffset =>
        Taken() is (DateTime moment, ushort offsetTag) ? WithOffset(moment, offsetTag) : null;

    /// <summary>
    /// How the stored image is turned: the Orientation tag (IFD0, 0x0112). Null without the tag and
    /// for a number outside 1 to 8.
    /// </summary>
    public ExifOrientation? Orientation =>
        Unsigned(ExifDirectoryKind.Ifd0, 0x0112) is uint number && number is >= 1 and <= 8
            ? (ExifOrientation)number
            : null;

    /// <summary>
    /// The exposure time in seconds, as the fraction the file stores: the ExposureTime tag (Exif,
    /// 0x829A). Null without the tag and for a zero denominator.
    /// </summary>
    public UnsignedRational? ExposureTime =>
        Rational(ExifDirectoryKind.Exif, 0x829A) is { Denominator: not 0 } time ? time : null;

    /// <summary>
    /// The F-number: the FNumber tag (Exif, 0x829D) divided out. Null without the tag and for a zero
    /// denominator.
    /// </summary>
    public double? FNumber => Quotient(ExifDirectoryKind.Exif, 0x829D);

    /// <summary>
    /// The ISO speed: the first number of the PhotographicSensitivity tag (Exif, 0x8827). When that tag
    /// is absent, or holds 65535, which is what the standard writes for a speed too large for it, the
    /// number comes from the first of ISOSpeed (0x8833), RecommendedExposureIndex (0x8832) and
    /// StandardOutputSensitivity (0x8831) that is present. Null when none of them can be read.
    /// </summary>
    public int? IsoSpeed
    {
        get
        {
            uint? sensitivity = null;
            if (TryGetEntry(new ExifTagKey(ExifDirectoryKind.Exif, 0x8827), out ExifEntry? entry) &&
                entry.Value.TryGetUInt32Array(out uint[]? numbers) && numbers.Length != 0)
                sensitivity = numbers[0];
            if (sensitivity is null or 65535)
            {
                foreach (ushort tag in (ReadOnlySpan<ushort>)[0x8833, 0x8832, 0x8831])
                {
                    if (Unsigned(ExifDirectoryKind.Exif, tag) is uint wide)
                    {
                        sensitivity = wide;
                        break;
                    }
                }
            }
            return sensitivity is uint found && found <= int.MaxValue ? (int)found : null;
        }
    }

    /// <summary>
    /// The focal length of the lens in millimetres: the FocalLength tag (Exif, 0x920A) divided out.
    /// Null without the tag and for a zero denominator.
    /// </summary>
    public double? FocalLength => Quotient(ExifDirectoryKind.Exif, 0x920A);

    /// <summary>
    /// The position the GPS directory records, or null when it does not record one that can be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A latitude or a longitude is three rationals, degrees, minutes and seconds, and a reference
    /// letter that gives its sign: <c>S</c> and <c>W</c> are negative. Both coordinates and both
    /// letters are required; a part with a zero denominator, a letter that is not one of the two the
    /// tag allows, a latitude beyond 90 degrees or a longitude beyond 180 make the whole position null,
    /// because a position with a guessed hemisphere is a different place. Each coordinate is the
    /// <see cref="double" /> nearest to degrees + minutes / 60 + seconds / 3600 as the three rationals
    /// state them exactly.
    /// </para>
    /// <para>
    /// The altitude is optional. It is one rational in metres (GPSAltitude, 0x0006), made negative when
    /// GPSAltitudeRef (0x0005) says it lies below its reference; without that tag, or with a number in
    /// it that the standard does not define, the altitude is returned as stored and the reference is
    /// null.
    /// </para>
    /// </remarks>
    public GpsPosition? GpsPosition
    {
        get
        {
            if (Coordinate(0x0002, 0x0001, 'N', 'S', 90) is not double latitude ||
                Coordinate(0x0004, 0x0003, 'E', 'W', 180) is not double longitude)
                return null;
            double? altitude = Quotient(ExifDirectoryKind.Gps, 0x0006);
            GpsAltitudeReference? reference = null;
            if (altitude is not null && Unsigned(ExifDirectoryKind.Gps, 0x0005) is uint number && number <= 3)
            {
                reference = (GpsAltitudeReference)number;
                if (reference is GpsAltitudeReference.BelowEllipsoid or GpsAltitudeReference.BelowSeaLevel)
                    altitude = -altitude;
            }
            return new GpsPosition(latitude, longitude, altitude, reference);
        }
    }

    /// <summary>
    /// The width of the image in pixels: the PixelXDimension tag (Exif, 0xA002), which a compressed
    /// file states its valid width with, otherwise the ImageWidth tag of the primary directory (IFD0,
    /// 0x0100). The primary directory is not asked when its NewSubfileType tag marks it as a reduced
    /// copy, as it does in a raw file whose first directory is a preview. Null when neither applies,
    /// and for zero.
    /// </summary>
    public int? PixelWidth => Dimension(0xA002, 0x0100);

    /// <summary>
    /// The height of the image in pixels: the PixelYDimension tag (Exif, 0xA003), otherwise the
    /// ImageLength tag of the primary directory (IFD0, 0x0101), by the rule of
    /// <see cref="PixelWidth" />.
    /// </summary>
    public int? PixelHeight => Dimension(0xA003, 0x0101);

    // ---- Shared pieces ---------------------------------------------------------------------------------

    private string? Text(ExifDirectoryKind directory, ushort tag) =>
        TryGetEntry(new ExifTagKey(directory, tag), out ExifEntry? entry) && entry.Value.TryGetString(out string? text)
            ? text
            : null;

    private uint? Unsigned(ExifDirectoryKind directory, ushort tag) =>
        TryGetEntry(new ExifTagKey(directory, tag), out ExifEntry? entry) && entry.Value.TryGetUInt32(out uint number)
            ? number
            : null;

    private UnsignedRational? Rational(ExifDirectoryKind directory, ushort tag) =>
        TryGetEntry(new ExifTagKey(directory, tag), out ExifEntry? entry) && entry.Value.TryGetRational(out UnsignedRational pair)
            ? pair
            : null;

    private double? Quotient(ExifDirectoryKind directory, ushort tag) =>
        Rational(directory, tag) is { Denominator: not 0 } pair ? pair.ToDouble() : null;

    // Prefer the EXIF pixel dimension, but do not substitute the full-image size for a reduced-resolution IFD.
    private int? Dimension(ushort exifTag, ushort imageTag)
    {
        uint? size = Unsigned(ExifDirectoryKind.Exif, exifTag);
        if (size is null or 0)
        {
            // Bit 0 of NewSubfileType: a reduced-resolution version of another image in the file.
            if ((Unsigned(ExifDirectoryKind.Ifd0, 0x00FE) ?? 0) % 2 == 1)
                return null;
            size = Unsigned(ExifDirectoryKind.Ifd0, imageTag);
        }
        return size is uint found && found != 0 && found <= int.MaxValue ? (int)found : null;
    }

    // Accept a GPS coordinate only with three valid rationals and a matching reference; keep exact arithmetic until formatting.
    private double? Coordinate(ushort tag, ushort referenceTag, char positive, char negative, uint limit)
    {
        if (!TryGetEntry(new ExifTagKey(ExifDirectoryKind.Gps, tag), out ExifEntry? entry) ||
            !entry.Value.TryGetRationalArray(out UnsignedRational[]? parts) || parts.Length != 3)
            return null;
        if (Text(ExifDirectoryKind.Gps, referenceTag) is not { Length: 1 } letter ||
            (letter[0] != positive && letter[0] != negative))
            return null;
        if (parts[0].Denominator == 0 || parts[1].Denominator == 0 || parts[2].Denominator == 0)
            return null;

        // Degrees + minutes / 60 + seconds / 3600 as one fraction. Three quotients added in floating
        // point land a bit or two away from the value the tag states; the fraction is exact, and its
        // terms stay below 2^110 for any three rationals of 32-bit parts.
        UInt128 degrees = parts[0].Denominator, minutes = parts[1].Denominator, seconds = parts[2].Denominator;
        UInt128 denominator = degrees * minutes * seconds * 3600;
        UInt128 numerator = (parts[0].Numerator * minutes * seconds * 3600) +
                            (parts[1].Numerator * degrees * seconds * 60) +
                            (parts[2].Numerator * degrees * minutes);
        if (numerator > denominator * limit)
            return null;

        // Long division, one binary place at a time, until the quotient holds the 53 bits of a double
        // and two more; what is left over is folded into the last one, so that the conversion rounds
        // once, to the double nearest the fraction. A fraction this small cannot need more than a few
        // hundred places, and the scaling by a power of two is exact.
        (UInt128 whole, UInt128 rest) = UInt128.DivRem(numerator, denominator);
        long quotient = (long)(ulong)whole;
        int places = 0;
        while (rest != 0 && quotient < RoundingBits)
        {
            rest <<= 1;
            quotient <<= 1;
            if (rest >= denominator)
            {
                rest -= denominator;
                quotient |= 1;
            }
            places++;
        }
        if (rest != 0)
            quotient |= 1;
        double value = Math.ScaleB(quotient, -places);
        return letter[0] == negative ? -value : value;
    }

    /// <summary>The smallest quotient that has 55 bits: the 53 of a <see cref="double" />, a guard bit and a sticky bit.</summary>
    private const long RoundingBits = 1L << 54;

    /// <summary>The date of a tag whose text is <c>YYYY:MM:DD HH:MM:SS</c>, or null.</summary>
    private DateTime? Moment(ExifDirectoryKind directory, ushort tag) =>
        Text(directory, tag) is { } text &&
        DateTime.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime moment)
            ? moment
            : null;

    /// <summary>The first of the three dates that reads as one, with the tag of the Exif directory that holds its offset.</summary>
    private (DateTime Moment, ushort OffsetTag)? Taken()
    {
        if (Moment(ExifDirectoryKind.Exif, 0x9003) is DateTime original)
            return (original, 0x9011);
        if (Moment(ExifDirectoryKind.Exif, 0x9004) is DateTime digitized)
            return (digitized, 0x9012);
        if (Moment(ExifDirectoryKind.Ifd0, 0x0132) is DateTime changed)
            return (changed, 0x9010);
        return null;
    }

    /// <summary>A local time with the offset an OffsetTime tag of the Exif directory gives, or null when the tag does not give one.</summary>
    private DateTimeOffset? WithOffset(DateTime moment, ushort offsetTag)
    {
        if (Text(ExifDirectoryKind.Exif, offsetTag) is not { Length: 6 } text)
            return null;
        if (text[0] is not ('+' or '-') || text[3] != ':' ||
            !TwoDigits(text, 1, out int hours) || !TwoDigits(text, 4, out int minutes) || minutes > 59)
            return null;
        var offset = new TimeSpan(hours, minutes, 0);
        if (offset > TimeSpan.FromHours(14))
            return null;
        if (text[0] == '-')
            offset = -offset;
        // The local time less the offset has to be a date DateTimeOffset can hold.
        long utcTicks = moment.Ticks - offset.Ticks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
            return null;
        return new DateTimeOffset(moment, offset);
    }

    // Parse exactly two ASCII digits; callers check the surrounding timestamp syntax and bounds.
    private static bool TwoDigits(string text, int at, out int number)
    {
        number = 0;
        if (!char.IsAsciiDigit(text[at]) || !char.IsAsciiDigit(text[at + 1]))
            return false;
        number = ((text[at] - '0') * 10) + (text[at + 1] - '0');
        return true;
    }
}
