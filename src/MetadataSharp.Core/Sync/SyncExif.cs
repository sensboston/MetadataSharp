using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.Sync;

/// <summary>The EXIF side of a conversion: a tag is read as the texts XMP holds for it, and written from them.</summary>
internal static class SyncExif
{
    private const ExifDirectoryKind ExifDirectory = ExifDirectoryKind.Exif, Gps = ExifDirectoryKind.Gps;
    private const ushort GpsDateStamp = 0x001D;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string Key(ExifTagKey key) => "Exif." + key.Directory + ".0x" + key.TagId.ToString("X4", Invariant);

    /// <summary>The sub-second and the time zone offset tags of a date tag, both in the Exif directory.</summary>
    private static (ushort SubSecond, ushort Offset) Companions(ushort tag) => tag switch
    {
        0x0132 => (0x9290, 0x9010),
        0x9003 => (0x9291, 0x9011),
        _ => (0x9292, 0x9012),
    };

    private static string? Ascii(ExifMetadata exif, ExifDirectoryKind directory, ushort tag) =>
        exif.TryGetEntry(new(directory, tag), out ExifEntry? entry) ? Ascii(entry.Value) : null;

    private static string? Ascii(ExifValue value) =>
        value.TryGetString(out string? text) && (text = text.TrimEnd(' ')).Length != 0 ? text : null;

    /// <summary>The value of a tag as XMP texts; null when the block lacks the tag or its value is not what the tag is defined to hold.</summary>
    internal static string[]? Read(ExifMetadata exif, ExifXmpMapping mapping)
    {
        if (!exif.TryGetEntry(mapping.Tag, out ExifEntry? entry)) return null;
        ExifValue value = entry.Value;
        switch (mapping.Conversion)
        {
            case SyncConversion.Text:
                return Ascii(value) is string single ? new[] { single } : null;
            case SyncConversion.TextList:
            {
                string[]? names = Ascii(value)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return names is { Length: > 0 } ? names : null;
            }
            case SyncConversion.Integer:
            {
                if (value.TryGetInt64Array(out long[]? numbers) && numbers.Length != 0)
                    return Array.ConvertAll(numbers, n => n.ToString(Invariant));
                if (value.FieldType == TiffFieldType.Undefined && value.TryGetByteArray(out byte[]? bytes) && bytes.Length != 0)
                    return Array.ConvertAll(bytes, b => b.ToString(Invariant));
                return null;
            }
            case SyncConversion.Rational:
            {
                if (value.TryGetRationalArray(out UnsignedRational[]? unsigned) && unsigned.Length != 0)
                    return Array.ConvertAll(unsigned, r => r.Numerator.ToString(Invariant) + "/" + r.Denominator.ToString(Invariant));
                if (value.TryGetSignedRationalArray(out SignedRational[]? signed) && signed.Length != 0)
                    return Array.ConvertAll(signed, r => r.Numerator.ToString(Invariant) + "/" + r.Denominator.ToString(Invariant));
                return null;
            }
            case SyncConversion.Date:
            {
                if (Ascii(value) is not string text || !SyncDate.TryParseExif(text, true, out SyncDate date)) return null;
                (ushort subSecond, ushort offset) = Companions(mapping.Tag.TagId);
                if (Ascii(exif, ExifDirectory, subSecond) is { Length: <= 9 } fraction && fraction.All(char.IsAsciiDigit))
                    date = date with { Fraction = fraction };
                if (Ascii(exif, ExifDirectory, offset) is string zone && SyncDate.TryParse("2000-01-01T00:00" + zone, out SyncDate zoned) && zoned.Zone.Length == 6)
                    date = date with { Zone = zoned.Zone };
                return [date.ToString()];
            }
            case SyncConversion.GpsTime:
            {
                if (!Parts(value, out decimal hours, out decimal minutes, out decimal seconds)) return null;
                if (Ascii(exif, Gps, GpsDateStamp) is not string stamp || !SyncDate.TryParseExif(stamp, false, out SyncDate day)) return null;
                decimal total = decimal.Round((hours * 3600) + (minutes * 60) + seconds, 9);
                if (total >= 86400) return null;
                int whole = (int)decimal.Truncate(total);
                string fraction = (total - whole).ToString("0.#########", Invariant);
                fraction = fraction.Length > 2 ? fraction[2..] : string.Empty;
                return [new SyncDate(day.Year, day.Month, day.Day, true, whole / 3600, whole / 60 % 60, whole % 60, fraction, "Z").ToString()];
            }
            case SyncConversion.GpsCoordinate:
            {
                if (!Parts(value, out decimal degrees, out decimal minutes, out decimal seconds)) return null;
                string? reference = Ascii(exif, Gps, (ushort)(mapping.Tag.TagId - 1));
                if (reference is null || !"NSEW".Contains(reference[0])) return null;
                decimal total = decimal.Round((degrees * 60) + minutes + (seconds / 60), 7);
                if (total > 180 * 60) return null;
                decimal whole = decimal.Truncate(total / 60);
                return [whole.ToString("0", Invariant) + "," + (total - (whole * 60)).ToString("0.0000000", Invariant) + reference[0]];
            }
            case SyncConversion.Version:
                return value.TryGetByteArray(out byte[]? version) && version.Length == 4 && Array.TrueForAll(version, b => b is >= 0x20 and < 0x7F)
                    ? new[] { Encoding.ASCII.GetString(version) } : null;
            case SyncConversion.GpsVersion:
                return value.TryGetByteArray(out byte[]? parts) && parts.Length == 4 ? new[] { string.Join('.', parts) } : null;
            case SyncConversion.Flash:
            {
                if (!value.TryGetUInt32(out uint flash)) return null;
                return [Truth((flash & 1) != 0), ((flash >> 1) & 3).ToString(Invariant), ((flash >> 3) & 3).ToString(Invariant),
                    Truth((flash & 0x20) != 0), Truth((flash & 0x40) != 0)];
            }
            default:
                return value.FieldType == TiffFieldType.Undefined && value.TryGetByteArray(out byte[]? comment) && Comment(comment, exif.ByteOrder) is string body
                    ? new[] { body } : null;
        }
    }

    private static string Truth(bool value) => value ? "True" : "False";

    /// <summary>Three rationals with denominators, as decimals.</summary>
    private static bool Parts(ExifValue value, out decimal first, out decimal second, out decimal third)
    {
        first = second = third = 0;
        if (!value.TryGetRationalArray(out UnsignedRational[]? parts) || parts.Length != 3 || Array.Exists(parts, p => p.Denominator == 0)) return false;
        first = (decimal)parts[0].Numerator / parts[0].Denominator;
        second = (decimal)parts[1].Numerator / parts[1].Denominator;
        third = (decimal)parts[2].Numerator / parts[2].Denominator;
        return true;
    }

    /// <summary>The text after the eight-byte character code; null for a code that is not ASCII, UNICODE or undefined, and for text that cannot be decoded.</summary>
    private static string? Comment(byte[] bytes, Endianness order)
    {
        if (bytes.Length <= 8) return null;
        ReadOnlySpan<byte> code = bytes.AsSpan(0, 8), body = bytes.AsSpan(8);
        string text;
        try
        {
            if (code.SequenceEqual("UNICODE\0"u8))
            {
                bool big = order == Endianness.BigEndian;
                if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE) { big = false; body = body[2..]; }
                else if (body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF) { big = true; body = body[2..]; }
                if (body.Length % 2 != 0) body = body[..^1];
                text = new UnicodeEncoding(big, false, true).GetString(body);
            }
            else if (code.SequenceEqual("ASCII\0\0\0"u8) || code.IndexOfAnyExcept((byte)0) < 0)
                text = StrictUtf8.GetString(body);
            else return null;
        }
        catch (DecoderFallbackException) { return null; }
        text = text.TrimEnd('\0', ' ');
        return text.Length == 0 ? null : text;
    }

    // ---- Writing -----------------------------------------------------------------------------------------

    /// <summary>Writes the tag from XMP texts, with the companion tags the value folds in.</summary>
    /// <exception cref="ArgumentException">The texts are not a value the tag can hold.</exception>
    internal static void Write(ExifEdit edit, ExifXmpMapping mapping, string[] items, ExifMetadata? existing)
    {
        ExifTagKey key = mapping.Tag;
        switch (mapping.Conversion)
        {
            case SyncConversion.Text:
                SetText(edit, key, One(items));
                break;
            case SyncConversion.TextList:
                SetText(edit, key, string.Join("; ", items));
                break;
            case SyncConversion.Integer:
            {
                items = Elements(items);
                long[] numbers = Array.ConvertAll(items, item =>
                    long.TryParse(item.Trim(), NumberStyles.AllowLeadingSign, Invariant, out long n) ? n : throw new ArgumentException("Not an integer."));
                TiffFieldType type = mapping.FieldType;
                if (type is TiffFieldType.Short or TiffFieldType.Long && existing is not null && existing.TryGetEntry(key, out ExifEntry? held)
                    && held.Value.FieldType is TiffFieldType.Short or TiffFieldType.Long)
                    type = held.Value.FieldType;
                long limit = type == TiffFieldType.Long ? uint.MaxValue : type == TiffFieldType.Short ? ushort.MaxValue : byte.MaxValue;
                if (Array.Exists(numbers, n => n < 0 || n > limit)) throw new ArgumentException("The number is outside the range of the field type.");
                if (type == TiffFieldType.Long) edit.Set(key, Array.ConvertAll(numbers, n => (uint)n));
                else if (type == TiffFieldType.Short) edit.Set(key, Array.ConvertAll(numbers, n => (ushort)n));
                else edit.Set(key, ExifValue.FromRaw(type, (uint)numbers.Length, Endianness.LittleEndian, Array.ConvertAll(numbers, n => (byte)n)));
                break;
            }
            case SyncConversion.Rational:
                items = Elements(items);
                if (mapping.FieldType == TiffFieldType.SRational)
                    edit.Set(key, Array.ConvertAll(items, item => { (long n, long d) = Ratio(item, int.MinValue, int.MaxValue); return new SignedRational((int)n, (int)d); }));
                else
                    edit.Set(key, Array.ConvertAll(items, item => { (long n, long d) = Ratio(item, 0, uint.MaxValue); return new UnsignedRational((uint)n, (uint)d); }));
                break;
            case SyncConversion.Date:
            {
                if (!SyncDate.TryParse(One(items), out SyncDate date) || !date.HasTime) throw new ArgumentException("The date names no time of day; EXIF holds a day and a time.");
                (ushort subSecond, ushort offset) = Companions(key.TagId);
                SetText(edit, key, date.ExifText());
                SetOrRemove(edit, new(ExifDirectory, subSecond), date.Fraction);
                SetOrRemove(edit, new(ExifDirectory, offset), date.Offset);
                break;
            }
            case SyncConversion.GpsTime:
            {
                if (!SyncDate.TryParse(One(items), out SyncDate date) || !date.HasTime) throw new ArgumentException("The date names no time of day.");
                if (date.Offset.Length != 0 && date.Offset != "+00:00")
                {
                    if (date.Year is < 2 or > 9998) throw new ArgumentException("The year cannot be moved to UTC.");
                    TimeSpan zone = TimeSpan.ParseExact(date.Offset[1..], @"hh\:mm", Invariant);
                    DateTimeOffset utc = new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second,
                        date.Offset[0] == '-' ? -zone : zone).ToUniversalTime();
                    date = date with { Year = utc.Year, Month = utc.Month, Day = utc.Day, Hour = utc.Hour, Minute = utc.Minute, Second = utc.Second };
                }
                // Seven digits of a second are what a RATIONAL holds beside 59 seconds.
                string fraction = (date.Fraction.Length > 7 ? date.Fraction[..7] : date.Fraction).TrimEnd('0');
                uint scale = 1;
                for (int i = 0; i < fraction.Length; i++) scale *= 10;
                uint seconds = ((uint)date.Second * scale) + (fraction.Length == 0 ? 0 : uint.Parse(fraction, Invariant));
                edit.Set(key, new UnsignedRational[] { new((uint)date.Hour, 1), new((uint)date.Minute, 1), new(seconds, scale) });
                SetText(edit, new(Gps, GpsDateStamp), date.ExifText()[..10]);
                break;
            }
            case SyncConversion.GpsCoordinate:
            {
                string text = One(items).Trim();
                char reference = text.Length == 0 ? ' ' : char.ToUpperInvariant(text[^1]);
                string[] parts = text.Length == 0 ? [] : text[..^1].Split(',');
                bool latitude = key.TagId is 0x0002 or 0x0014;
                if (!(latitude ? reference is 'N' or 'S' : reference is 'E' or 'W') || parts.Length is not (2 or 3)
                    || !uint.TryParse(parts[0], NumberStyles.None, Invariant, out uint degrees)
                    || !decimal.TryParse(parts[1], NumberStyles.AllowDecimalPoint, Invariant, out decimal minutes) || minutes >= 60)
                    throw new ArgumentException("Not a GPS coordinate DDD,MM.mmK or DDD,MM,SSK.");
                decimal seconds = 0;
                if (parts.Length == 3)
                {
                    if (minutes != decimal.Truncate(minutes) || !decimal.TryParse(parts[2], NumberStyles.AllowDecimalPoint, Invariant, out seconds) || seconds >= 60)
                        throw new ArgumentException("Not a GPS coordinate DDD,MM,SSK.");
                }
                else seconds = (minutes - decimal.Truncate(minutes)) * 60;
                uint wholeMinutes = (uint)decimal.Truncate(minutes), tenThousandths = (uint)decimal.Round(seconds * 10000);
                if (tenThousandths >= 600000) { tenThousandths -= 600000; wholeMinutes++; }
                if (wholeMinutes >= 60) { wholeMinutes -= 60; degrees++; }
                if (degrees > (latitude ? 90u : 180u)) throw new ArgumentException("The coordinate is outside the globe.");
                edit.Set(key, new UnsignedRational[] { new(degrees, 1), new(wholeMinutes, 1), new(tenThousandths, 10000) });
                SetText(edit, new(Gps, (ushort)(key.TagId - 1)), reference.ToString());
                break;
            }
            case SyncConversion.Version:
            {
                string text = One(items);
                if (text.Length != 4 || text.Any(c => c is < ' ' or > '~')) throw new ArgumentException("A version is four ASCII characters.");
                edit.Set(key, ExifValue.FromRaw(TiffFieldType.Undefined, 4, Endianness.LittleEndian, Encoding.ASCII.GetBytes(text)));
                break;
            }
            case SyncConversion.GpsVersion:
            {
                string[] parts = One(items).Split('.');
                if (parts.Length != 4) throw new ArgumentException("A GPS version is four numbers.");
                byte[] bytes = Array.ConvertAll(parts, part =>
                    byte.TryParse(part, NumberStyles.None, Invariant, out byte b) ? b : throw new ArgumentException("A GPS version is four numbers below 256."));
                edit.Set(key, ExifValue.FromRaw(TiffFieldType.Byte, 4, Endianness.LittleEndian, bytes));
                break;
            }
            case SyncConversion.Flash:
            {
                if (items.Length != SyncXmp.FlashFields.Length) throw new ArgumentException("Flash has five fields.");
                int flash = (Flag(items[0]) ? 1 : 0) | (Two(items[1]) << 1) | (Two(items[2]) << 3) | (Flag(items[3]) ? 0x20 : 0) | (Flag(items[4]) ? 0x40 : 0);
                edit.Set(key, (ushort)flash);
                break;
            }
            default:
            {
                string text = One(items);
                bool ascii = text.All(c => c is >= ' ' and <= '~' or '\n' or '\r' or '\t');
                bool big = existing?.ByteOrder == Endianness.BigEndian;
                byte[] body = ascii ? Encoding.ASCII.GetBytes(text) : new UnicodeEncoding(big, false, true).GetBytes(text);
                byte[] bytes = new byte[8 + body.Length];
                (ascii ? "ASCII\0\0\0"u8 : "UNICODE\0"u8).CopyTo(bytes);
                body.CopyTo(bytes, 8);
                edit.Set(key, ExifValue.FromRaw(TiffFieldType.Undefined, (uint)bytes.Length, Endianness.LittleEndian, bytes));
                break;
            }
        }
    }

    /// <summary>Removes the tag and the companion tags its value was read with.</summary>
    internal static void Erase(ExifEdit edit, ExifXmpMapping mapping)
    {
        ExifTagKey key = mapping.Tag;
        edit.Remove(key);
        switch (mapping.Conversion)
        {
            case SyncConversion.Date:
                (ushort subSecond, ushort offset) = Companions(key.TagId);
                edit.Remove(new(ExifDirectory, subSecond));
                edit.Remove(new(ExifDirectory, offset));
                break;
            case SyncConversion.GpsTime:
                edit.Remove(new(Gps, GpsDateStamp));
                break;
            case SyncConversion.GpsCoordinate:
                edit.Remove(new(Gps, (ushort)(key.TagId - 1)));
                break;
        }
    }

    /// <summary>The elements of an array that was written as one text with spaces between them, which is how Exiv2 writes it.</summary>
    private static string[] Elements(string[] items) =>
        items.Length == 1 && items[0].Trim().Contains(' ') ? items[0].Split(' ', StringSplitOptions.RemoveEmptyEntries) : items;

    /// <summary>
    /// Whether the tag holds a text without a character: NULs and spaces, after the character code of
    /// a comment. Such a value says nothing; a conversion leaves it out without a note.
    /// </summary>
    internal static bool IsBlank(ExifMetadata exif, ExifXmpMapping mapping)
    {
        if (mapping.Conversion is not (SyncConversion.Text or SyncConversion.TextList or SyncConversion.Comment)) return false;
        if (!exif.TryGetEntry(mapping.Tag, out ExifEntry? entry)) return false;
        ReadOnlySpan<byte> bytes = entry.Value.RawBytes;
        if (mapping.Conversion == SyncConversion.Comment) bytes = bytes.Length > 8 ? bytes[8..] : default;
        else if (entry.Value.FieldType != TiffFieldType.Ascii) return false;
        return bytes.IndexOfAnyExcept((byte)0, (byte)0x20) < 0;
    }

    private static string One(string[] items) => items.Length == 1 ? items[0] : throw new ArgumentException("The tag holds one value.");

    private static bool Flag(string text) => text.Trim() is "True" or "true" or "1";

    private static int Two(string text) =>
        text.Trim().Length == 0 ? 0 : int.TryParse(text.Trim(), NumberStyles.None, Invariant, out int n) && n <= 3 ? n : throw new ArgumentException("A Flash field is 0 to 3.");

    private static (long Numerator, long Denominator) Ratio(string text, long min, long max)
    {
        string[] parts = text.Trim().Split('/');
        long denominator = 1;
        if (parts.Length is not (1 or 2) || !long.TryParse(parts[0], NumberStyles.AllowLeadingSign, Invariant, out long numerator)
            || (parts.Length == 2 && !long.TryParse(parts[1], NumberStyles.AllowLeadingSign, Invariant, out denominator))
            || numerator < min || numerator > max || denominator < min || denominator > max)
            throw new ArgumentException("Not a rational numerator/denominator in the range of the field type.");
        return (numerator, denominator);
    }

    private static void SetOrRemove(ExifEdit edit, ExifTagKey key, string text)
    {
        if (text.Length == 0) edit.Remove(key);
        else SetText(edit, key, text);
    }

    /// <summary>
    /// ASCII; text beyond ASCII as UTF-8, which is what the MWG guidelines ask for and readers expect.
    /// The text is written at its own length: files hold texts shorter than the fixed length a tag is
    /// defined with (ImageUniqueID), and a conversion that refused them could not give back what it took.
    /// </summary>
    private static void SetText(ExifEdit edit, ExifTagKey key, string text)
    {
        if (text.Length == 0 || text.Contains('\0')) throw new ArgumentException("EXIF text is not empty and holds no NUL.");
        byte[] utf8;
        try { utf8 = StrictUtf8.GetBytes(text); }
        catch (EncoderFallbackException) { throw new ArgumentException("The text is not valid Unicode."); }
        byte[] bytes = new byte[utf8.Length + 1];
        utf8.CopyTo(bytes, 0);
        edit.SetUnchecked(key, ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes));
    }
}
