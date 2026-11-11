using MetadataSharp.Core;
using MetadataSharp.Formats.Cr3;
using Xunit;
using static MetadataSharp.Tests.Cr3.Cr3Fixture;

namespace MetadataSharp.Tests.Cr3;

/// <summary>The box walk: which boxes are opened, where the metadata boxes are found, what is refused.</summary>
public sealed class Cr3LayoutTests
{
    private static readonly byte[] Data = Box("mdat", new byte[16]);

    private static byte[] Canon(params byte[][] boxes) => Uuid(CanonBox, boxes);

    private static MetadataErrorCode Refusal(byte[] file, MetadataLimits? limits = null) =>
        Assert.Throws<MetadataException>(() => Cr3Reader.Locate(file, limits)).Code;

    [Fact]
    public void TheMetadataBoxesOfTheUsualLayoutAreFound()
    {
        var plan = new Layout();
        Cr3Fixture file = Create(plan);

        Cr3Layout found = Cr3Reader.Locate(file.Bytes);

        Assert.Equal(new Cr3Block(file.At["CMT1"], plan.Ifd0!.Length), found.Ifd0);
        Assert.Equal(new Cr3Block(file.At["CMT2"], plan.Exif!.Length), found.Exif);
        Assert.Equal(new Cr3Block(file.At["CMT3"], plan.MakerNote!.Length), found.MakerNote);
        Assert.Equal(new Cr3Block(file.At["CMT4"], plan.Gps!.Length), found.Gps);
        Assert.Equal(new Cr3Block(file.At["XMP"], plan.Xmp!.Length), found.Xmp);
        Assert.Equal(plan.Ifd0, file.Bytes.AsSpan(found.Ifd0.Start, found.Ifd0.Length).ToArray());
        Assert.Equal(plan.Xmp, file.Bytes.AsSpan(found.Xmp.Start, found.Xmp.Length).ToArray());
    }

    [Fact]
    public void ExtendedAndOpenEndedSizesAreWalked()
    {
        var plan = new Layout { WideMovie = true, OpenData = true };
        Cr3Fixture file = Create(plan);

        Cr3Layout found = Cr3Reader.Locate(file.Bytes);

        Assert.Equal(new Cr3Block(file.At["CMT1"], plan.Ifd0!.Length), found.Ifd0);
        Assert.Equal(new Cr3Block(file.At["CMT4"], plan.Gps!.Length), found.Gps);
        Assert.Equal(new Cr3Block(file.At["XMP"], plan.Xmp!.Length), found.Xmp);
        Assert.Equal(file.At["CMT1"] - 8, Create(new Layout { OpenData = true }).At["CMT1"]);
    }

    [Fact]
    public void ABoxTheFileDoesNotHaveIsNotPresent()
    {
        Cr3Layout found = Cr3Reader.Locate(Create(new Layout { Exif = null, Gps = null, Xmp = null }).Bytes);

        Assert.True(found.Ifd0.Present);
        Assert.False(found.Exif.Present);
        Assert.True(found.MakerNote.Present);
        Assert.False(found.Gps.Present);
        Assert.False(found.Xmp.Present);
    }

    [Fact]
    public void AFileWithoutAMovieBoxOrWithoutACanonBoxHasNothingToFind()
    {
        Assert.Equal(default(Cr3Layout), Cr3Reader.Locate(FileType()));
        Assert.Equal(default(Cr3Layout), Cr3Reader.Locate(Join(FileType(), Data)));
        Assert.Equal(default(Cr3Layout), Cr3Reader.Locate(Join(FileType(), Box("moov", Box("mvhd", new byte[100])), Data)));
        // A Canon box without directories, and a box of another identifier with boxes named like them.
        Assert.Equal(default(Cr3Layout), Cr3Reader.Locate(Join(FileType(), Box("moov", Canon(Box("CNCV", new byte[30]))))));
        Assert.Equal(default(Cr3Layout), Cr3Reader.Locate(Join(FileType(), Box("moov", Uuid(PreviewBox, Box("CMT1", new byte[8]))))));
    }

    [Fact]
    public void OnlyTheDocumentedPlacesAreRead()
    {
        byte[] tiff = Tiff(true, Shorts(0x0112, 1));
        // The Canon box at the top level, the XMP box inside the movie box, a CMT box beside the Canon box.
        byte[] file = Join(FileType(), Canon(Box("CMT1", tiff)), Box("moov", Uuid(XmpBox, Packet("inside")), Box("CMT2", tiff)), Data);

        Assert.Equal(default(Cr3Layout), Cr3Reader.Locate(file));
    }

    [Fact]
    public void AnEmptyMetadataBoxIsFoundWithNoBytes()
    {
        byte[] file = Join(FileType(), Box("moov", Canon(Box("CMT1"))), Uuid(XmpBox));

        Cr3Layout found = Cr3Reader.Locate(file);

        Assert.Equal(new Cr3Block(24 + 8 + 8 + 16 + 8, 0), found.Ifd0);
        Assert.Equal(new Cr3Block(file.Length, 0), found.Xmp);
    }

    public static TheoryData<string, byte[]> Malformed()
    {
        byte[] tiff = Tiff(true, Shorts(0x0112, 1));
        byte[] usual = Create().Bytes;
        byte[] shortHeader = Join(FileType(), new byte[] { 0, 0, 0, 4, (byte)'f', (byte)'r', (byte)'e', (byte)'e' });
        byte[] wideTruncated = Join(FileType(), new byte[] { 0, 0, 0, 1, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 0, 0, 0, 0 });
        byte[] wideTooLong = Join(FileType(), new byte[] { 0, 0, 0, 1, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 0, 0, 0, 0, 0, 0, 0, 17 });
        byte[] wideTooShort = Join(FileType(), new byte[] { 0, 0, 0, 1, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 0, 0, 0, 0, 0, 0, 0, 15 });
        // The CMT1 box states four bytes more than the Canon box has left.
        byte[] inner = Box("CMT1", tiff);
        inner[3] += 4;
        return new TheoryData<string, byte[]>
        {
            { "empty", [] },
            { "no file type box", Join(Data, FileType()) },
            { "another brand", Join(FileType("heic"), Box("moov", Canon(Box("CMT1", tiff)))) },
            { "file type box without a brand", Box("ftyp", new byte[4]) },
            { "file type box unaligned", Box("ftyp", "crx "u8.ToArray(), new byte[6]) },
            { "bytes after the last box", Join(usual, new byte[3]) },
            { "size below the header", shortHeader },
            { "size beyond the file", Join(FileType(), Be32(4096), "mdat"u8.ToArray(), new byte[16]) },
            { "extended header truncated", wideTruncated },
            { "extended size beyond the file", wideTooLong },
            { "extended size below the header", wideTooShort },
            { "child beyond its parent", Join(FileType(), Box("moov", Canon(inner))) },
            { "uuid without an identifier", Join(FileType(), Box("uuid", new byte[15])) },
            { "uuid without an identifier in the movie box", Join(FileType(), Box("moov", Box("uuid", new byte[8]))) },
            { "two movie boxes", Join(FileType(), Box("moov"), Box("moov")) },
            { "two Canon boxes", Join(FileType(), Box("moov", Canon(), Canon())) },
            { "two CMT1 boxes", Join(FileType(), Box("moov", Canon(Box("CMT1", tiff), Box("CMT1", tiff)))) },
            { "two CMT3 boxes", Join(FileType(), Box("moov", Canon(Box("CMT3", tiff), Box("CMT2", tiff), Box("CMT3", tiff)))) },
            { "two XMP boxes", Join(FileType(), Uuid(XmpBox, Packet("a")), Data, Uuid(XmpBox, Packet("b"))) },
        };
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedStructureIsRefused(string what, byte[] file)
    {
        Assert.False(string.IsNullOrEmpty(what));
        Assert.Equal(MetadataErrorCode.InvalidBmff, Refusal(file));
    }

    [Fact]
    public void ARefusalSaysWhere()
    {
        byte[] file = Join(FileType(), Box("moov", Canon(), Canon()));

        MetadataException refusal = Assert.Throws<MetadataException>(() => Cr3Reader.Locate(file));

        Assert.Equal(24L + 8 + 24, refusal.SourceOffset);
    }

    [Fact]
    public void TheBudgetsBoundTheInputTheBoxCountAndTheNesting()
    {
        byte[] file = Create().Bytes;
        var limits = new MetadataLimits();

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxInputBytes = file.Length - 1 }));
        Cr3Reader.Locate(file, limits with { MaxInputBytes = file.Length });

        // The walk reads the header of every box of the three levels it opens, and of no other.
        int boxes = 0;
        while (true)
        {
            try { Cr3Reader.Locate(file, limits with { MaxSegments = ++boxes }); break; }
            catch (MetadataException refusal) { Assert.Equal(MetadataErrorCode.LimitExceeded, refusal.Code); }
        }
        // ftyp, moov, uuid, uuid, mdat, free; uuid, mvhd, trak; CNCV, CCTP, CTBO, free, CMT1 to CMT4, THMB.
        Assert.Equal(6 + 3 + 9, boxes);

        // The Canon box is the third level.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxDirectoryDepth = 2 }));
        Cr3Reader.Locate(file, limits with { MaxDirectoryDepth = 3 });
    }

    [Fact]
    public void EveryTruncationIsFoundOrRefused()
    {
        byte[] file = Create().Bytes;
        Cr3Layout whole = Cr3Reader.Locate(file);
        for (int length = 0; length < file.Length; length++)
        {
            try
            {
                Cr3Layout found = Cr3Reader.Locate(file.AsSpan(0, length));
                // Only a cut on a top-level box boundary leaves a well-formed file, with the boxes before the cut.
                Assert.True(found.Ifd0 == whole.Ifd0 || !found.Ifd0.Present);
            }
            catch (MetadataException refusal)
            {
                Assert.Equal(MetadataErrorCode.InvalidBmff, refusal.Code);
            }
        }
    }

    [Fact]
    public void ADamagedByteIsFoundOrRefused()
    {
        byte[] file = Create().Bytes;
        foreach (byte damage in new byte[] { 0x00, 0x01, 0x7F, 0xFF })
            for (int at = 0; at < file.Length; at++)
            {
                byte[] copy = (byte[])file.Clone();
                copy[at] = damage;
                try
                {
                    Cr3Layout found = Cr3Reader.Locate(copy);
                    foreach (Cr3Block block in new[] { found.Ifd0, found.Exif, found.MakerNote, found.Gps, found.Xmp })
                        Assert.True(block.Start >= 0 && block.Length >= 0 && block.Start <= copy.Length - block.Length);
                }
                catch (MetadataException refusal)
                {
                    Assert.Equal(MetadataErrorCode.InvalidBmff, refusal.Code);
                }
            }
    }
}
