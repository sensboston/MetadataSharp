using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.Operations;

/// <summary>Builds a file name from a timestamp: the naming of <c>exiv2 rename -r format</c>. Nothing is renamed here.</summary>
public static class MetadataFileNames
{
    /// <summary>The format Exiv2 uses when none is given: <c>20261112_090507</c>.</summary>
    public const string DefaultFormat = "%Y%m%d_%H%M%S";

    /// <summary>
    /// Formats the new name of a file: the format with its tokens replaced, then the extension the
    /// file has. The result is a name, not a path; the caller renames and decides what to do when
    /// the name is taken (Exiv2 asks, or appends <c>_1</c>, <c>_2</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keywords, replaced first, as Exiv2 does: <c>:basename:</c> (the name without its extension),
    /// <c>:basesuffix:</c> (what the name without its extension has from its first dot on, or
    /// nothing), <c>:dirname:</c> (the name of the directory holding the file), <c>:parentname:</c>
    /// (the name of that directory's parent).
    /// </para>
    /// <para>
    /// Conversions of strftime, in the C locale whatever the culture of the thread:
    /// <c>%Y %C %y %m %d %e %j %H %I %M %S %p %B %b %h %A %a %u %w %F %D %T %R %n %t %%</c>.
    /// Conversions that depend on a locale or a time zone (<c>%c %x %X %Z %z %U %W %V %G %g %s</c>)
    /// and any other character after <c>%</c> are refused: the timestamp of a picture names no zone,
    /// and a name should not change with the machine.
    /// </para>
    /// </remarks>
    /// <param name="format">The format; <see cref="DefaultFormat"/> is what Exiv2 defaults to.</param>
    /// <param name="taken">The moment to name the file after, usually <see cref="ExifMetadata.DateTaken"/>.</param>
    /// <param name="path">The path or the name of the file as it is now.</param>
    /// <returns>The formatted file name with the original extension, not a path.</returns>
    /// <exception cref="ArgumentException">The format has a conversion that is not listed or ends with <c>%</c>, the result is empty, or it holds a character a file name cannot (a separator, a NUL).</exception>
    public static string Format(string format, DateTime taken, string path)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(path);
        // Both separators are separators here, on every system: a name must not depend on the machine.
        string[] parts = path.Split('/', '\\');
        string file = parts[^1];
        int dot = file.LastIndexOf('.');
        // A leading dot starts a hidden file's name, not an extension.
        string stem = dot > 0 ? file[..dot] : file, extension = dot > 0 ? file[dot..] : string.Empty;
        int first = stem.IndexOf('.');
        string text = format
            .Replace(":basename:", stem, StringComparison.Ordinal)
            .Replace(":basesuffix:", first >= 0 ? stem[first..] : string.Empty, StringComparison.Ordinal)
            .Replace(":dirname:", parts.Length > 1 ? parts[^2] : string.Empty, StringComparison.Ordinal)
            .Replace(":parentname:", parts.Length > 2 ? parts[^3] : string.Empty, StringComparison.Ordinal);

        CultureInfo c = CultureInfo.InvariantCulture;
        var name = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '%') { name.Append(text[i]); continue; }
            if (++i == text.Length) throw new ArgumentException("The format ends with %.", nameof(format));
            int hour12 = taken.Hour % 12 == 0 ? 12 : taken.Hour % 12;
            name.Append(text[i] switch
            {
                'Y' => taken.Year.ToString(c),
                'C' => (taken.Year / 100).ToString("00", c),
                'y' => (taken.Year % 100).ToString("00", c),
                'm' => taken.Month.ToString("00", c),
                'd' => taken.Day.ToString("00", c),
                'e' => taken.Day.ToString(c).PadLeft(2),
                'j' => taken.DayOfYear.ToString("000", c),
                'H' => taken.Hour.ToString("00", c),
                'I' => hour12.ToString("00", c),
                'M' => taken.Minute.ToString("00", c),
                'S' => taken.Second.ToString("00", c),
                'p' => taken.Hour < 12 ? "AM" : "PM",
                'B' => c.DateTimeFormat.GetMonthName(taken.Month),
                'b' or 'h' => c.DateTimeFormat.GetAbbreviatedMonthName(taken.Month),
                'A' => c.DateTimeFormat.GetDayName(taken.DayOfWeek),
                'a' => c.DateTimeFormat.GetAbbreviatedDayName(taken.DayOfWeek),
                'u' => (taken.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)taken.DayOfWeek).ToString(c),
                'w' => ((int)taken.DayOfWeek).ToString(c),
                'F' => taken.ToString("yyyy'-'MM'-'dd", c),
                'D' => taken.ToString("MM'/'dd'/'yy", c),
                'T' => taken.ToString("HH':'mm':'ss", c),
                'R' => taken.ToString("HH':'mm", c),
                'n' => "\n",
                't' => "\t",
                '%' => "%",
                _ => throw new ArgumentException($"The conversion %{text[i]} is not supported.", nameof(format)),
            });
        }
        if (name.Length == 0) throw new ArgumentException("The format gives an empty name.", nameof(format));
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] is '/' or '\\' or '\0')
                throw new ArgumentException("The format gives a name with a path separator or a NUL.", nameof(format));
        }
        return name.Append(extension).ToString();
    }
}
