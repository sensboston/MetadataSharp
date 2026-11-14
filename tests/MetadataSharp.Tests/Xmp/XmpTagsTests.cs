using System.Text;
using Xunit;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Tests.Xmp;

public sealed class XmpTagsTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Exif = "http://ns.adobe.com/exif/1.0/";
    private const string Tiff = "http://ns.adobe.com/tiff/1.0/";

    private static XmpPacket Packet(string attributes) => XmpPacket.Parse(Encoding.UTF8.GetBytes(
        $"<rdf:RDF xmlns:rdf='{Rdf}'><rdf:Description rdf:about='' xmlns:exif='{Exif}' xmlns:tiff='{Tiff}' " +
        $"xmlns:p='urn:owned:test' {attributes}/></rdf:RDF>"));

    private static string? Reading(XmpPacket packet, string key)
    {
        foreach (XmpEntry entry in packet.Entries)
            if ("Xmp." + entry.Prefix + "." + entry.Path == key) return XmpTags.Interpret(entry);
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
}
