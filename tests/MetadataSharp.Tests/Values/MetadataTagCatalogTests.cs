using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using Xunit;

namespace MetadataSharp.Tests.Values;

public sealed class MetadataTagCatalogTests
{
    [Fact]
    public void ExifAndMakerNoteTablesContainUniqueNamesAndNumericIds()
    {
        Assert.Equal("Exiv2 0.28.9 (b0859fc61fea5dedfc898aece6b15893ddf4314e)", MetadataTagCatalog.ReferenceVersion);
        Assert.True(MetadataTagCatalog.ExifGroups.Count >= 120);
        Assert.Contains("Image", MetadataTagCatalog.ExifGroups);
        Assert.Contains("Photo", MetadataTagCatalog.ExifGroups);
        Assert.Contains("Canon", MetadataTagCatalog.ExifGroups);
        Assert.Contains("CanonCs", MetadataTagCatalog.ExifGroups);
        Assert.Contains("Nikon3", MetadataTagCatalog.ExifGroups);
        Assert.Contains("NikonLd4", MetadataTagCatalog.ExifGroups);
        Assert.Contains("OlympusEq", MetadataTagCatalog.ExifGroups);
        Assert.Contains("SonyMisc3c", MetadataTagCatalog.ExifGroups);

        foreach (string group in MetadataTagCatalog.ExifGroups)
        {
            IReadOnlyList<NumberedTagDefinition> tags = MetadataTagCatalog.Tags(group);
            Assert.Equal(tags.Count, tags.Select(tag => tag.Id).Distinct().Count());
            Assert.All(tags, tag =>
            {
                Assert.NotEmpty(tag.Name);
                Assert.NotEmpty(tag.Label);
                Assert.NotEmpty(tag.Description);
            });
        }

        Assert.True(MetadataTagCatalog.TryGet("Image", 0x0100, out NumberedTagDefinition? width));
        Assert.Equal("ImageWidth", width!.Name);
        Assert.Contains("pixels", width.Description);
        Assert.True(MetadataTagCatalog.TryGet("Canon", 0x0001, out NumberedTagDefinition? settings));
        Assert.Contains("Camera", settings!.Name);
        Assert.False(MetadataTagCatalog.TryGet("NoSuchGroup", 0x0100, out _));
    }

    [Fact]
    public void XmpPropertyNamesAndPrefixAliasesAreQueryable()
    {
        Assert.True(MetadataTagCatalog.TryGetXmp("dc", "title", out XmpTagDefinition? title));
        Assert.Equal("http://purl.org/dc/elements/1.1/", title!.NamespaceUri);
        Assert.Contains("title", title.Description);
        Assert.True(MetadataTagCatalog.TryGetXmp("Iptc4xmpCore", "Location", out _));
        Assert.True(MetadataTagCatalog.XmpSchemas.Count >= 30);
        Assert.True(MetadataTagCatalog.XmpSchemas.Sum(schema => schema.Properties.Count) > 1000);
    }

    [Fact]
    public void IptcDatasetDefinitionsHaveLocalLabelsAliasesDescriptionsAndEnumMeanings()
    {
        Assert.All(IptcDatasets.All, definition =>
        {
            Assert.NotEmpty(definition.Label);
            Assert.NotEmpty(definition.Description);
        });
        Assert.Contains("CaptionAbstract", IptcDatasets.All.Single(item => item.Key == new IptcDatasetKey(2, 120)).Aliases);
        Assert.Contains(new IptcDatasetValueMeaning("02", "Object replace"),
            IptcDatasets.All.Single(item => item.Key == new IptcDatasetKey(2, 42)).ValueMeanings);
    }
}
