using System.Text;
using MetadataSharp.Core.Iptc;
using Xunit;

namespace MetadataSharp.Tests.Iptc;

/// <summary>
/// The typed form of an IPTC value: a number, a date and a time as a person writes them. Text and binary
/// data have none, and neither has a value that is not in the form the specification gives its dataset.
/// </summary>
public sealed class IptcInterpretationTests
{
    private static string? Interpret(int record, int dataset, byte[] value) =>
        IptcDatasetNames.Interpret(new IptcDatasetKey((byte)record, (byte)dataset), IptcValue.FromRaw(value));

    private static string? Interpret(int record, int dataset, string value) =>
        Interpret(record, dataset, Encoding.ASCII.GetBytes(value));

    [Theory]
    [InlineData(1, 0, 0, 4, "4")]         // ModelVersion
    [InlineData(2, 0, 0, 2, "2")]         // RecordVersion
    [InlineData(2, 0, 0, 0, "0")]
    [InlineData(2, 0, 1, 2, "258")]       // most significant octet first
    [InlineData(1, 20, 0, 11, "11")]      // FileFormat
    public void ATwoOctetNumberIsADecimalNumber(int record, int dataset, int high, int low, string expected) =>
        Assert.Equal(expected, Interpret(record, dataset, new[] { (byte)high, (byte)low }));

    [Theory]
    [InlineData(new byte[] { 4 })]
    [InlineData(new byte[] { 0, 0, 4 })]
    [InlineData(new byte[0])]
    public void ANumberOfAnotherLengthHasNoTypedForm(byte[] value) =>
        Assert.Null(Interpret(2, 0, value));

    [Theory]
    [InlineData(2, 55, "20030403", "2003-04-03")]   // DateCreated
    [InlineData(1, 70, "20220104", "2022-01-04")]   // DateSent
    [InlineData(2, 62, "00000000", "0000-00-00")]   // digits are copied, not judged
    public void ADateIsWrittenWithHyphens(int record, int dataset, string value, string expected) =>
        Assert.Equal(expected, Interpret(record, dataset, value));

    [Theory]
    [InlineData("2003040")]
    [InlineData("200304031")]
    [InlineData("2003-4-3")]
    [InlineData("2003040a")]
    public void ADateThatIsNotEightDigitsHasNoTypedForm(string value) =>
        Assert.Null(Interpret(2, 55, value));

    [Theory]
    [InlineData(2, 60, "160930+0000", "16:09:30+00:00")]   // TimeCreated
    [InlineData(2, 60, "221312-0500", "22:13:12-05:00")]
    [InlineData(2, 63, "062923-0629", "06:29:23-06:29")]   // the offset is copied, not judged
    [InlineData(1, 80, "000000+0000", "00:00:00+00:00")]   // TimeSent
    [InlineData(2, 60, "160930", "16:09:30+00:00")]        // no offset reads as none
    public void ATimeIsWrittenWithColonsAndItsOffset(int record, int dataset, string value, string expected) =>
        Assert.Equal(expected, Interpret(record, dataset, value));

    [Theory]
    [InlineData("1609")]
    [InlineData("160930+00")]
    [InlineData("160930 0000")]
    [InlineData("160930+00a0")]
    [InlineData("16093a+0000")]
    [InlineData("16:09:30")]
    public void ATimeInAnotherFormHasNoTypedForm(string value) =>
        Assert.Null(Interpret(2, 60, value));

    [Fact]
    public void TheCharacterSetDeclarationIsReadFromThePacketNotFromItsValue()
    {
        byte[] utf8 = [0x1B, 0x25, 0x47];
        Assert.Null(Interpret(1, 90, utf8));
        Assert.Equal(IptcCharacterSet.Utf8, IptcPacket.Parse([0x1C, 1, 90, 0, 3, .. utf8, 0x1C, 2, 0, 0, 2, 0, 4]).CharacterSet);
    }

    [Theory]
    [InlineData(2, 120)]   // Caption
    [InlineData(2, 25)]    // Keywords
    [InlineData(2, 202)]   // Preview, binary
    [InlineData(2, 56)]    // not a dataset of record 2
    [InlineData(3, 0)]     // another record
    public void TextBinaryDataAndUnknownDatasetsHaveNoTypedForm(int record, int dataset) =>
        Assert.Null(Interpret(record, dataset, "20030403"));

    [Fact]
    public void ANullValueIsRefused() =>
        Assert.Throws<ArgumentNullException>(() => IptcDatasetNames.Interpret(new IptcDatasetKey(2, 0), null!));

    [Fact]
    public void AParsedPacketOffersTheTypedFormOfEachDataset()
    {
        // 1:90 ESC % G, 2:0 version 4, 2:55 a date, 2:120 a caption.
        byte[] packet =
        [
            0x1C, 1, 90, 0, 3, 0x1B, 0x25, 0x47,
            0x1C, 2, 0, 0, 2, 0, 4,
            0x1C, 2, 55, 0, 8, (byte)'2', (byte)'0', (byte)'0', (byte)'3', (byte)'0', (byte)'4', (byte)'0', (byte)'3',
            0x1C, 2, 120, 0, 2, (byte)'h', (byte)'i',
        ];
        string?[] typed = IptcPacket.Parse(packet).Datasets
            .Select(dataset => IptcDatasetNames.Interpret(dataset.Key, dataset.Value)).ToArray();
        Assert.Equal(new[] { null, "4", "2003-04-03", null }, typed);
    }
}
