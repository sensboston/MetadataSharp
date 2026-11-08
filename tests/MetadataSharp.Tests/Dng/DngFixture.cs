using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MetadataSharp.Tests.Dng;

/// <summary>
/// Authored minimal synthetic DNG fixtures. Byte-identical to the independent
/// Python writer tools/reference/dng_fixtures.py; CI compares SHA-256 of both.
/// Raw image: 4x2, 16-bit unsigned CFA (photometric 32803), one strip.
/// Thumbnail variant: IFD0 is a 4x2 8-bit RGB image with one SubIFD to the raw leaf.
/// </summary>
internal sealed class DngFixture
{
    internal byte[] Bytes { get; }
    private DngFixture(byte[] bytes) => Bytes = bytes;
    internal static DngFixture RawOnly(bool bigEndian = false) => new(Build(bigEndian, false));
    internal static DngFixture ThumbnailAndRaw(bool bigEndian = false) => new(Build(bigEndian, true));
    internal string Sha256 => Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();

    private const ushort TAscii = 2, TShort = 3, TLong = 4, TRational = 5, TSRational = 10, TByte = 1;
    private static readonly ushort[] RawPixels = [0x0000, 0x1000, 0x2000, 0x3000, 0x4000, 0x5000, 0x6000, 0x7000];
    private static byte[] RawPixelBytes(bool big)
    {
        byte[] bytes = new byte[16];
        for (int i = 0; i < RawPixels.Length; i++) WriteU16(bytes, i * 2, RawPixels[i], big);
        return bytes;
    }
    private static byte[] ThumbPixelBytes() { var bytes = new byte[24]; for (int i = 0; i < 24; i++) bytes[i] = (byte)i; return bytes; }
    internal static readonly byte[] Software = "MetadataSharp D1 fixture\0"u8.ToArray();
    internal static readonly byte[] Artist = "MetadataSharp\0"u8.ToArray();
    internal static readonly byte[] UniqueCameraModel = "MetadataSharp DNG1\0"u8.ToArray();

    private static void WriteU16(byte[] b, int at, ushort v, bool big) { if (big) BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), v); else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v); }
    private static void WriteU32(byte[] b, int at, uint v, bool big) { if (big) BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), v); else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v); }
    private static uint Align2(uint v) => (v + 1) & ~1u;

    private readonly record struct Entry(ushort Tag, ushort Type, uint Count, byte[] Payload);

    private static byte[] PackShorts(bool big, params ushort[] values) { var b = new byte[2 * values.Length]; for (int i = 0; i < values.Length; i++) WriteU16(b, i * 2, values[i], big); return b; }
    private static byte[] PackLongs(bool big, params uint[] values) { var b = new byte[4 * values.Length]; for (int i = 0; i < values.Length; i++) WriteU32(b, i * 4, values[i], big); return b; }
    private static byte[] PackRationals(bool big, params (uint Num, uint Den)[] values) { var b = new byte[8 * values.Length]; for (int i = 0; i < values.Length; i++) { WriteU32(b, i * 8, values[i].Num, big); WriteU32(b, i * 8 + 4, values[i].Den, big); } return b; }
    private static byte[] PackSRationals(bool big, params (int Num, int Den)[] values)
    {
        var b = new byte[8 * values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            if (big) { BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(i * 8), values[i].Num); BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(i * 8 + 4), values[i].Den); }
            else { BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(i * 8), values[i].Num); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(i * 8 + 4), values[i].Den); }
        }
        return b;
    }

    private static Entry[] RawEntries(bool big) =>
    [
        new(254, TLong, 1, PackLongs(big, 0)),
        new(256, TLong, 1, PackLongs(big, 4)),
        new(257, TLong, 1, PackLongs(big, 2)),
        new(258, TShort, 1, PackShorts(big, 16)),
        new(259, TShort, 1, PackShorts(big, 1)),
        new(262, TShort, 1, PackShorts(big, 32803)),
        new(273, TLong, 1, PackLongs(big, 0)), // patched: raw strip offset
        new(274, TShort, 1, PackShorts(big, 1)),
        new(277, TShort, 1, PackShorts(big, 1)),
        new(278, TLong, 1, PackLongs(big, 2)),
        new(279, TLong, 1, PackLongs(big, 16)),
        new(284, TShort, 1, PackShorts(big, 1)),
        new(305, TAscii, (uint)Software.Length, Software),
        new(315, TAscii, (uint)Artist.Length, Artist),
        new(339, TShort, 1, PackShorts(big, 1)),
        new(33421, TShort, 2, PackShorts(big, 2, 2)),
        new(33422, TByte, 4, [0, 1, 1, 2]),
        new(50706, TByte, 4, [1, 1, 0, 0]),
        new(50707, TByte, 4, [1, 0, 0, 0]),
        new(50708, TAscii, (uint)UniqueCameraModel.Length, UniqueCameraModel),
        new(50710, TByte, 3, [0, 1, 2]),
        new(50711, TShort, 1, PackShorts(big, 1)),
        new(50713, TShort, 2, PackShorts(big, 1, 1)),
        new(50714, TRational, 1, PackRationals(big, (0, 1))),
        new(50717, TLong, 1, PackLongs(big, 65535)),
        new(50719, TLong, 2, PackLongs(big, 0, 0)),
        new(50720, TLong, 2, PackLongs(big, 2, 4)),
        new(50721, TSRational, 9, PackSRationals(big, (1, 1), (0, 1), (0, 1), (0, 1), (1, 1), (0, 1), (0, 1), (0, 1), (1, 1))),
        new(50728, TRational, 3, PackRationals(big, (1, 1), (1, 1), (1, 1))),
        new(50778, TShort, 1, PackShorts(big, 21)),
        new(50829, TLong, 4, PackLongs(big, 0, 0, 2, 4)),
    ];

    private static Entry[] ThumbnailEntries(bool big) =>
    [
        new(254, TLong, 1, PackLongs(big, 1)),
        new(256, TLong, 1, PackLongs(big, 4)),
        new(257, TLong, 1, PackLongs(big, 2)),
        new(258, TShort, 3, PackShorts(big, 8, 8, 8)),
        new(259, TShort, 1, PackShorts(big, 1)),
        new(262, TShort, 1, PackShorts(big, 2)),
        new(273, TLong, 1, PackLongs(big, 0)), // patched: thumbnail strip offset
        new(274, TShort, 1, PackShorts(big, 1)),
        new(277, TShort, 3, PackShorts(big, 3, 3, 3)),
        new(278, TLong, 1, PackLongs(big, 2)),
        new(279, TLong, 1, PackLongs(big, 24)),
        new(284, TShort, 1, PackShorts(big, 1)),
        new(305, TAscii, (uint)Software.Length, Software),
        new(330, TLong, 1, PackLongs(big, 0)), // patched: raw IFD offset
        new(339, TShort, 3, PackShorts(big, 1, 1, 1)),
        new(50706, TByte, 4, [1, 1, 0, 0]),
        new(50707, TByte, 4, [1, 0, 0, 0]),
        new(50708, TAscii, (uint)UniqueCameraModel.Length, UniqueCameraModel),
        new(50721, TSRational, 9, PackSRationals(big, (1, 1), (0, 1), (0, 1), (0, 1), (1, 1), (0, 1), (0, 1), (0, 1), (1, 1))),
        new(50728, TRational, 3, PackRationals(big, (1, 1), (1, 1), (1, 1))),
        new(50778, TShort, 1, PackShorts(big, 21)),
    ];

    private readonly record struct Planned(Entry Entry, uint? ExternalOffset);

    private static (Planned[] Items, uint ExtEnd) Plan(Entry[] entries, uint baseOffset)
    {
        Entry[] sorted = entries.OrderBy(e => e.Tag).ToArray();
        uint body = (uint)(2 + 12 * sorted.Length + 4);
        uint ext = Align2(baseOffset + body);
        var items = new Planned[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            Entry e = sorted[i];
            if (e.Payload.Length > 4) { ext = Align2(ext); items[i] = new(e, ext); ext += (uint)e.Payload.Length; }
            else items[i] = new(e, null);
        }
        return (items, Align2(ext));
    }

    private static byte[] Render(Planned[] items, uint baseOffset, bool big, uint nextIfd)
    {
        byte[] body = new byte[2 + 12 * items.Length + 4];
        WriteU16(body, 0, (ushort)items.Length, big);
        var extras = new List<(uint Offset, byte[] Payload)>();
        for (int i = 0; i < items.Length; i++)
        {
            (Entry e, uint? off) = (items[i].Entry, items[i].ExternalOffset);
            int at = 2 + 12 * i;
            WriteU16(body, at, e.Tag, big); WriteU16(body, at + 2, e.Type, big); WriteU32(body, at + 4, e.Count, big);
            if (off is null) e.Payload.AsSpan().CopyTo(body.AsSpan(at + 8));
            else { WriteU32(body, at + 8, off.Value, big); extras.Add((off.Value, e.Payload)); }
        }
        WriteU32(body, 2 + 12 * items.Length, nextIfd, big);
        var blob = new List<byte>();
        uint end = baseOffset + (uint)body.Length;
        foreach ((uint off, byte[] payload) in extras.OrderBy(x => x.Offset))
        {
            while (end + (uint)blob.Count < off) blob.Add(0);
            blob.AddRange(payload);
        }
        byte[] output = new byte[body.Length + blob.Count];
        body.CopyTo(output, 0); blob.CopyTo(output, body.Length);
        return output;
    }

    private static byte[] Assemble(bool big, Dictionary<uint, byte[]> blocks)
    {
        var sorted = blocks.OrderBy(kv => kv.Key).ToList();
        long total = 0; foreach (var (off, bytes) in sorted) total = Math.Max(total, off + bytes.Length);
        byte[] data = new byte[total];
        foreach (var (off, bytes) in sorted) bytes.CopyTo(data, off);
        return data;
    }

    private static byte[] Build(bool big, bool thumb)
    {
        byte[] header = new byte[8];
        header[0] = header[1] = big ? (byte)0x4d : (byte)0x49;
        WriteU16(header, 2, 42, big); WriteU32(header, 4, 8, big);
        var blocks = new Dictionary<uint, byte[]> { [0] = header };
        if (!thumb)
        {
            Entry[] raw = RawEntries(big);
            (Planned[] items, uint extEnd) = Plan(raw, 8);
            raw[6] = raw[6] with { Payload = PackLongs(big, extEnd) };
            (items, _) = Plan(raw, 8);
            blocks[8] = Render(items, 8, big, 0);
            blocks[extEnd] = RawPixelBytes(big);
        }
        else
        {
            Entry[] t = ThumbnailEntries(big);
            Entry[] raw = RawEntries(big);
            (_, uint tExtEnd) = Plan(t, 8);
            uint rawBase = tExtEnd + 24;
            t[6] = t[6] with { Payload = PackLongs(big, tExtEnd) };
            t[13] = t[13] with { Payload = PackLongs(big, rawBase) };
            (Planned[] tItems, _) = Plan(t, 8);
            (Planned[] rawItems, uint rawExtEnd) = Plan(raw, rawBase);
            raw[6] = raw[6] with { Payload = PackLongs(big, rawExtEnd) };
            (rawItems, _) = Plan(raw, rawBase);
            blocks[8] = Render(tItems, 8, big, 0);
            blocks[tExtEnd] = ThumbPixelBytes();
            blocks[rawBase] = Render(rawItems, rawBase, big, 0);
            blocks[rawExtEnd] = RawPixelBytes(big);
        }
        return Assemble(big, blocks);
    }
}
