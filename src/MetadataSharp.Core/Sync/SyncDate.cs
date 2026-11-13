using System.Globalization;

namespace MetadataSharp.Core.Sync;

/// <summary>
/// A moment as XMP writes it: <c>YYYY[-MM[-DD[Thh:mm[:ss[.s]][Z|±hh:mm]]]]</c>. The parts are kept
/// as they were written; nothing is moved to another time zone.
/// </summary>
internal readonly record struct SyncDate(int Year, int Month, int Day, bool HasTime, int Hour, int Minute, int Second, string Fraction, string Zone)
{
    private static string D2(int value) => value.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>The zone as <c>±hh:mm</c>; empty when the moment names none. <c>Z</c> is <c>+00:00</c>.</summary>
    internal string Offset => Zone == "Z" ? "+00:00" : Zone;

    // Parse the accepted ISO-like precision without normalizing its offset or discarding fractional digits.
    internal static bool TryParse(string text, out SyncDate date)
    {
        date = default;
        text = text.Trim();
        int at = 0;
        if (!Number(text, ref at, 4, out int year)) return false;
        int month = 0, day = 0, hour = 0, minute = 0, second = 0;
        bool time = false;
        string fraction = string.Empty, zone = string.Empty;
        if (Skip(text, ref at, '-'))
        {
            if (!Number(text, ref at, 2, out month) || month is < 1 or > 12) return false;
            if (Skip(text, ref at, '-'))
            {
                if (!Number(text, ref at, 2, out day) || day < 1 || day > DateTime.DaysInMonth(year is < 1 or > 9999 ? 2000 : year, month)) return false;
                if (Skip(text, ref at, 'T') || Skip(text, ref at, ' '))
                {
                    time = true;
                    if (!Number(text, ref at, 2, out hour) || hour > 23 || !Skip(text, ref at, ':') || !Number(text, ref at, 2, out minute) || minute > 59) return false;
                    if (Skip(text, ref at, ':'))
                    {
                        if (!Number(text, ref at, 2, out second) || second > 59) return false;
                        if (Skip(text, ref at, '.'))
                        {
                            int start = at;
                            while (at < text.Length && char.IsAsciiDigit(text[at])) at++;
                            if (at == start || at - start > 9) return false;
                            fraction = text[start..at];
                        }
                    }
                    if (Skip(text, ref at, 'Z')) zone = "Z";
                    else if (at < text.Length && text[at] is '+' or '-')
                    {
                        char sign = text[at++];
                        if (!Number(text, ref at, 2, out int zh) || zh > 14 || !Skip(text, ref at, ':') || !Number(text, ref at, 2, out int zm) || zm > 59) return false;
                        zone = sign + D2(zh) + ":" + D2(zm);
                    }
                }
            }
        }
        if (at != text.Length) return false;
        date = new(year, month, day, time, hour, minute, second, fraction, zone);
        return true;
    }

    // Consume a delimiter only at the current cursor so optional date-time sections stay unambiguous.
    private static bool Skip(string text, ref int at, char c)
    {
        if (at >= text.Length || text[at] != c) return false;
        at++;
        return true;
    }

    // Parse a fixed number of ASCII digits and advance only after the entire component is valid.
    private static bool Number(string text, ref int at, int digits, out int value)
    {
        value = 0;
        if (at + digits > text.Length) return false;
        for (int i = 0; i < digits; i++)
        {
            if (!char.IsAsciiDigit(text[at + i])) return false;
            value = (value * 10) + (text[at + i] - '0');
        }
        at += digits;
        return true;
    }

    // Reconstruct exactly the precision represented by the parsed fields, including the original zone.
    public override string ToString()
    {
        string text = Year.ToString("0000", CultureInfo.InvariantCulture);
        if (Month == 0) return text;
        text += "-" + D2(Month);
        if (Day == 0) return text;
        text += "-" + D2(Day);
        if (!HasTime) return text;
        return text + "T" + D2(Hour) + ":" + D2(Minute) + ":" + D2(Second) + (Fraction.Length == 0 ? string.Empty : "." + Fraction) + Zone;
    }

    /// <summary><c>YYYY:MM:DD HH:MM:SS</c>; the moment has to name a day and a time.</summary>
    internal string ExifText() =>
        Year.ToString("0000", CultureInfo.InvariantCulture) + ":" + D2(Month) + ":" + D2(Day) + " " + D2(Hour) + ":" + D2(Minute) + ":" + D2(Second);

    /// <summary>Reads <c>YYYY:MM:DD HH:MM:SS</c> or, without a time, <c>YYYY:MM:DD</c>.</summary>
    internal static bool TryParseExif(string text, bool withTime, out SyncDate date)
    {
        date = default;
        text = text.TrimEnd(' ', '\0');
        if (text.Length != (withTime ? 19 : 10) || text[4] != ':' || text[7] != ':') return false;
        string xmp = text[..4] + "-" + text[5..7] + "-" + text[8..10] + (withTime ? "T" + text[11..] : string.Empty);
        return (!withTime || text[10] == ' ') && TryParse(xmp, out date) && date.Day != 0;
    }
}
