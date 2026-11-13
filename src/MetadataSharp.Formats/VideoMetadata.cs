using System.Collections.ObjectModel;
using System.Globalization;
using MetadataSharp.Core;

namespace MetadataSharp.Formats;

/// <summary>One property reported by a video-container reader.</summary>
/// <param name="Key">The key in the form the reference prints it, such as <c>Xmp.video.Width</c>.</param>
/// <param name="Value">The displayed value.</param>
/// <param name="Items">The elements when the property is a sequence; otherwise null.</param>
public sealed record VideoProperty(string Key, string Value, IReadOnlyList<string>? Items = null);

/// <summary>The ordered, read-only properties of one ASF, Matroska/WebM, QuickTime-family or RIFF file.</summary>
/// <remarks>
/// These properties are decoded from container fields. They are not an embedded RDF/XMP packet;
/// <see cref="ImageMetadata.Xmp"/> continues to mean an actual XMP packet.
/// </remarks>
public sealed class VideoMetadata
{
    private readonly IReadOnlyList<VideoProperty> properties;

    // Capture validated inputs and stable state for this video-container object.
    private VideoMetadata(MetadataFormat format, IEnumerable<VideoProperty> properties)
    {
        Format = format;
        VideoProperty[] ordered = properties
            .Select(property => property.Items is null
                ? property
                : property with { Items = Array.AsReadOnly(property.Items.ToArray()) })
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .ToArray();
        this.properties = Array.AsReadOnly(ordered);
    }

    /// <summary>The video reader selected by the file signature.</summary>
    public MetadataFormat Format { get; }

    /// <summary>The properties in the order printed by the pinned reference.</summary>
    public IReadOnlyList<VideoProperty> Properties => properties;

    /// <summary>The first property with this exact key, or null when no such property was read.</summary>
    public VideoProperty? this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            foreach (VideoProperty property in properties)
                if (string.Equals(property.Key, key, StringComparison.Ordinal)) return property;
            return null;
        }
    }

    /// <summary>Reads the supported video container from its bytes, within the supplied budgets.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidVideoContainer"/> for malformed or unsupported container structure;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when a resource budget is exceeded.
    /// </exception>
    /// <param name="data">Complete video container file bytes starting at the container signature.</param>
    /// <param name="limits">Resource budgets applied while parsing video container; null selects the default limits.</param>
    /// <returns>The parsed VideoMetadata for the complete input file.</returns>
    public static VideoMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null)
    {
        limits ??= new MetadataLimits();
        limits.Validate();
        if (data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The video file exceeds the input budget.", 0);
        MetadataFormat format = ImageMetadata.Detect(data);
        if (format is not (MetadataFormat.Asf or MetadataFormat.Matroska or MetadataFormat.QuickTime or MetadataFormat.Riff))
            throw new MetadataException(MetadataErrorCode.InvalidVideoContainer, "The data does not have a supported video-container signature.", 0);
        return ReadKnown(data, format, limits);
    }

    internal static VideoMetadata ReadKnown(ReadOnlySpan<byte> data, MetadataFormat format, MetadataLimits limits) =>
        new(format, VideoContainerReader.Read(data, format, limits));

    // Keep TryDetect focused on video-container and its format-specific validation contract.
    internal static bool TryDetect(ReadOnlySpan<byte> data, out MetadataFormat format)
    {
        format = MetadataFormat.Unknown;
        if (data.Length >= 16 && data[..16].SequenceEqual(AsfHeader))
        {
            format = MetadataFormat.Asf;
            return true;
        }
        if (data.Length >= 4 && data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3)
        {
            format = MetadataFormat.Matroska;
            return true;
        }
        if (data.Length >= 12 && QuickTimeBoxTypes.Contains(Ascii(data.Slice(4, 4))) && QuickTimeBrands.Contains(Ascii(data.Slice(8, 4))))
        {
            format = MetadataFormat.QuickTime;
            return true;
        }
        if (data.Length >= 4 && data[..4].SequenceEqual("RIFF"u8))
        {
            format = MetadataFormat.Riff;
            return true;
        }
        return false;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes) => System.Text.Encoding.Latin1.GetString(bytes);

    private static readonly byte[] AsfHeader = [
        0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
        0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];

    private static readonly HashSet<string> QuickTimeBoxTypes = new(StringComparer.Ordinal)
    {
        "PICT", "free", "ftyp", "junk", "mdat", "moov", "pict", "pnot", "skip", "uuid", "wide"
    };

    // File-type vocabulary entries recognized by the pinned reader; this is a flat membership set,
    // not a copy of its label table. The labels exposed as values are maintained separately by our reader.
    private static readonly HashSet<string> QuickTimeBrands = new(StringComparer.Ordinal)
    {
        "3g2a", "3g2b", "3g2c", "3ge6", "3ge7", "3gg6", "3gp1", "3gp2", "3gp3", "3gp4", "3gp5", "3gp6", "3gs7",
        "CAEP", "CDes", "F4A ", "F4B ", "F4P ", "F4V ", "JP2 ", "JP20", "KDDI", "M4A ", "M4B ", "M4P ", "M4V ",
        "M4VH", "M4VP", "MPPI", "MSNV", "NDAS", "NDSC", "NDSH", "NDSM", "NDSP", "NDSS", "NDXC", "NDXH", "NDXM",
        "NDXP", "NDXS", "NIKO", "ROSS", "avc1", "caqv", "da0a", "da0b", "da1a", "da1b", "da2a", "da2b", "da3a",
        "da3b", "dmb1", "dmpf", "drc1", "dv1a", "dv1b", "dv2a", "dv2b", "dv3a", "dv3b", "dvr1", "dvt1", "isc2",
        "iso2", "isom", "jpm ", "jpx ", "mj2s", "mjp2", "mmp4", "mp21", "mp41", "mp42", "mp71", "mqt ", "niko",
        "odcf", "opf2", "opx2", "pana", "qt  ", "sdv ", "ssc1", "ssc2",
        "FFMP", "appl", "olym", "GIC ", "fe20", "KMPI", "kdak", "pent", "leic", "pr01", "SMI ", "mino", "sany"
    };
}
