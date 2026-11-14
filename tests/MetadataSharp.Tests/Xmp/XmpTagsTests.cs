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
    public void AValueTheTagCannotHoldReadsAsNothing()
    {
        // Orientation is a SHORT: the reference prints nothing of a text that is not a number.
        Assert.Null(Reading(Packet("tiff:Orientation='sideways'"), "Xmp.tiff.Orientation"));
    }
}
