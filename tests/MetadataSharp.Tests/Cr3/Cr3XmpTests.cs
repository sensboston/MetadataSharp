using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Cr3;
using Xunit;
using static MetadataSharp.Tests.Cr3.Cr3Fixture;

namespace MetadataSharp.Tests.Cr3;

/// <summary>The XMP box: a packet as the payload of a top-level box of its own.</summary>
public sealed class Cr3XmpTests
{
    private static readonly XmpPropertyName Title = new("urn:owned:test", "title");
    private static readonly byte[] Data = Box("mdat", new byte[16]);

    private static MetadataErrorCode Refusal(byte[] file, MetadataLimits? limits = null) =>
        Assert.Throws<MetadataException>(() => Cr3Metadata.Read(file, limits)).Code;

    private static string? TitleOf(byte[] file)
    {
        XmpPacket? packet = Cr3Metadata.Read(file).Xmp;
        Assert.NotNull(packet);
        return packet.Properties[Title].Text;
    }

    [Fact]
    public void ThePacketOfTheXmpBoxIsRead()
    {
        var plan = new Layout();
        Cr3Metadata metadata = Cr3Metadata.Read(Create(plan).Bytes);

        Assert.NotNull(metadata.Xmp);
        Assert.True(metadata.Xmp.IsEditable);
        Assert.Equal("cr3", metadata.Xmp.Properties[Title].Text);
        // The packet is kept as the box holds it, wrapper and padding included.
        var raw = new byte[metadata.Xmp.ByteLength];
        metadata.Xmp.CopyRawTo(raw);
        Assert.Equal(plan.Xmp, raw);
        Assert.NotNull(metadata.Exif);
    }

    [Fact]
    public void AFileWithoutAnXmpBoxHasNoPacket()
    {
        Cr3Metadata metadata = Cr3Metadata.Read(Create(new Layout { Xmp = null }).Bytes);

        Assert.Null(metadata.Xmp);
        Assert.NotNull(metadata.Exif);
    }

    [Fact]
    public void TheBoxMayStandAnywhereAtTheTopLevelAndNeedsNoDirectories()
    {
        byte[] box = Uuid(XmpBox, Packet("alone"));
        byte[] movie = Box("moov", Box("mvhd", new byte[100]));

        Assert.Equal("alone", TitleOf(Join(FileType(), box)));
        Assert.Equal("alone", TitleOf(Join(FileType(), box, movie, Data)));
        Assert.Equal("alone", TitleOf(Join(FileType(), movie, Data, box)));
        Assert.Null(Cr3Metadata.Read(Join(FileType(), box)).Exif);
        // A box of another identifier is not the packet, whatever it holds.
        Assert.Null(Cr3Metadata.Read(Join(FileType(), Uuid(PreviewBox, Packet("other")), Data)).Xmp);
    }

    [Fact]
    public void ZeroBytesAfterThePacketAreNotPartOfIt()
    {
        byte[] padded = Join(Packet("slot"), new byte[512]);

        Assert.Equal("slot", TitleOf(Join(FileType(), Uuid(XmpBox, padded), Data)));
    }

    [Fact]
    public void APacketWithoutAWrapperIsRead()
    {
        byte[] bare = Encoding.UTF8.GetBytes(
            $"<rdf:RDF xmlns:rdf='{Rdf}' xmlns:t='urn:owned:test'><rdf:Description rdf:about=''><t:title>bare</t:title></rdf:Description></rdf:RDF>");

        Assert.Equal("bare", TitleOf(Join(FileType(), Uuid(XmpBox, bare))));
    }

    [Fact]
    public void ABoxThatHoldsNoPacketIsRefused()
    {
        Assert.Equal(MetadataErrorCode.InvalidXmp, Refusal(Join(FileType(), Uuid(XmpBox), Data)));
        Assert.Equal(MetadataErrorCode.InvalidXmp, Refusal(Join(FileType(), Uuid(XmpBox, new byte[64]), Data)));
        Assert.Equal(MetadataErrorCode.InvalidXmp, Refusal(Join(FileType(), Uuid(XmpBox, "<x:xmpmeta xmlns:x='adobe:ns:meta/'/>"u8.ToArray()))));
        Assert.Equal(MetadataErrorCode.InvalidXmp, Refusal(Join(FileType(), Uuid(XmpBox, Packet("cut")[..^100]))));
        Assert.Equal(MetadataErrorCode.InvalidXmp, Refusal(Join(FileType(), Uuid(XmpBox, Join(new byte[] { 0xFF, 0xFE }, Packet("mark"))))));
    }

    [Fact]
    public void TheRetainedBudgetBoundsThePacketWithTheDirectories()
    {
        var plan = new Layout();
        byte[] file = Create(plan).Bytes;
        var limits = new MetadataLimits();
        Cr3Metadata whole = Cr3Metadata.Read(file, limits);
        Assert.NotNull(whole.Exif);
        int directories = plan.Ifd0!.Length + plan.Exif!.Length + plan.Gps!.Length + plan.MakerNote!.Length +
            whole.Exif.Entries.Sum(entry => entry.Value.ByteLength);
        int needed = directories + plan.Xmp!.Length;

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxRetainedMetadataBytes = needed - 1 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxRetainedMetadataBytes = directories }));
        Cr3Metadata tight = Cr3Metadata.Read(file, limits with { MaxRetainedMetadataBytes = needed });
        Assert.NotNull(tight.Xmp);
        Assert.NotNull(tight.Exif);
        // The packet is counted before the MakerNote is decoded, so a tight budget costs the decoding and not the packet.
        Assert.Null(tight.Exif.MakerNote);
        Assert.NotNull(whole.Exif.MakerNote);

        byte[] alone = Join(FileType(), Uuid(XmpBox, plan.Xmp));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(alone, limits with { MaxRetainedMetadataBytes = plan.Xmp.Length - 1 }));
        Assert.NotNull(Cr3Metadata.Read(alone, limits with { MaxRetainedMetadataBytes = plan.Xmp.Length }).Xmp);
    }

    [Fact]
    public void ThePacketBudgetOfTheXmpReaderApplies()
    {
        byte[] packet = Packet("budget");
        byte[] file = Join(FileType(), Uuid(XmpBox, packet), Data);
        XmpReadOptions Options(int bytes) => new() { Limits = new XmpLimits { MaxPacketBytes = bytes } };

        Assert.NotNull(Cr3Reader.Read(file, null, Options(packet.Length)).Xmp);
        MetadataException refusal = Assert.Throws<MetadataException>(() => Cr3Reader.Read(file, null, Options(packet.Length - 1)));
        Assert.Equal(MetadataErrorCode.LimitExceeded, refusal.Code);
        // The default of the XMP reader is one mebibyte: a longer packet is refused by it, not cut.
        byte[] large = Join(FileType(), Uuid(XmpBox, Join(packet, new byte[1_048_576])));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(large));
    }
}
