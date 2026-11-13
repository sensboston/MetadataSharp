using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.Iptc;

/// <summary>The form IPTC IIM 4.2 gives the value of a dataset.</summary>
public enum IptcValueFormat
{
    /// <summary>Graphic characters, or a form this table does not describe.</summary>
    Text,
    /// <summary>A binary number of two octets, most significant first.</summary>
    UInt16,
    /// <summary>A date written as eight digits, CCYYMMDD.</summary>
    Date,
    /// <summary>A time written as HHMMSS followed by the offset from UTC, ±HHMM.</summary>
    Time,
    /// <summary>Binary data that is not text.</summary>
    Binary,
}

/// <summary>
/// The names of the datasets that IPTC IIM 4.2 defines in record 1 (Envelope) and record 2
/// (Application), each written as a single PascalCase word. A name is a label for display: it says
/// nothing about whether a value is present, well-formed or meaningful.
/// </summary>
public static class IptcDatasetNames
{
    /// <summary>Every named dataset, ascending by record and then by dataset number.</summary>
    internal static readonly (byte Record, byte Dataset, string Name)[] Table =
    [
        // Record 1, Envelope.
        (1, 0, "ModelVersion"), (1, 5, "Destination"), (1, 20, "FileFormat"), (1, 22, "FileVersion"),
        (1, 30, "ServiceId"), (1, 40, "EnvelopeNumber"), (1, 50, "ProductId"), (1, 60, "EnvelopePriority"),
        (1, 70, "DateSent"), (1, 80, "TimeSent"), (1, 90, "CharacterSet"), (1, 100, "UNO"),
        (1, 120, "ARMId"), (1, 122, "ARMVersion"),
        // Record 2, Application.
        (2, 0, "RecordVersion"), (2, 3, "ObjectType"), (2, 4, "ObjectAttribute"), (2, 5, "ObjectName"),
        (2, 7, "EditStatus"), (2, 8, "EditorialUpdate"), (2, 10, "Urgency"), (2, 12, "Subject"),
        (2, 15, "Category"), (2, 20, "SuppCategory"), (2, 22, "FixtureId"), (2, 25, "Keywords"),
        (2, 26, "LocationCode"), (2, 27, "LocationName"), (2, 30, "ReleaseDate"), (2, 35, "ReleaseTime"),
        (2, 37, "ExpirationDate"), (2, 38, "ExpirationTime"), (2, 40, "SpecialInstructions"),
        (2, 42, "ActionAdvised"), (2, 45, "ReferenceService"), (2, 47, "ReferenceDate"),
        (2, 50, "ReferenceNumber"), (2, 55, "DateCreated"), (2, 60, "TimeCreated"),
        (2, 62, "DigitizationDate"), (2, 63, "DigitizationTime"), (2, 65, "Program"),
        (2, 70, "ProgramVersion"), (2, 75, "ObjectCycle"), (2, 80, "Byline"), (2, 85, "BylineTitle"),
        (2, 90, "City"), (2, 92, "SubLocation"), (2, 95, "ProvinceState"), (2, 100, "CountryCode"),
        (2, 101, "CountryName"), (2, 103, "TransmissionReference"), (2, 105, "Headline"),
        (2, 110, "Credit"), (2, 115, "Source"), (2, 116, "Copyright"), (2, 118, "Contact"),
        (2, 120, "Caption"), (2, 122, "Writer"), (2, 125, "RasterizedCaption"), (2, 130, "ImageType"),
        (2, 131, "ImageOrientation"), (2, 135, "Language"), (2, 150, "AudioType"), (2, 151, "AudioRate"),
        (2, 152, "AudioResolution"), (2, 153, "AudioDuration"), (2, 154, "AudioOutcue"),
        (2, 200, "PreviewFormat"), (2, 201, "PreviewVersion"), (2, 202, "Preview"),
    ];

    /// <summary>
    /// The form the specification gives the value of a record 1 or record 2 dataset. It describes the
    /// definition, not the bytes: a value may be present in another form, and a reader has to check.
    /// </summary>
    /// <param name="key">The record and dataset number.</param>
    /// <returns>The value form specified for the dataset, or <see cref="IptcValueFormat.Text"/> when no typed form is listed.</returns>
    public static IptcValueFormat FormatOf(IptcDatasetKey key) => (key.Record, key.Dataset) switch
    {
        (1, 0) or (1, 20) or (1, 22) or (1, 120) or (1, 122) or (2, 0) or (2, 200) or (2, 201) => IptcValueFormat.UInt16,
        (1, 70) or (2, 30) or (2, 37) or (2, 47) or (2, 55) or (2, 62) => IptcValueFormat.Date,
        (1, 80) or (2, 35) or (2, 38) or (2, 60) or (2, 63) => IptcValueFormat.Time,
        (2, 125) or (2, 202) => IptcValueFormat.Binary,
        _ => IptcValueFormat.Text,
    };

    /// <summary>
    /// The typed form of one value of a record 1 or record 2 dataset, or null when the value has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A two-octet binary number answers as a decimal number (<c>4</c> for RecordVersion), a date as
    /// <c>CCYY-MM-DD</c>, and a time as <c>HH:MM:SS±HH:MM</c>; a time written without its offset from
    /// UTC answers with a zero offset. The digits are copied as they stand: a month 13 is not refused.
    /// </para>
    /// <para>
    /// Null means there is nothing to add to the bytes: the dataset is text or binary data, or the
    /// value is not in the form the specification gives the dataset. Text is read with
    /// <see cref="IptcPacket.GetText" />, which knows the packet's character set, and the character
    /// set a packet declares is <see cref="IptcPacket.CharacterSet" />. Nothing is ever thrown for the
    /// content of a value.
    /// </para>
    /// </remarks>
    /// <param name="key">The record and dataset number of the value.</param>
    /// <param name="value">The raw dataset value.</param>
    /// <returns>The value's typed representation, or <see langword="null"/> when the data is not in the specified form.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is null.</exception>
    public static string? Interpret(IptcDatasetKey key, IptcValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ReadOnlySpan<byte> bytes = value.Span;
        switch (FormatOf(key))
        {
            case IptcValueFormat.UInt16:
                return bytes.Length == 2
                    ? ((bytes[0] << 8) | bytes[1]).ToString(CultureInfo.InvariantCulture)
                    : null;
            case IptcValueFormat.Date:
                if (bytes.Length != 8 || !Digits(bytes))
                    return null;
                return Ascii(bytes.Slice(0, 4)) + "-" + Ascii(bytes.Slice(4, 2)) + "-" + Ascii(bytes.Slice(6, 2));
            case IptcValueFormat.Time:
                if (bytes.Length is not (6 or 11) || !Digits(bytes.Slice(0, 6)))
                    return null;
                string time = Ascii(bytes.Slice(0, 2)) + ":" + Ascii(bytes.Slice(2, 2)) + ":" + Ascii(bytes.Slice(4, 2));
                if (bytes.Length == 6)
                    return time + "+00:00";
                if ((bytes[6] != (byte)'+' && bytes[6] != (byte)'-') || !Digits(bytes.Slice(7, 4)))
                    return null;
                return time + (char)bytes[6] + Ascii(bytes.Slice(7, 2)) + ":" + Ascii(bytes.Slice(9, 2));
            default:
                return null;
        }
    }

    // Typed date/time forms are ASCII digits; reject high-bit bytes rather than normalizing them.
    private static bool Digits(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if (b < (byte)'0' || b > (byte)'9')
                return false;
        }
        return true;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);

    /// <summary>
    /// Looks up the name of a record 1 or record 2 dataset. Returns false, with a null name, for a
    /// dataset the specification does not define in those records and for every other record.
    /// </summary>
    /// <param name="key">The record and dataset number to look up.</param>
    /// <param name="name">Receives the dataset name on success; otherwise receives <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the dataset has a name in records 1 or 2; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetName(IptcDatasetKey key, [NotNullWhen(true)] out string? name)
    {
        int wanted = (key.Record << 8) | key.Dataset;
        int low = 0, high = Table.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            (byte record, byte dataset, string candidate) = Table[middle];
            int found = (record << 8) | dataset;
            if (found == wanted)
            {
                name = candidate;
                return true;
            }
            if (found < wanted) low = middle + 1;
            else high = middle - 1;
        }
        name = null;
        return false;
    }
}
