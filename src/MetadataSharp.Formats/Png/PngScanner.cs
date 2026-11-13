using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Png;

// Represent the PngChunk state used by PNG parsing and output.
internal sealed record PngChunk(string Type, long Start, int Size, bool Xmp, bool UnsafeUnknown)
{
    /// <summary>The NAME of a tEXt or zTXt chunk whose keyword is "Raw profile type NAME", else null.</summary>
    internal string? Profile { get; init; }
    /// <summary>A tEXt, zTXt or iTXt chunk whose keyword is exactly "Comment".</summary>
    internal bool Comment { get; init; }
    /// <summary>A tEXt, zTXt or iTXt chunk whose keyword is exactly "Description".</summary>
    internal bool Description { get; init; }
    /// <summary>The checksum the chunk ends with, which the scan found to hold.</summary>
    internal uint Checksum { get; init; }
    internal long Payload => Start + 8;
    internal long Length => (long)Size + 12;
}
internal sealed record PngScan(long Length, IReadOnlyList<PngChunk> Chunks, long Insertion);
internal static class PngScanner
{
    internal static MetadataException Invalid(string message = "Malformed PNG container.") => new(MetadataErrorCode.InvalidPng, message);
    private static readonly HashSet<string> Known = ["IHDR", "PLTE", "IDAT", "IEND", "eXIf", "iTXt", "tEXt", "zTXt", "iCCP", "cHRM", "gAMA", "sBIT", "sRGB", "cICP", "mDCV", "cLLI", "bKGD", "hIST", "tRNS", "pHYs", "sPLT", "tIME"];
    // Keep RawProfileName focused on PNG and its format-specific validation contract.
    private static string? RawProfileName(ReadOnlySpan<byte> prefix)
    {
        ReadOnlySpan<byte> lead = "Raw profile type "u8;
        if (!prefix.StartsWith(lead)) return null;
        ReadOnlySpan<byte> rest = prefix[lead.Length..];
        int end = rest.IndexOf((byte)0);
        return end <= 0 ? null : Encoding.Latin1.GetString(rest[..end]);
    }
    internal static async Task<PngScan> Scan(Stream source, MetadataLimits limits, bool asynchronous, CancellationToken token)
    {
        if (!source.CanRead || !source.CanSeek || source.Position != 0) throw new ArgumentException("PNG source must be readable, seekable and at zero.", nameof(source));
        long length = source.Length;
        if (length > limits.MaxInputBytes) throw new MetadataException(MetadataErrorCode.LimitExceeded, "Input exceeds its byte budget.");
        if (length < 8) throw Invalid();
        byte[] signature = new byte[8]; await ContainerIo.Read(source, signature, asynchronous, token).ConfigureAwait(false);
        if (!signature.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw Invalid("Invalid PNG signature.");
        byte[] buffer = ArrayPool<byte>.Shared.Rent(limits.CopyBufferBytes);
        try
        {
            var chunks = new List<PngChunk>(); var seen = new HashSet<string>();
            int color = -1, depth = 0, palette = 0; bool idat = false, ended = false, paletteDependent = false; long insertion = -1;
            while (source.Position < length)
            {
                token.ThrowIfCancellationRequested(); long start = source.Position;
                if (length - start < 12) throw Invalid();
                byte[] header = new byte[8]; await ContainerIo.Read(source, header, asynchronous, token).ConfigureAwait(false);
                uint size = BinaryPrimitives.ReadUInt32BigEndian(header);
                if (size > int.MaxValue || size > length - start - 12) throw Invalid("Invalid PNG chunk range.");
                for (int i = 4; i < 8; i++) if (!char.IsAsciiLetter((char)header[i])) throw Invalid("Invalid PNG chunk name.");
                if ((header[6] & 32) != 0) throw Invalid("The PNG reserved name bit is set.");
                string type = Encoding.ASCII.GetString(header, 4, 4);
                if (chunks.Count >= limits.MaxSegments) throw new MetadataException(MetadataErrorCode.LimitExceeded, "PNG chunk count exceeds its budget.");
                if (chunks.Count == 0 && type != "IHDR") throw Invalid("IHDR must be first.");
                if (type is "acTL" or "fcTL" or "fdAT") throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "APNG is outside the static PNG profile.");
                if (!Known.Contains(type) && (header[4] & 32) == 0) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unknown critical PNG chunk.");
                uint crc = Crc.Update(uint.MaxValue, header.AsSpan(4));
                byte[] prefix = new byte[(int)Math.Min(size, 80)]; int remaining = (int)size, captured = 0;
                while (remaining > 0)
                {
                    int count = Math.Min(remaining, limits.CopyBufferBytes);
                    await ContainerIo.Read(source, buffer.AsMemory(0, count), asynchronous, token).ConfigureAwait(false);
                    crc = Crc.Update(crc, buffer.AsSpan(0, count));
                    int copy = Math.Min(prefix.Length - captured, count); buffer.AsSpan(0, copy).CopyTo(prefix.AsSpan(captured)); captured += copy; remaining -= count;
                }
                byte[] checksum = new byte[4]; await ContainerIo.Read(source, checksum, asynchronous, token).ConfigureAwait(false);
                if (BinaryPrimitives.ReadUInt32BigEndian(checksum) != ~crc) throw Invalid("PNG CRC mismatch.");
                bool repeated = !seen.Add(type);
                if (type is not ("IDAT" or "iTXt" or "tEXt" or "zTXt" or "sPLT" or "eXIf") && Known.Contains(type) && repeated) throw Invalid("Duplicate structural PNG chunk.");
                if (idat && type != "IDAT") ended = true;
                switch (type)
                {
                    case "IHDR":
                        if (size != 13 || repeated) throw Invalid();
                        uint width = BinaryPrimitives.ReadUInt32BigEndian(prefix), height = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(4));
                        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue) throw Invalid();
                        depth = prefix[8]; color = prefix[9];
                        bool legal = color switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 2 or 4 or 6 => depth is 8 or 16, 3 => depth is 1 or 2 or 4 or 8, _ => false };
                        if (!legal || prefix[10] != 0 || prefix[11] != 0 || prefix[12] > 1) throw Invalid("Unsupported or invalid IHDR values.");
                        break;
                    case "PLTE":
                        if (idat || paletteDependent || color is 0 or 4 || size == 0 || size > 768 || size % 3 != 0 || (color == 3 && size / 3 > (1 << depth))) throw Invalid("Invalid PLTE.");
                        palette = (int)size / 3; break;
                    case "IDAT":
                        if (ended || (color == 3 && palette == 0)) throw Invalid("IDAT ordering or palette is invalid.");
                        if (!idat) insertion = start; idat = true; break;
                    case "IEND":
                        if (!idat || size != 0 || source.Position != length) throw Invalid("Invalid IEND or trailing data.");
                        chunks.Add(new(type, start, (int)size, false, false) { Checksum = ~crc }); return new(length, chunks.AsReadOnly(), insertion);
                }
                if (type is "cHRM" or "gAMA" or "iCCP" or "sBIT" or "sRGB" or "cICP" or "mDCV" or "cLLI")
                    if (idat || palette != 0) throw Invalid("Color metadata must precede PLTE/IDAT.");
                // eXIf is not in this list: the specification lets it follow the image data.
                if (type is "bKGD" or "hIST" or "tRNS" or "pHYs" or "sPLT")
                    if (idat) throw Invalid("This PNG chunk must precede IDAT.");
                if (type is "bKGD" or "hIST" or "tRNS") paletteDependent = true;
                if (type == "hIST" && (palette == 0 || size != palette * 2)) throw Invalid("Invalid hIST.");
                if (type == "tRNS" && (color is 4 or 6 || (color == 0 && size != 2) || (color == 2 && size != 6) || (color == 3 && (palette == 0 || size > palette)))) throw Invalid("Invalid tRNS.");
                if (type == "bKGD" && (size != (color == 3 ? 1 : color is 0 or 4 ? 2 : 6) || (color == 3 && (palette == 0 || prefix[0] >= palette)))) throw Invalid("Invalid bKGD.");
                int fixedSize = type switch { "gAMA" or "cICP" => 4, "cHRM" => 32, "sRGB" => 1, "pHYs" => 9, "tIME" => 7, "mDCV" => 24, "cLLI" => 8, "sBIT" => color switch { 0 => 1, 2 or 3 => 3, 4 => 2, _ => 4 }, _ => -1 };
                if (fixedSize >= 0 && size != fixedSize) throw Invalid("Invalid fixed-size ancillary chunk.");
                bool xmp = type == "iTXt" && prefix.AsSpan().StartsWith("XML:com.adobe.xmp\0"u8);
                string? profile = type is "tEXt" or "zTXt" ? RawProfileName(prefix) : null;
                bool text = type is "tEXt" or "zTXt" or "iTXt";
                bool comment = text && prefix.AsSpan().StartsWith("Comment\0"u8);
                bool description = text && prefix.AsSpan().StartsWith("Description\0"u8);
                chunks.Add(new(type, start, (int)size, xmp, !Known.Contains(type) && (header[7] & 32) == 0)
                { Profile = profile, Comment = comment, Description = description, Checksum = ~crc });
            }
            throw Invalid("Missing IEND.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
/// <summary>
/// The text chunks in which ImageMagick, and the tools that follow it, store a metadata block PNG has
/// no chunk for. The keyword is "Raw profile type NAME". The text is a line with the name, a line with
/// the number of bytes in decimal, and the bytes in hexadecimal, broken into lines.
/// </summary>
internal static class PngRawProfile
{
    private static MetadataException Bad() => PngScanner.Invalid("Malformed raw profile text.");
    private static bool Blank(byte value) => value == ' ' || value == '\n' || value == '\r' || value == '\t';
    private static int Nibble(byte value) =>
        value >= '0' && value <= '9' ? value - '0' : value >= 'a' && value <= 'f' ? value - 'a' + 10 : value >= 'A' && value <= 'F' ? value - 'A' + 10 : -1;
    internal static byte[] Decode(ReadOnlySpan<byte> text)
    {
        int at = 0;
        // The name line, with the line break writers put in front of it. The keyword has named the profile.
        while (at < text.Length && (text[at] == '\n' || text[at] == '\r')) at++;
        while (at < text.Length && text[at] != '\n') at++;
        while (at < text.Length && Blank(text[at])) at++;
        long declared = 0; int digits = 0;
        while (at < text.Length && text[at] >= '0' && text[at] <= '9')
        {
            declared = declared * 10 + (text[at++] - '0'); digits++;
            // Two digits spell one byte, so the text bounds the count before anything is allocated.
            if (declared > text.Length) throw Bad();
        }
        if (digits == 0) throw Bad();
        byte[] result = new byte[declared]; int count = 0, high = -1;
        for (; at < text.Length; at++)
        {
            if (Blank(text[at])) continue;
            int nibble = Nibble(text[at]);
            if (nibble < 0 || count == result.Length) throw Bad();
            if (high < 0) high = nibble;
            else { result[count++] = (byte)((high << 4) | nibble); high = -1; }
        }
        if (high >= 0 || count != result.Length) throw Bad();
        return result;
    }
    /// <summary>
    /// The text of a profile as exiv2 and ImageMagick write it: a line break, the name, the number of
    /// bytes right-aligned in eight columns, then the bytes in hexadecimal, thirty-six to a line.
    /// </summary>
    internal static byte[] Encode(string name, ReadOnlySpan<byte> profile)
    {
        const string Hex = "0123456789abcdef";
        var text = new System.Text.StringBuilder(profile.Length * 2 + profile.Length / 36 + name.Length + 16);
        text.Append('\n').Append(name).Append('\n')
            .Append(profile.Length.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(8));
        for (int i = 0; i < profile.Length; i++)
        {
            if (i % 36 == 0) text.Append('\n');
            text.Append(Hex[profile[i] >> 4]).Append(Hex[profile[i] & 15]);
        }
        text.Append('\n');
        return System.Text.Encoding.ASCII.GetBytes(text.ToString());
    }
}
// Represent the Crc state used by PNG parsing and output.
internal static class Crc
{
    private static readonly uint[] Table = Build();
    // Keep Build focused on PNG and its format-specific validation contract.
    private static uint[] Build()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++) { uint value = i; for (int n = 0; n < 8; n++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320); table[i] = value; }
        return table;
    }
    // Keep Update focused on PNG and its format-specific validation contract.
    internal static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    { foreach (byte b in bytes) crc = Table[(crc ^ b) & 255] ^ (crc >> 8); return crc; }
    // Keep Chunk focused on PNG and its format-specific validation contract.
    internal static byte[] Chunk(string type, byte[] data)
    {
        byte[] result = new byte[checked(data.Length + 12)]; BinaryPrimitives.WriteUInt32BigEndian(result, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, result.AsSpan(4, 4)); data.CopyTo(result, 8);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(result.Length - 4), ~Update(uint.MaxValue, result.AsSpan(4, data.Length + 4))); return result;
    }
}
