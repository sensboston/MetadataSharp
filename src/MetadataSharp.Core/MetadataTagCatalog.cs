using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MetadataSharp.Core;

/// <summary>A locally authored name and brief description for one numbered Exif/TIFF or MakerNote tag.</summary>
/// <param name="Group">The Exiv2-compatible Exif group name.</param>
/// <param name="Id">The tag number in that group.</param>
/// <param name="Name">The canonical name used by this table.</param>
/// <param name="Aliases">Other names that identify the same group/tag number.</param>
/// <param name="Label">A short display label authored from the tag name.</param>
/// <param name="Description">A concise local description; no Exiv2 prose is included.</param>
public sealed record NumberedTagDefinition(
    string Group,
    ushort Id,
    string Name,
    IReadOnlyList<string> Aliases,
    string Label,
    string Description);

/// <summary>A locally authored name and brief description for one property in an XMP schema.</summary>
/// <param name="NamespaceUri">The property's namespace URI.</param>
/// <param name="Prefixes">All registered aliases of the namespace prefix.</param>
/// <param name="Name">The local property name.</param>
/// <param name="ValueType">The property's declared XMP value shape, when known.</param>
/// <param name="Label">A short display label authored from the property name.</param>
/// <param name="Description">A concise local description; no Exiv2 prose is included.</param>
public sealed record XmpTagDefinition(
    string NamespaceUri,
    IReadOnlyList<string> Prefixes,
    string Name,
    string? ValueType,
    string Label,
    string Description);

/// <summary>A registered XMP namespace, its prefix aliases, and its known properties.</summary>
/// <param name="NamespaceUri">The registered namespace URI.</param>
/// <param name="Prefixes">The preferred and accepted aliases of the URI.</param>
/// <param name="Properties">Known top-level properties, in the order recorded in this table.</param>
public sealed record XmpTagSchema(string NamespaceUri, IReadOnlyList<string> Prefixes, IReadOnlyList<XmpTagDefinition> Properties);

/// <summary>
/// The hand-maintained tag-table surface: standard Exif/TIFF and MakerNote group identifiers, XMP
/// namespace/property names, aliases, local labels and short descriptions. It is a naming catalog,
/// not a file validator; raw values remain available when no translation is defined.
/// </summary>
/// <remarks>
/// Numeric IDs, names, group identities, namespace URIs and XMP property names are factual registry
/// data pinned to Exiv2 0.28.9. The table uses this library's own JSON shape and does not include the
/// upstream labels, descriptions or source implementation. Descriptions are generated locally from
/// names, with a small hand-authored dictionary for common fields. Value translation is performed by
/// <see cref="ExifTags.Interpret(ExifTagKey, ExifValue)" />, <see cref="Iptc.IptcDatasetNames"/>,
/// and the MakerNote interpreter, not by copying text descriptions into the registry.
/// </remarks>
public static partial class MetadataTagCatalog
{
    private static readonly CatalogData Data = Load();
    private static readonly IReadOnlyList<string> GroupsValue = Array.AsReadOnly(Data.ExifGroups.Select(group => group.Name).ToArray());
    private static readonly IReadOnlyList<XmpTagSchema> SchemasValue = Array.AsReadOnly(Data.XmpSchemas.Select(ToSchema).ToArray());
    private static readonly Dictionary<string, CatalogGroup> GroupByName = Data.ExifGroups.ToDictionary(group => group.Name, StringComparer.Ordinal);
    private static readonly Dictionary<string, XmpTagSchema> SchemaByPrefix = SchemasValue
        .SelectMany(schema => schema.Prefixes.Select(prefix => (prefix, schema)))
        .GroupBy(pair => pair.prefix, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().schema, StringComparer.Ordinal);

    /// <summary>Every Exif/TIFF and MakerNote group name in the pinned registry, in source order.</summary>
    public static IReadOnlyList<string> ExifGroups => GroupsValue;

    /// <summary>Every XMP schema and its known top-level properties, including accepted prefix aliases.</summary>
    public static IReadOnlyList<XmpTagSchema> XmpSchemas => SchemasValue;

    /// <summary>The Exiv2 reference revision from which the registry identities were checked.</summary>
    public static string ReferenceVersion => Data.Reference;

    /// <summary>Every named tag of a standard Exif/TIFF or MakerNote group, sorted by ID in each group.</summary>
    public static IReadOnlyList<NumberedTagDefinition> Tags(string group)
    {
        ArgumentException.ThrowIfNullOrEmpty(group);
        if (!GroupByName.TryGetValue(group, out CatalogGroup? found)) return Array.Empty<NumberedTagDefinition>();
        return found.Definitions;
    }

    /// <summary>Looks up a named Exif/TIFF or MakerNote tag by Exiv2 group and numeric ID.</summary>
    /// <param name="group">The Exiv2-compatible group name.</param>
    /// <param name="id">The numeric tag ID.</param>
    /// <param name="definition">Receives the definition, or null if this table has no such tag.</param>
    /// <returns>True when the group and tag ID are listed.</returns>
    public static bool TryGet(string group, ushort id, out NumberedTagDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrEmpty(group);
        if (GroupByName.TryGetValue(group, out CatalogGroup? found) && found.ById.TryGetValue(id, out definition)) return true;
        definition = null;
        return false;
    }

    /// <summary>Looks up an XMP property's schema by any registered prefix alias.</summary>
    /// <param name="prefix">A prefix such as <c>dc</c> or <c>Iptc4xmpCore</c>.</param>
    /// <param name="propertyName">The local property name.</param>
    /// <param name="definition">Receives the property definition, or null if the pair is not listed.</param>
    /// <returns>True when the schema and property are listed.</returns>
    public static bool TryGetXmp(string prefix, string propertyName, out XmpTagDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        if (SchemaByPrefix.TryGetValue(prefix, out XmpTagSchema? schema))
        {
            definition = schema.Properties.FirstOrDefault(property => property.Name == propertyName);
            return definition is not null;
        }
        definition = null;
        return false;
    }

    // Load the fixed JSON resource once; malformed built-in facts fail fast rather than silently degrading names.
    private static CatalogData Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MetadataSharp.Core.tag-catalog-data.json")
            ?? throw new InvalidOperationException("The embedded metadata tag catalog is missing.");
        CatalogData data = JsonSerializer.Deserialize(stream, CatalogJsonContext.Default.CatalogData)
            ?? throw new InvalidOperationException("The embedded metadata tag catalog is empty.");
        foreach (CatalogGroup group in data.ExifGroups) group.Initialize();
        return data;
    }

    // Build per-group definitions in the library's own typed format, combining same-ID names as aliases.
    private static XmpTagSchema ToSchema(CatalogXmpSchema schema)
    {
        string[] prefixes = schema.Prefixes.Distinct(StringComparer.Ordinal).ToArray();
        var properties = schema.Properties.Select(property => new XmpTagDefinition(schema.Uri,
            Array.AsReadOnly(prefixes), property.Name, property.Type, Humanize(property.Name),
            "The " + Humanize(property.Name).ToLowerInvariant() + " property in the " + prefixes[0] + " XMP schema.")).ToArray();
        return new XmpTagSchema(schema.Uri, Array.AsReadOnly(prefixes), Array.AsReadOnly(properties));
    }

    private static string Humanize(string name)
    {
        string spaced = Regex.Replace(name.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        return spaced.Length == 0 ? name : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private sealed class CatalogData
    {
        public string Reference { get; set; } = "";
        public List<CatalogGroup> ExifGroups { get; set; } = [];
        public List<CatalogXmpSchema> XmpSchemas { get; set; } = [];
    }

    private sealed class CatalogGroup
    {
        public string Name { get; set; } = "";
        public List<CatalogTag> Tags { get; set; } = [];
        public IReadOnlyList<NumberedTagDefinition> Definitions { get; private set; } = Array.Empty<NumberedTagDefinition>();
        public Dictionary<ushort, NumberedTagDefinition> ById { get; private set; } = [];

        // Initialize a group after JSON deserialization; duplicate IDs are represented by explicit aliases.
        public void Initialize()
        {
            var definitions = new List<NumberedTagDefinition>(Tags.Count);
            var byId = new Dictionary<ushort, NumberedTagDefinition>();
            foreach (CatalogTag tag in Tags.OrderBy(tag => tag.Id))
            {
                string[] names = tag.Names.Distinct(StringComparer.Ordinal).ToArray();
                if (names.Length == 0) continue;
                string name = names[0];
                string label = Humanize(name);
                var definition = new NumberedTagDefinition(Name, checked((ushort)tag.Id), name,
                    Array.AsReadOnly(names.Skip(1).ToArray()), label, Describe(Name, name, label));
                definitions.Add(definition);
                byId[definition.Id] = definition;
            }
            Definitions = Array.AsReadOnly(definitions.ToArray());
            ById = byId;
        }
    }

    private sealed class CatalogTag
    {
        public uint Id { get; set; }
        public List<string> Names { get; set; } = [];
    }

    private sealed class CatalogXmpSchema
    {
        public string Uri { get; set; } = "";
        public List<string> Prefixes { get; set; } = [];
        public List<CatalogXmpProperty> Properties { get; set; } = [];
    }

    private sealed class CatalogXmpProperty
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(CatalogData))]
    private partial class CatalogJsonContext : JsonSerializerContext
    {
    }

    // Brief local summaries: these are authored here and intentionally not copied from upstream prose.
    private static string Describe(string group, string name, string label)
    {
        string key = group + "." + name;
        string? known = key switch
        {
            "Image.ImageWidth" or "Image.ImageLength" or "Thumbnail.ImageWidth" or "Thumbnail.ImageLength" => "The dimensions of the image in pixels.",
            "Image.Orientation" or "Thumbnail.Orientation" => "The arrangement of rows and columns used to display the image.",
            "Image.Make" => "The manufacturer of the image-capture device.",
            "Image.Model" => "The model name or number of the image-capture device.",
            "Photo.ExposureTime" => "The exposure duration, in seconds.",
            "Photo.FNumber" => "The lens aperture expressed as an f-number.",
            "Photo.ISOSpeedRatings" => "The camera's ISO speed rating.",
            "Photo.DateTimeOriginal" => "The date and time when the original image was captured.",
            "Photo.FocalLength" => "The physical focal length of the lens, in millimetres.",
            "Photo.UserComment" => "A user-supplied comment with an explicit character-code prefix.",
            "GPSInfo.GPSLatitude" => "The latitude coordinate recorded by the GPS receiver.",
            "GPSInfo.GPSLongitude" => "The longitude coordinate recorded by the GPS receiver.",
            "GPSInfo.GPSAltitude" => "The altitude above or below sea level, in metres.",
            _ => null,
        };
        return known ?? "The " + label.ToLowerInvariant() + " value recorded in the " + group + " metadata group.";
    }
}
