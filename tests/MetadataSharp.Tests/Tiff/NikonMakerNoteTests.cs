using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class NikonMakerNoteTests
{
    private const int MakerNoteStart = 62;

    /// <summary>IFD0 (Make, ExifIFD) -> Exif IFD (MakerNote at 62) -> the given MakerNote bytes.</summary>
    private static byte[] Wrap(bool big, byte[] makerNote, string make = "NIKON")
    {
        var b = new byte[MakerNoteStart + makerNote.Length + (makerNote.Length & 1)];
        var w = new Writer(b, big);
        b[0] = b[1] = (byte)(big ? 'M' : 'I');
        w.U16(2, 42); w.U32(4, 8);
        w.U16(8, 2);
        w.Entry(10, 0x010F, 2, 6, 38);
        w.Entry(22, 0x8769, 4, 1, 44);
        Encoding.ASCII.GetBytes(make.PadRight(5)[..5]).CopyTo(b, 38);
        w.U16(44, 1);
        w.Entry(46, 0x927C, 7, (uint)makerNote.Length, MakerNoteStart);
        makerNote.CopyTo(b, MakerNoteStart);
        return b;
    }

    private readonly ref struct Writer(byte[] b, bool big)
    {
        public void U16(int at, int v) { if (big) BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), (ushort)v); else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v); }
        public void U32(int at, long v) { if (big) BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), (uint)v); else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)v); }
        public void Entry(int at, int tag, int type, long count, long value) { U16(at, tag); U16(at + 2, type); U32(at + 4, count); U32(at + 8, value); }
    }

    /// <summary>
    /// A two-entry IFD at <paramref name="ifdAt"/> inside <paramref name="note"/>: ISOSpeed (inline SHORT[2])
    /// and Quality (ASCII[8] at <paramref name="qualityAt"/> in the note), whose stored offset is
    /// <paramref name="qualityOffset"/>.
    /// </summary>
    private static void Ifd(byte[] note, bool big, int ifdAt, int qualityAt, long qualityOffset)
    {
        var w = new Writer(note, big);
        w.U16(ifdAt, 2);
        w.Entry(ifdAt + 2, 0x0002, 3, 2, 0);
        w.U16(ifdAt + 2 + 8, 0); w.U16(ifdAt + 2 + 10, 200);
        w.Entry(ifdAt + 14, 0x0004, 2, 8, qualityOffset);
        w.U32(ifdAt + 26, 0);
        Encoding.ASCII.GetBytes("FINE   \0").CopyTo(note, qualityAt);
    }

    /// <summary>"Nikon\0" 02 10 00 00, an embedded TIFF header in <paramref name="innerBig"/> order, IFD at 8.</summary>
    private static byte[] Nikon3(bool innerBig, long? qualityOffset = null)
    {
        var note = new byte[10 + 8 + 30 + 8];
        "Nikon\0"u8.CopyTo(note);
        note[6] = 0x02; note[7] = 0x10;
        var w = new Writer(note, innerBig);
        note[10] = note[11] = (byte)(innerBig ? 'M' : 'I');
        w.U16(12, 42); w.U32(14, 8);
        Ifd(note, innerBig, 18, 48, qualityOffset ?? 38); // 48 is 38 past the embedded header at 10.
        return note;
    }

    private static string Text(ExifValue value)
    {
        byte[] raw = new byte[value.ByteLength];
        value.CopyRawTo(raw);
        return Encoding.ASCII.GetString(raw).TrimEnd('\0', ' ');
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Nikon3UsesTheEmbeddedHeaderForOrderAndOffsets(bool outerBig, bool innerBig)
    {
        MakerNote? note = ExifTiffReader.Read(Wrap(outerBig, Nikon3(innerBig))).Metadata.MakerNote;
        Assert.NotNull(note);
        Assert.Equal(MakerNoteFormat.Nikon3, note.Format);
        Assert.Equal(innerBig ? Endianness.BigEndian : Endianness.LittleEndian, note.ByteOrder);
        Assert.True(note.TryGetEntry(0x0004, out MakerNoteEntry? quality));
        Assert.Equal("Quality", quality.Name);
        Assert.Equal("FINE", Text(quality.Value));
        Assert.True(note.TryGetEntry(0x0002, out MakerNoteEntry? iso));
        Assert.Equal("ISOSpeed", iso.Name);
        Assert.Equal(2u, iso.Value.Count);
    }

    [Theory]
    [InlineData(1000L)]   // Beyond the MakerNote.
    [InlineData(-10L)]    // Before the embedded header (wraps to a huge unsigned offset).
    public void Nikon3OffsetsMustStayInsideTheMakerNote(long offset)
    {
        Assert.Null(ExifTiffReader.Read(Wrap(false, Nikon3(false, offset))).Metadata.MakerNote);
    }

    [Fact]
    public void Nikon3WithABrokenEmbeddedHeaderIsOpaque()
    {
        byte[] note = Nikon3(false);
        note[12] = 43;
        ExifMetadata metadata = ExifTiffReader.Read(Wrap(false, note)).Metadata;
        Assert.Null(metadata.MakerNote);
        Assert.True(metadata.TryGetEntry(new(ExifDirectoryKind.Exif, 0x927C), out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nikon2HasASignatureAndTiffRelativeOffsets(bool big)
    {
        var note = new byte[8 + 30 + 8];
        "Nikon\0\u0001\0"u8.CopyTo(note);
        Ifd(note, big, 8, 38, MakerNoteStart + 38);
        MakerNote? decoded = ExifTiffReader.Read(Wrap(big, note)).Metadata.MakerNote;
        Assert.NotNull(decoded);
        Assert.Equal(MakerNoteFormat.Nikon2, decoded.Format);
        Assert.True(decoded.TryGetEntry(0x0004, out MakerNoteEntry? quality));
        Assert.Equal("FINE", Text(quality.Value));
        Assert.Equal("ColorMode", Core.MakerNotes.NikonMakerNoteNames.Nikon2(0x0004));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nikon1IsAHeaderlessDirectory(bool big)
    {
        var note = new byte[30 + 8];
        Ifd(note, big, 0, 30, MakerNoteStart + 30);
        MakerNote? decoded = ExifTiffReader.Read(Wrap(big, note)).Metadata.MakerNote;
        Assert.NotNull(decoded);
        Assert.Equal(MakerNoteFormat.Nikon1, decoded.Format);
        Assert.True(decoded.TryGetEntry(0x0004, out MakerNoteEntry? quality));
        Assert.Equal("Quality", quality.Name);
        Assert.Equal("FINE", Text(quality.Value));
    }

    [Fact]
    public void ANikonSignatureWithAnUnknownVersionIsOpaque()
    {
        byte[] note = Nikon3(false);
        note[6] = 0x07;
        Assert.Null(ExifTiffReader.Read(Wrap(false, note)).Metadata.MakerNote);
    }

    [Fact]
    public void TheMakeDecidesTheVendor()
    {
        Assert.Null(ExifTiffReader.Read(Wrap(false, Nikon3(false), make: "Sony ")).Metadata.MakerNote);
        Assert.NotNull(ExifTiffReader.Read(Wrap(false, Nikon3(false), make: "Nikon")).Metadata.MakerNote);
    }

    [Fact]
    public void NikonNameTablesAreSortedAndUnique()
    {
        foreach (var table in new[] { Core.MakerNotes.NikonMakerNoteNames.Nikon1Names,
                     Core.MakerNotes.NikonMakerNoteNames.Nikon2Names, Core.MakerNotes.NikonMakerNoteNames.Nikon3Names })
        {
            var ids = table.Select(t => t.Id).ToArray();
            Assert.Equal(ids.OrderBy(i => i), ids);
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.Equal(ids.Length, table.Select(t => t.Name).Distinct().Count());
        }
    }
}
