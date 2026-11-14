using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MetadataSharp.Core.Iptc;

/// <summary>
/// What IPTC IIM 4.2 says of one dataset of record 1 (Envelope) or record 2 (Application): its form,
/// its length in octets, whether it may occur more than once, and the values it is limited to.
/// </summary>
public sealed class IptcDatasetDefinition
{
    /// <summary>Builds one immutable IPTC dataset schema row from the verified local definition table.</summary>
    /// <param name="key">The record and dataset number.</param>
    /// <param name="name">The local name for the dataset.</param>
    /// <param name="rule">The validation rule associated with the dataset.</param>
    /// <param name="minBytes">The minimum encoded length.</param>
    /// <param name="maxBytes">The maximum encoded length.</param>
    /// <param name="repeatable">Whether multiple occurrences are permitted.</param>
    /// <param name="allowed">The set of restricted values, or an empty array.</param>
    internal IptcDatasetDefinition(IptcDatasetKey key, string name, IptcRule rule, int minBytes, int maxBytes, bool repeatable, string[] allowed)
    {
        Key = key; Name = name; Rule = rule; MinBytes = minBytes; MaxBytes = maxBytes; IsRepeatable = repeatable;
        AllowedValues = Array.AsReadOnly(allowed); Format = IptcDatasetNames.FormatOf(key);
        Label = IptcDatasetNames.LabelOf(name); Description = IptcDatasetNames.DescriptionOf(key, name);
        Aliases = Array.AsReadOnly(IptcDatasetNames.AliasesOf(key));
        ValueMeanings = Array.AsReadOnly(IptcDatasetNames.ValueMeaningsOf(key));
    }
    /// <summary>The record and dataset number.</summary>
    public IptcDatasetKey Key { get; }
    /// <summary>The name <see cref="IptcDatasetNames"/> gives the dataset.</summary>
    public string Name { get; }
    /// <summary>A short display label written in this library's own wording.</summary>
    public string Label { get; }
    /// <summary>A concise description written for this library; it does not reproduce upstream prose.</summary>
    public string Description { get; }
    /// <summary>Other local names accepted for this record and dataset ID.</summary>
    public IReadOnlyList<string> Aliases { get; }
    /// <summary>The form of the value.</summary>
    public IptcValueFormat Format { get; }
    /// <summary>The shortest value, in octets.</summary>
    public int MinBytes { get; }
    /// <summary>The longest value, in octets.</summary>
    public int MaxBytes { get; }
    /// <summary>Whether the specification lets the dataset occur more than once.</summary>
    public bool IsRepeatable { get; }
    /// <summary>The values an enumerated dataset is limited to; empty for every other dataset.</summary>
    public IReadOnlyList<string> AllowedValues { get; }
    /// <summary>The locally authored display meaning for enumerated values; empty for non-enumerated datasets.</summary>
    public IReadOnlyList<IptcDatasetValueMeaning> ValueMeanings { get; }
    /// <summary>
    /// True for the two datasets the writer owns and an edit cannot address: CodedCharacterSet (1:90)
    /// and RecordVersion (2:0).
    /// </summary>
    public bool IsManaged => Key == new IptcDatasetKey(1, 90) || Key == new IptcDatasetKey(2, 0);
    internal IptcRule Rule { get; }
}

internal enum IptcRule
{
    Graphic, Caption, Digits, Alpha, Enumerated, Date, PartialDate, Time, Duration,
    ObjectType, ObjectAttribute, Subject, ImageType, AudioType, UInt16, Binary,
}

/// <summary>The definitions of the datasets of IPTC IIM 4.2 records 1 and 2.</summary>
public static class IptcDatasets
{
    private static readonly string[] None = [];
    private static readonly string[] OneToNine = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];
    private static readonly Dictionary<IptcDatasetKey, IptcDatasetDefinition> Lookup = Build();

    /// <summary>Every definition, ascending by record and then by dataset number.</summary>
    public static IReadOnlyList<IptcDatasetDefinition> All { get; } =
        Array.AsReadOnly(Lookup.Values.OrderBy(d => (d.Key.Record << 8) | d.Key.Dataset).ToArray());

    /// <summary>Looks up the definition of a dataset; false for one IIM 4.2 does not define in records 1 and 2.</summary>
    /// <param name="key">The IIM record and dataset number.</param>
    /// <param name="definition">Receives the definition when found; otherwise receives <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the dataset is defined in record 1 or 2; otherwise, <see langword="false"/>.</returns>
    public static bool TryGet(IptcDatasetKey key, [NotNullWhen(true)] out IptcDatasetDefinition? definition) =>
        Lookup.TryGetValue(key, out definition);

    // Build one immutable lookup from the checked IIM table; a missing corresponding name invalidates the schema.
    private static Dictionary<IptcDatasetKey, IptcDatasetDefinition> Build()
    {
        var table = new Dictionary<IptcDatasetKey, IptcDatasetDefinition>();
        void Add(byte record, byte dataset, IptcRule rule, int min, int max, bool repeatable = false, string[]? allowed = null)
        {
            var key = new IptcDatasetKey(record, dataset);
            if (!IptcDatasetNames.TryGetName(key, out string? name)) throw new InvalidOperationException("A defined dataset has no name.");
            table.Add(key, new(key, name, rule, min, max, repeatable, allowed ?? None));
        }
        void Text(byte record, byte dataset, int max, bool repeatable = false) => Add(record, dataset, IptcRule.Graphic, 0, max, repeatable);
        void Number(byte record, byte dataset) => Add(record, dataset, IptcRule.UInt16, 2, 2);
        void Date(byte record, byte dataset, bool repeatable = false) => Add(record, dataset, IptcRule.Date, 8, 8, repeatable);
        void Time(byte record, byte dataset) => Add(record, dataset, IptcRule.Time, 11, 11);

        // Record 1, Envelope.
        Number(1, 0); Text(1, 5, 1024, true); Number(1, 20); Number(1, 22); Text(1, 30, 10);
        Add(1, 40, IptcRule.Digits, 8, 8); Text(1, 50, 32, true);
        Add(1, 60, IptcRule.Enumerated, 1, 1, false, OneToNine);
        Date(1, 70); Time(1, 80); Add(1, 90, IptcRule.Binary, 0, 32);
        Add(1, 100, IptcRule.Graphic, 14, 80); Number(1, 120); Number(1, 122);
        // Record 2, Application.
        Number(2, 0);
        Add(2, 3, IptcRule.ObjectType, 3, 67); Add(2, 4, IptcRule.ObjectAttribute, 4, 68, true);
        Text(2, 5, 64); Text(2, 7, 64);
        Add(2, 8, IptcRule.Enumerated, 2, 2, false, ["01"]);
        Add(2, 10, IptcRule.Enumerated, 1, 1, false, OneToNine);
        Add(2, 12, IptcRule.Subject, 13, 236, true);
        Add(2, 15, IptcRule.Alpha, 0, 3); Text(2, 20, 32, true); Text(2, 22, 32); Text(2, 25, 64, true);
        Add(2, 26, IptcRule.Alpha, 3, 3, true); Text(2, 27, 64, true);
        Date(2, 30); Time(2, 35); Date(2, 37); Time(2, 38); Text(2, 40, 256);
        Add(2, 42, IptcRule.Enumerated, 2, 2, false, ["01", "02", "03", "04"]);
        Text(2, 45, 10, true); Date(2, 47, true); Add(2, 50, IptcRule.Digits, 8, 8, true);
        Add(2, 55, IptcRule.PartialDate, 8, 8); Time(2, 60); Add(2, 62, IptcRule.PartialDate, 8, 8); Time(2, 63);
        Text(2, 65, 32); Text(2, 70, 10);
        Add(2, 75, IptcRule.Enumerated, 1, 1, false, ["a", "p", "b"]);
        Text(2, 80, 32, true); Text(2, 85, 32, true); Text(2, 90, 32); Text(2, 92, 32); Text(2, 95, 32);
        Add(2, 100, IptcRule.Alpha, 3, 3); Text(2, 101, 64); Text(2, 103, 32); Text(2, 105, 256);
        Text(2, 110, 32); Text(2, 115, 32); Text(2, 116, 128); Text(2, 118, 128, true);
        Add(2, 120, IptcRule.Caption, 0, 2000); Text(2, 122, 32, true);
        Add(2, 125, IptcRule.Binary, 7360, 7360);
        Add(2, 130, IptcRule.ImageType, 2, 2);
        Add(2, 131, IptcRule.Enumerated, 1, 1, false, ["P", "L", "S"]);
        Add(2, 135, IptcRule.Alpha, 2, 3);
        Add(2, 150, IptcRule.AudioType, 2, 2); Add(2, 151, IptcRule.Digits, 6, 6); Add(2, 152, IptcRule.Digits, 2, 2);
        Add(2, 153, IptcRule.Duration, 6, 6); Text(2, 154, 64);
        Number(2, 200); Number(2, 201); Add(2, 202, IptcRule.Binary, 0, 256000);
        return table;
    }

    /// <summary>The octets of a text value, or an <see cref="ArgumentException"/> that says which rule it breaks.</summary>
    internal static byte[] Encode(IptcDatasetDefinition definition, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string name = definition.Key.Record + ":" + definition.Key.Dataset + " " + definition.Name;
        if (definition.Rule is IptcRule.UInt16 or IptcRule.Binary)
            throw new ArgumentException(name + " holds binary data, not text.", nameof(text));
        if (text.Length > definition.MaxBytes)
            throw new ArgumentException(name + " is limited to " + definition.MaxBytes + " octets.", nameof(text));
        byte[] bytes;
        try { bytes = IptcText.Utf8.GetBytes(text); }
        catch (System.Text.EncoderFallbackException e) { throw new ArgumentException("Text is not valid Unicode.", nameof(text), e); }
        if (bytes.Length < definition.MinBytes || bytes.Length > definition.MaxBytes)
            throw new ArgumentException(name + " takes " + definition.MinBytes + " to " + definition.MaxBytes + " octets.", nameof(text));
        bool lines = definition.Rule == IptcRule.Caption;
        foreach (char c in text)
        {
            if (char.IsControl(c) && !(lines && (c is '\r' or '\n')))
                throw new ArgumentException(name + " cannot hold control characters.", nameof(text));
            // Record 1 is always ISO 646: the character set of 1:90 covers the records after it.
            if (definition.Key.Record == 1 && c > 0x7e)
                throw new ArgumentException(name + " is an envelope dataset, limited to ASCII.", nameof(text));
        }
        string? broken = definition.Rule switch
        {
            IptcRule.Digits => Digits(text, 0, text.Length) ? null : "numeric characters",
            IptcRule.Alpha => text.All(char.IsAsciiLetter) ? null : "alphabetic characters",
            IptcRule.Enumerated => definition.AllowedValues.Contains(text) ? null : "one of " + string.Join(", ", definition.AllowedValues),
            IptcRule.Date => IsDate(text, false) ? null : "a date CCYYMMDD",
            IptcRule.PartialDate => IsDate(text, true) ? null : "a date CCYYMMDD, 00 for an unknown month or day",
            IptcRule.Time => IsTime(text) ? null : "a time HHMMSS±HHMM",
            IptcRule.Duration => Digits(text, 0, 6) && Below(text, 2, 60) && Below(text, 4, 60) ? null : "a duration HHMMSS",
            IptcRule.ObjectType => text.Length > 2 && Digits(text, 0, 2) && text[2] == ':' ? null : "two digits, a colon and an optional name",
            IptcRule.ObjectAttribute => text.Length > 3 && Digits(text, 0, 3) && text[3] == ':' ? null : "three digits, a colon and an optional name",
            IptcRule.Subject => IsSubject(text) ? null : "IPR:8 digits, then up to three names after colons",
            IptcRule.ImageType => text.Length == 2 && "012349".Contains(text[0]) && "WYMCKRGBTFLPS".Contains(text[1]) ? null : "a component count 0-4 or 9 and a colour code",
            IptcRule.AudioType => text.Length == 2 && "012".Contains(text[0]) && "ACMQRSTVW".Contains(text[1]) ? null : "a channel count 0-2 and a type code",
            _ => null,
        };
        if (broken is not null) throw new ArgumentException(name + " takes " + broken + ".", nameof(text));
        return bytes;
    }

    // Keep numeric text validation ASCII-only and bounds-checked before indexing the requested slice.
    private static bool Digits(string text, int start, int count)
    {
        if (text.Length < start + count) return false;
        for (int i = start; i < start + count; i++)
            if (!char.IsAsciiDigit(text[i])) return false;
        return true;
    }
    // Read two digits after IsDate or IsTime has verified the complete source slice.
    private static int Pair(string text, int at) => (text[at] - '0') * 10 + (text[at + 1] - '0');
    // Compare a fixed two-digit field without converting or accepting a value outside its domain.
    private static bool Below(string text, int at, int limit) => Pair(text, at) < limit;
    // Validate calendar dates while allowing only the explicitly supported partial month/day forms.
    private static bool IsDate(string text, bool partial)
    {
        if (text.Length != 8 || !Digits(text, 0, 8)) return false;
        int year = Pair(text, 0) * 100 + Pair(text, 2), month = Pair(text, 4), day = Pair(text, 6);
        if (month == 0) return partial && day <= 31;
        if (month > 12) return false;
        if (day == 0) return partial;
        // Year 0000 is written by some tools; it is a leap year of the proleptic calendar.
        return day <= DateTime.DaysInMonth(year == 0 ? 2000 : year, month);
    }
    // Require the eleven-byte IIM time form and validate every clock and UTC-offset component.
    private static bool IsTime(string text) =>
        text.Length == 11 && Digits(text, 0, 6) && (text[6] is '+' or '-') && Digits(text, 7, 4)
        && Below(text, 0, 24) && Below(text, 2, 60) && Below(text, 4, 60) && Below(text, 7, 15) && Below(text, 9, 60);
    // Subject Reference consists of an optional IPR prefix, an eight-digit ID, and up to three names.
    private static bool IsSubject(string text)
    {
        string[] parts = text.Split(':');
        return parts.Length is >= 2 and <= 5 && parts[0].Length is >= 1 and <= 32 && parts[1].Length == 8 && Digits(parts[1], 0, 8);
    }

    // IIM stores dates without separators, independent of the process culture.
    internal static string Format(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    // Preserve the caller's wall-clock time and serialize only an exact whole-minute UTC offset.
    internal static string Format(TimeOnly time, TimeSpan utcOffset)
    {
        if (utcOffset.Ticks % TimeSpan.TicksPerMinute != 0 || utcOffset.Duration() >= TimeSpan.FromHours(15))
            throw new ArgumentOutOfRangeException(nameof(utcOffset), "The offset from UTC is whole minutes, less than fifteen hours.");
        TimeSpan size = utcOffset.Duration();
        return time.ToString("HHmmss", CultureInfo.InvariantCulture) + (utcOffset < TimeSpan.Zero ? "-" : "+")
            + size.Hours.ToString("00", CultureInfo.InvariantCulture) + size.Minutes.ToString("00", CultureInfo.InvariantCulture);
    }
}
