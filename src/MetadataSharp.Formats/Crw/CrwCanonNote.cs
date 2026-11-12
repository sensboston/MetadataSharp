using System.Buffers.Binary;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Crw;

/// <summary>
/// The records of a CRW file that are tags of the Canon MakerNote, laid out as an EXIF block that
/// holds such a note: a primary directory with the make, an Exif directory with the MakerNote tag,
/// and the note, a Canon directory whose values count from the TIFF header. The reader of the note
/// then names the tags and splits the arrays as in every other file, and the writer of the note
/// changes them by the same rules.
/// </summary>
internal static class CrwCanonNote
{
    /// <summary>Where the note starts in the block.</summary>
    internal const int Start = 62;

    /// <summary>The CIFF record of each Canon tag: its tag, the directories that lead to it from the root, and the length the reference fixes for it.</summary>
    internal static readonly (ushort Tag, ushort Record, ushort[] Path, int Fixed)[] Records =
    [
        (0x0001, 0x102D, [0x300A, 0x300B], 0), (0x0002, 0x1029, [0x300A, 0x300B], 0),
        (0x0004, 0x102A, [0x300A, 0x300B], 0), (0x0006, 0x0815, [0x300A, 0x2804], 0),
        (0x0007, 0x080B, [0x300A, 0x2807, 0x3004], 0), (0x0008, 0x1817, [0x300A], 4),
        (0x0009, 0x0810, [0x300A, 0x2807], 0), (0x000C, 0x180B, [0x300A, 0x2807, 0x3004], 0),
        (0x000F, 0x1033, [0x300A, 0x300B], 0), (0x0012, 0x1038, [0x300A, 0x300B], 0),
        (0x0015, 0x183B, [0x300A, 0x300B], 0), (0x00A9, 0x10A9, [0x300A, 0x300B], 0),
        (0x00B5, 0x10B5, [0x300A, 0x300B], 0), (0x00C0, 0x10C0, [0x300A, 0x300B], 0),
        (0x00C1, 0x10C1, [0x300A, 0x300B], 0),
    ];

    internal static byte[] Block(bool little, IReadOnlyDictionary<ushort, (TiffFieldType Type, byte[] Bytes)> canon)
    {
        int count = canon.Count;
        int values = Start + 2 + (count * 12) + 4, total = values;
        foreach ((TiffFieldType _, byte[] bytes) in canon.Values)
            if (bytes.Length > 4) total += bytes.Length + (bytes.Length & 1);
        // Eight bytes of zero end the note, so that its end is never read as the footer of a note that was moved.
        int length = total + 8 - Start;
        byte[] block = new byte[total + 8];
        Span<byte> span = block;
        (little ? "II*\0"u8 : "MM\0*"u8).CopyTo(span);
        Put32(span, 4, 8, little);
        // The primary directory: the make, and where the Exif directory is.
        Put16(span, 8, 2, little);
        Entry(span, 10, 0x010F, 2, 6, 38, little);
        Entry(span, 22, 0x8769, 4, 1, 44, little);
        "Canon\0"u8.CopyTo(span[38..]);
        // The Exif directory: the note.
        Put16(span, 44, 1, little);
        Entry(span, 46, 0x927C, 7, (uint)length, Start, little);
        Put16(span, Start, (ushort)count, little);
        int entry = Start + 2;
        foreach (ushort tag in canon.Keys.Order())
        {
            (TiffFieldType type, byte[] bytes) = canon[tag];
            int width = type is TiffFieldType.Short ? 2 : type is TiffFieldType.Long ? 4 : 1;
            Put16(span, entry, tag, little);
            Put16(span, entry + 2, (ushort)type, little);
            Put32(span, entry + 4, (uint)(bytes.Length / width), little);
            if (bytes.Length <= 4) bytes.CopyTo(span[(entry + 8)..]);
            else
            {
                Put32(span, entry + 8, (uint)values, little);
                bytes.CopyTo(span[values..]);
                values += bytes.Length + (bytes.Length & 1);
            }
            entry += 12;
        }
        return block;
    }

    private static void Entry(Span<byte> span, int at, ushort tag, ushort type, uint count, uint value, bool little)
    {
        Put16(span, at, tag, little);
        Put16(span, at + 2, type, little);
        Put32(span, at + 4, count, little);
        Put32(span, at + 8, value, little);
    }

    private static void Put16(Span<byte> span, int at, ushort value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(span[at..], value); else BinaryPrimitives.WriteUInt16BigEndian(span[at..], value);
    }

    private static void Put32(Span<byte> span, int at, uint value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(span[at..], value); else BinaryPrimitives.WriteUInt32BigEndian(span[at..], value);
    }
}
