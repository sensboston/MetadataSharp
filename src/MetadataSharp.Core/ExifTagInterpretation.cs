using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MetadataSharp.Core;

// The human-readable form of a standard EXIF value. The raw value stays the truth: an interpretation is
// derived from it for display, is never read back, and is absent (null) wherever the publication gives a
// value no meaning beyond its digits.
//
// Arithmetic is done on the stored rational with System.Decimal and rounded half to even, so a result
// does not depend on the platform's floating-point formatting. The two APEX conversions need a power of
// two and go through System.Double; they are rounded to two significant digits or to a whole number, far
// above the precision at which platforms could differ.
public static partial class ExifTags
{
    /// <summary>
    /// The human-readable form of one value of a standard EXIF tag, or null when the tag has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An enumerated tag answers with the wording of its <see cref="ExifTagDefinition.ValueMeanings" />.
    /// A rational answers as a decimal with the unit the standard gives it: ExposureTime as
    /// <c>1/250 s</c>, FNumber as <c>F2.8</c>, FocalLength as <c>50.0 mm</c>, ExposureBiasValue as
    /// <c>+1/3 EV</c>, and ApertureValue, MaxApertureValue and ShutterSpeedValue converted from APEX to
    /// an F-number and an exposure time. A date answers as written. A GPS coordinate answers as degrees,
    /// minutes and seconds, and its reference letter as the direction it names. Flash answers as a
    /// description of its bit fields, ExifVersion and FlashpixVersion as dotted versions, and UserComment
    /// as the text behind its character-code prefix.
    /// </para>
    /// <para>
    /// Null means there is nothing to add to the raw value: the tag is outside the vocabulary, the tag
    /// is free text or a plain number, the number is not one the standard assigns a meaning to, or the
    /// value does not have the type and count the interpretation needs. Nothing is ever thrown for the
    /// content of a value.
    /// </para>
    /// </remarks>
    /// <param name="key">The directory and tag the value was read from.</param>
    /// <param name="value">The raw value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is null.</exception>
    public static string? Interpret(ExifTagKey key, ExifValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Find(key) is not ExifTagDefinition definition)
            return null;
        if (!definition.ValueMeanings.IsEmpty)
            return InterpretEnumerated(key, definition, value);
        return key.Directory switch
        {
            ExifDirectoryKind.Ifd0 or ExifDirectoryKind.Thumbnail => InterpretImage(key.TagId, value),
            ExifDirectoryKind.Exif => InterpretPhoto(key.TagId, value),
            ExifDirectoryKind.Gps => InterpretGps(key.TagId, value),
            _ => null,
        };
    }

    /// <summary>
    /// The human-readable form of one tag of a metadata snapshot, or null when the snapshot does not
    /// carry the tag or the tag has no interpretation.
    /// </summary>
    /// <remarks>
    /// This is <see cref="Interpret(ExifTagKey, ExifValue)" /> with the one piece of context a single
    /// value cannot supply: a GPS latitude or longitude is followed by the letter of its reference tag
    /// (<c>51 deg 10' 41.81" N</c>) when the snapshot carries a valid one.
    /// </remarks>
    /// <param name="metadata">The snapshot the tag belongs to.</param>
    /// <param name="key">The directory and tag to interpret.</param>
    /// <exception cref="ArgumentNullException"><paramref name="metadata" /> is null.</exception>
    public static string? Interpret(ExifMetadata metadata, ExifTagKey key)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!metadata.TryGetEntry(key, out ExifEntry? entry))
            return null;
        string? text = Interpret(key, entry.Value);
        if (text is null || key.Directory != ExifDirectoryKind.Gps)
            return text;

        int referenceTag;
        string letters;
        switch (key.TagId)
        {
            case 0x0002: referenceTag = 0x0001; letters = "NS"; break;
            case 0x0004: referenceTag = 0x0003; letters = "EW"; break;
            case 0x0014: referenceTag = 0x0013; letters = "NS"; break;
            case 0x0016: referenceTag = 0x0015; letters = "EW"; break;
            default: return text;
        }

        var referenceKey = new ExifTagKey(ExifDirectoryKind.Gps, (ushort)referenceTag);
        if (!metadata.TryGetEntry(referenceKey, out ExifEntry? reference))
            return text;
        string? letter = AsciiText(reference.Value);
        if (letter is null || letter.Length != 1 || !letters.Contains(letter[0]))
            return text;
        return text + " " + letter;
    }

    // ---- Enumerated tags -------------------------------------------------------------------------------

    private static string? InterpretEnumerated(ExifTagKey key, ExifTagDefinition definition, ExifValue value)
    {
        // ComponentsConfiguration is the one enumerated tag with four values: the channel in each of
        // four positions, where 0 says the position is unused. "1 2 3 0" therefore reads YCbCr.
        if (key.Directory == ExifDirectoryKind.Exif && key.TagId == 0x9101)
        {
            if (value.Count != 4)
                return null;
            var channels = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                if (!TryInteger(value, i, out long channel))
                    return null;
                if (channel == 0)
                    continue;
                string? name = Meaning(definition, channel);
                if (name is null)
                    return null;
                channels.Append(name);
            }
            return channels.Length == 0 ? null : channels.ToString();
        }

        if (value.Count != 1 || !TryInteger(value, 0, out long number))
            return null;
        // The vocabulary's ColorSpace table stops at sRGB; the standard's other assigned value is
        // FFFF.H, which it calls Uncalibrated.
        if (key.Directory == ExifDirectoryKind.Exif && key.TagId == 0xA001 && number == 0xFFFF)
            return "Uncalibrated";
        return Meaning(definition, number);
    }

    private static string? Meaning(ExifTagDefinition definition, long number)
    {
        foreach (ExifTagValueMeaning meaning in definition.ValueMeanings)
        {
            if (meaning.Number == number)
                return meaning.Meaning;
        }
        return null;
    }

    // ---- 0th IFD and thumbnail -------------------------------------------------------------------------

    private static string? InterpretImage(ushort tag, ExifValue value) => tag switch
    {
        0x011A or 0x011B or 0x011E or 0x011F => PlainDecimal(value),    // X/YResolution, X/YPosition
        0x0132 => AsciiText(value),                                      // DateTime
        _ => null,
    };

    // ---- Exif IFD --------------------------------------------------------------------------------------

    private static string? InterpretPhoto(ushort tag, ExifValue value) => tag switch
    {
        0x829A => ExposureTime(value),
        0x829D => FNumber(value),
        0x9000 or 0xA000 => DottedVersion(value),                        // ExifVersion, FlashpixVersion
        0x9003 or 0x9004 => AsciiText(value),                            // DateTimeOriginal, DateTimeDigitized
        0x9102 => PlainDecimal(value),                                   // CompressedBitsPerPixel
        0x9201 => ShutterSpeed(value),
        0x9202 or 0x9205 => Aperture(value),                             // ApertureValue, MaxApertureValue
        0x9203 => PlainDecimal(value),                                   // BrightnessValue, an APEX value
        0x9204 => ExposureBias(value),
        0x9206 => SubjectDistance(value),
        0x9209 => Flash(value),
        0x920A => FocalLength(value),
        0x9286 => UserComment(value),
        0x9400 => Measured(value, "\u00B0C"),                            // Temperature
        0x9401 => Measured(value, "%"),                                  // Humidity
        0x9402 => Measured(value, "hPa"),                                // Pressure
        0x9403 => Measured(value, "m"),                                  // WaterDepth
        0x9404 => Measured(value, "mGal"),                               // Acceleration
        0x9405 => Measured(value, "deg"),                                // CameraElevationAngle
        0xA20B or 0xA20E or 0xA20F or 0xA215 or 0xA500 => PlainDecimal(value),
        0xA210 => FocalPlaneResolutionUnit(value),
        0xA404 => DigitalZoomRatio(value),
        0xA405 => FocalLengthIn35mmFilm(value),
        0xA432 => LensSpecification(value),
        _ => null,
    };

    private static string? ExposureTime(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || numerator <= 0 || denominator <= 0)
            return null;
        // A time that is one over a whole number is written that way, which is how shutter speeds are
        // named; anything else is the number of seconds.
        if (denominator % numerator == 0)
        {
            long reciprocal = denominator / numerator;
            return reciprocal == 1 ? "1 s" : "1/" + Whole(reciprocal) + " s";
        }
        return Significant((decimal)numerator / denominator, 6) + " s";
    }

    private static string? FNumber(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || numerator <= 0 || denominator <= 0)
            return null;
        return "F" + Significant((decimal)numerator / denominator, 2);
    }

    /// <summary>APEX aperture value to F-number: F = sqrt(2) to the power Av.</summary>
    private static string? Aperture(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || denominator == 0)
            return null;
        double apex = (double)numerator / denominator;
        if (apex is < -8.0 or > 40.0)
            return null;
        return "F" + Significant((decimal)Math.Pow(2.0, apex / 2.0), 2);
    }

    /// <summary>APEX shutter speed value to exposure time: t = 1 / 2 to the power Tv.</summary>
    private static string? ShutterSpeed(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || denominator == 0)
            return null;
        double apex = (double)numerator / denominator;
        if (apex is < -31.0 or > 31.0)
            return null;
        double perSecond = Math.Pow(2.0, apex);
        if (perSecond > 1.0)
        {
            long reciprocal = (long)Math.Round(perSecond, MidpointRounding.AwayFromZero);
            return reciprocal <= 1 ? "1 s" : "1/" + Whole(reciprocal) + " s";
        }
        return Whole((long)Math.Round(1.0 / perSecond, MidpointRounding.AwayFromZero)) + " s";
    }

    private static string? ExposureBias(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || denominator == 0)
            return null;
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }
        if (numerator == 0)
            return "0 EV";
        // Bias is set in thirds or halves of a stop, so the fraction in lowest terms is the readable form.
        long magnitude = Math.Abs(numerator);
        long divisor = GreatestCommonDivisor(magnitude, denominator);
        magnitude /= divisor;
        denominator /= divisor;
        string amount = denominator == 1 ? Whole(magnitude) : Whole(magnitude) + "/" + Whole(denominator);
        return (numerator < 0 ? "-" : "+") + amount + " EV";
    }

    private static string? SubjectDistance(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || numerator < 0)
            return null;
        // The standard reserves two numerators: 0 for an unknown distance and FFFFFFFF.H for infinity.
        if (numerator == 0)
            return "Unknown";
        if (numerator == uint.MaxValue)
            return "Infinity";
        if (denominator <= 0)
            return null;
        return Fixed((decimal)numerator / denominator, 2) + " m";
    }

    private static string? FocalLength(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || numerator < 0 || denominator <= 0)
            return null;
        return Fixed((decimal)numerator / denominator, 1) + " mm";
    }

    /// <summary>"The same as ResolutionUnit", so that tag's meanings are the ones to answer with.</summary>
    private static string? FocalPlaneResolutionUnit(ExifValue value)
    {
        if (value.Count != 1 || !TryInteger(value, 0, out long unit))
            return null;
        return Find(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0128)) is ExifTagDefinition resolutionUnit
            ? Meaning(resolutionUnit, unit)
            : null;
    }

    private static string? FocalLengthIn35mmFilm(ExifValue value)
    {
        if (value.Count != 1 || !TryInteger(value, 0, out long length) || length < 0)
            return null;
        return length == 0 ? "Unknown" : Whole(length) + ".0 mm";
    }

    private static string? DigitalZoomRatio(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || numerator < 0)
            return null;
        // "If the numerator of the recorded value is 0, this indicates that digital zoom was not used."
        if (numerator == 0)
            return "Digital zoom not used";
        if (denominator <= 0)
            return null;
        return Fixed((decimal)numerator / denominator, 1);
    }

    /// <summary>Shortest and longest focal length, then the widest aperture at each; 0 is unknown.</summary>
    private static string? LensSpecification(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Rational || value.Count != 4)
            return null;
        var parts = new decimal[4];
        var known = new bool[4];
        for (int i = 0; i < 4; i++)
        {
            if (!TryRational(value, i, out long numerator, out long denominator))
                return null;
            if (numerator == 0)
                continue;
            if (denominator == 0)
                return null;
            parts[i] = (decimal)numerator / denominator;
            known[i] = true;
        }

        if (!known[0] && !known[1] && !known[2] && !known[3])
            return "n/a";
        if (!known[0] || !known[1] || parts[0] > parts[1])
            return null;
        string text = parts[0] == parts[1]
            ? Significant(parts[0], 5) + "mm"
            : Significant(parts[0], 5) + "-" + Significant(parts[1], 5) + "mm";
        if (known[2] && known[3])
        {
            if (parts[2] > parts[3])
                return null;
            text += parts[2] == parts[3]
                ? " F" + Significant(parts[2], 2)
                : " F" + Significant(parts[2], 2) + "-" + Significant(parts[3], 2);
        }
        return text;
    }

    /// <summary>
    /// The environmental tags Exif 2.31 added: a decimal with its unit, or Unknown where the
    /// denominator is FFFFFFFF.H, which is how the standard says the value was not measured.
    /// </summary>
    private static string? Measured(ExifValue value, string unit)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator))
            return null;
        if (denominator == uint.MaxValue || denominator == -1)
            return "Unknown";
        if (denominator == 0)
            return null;
        return Significant((decimal)numerator / denominator, 6) + " " + unit;
    }

    /// <summary>Four ASCII digits, two for the major version and two for the minor: "0230" is 2.30.</summary>
    private static string? DottedVersion(ExifValue value)
    {
        if (value.Count != 4 || value.FieldType is not (TiffFieldType.Undefined or TiffFieldType.Ascii or TiffFieldType.Byte))
            return null;
        ReadOnlySpan<byte> digits = value.RawBytes;
        foreach (byte digit in digits)
        {
            if (digit < (byte)'0' || digit > (byte)'9')
                return null;
        }
        int major = ((digits[0] - '0') * 10) + (digits[1] - '0');
        return Whole(major) + "." + (char)digits[2] + (char)digits[3];
    }

    /// <summary>
    /// The Flash bit fields, each named as the standard names it: whether the flash fired, the flash
    /// mode, whether the camera has a flash at all, red-eye reduction, and the returned light.
    /// </summary>
    private static string? Flash(ExifValue value)
    {
        if (value.Count != 1 || !TryInteger(value, 0, out long bits) || bits < 0 || bits > 0x7F)
            return null;
        long returned = (bits >> 1) & 3;
        if (returned == 1)
            return null;    // reserved
        var text = new StringBuilder((bits & 1) != 0 ? "Flash fired" : "Flash did not fire");
        switch ((bits >> 3) & 3)
        {
            case 1: text.Append(", compulsory flash firing"); break;
            case 2: text.Append(", compulsory flash suppression"); break;
            case 3: text.Append(", auto mode"); break;
        }
        if ((bits & 0x20) != 0)
            text.Append(", no flash function");
        if ((bits & 0x40) != 0)
            text.Append(", red-eye reduction mode");
        if (returned == 2)
            text.Append(", return light not detected");
        else if (returned == 3)
            text.Append(", return light detected");
        return text.ToString();
    }

    /// <summary>
    /// The comment behind its eight-byte character code. ASCII and Unicode are decoded; an undefined
    /// code is decoded only when the bytes are valid UTF-8 text; JIS and anything unrecognised is left
    /// to the caller.
    /// </summary>
    private static string? UserComment(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Undefined || value.Count < 8)
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        ReadOnlySpan<byte> code = bytes.Slice(0, 8);
        ReadOnlySpan<byte> body = bytes.Slice(8);
        string? text;
        if (code.SequenceEqual("ASCII\0\0\0"u8))
            text = DecodeAscii(body);
        else if (code.SequenceEqual("UNICODE\0"u8))
            text = DecodeUtf16(body, value.ByteOrder);
        else if (code.SequenceEqual("\0\0\0\0\0\0\0\0"u8))
            text = DecodeUtf8(body);
        else
            return null;
        // The standard pads the unused part of the comment area with spaces, and writers also use NUL.
        return text?.TrimEnd(' ', '\0');
    }

    private static string? DecodeAscii(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);
        if (length >= 0)
            bytes = bytes.Slice(0, length);
        foreach (byte b in bytes)
        {
            if (b > 0x7F)
                return null;
        }
        return Encoding.ASCII.GetString(bytes);
    }

    private static string? DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);
        if (length >= 0)
            bytes = bytes.Slice(0, length);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string? DecodeUtf16(ReadOnlySpan<byte> bytes, Endianness byteOrder)
    {
        bool bigEndian = byteOrder == Endianness.BigEndian;
        if (bytes.Length >= 2)
        {
            // A byte order mark overrides the byte order of the file, as it does for any UTF-16 text.
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                bigEndian = true;
                bytes = bytes.Slice(2);
            }
            else if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                bigEndian = false;
                bytes = bytes.Slice(2);
            }
        }
        if ((bytes.Length & 1) != 0)
            bytes = bytes.Slice(0, bytes.Length - 1);
        try
        {
            return new UnicodeEncoding(bigEndian, false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    // ---- GPS IFD ---------------------------------------------------------------------------------------

    private static string? InterpretGps(ushort tag, ExifValue value) => tag switch
    {
        0x0000 => GpsVersion(value),
        0x0001 or 0x0013 => Letter(value, 'N', "North", 'S', "South"),
        0x0003 or 0x0015 => Letter(value, 'E', "East", 'W', "West"),
        0x0002 or 0x0004 or 0x0014 or 0x0016 => Coordinate(value),
        0x0006 => GpsAltitude(value),
        0x0007 => GpsTime(value),
        0x0009 => Letter(value, 'A', "Measurement in progress", 'V', "Measurement interrupted"),
        0x000A => Letter(value, '2', "2-dimensional measurement", '3', "3-dimensional measurement"),
        0x000C => Letter(value, 'K', "km/h", 'M', "mph", 'N', "knots"),
        0x000E or 0x0010 or 0x0017 => Letter(value, 'T', "True direction", 'M', "Magnetic direction"),
        0x0019 => Letter(value, 'K', "km", 'M', "miles", 'N', "nautical miles"),
        0x000B or 0x000D or 0x000F or 0x0011 or 0x0018 or 0x001A or 0x001F => PlainDecimal(value),
        0x001D => AsciiText(value),                                      // GPSDateStamp
        _ => null,
    };

    private static string? GpsVersion(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Byte || value.Count != 4)
            return null;
        ReadOnlySpan<byte> parts = value.RawBytes;
        return Whole(parts[0]) + "." + Whole(parts[1]) + "." + Whole(parts[2]) + "." + Whole(parts[3]);
    }

    private static string? Letter(ExifValue value, char first, string firstMeaning, char second, string secondMeaning,
        char third = '\0', string? thirdMeaning = null)
    {
        string? text = AsciiText(value);
        if (text is null || text.Length != 1)
            return null;
        if (text[0] == first)
            return firstMeaning;
        if (text[0] == second)
            return secondMeaning;
        return thirdMeaning is not null && text[0] == third ? thirdMeaning : null;
    }

    /// <summary>
    /// Degrees, minutes and seconds. The standard allows the seconds to be folded into decimal minutes
    /// (dd/1, mmmm/100, 0/1), so the three parts are summed and split again; seconds are whole when the
    /// value was stored in whole parts and carry two decimals otherwise.
    /// </summary>
    private static string? Coordinate(ExifValue value)
    {
        if (value.Count != 3)
            return null;
        // Each part is scaled to seconds before it is divided, so that a third of a degree is exactly
        // twenty minutes and not a rounding step short of it.
        decimal total = 0m;
        decimal scale = 3600m;
        bool whole = true;
        for (int i = 0; i < 3; i++)
        {
            if (!TryRational(value, i, out long numerator, out long denominator) || numerator < 0 || denominator <= 0)
                return null;
            total += (numerator * scale) / denominator;
            whole &= i == 2 ? denominator == 1 : numerator % denominator == 0;
            scale /= 60m;
        }

        int places = whole ? 0 : 2;
        total = Math.Round(total, places, MidpointRounding.ToEven);
        decimal degrees = decimal.Truncate(total / 3600m);
        decimal rest = total - (degrees * 3600m);
        decimal minutes = decimal.Truncate(rest / 60m);
        decimal seconds = rest - (minutes * 60m);
        return Fixed(degrees, 0) + " deg " + Fixed(minutes, 0) + "' " + Fixed(seconds, places) + "\"";
    }

    private static string? GpsAltitude(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || numerator < 0 || denominator <= 0)
            return null;
        // A whole number of metres is printed as one; a finer denominator earns one decimal.
        return Fixed((decimal)numerator / denominator, denominator > 1 ? 1 : 0) + " m";
    }

    /// <summary>UTC as hours, minutes and seconds, with tenths when the stored time has a fraction.</summary>
    private static string? GpsTime(ExifValue value)
    {
        if (value.Count != 3)
            return null;
        decimal total = 0m;
        decimal scale = 3600m;
        for (int i = 0; i < 3; i++)
        {
            if (!TryRational(value, i, out long numerator, out long denominator) || numerator < 0 || denominator <= 0)
                return null;
            // Scaled before it is divided, for the same reason as a coordinate.
            total += (numerator * scale) / denominator;
            scale /= 60m;
        }

        int places = decimal.Truncate(total) == total ? 0 : 1;
        total = Math.Round(total, places, MidpointRounding.ToEven);
        if (total >= 86400m)
            return null;
        decimal hours = decimal.Truncate(total / 3600m);
        decimal rest = total - (hours * 3600m);
        decimal minutes = decimal.Truncate(rest / 60m);
        decimal seconds = rest - (minutes * 60m);
        return hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
            minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
            seconds.ToString(places == 0 ? "00" : "00.0", CultureInfo.InvariantCulture);
    }

    // ---- Shared pieces ---------------------------------------------------------------------------------

    /// <summary>One rational as a decimal of at most six significant digits.</summary>
    private static string? PlainDecimal(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || denominator == 0)
            return null;
        return Significant((decimal)numerator / denominator, 6);
    }

    /// <summary>ASCII text as written, up to its terminator; null for anything that is not ASCII typed.</summary>
    private static string? AsciiText(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Ascii || value.Count == 0)
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        int length = bytes.IndexOf((byte)0);
        if (length >= 0)
            bytes = bytes.Slice(0, length);
        return Encoding.Latin1.GetString(bytes);
    }

    private static bool TrySingleRational(ExifValue value, out long numerator, out long denominator)
    {
        numerator = 0;
        denominator = 0;
        return value.Count == 1 && TryRational(value, 0, out numerator, out denominator);
    }

    private static bool TryRational(ExifValue value, int index, out long numerator, out long denominator)
    {
        numerator = 0;
        denominator = 0;
        if (index < 0 || (uint)index >= value.Count)
            return false;
        bool little = value.ByteOrder == Endianness.LittleEndian;
        if (value.FieldType == TiffFieldType.Rational)
        {
            ReadOnlySpan<byte> pair = value.RawBytes.Slice(index * 8, 8);
            numerator = little ? BinaryPrimitives.ReadUInt32LittleEndian(pair) : BinaryPrimitives.ReadUInt32BigEndian(pair);
            denominator = little
                ? BinaryPrimitives.ReadUInt32LittleEndian(pair.Slice(4))
                : BinaryPrimitives.ReadUInt32BigEndian(pair.Slice(4));
            return true;
        }
        if (value.FieldType == TiffFieldType.SRational)
        {
            ReadOnlySpan<byte> pair = value.RawBytes.Slice(index * 8, 8);
            numerator = little ? BinaryPrimitives.ReadInt32LittleEndian(pair) : BinaryPrimitives.ReadInt32BigEndian(pair);
            denominator = little
                ? BinaryPrimitives.ReadInt32LittleEndian(pair.Slice(4))
                : BinaryPrimitives.ReadInt32BigEndian(pair.Slice(4));
            return true;
        }
        return false;
    }

    private static bool TryInteger(ExifValue value, int index, out long number)
    {
        number = 0;
        if (index < 0 || (uint)index >= value.Count)
            return false;
        bool little = value.ByteOrder == Endianness.LittleEndian;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        switch (value.FieldType)
        {
            case TiffFieldType.Byte:
            case TiffFieldType.Undefined:
                number = bytes[index];
                return true;
            case TiffFieldType.SByte:
                number = unchecked((sbyte)bytes[index]);
                return true;
            case TiffFieldType.Short:
                number = little
                    ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(index * 2, 2))
                    : BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index * 2, 2));
                return true;
            case TiffFieldType.SShort:
                number = little
                    ? BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(index * 2, 2))
                    : BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(index * 2, 2));
                return true;
            case TiffFieldType.Long:
                number = little
                    ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(index * 4, 4))
                    : BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(index * 4, 4));
                return true;
            case TiffFieldType.SLong:
                number = little
                    ? BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(index * 4, 4))
                    : BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(index * 4, 4));
                return true;
            default:
                return false;
        }
    }

    private static string Whole(long number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>A fixed number of decimals, rounded half to even, never a negative zero.</summary>
    private static string Fixed(decimal value, int places)
    {
        decimal rounded = Math.Round(value, places, MidpointRounding.ToEven);
        if (rounded == decimal.Zero)
            rounded = decimal.Zero;
        string format = places switch { 0 => "0", 1 => "0.0", _ => "0.00" };
        return rounded.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// At most <paramref name="digits" /> significant digits, rounded half to even, without an exponent
    /// and without trailing zeros: 2.8, 11, 0.0896435, 3810.58.
    /// </summary>
    private static string Significant(decimal value, int digits)
    {
        if (value == decimal.Zero)
            return "0";
        decimal magnitude = Math.Abs(value);
        int exponent = 0;
        while (magnitude >= 10m)
        {
            magnitude /= 10m;
            exponent++;
        }
        while (magnitude < 1m)
        {
            magnitude *= 10m;
            exponent--;
        }

        int places = digits - 1 - exponent;
        decimal rounded;
        if (places >= 0)
        {
            rounded = Math.Round(value, Math.Min(places, 28), MidpointRounding.ToEven);
        }
        else
        {
            decimal unit = 1m;
            for (int i = 0; i < -places; i++)
                unit *= 10m;
            rounded = Math.Round(value / unit, 0, MidpointRounding.ToEven) * unit;
        }
        return rounded.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0)
        {
            long next = a % b;
            a = b;
            b = next;
        }
        return a == 0 ? 1 : a;
    }
}
