using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MetadataSharp.Core.Iptc;

namespace MetadataSharp.Core.Operations;

/// <summary>
/// The keys the Exiv2 tools print and take: <c>Exif.Photo.DateTimeOriginal</c>,
/// <c>Iptc.Application2.Keywords</c>. (An XMP key, <c>Xmp.dc.subject</c>, is the path an
/// <see cref="MetadataSharp.Core.Xmp.XmpEdit"/> takes as it is.) A tag or a dataset without a name is written by its
/// number: <c>Exif.Image.0x1234</c>, <c>Iptc.Application2.0x00c8</c>.
/// </summary>
public static class MetadataKeys
{
    private static readonly (ExifDirectoryKind Directory, string Group)[] Groups =
    [
        (ExifDirectoryKind.Ifd0, "Image"), (ExifDirectoryKind.Exif, "Photo"), (ExifDirectoryKind.Gps, "GPSInfo"),
        (ExifDirectoryKind.Interop, "Iop"), (ExifDirectoryKind.Thumbnail, "Thumbnail"),
    ];

    /// <summary>The names Exiv2 gives where it does not use the word of the EXIF standard.</summary>
    private static string? Exiv2Name(ExifTagKey key) => (key.Directory, key.TagId) switch
    {
        (ExifDirectoryKind.Ifd0 or ExifDirectoryKind.Thumbnail, 0x8769) => "ExifTag",
        (ExifDirectoryKind.Ifd0 or ExifDirectoryKind.Thumbnail, 0x8825) => "GPSTag",
        (ExifDirectoryKind.Exif, 0x8827) => "ISOSpeedRatings",
        (ExifDirectoryKind.Exif, 0xA005) => "InteroperabilityTag",
        _ => null,
    };

    /// <summary>The key of an EXIF tag: <c>Exif.</c>, the group of the directory (<c>Image</c>, <c>Photo</c>, <c>GPSInfo</c>, <c>Iop</c>, <c>Thumbnail</c>) and the name or the number of the tag.</summary>
    public static string Format(ExifTagKey key)
    {
        string group = "Image";
        foreach ((ExifDirectoryKind directory, string name) in Groups)
            if (directory == key.Directory) group = name;
        string tag = Exiv2Name(key) ?? ExifTags.Find(key)?.Name ?? "0x" + key.TagId.ToString("x4", CultureInfo.InvariantCulture);
        return "Exif." + group + "." + tag;
    }

    /// <summary>
    /// Reads an EXIF key. The tag is a name of <see cref="ExifTags"/>, a name Exiv2 uses instead
    /// (<c>ISOSpeedRatings</c>, <c>ExifTag</c>, <c>GPSTag</c>, <c>InteroperabilityTag</c>) or
    /// <c>0x</c> and four hexadecimal digits. Names are matched exactly. False for any other text,
    /// MakerNote groups included: those are not edited.
    /// </summary>
    public static bool TryParseExif([NotNullWhen(true)] string? key, out ExifTagKey result)
    {
        result = default;
        if (key is null) return false;
        string[] parts = key.Split('.');
        if (parts.Length != 3 || parts[0] != "Exif" || parts[2].Length == 0) return false;
        ExifDirectoryKind? found = null;
        foreach ((ExifDirectoryKind directory, string name) in Groups)
            if (name == parts[1]) found = directory;
        if (found is not ExifDirectoryKind kind) return false;
        string tag = parts[2];
        if (tag.Length == 6 && tag.StartsWith("0x", StringComparison.Ordinal)
            && ushort.TryParse(tag.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort number))
        {
            result = new(kind, number);
            return true;
        }
        foreach (ExifTagDefinition definition in ExifTags.Definitions(kind))
        {
            ExifTagKey candidate = new(kind, definition.Id);
            if ((Exiv2Name(candidate) ?? definition.Name) != tag && definition.Name != tag) continue;
            result = candidate;
            return true;
        }
        return false;
    }

    /// <summary>The key of an IPTC dataset: <c>Iptc.Envelope.</c> or <c>Iptc.Application2.</c> and the name or the number of the dataset; null for a record other than 1 and 2, which Exiv2 has no key for.</summary>
    public static string? Format(IptcDatasetKey key)
    {
        if (key.Record is not (1 or 2)) return null;
        string dataset = IptcDatasetNames.TryGetName(key, out string? name) ? name : "0x" + key.Dataset.ToString("x4", CultureInfo.InvariantCulture);
        return (key.Record == 1 ? "Iptc.Envelope." : "Iptc.Application2.") + dataset;
    }

    /// <summary>Reads an IPTC key: the record <c>Envelope</c> or <c>Application2</c>, then a name of <see cref="IptcDatasetNames"/> or <c>0x</c> and four hexadecimal digits below 0x0100.</summary>
    public static bool TryParseIptc([NotNullWhen(true)] string? key, out IptcDatasetKey result)
    {
        result = default;
        if (key is null) return false;
        string[] parts = key.Split('.');
        if (parts.Length != 3 || parts[0] != "Iptc" || parts[2].Length == 0) return false;
        byte record = parts[1] switch { "Envelope" => (byte)1, "Application2" => (byte)2, _ => (byte)0 };
        if (record == 0) return false;
        string dataset = parts[2];
        if (dataset.Length == 6 && dataset.StartsWith("0x", StringComparison.Ordinal)
            && ushort.TryParse(dataset.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort number))
        {
            if (number > 255) return false;
            result = new(record, (byte)number);
            return true;
        }
        foreach ((byte tableRecord, byte tableDataset, string name) in IptcDatasetNames.Table)
        {
            if (tableRecord != record || name != dataset) continue;
            result = new(record, tableDataset);
            return true;
        }
        return false;
    }
}
