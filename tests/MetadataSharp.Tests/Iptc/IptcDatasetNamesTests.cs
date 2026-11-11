using MetadataSharp.Core.Iptc;
using Xunit;

namespace MetadataSharp.Tests.Iptc;

public sealed class IptcDatasetNamesTests
{
    [Theory]
    [InlineData(1, 0, "ModelVersion")] // the first row of the table
    [InlineData(1, 90, "CharacterSet")]
    [InlineData(1, 122, "ARMVersion")]
    [InlineData(2, 0, "RecordVersion")]
    [InlineData(2, 5, "ObjectName")]
    [InlineData(2, 25, "Keywords")]
    [InlineData(2, 55, "DateCreated")]
    [InlineData(2, 80, "Byline")]
    [InlineData(2, 101, "CountryName")]
    [InlineData(2, 120, "Caption")]
    [InlineData(2, 122, "Writer")]
    [InlineData(2, 202, "Preview")] // the last row of the table
    public void AKnownDatasetResolvesToItsName(int record, int dataset, string expected)
    {
        Assert.True(IptcDatasetNames.TryGetName(new IptcDatasetKey((byte)record, (byte)dataset), out string? name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(1, 1)]   // between two Envelope datasets
    [InlineData(1, 91)]
    [InlineData(1, 255)]
    [InlineData(2, 1)]
    [InlineData(2, 121)]
    [InlineData(2, 203)] // just past the last Application dataset
    [InlineData(2, 255)]
    [InlineData(0, 0)]   // not a record
    [InlineData(0, 90)]
    [InlineData(3, 5)]   // a dataset number that record 2 names, in a record that is not named here
    [InlineData(8, 10)]
    [InlineData(255, 255)]
    public void AnUnknownDatasetDoesNotResolve(int record, int dataset)
    {
        Assert.False(IptcDatasetNames.TryGetName(new IptcDatasetKey((byte)record, (byte)dataset), out string? name));
        Assert.Null(name);
    }

    [Fact]
    public void TheSameNumberNamesDifferentDatasetsInDifferentRecords()
    {
        Assert.True(IptcDatasetNames.TryGetName(new IptcDatasetKey(1, 90), out string? envelope));
        Assert.True(IptcDatasetNames.TryGetName(new IptcDatasetKey(2, 90), out string? application));
        Assert.Equal("CharacterSet", envelope);
        Assert.Equal("City", application);
    }

    [Fact]
    public void TheTableHasNoDuplicateKeys()
    {
        var keys = new HashSet<(byte Record, byte Dataset)>();
        foreach ((byte record, byte dataset, string name) in IptcDatasetNames.Table)
            Assert.True(keys.Add((record, dataset)), $"{record}:{dataset} ({name}) is listed twice.");
    }

    [Fact]
    public void TheTableHasNoDuplicateNamesWithinARecord()
    {
        var names = new HashSet<(byte Record, string Name)>();
        foreach ((byte record, byte dataset, string name) in IptcDatasetNames.Table)
            Assert.True(names.Add((record, name)), $"{name} names two datasets of record {record} (second is {dataset}).");
    }

    [Fact]
    public void TheTableIsAscendingAndCoversRecordsOneAndTwoOnly()
    {
        int previous = -1;
        foreach ((byte record, byte dataset, string name) in IptcDatasetNames.Table)
        {
            Assert.True(record is 1 or 2, $"{name} is in record {record}.");
            int order = (record << 8) | dataset;
            Assert.True(order > previous, $"{record}:{dataset} ({name}) is out of order.");
            previous = order;
        }
    }

    [Fact]
    public void EveryNameIsASinglePascalCaseWord()
    {
        foreach ((byte record, byte dataset, string name) in IptcDatasetNames.Table)
        {
            Assert.True(name.Length != 0 && name[0] is >= 'A' and <= 'Z', $"{record}:{dataset} has the name '{name}'.");
            Assert.All(name, c => Assert.True(char.IsAsciiLetterOrDigit(c), $"'{name}' is not a single word."));
        }
    }

    [Fact]
    public void TheLookupAnswersForExactlyTheRowsOfTheTable()
    {
        var expected = new Dictionary<(byte Record, byte Dataset), string>();
        foreach ((byte record, byte dataset, string name) in IptcDatasetNames.Table)
            expected[(record, dataset)] = name;

        int resolved = 0;
        for (int record = 0; record <= byte.MaxValue; record++)
        {
            for (int dataset = 0; dataset <= byte.MaxValue; dataset++)
            {
                bool known = IptcDatasetNames.TryGetName(new IptcDatasetKey((byte)record, (byte)dataset), out string? name);
                Assert.Equal(expected.GetValueOrDefault(((byte)record, (byte)dataset)), name);
                Assert.Equal(name is not null, known);
                if (known) resolved++;
            }
        }
        Assert.Equal(IptcDatasetNames.Table.Length, resolved);
    }
}
