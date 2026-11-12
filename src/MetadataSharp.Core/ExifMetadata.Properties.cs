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
    public DateTime? DateTimeOriginal =>
        Text(ExifDirectoryKind.Exif, 0x9003) is { } text &&
        DateTime.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime moment)
            ? moment
            : null;

    /// <summary>
    /// <see cref="DateTimeOriginal" /> with the offset from UTC that the OffsetTimeOriginal tag (Exif,
    /// 0x9011) gives as <c>+HH:MM</c> or <c>-HH:MM</c>. Null when either tag is absent or cannot be
    /// read, or when the offset is beyond the fourteen hours a <see cref="DateTimeOffset" /> holds.
    /// </summary>
    public DateTimeOffset? DateTimeOriginalWithOffset
    {
        get
        {
            if (DateTimeOriginal is not DateTime moment || Text(ExifDirectoryKind.Exif, 0x9011) is not { Length: 6 } text)
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
    }

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
    /// because a position with a guessed hemisphere is a different place.
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

    private double? Coordinate(ushort tag, ushort referenceTag, char positive, char negative, double limit)
    {
        if (!TryGetEntry(new ExifTagKey(ExifDirectoryKind.Gps, tag), out ExifEntry? entry) ||
            !entry.Value.TryGetRationalArray(out UnsignedRational[]? parts) || parts.Length != 3)
            return null;
        if (Text(ExifDirectoryKind.Gps, referenceTag) is not { Length: 1 } letter ||
            (letter[0] != positive && letter[0] != negative))
            return null;
        double degrees = 0, scale = 1;
        foreach (UnsignedRational part in parts)
        {
            if (part.Denominator == 0)
                return null;
            degrees += part.ToDouble() / scale;
            scale *= 60;
        }
        if (!(degrees <= limit))
            return null;
        return letter[0] == negative ? -degrees : degrees;
    }

    private static bool TwoDigits(string text, int at, out int number)
    {
        number = 0;
        if (!char.IsAsciiDigit(text[at]) || !char.IsAsciiDigit(text[at + 1]))
            return false;
        number = ((text[at] - '0') * 10) + (text[at + 1] - '0');
        return true;
    }
}
