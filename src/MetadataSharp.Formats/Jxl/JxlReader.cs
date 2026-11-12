using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Bmff;

namespace MetadataSharp.Formats.Jxl;

/// <summary>
/// The payload of one metadata box of a JPEG XL container: where it starts in the file and how long
/// it is. <c>Compressed</c> says the bytes are one Brotli stream that decodes to the payload, as in a
/// <c>brob</c> box; the range then starts after the four bytes that name the box it stands for.
/// </summary>
internal readonly record struct JxlBlock(int Start, int Length, bool Compressed = false)
{
    /// <summary>False for a box the file does not have. A payload never starts at zero, where the signature box is.</summary>
    internal bool Present => Start != 0;
}

/// <summary>
/// Where the metadata of a JPEG XL file is. <c>Container</c> is false for a bare codestream, which
/// has no boxes at all. The payload of the <c>Exif</c> box is a four-byte offset and then a TIFF;
/// the payload of the <c>xml </c> box is an XMP packet. Either may be stored compressed.
/// </summary>
internal readonly record struct JxlLayout(bool Container, JxlBlock Exif, JxlBlock Xmp, bool HasJumbf);

/// <summary>
/// One bounded pass over the top-level boxes of a JPEG XL container. No box is opened: the
/// codestream boxes, the level box and every box of a type this reader does not know are stepped
/// over by their size. A <c>brob</c> box stands for the box its first four bytes name; it is
/// decompressed only when that box is the Exif or the XMP box, and only as far as the retained
/// budget has room. The EXIF block and the packet are then read by the readers that read them in
/// every other container.
/// </summary>
internal ref struct JxlReader
{
    private const uint SignatureBox = 0x4A584C20; // 'JXL '
    private const uint FileTypeBox = 0x66747970;  // 'ftyp'
    private const uint Brand = 0x6A786C20;        // 'jxl '
    private const uint ExifBox = 0x45786966;      // 'Exif'
    private const uint XmlBox = 0x786D6C20;       // 'xml '
    private const uint JumbfBox = 0x6A756D62;     // 'jumb'
    private const uint BrotliBox = 0x62726F62;    // 'brob'

    /// <summary>The signature box: twelve bytes that no other file starts with.</summary>
    internal static ReadOnlySpan<byte> Signature => new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A };

    private readonly ReadOnlySpan<byte> data;
    private readonly MetadataLimits limits;
    private BmffBoxWalk walk;
    private long retained;
    private JxlBlock exif;
    private JxlBlock xmp;
    private bool hasJumbf;

    private JxlReader(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        this.data = data;
        this.limits = limits;
        walk = new BmffBoxWalk(data, limits);
        retained = 0;
        exif = default;
        xmp = default;
        hasJumbf = false;
    }

    /// <summary>
    /// Finds the metadata boxes of a complete JPEG XL file. A file that is neither a container nor a
    /// codestream is refused; a container without a metadata box is not malformed, it has nothing
    /// to find.
    /// </summary>
    internal static JxlLayout Locate(ReadOnlySpan<byte> data, MetadataLimits? limits = null) =>
        Open(data, limits).Walk();

    /// <summary>Reads the EXIF block and the XMP packet of a complete JPEG XL file.</summary>
    internal static JxlMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits, XmpReadOptions? xmpOptions = null)
    {
        JxlReader reader = Open(data, limits);
        JxlLayout layout = reader.Walk();
        return reader.Metadata(layout, xmpOptions);
    }

    private static JxlReader Open(ReadOnlySpan<byte> data, MetadataLimits? limits)
    {
        limits ??= BmffReadOptions.DefaultLimits;
        limits.Validate();
        if (data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The JPEG XL file exceeds the input budget.", 0);
        return new JxlReader(data, limits);
    }

    private JxlLayout Walk()
    {
        // A bare codestream: the two bytes of its marker, and no box to hold anything else.
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x0A) return default;
        if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature))
            throw walk.Fail("The file is neither a JPEG XL container nor a JPEG XL codestream.", 0);
        int position = 0;
        BoxSpan signature = walk.Next(ref position, data.Length, 1);
        if (signature.Type != SignatureBox || position >= data.Length)
            throw walk.Fail("The signature box is not followed by a file type box.", position);
        BoxSpan fileType = walk.Next(ref position, data.Length, 1);
        if (fileType.Type != FileTypeBox)
            throw walk.Fail("The signature box is not followed by a file type box.", fileType.Start);
        if (fileType.PayloadLength < 8 || fileType.PayloadLength % 4 != 0)
            throw walk.Fail("The file type box payload is truncated or unaligned.", fileType.Payload);
        if (walk.U32(fileType.Payload) != Brand)
            throw walk.Fail("The major brand is not 'jxl ': the file is not a JPEG XL container.", fileType.Payload);
        while (position < data.Length)
        {
            BoxSpan box = walk.Next(ref position, data.Length, 1);
            uint type = box.Type;
            int start = box.Payload;
            bool compressed = type == BrotliBox;
            if (compressed)
            {
                if (box.PayloadLength < 4)
                    throw walk.Fail("A compressed box is shorter than the type of the box it stands for.", box.Start);
                type = walk.U32(start);
                start += 4;
            }
            // A compressed box and a plain one of the same type are the same box twice.
            if (type == ExifBox) Claim(ref exif, box, start, compressed, "The Exif box appears more than once.");
            else if (type == XmlBox) Claim(ref xmp, box, start, compressed, "The XMP box appears more than once.");
            else if (type == JumbfBox) hasJumbf = true;
        }
        return new JxlLayout(true, exif, xmp, hasJumbf);
    }

    private readonly void Claim(ref JxlBlock block, BoxSpan box, int start, bool compressed, string twice)
    {
        if (block.Present) throw walk.Fail(twice, box.Start);
        block = new JxlBlock(start, box.End - start, compressed);
    }

    private JxlMetadata Metadata(JxlLayout layout, XmpReadOptions? xmpOptions)
    {
        // The block before the packet, whichever comes first in the file: what the budget has left
        // for the second does not depend on the order of the boxes.
        ExifMetadata? metadata = layout.Exif.Present ? Exif(layout.Exif) : null;
        XmpPacket? packet = layout.Xmp.Present ? Xmp(layout.Xmp, xmpOptions) : null;
        return new JxlMetadata(metadata, packet, layout.HasJumbf);
    }

    /// <summary>
    /// The payload of the Exif box: the offset of the TIFF header, counted from the end of the
    /// offset itself, then the bytes it skips and the TIFF. The layout is that of the Exif item of a
    /// HEIF file, and so is the rule that only the budgets bound the block.
    /// </summary>
    private ExifMetadata Exif(JxlBlock block)
    {
        ReadOnlySpan<byte> payload = Payload(block);
        if (payload.Length < 4)
            throw walk.Fail("The Exif box is shorter than its TIFF offset.", block.Start);
        long start = 4L + BinaryPrimitives.ReadUInt32BigEndian(payload);
        if (start >= payload.Length)
            throw walk.Fail("The TIFF header lies past its Exif box.", block.Start);
        // The payload fits what is left, and the TIFF is shorter than the payload: the budget handed on is never empty.
        MetadataLimits left = limits with { MaxRetainedMetadataBytes = (int)(limits.MaxRetainedMetadataBytes - retained - start) };
        ExifMetadata metadata;
        try
        {
            metadata = ExifTiffReader.Inspect(payload[(int)start..], left, block.Compressed ? 0 : block.Start + start, ExifTiffReader.NoCeiling);
        }
        catch (MetadataException refusal) when (block.Compressed)
        {
            // An offset into the decompressed bytes is not a place in the file: the refusal points at the stream.
            throw new MetadataException(refusal.Code, refusal.Message, block.Start);
        }
        // What the block reader counted against the budget it was given: the block and every value.
        retained += payload.Length;
        foreach (ExifEntry entry in metadata.Entries)
            retained += entry.Value.ByteLength;
        return metadata;
    }

    private XmpPacket Xmp(JxlBlock block, XmpReadOptions? xmpOptions)
    {
        ReadOnlySpan<byte> payload = Payload(block);
        retained += payload.Length;
        return XmpPacket.Parse(payload, xmpOptions);
    }

    /// <summary>The bytes of a metadata box, once the retained budget is known to have room for them.</summary>
    private readonly ReadOnlySpan<byte> Payload(JxlBlock block)
    {
        if (block.Compressed) return Inflate(block);
        if (block.Length > limits.MaxRetainedMetadataBytes - retained)
            throw walk.Limit("The metadata boxes exceed the retained metadata budget.", block.Start);
        return data.Slice(block.Start, block.Length);
    }

    /// <summary>
    /// The payload a compressed box decodes to. The output is bounded by what the retained budget
    /// has left, not by the stream: the buffer grows to one byte more than that and no further, so
    /// a stream that would decode to more is refused after that much memory and no more. The box
    /// has to hold exactly one whole stream.
    /// </summary>
    private readonly byte[] Inflate(JxlBlock block)
    {
        long room = limits.MaxRetainedMetadataBytes - retained;
        if (room <= 0)
            throw walk.Limit("The metadata boxes exceed the retained metadata budget.", block.Start);
        int ceiling = (int)Math.Min(room + 1, Array.MaxLength);
        ReadOnlySpan<byte> source = data.Slice(block.Start, block.Length);
        byte[] output = new byte[(int)Math.Min(ceiling, Math.Max(256L, 4L * source.Length))];
        int written = 0;
        var decoder = new BrotliDecoder();
        try
        {
            while (true)
            {
                OperationStatus status = decoder.Decompress(source, output.AsSpan(written), out int consumed, out int produced);
                source = source[consumed..];
                written += produced;
                if (written > room)
                    throw walk.Limit("A compressed box decodes to more than the retained metadata budget has room for.", block.Start);
                if (status == OperationStatus.Done) break;
                // Out of input before the end of the stream, or bytes that are no stream at all.
                if (status != OperationStatus.DestinationTooSmall)
                    throw walk.Fail("A compressed box does not hold a whole Brotli stream.", block.Start);
                if (written < output.Length)
                {
                    // Room was left and nothing moved: asking again would not end.
                    if (consumed == 0 && produced == 0)
                        throw walk.Fail("A compressed box does not hold a whole Brotli stream.", block.Start);
                    continue;
                }
                if (output.Length >= ceiling)
                    throw walk.Limit("A compressed box decodes to more than the retained metadata budget has room for.", block.Start);
                Array.Resize(ref output, (int)Math.Min(ceiling, 2L * output.Length));
            }
        }
        finally
        {
            decoder.Dispose();
        }
        if (!source.IsEmpty)
            throw walk.Fail("Bytes follow the Brotli stream of a compressed box.", block.Start + block.Length - source.Length);
        return written == output.Length ? output : output[..written];
    }
}
