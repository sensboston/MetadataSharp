using System.Text;
using Xunit;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Tests.Xmp;

public sealed class XmpTagsTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Exif = "http://ns.adobe.com/exif/1.0/";
    private const string Tiff = "http://ns.adobe.com/tiff/1.0/";
    private const string Iptc = "http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/";
    private const string Plus = "http://ns.useplus.org/ldf/xmp/1.0/";

    private static XmpPacket Packet(string attributes) => Packet(attributes, string.Empty);

    private static XmpPacket Packet(string attributes, string elements) => XmpPacket.Parse(Encoding.UTF8.GetBytes(
        $"<rdf:RDF xmlns:rdf='{Rdf}'><rdf:Description rdf:about='' xmlns:exif='{Exif}' xmlns:tiff='{Tiff}' " +
        $"xmlns:iptc='{Iptc}' xmlns:plus='{Plus}' xmlns:p='urn:owned:test' {attributes}>{elements}</rdf:Description></rdf:RDF>"));

    private static string? Reading(XmpPacket packet, string key)
    {
        foreach (XmpEntry entry in packet.Entries)
            if ("Xmp." + entry.Prefix + "." + entry.Path == key) return XmpTags.Interpret(entry, packet);
        return "no such entry";
    }

    [Fact]
    public void APropertyWithAnExifCounterpartReadsAsItsTag()
    {
        XmpPacket packet = Packet("exif:FNumber='5/1' tiff:Orientation='1'");
        // The conversion is the sync direction's, and the reading is the EXIF tag's own.
        Assert.Equal("F5", Reading(packet, "Xmp.exif.FNumber"));
        Assert.Equal("top, left", Reading(packet, "Xmp.tiff.Orientation"));
    }

    [Fact]
    public void APropertyWithoutACounterpartReadsAsNothing()
    {
        XmpPacket packet = Packet("p:title='a title'");
        Assert.Null(Reading(packet, "Xmp.p.title"));
    }

    [Fact]
    public void ACodeTheReferenceDoesNotNameReadsAsTheBareNumber()
    {
        // Orientation is a code: a text that names none is read as the zero it is given, in brackets.
        Assert.Equal("(0)", Reading(Packet("tiff:Orientation='sideways'"), "Xmp.tiff.Orientation"));
        Assert.Equal("(9)", Reading(Packet("tiff:Orientation='9'"), "Xmp.tiff.Orientation"));
    }

    [Fact]
    public void TheReferenceReadsADateAndAVersionItsOwnWay()
    {
        XmpPacket packet = Packet("exif:DateTimeOriginal='2002-07-13T15:58:28Z' exif:ExifVersion='0220'");
        Assert.Equal("2002:07:13 15:58:28", Reading(packet, "Xmp.exif.DateTimeOriginal"));
        Assert.Equal("2.20", Reading(packet, "Xmp.exif.ExifVersion"));
    }

    [Fact]
    public void APropertyOutsideTheReferencesTableReadsAsNothing()
    {
        // The reference prints a reading for the properties of its own table only, and a text
        // property with an EXIF counterpart is not one of them.
        Assert.Null(Reading(Packet("tiff:Make='Canon'"), "Xmp.tiff.Make"));
    }

    [Fact]
    public void APublishedCodeListNamesEachItemOfAnArray()
    {
        // The IPTC Scene and Subject Code vocabularies, item by item, and the bare code in
        // parentheses for a value the vocabulary does not name.
        XmpPacket scene = Packet(string.Empty,
            "<iptc:Scene><rdf:Bag><rdf:li>011900</rdf:li><rdf:li>900000</rdf:li></rdf:Bag></iptc:Scene>" +
            "<iptc:SubjectCode><rdf:Bag><rdf:li>04001000</rdf:li></rdf:Bag></iptc:SubjectCode>");
        Assert.Equal("Action, (900000)", Reading(scene, "Xmp.iptc.Scene"));
        Assert.Equal("agriculture", Reading(scene, "Xmp.iptc.SubjectCode"));
    }

    [Fact]
    public void APublishedCodeListNamesTheValueAPlusUrlHolds()
    {
        // A PLUS term is held as the URL of the vocabulary, and the name is the term's.
        XmpPacket packet = Packet(string.Empty,
            "<plus:ModelReleaseStatus>http://ns.useplus.org/ldf/vocab/MR-NAP</plus:ModelReleaseStatus>" +
            "<plus:MinorModelAgeDisclosure>http://ns.useplus.org/ldf/vocab/AG-A25</plus:MinorModelAgeDisclosure>");
        Assert.Equal("Not Applicable", Reading(packet, "Xmp.plus.ModelReleaseStatus"));
        Assert.Equal("Age 25 or Over", Reading(packet, "Xmp.plus.MinorModelAgeDisclosure"));
    }

    [Fact]
    public void ATimeWithoutADateIsReadWithThePacketsDate()
    {
        // The toolkit the reference parses packets with fills the day in from DateTimeOriginal, and
        // the reading is that day and the time; the property's own text is left as the packet has it.
        XmpPacket packet = Packet("exif:GPSTimeStamp='14:58:24Z' exif:DateTimeOriginal='2002-07-13T15:58:28Z'");
        Assert.Equal("2002:07:13 14:58:24", Reading(packet, "Xmp.exif.GPSTimeStamp"));
        foreach (XmpEntry entry in packet.Entries)
            if (entry.Key == "Xmp.exif.GPSTimeStamp") Assert.Equal("14:58:24Z", entry.Values[0]);
        // Without a date to take, there is nothing to complete and the time reads as it stands.
        Assert.Equal("14:58:24Z", Reading(Packet("exif:GPSTimeStamp='14:58:24Z'"), "Xmp.exif.GPSTimeStamp"));
    }
}
