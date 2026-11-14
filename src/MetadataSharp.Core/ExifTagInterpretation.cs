using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MetadataSharp.Core;

// The human-readable form of a standard EXIF value. The raw value stays the truth: an interpretation is
// derived from it for display, is never read back, and is absent (null) wherever the publication gives a
// value no meaning beyond its digits.
//
// A rational becomes a number by single-precision division, which is how metadata tools have always
// computed it, so that a value halfway between two printed digits falls to the same side here as it does
// for them. The digits are then taken from the exact binary value with integer arithmetic, an exact tie
// going to the even digit, so that no platform's floating-point formatting has a say. Coordinates and
// times are sums of three rationals and are added exactly, with System.Decimal.
public static partial class ExifTags
{
    /// <summary>
    /// The human-readable form of one value of a standard EXIF tag, or null when the tag has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An enumerated tag answers with a short label for the standard's meaning, in the form metadata
    /// tools conventionally print: <c>top, left</c>, <c>inch</c>, <c>Auto</c>. The publication's full
    /// wording stays in <see cref="ExifTagDefinition.ValueMeanings" />.
    /// A rational answers as a decimal with the unit the standard gives it: ExposureTime as
    /// <c>1/250 s</c>, FNumber as <c>F2.8</c>, FocalLength as <c>50.0 mm</c>, ExposureBiasValue as
    /// <c>+1/3 EV</c>, and ApertureValue, MaxApertureValue and ShutterSpeedValue converted from APEX to
    /// an F-number and an exposure time. A date answers as written. A GPS coordinate answers as degrees,
    /// minutes and seconds, and its reference letter as the direction it names. Flash answers as a
    /// short description of its bit fields (<c>Yes, auto, return light detected</c>), ExifVersion and
    /// FlashpixVersion as dotted versions, and UserComment as the text behind its character-code prefix.
    /// </para>
    /// <para>
    /// A value the tag has an interpretation for but that cannot be read as one answers with the stored
    /// value in parentheses: <c>(9)</c> for a number an enumerated tag does not assign, <c>(1/0)</c> for
    /// a rational that cannot be divided.
    /// </para>
    /// <para>
    /// Null means there is nothing to add to the raw value: the tag is outside the vocabulary, the tag
    /// is free text or a plain number, or the value does not have the type and count the interpretation
    /// needs. Nothing is ever thrown for the content of a value.
    /// </para>
    /// </remarks>
    /// <param name="key">The directory and tag the value was read from.</param>
    /// <param name="value">The raw value.</param>
    /// <returns>The display form of the value, or <see langword="null"/> when this tag has no interpretation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is null.</exception>
    public static string? Interpret(ExifTagKey key, ExifValue value) => Interpret(key, value, null);

    /// <inheritdoc cref="Interpret(ExifTagKey, ExifValue)"/>
    /// <param name="key">The directory and tag the value was read from.</param>
    /// <param name="value">The raw value.</param>
    /// <param name="options">Display-time charset choices; null uses the file's own byte-order and charset markers.</param>
    public static string? Interpret(ExifTagKey key, ExifValue value, ExifInterpretationOptions? options)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Find(key) is not ExifTagDefinition definition)
            return null;
        if (!definition.ValueMeanings.IsEmpty)
            return InterpretEnumerated(key, definition, value);
        return key.Directory switch
        {
            ExifDirectoryKind.Ifd0 or ExifDirectoryKind.Thumbnail => InterpretImage(key, value),
            ExifDirectoryKind.Exif => InterpretPhoto(key.TagId, value, options),
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
    /// <returns>The display form of the tag, or <see langword="null"/> when it is absent or has no interpretation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metadata" /> is null.</exception>
    public static string? Interpret(ExifMetadata metadata, ExifTagKey key) => Interpret(metadata, key, null);

    /// <inheritdoc cref="Interpret(ExifMetadata, ExifTagKey)"/>
    /// <param name="metadata">The snapshot the tag belongs to.</param>
    /// <param name="key">The directory and tag to interpret.</param>
    /// <param name="options">Display-time charset choices; null uses the file's own byte-order and charset markers.</param>
    public static string? Interpret(ExifMetadata metadata, ExifTagKey key, ExifInterpretationOptions? options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!metadata.TryGetEntry(key, out ExifEntry? entry))
            return null;
        string? text = Interpret(key, entry.Value, options);
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

    // Decode enumerated values only when their count and encoded padding fit the tag-specific representation.
    private static string? InterpretEnumerated(ExifTagKey key, ExifTagDefinition definition, ExifValue value)
    {
        // ComponentsConfiguration is the one enumerated tag with a value per position: the channel in
        // each of up to four positions, where 0 says the position is unused. "1 2 3 0" reads YCbCr.
        if (key.Directory == ExifDirectoryKind.Exif && key.TagId == 0x9101)
        {
            if (value.Count == 0 || value.Count > 4)
                return null;
            var channels = new StringBuilder();
            for (int i = 0; (uint)i < value.Count; i++)
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

        if (!TryEnumerated(value, out long number))
            return null;
        // The vocabulary's ColorSpace table stops at sRGB; the standard's other assigned value is
        // FFFF.H, which it calls Uncalibrated.
        if (key.Directory == ExifDirectoryKind.Exif && key.TagId == 0xA001 && number == 0xFFFF)
            return "Uncalibrated";
        return Label(key, number) ?? Meaning(definition, number) ?? Unassigned(number);
    }

    /// <summary>
    /// A tag whose numbers have labels here although the vocabulary lists no meanings for it: the TIFF
    /// baseline tags the Exif publication only names.
    /// </summary>
    private static string? Labelled(ExifTagKey labels, ExifValue value)
    {
        if (value.Count != 1 || !TryInteger(value, 0, out long number))
            return null;
        return Label(labels, number) ?? Unassigned(number);
    }

    /// <summary>One format per sample; the samples of an image share theirs, and that is the one to name.</summary>
    private static string? SampleFormat(ExifTagKey key, ExifValue value)
    {
        if (value.Count == 0 || !TryInteger(value, 0, out long format))
            return null;
        for (int i = 1; (uint)i < value.Count; i++)
        {
            if (!TryInteger(value, i, out long other) || other != format)
                return null;
        }
        return Label(key, format) ?? Unassigned(format);
    }

    /// <summary>
    /// The one number of an enumerated value. A one-byte value that a writer padded to a longer field
    /// still has one number: the first byte, when only zeros follow it.
    /// </summary>
    private static bool TryEnumerated(ExifValue value, out long number)
    {
        number = 0;
        if (value.Count == 0 || !TryInteger(value, 0, out number))
            return false;
        if (value.Count == 1)
            return true;
        if (value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined))
            return false;
        foreach (byte padding in value.RawBytes.Slice(1))
        {
            if (padding != 0)
                return false;
        }
        return true;
    }

    /// <summary>A number the tag does not assign, shown as stored so that it is not mistaken for a label.</summary>
    private static string Unassigned(long number) => "(" + Whole(number) + ")";

    // Resolve a numeric value against the published meanings without assigning labels to unknown values.
    private static string? Meaning(ExifTagDefinition definition, long number)
    {
        foreach (ExifTagValueMeaning meaning in definition.ValueMeanings)
        {
            if (meaning.Number == number)
                return meaning.Meaning;
        }
        return null;
    }

    /// <summary>
    /// The short label of an enumerated number, where it is not the vocabulary's wording as it stands.
    /// The vocabulary quotes the publication; a label is that meaning in the compact form metadata tools
    /// print, so that a value reads the same here as it does elsewhere. The TIFF numbers the Exif
    /// publication does not list are named from TIFF 6.0, its technical notes, TIFF/EP and DNG; numbers
    /// a camera maker uses privately are not named.
    /// </summary>
    private static string? Label(ExifTagKey key, long number) => key.Directory switch
    {
        ExifDirectoryKind.Ifd0 or ExifDirectoryKind.Thumbnail => key.TagId switch
        {
            // NewSubfileType
            0x00FE => number switch
            {
                0 => "Primary image",
                1 => "Thumbnail/Preview image",
                2 => "Page of a multi-page image",
                4 => "Transparency mask",
                _ => null,
            },
            // Compression
            0x0103 => number switch
            {
                1 => "Uncompressed",
                2 => "CCITT RLE",
                3 => "T4/Group 3 Fax",
                4 => "T6/Group 4 Fax",
                5 => "LZW",
                6 => "JPEG (old-style)",
                7 => "JPEG",
                8 => "Adobe Deflate",
                32773 => "PackBits (Macintosh RLE)",
                _ => null,
            },
            // PhotometricInterpretation
            0x0106 => number switch
            {
                0 => "White Is Zero",
                1 => "Black Is Zero",
                3 => "Palette color",
                4 => "Transparency mask",
                5 => "CMYK",
                8 => "CIELab",
                32803 => "Color Filter Array",      // TIFF/EP, the photometric of a raw sensor image
                34892 => "Linear Raw",              // DNG
                _ => null,
            },
            // Orientation: where row 0 is, then where column 0 is.
            0x0112 => number switch
            {
                1 => "top, left",
                2 => "top, right",
                3 => "bottom, right",
                4 => "bottom, left",
                5 => "left, top",
                6 => "right, top",
                7 => "right, bottom",
                8 => "left, bottom",
                _ => null,
            },
            // PlanarConfiguration
            0x011C => number switch { 1 => "Chunky", 2 => "Planar", _ => null },
            // ResolutionUnit
            0x0128 => number switch { 1 => "none", 2 => "inch", 3 => "cm", _ => null },
            // Predictor
            0x013D => number switch
            {
                1 => "No prediction scheme used",
                2 => "Horizontal differencing",
                3 => "Floating point horizontal differencing",
                _ => null,
            },
            // SampleFormat
            0x0153 => number switch
            {
                1 => "Unsigned integer data",
                2 => "Two's complement signed integer data",
                3 => "IEEE floating point data",
                4 => "Undefined data format",
                _ => null,
            },
            // YCbCrPositioning
            0x0213 => number switch { 1 => "Centered", 2 => "Co-sited", _ => null },
            _ => null,
        },
        ExifDirectoryKind.Exif => key.TagId switch
        {
            // ExposureProgram: the publication's parenthesised explanations are left to the vocabulary.
            0x8822 => number switch
            {
                5 => "Creative program",
                6 => "Action program",
                7 => "Portrait mode",
                8 => "Landscape mode",
                _ => null,
            },
            // MeteringMode
            0x9207 => number switch { 0 => "Unknown", 2 => "Center weighted average", 255 => "Other", _ => null },
            // LightSource
            0x9208 => number switch { 0 => "Unknown", 255 => "Other light source", _ => null },
            // SensingMethod
            0xA217 => number switch
            {
                2 => "One-chip color area",
                3 => "Two-chip color area",
                4 => "Three-chip color area",
                5 => "Color sequential area",
                7 => "Trilinear",
                8 => "Color sequential linear",
                _ => null,
            },
            // FileSource
            0xA300 => number switch
            {
                0 => "Others",
                1 => "Scanner of transparent type",
                2 => "Scanner of reflex type",
                3 => "Digital still camera",
                _ => null,
            },
            // SceneType
            0xA301 => number switch { 1 => "Directly photographed", _ => null },
            // ExposureMode and WhiteBalance
            0xA402 or 0xA403 => number switch { 0 => "Auto", 1 => "Manual", _ => null },
            // Saturation
            0xA409 => number switch { 1 => "Low", 2 => "High", _ => null },
            // SubjectDistanceRange
            0xA40C => number switch { 0 => "Unknown", _ => null },
            // CompositeImage
            0xA460 => number switch { 0 => "Unknown", 1 => "Non-composite image", _ => null },
            _ => null,
        },
        ExifDirectoryKind.Gps => key.TagId switch
        {
            // GPSDifferential
            0x001E => number switch { 0 => "Without correction", 1 => "Correction applied", _ => null },
            _ => null,
        },
        _ => null,
    };

    // ---- 0th IFD and thumbnail -------------------------------------------------------------------------

    private static string? InterpretImage(ExifTagKey key, ExifValue value) => key.TagId switch
    {
        0x00FE or 0x013D => Labelled(key, value),                        // NewSubfileType, Predictor
        0x011A or 0x011B or 0x011E or 0x011F => PlainDecimal(value),    // X/YResolution, X/YPosition
        0x0132 => AsciiText(value),                                      // DateTime
        0x0153 => SampleFormat(key, value),
        _ => null,
    };

    // ---- Exif IFD --------------------------------------------------------------------------------------

    // Dispatch EXIF Photo IFD tags to their unit-aware conversions; unlisted tags remain uninterpreted.
    private static string? InterpretPhoto(ushort tag, ExifValue value, ExifInterpretationOptions? options) => tag switch
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
        0x9286 => UserComment(value, options?.UserCommentEncoding),
        0x9400 => Measured(value, "\u00B0C"),                            // Temperature
        0x9401 => Measured(value, "%"),                                  // Humidity
        0x9402 => Measured(value, "hPa"),                                // Pressure
        0x9403 => Measured(value, "m"),                                  // WaterDepth
        0x9404 => Measured(value, "mGal"),                               // Acceleration
        0x9405 => Measured(value, "deg"),                                // CameraElevationAngle
        0xA20B or 0xA20E or 0xA20F or 0xA215 or 0xA500 => PlainDecimal(value),
        // FocalPlaneResolutionUnit is "the same as ResolutionUnit", so that tag's labels answer for it.
        0xA210 => Labelled(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0128), value),
        0xA404 => DigitalZoomRatio(value),
        0xA405 => FocalLengthIn35mmFilm(value),
        0xA432 => LensSpecification(value),
        _ => null,
    };

    // Render reciprocal shutter speeds as fractions while preserving other valid times in seconds.
    private static string? ExposureTime(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator))
            return null;
        if (denominator == 0)
            return Undivided(numerator, denominator);
        if (numerator <= 0 || denominator < 0)
            return null;
        // A time that is one over a whole number is written that way, which is how shutter speeds are
        // named; anything else is the number of seconds.
        if (denominator % numerator == 0)
        {
            long reciprocal = denominator / numerator;
            return reciprocal == 1 ? "1 s" : "1/" + Whole(reciprocal) + " s";
        }
        return Significant(Quotient(numerator, denominator), 6) + " s";
    }

    // Format a positive aperture ratio as an F-number; do not invent a value for invalid ratios.
    private static string? FNumber(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator))
            return null;
        if (denominator == 0)
            return Undivided(numerator, denominator);
        if (numerator <= 0 || denominator < 0)
            return null;
        return "F" + Significant(Quotient(numerator, denominator), 2);
    }

    /// <summary>APEX aperture value to F-number: F = sqrt(2) to the power Av.</summary>
    private static string? Aperture(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator))
            return null;
        if (denominator == 0)
            return Undivided(numerator, denominator);
        double apex = (double)numerator / denominator;
        if (apex is < -8.0 or > 40.0)
            return null;
        return "F" + Significant(Math.Pow(2.0, apex / 2.0), 2);
    }

    /// <summary>APEX shutter speed value to exposure time: t = 1 / 2 to the power Tv.</summary>
    private static string? ShutterSpeed(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator))
            return null;
        if (denominator == 0)
            return Undivided(numerator, denominator);
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

    // Normalize the rational sign and reduce the fraction before displaying the exposure compensation.
    private static string? ExposureBias(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator))
            return null;
        if (denominator == 0)
            return Undivided(numerator, denominator);
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

    // Handle the standard unknown and infinity sentinels before converting a distance to metres.
    private static string? SubjectDistance(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || numerator < 0)
            return null;
        // The standard reserves two numerators: 0 for an unknown distance and FFFFFFFF.H for infinity.
        if (numerator == 0)
            return "Unknown";
        if (numerator == uint.MaxValue)
            return "Infinity";
        if (denominator == 0)
            return Undivided(numerator, denominator);
        if (denominator < 0)
            return null;
        return Fixed(Quotient(numerator, denominator), 2) + " m";
    }

    // Format a nonnegative focal-length ratio in millimetres, preserving an undivided zero denominator.
    private static string? FocalLength(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator))
            return null;
        if (denominator == 0)
            return Undivided(numerator, denominator);
        if (numerator < 0 || denominator < 0)
            return null;
        return Fixed(Quotient(numerator, denominator), 1) + " mm";
    }

    // Treat zero as the standard unknown sentinel; positive integer lengths are millimetres.
    private static string? FocalLengthIn35mmFilm(ExifValue value)
    {
        if (value.Count != 1 || !TryInteger(value, 0, out long length) || length < 0)
            return null;
        return length == 0 ? "Unknown" : Whole(length) + ".0 mm";
    }

    // Interpret a zero numerator as no digital zoom and otherwise format the ratio only when valid.
    private static string? DigitalZoomRatio(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator) || numerator < 0)
            return null;
        // "If the numerator of the recorded value is 0, this indicates that digital zoom was not used."
        if (numerator == 0)
            return "Digital zoom not used";
        if (denominator == 0)
            return Undivided(numerator, denominator);
        if (denominator < 0)
            return null;
        return Fixed(Quotient(numerator, denominator), 1);
    }

    /// <summary>Shortest and longest focal length, then the widest aperture at each; 0 is unknown.</summary>
    private static string? LensSpecification(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Rational || value.Count != 4)
            return null;
        var parts = new double[4];
        var known = new bool[4];
        for (int i = 0; i < 4; i++)
        {
            if (!TryRational(value, i, out long numerator, out long denominator))
                return null;
            if (numerator == 0)
                continue;
            if (denominator == 0)
                return null;
            parts[i] = Quotient(numerator, denominator);
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
            return Undivided(numerator, denominator);
        return Significant(Quotient(numerator, denominator), 6) + " " + unit;
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
    /// The Flash bit fields in the short form metadata tools print: whether the flash fired, the flash
    /// mode, red-eye reduction and the returned light. The standard's own list of values calls both
    /// compulsory modes "compulsory flash mode", and so does this.
    /// </summary>
    private static string? Flash(ExifValue value)
    {
        if (value.Count != 1 || !TryInteger(value, 0, out long bits))
            return null;
        if (bits == 0x20)
            return "No flash function";
        long returned = (bits >> 1) & 3;
        // Bit 7 is not assigned, return value 1 is reserved, and a camera without a flash function has
        // nothing else to report.
        if (bits < 0 || bits > 0x7F || returned == 1 || (bits & 0x20) != 0)
            return Unassigned(bits);

        bool fired = (bits & 1) != 0;
        var text = new StringBuilder();
        switch ((bits >> 3) & 3)
        {
            case 0: text.Append(fired ? "Fired" : "No flash"); break;
            case 3: text.Append(fired ? "Yes" : "No").Append(", auto"); break;
            default: text.Append(fired ? "Yes" : "No").Append(", compulsory"); break;
        }
        if ((bits & 0x40) != 0)
            text.Append(", red-eye reduction");
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
    private static string? UserComment(ExifValue value, Encoding? unicodeEncoding)
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
            text = unicodeEncoding is null ? DecodeUtf16(body, value.ByteOrder) : DecodeWith(body, unicodeEncoding);
        else if (code.SequenceEqual("\0\0\0\0\0\0\0\0"u8))
            text = DecodeUtf8(body);
        else
            return null;
        // The standard pads the unused part of the comment area with spaces, and writers also use NUL.
        return text?.TrimEnd(' ', '\0');
    }

    // Stop at the first NUL and reject non-ASCII bytes instead of replacing them during decoding.
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

    // Decode strictly after removing the terminator so malformed UTF-8 produces no interpretation.
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

    // Apply an explicit Exiv2 -n equivalent without replacing bytes that do not decode in that charset.
    private static string? DecodeWith(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        try
        {
            Encoding strict = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return strict.GetString(bytes).TrimEnd(' ', '\0');
        }
        catch (DecoderFallbackException) { return null; }
        catch (ArgumentException) { return null; }
    }

    // Honor an embedded byte-order mark, drop an incomplete trailing code unit, and reject invalid UTF-16.
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

    // Dispatch GPS tags while keeping coordinate references separate from coordinate values.
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

    // Accept only the four-byte GPS version representation.
    private static string? GpsVersion(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Byte || value.Count != 4)
            return null;
        ReadOnlySpan<byte> parts = value.RawBytes;
        return Whole(parts[0]) + "." + Whole(parts[1]) + "." + Whole(parts[2]) + "." + Whole(parts[3]);
    }

    // Translate a one-character ASCII code only when it matches a listed reference value.
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
        return FixedExact(degrees, 0) + " deg " + FixedExact(minutes, 0) + "' " + FixedExact(seconds, places) + "\"";
    }

    // Format absolute altitude magnitude; the separate reference tag supplies above/below sea level.
    private static string? GpsAltitude(ExifValue value)
    {
        if (!TrySingleRational(value, out long numerator, out long denominator))
            return null;
        if (denominator == 0)
            return Undivided(numerator, denominator);
        if (numerator < 0 || denominator < 0)
            return null;
        // A whole number of metres is printed as one; a finer denominator earns one decimal.
        return Fixed(Quotient(numerator, denominator), denominator > 1 ? 1 : 0) + " m";
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
        if (!TrySingleRational(value, out long numerator, out long denominator))
            return null;
        if (denominator == 0)
            return Undivided(numerator, denominator);
        return Significant(Quotient(numerator, denominator), 6);
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

    // Require exactly one rational element before exposing its numerator and denominator.
    private static bool TrySingleRational(ExifValue value, out long numerator, out long denominator)
    {
        numerator = 0;
        denominator = 0;
        return value.Count == 1 && TryRational(value, 0, out numerator, out denominator);
    }

    // Read signed or unsigned rational fields in their declared byte order without normalizing the pair.
    internal static bool TryRational(ExifValue value, int index, out long numerator, out long denominator)
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

    // Decode an integer element only for supported TIFF integer types and an in-range index.
    internal static bool TryInteger(ExifValue value, int index, out long number)
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

    /// <summary>A rational that cannot be divided, shown as stored so that it is not mistaken for a number.</summary>
    private static string Undivided(long numerator, long denominator) =>
        "(" + Whole(numerator) + "/" + Whole(denominator) + ")";

    /// <summary>
    /// A rational as a number: single-precision division, widened without change. The denominator is
    /// never zero here.
    /// </summary>
    private static double Quotient(long numerator, long denominator) =>
        (float)((float)numerator / (float)denominator);

    /// <summary>A fixed number of decimals, rounded from the exact binary value, never a negative zero.</summary>
    private static string Fixed(double value, int places)
    {
        BigInteger scaled = Scaled(Math.Abs(value), places);
        return (value < 0.0 && !scaled.IsZero ? "-" : string.Empty) + Placed(scaled, places, trim: false);
    }

    /// <summary>A fixed number of decimals of an exact sum, rounded half to even, never a negative zero.</summary>
    private static string FixedExact(decimal value, int places)
    {
        decimal rounded = Math.Round(value, places, MidpointRounding.ToEven);
        if (rounded == decimal.Zero)
            rounded = decimal.Zero;
        string format = places switch { 0 => "0", 1 => "0.0", _ => "0.00" };
        return rounded.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// At most <paramref name="digits" /> significant digits, rounded from the exact binary value,
    /// without an exponent and without trailing zeros: 2.8, 11, 0.0896435, 3810.58.
    /// </summary>
    private static string Significant(double value, int digits)
    {
        if (value == 0.0 || !double.IsFinite(value))
            return "0";
        double magnitude = Math.Abs(value);
        BigInteger limit = BigInteger.Pow(10, digits);
        // The logarithm only has to land near the answer: the loop settles the number of decimals
        // exactly, including for a value that rounds up into one more digit (999999.5 is 1000000).
        int places = digits - 1 - (int)Math.Floor(Math.Log10(magnitude));
        BigInteger scaled = Scaled(magnitude, places);
        while (scaled >= limit)
        {
            places--;
            scaled = Scaled(magnitude, places);
        }
        while (scaled * 10 < limit)
        {
            BigInteger finer = Scaled(magnitude, places + 1);
            if (finer >= limit)
                break;
            places++;
            scaled = finer;
        }
        return (value < 0.0 ? "-" : string.Empty) + Placed(scaled, places, trim: true);
    }

    /// <summary>
    /// A finite value that is not negative, times ten to the power <paramref name="places" />, as the
    /// nearest whole number; exactly halfway goes to the even one. A binary floating-point number is a
    /// whole number times a power of two, so the arithmetic is exact.
    /// </summary>
    private static BigInteger Scaled(double value, int places)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7FF);
        long fraction = bits & 0xFFFFFFFFFFFFFL;
        BigInteger numerator = exponent == 0 ? fraction : fraction | (1L << 52);
        int power = exponent == 0 ? -1074 : exponent - 1075;

        BigInteger denominator = BigInteger.One;
        if (power >= 0)
            numerator <<= power;
        else
            denominator <<= -power;
        if (places >= 0)
            numerator *= BigInteger.Pow(10, places);
        else
            denominator *= BigInteger.Pow(10, -places);

        BigInteger whole = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        int half = (remainder * 2).CompareTo(denominator);
        if (half > 0 || (half == 0 && !whole.IsEven))
            whole += BigInteger.One;
        return whole;
    }

    /// <summary>
    /// The digits of <paramref name="scaled" /> with the decimal point <paramref name="places" /> from
    /// the right, optionally without trailing zeros.
    /// </summary>
    private static string Placed(BigInteger scaled, int places, bool trim)
    {
        string digits = scaled.ToString(CultureInfo.InvariantCulture);
        if (places <= 0)
            return scaled.IsZero ? "0" : digits + new string('0', -places);
        if (digits.Length <= places)
            digits = new string('0', places - digits.Length + 1) + digits;
        string whole = digits.Substring(0, digits.Length - places);
        string fraction = digits.Substring(digits.Length - places);
        if (trim)
            fraction = fraction.TrimEnd('0');
        return fraction.Length == 0 ? whole : whole + "." + fraction;
    }

    // Reduce rational display values using a positive divisor, with one as the zero-pair fallback.
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
