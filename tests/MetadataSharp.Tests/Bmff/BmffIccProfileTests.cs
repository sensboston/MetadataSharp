using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Tests.Containers;
using Xunit;

namespace MetadataSharp.Tests.Bmff;

/// <summary>
/// Writing the ICC profile of HEIC, HEIF and AVIF files: a colr property of the primary item is
/// replaced, added or turned into free space, and no item moves. Every case finds the properties
/// and the items of the output with a reader of boxes that shares nothing with production code.
/// </summary>
public sealed class BmffIccProfileTests
{
    private static readonly XmpPropertyName Label = new("http://ns.adobe.com/xap/1.0/", "Label");
    private static readonly XmpPropertyName Tool = new("http://ns.adobe.com/xap/1.0/", "CreatorTool");

    public static TheoryData<string> AllLayouts()
    {
        var data = new TheoryData<string>();
        foreach (string name in BmffFixture.Names) data.Add(name);
        return data;
    }

    private static byte[] Profile(int size, int seed = 1) => IccProfileTests.Profile(size, seed);

    private static byte[] Save(byte[] file, byte[]? profile, BmffReadOptions? options = null)
    {
        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(file), options);
        using var output = new MemoryStream();
        document.SaveIccProfile(output, profile);
        return output.ToArray();
    }

    /// <summary>The refusal of a save, and the proof that it left the destination empty.</summary>
    private static MetadataErrorCode Refusal(byte[] file, byte[]? profile, BmffReadOptions? options = null)
    {
        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(file), options);
        using var output = new MemoryStream();
        MetadataException refusal = Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, profile));
        Assert.Equal(0, output.Length);
        return refusal.Code;
    }

    /// <summary>
    /// A layout whose primary item has a profile: the property container holds the extents of the
    /// image and the profile, and the association box gives the image both.
    /// </summary>
    private static BmffFixture WithProfile(string name, byte[] profile, string kind = "prof")
    {
        BmffFixture.Layout layout = BmffFixture.LayoutOf(name, false);
        layout.Properties.Add(BmffFixture.Colour(kind, profile));
        layout.Associations[0] = (BmffFixture.ImageId, new[] { (true, 1), (false, 2) });
        return BmffFixture.Build(name, false, layout);
    }

    private static byte[] Bytes(byte[] file, BmffFixture.BoxInfo box) => file[box.Start..box.End];

    private static BmffFixture.BoxInfo Child(byte[] file, string type) => BmffFixture.MetaChildren(file).Single(box => box.Type == type);

    private static string TopLevel(byte[] file) => BmffFixture.Types(BmffFixture.TopLevel(file));

    private static string Children(byte[] file) => BmffFixture.Types(BmffFixture.MetaChildren(file));

    /// <summary>
    /// Every byte of the source is in the output where it was, except inside the given ranges, and
    /// inside each of them something did change.
    /// </summary>
    private static void AssertSourceKeptExcept(byte[] source, byte[] output, params (int Start, int Length)[] allowed)
    {
        Assert.True(output.Length >= source.Length);
        var touched = new bool[allowed.Length];
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == output[i]) continue;
            int range = Array.FindIndex(allowed, a => i >= a.Start && i < a.Start + a.Length);
            Assert.True(range >= 0, $"Byte {i} of the source changed.");
            touched[range] = true;
        }
        Assert.DoesNotContain(false, touched);
    }

    /// <summary>Every item of the fixture is declared as it was and yields, through the item location box, the bytes it had.</summary>
    private static void AssertItemsKept(BmffFixture fixture, byte[] output)
    {
        Assert.Equal(BmffFixture.Declared(fixture.Bytes), BmffFixture.Declared(output));
        foreach (BmffFixture.Item item in fixture.Plan.Items)
            Assert.Equal(BmffFixture.ItemBytes(fixture.Bytes, item.Id), BmffFixture.ItemBytes(output, item.Id));
        BmffMetadata before = BmffMetadata.Read(fixture.Bytes), after = BmffMetadata.Read(output);
        Assert.Equal(before.MajorBrand, after.MajorBrand);
        Assert.Equal(before.Exif?.Entries.Count, after.Exif?.Entries.Count);
        Assert.Equal(before.Xmp?.Properties[Label].Text, after.Xmp?.Properties[Label].Text);
    }

    // ---- a profile for a file that has none ----

    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void AFileWithoutAProfileIsGivenOneAsANewPropertyOfThePrimaryItem(string name)
    {
        BmffFixture fixture = BmffFixture.Create(name, false);
        byte[] source = fixture.Bytes, profile = Profile(560);
        Assert.Null(IccProfiles.Read(source));

        byte[] output = Save(source, profile);

        Assert.Equal(profile, IccProfiles.Read(output));
        // The new property follows the last one, and the primary item is given it first, as one a
        // reader may ignore.
        Assert.Equal("ispe colr:prof", BmffFixture.PropertyTypes(output));
        Assert.Equal(BmffFixture.Colour("prof", profile), Bytes(output, BmffFixture.PropertyBoxes(output)[1]));
        Assert.Equal(Bytes(source, BmffFixture.PropertyBoxes(source)[0]), Bytes(output, BmffFixture.PropertyBoxes(output)[0]));
        Assert.Equal("1:[2,!1]", BmffFixture.Associations(output));
        AssertItemsKept(fixture, output);

        // The property box of the profile, and one byte of the association box.
        int grown = 12 + profile.Length + 1;
        BmffFixture.BoxInfo meta = BmffFixture.Meta(source);
        if (fixture.Plan.MetaLast)
        {
            // The last box of the file grows where it is.
            Assert.Equal(source.Length + grown, output.Length);
            Assert.Equal(TopLevel(source), TopLevel(output));
            Assert.Equal(source[..meta.Start], output[..meta.Start]);
            Assert.Equal(meta.Length + grown, BmffFixture.Meta(output).Length);
        }
        else
        {
            // A new meta box follows the source, and the old one is free space: four bytes of the
            // source changed, its type.
            Assert.Equal(source.Length + meta.Length + grown, output.Length);
            AssertSourceKeptExcept(source, output, (meta.Start + 4, 4));
            Assert.Equal(TopLevel(source).Replace("meta", "free") + " meta", TopLevel(output));
            Assert.Equal(source.Length, BmffFixture.Meta(output).Start);
        }
        Assert.Equal(Children(source), Children(output));
    }

    [Fact]
    public void ColourInformationThatIsNotAProfileStaysWhereItIs()
    {
        byte[] numbers = BmffFixture.Colour("nclx", [0, 1, 0, 13, 0, 6, 0x80]);
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.Properties.Add(numbers);
        layout.Associations[0] = (BmffFixture.ImageId, new[] { (true, 1), (false, 2) });
        byte[] source = BmffFixture.Build("heic", false, layout).Bytes, profile = Profile(560);

        // It is no profile: there is none to read and none to remove.
        Assert.Null(IccProfiles.Read(source));
        Assert.Equal(source, Save(source, null));

        byte[] output = Save(source, profile);
        Assert.Equal("ispe colr:nclx colr:prof", BmffFixture.PropertyTypes(output));
        Assert.Equal(numbers, Bytes(output, BmffFixture.PropertyBoxes(output)[1]));
        Assert.Equal("1:[3,!1,2]", BmffFixture.Associations(output));
        Assert.Equal(profile, IccProfiles.Read(output));

        // And it is there after the profile went.
        byte[] removed = Save(output, null);
        Assert.Equal("ispe colr:nclx free", BmffFixture.PropertyTypes(removed));
        Assert.Equal("1:[!1,2]", BmffFixture.Associations(removed));
        Assert.Null(IccProfiles.Read(removed));
    }

    [Fact]
    public void APrimaryItemWithoutAnEntryIsGivenOneInTheOrderOfTheItems()
    {
        // The association box has an entry for the EXIF item alone, which comes after the image.
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.Associations[0] = (BmffFixture.ExifId, new[] { (true, 1) });
        byte[] first = Save(BmffFixture.Build("heic", false, layout).Bytes, Profile(560));
        Assert.Equal("1:[2] 2:[!1]", BmffFixture.Associations(first));
        Assert.Equal(Profile(560), IccProfiles.Read(first));

        // The primary item is a second image, which comes after the one that has an entry.
        layout = BmffFixture.LayoutOf("heic", false);
        layout.Items.Add(new BmffFixture.Item(5, "hvc1", BmffFixture.Pattern(64, 3)));
        layout.PrimaryId = 5;
        byte[] last = Save(BmffFixture.Build("heic", false, layout).Bytes, Profile(560));
        Assert.Equal("1:[!1] 5:[2]", BmffFixture.Associations(last));
        Assert.Equal(Profile(560), IccProfiles.Read(last));
    }

    [Fact]
    public void TheWidthsOfTheAssociationBoxAreRaisedOnlyWhenTheNewAssociationNeedsIt()
    {
        // 127 properties: the next one has a number that seven bits do not hold.
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        for (int i = 0; i < 126; i++) layout.Properties.Add(BmffFixture.Box("pixi", [0, 0, 0, 0, 3, 8, 8, (byte)i]));
        layout.Associations[0] = (BmffFixture.ImageId, new[] { (true, 1), (false, 127) });
        byte[] source = BmffFixture.Build("heic", false, layout).Bytes;
        byte[] output = Save(source, Profile(560));
        Assert.Equal("wide 1:[128,!1,127]", BmffFixture.Associations(output));
        Assert.Equal(128, BmffFixture.PropertyBoxes(output).Count);
        Assert.Equal(Profile(560), IccProfiles.Read(output));
        // Removing it leaves the widths as they are.
        Assert.Equal("wide 1:[!1,127]", BmffFixture.Associations(Save(output, null)));

        // An item that 16 bits do not hold, in a box that names its items in 16 bits.
        layout = BmffFixture.LayoutOf("heif-wide", false);
        layout.Items.Add(new BmffFixture.Item(70_000, "hvc1", BmffFixture.Pattern(64, 3)));
        layout.PrimaryId = 70_000;
        output = Save(BmffFixture.Build("heif-wide", false, layout).Bytes, Profile(560));
        Assert.Equal("v1 1:[!1] 70000:[2]", BmffFixture.Associations(output));
        Assert.Equal(Profile(560), IccProfiles.Read(output));

        // A box that is wide in both already stays as it is.
        layout = BmffFixture.LayoutOf("heic", false);
        layout.WideProperties = true;
        layout.WideAssociations = true;
        output = Save(BmffFixture.Build("heic", false, layout).Bytes, Profile(560));
        Assert.Equal("v1 wide 1:[2,!1]", BmffFixture.Associations(output));
        Assert.Equal(Profile(560), IccProfiles.Read(output));
    }

    // ---- a profile for a file that has one ----

    [Theory]
    [InlineData("heic")]
    [InlineData("heic-last")]
    [InlineData("avif-idat")]
    [InlineData("heif-wide")]
    public void AProfileOfTheSameLengthIsWrittenOverTheOldOne(string name)
    {
        BmffFixture fixture = WithProfile(name, Profile(560));
        byte[] source = fixture.Bytes, profile = Profile(560, 9);
        Assert.Equal(Profile(560), IccProfiles.Read(source));

        byte[] output = Save(source, profile);

        BmffFixture.BoxInfo property = BmffFixture.PropertyBoxes(source)[1];
        Assert.Equal(source.Length, output.Length);
        AssertSourceKeptExcept(source, output, (property.Payload + 4, 560));
        Assert.Equal(profile, IccProfiles.Read(output));
        AssertItemsKept(fixture, output);
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("heic-last")]
    [InlineData("avif-idat")]
    public void AShorterProfileLeavesFreeSpaceAfterTheItemPropertiesBox(string name)
    {
        BmffFixture fixture = WithProfile(name, Profile(560));
        byte[] source = fixture.Bytes, profile = Profile(400, 9);

        byte[] output = Save(source, profile);

        // The box is rewritten in the bytes it has; no other child of the meta box moves.
        BmffFixture.BoxInfo properties = Child(source, "iprp");
        Assert.Equal(source.Length, output.Length);
        AssertSourceKeptExcept(source, output, (properties.Start, properties.Length));
        Assert.Equal(Children(source).Replace("iprp", "iprp free"), Children(output));
        BmffFixture.BoxInfo free = Child(output, "free");
        Assert.Equal(160, free.Length);
        Assert.Equal(properties.End, free.End);
        Assert.All(output[free.Payload..free.End], value => Assert.Equal(0, value));
        Assert.Equal("ispe colr:prof", BmffFixture.PropertyTypes(output));
        Assert.Equal("1:[!1,2]", BmffFixture.Associations(output));
        Assert.Equal(profile, IccProfiles.Read(output));
        AssertItemsKept(fixture, output);
    }

    [Theory]
    [InlineData("heic", 2000, "ftyp free mdat meta")]
    [InlineData("heic-last", 2000, "ftyp mdat meta")]
    [InlineData("avif-idat", 2000, "ftyp free mdat meta")]
    // Shorter by less than the eight bytes of a free box: the meta box cannot keep its length.
    [InlineData("heic", 556, "ftyp free mdat meta")]
    [InlineData("heic-last", 556, "ftyp mdat free meta")]
    public void AProfileThatDoesNotFitTheOldOneChangesTheLengthOfTheMetaBox(string name, int size, string boxes)
    {
        BmffFixture fixture = WithProfile(name, Profile(560));
        byte[] source = fixture.Bytes, profile = Profile(size, 9);

        byte[] output = Save(source, profile);

        Assert.Equal(boxes, TopLevel(output));
        BmffFixture.BoxInfo meta = BmffFixture.Meta(source);
        Assert.Equal(meta.Length + size - 560, BmffFixture.Meta(output).Length);
        if (boxes == TopLevel(source))
        {
            Assert.Equal(source.Length + size - 560, output.Length);
            Assert.Equal(source[..meta.Start], output[..meta.Start]);
        }
        else
        {
            // The old meta box is free space and holds the old profile still.
            Assert.Equal(source.Length + meta.Length + size - 560, output.Length);
            AssertSourceKeptExcept(source, output, (meta.Start + 4, 4));
        }
        Assert.Equal("ispe colr:prof", BmffFixture.PropertyTypes(output));
        Assert.Equal("1:[!1,2]", BmffFixture.Associations(output));
        Assert.Equal(Children(source), Children(output));
        Assert.Equal(profile, IccProfiles.Read(output));
        AssertItemsKept(fixture, output);
    }

    [Fact]
    public void ARestrictedProfileIsReplacedByAnUnrestrictedOne()
    {
        BmffFixture fixture = WithProfile("heic", Profile(560), "rICC");
        Assert.Equal("ispe colr:rICC", BmffFixture.PropertyTypes(fixture.Bytes));
        // The file holds exactly these bytes as its profile, whatever the kind says.
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, Profile(560)));

        byte[] output = Save(fixture.Bytes, Profile(560, 9));
        Assert.Equal("ispe colr:prof", BmffFixture.PropertyTypes(output));
        Assert.Equal(Profile(560, 9), IccProfiles.Read(output));
        Assert.Equal(fixture.Bytes.Length, output.Length);
    }

    [Fact]
    public void AProfileThatSeveralItemsShareIsReplacedAndRemovedForAllOfThem()
    {
        // As a camera writes it: the profile is the first property, and the image and a second
        // one, a tile or a thumbnail, are both given it.
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.Items.Add(new BmffFixture.Item(5, "hvc1", BmffFixture.Pattern(64, 3)));
        layout.Properties.Insert(0, BmffFixture.Colour("prof", Profile(560)));
        layout.Associations.Clear();
        layout.Associations.Add((BmffFixture.ImageId, new[] { (true, 1), (true, 2) }));
        layout.Associations.Add((5, new[] { (true, 2), (true, 1) }));
        BmffFixture fixture = BmffFixture.Build("heic", false, layout);

        byte[] replaced = Save(fixture.Bytes, Profile(2000, 9));
        Assert.Equal("colr:prof ispe", BmffFixture.PropertyTypes(replaced));
        Assert.Equal("1:[!1,!2] 5:[!2,!1]", BmffFixture.Associations(replaced));
        Assert.Equal(Profile(2000, 9), IccProfiles.Read(replaced));
        AssertItemsKept(fixture, replaced);

        byte[] removed = Save(fixture.Bytes, null);
        Assert.Equal("free ispe", BmffFixture.PropertyTypes(removed));
        Assert.Equal("1:[!2] 5:[!2]", BmffFixture.Associations(removed));
        Assert.Null(IccProfiles.Read(removed));
        AssertItemsKept(fixture, removed);
    }

    // ---- no profile ----

    [Theory]
    [InlineData("heic")]
    [InlineData("heic-last")]
    [InlineData("avif-idat")]
    [InlineData("heif-wide")]
    public void RemovingTheProfileLeavesFreeSpaceInItsPlaceAndGivesItToNoItem(string name)
    {
        byte[] old = Profile(560);
        BmffFixture fixture = WithProfile(name, old);
        byte[] source = fixture.Bytes;

        byte[] output = Save(source, null);

        Assert.Null(IccProfiles.Read(output));
        Assert.Equal("ispe free", BmffFixture.PropertyTypes(output));
        Assert.Equal(8, BmffFixture.PropertyBoxes(output)[1].Length);
        Assert.Equal("1:[!1]", BmffFixture.Associations(output));
        // The item properties box is rewritten in the bytes it has, and what it lost is cleared:
        // the profile is in the file no more.
        BmffFixture.BoxInfo properties = Child(source, "iprp");
        Assert.Equal(source.Length, output.Length);
        AssertSourceKeptExcept(source, output, (properties.Start, properties.Length));
        Assert.Equal(Children(source).Replace("iprp", "iprp free"), Children(output));
        Assert.Equal(565, Child(output, "free").Length);
        Assert.True(new ReadOnlySpan<byte>(source).IndexOf(new ReadOnlySpan<byte>(old, 4, 64)) >= 0);
        Assert.True(new ReadOnlySpan<byte>(output).IndexOf(new ReadOnlySpan<byte>(old, 4, 64)) < 0);
        AssertItemsKept(fixture, output);

        // A profile for the file again: a new property, after the free space.
        byte[] again = Save(output, Profile(300, 5));
        Assert.Equal("ispe free colr:prof", BmffFixture.PropertyTypes(again));
        Assert.Equal("1:[3,!1]", BmffFixture.Associations(again));
        Assert.Equal(Profile(300, 5), IccProfiles.Read(again));
        AssertItemsKept(fixture, again);
    }

    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void AFileThatIsTheResultAlreadyIsCopiedAsItIs(string name)
    {
        // No profile to remove.
        BmffFixture bare = BmffFixture.Create(name, false);
        Assert.Equal(bare.Bytes, Save(bare.Bytes, null));

        // The profile it has.
        BmffFixture fixture = WithProfile(name, Profile(560));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, Profile(560)));

        // And the output of a save, saved again.
        byte[] output = Save(bare.Bytes, Profile(700, 3));
        Assert.Equal(output, Save(output, Profile(700, 3)));
    }

    [Fact]
    public void TheOutputOfAProfileSaveTakesTheOtherEdits()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        byte[] output = Save(fixture.Bytes, Profile(560));

        var edit = new XmpEdit();
        edit.Set(Tool, XmpValue.CreateText("MetadataSharp"));
        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(output));
        using var edited = new MemoryStream();
        document.SaveXmp(edited, edit);

        byte[] bytes = edited.ToArray();
        Assert.Equal("MetadataSharp", BmffMetadata.Read(bytes).Xmp!.Properties[Tool].Text);
        Assert.Equal(Profile(560), IccProfiles.Read(bytes));
        // And the other way round: a profile for a file whose XMP item was appended.
        byte[] both = Save(bytes, Profile(900, 4));
        Assert.Equal("MetadataSharp", BmffMetadata.Read(both).Xmp!.Properties[Tool].Text);
        Assert.Equal(Profile(900, 4), IccProfiles.Read(both));
    }

    // ---- refusals ----

    [Fact]
    public void AProfileThatIsNotOneIsRefusedWhateverTheFile()
    {
        byte[] source = BmffFixture.Create("heic", false).Bytes;
        byte[] padded = [.. Profile(560), 0, 0];
        byte[] unsigned = Profile(560);
        unsigned[36] = (byte)'x';

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(source, Profile(560)[..100]));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(source, padded));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(source, unsigned));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(source, []));
    }

    [Fact]
    public void TheBudgetsBoundTheProfileAndTheOutput()
    {
        byte[] source = BmffFixture.Create("heic-bare", false).Bytes;
        // A value of the default limits of the library is what a JPEG segment holds; the limits of
        // a BMFF document are those of a whole file unless others are given.
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Refusal(source, Profile(70_000), new BmffReadOptions { Limits = new MetadataLimits() }));
        Assert.Equal(Profile(70_000), IccProfiles.Read(Save(source, Profile(70_000))));

        // The meta box of this file cannot grow where it is: a new one would pass the input budget.
        BmffFixture fixture = WithProfile("heic", Profile(560));
        var options = new BmffReadOptions { Limits = new MetadataLimits { MaxInputBytes = fixture.Bytes.Length + 16 } };
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(fixture.Bytes, Profile(600), options));
        Assert.Equal(fixture.Bytes.Length, Save(fixture.Bytes, Profile(560, 9), options).Length);
        Assert.Equal(fixture.Bytes.Length, Save(fixture.Bytes, null, options).Length);
    }

    [Fact]
    public void TwoProfilesOfThePrimaryItemAreNotChosenBetween()
    {
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.Properties.Add(BmffFixture.Colour("prof", Profile(560)));
        layout.Properties.Add(BmffFixture.Colour("rICC", Profile(300, 2)));
        layout.Associations[0] = (BmffFixture.ImageId, new[] { (true, 1), (false, 2), (false, 3) });
        byte[] source = BmffFixture.Build("heic", false, layout).Bytes;

        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => IccProfiles.Read(source)).Code);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Refusal(source, Profile(560)));
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Refusal(source, null));

        // The same property given twice is one profile.
        layout = BmffFixture.LayoutOf("heic", false);
        layout.Properties.Add(BmffFixture.Colour("prof", Profile(560)));
        layout.Associations[0] = (BmffFixture.ImageId, new[] { (true, 1), (false, 2), (false, 2) });
        byte[] twice = BmffFixture.Build("heic", false, layout).Bytes;
        byte[] removed = Save(twice, null);
        Assert.Equal("1:[!1]", BmffFixture.Associations(removed));
        Assert.Null(IccProfiles.Read(removed));
    }

    [Fact]
    public void AFileWithoutAPlaceForAProfileIsNotGivenOneAndIsCopiedWhenItIsToHaveNone()
    {
        // No primary item.
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic-bare", false);
        layout.Primary = false;
        byte[] source = BmffFixture.Build("heic-bare", false, layout).Bytes;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(source, Profile(560)));
        Assert.Equal(source, Save(source, null));

        // No item properties box.
        layout = BmffFixture.LayoutOf("heic-bare", false);
        layout.NoProperties = true;
        source = BmffFixture.Build("heic-bare", false, layout).Bytes;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(source, Profile(560)));
        Assert.Equal(source, Save(source, null));

        // No meta box: the file type box alone, and data.
        source = BmffFixture.Concat(
            BmffFixture.Box("ftyp", BmffFixture.Ascii("heic"), BmffFixture.U32(0), BmffFixture.Ascii("mif1heic")),
            BmffFixture.Box("mdat", BmffFixture.Pattern(32, 1)));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(source, Profile(560)));
        Assert.Equal(source, Save(source, null));
    }

    [Fact]
    public void ItemPropertiesTheWriterCannotAccountForAreRefused()
    {
        // A second association box, which the format allows and the reader of profiles does not read.
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.AfterAssociations = BmffFixture.Box("ipma", BmffFixture.U32(1), BmffFixture.U32(0));
        byte[] source = BmffFixture.Build("heic", false, layout).Bytes;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(source, Profile(560)));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(source, null));

        // An association box that does not end with its last entry: it says it has none.
        source = WithProfile("heic", Profile(560)).Bytes;
        BmffFixture.BoxInfo associations = BmffFixture.PropertyChildren(source).Single(box => box.Type == "ipma");
        source[associations.Payload + 7] = 0;
        Assert.Equal(MetadataErrorCode.InvalidBmff, Refusal(source, Profile(560, 9)));
        Assert.Equal(MetadataErrorCode.InvalidBmff, Refusal(source, null));

        // An association with a property the container does not hold: a new one would take its number.
        layout = BmffFixture.LayoutOf("heic", false);
        layout.Associations[0] = (BmffFixture.ImageId, new[] { (true, 1), (false, 2) });
        source = BmffFixture.Build("heic", false, layout).Bytes;
        Assert.Equal(MetadataErrorCode.InvalidBmff, Refusal(source, Profile(560)));
        Assert.Equal(source, Save(source, null));

        // An entry that holds as many associations as it can count.
        layout = BmffFixture.LayoutOf("heic", false);
        layout.Associations[0] = (BmffFixture.ImageId, Enumerable.Repeat((false, 1), 255).ToArray());
        source = BmffFixture.Build("heic", false, layout).Bytes;
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(source, Profile(560)));
    }

    [Fact]
    public void AMetaBoxThatCannotChangeItsLengthTakesWhatFitsTheBytesItHas()
    {
        // A top-level box of an unknown kind: nothing is appended to such a file.
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.After = BmffFixture.Box("abcd", BmffFixture.Pattern(24, 3));
        layout.Properties.Add(BmffFixture.Colour("prof", Profile(560)));
        layout.Associations[0] = (BmffFixture.ImageId, new[] { (true, 1), (false, 2) });
        byte[] unknown = BmffFixture.Build("heic", false, layout).Bytes;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(unknown, Profile(600)));
        Assert.Equal(Profile(560, 9), IccProfiles.Read(Save(unknown, Profile(560, 9))));
        Assert.Equal(Profile(300, 9), IccProfiles.Read(Save(unknown, Profile(300, 9))));
        Assert.Null(IccProfiles.Read(Save(unknown, null)));

        // A media data box that runs to the end of the file: nothing can follow it.
        layout = BmffFixture.LayoutOf("heic", false);
        layout.OpenData = true;
        layout.Properties.Add(BmffFixture.Colour("prof", Profile(560)));
        layout.Associations[0] = (BmffFixture.ImageId, new[] { (true, 1), (false, 2) });
        byte[] open = BmffFixture.Build("heic", false, layout).Bytes;
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Refusal(open, Profile(600)));
        Assert.Equal(Profile(560, 9), IccProfiles.Read(Save(open, Profile(560, 9))));
        Assert.Null(IccProfiles.Read(Save(open, null)));
    }

    [Fact]
    public void AFileTheReaderRefusesIsNotWritten()
    {
        // Two item location boxes: neither the reader nor the writer chooses between them, and
        // whether the file has a profile to remove is not looked into.
        byte[] source = WithProfile("heic", Profile(560)).Bytes;
        BmffFixture.BoxInfo meta = BmffFixture.Meta(source), locations = Child(source, "iloc");
        byte[] second = source[locations.Start..locations.End];
        byte[] grown = BmffFixture.Concat(source[..meta.End], second, source[meta.End..]);
        BinaryPrimitives.WriteUInt32BigEndian(grown.AsSpan(meta.Start), (uint)(meta.Length + second.Length));

        Assert.Equal(MetadataErrorCode.InvalidBmff, Refusal(grown, Profile(560, 9)));
        Assert.Equal(MetadataErrorCode.InvalidBmff, Refusal(grown, null));
    }

    // ---- the check of a planned output ----

    /// <summary>
    /// A planned profile for the usual layout, whose meta box is replaced by a new one after the
    /// source, and places of the output the plan has no business with: a byte of the image and one
    /// of the EXIF item, and, in the new meta box, the type of the image in the item information
    /// box, the handler, the extents of the image, the profile, and the last association.
    /// </summary>
    private static (byte[] Source, BmffFileMap Map, BmffMetadata Before, BmffColourPlan Plan, int[] Places) Planned()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        byte[] source = fixture.Bytes;
        var limits = new MetadataLimits();
        BmffFileMap map = BmffFileMap.Read(source, limits);
        BmffColourPlan plan = BmffColourPlanner.Create(source, map, Profile(560), limits)!;
        byte[] output = plan.Apply(source);
        List<BmffFixture.BoxInfo> properties = BmffFixture.PropertyBoxes(output);
        int[] places =
        [
            fixture.Positions[BmffFixture.ImageId][0].Start + 5,
            fixture.Positions[BmffFixture.ExifId][0].Start + fixture.Plan.ExifPrefix + BmffFixture.MakeAt,
            Child(output, "iinf").Start + 30,
            Child(output, "hdlr").Payload + 9,
            properties[0].Payload + 7,
            properties[1].Payload + 100,
            output.Length - 1
        ];
        return (source, map, BmffMetadata.Read(source), plan, places);
    }

    private static MetadataErrorCode Code(Action action) => Assert.Throws<MetadataException>(action).Code;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void AnOutputThatIsNotThePlanFailsVerification(int place)
    {
        (byte[] source, BmffFileMap map, BmffMetadata before, BmffColourPlan plan, int[] places) = Planned();
        var limits = new MetadataLimits();
        byte[] output = plan.Apply(source);
        BmffColourVerifier.Verify(source, map, before, output, plan, limits);

        output[places[place]] ^= 0x01;
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => BmffColourVerifier.Verify(source, map, before, output, plan, limits)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void APlanThatWritesWhereItMustNotFailsVerification(int place)
    {
        // The output is the plan, byte for byte; the plan itself changes a byte of an item, of a
        // box the edit did not name, of the profile or of an association. The checks that read the
        // output again refuse it.
        (byte[] source, BmffFileMap map, BmffMetadata before, BmffColourPlan plan, int[] places) = Planned();
        var limits = new MetadataLimits();
        List<(int Offset, byte[] Bytes)> patches = plan.Patches.ToList();
        byte[] tail = (byte[])plan.Tail.Clone();
        int at = places[place];
        if (at < source.Length) patches.Add((at, new[] { (byte)(source[at] ^ 0x01) }));
        else tail[at - source.Length] ^= 0x01;
        var wrong = new BmffColourPlan(source.Length, patches, tail, plan.Placement, plan.Primary, plan.Index, plan.Added, plan.Profile);
        byte[] output = wrong.Apply(source);
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => BmffColourVerifier.Verify(source, map, before, output, wrong, limits)));
    }

    [Fact]
    public void EachEditIsPlannedTheWayTheMetaBoxAllows()
    {
        var limits = new MetadataLimits();
        static BmffColourPlan? Plan(byte[] source, byte[]? profile, MetadataLimits limits) =>
            BmffColourPlanner.Create(source, BmffFileMap.Read(source, limits), profile, limits);

        byte[] first = BmffFixture.Create("heic", false).Bytes, last = BmffFixture.Create("heic-last", false).Bytes;
        Assert.Equal(BmffMetaPlacement.Moved, Plan(first, Profile(560), limits)!.Placement);
        Assert.Equal(BmffMetaPlacement.Extended, Plan(last, Profile(560), limits)!.Placement);
        Assert.Null(Plan(first, null, limits));

        byte[] held = WithProfile("heic", Profile(560)).Bytes, heldLast = WithProfile("heic-last", Profile(560)).Bytes;
        Assert.Null(Plan(held, Profile(560), limits));
        BmffColourPlan same = Plan(held, Profile(560, 9), limits)!;
        Assert.Equal(BmffMetaPlacement.InPlace, same.Placement);
        Assert.Empty(same.Tail);
        Assert.Equal((2, false), (same.Index, same.Added));
        Assert.Equal(BmffMetaPlacement.InPlace, Plan(held, Profile(552, 9), limits)!.Placement);
        Assert.Equal(BmffMetaPlacement.Moved, Plan(held, Profile(553, 9), limits)!.Placement);
        Assert.Equal(BmffMetaPlacement.Moved, Plan(held, Profile(561, 9), limits)!.Placement);
        Assert.Equal(BmffMetaPlacement.InPlace, Plan(held, null, limits)!.Placement);
        Assert.Equal(BmffMetaPlacement.Extended, Plan(heldLast, Profile(561, 9), limits)!.Placement);
        Assert.Equal(BmffMetaPlacement.Moved, Plan(heldLast, Profile(559, 9), limits)!.Placement);
        Assert.Equal(BmffMetaPlacement.InPlace, Plan(heldLast, null, limits)!.Placement);
    }

    // ---- the document ----

    [Fact]
    public async Task TheAsynchronousAndPathSavesWriteTheSameBytes()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        byte[] profile = Profile(560), expected = Save(fixture.Bytes, profile);
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.heic"), target = Path.Combine(directory, "target.heic");
            await File.WriteAllBytesAsync(source, fixture.Bytes);
            await using (BmffMetadataDocument document = await BmffMetadataDocument.OpenAsync(source))
            {
                using var stream = new MemoryStream();
                await document.SaveIccProfileAsync(stream, profile);
                Assert.Equal(expected, stream.ToArray());

                await document.SaveIccProfileAsAsync(target, profile);
                Assert.Equal(expected, await File.ReadAllBytesAsync(target));
                // Neither the source nor an existing file is written over.
                await Assert.ThrowsAsync<IOException>(() => document.SaveIccProfileAsAsync(source, profile));
                await Assert.ThrowsAsync<IOException>(() => document.SaveIccProfileAsAsync(target, null));
                Assert.Throws<IOException>(() => document.SaveIccProfileAs(target, profile));
                // A refused save leaves no file.
                string refused = Path.Combine(directory, "refused.heic");
                Assert.Throws<MetadataException>(() => document.SaveIccProfileAs(refused, new byte[200]));
                Assert.False(File.Exists(refused));
            }
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(source));
            Assert.Equal(2, Directory.GetFiles(directory).Length);

            using BmffMetadataDocument reopened = BmffMetadataDocument.Open(target);
            string other = Path.Combine(directory, "other.heic");
            reopened.SaveIccProfileAs(other, null);
            Assert.Null(IccProfiles.Read(File.ReadAllBytes(other)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void TheProfileIsTakenWhenTheSaveIsCalledAndTheDestinationRulesHold()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(fixture.Bytes));

        Assert.Throws<ArgumentNullException>(() => document.SaveIccProfile(null!, Profile(560)));
        using var used = new MemoryStream(new byte[] { 1, 2, 3 });
        Assert.Throws<ArgumentException>(() => document.SaveIccProfile(used, Profile(560)));
        Assert.Equal(new byte[] { 1, 2, 3 }, used.ToArray());

        // The document is as it was opened: a save does not change what the next one starts from.
        using var first = new MemoryStream();
        document.SaveIccProfile(first, Profile(560));
        using var second = new MemoryStream();
        document.SaveIccProfile(second, null);
        Assert.Equal(fixture.Bytes, second.ToArray());

        document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => document.SaveIccProfile(new MemoryStream(), Profile(560)));
    }
}
