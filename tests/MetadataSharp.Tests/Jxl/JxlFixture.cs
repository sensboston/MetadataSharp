using System.IO.Compression;
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

    /// <summary>A compressed box: the type of the box it stands for, then a Brotli stream of that box's payload.</summary>
    internal static byte[] Brob(string type, params byte[][] stream) => Box("brob", Encoding.ASCII.GetBytes(type), Join(stream));

    /// <summary>
    /// A Brotli stream written by hand, so that its bytes are the same on every platform: the
    /// window bit, then meta-blocks of up to 65,536 bytes that hold the payload as it is, then an
    /// empty last meta-block. A meta-block header is three bytes: its length less one, shifted past
    /// the bits before it, and the bit that says the bytes are not compressed. The stream is the
    /// payload, 3 bytes for each meta-block and one byte at the end.
    /// </summary>
    internal static byte[] Stored(byte[] payload)
    {
        // The window bit, "last" and "last and empty": a stream of nothing.
        if (payload.Length == 0) return [0x06];
        var stream = new List<byte>(payload.Length + 8);
        for (int at = 0; at < payload.Length; at += 65_536)
        {
            int length = Math.Min(65_536, payload.Length - at);
            // The first header shares its byte with the window bit, so its fields sit one bit higher.
            int header = at == 0 ? ((length - 1) << 4) | (1 << 20) : ((length - 1) << 3) | (1 << 19);
            stream.Add((byte)header);
            stream.Add((byte)(header >> 8));
            stream.Add((byte)(header >> 16));
            stream.AddRange(payload.AsSpan(at, length));
        }
        stream.Add(0x03);
        return stream.ToArray();
    }

    /// <summary>A Brotli stream from the encoder of the runtime: the payload really compressed.</summary>
    internal static byte[] Squeezed(byte[] payload)
    {
        var stream = new byte[BrotliEncoder.GetMaxCompressedLength(payload.Length)];
        if (!BrotliEncoder.TryCompress(payload, stream, out int written, quality: 5, window: 22))
            throw new InvalidOperationException("The payload did not compress.");
        return stream[..written];
    }

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
