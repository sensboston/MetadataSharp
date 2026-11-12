using System.Text;
using static MetadataSharp.Tests.Cr3.Cr3Fixture;

namespace MetadataSharp.Tests.Jxl;

/// <summary>
/// JPEG XL files laid out by hand: the signature box, the file type box and whatever boxes a test
/// names, so that every offset an assertion uses can be read off the builder. The codestream is two
/// marker bytes and filler; no reader looks inside it.
/// </summary>
internal static class JxlFixture
{
    /// <summary>The twelve bytes of the signature box.</summary>
    internal static readonly byte[] Signature = [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    /// <summary>Where the first box after the file type box starts: 12 bytes of signature, 20 of file type.</summary>
    internal const int First = 32;

    internal static byte[] Brand(string brand = "jxl ") => Box("ftyp", Encoding.ASCII.GetBytes(brand), Be32(0), "jxl "u8.ToArray());

    /// <summary>A bare codestream: the marker and filler.</summary>
    internal static byte[] Codestream(int filler = 30) => [0xFF, 0x0A, .. new byte[filler]];

    internal static byte[] CodestreamBox() => Box("jxlc", Codestream());

    /// <summary>The signature, the file type and the given boxes, in that order. Nothing is added.</summary>
    internal static byte[] Boxes(params byte[][] boxes) => Join(Signature, Brand(), Join(boxes));

    /// <summary>A container with the given boxes before its codestream.</summary>
    internal static byte[] Container(params byte[][] boxes) => Boxes(Join(boxes), CodestreamBox());

    /// <summary>The payload of an Exif box: the length of what is skipped, what is skipped, the TIFF.</summary>
    internal static byte[] ExifPayload(byte[] tiff, params byte[][] skipped)
    {
        byte[] before = Join(skipped);
        return Join(Be32((uint)before.Length), before, tiff);
    }

    internal static byte[] ExifBox(byte[] tiff, params byte[][] skipped) => Box("Exif", ExifPayload(tiff, skipped));

    internal static byte[] XmlBox(byte[] packet) => Box("xml ", packet);

    /// <summary>
    /// A TIFF with a primary directory (Make, Model, Orientation, the pointer) and the Exif
    /// directory it points to (ExposureTime, ISO, the original date).
    /// </summary>
    internal static byte[] Block(bool little = true, string model = "Model XL")
    {
        Entry[] Primary(uint pointer) => [Text(0x010F, "Maker"), Text(0x0110, model), Shorts(0x0112, 6), Longs(0x8769, pointer)];
        int at = TiffBlock(little, 0, Primary(0)).Length;
        return Join(TiffBlock(little, 0, Primary((uint)at)),
            Ifd(little, at, 0, Rational(0x829A, 1, 250), Shorts(0x8827, 100), Text(0x9003, "2021:01:02 03:04:05")));
    }
}
