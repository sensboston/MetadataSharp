using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using Xunit;

namespace MetadataSharp.Tests.Iptc;

/// <summary>The full IPTC edit model: every dataset of records 1 and 2, occurrences, validation, character set.</summary>
public sealed class IptcEditTests
{
    private static readonly IptcDatasetKey Keywords = new(2, 25);
    private static readonly byte[] Utf8Declaration = [0x1c, 1, 90, 0, 3, 0x1b, 0x25, 0x47];
    private static byte[] Dataset(byte record, byte id, params byte[] data) => IptcCodecTests.Dataset(record, id, data);
    private static byte[] Dataset(byte record, byte id, string text) => IptcCodecTests.Dataset(record, id, Encoding.UTF8.GetBytes(text));
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] Raw(IptcPacket packet) => IptcCodecTests.Raw(packet);
    private static IptcPacket Apply(IptcPacket packet, Action<IptcEdit> change)
    {
        var edit = new IptcEdit(); change(edit); return packet.Apply(edit)!;
    }
    private static string[] Texts(IptcPacket packet, IptcDatasetKey key) =>
        Enumerable.Range(0, packet.Datasets.Count).Where(i => packet.Datasets[i].Key == key).Select(packet.GetText).ToArray();

    // ---- The definition table ----

    [Fact]
    public void EveryNamedDatasetHasADefinitionAndTheReverse()
    {
        Assert.Equal(IptcDatasetNames.Table.Length, IptcDatasets.All.Count);
        foreach ((byte record, byte dataset, string name) in IptcDatasetNames.Table)
        {
            Assert.True(IptcDatasets.TryGet(new(record, dataset), out IptcDatasetDefinition? definition));
            Assert.Equal(name, definition!.Name);
            Assert.Equal(IptcDatasetNames.FormatOf(definition.Key), definition.Format);
            Assert.InRange(definition.MinBytes, 0, definition.MaxBytes);
        }
        Assert.False(IptcDatasets.TryGet(new(2, 250), out _));
        Assert.False(IptcDatasets.TryGet(new(3, 10), out _));
        Assert.Equal(new[] { "1:90", "2:0" }, IptcDatasets.All.Where(d => d.IsManaged).Select(d => d.Key.Record + ":" + d.Key.Dataset));
    }

    [Theory]
    [InlineData(2, 5, 64, false)]
    [InlineData(2, 25, 64, true)]
    [InlineData(2, 80, 32, true)]
    [InlineData(2, 105, 256, false)]
    [InlineData(2, 116, 128, false)]
    [InlineData(2, 120, 2000, false)]
    [InlineData(1, 5, 1024, true)]
    [InlineData(2, 12, 236, true)]
    [InlineData(2, 118, 128, true)]
    [InlineData(2, 202, 256000, false)]
    public void ADefinitionCarriesTheLengthAndRepeatabilityOfTheSpecification(int record, int dataset, int max, bool repeatable)
    {
        Assert.True(IptcDatasets.TryGet(new((byte)record, (byte)dataset), out IptcDatasetDefinition? definition));
        Assert.Equal(max, definition!.MaxBytes);
        Assert.Equal(repeatable, definition.IsRepeatable);
    }

    // ---- Validation, one case per dataset class ----

    [Theory]
    // Graphic text: the length is counted in octets.
    [InlineData(2, 90, "Zürich", true)]
    [InlineData(2, 70, "12345678901", false)]
    [InlineData(2, 90, "a\tb", false)]
    // Envelope text is ASCII whatever 1:90 says.
    [InlineData(1, 30, "service", true)]
    [InlineData(1, 30, "sérvice", false)]
    [InlineData(1, 100, "IPTC:20260131:x", true)]
    [InlineData(1, 100, "too short", false)]
    // Numeric characters of a fixed length.
    [InlineData(1, 40, "00000042", true)]
    [InlineData(1, 40, "42", false)]
    [InlineData(2, 50, "0000004a", false)]
    [InlineData(2, 151, "044100", true)]
    // Alphabetic characters.
    [InlineData(2, 100, "CHE", true)]
    [InlineData(2, 100, "CH", false)]
    [InlineData(2, 100, "C1E", false)]
    [InlineData(2, 15, "SPO", true)]
    [InlineData(2, 15, "SPOR", false)]
    [InlineData(2, 135, "en", true)]
    // Enumerated values.
    [InlineData(2, 10, "5", true)]
    [InlineData(2, 10, "0", false)]
    [InlineData(2, 10, "x", false)]
    [InlineData(1, 60, "9", true)]
    [InlineData(2, 8, "01", true)]
    [InlineData(2, 8, "02", false)]
    [InlineData(2, 42, "04", true)]
    [InlineData(2, 42, "05", false)]
    [InlineData(2, 75, "p", true)]
    [InlineData(2, 75, "P", false)]
    [InlineData(2, 131, "L", true)]
    [InlineData(2, 131, "X", false)]
    // Dates: a real day of the calendar; 00 for an unknown part in the two creation dates only.
    [InlineData(2, 30, "20240229", true)]
    [InlineData(2, 30, "20230229", false)]
    [InlineData(2, 30, "20231301", false)]
    [InlineData(2, 30, "2023-1-1", false)]
    [InlineData(2, 30, "20230000", false)]
    [InlineData(2, 55, "20230000", true)]
    [InlineData(2, 62, "20230500", true)]
    // Times: HHMMSS and the offset from UTC.
    [InlineData(2, 60, "235959+0130", true)]
    [InlineData(2, 60, "235959-1100", true)]
    [InlineData(2, 60, "240000+0000", false)]
    [InlineData(2, 60, "235959", false)]
    [InlineData(2, 60, "235959Z0000", false)]
    [InlineData(2, 60, "235959+1500", false)]
    [InlineData(2, 153, "000130", true)]
    [InlineData(2, 153, "000160", false)]
    // Structured values.
    [InlineData(2, 3, "01:News", true)]
    [InlineData(2, 3, "1:News", false)]
    [InlineData(2, 4, "001:Current", true)]
    [InlineData(2, 4, "01:Current", false)]
    [InlineData(2, 12, "IPTC:15054000:sport:soccer", true)]
    [InlineData(2, 12, "IPTC:1505400:sport:soccer", false)]
    [InlineData(2, 130, "3Y", true)]
    [InlineData(2, 130, "5Y", false)]
    [InlineData(2, 130, "3X", false)]
    [InlineData(2, 150, "2A", true)]
    [InlineData(2, 150, "3A", false)]
    public void ATextValueIsCheckedAgainstTheDefinitionOfItsDataset(int record, int dataset, string value, bool accepted)
    {
        var key = new IptcDatasetKey((byte)record, (byte)dataset); var edit = new IptcEdit();
        if (!accepted)
        {
            Assert.Throws<ArgumentException>(() => edit.Set(key, value));
            Assert.True(edit.IsEmpty);
            return;
        }
        edit.Set(key, value);
        IptcPacket packet = IptcPacket.CreateEmpty().Apply(edit)!;
        IptcDataset written = Assert.Single(packet.Datasets, d => d.Key == key);
        byte[] bytes = new byte[written.Value.ByteLength]; written.Value.CopyRawTo(bytes);
        Assert.Equal(Encoding.UTF8.GetBytes(value), bytes);
    }

    [Fact]
    public void BinaryAndUndefinedDatasetsAreWrittenByTheTypedAndRawMethodsOnly()
    {
        var edit = new IptcEdit();
        Assert.Throws<ArgumentException>(() => edit.Set(new(1, 20), "1"));          // a two-octet number
        Assert.Throws<ArgumentException>(() => edit.Set(new(2, 202), "preview"));   // binary
        Assert.Throws<ArgumentException>(() => edit.Set(new(2, 250), "x"));         // not defined
        Assert.Throws<ArgumentException>(() => edit.SetUInt16(new(2, 25), 1));
        Assert.Throws<ArgumentException>(() => edit.SetDate(new(2, 25), new DateOnly(2026, 1, 31)));
        Assert.Throws<ArgumentException>(() => edit.SetTime(new(2, 55), new TimeOnly(1, 2, 3), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => edit.SetTime(new(2, 60), new TimeOnly(1, 2, 3), TimeSpan.FromSeconds(30)));
        Assert.True(edit.IsEmpty);

        edit.SetUInt16(new(1, 20), 11); edit.SetUInt16(new(2, 200), 0x0102);
        edit.SetDate(new(2, 30), new DateOnly(2026, 1, 31));
        edit.SetTime(new(2, 35), new TimeOnly(7, 8, 9), new TimeSpan(-5, -30, 0));
        edit.ReplaceRaw(new(2, 250), [IptcValue.FromRaw([0, 0xff, 1])]);
        byte[] expected =
        [
            .. Utf8Declaration, .. Dataset(1, 20, 0, 11), .. Dataset(2, 0, 0, 4), .. Dataset(2, 200, 1, 2),
            .. Dataset(2, 30, "20260131"), .. Dataset(2, 35, "070809-0530"), .. Dataset(2, 250, 0, 0xff, 1),
        ];
        Assert.Equal(expected, Raw(IptcPacket.CreateEmpty().Apply(edit)!));
    }

    [Fact]
    public void ManagedDatasetsAndOtherRecordsCannotBeAddressed()
    {
        var edit = new IptcEdit();
        foreach (IptcDatasetKey key in new IptcDatasetKey[] { new(1, 90), new(2, 0), new(0, 5), new(3, 10), new(7, 10), new(8, 10), new(9, 10) })
        {
            Assert.Throws<ArgumentException>(() => edit.Remove(key));
            Assert.Throws<ArgumentException>(() => edit.RemoveAt(key, 0));
            Assert.Throws<ArgumentException>(() => edit.ReplaceRaw(key, [IptcValue.FromRaw([1])]));
            Assert.Throws<ArgumentException>(() => edit.AppendRaw(key, IptcValue.FromRaw([1])));
            Assert.Throws<ArgumentException>(() => edit.InsertRaw(key, 0, IptcValue.FromRaw([1])));
        }
        Assert.Throws<ArgumentException>(() => edit.SetUInt16(new(2, 0), 4));
        Assert.True(edit.IsEmpty);
    }

    // ---- Occurrences ----

    [Fact]
    public void OccurrencesAreAppendedInsertedAndRemovedInOrder()
    {
        IptcPacket source = IptcPacket.Parse([.. Dataset(2, 25, "a"), .. Dataset(2, 25, "b"), .. Dataset(2, 25, "c")]);
        Assert.Equal(new[] { "a", "b", "c", "d" }, Texts(Apply(source, e => e.Append(Keywords, "d")), Keywords));
        Assert.Equal(new[] { "x", "a", "b", "c" }, Texts(Apply(source, e => e.Insert(Keywords, 0, "x")), Keywords));
        Assert.Equal(new[] { "a", "b", "c", "x" }, Texts(Apply(source, e => e.Insert(Keywords, 3, "x")), Keywords));
        Assert.Equal(new[] { "a", "c" }, Texts(Apply(source, e => e.RemoveAt(Keywords, 1)), Keywords));
        Assert.Equal(new[] { "z" }, Texts(Apply(source, e => e.SetKeywords(["z"])), Keywords));
        Assert.Empty(Texts(Apply(source, e => e.Remove(Keywords)), Keywords));
        // Each operation sees what the ones before it left.
        IptcPacket chained = Apply(source, e => { e.RemoveAt(Keywords, 0); e.Insert(Keywords, 1, "x"); e.Append(Keywords, "y"); e.RemoveAt(Keywords, 2); });
        Assert.Equal(new[] { "b", "x", "y" }, Texts(chained, Keywords));
        // A replacement of all makes the operations before it moot, and the ones after it count from it.
        IptcPacket replaced = Apply(source, e => { e.RemoveAt(Keywords, 9); e.SetKeywords(["k"]); e.AddKeyword("l"); });
        Assert.Equal(new[] { "k", "l" }, Texts(replaced, Keywords));
    }

    [Fact]
    public void AnIndexPastTheOccurrencesIsRefusedWhenTheEditIsApplied()
    {
        IptcPacket source = IptcPacket.Parse(Dataset(2, 25, "a"));
        var remove = new IptcEdit(); remove.RemoveAt(Keywords, 1);
        Assert.Equal(MetadataErrorCode.InvalidIptcEdit, Assert.Throws<MetadataException>(() => source.Apply(remove)).Code);
        var insert = new IptcEdit(); insert.Insert(Keywords, 2, "x");
        Assert.Equal(MetadataErrorCode.InvalidIptcEdit, Assert.Throws<MetadataException>(() => source.Apply(insert)).Code);
        var absent = new IptcEdit(); absent.RemoveAt(new(2, 80), 0);
        Assert.Equal(MetadataErrorCode.InvalidIptcEdit, Assert.Throws<MetadataException>(() => source.Apply(absent)).Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => new IptcEdit().RemoveAt(Keywords, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IptcEdit().Insert(Keywords, -1, "x"));
    }

    [Fact]
    public void ADatasetThatDoesNotRepeatTakesOneValue()
    {
        var edit = new IptcEdit();
        Assert.Throws<ArgumentException>(() => edit.Append(new(2, 5), "title"));
        Assert.Throws<ArgumentException>(() => edit.Insert(new(2, 120), 0, "caption"));
        Assert.Throws<ArgumentException>(() => edit.ReplaceText(new(2, 90), ["one", "two"]));
        Assert.True(edit.IsEmpty);
        // The raw methods are the stated way round a rule.
        edit.AppendRaw(new(2, 5), IptcValue.FromRaw("second"u8));
        IptcPacket packet = IptcPacket.Parse(Dataset(2, 5, "first")).Apply(edit)!;
        Assert.Equal(2, packet.Datasets.Count(d => d.Key == new IptcDatasetKey(2, 5)));
    }

    [Fact]
    public void KeptOccurrencesKeepTheirBytesAndNewDatasetsStayInRecordOrder()
    {
        // Non-minimal framing of a kept keyword, object data in record 8, an unknown record-2 dataset.
        byte[] wideKeyword = [0x1c, 2, 25, 0x80, 2, 0, 1, (byte)'a'];
        byte[] objectData = Dataset(8, 10, 1, 2, 3, 0xff);
        byte[] unknown = Dataset(2, 240, 0xfe);
        IptcPacket source = IptcPacket.Parse([.. Utf8Declaration, .. Dataset(2, 0, 0, 4), .. wideKeyword, .. unknown, .. objectData]);
        IptcPacket changed = Apply(source, e => { e.AddKeyword("b"); e.SetCity("Riga"); e.Set(new(1, 30), "svc"); });
        byte[] expected =
        [
            .. Utf8Declaration, .. Dataset(1, 30, "svc"), .. Dataset(2, 0, 0, 4), .. wideKeyword, .. Dataset(2, 25, "b"),
            .. unknown, .. Dataset(2, 90, "Riga"), .. objectData,
        ];
        Assert.Equal(expected, Raw(changed));
    }

    [Fact]
    public void AnEditThatChangesNoByteReturnsTheSameStream()
    {
        IptcPacket source = IptcPacket.Parse([.. Utf8Declaration, .. Dataset(2, 0, 0, 4), .. Dataset(2, 25, "a"), .. Dataset(2, 5, "t")]);
        Assert.Same(source, Apply(source, e => e.SetObjectName("t")));
        Assert.Same(source, Apply(source, e => e.SetKeywords(["a"])));
        Assert.Same(source, Apply(source, e => { e.AddKeyword("b"); e.RemoveAt(Keywords, 1); }));
        Assert.Same(source, Apply(source, e => e.SetCity(null)));
        Assert.NotSame(source, Apply(source, e => e.SetObjectName("u")));
    }

    // ---- Character set ----

    [Fact]
    public void AnUndeclaredStreamKeepsDeclaringNothingForAsciiAndGainsUtf8ForTextBeyondIt()
    {
        IptcPacket source = IptcPacket.Parse([.. Dataset(2, 0, 0, 4), .. Dataset(2, 5, "title")]);
        IptcPacket ascii = Apply(source, e => e.SetCity("Riga"));
        Assert.Equal(IptcCharacterSet.Unspecified, ascii.CharacterSet);
        Assert.Equal(Join(Dataset(2, 0, 0, 4), Dataset(2, 5, "title"), Dataset(2, 90, "Riga")), Raw(ascii));

        IptcPacket wide = Apply(source, e => e.SetCity("Rīga"));
        Assert.Equal(IptcCharacterSet.Utf8, wide.CharacterSet);
        Assert.Equal(Join(Utf8Declaration, Dataset(2, 0, 0, 4), Dataset(2, 5, "title"), Dataset(2, 90, "Rīga")), Raw(wide));
        Assert.Equal("Rīga", wide.GetText(3));
    }

    [Fact]
    public void ADeclaredUtf8StreamKeepsItsDeclaration()
    {
        IptcPacket source = IptcPacket.CreateEmpty();
        IptcPacket changed = Apply(source, e => { e.SetCaption("Привет"); e.SetCity("Riga"); });
        Assert.Equal(IptcCharacterSet.Utf8, changed.CharacterSet);
        Assert.Single(changed.Datasets, d => d.Key == new IptcDatasetKey(1, 90));
    }

    [Fact]
    public void AWriteThatWouldMixEncodingsIsRefused()
    {
        // Latin-1 "é" in a caption, with no declaration: nobody said what 0xE9 is.
        IptcPacket legacy = IptcPacket.Parse([.. Dataset(2, 120, 0xe9), .. Dataset(2, 240, 0xff), .. Dataset(2, 202, 0xff)]);
        var wide = new IptcEdit(); wide.SetCity("Rīga");
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => legacy.Apply(wide)).Code);
        // ASCII is the same bytes in every encoding the stream could be in: nothing is declared, nothing is mixed.
        IptcPacket ascii = Apply(legacy, e => e.SetCity("Riga"));
        Assert.Equal(IptcCharacterSet.Unspecified, ascii.CharacterSet);
        // Replacing the one undeclared text leaves no legacy text; binary and undefined datasets are not text.
        IptcPacket replaced = Apply(legacy, e => { e.SetCaption("é"); e.SetCity("Rīga"); });
        Assert.Equal(IptcCharacterSet.Utf8, replaced.CharacterSet);
        Assert.Equal("é", replaced.Caption);
        // Raw bytes are not text: they are written as given and declare nothing.
        IptcPacket raw = Apply(legacy, e => e.ReplaceRaw(new(2, 90), [IptcValue.FromRaw([0xe9])]));
        Assert.Equal(IptcCharacterSet.Unspecified, raw.CharacterSet);
        // Another declared character set: text is refused, ASCII included; raw is written.
        IptcPacket other = IptcPacket.Parse([.. Dataset(1, 90, 0x1b, 0x2e, 0x41), .. Dataset(2, 120, 0xe9)]);
        var plain = new IptcEdit(); plain.SetCity("Riga");
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => other.Apply(plain)).Code);
        Assert.Equal(IptcCharacterSet.Unsupported, Apply(other, e => e.ReplaceRaw(new(2, 90), [IptcValue.FromRaw([0xe9])])).CharacterSet);
    }

    // ---- Typed convenience ----

    [Fact]
    public void TheTypedSettersWriteTheirDatasets()
    {
        IptcPacket packet = Apply(IptcPacket.CreateEmpty(), e =>
        {
            e.SetObjectName("Title"); e.SetKeywords(["k1", "k2"]); e.AddKeyword("k3"); e.SetBylines(["A", "B"]); e.SetBylineTitles(["Staff"]);
            e.SetHeadline("Head"); e.SetCaption("Line one\r\nline two"); e.SetCopyright("(c) 2026"); e.SetCredit("Agency"); e.SetSource("Src");
            e.SetCity("Riga"); e.SetSubLocation("Old town"); e.SetProvinceState("Vidzeme"); e.SetCountryCode("LVA"); e.SetCountryName("Latvia");
            e.SetDateCreated(new DateTimeOffset(2026, 11, 12, 9, 5, 7, new TimeSpan(2, 0, 0)));
        });
        Assert.Equal("Title", packet.ObjectName); Assert.Equal(new[] { "k1", "k2", "k3" }, packet.Keywords); Assert.Equal(new[] { "A", "B" }, packet.Bylines);
        Assert.Equal("Head", packet.Headline); Assert.Equal("Line one\r\nline two", packet.Caption); Assert.Equal("(c) 2026", packet.Copyright);
        var expected = new (int Dataset, string Text)[]
        {
            (85, "Staff"), (110, "Agency"), (115, "Src"), (90, "Riga"), (92, "Old town"), (95, "Vidzeme"), (100, "LVA"), (101, "Latvia"),
            (55, "20261112"), (60, "090507+0200"),
        };
        foreach ((int dataset, string text) in expected)
            Assert.Equal(new[] { text }, Texts(packet, new(2, (byte)dataset)));
    }

    [Fact]
    public void ADateAloneRemovesTheTimeThatWouldReadAsItsTime()
    {
        IptcPacket source = Apply(IptcPacket.CreateEmpty(), e => e.SetDateCreated(new DateTimeOffset(2026, 11, 12, 9, 5, 7, new TimeSpan(-3, -30, 0))));
        Assert.Equal(new[] { "090507-0330" }, Texts(source, new(2, 60)));
        IptcPacket dated = Apply(source, e => e.SetDateCreated(new DateOnly(2027, 1, 2)));
        Assert.Equal(new[] { "20270102" }, Texts(dated, new(2, 55)));
        Assert.Empty(Texts(dated, new(2, 60)));
        IptcPacket removed = Apply(dated, e => e.RemoveDateCreated());
        Assert.Equal(IptcCodecTests.Raw(IptcPacket.CreateEmpty()), Raw(removed));
        IptcPacket cleared = Apply(Apply(source, e => e.SetCity("Riga")), e => e.SetCity(null));
        Assert.Empty(Texts(cleared, new(2, 90)));
    }

    [Fact]
    public void EveryTextDatasetOfTheSpecificationIsReadBack()
    {
        IptcPacket packet = IptcPacket.Parse([.. Utf8Declaration, .. Dataset(2, 0, 0, 4), .. Dataset(2, 90, "Rīga"), .. Dataset(2, 55, "20260131"), .. Dataset(2, 240, "x")]);
        Assert.Equal("Rīga", packet.GetText(2));
        Assert.Equal("20260131", packet.GetText(3));
        foreach (int index in new[] { 0, 1, 4 })
            Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => packet.GetText(index)).Code);
    }
}
