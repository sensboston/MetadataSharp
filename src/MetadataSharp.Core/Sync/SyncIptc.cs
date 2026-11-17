using System.Globalization;
using System.Text;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Core.Sync;

/// <summary>The IPTC side of a conversion: a dataset is read as the texts XMP holds for it, and written from them.</summary>
internal static class SyncIptc
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal static string Key(IptcDatasetKey key) => "Iptc." + key.Record.ToString(Invariant) + ":" + key.Dataset.ToString(Invariant);

    // Compare dataset presence only; value equality and replacement policy belong to the sync caller.
    internal static bool Has(IptcPacket iptc, IptcDatasetKey key)
    {
        foreach (IptcDataset dataset in iptc.Datasets)
            if (dataset.Key == key) return true;
        return false;
    }

    private static bool IsArray(IptcXmpMapping mapping) => mapping.XmpForm is XmpValueKind.Bag or XmpValueKind.Sequence;

    /// <summary>Every occurrence of a dataset as text, in order; null when one of them cannot be decoded.</summary>
    private static List<string>? Texts(IptcPacket iptc, IptcDatasetKey key)
    {
        var texts = new List<string>();
        for (int i = 0; i < iptc.Datasets.Count; i++)
        {
            if (iptc.Datasets[i].Key != key) continue;
            try { texts.Add(iptc.GetText(i)); }
            catch (MetadataException) { return null; }
        }
        return texts;
    }

    /// <summary>
    /// The value of a dataset as XMP texts. Null when the stream lacks it or holds it empty, and, with
    /// <paramref name="undecodable"/> true, when its text cannot be decoded or is not a date.
    /// </summary>
    internal static string[]? Read(IptcPacket iptc, IptcXmpMapping mapping, out bool undecodable)
    {
        undecodable = false;
        List<string>? texts = Texts(iptc, mapping.Dataset);
        if (texts is null) { undecodable = true; return null; }
        texts.RemoveAll(text => text.Length == 0);
        if (texts.Count == 0) return null;
        if (mapping.TimeDataset is not IptcDatasetKey timeKey)
            return IsArray(mapping) ? texts.ToArray() : [texts[0]];

        // CCYYMMDD, where 00 stands for a month or a day that is not known, and HHMMSS±HHMM.
        string day = texts[0];
        if (day.Length != 8 || !day.All(char.IsAsciiDigit)) { undecodable = true; return null; }
        string xmp = day[..4];
        if (day[4..6] != "00")
        {
            xmp += "-" + day[4..6];
            if (day[6..] != "00")
            {
                xmp += "-" + day[6..];
                List<string>? times = Texts(iptc, timeKey);
                if (times is { Count: > 0 } && times[0] is { Length: 11 } time)
                    xmp += "T" + time[..2] + ":" + time[2..4] + ":" + time[4..6] + time[6..9] + ":" + time[9..];
            }
        }
        if (!SyncDate.TryParse(xmp, out SyncDate date)) { undecodable = true; return null; }
        return [date.ToString()];
    }

    /// <summary>
    /// What reading the dataset gives after <see cref="Write"/> was given the texts: text cut to the
    /// length the dataset holds, a date without its fraction of a second and with a zone. Null when
    /// the texts are not a value of the dataset's kind.
    /// </summary>
    internal static string[]? Project(IptcXmpMapping mapping, string[] items)
    {
        if (items.Length == 0) return null;
        if (mapping.TimeDataset is not null)
        {
            if (!SyncDate.TryParse(items[0], out SyncDate date)) return null;
            return [(date with { Fraction = string.Empty, Zone = date.HasTime ? (date.Offset.Length == 0 ? "+00:00" : date.Offset) : string.Empty }).ToString()];
        }
        int max = IptcDatasets.TryGet(mapping.Dataset, out IptcDatasetDefinition? definition) ? definition.MaxBytes : int.MaxValue;
        string[] cut = (IsArray(mapping) ? items : [items[0]]).Select(item => Cut(item, max)).Where(item => item.Length != 0).ToArray();
        return cut.Length == 0 ? null : cut;
    }

    /// <summary>The longest start of the text that is at most <paramref name="max"/> octets of UTF-8 and splits no character.</summary>
    private static string Cut(string text, int max)
    {
        int bytes = 0, length = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > max) return text[..length];
            length += rune.Utf16SequenceLength;
        }
        return text;
    }

    /// <summary>Writes the dataset from XMP texts; a date writes its time dataset too, or removes it.</summary>
    /// <exception cref="ArgumentException">The texts are not a value the dataset can hold.</exception>
    internal static void Write(IptcEdit edit, IptcXmpMapping mapping, string[] items)
    {
        if (mapping.TimeDataset is IptcDatasetKey timeKey)
        {
            if (items.Length != 1 || !SyncDate.TryParse(items[0], out SyncDate date)) throw new ArgumentException("Not a date.");
            string D2(int value) => value.ToString("00", Invariant);
            string time = string.Empty;
            if (date.HasTime)
            {
                string offset = date.Offset.Length == 0 ? "+00:00" : date.Offset;
                time = D2(date.Hour) + D2(date.Minute) + D2(date.Second) + offset[..3] + offset[4..];
            }
            edit.Set(mapping.Dataset, date.Year.ToString("0000", Invariant) + D2(date.Month) + D2(date.Day));
            if (time.Length == 0) edit.Remove(timeKey);
            else edit.Set(timeKey, time);
            return;
        }
        string[] values = Project(mapping, items) ?? throw new ArgumentException("The text is empty.");
        if (IsArray(mapping)) edit.ReplaceText(mapping.Dataset, values);
        else edit.Set(mapping.Dataset, values[0]);
    }

    internal static void Erase(IptcEdit edit, IptcXmpMapping mapping)
    {
        edit.Remove(mapping.Dataset);
        if (mapping.TimeDataset is IptcDatasetKey timeKey) edit.Remove(timeKey);
    }
}
