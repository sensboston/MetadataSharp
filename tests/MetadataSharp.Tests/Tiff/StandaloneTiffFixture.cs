using System.Buffers.Binary;

namespace MetadataSharp.Tests.Tiff;

/// <summary>Authored 16x16 grayscale TIFF. No imported image or codec implementation.</summary>
internal sealed class StandaloneTiffFixture(bool big = false)
{
    internal byte[] Bytes { get; } = new byte[4096];
    internal void U16(int at, ushort value) { if (big) BinaryPrimitives.WriteUInt16BigEndian(Bytes.AsSpan(at), value); else BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(at), value); }
    internal void U32(int at, uint value) { if (big) BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(at), value); else BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(at), value); }
    internal void Directory(int at, (ushort Tag, ushort Type, uint[] Values)[] fields, uint next = 0)
    {
        U16(at, (ushort)fields.Length); int external = at + 256;
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i]; int entry = at + 2 + 12 * i; U16(entry, f.Tag); U16(entry + 2, f.Type); U32(entry + 4, (uint)f.Values.Length);
            int width = f.Type == 3 ? 2 : 4, pos = entry + 8;
            if (f.Values.Length * width > 4) { pos = external; U32(entry + 8, (uint)pos); external += f.Values.Length * width; }
            foreach (uint value in f.Values) { if (width == 2) U16(pos, (ushort)value); else U32(pos, value); pos += width; }
        }
        U32(at + 2 + 12 * fields.Length, next);
    }
    internal static (ushort Tag, ushort Type, uint[] Values)[] Fields(bool tiles = false) =>
        [(256, 4, [16]), (257, 4, [16]), (258, 3, [8]), (259, 3, [1]), (262, 3, [1]),
         (277, 3, [1]), .. tiles ? new (ushort, ushort, uint[])[] { (322, 4, [16]), (323, 4, [16]), (324, 4, [2048]), (325, 4, [256]) }
             : [(273, 4, [2048]), (278, 4, [16]), (279, 4, [256])]];
    internal static StandaloneTiffFixture Create(bool big = false, bool tiles = false)
    {
        var f = new StandaloneTiffFixture(big); f.Bytes[0] = f.Bytes[1] = big ? (byte)0x4d : (byte)0x49;
        f.U16(2, 42); f.U32(4, 8); f.Directory(8, Fields(tiles));
        for (int i = 0; i < 256; i++) f.Bytes[2048 + i] = (byte)i;
        return f;
    }
}
