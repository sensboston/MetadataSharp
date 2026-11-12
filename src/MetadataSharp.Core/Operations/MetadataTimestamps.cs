using System.Globalization;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Core.Operations;

/// <summary>
/// An amount to move timestamps by: whole years, months and days, and a time. Any part may be
/// negative. Years and months are counted on the calendar first, then the days and the time are
/// added; a day the month reached does not have runs over into the next month (31 January and one
/// month is 3 March, or 2 March in a leap year), which is what <c>exiv2 -Y -O -D -a</c> gives.
/// </summary>
/// <param name="Years">Calendar years.</param>
/// <param name="Months">Calendar months.</param>
/// <param name="Days">Days.</param>
/// <param name="Time">Hours, minutes and seconds; whole seconds only.</param>
public readonly record struct TimestampShift(int Years, int Months, int Days, TimeSpan Time)
{
    /// <summary>A shift by a time alone.</summary>
    public TimestampShift(TimeSpan time) : this(0, 0, 0, time) { }

    /// <summary>True when the shift moves nothing.</summary>
    public bool IsZero => Years == 0 && Months == 0 && Days == 0 && Time == TimeSpan.Zero;
}

/// <summary>Moves the timestamps of a file: the operation of <c>exiv2 adjust</c>.</summary>
public static class MetadataTimestamps
{
    private static readonly ExifTagKey[] ExifDates =
    [
        new(ExifDirectoryKind.Ifd0, 0x0132), new(ExifDirectoryKind.Exif, 0x9003), new(ExifDirectoryKind.Exif, 0x9004),
    ];

    /// <summary>The XMP properties that say when the picture was taken, digitized or changed. xmp:MetadataDate is not one: it says when the metadata was written.</summary>
    private static readonly (string Prefix, string Name)[] XmpDates =
    [
        ("xmp", "ModifyDate"), ("xmp", "CreateDate"), ("photoshop", "DateCreated"),
        ("exif", "DateTimeOriginal"), ("exif", "DateTimeDigitized"), ("tiff", "DateTime"),
    ];

    /// <summary>
    /// Computes the edits that move every timestamp the blocks hold by the same amount.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXIF: DateTime, DateTimeOriginal and DateTimeDigitized. The companions stay as they are: a
    /// shift is whole seconds, so SubSecTime* still holds the fraction, and OffsetTime* still names
    /// the zone the corrected clock time is in. GPS time is not a camera clock and is not moved.
    /// </para>
    /// <para>
    /// XMP: xmp:ModifyDate, xmp:CreateDate, photoshop:DateCreated, exif:DateTimeOriginal,
    /// exif:DateTimeDigitized and tiff:DateTime, each where the packet has it, written back with the
    /// fraction and the zone it had. IPTC: DateCreated with TimeCreated and DigitizationDate with
    /// DigitizationTime (the date rows of <see cref="MetadataSync.IptcMappings"/>). A value that
    /// names a day and no time of day moves by the years, months and days only. A value without a
    /// day (a year, or a year and a month) is left.
    /// </para>
    /// <para>
    /// Every block moves its own value, so blocks that agreed before agree after, and a block is
    /// never filled from another. A value that is not a timestamp, or one the shift would take out
    /// of the years 1 to 9999, is not written and is named in <see cref="SyncEdits.Notes"/>.
    /// Exiv2 adjusts EXIF only; the XMP and IPTC parts are this library's.
    /// </para>
    /// </remarks>
    /// <param name="exif">The EXIF block of the file, or null.</param>
    /// <param name="iptc">The IPTC stream of the file, or null.</param>
    /// <param name="xmp">The XMP packet of the file, or null.</param>
    /// <param name="shift">The amount.</param>
    /// <exception cref="ArgumentException">The time of the shift has a fraction of a second.</exception>
    public static SyncEdits Adjust(ExifMetadata? exif, IptcPacket? iptc, XmpPacket? xmp, TimestampShift shift)
    {
        if (shift.Time.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new ArgumentException("A timestamp is shifted by whole seconds.", nameof(shift));
        var edits = new SyncEdits();
        if (shift.IsZero) return edits;
        const string NotATime = "The value is not a timestamp.", OutOfRange = "The shifted timestamp is outside the years 1 to 9999.";

        if (exif is not null)
        {
            foreach (ExifTagKey key in ExifDates)
            {
                if (!exif.TryGetEntry(key, out ExifEntry? entry)) continue;
                string name = MetadataKeys.Format(key);
                if (!entry.Value.TryGetString(out string? text) || !SyncDate.TryParseExif(text ?? string.Empty, true, out SyncDate date))
                    edits.Note(name, name, NotATime);
                else if (!Shift(date, shift, out SyncDate moved))
                    edits.Note(name, name, OutOfRange);
                else
                    edits.Exif.Set(key, moved.ExifText());
            }
        }

        if (xmp is not null)
        {
            var reader = new SyncXmp(xmp);
            foreach ((string prefix, string property) in XmpDates)
            {
                string uri = SyncXmp.Uri(prefix), path = "Xmp." + prefix + "." + property;
                if (!reader.Has(uri, property)) continue;
                if (reader.Kind(uri, property) != XmpEntryKind.Text || reader.Read(uri, property) is not { Length: 1 } texts
                    || !SyncDate.TryParse(texts[0], out SyncDate date))
                    edits.Note(path, path, NotATime);
                else if (date.Day == 0)
                    continue;
                else if (!Shift(date, shift, out SyncDate moved))
                    edits.Note(path, path, OutOfRange);
                else
                    edits.Xmp.Set(path, moved.ToString());
            }
        }

        if (iptc is not null)
        {
            foreach (IptcXmpMapping mapping in MetadataSync.IptcMappings)
            {
                if (mapping.TimeDataset is null || !SyncIptc.Has(iptc, mapping.Dataset)) continue;
                string name = MetadataKeys.Format(mapping.Dataset) ?? SyncIptc.Key(mapping.Dataset);
                if (SyncIptc.Read(iptc, mapping, out _) is not { Length: 1 } texts || !SyncDate.TryParse(texts[0], out SyncDate date))
                    edits.Note(name, name, NotATime);
                else if (date.Day == 0)
                    continue;
                else if (!Shift(date, shift, out SyncDate moved))
                    edits.Note(name, name, OutOfRange);
                else if (moved.HasTime)
                    SyncIptc.Write(edits.Iptc, mapping, [moved.ToString()]);
                else
                    edits.Iptc.Set(mapping.Dataset, moved.Year.ToString("0000", CultureInfo.InvariantCulture)
                        + moved.Month.ToString("00", CultureInfo.InvariantCulture) + moved.Day.ToString("00", CultureInfo.InvariantCulture));
            }
        }
        return edits;
    }

    /// <summary>The moment moved; false when the result leaves the years 1 to 9999. The fraction and the zone are kept.</summary>
    internal static bool Shift(SyncDate date, TimestampShift shift, out SyncDate moved)
    {
        moved = date;
        long months = ((date.Year + (long)shift.Years) * 12) + (date.Month - 1) + shift.Months;
        long year = Math.DivRem(months, 12, out long month);
        if (month < 0) { month += 12; year--; }
        if (year is < 1 or > 9999) return false;
        try
        {
            DateTime moment = new DateTime((int)year, (int)month + 1, 1, date.Hour, date.Minute, date.Second, DateTimeKind.Unspecified)
                .AddDays((long)date.Day - 1 + shift.Days);
            if (date.HasTime) moment = moment.Add(shift.Time);
            moved = date with { Year = moment.Year, Month = moment.Month, Day = moment.Day, Hour = moment.Hour, Minute = moment.Minute, Second = moment.Second };
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
