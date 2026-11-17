using MetadataSharp.Core;
using MetadataSharp.Formats.Jxl;
using Xunit;
using static MetadataSharp.Tests.Cr3.Cr3Fixture;
using static MetadataSharp.Tests.Jxl.JxlFixture;

namespace MetadataSharp.Tests.Jxl;

/// <summary>The box walk of a JPEG XL file: which form the file has and where its metadata boxes are.</summary>
public sealed class JxlLayoutTests
{
    private static readonly byte[] Tiff8 = TiffBlock(true, Shorts(0x0112, 6));

    private static MetadataException Refusal(byte[] file, MetadataLimits? limits = null) =>
        Assert.Throws<MetadataException>(() => JxlReader.Locate(file, limits));

    [Fact]
    public void ABareCodestreamHasNoBoxes()
    {
        Assert.Equal(default(JxlLayout), JxlReader.Locate(Codestream()));
        Assert.Equal(default(JxlLayout), JxlReader.Locate(new byte[] { 0xFF, 0x0A }));
        // Nothing after the marker is looked at, whatever it resembles.
        Assert.Equal(default(JxlLayout), JxlReader.Locate(Join(new byte[] { 0xFF, 0x0A }, ExifBox(Tiff8), XmlBox(Packet("no")))));
        Assert.False(JxlReader.Locate(Codestream()).Container);
    }

    [Fact]
    public void TheMetadataBoxesOfAContainerAreFound()
    {
        byte[] packet = Packet("found");
        byte[] file = Container(ExifBox(Tiff8), XmlBox(packet));

        JxlLayout found = JxlReader.Locate(file);

        Assert.True(found.Container);
        Assert.Equal(new JxlBlock(First + 8, 4 + Tiff8.Length), found.Exif);
        Assert.Equal(new JxlBlock(First + 8 + 4 + Tiff8.Length + 8, packet.Length), found.Xmp);
        Assert.False(found.HasJumbf);
    }

    [Fact]
    public void TheBoxesMayComeInAnyOrderAndAfterTheCodestream()
    {
        byte[] packet = Packet("order");
        byte[] code = CodestreamBox();

        JxlLayout found = JxlReader.Locate(Boxes(code, XmlBox(packet), ExifBox(Tiff8)));

        Assert.Equal(new JxlBlock(First + code.Length + 8, packet.Length), found.Xmp);
        Assert.Equal(new JxlBlock(First + code.Length + 8 + packet.Length + 8, 4 + Tiff8.Length), found.Exif);
    }

    [Fact]
    public void AContainerWithoutMetadataBoxesHasNothingToFind()
    {
        JxlLayout found = JxlReader.Locate(Container());

        Assert.Equal(new JxlLayout(true, default, default, false), found);
        Assert.Equal(new JxlLayout(true, default, default, false), JxlReader.Locate(Boxes()));
    }

    [Fact]
    public void AJumbfBoxIsNoticedAndNotOpened()
    {
        // The payload is not a JUMBF structure, and nobody checks.
        byte[] jumbf = Box("jumb", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        Assert.True(JxlReader.Locate(Container(jumbf)).HasJumbf);
        Assert.True(JxlReader.Locate(Container(jumbf, jumbf, Box("jumb"))).HasJumbf);
        Assert.True(JxlReader.Locate(Boxes(CodestreamBox(), ExifBox(Tiff8), jumbf)).HasJumbf);
        Assert.False(JxlReader.Locate(Container(Box("jumB", new byte[4]))).HasJumbf);
    }

    [Fact]
    public void BoxesOfOtherTypesAreSteppedOver()
    {
        byte[] before = Join(Box("jxll", new byte[] { 10 }), Box("jbrd", new byte[40]), Box("jxlp", new byte[12]), Box("free", new byte[3]));
        byte[] file = Boxes(before, ExifBox(Tiff8), Box("jxlp", new byte[12]), Box("jxli", new byte[9]), Box("exif", new byte[5]), Box("XML ", new byte[5]));

        JxlLayout found = JxlReader.Locate(file);

        Assert.Equal(new JxlBlock(First + before.Length + 8, 4 + Tiff8.Length), found.Exif);
        Assert.False(found.Xmp.Present);
    }

    [Fact]
    public void WideAndOpenEndedBoxesAreWalked()
    {
        byte[] packet = Packet("wide");
        // The last box has size zero: it runs to the end of the file.
        byte[] open = Join(Be32(0), "jxlc"u8.ToArray(), Codestream());
        byte[] file = Boxes(WideBox("Exif", ExifPayload(Tiff8)), WideBox("free", new byte[5]), XmlBox(packet), open);

        JxlLayout found = JxlReader.Locate(file);

        Assert.Equal(new JxlBlock(First + 16, 4 + Tiff8.Length), found.Exif);
        Assert.Equal(new JxlBlock(First + 16 + 4 + Tiff8.Length + 16 + 5 + 8, packet.Length), found.Xmp);
        // An open-ended metadata box takes the rest of the file as its payload.
        byte[] last = Boxes(CodestreamBox(), Join(Be32(0), "xml "u8.ToArray(), packet));
        Assert.Equal(new JxlBlock(last.Length - packet.Length, packet.Length), JxlReader.Locate(last).Xmp);
    }

    [Fact]
    public void AnEmptyMetadataBoxIsStillFound()
    {
        byte[] file = Boxes(CodestreamBox(), Box("Exif"), Box("xml "));

        JxlLayout found = JxlReader.Locate(file);

        Assert.Equal(new JxlBlock(file.Length - 8, 0), found.Exif);
        Assert.Equal(new JxlBlock(file.Length, 0), found.Xmp);
        Assert.True(found.Exif.Present && found.Xmp.Present);
    }

    public static TheoryData<string, byte[]> Malformed()
    {
        byte[] exif = ExifBox(Tiff8), xml = XmlBox(Packet("twice"));
        byte[] wrong = (byte[])Signature.Clone();
        wrong[11] = 0x0B;
        return new TheoryData<string, byte[]>
        {
            { "an empty file", [] },
            { "one byte of the codestream marker", [0xFF] },
            { "another marker", [0xFF, 0xD8, 0xFF, 0xE0] },
            { "a signature cut short", Signature[..11] },
            { "a signature with another payload", Join(wrong, Brand(), CodestreamBox()) },
            { "a signature box of another size", Join(Box("JXL ", new byte[] { 0x0D, 0x0A, 0x87, 0x0A, 0 }), Brand()) },
            { "the signature alone", Signature },
            { "no signature", Join(Brand(), CodestreamBox()) },
            { "a codestream box after the signature", Join(Signature, CodestreamBox(), Brand()) },
            { "the brand of another format", Join(Signature, Brand("heic"), CodestreamBox()) },
            { "a file type box without a brand", Join(Signature, Box("ftyp", new byte[4])) },
            { "a file type box that is not whole brands", Join(Signature, Box("ftyp", "jxl "u8.ToArray(), new byte[6])) },
            { "a file type box cut short", Join(Signature, Brand()[..14]) },
            { "two Exif boxes", Container(exif, exif) },
            { "two Exif boxes around the codestream", Boxes(exif, CodestreamBox(), Box("Exif")) },
            { "two XMP boxes", Container(xml, xml) },
            { "a box header cut short", Join(Container(exif), new byte[] { 0, 0, 0, 16 }) },
            { "a box that leaves the file", Boxes(Join(Be32(64), "Exif"u8.ToArray(), new byte[8])) },
            { "a box smaller than its header", Boxes(Join(Be32(4), "free"u8.ToArray()), CodestreamBox()) },
            { "a wide box cut short", Boxes(Join(Be32(1), "jxlc"u8.ToArray(), new byte[4])) },
            { "a wide box that leaves the file", Boxes(Join(Be32(1), "jxlc"u8.ToArray(), Be32(1), Be32(0), new byte[8])) },
        };
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedFileIsRefused(string what, byte[] file)
    {
        MetadataException refusal = Refusal(file);

        Assert.True(refusal.Code == MetadataErrorCode.InvalidBmff, what);
        Assert.NotNull(refusal.SourceOffset);
        Assert.InRange(refusal.SourceOffset.Value, 0L, (long)file.Length);
    }

    [Fact]
    public void ARefusalSaysWhereInTheFile()
    {
        byte[] exif = ExifBox(Tiff8);

        Assert.Equal(0L, Refusal(Join(Brand(), CodestreamBox())).SourceOffset);
        Assert.Equal(12L, Refusal(Join(Signature, CodestreamBox())).SourceOffset);
        Assert.Equal(20L, Refusal(Join(Signature, Brand("avif"))).SourceOffset);
        Assert.Equal((long)First + exif.Length, Refusal(Container(exif, exif)).SourceOffset);
    }

    [Fact]
    public void TheBoxAndInputBudgetsApply()
    {
        // The signature, the file type, the metadata box and the codestream: four boxes.
        byte[] file = Container(ExifBox(Tiff8));
        var limits = new MetadataLimits();

        Assert.True(JxlReader.Locate(file, limits with { MaxSegments = 4 }).Exif.Present);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxSegments = 3 }).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxSegments = 1 }).Code);
        Assert.True(JxlReader.Locate(file, limits with { MaxInputBytes = file.Length }).Exif.Present);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxInputBytes = file.Length - 1 }).Code);
        // A codestream has no boxes to count, and is held to the input budget alone.
        Assert.False(JxlReader.Locate(Codestream(), limits with { MaxSegments = 1 }).Container);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(Codestream(), limits with { MaxInputBytes = 8 }).Code);
    }

    [Fact]
    public void EveryTruncationIsLocatedOrRefused()
    {
        byte[] file = Boxes(ExifBox(Tiff8), Box("jumb", new byte[9]), CodestreamBox(), XmlBox(Packet("cut")));
        JxlLayout whole = JxlReader.Locate(file);
        int located = 0;

        for (int length = 0; length < file.Length; length++)
        {
            try
            {
                JxlLayout found = JxlReader.Locate(file.AsSpan(0, length));
                located++;
                // What a shorter file holds is what the whole file holds there, or nothing yet.
                Assert.True(found.Exif == whole.Exif || !found.Exif.Present);
                Assert.False(found.Xmp.Present);
            }
            catch (MetadataException refusal)
            {
                Assert.Equal(MetadataErrorCode.InvalidBmff, refusal.Code);
            }
        }
        // A prefix is a whole file at the end of the file type box and of each box after it but the last.
        Assert.Equal(4, located);
    }
}
