using System.Diagnostics.CodeAnalysis;

namespace MetadataSharp.Core.Iptc;

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
    /// Looks up the name of a record 1 or record 2 dataset. Returns false, with a null name, for a
    /// dataset the specification does not define in those records and for every other record.
    /// </summary>
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
