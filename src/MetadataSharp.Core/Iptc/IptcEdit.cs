namespace MetadataSharp.Core.Iptc;

/// <summary>
/// An ordered list of changes to the datasets of IIM records 1 and 2. A dataset may occur more than
/// once; its occurrences are numbered from zero in stream order, and each operation sees what the
/// operations before it left. Datasets of every other record cannot be addressed: a write keeps them
/// byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// The text methods take the datasets IPTC IIM 4.2 defines (<see cref="IptcDatasets"/>) and check each
/// value against the definition when the method is called: the length in octets, the numeric, date and
/// time forms, the values of an enumerated dataset, and whether the dataset may repeat. A value that
/// breaks its definition is an <see cref="ArgumentException"/> and leaves the edit as it was. The raw
/// methods check nothing but the budget: they are the way to write a dataset the specification does
/// not define, and the stated way round a rule.
/// </para>
/// <para>
/// CodedCharacterSet (1:90) and RecordVersion (2:0) belong to the writer and cannot be addressed.
/// Text is encoded as UTF-8. A stream that declares UTF-8 keeps its declaration; a stream that
/// declares nothing keeps declaring nothing while the text written is ASCII, and gains the UTF-8
/// declaration when it is not - unless a text dataset the write keeps already holds bytes above 0x7F,
/// whose encoding nobody declared: that write is refused, as is a text write to a stream that
/// declares another character set. See <see cref="IptcPacket.Apply"/>.
/// </para>
/// </remarks>
public sealed class IptcEdit
{
    internal enum OperationKind { ReplaceAll, Append, Insert, RemoveAt, SetFirst }
    internal sealed record Operation(IptcDatasetKey Key, OperationKind Kind, int Index, IptcValue[] Values, bool Text);
    private const int MaxValues = 4096;
    private const long MaxBytes = 1_048_576;
    private List<Operation> operations = [];
    private bool removeAll;

    /// <summary>Creates an edit that holds no change.</summary>
    public IptcEdit() { }
    /// <summary>True when the edit holds no change.</summary>
    public bool IsEmpty => !removeAll && operations.Count == 0;

    // ---- Generic, validated ----

    /// <summary>Replaces every occurrence of a dataset with one text value.</summary>
    /// <exception cref="ArgumentException">The dataset is not defined, is managed, or the value breaks its definition.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void Set(IptcDatasetKey key, string value) { ArgumentNullException.ThrowIfNull(value); ReplaceText(key, [value]); }
    /// <summary>
    /// Replaces every occurrence of a dataset with the given text values, in order; no values removes
    /// it. A date is written CCYYMMDD and a time HHMMSS±HHMM, as the stream holds them.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The dataset is not one IIM 4.2 defines in records 1 and 2, is managed or holds binary data; a
    /// value breaks its definition; or a dataset that does not repeat is given more than one value.
    /// </exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="values">The text values to encode in order; an empty sequence removes all occurrences.</param>
    public void ReplaceText(IptcDatasetKey key, IEnumerable<string> values)
    {
        Check(key); ArgumentNullException.ThrowIfNull(values);
        IptcDatasetDefinition definition = Definition(key);
        var copy = new List<IptcValue>();
        foreach (string value in values)
        {
            if (copy.Count >= (definition.IsRepeatable ? MaxValues : 1))
                throw new ArgumentException("The dataset does not repeat, or the values exceed the default count budget.", nameof(values));
            copy.Add(new(IptcDatasets.Encode(definition, value)));
        }
        Commit(new(key, OperationKind.ReplaceAll, 0, copy.ToArray(), true));
    }
    /// <summary>
    /// Replaces the first occurrence of a dataset with a text value and leaves the others; a dataset
    /// the stream does not have is added. This is what <c>set</c> does in an Exiv2 command file.
    /// </summary>
    /// <exception cref="ArgumentException">The dataset is not defined, is managed or holds binary data, or the value breaks its definition.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetFirst(IptcDatasetKey key, string value)
    {
        Check(key); ArgumentNullException.ThrowIfNull(value);
        Commit(new(key, OperationKind.SetFirst, 0, [new(IptcDatasets.Encode(Definition(key), value))], true));
    }
    /// <summary>Adds an occurrence of a repeatable dataset after its last one.</summary>
    /// <exception cref="ArgumentException">The dataset is not defined, is managed, does not repeat, or the value breaks its definition.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void Append(IptcDatasetKey key, string value) =>
        Commit(new(key, OperationKind.Append, 0, [Repeated(key, value)], true));
    /// <summary>
    /// Adds an occurrence of a repeatable dataset before the occurrence at <paramref name="index"/>;
    /// an index equal to the number of occurrences appends. An index past that is refused when the
    /// edit is applied, with <see cref="MetadataErrorCode.InvalidIptcEdit"/>.
    /// </summary>
    /// <exception cref="ArgumentException">As <see cref="Append"/>.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="index">The zero-based occurrence index within this dataset.</param>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void Insert(IptcDatasetKey key, int index, string value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Commit(new(key, OperationKind.Insert, index, [Repeated(key, value)], true));
    }
    /// <summary>
    /// Removes the occurrence at <paramref name="index"/> of a dataset of record 1 or 2. An index the
    /// dataset does not have is refused when the edit is applied, with
    /// <see cref="MetadataErrorCode.InvalidIptcEdit"/>.
    /// </summary>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="index">The zero-based occurrence index within this dataset.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public void RemoveAt(IptcDatasetKey key, int index)
    {
        Check(key); ArgumentOutOfRangeException.ThrowIfNegative(index);
        Commit(new(key, OperationKind.RemoveAt, index, [], false));
    }
    /// <summary>Removes every occurrence of a dataset of record 1 or 2; nothing happens when it has none.</summary>
    /// <param name="key">The IIM record and dataset number to change.</param>
    public void Remove(IptcDatasetKey key) { Check(key); Commit(new(key, OperationKind.ReplaceAll, 0, [], false)); }

    /// <summary>Replaces every occurrence of a two-octet binary number dataset, such as FileFormat (1:20).</summary>
    /// <exception cref="ArgumentException">The dataset is not a two-octet number, or is managed.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetUInt16(IptcDatasetKey key, ushort value)
    {
        Check(key);
        if (Definition(key).Rule != IptcRule.UInt16) throw new ArgumentException("The dataset is not a two-octet binary number.", nameof(key));
        Commit(new(key, OperationKind.ReplaceAll, 0, [new(new byte[] { (byte)(value >> 8), (byte)value })], false));
    }
    /// <summary>Replaces every occurrence of a date dataset, such as DateCreated (2:55).</summary>
    /// <exception cref="ArgumentException">The dataset is not a date.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="date">The calendar date to write as CCYYMMDD.</param>
    public void SetDate(IptcDatasetKey key, DateOnly date)
    {
        Check(key);
        if (Definition(key).Format != IptcValueFormat.Date) throw new ArgumentException("The dataset is not a date.", nameof(key));
        ReplaceText(key, [IptcDatasets.Format(date)]);
    }
    /// <summary>Replaces every occurrence of a time dataset, such as TimeCreated (2:60), with a time and its offset from UTC.</summary>
    /// <exception cref="ArgumentException">The dataset is not a time.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The offset is not whole minutes, or is fifteen hours or more.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="time">The time of day to write.</param>
    /// <param name="utcOffset">The offset of <paramref name="time"/> from UTC; it must be a whole number of minutes and less than fifteen hours.</param>
    public void SetTime(IptcDatasetKey key, TimeOnly time, TimeSpan utcOffset)
    {
        Check(key);
        if (Definition(key).Format != IptcValueFormat.Time) throw new ArgumentException("The dataset is not a time.", nameof(key));
        ReplaceText(key, [IptcDatasets.Format(time, utcOffset)]);
    }

    // ---- Raw ----

    /// <summary>
    /// Replaces every occurrence of a dataset of record 1 or 2 with the given raw values, in order; no
    /// values removes it. Nothing is checked against a definition, and the bytes are not text to the
    /// character set rules.
    /// </summary>
    /// <exception cref="ArgumentException">The key is outside records 1 and 2 or is managed, the edit already removes all IPTC, or the values exceed the default budget.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="values">The raw dataset values to write, in order.</param>
    public void ReplaceRaw(IptcDatasetKey key, IEnumerable<IptcValue> values)
    {
        Check(key); ArgumentNullException.ThrowIfNull(values);
        var copy = new List<IptcValue>();
        foreach (IptcValue value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (copy.Count >= MaxValues) throw new ArgumentException("Replacement exceeds the default count budget.", nameof(values));
            copy.Add(value);
        }
        Commit(new(key, OperationKind.ReplaceAll, 0, copy.ToArray(), false));
    }
    /// <summary>Adds a raw occurrence of a dataset of record 1 or 2 after its last one.</summary>
    /// <exception cref="ArgumentException">The key is outside records 1 and 2 or is managed, the edit removes all IPTC, or the operation budget is exceeded.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="value">The raw dataset value to write.</param>
    public void AppendRaw(IptcDatasetKey key, IptcValue value)
    {
        Check(key); ArgumentNullException.ThrowIfNull(value);
        Commit(new(key, OperationKind.Append, 0, [value], false));
    }
    /// <summary>Adds a raw occurrence before the occurrence at <paramref name="index"/>; see <see cref="Insert"/>.</summary>
    /// <exception cref="ArgumentException">The key is outside records 1 and 2 or is managed, the edit removes all IPTC, or the operation budget is exceeded.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    /// <param name="key">The IIM record and dataset number to change.</param>
    /// <param name="index">The zero-based occurrence index within this dataset.</param>
    /// <param name="value">The raw dataset value to write.</param>
    public void InsertRaw(IptcDatasetKey key, int index, IptcValue value)
    {
        Check(key); ArgumentOutOfRangeException.ThrowIfNegative(index); ArgumentNullException.ThrowIfNull(value);
        Commit(new(key, OperationKind.Insert, index, [value], false));
    }

    /// <summary>Removes the whole IPTC stream, the datasets of every record with it.</summary>
    /// <exception cref="ArgumentException">The edit already holds a dataset change.</exception>
    public void RemoveAllIptc()
    {
        if (operations.Count != 0) throw new ArgumentException("Whole IPTC removal cannot be mixed with dataset edits.");
        removeAll = true;
    }

    // ---- Typed convenience: null (or no values) removes the dataset ----

    /// <summary>Sets or removes ObjectName (2:5), the title.</summary>
    /// <param name="value">The title to store, or null to remove ObjectName.</param>
    public void SetObjectName(string? value) => One(new(2, 5), value);
    /// <summary>Replaces every Keywords dataset (2:25).</summary>
    /// <param name="values">The text values to encode in order; an empty sequence removes all occurrences.</param>
    public void SetKeywords(IEnumerable<string> values) => ReplaceText(new(2, 25), values);
    /// <summary>Adds one Keywords dataset (2:25) after the last.</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void AddKeyword(string value) => Append(new(2, 25), value);
    /// <summary>Replaces every Byline dataset (2:80), the creators.</summary>
    /// <param name="values">The text values to encode in order; an empty sequence removes all occurrences.</param>
    public void SetBylines(IEnumerable<string> values) => ReplaceText(new(2, 80), values);
    /// <summary>Replaces every BylineTitle dataset (2:85).</summary>
    /// <param name="values">The text values to encode in order; an empty sequence removes all occurrences.</param>
    public void SetBylineTitles(IEnumerable<string> values) => ReplaceText(new(2, 85), values);
    /// <summary>Sets or removes Headline (2:105).</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetHeadline(string? value) => One(new(2, 105), value);
    /// <summary>Sets or removes Caption (2:120), the description; it may hold line breaks.</summary>
    /// <param name="value">The caption text to store, or null to remove Caption.</param>
    public void SetCaption(string? value) => One(new(2, 120), value);
    /// <summary>Sets or removes Copyright (2:116).</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetCopyright(string? value) => One(new(2, 116), value);
    /// <summary>Sets or removes Credit (2:110).</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetCredit(string? value) => One(new(2, 110), value);
    /// <summary>Sets or removes Source (2:115).</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetSource(string? value) => One(new(2, 115), value);
    /// <summary>Sets or removes City (2:90).</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetCity(string? value) => One(new(2, 90), value);
    /// <summary>Sets or removes SubLocation (2:92).</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetSubLocation(string? value) => One(new(2, 92), value);
    /// <summary>Sets or removes ProvinceState (2:95).</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetProvinceState(string? value) => One(new(2, 95), value);
    /// <summary>Sets or removes CountryCode (2:100): three letters.</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetCountryCode(string? value) => One(new(2, 100), value);
    /// <summary>Sets or removes CountryName (2:101).</summary>
    /// <param name="value">The text value to encode according to the dataset definition.</param>
    public void SetCountryName(string? value) => One(new(2, 101), value);
    /// <summary>
    /// Sets DateCreated (2:55) and TimeCreated (2:60) from one moment: the date and time of day as
    /// written, with the offset from UTC, which has to be whole minutes.
    /// </summary>
    /// <param name="value">The moment whose local date, time of day, and UTC offset are written.</param>
    public void SetDateCreated(DateTimeOffset value)
    {
        string time = IptcDatasets.Format(TimeOnly.FromTimeSpan(new TimeSpan(value.Hour, value.Minute, value.Second)), value.Offset);
        ReplaceText(new(2, 55), [IptcDatasets.Format(DateOnly.FromDateTime(value.DateTime))]);
        ReplaceText(new(2, 60), [time]);
    }
    /// <summary>
    /// Sets DateCreated (2:55) to a date alone and removes TimeCreated (2:60): a time kept from
    /// before would read as the time of the new date.
    /// </summary>
    /// <param name="date">The calendar date to write as CCYYMMDD.</param>
    public void SetDateCreated(DateOnly date)
    {
        ReplaceText(new(2, 55), [IptcDatasets.Format(date)]);
        Remove(new(2, 60));
    }
    /// <summary>Removes DateCreated (2:55) and TimeCreated (2:60).</summary>
    public void RemoveDateCreated() { Remove(new(2, 55)); Remove(new(2, 60)); }

    private void One(IptcDatasetKey key, string? value)
    {
        if (value is null) Remove(key); else ReplaceText(key, [value]);
    }
    // Encode once after checking repeatability so append and insert share the same definition rules.
    private IptcValue Repeated(IptcDatasetKey key, string value)
    {
        Check(key); ArgumentNullException.ThrowIfNull(value);
        IptcDatasetDefinition definition = Definition(key);
        if (!definition.IsRepeatable) throw new ArgumentException("The dataset does not repeat; set it instead.", nameof(key));
        return new(IptcDatasets.Encode(definition, value));
    }
    // Centralize definition lookup so text edits cannot address unknown or writer-managed datasets.
    private static IptcDatasetDefinition Definition(IptcDatasetKey key) =>
        IptcDatasets.TryGet(key, out IptcDatasetDefinition? definition) ? definition
            : throw new ArgumentException("IIM 4.2 does not define the dataset in records 1 and 2; the raw methods write it.", nameof(key));
    // Reject controls and non-addressable records before any typed operation is queued.
    private void Check(IptcDatasetKey key)
    {
        if (removeAll || key.Record is not (1 or 2) || key == new IptcDatasetKey(1, 90) || key == new IptcDatasetKey(2, 0))
            throw new ArgumentException("Only datasets of records 1 and 2 other than 1:90 and 2:0 can be addressed, without whole removal.", nameof(key));
    }
    // Compute the new aggregate budget before swapping the list, so a rejected operation leaves the edit unchanged.
    private void Commit(Operation operation)
    {
        // A replacement of all occurrences makes every earlier operation on the dataset moot.
        List<Operation> next = operation.Kind == OperationKind.ReplaceAll
            ? operations.Where(o => o.Key != operation.Key).ToList() : new(operations);
        next.Add(operation);
        long bytes = 0; int count = 0;
        foreach (Operation item in next)
        {
            count += item.Values.Length;
            foreach (IptcValue value in item.Values) bytes += value.ByteLength + 9L;
        }
        if (count > MaxValues || bytes > MaxBytes || next.Count > MaxValues)
            throw new ArgumentException("The edit exceeds its aggregate default budget.");
        operations = next;
    }
    internal IptcEdit Capture() => new() { removeAll = removeAll, operations = new(operations) };
    internal bool RemovesAll => removeAll;
    internal IReadOnlyList<Operation> Operations => operations;
}
