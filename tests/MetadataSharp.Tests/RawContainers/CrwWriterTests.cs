using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Formats.Crw;
using Xunit;
using static MetadataSharp.Tests.RawContainers.RawContainerTests;

namespace MetadataSharp.Tests.RawContainers;

/// <summary>The writer of Canon CRW files, on authored files: tags and the comment go back into the records they were read from.</summary>
public sealed class CrwWriterTests
{
    private static readonly ExifTagKey Make = new(ExifDirectoryKind.Ifd0, 0x010F);
    private static readonly ExifTagKey Model = new(ExifDirectoryKind.Ifd0, 0x0110);
    private static readonly ExifTagKey Orientation = new(ExifDirectoryKind.Ifd0, 0x0112);
    private static readonly ExifTagKey Width = new(ExifDirectoryKind.Exif, 0xA002);
    private static readonly ExifTagKey Height = new(ExifDirectoryKind.Exif, 0xA003);
    private static readonly ExifTagKey Date = new(ExifDirectoryKind.Exif, 0x9003);
    private static readonly ExifTagKey ColorSpace = new(ExifDirectoryKind.Exif, 0xA001);
    private static readonly ExifTagKey FNumber = new(ExifDirectoryKind.Exif, 0x829D);

    private static readonly byte[] Unmapped = [9, 8, 7, 6, 5];

    /// <summary>A file with the directories where a camera has them, and a record no tag is mapped to.</summary>
    private static byte[] Sample() => Crw(
        (0x2008, [0xFF, 0xD8, 0xFF, 0xD9]),
        (0x2007, [0xFF, 0xD8, 1, 2, 0xFF, 0xD9]),
        (0x300A, Heap(
            (0x0805, "A comment\0\0\0"u8.ToArray()),
            (0x180E, [.. Le32(1000000000), .. Le32(7), .. Le32(9)]),
            (0x1810, [.. Le32(2272), .. Le32(1704), .. Le32(0), .. Le32(270), .. Le32(8), .. Le32(3), .. Le32(0)]),
            (0x5817, Le32(1171771)),
            (0x0816, Unmapped),
            (0x2807, Heap(
                (0x080A, "Canon\0Canon PowerShot S40\0"u8.ToArray()),
                (0x0810, "Owner\0padding"u8.ToArray()))),
            (0x300B, Heap(
                (0x102A, ShotInfo()),
                (0x102D, Shorts16(8, 2, 0, 5)),
                (0x50B4, Le16(1)))))));

    private static byte[] Save(byte[] file, Action<ExifEdit> change)
    {
        var edit = new ExifEdit();
        change(edit);
        using CrwMetadataDocument document = CrwMetadataDocument.Open(new MemoryStream(file));
        var output = new MemoryStream();
        document.SaveExif(output, edit);
        return output.ToArray();
    }

    private static byte[] SaveComment(byte[] file, string? comment)
    {
        using CrwMetadataDocument document = CrwMetadataDocument.Open(new MemoryStream(file));
        var output = new MemoryStream();
        document.SaveComment(output, comment);
        return output.ToArray();
    }

    private static MetadataErrorCode Refusal(Action<ExifEdit> change) =>
        Assert.Throws<MetadataException>(() => Save(Sample(), change)).Code;

    private static string Ascii(CrwMetadata crw, ExifTagKey key)
    {
        Assert.True(crw.Exif!.TryGetEntry(key, out ExifEntry? entry));
        return Encoding.ASCII.GetString(entry.Value.RawBytes);
    }

    private static uint Number(CrwMetadata crw, ExifTagKey key)
    {
        Assert.True(crw.Exif!.TryGetEntry(key, out ExifEntry? entry));
        byte[] bytes = entry.Value.RawBytes.ToArray();
        return entry.Value.FieldType == TiffFieldType.Short ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    /// <summary>The tags of a file as text, without the ones named.</summary>
    private static string[] Others(CrwMetadata crw, params ExifTagKey[] named) =>
        crw.Exif!.Entries.Where(entry => !named.Contains(entry.Key))
            .Select(entry => $"{entry.Key.Directory} {entry.Key.TagId:X4} {entry.Value.FieldType} {Convert.ToHexString(entry.Value.RawBytes)}").ToArray();

    private static int IndexOf(byte[] file, byte[] bytes) => file.AsSpan().IndexOf(bytes);

    [Fact]
    public void TheDocumentGivesWhatTheReaderGives()
    {
        using CrwMetadataDocument document = CrwMetadataDocument.Open(new MemoryStream(Sample()));

        Assert.Equal("A comment", document.Comment);
        Assert.True(document.Exif!.TryGetEntry(Model, out _));
    }

    [Fact]
    public void AnEditOfNothingWritesTheRecordsAsTheyAreInTheLayoutOfTheReference()
    {
        byte[] file = Sample();

        byte[] output = Save(file, _ => { });

        // The authored file does not pad its values to an even length; the writer does, as the reference does.
        Assert.True(output.Length > file.Length);
        Assert.Equal(file.AsSpan(0, 14).ToArray(), output.AsSpan(0, 14).ToArray());
        Assert.Equal(Others(CrwMetadata.Read(file)), Others(CrwMetadata.Read(output)));
        Assert.Equal("A comment", CrwMetadata.Read(output).Comment);
        // What the writer wrote it writes again byte for byte.
        Assert.Equal(output, Save(output, _ => { }));
    }

    [Fact]
    public void TheMakeAndTheModelAreWrittenToTheirOneRecord()
    {
        byte[] file = Sample();

        byte[] output = Save(file, edit => edit.Set(Model, "Canon PowerShot G1"));

        CrwMetadata crw = CrwMetadata.Read(output);
        Assert.Equal("Canon\0", Ascii(crw, Make));
        Assert.Equal("Canon PowerShot G1\0", Ascii(crw, Model));
        Assert.True(IndexOf(output, "Canon\0Canon PowerShot G1\0"u8.ToArray()) > 0);
        Assert.Equal(Others(CrwMetadata.Read(file), Model), Others(crw, Model));
        Assert.Equal("A comment", crw.Comment);
        // A record no tag is mapped to keeps its bytes.
        Assert.True(IndexOf(output, Unmapped) > 0);
    }

    [Fact]
    public void TheMakeAndTheModelAreRemovedTogetherOnly()
    {
        byte[] output = Save(Sample(), edit => { edit.Remove(Make); edit.Remove(Model); });

        CrwMetadata crw = CrwMetadata.Read(output);
        Assert.False(crw.Exif!.TryGetEntry(Make, out _));
        Assert.False(crw.Exif.TryGetEntry(Model, out _));
        // The directory still holds the owner, so it stays.
        Assert.True(crw.Exif.MakerNote!.TryGetEntry(0x0009, out _));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(edit => edit.Remove(Make)));
    }

    [Fact]
    public void TheFieldsOfTheImageRecordAreSetInPlace()
    {
        byte[] file = Sample();

        byte[] output = Save(file, edit =>
        {
            edit.Set(Orientation, (ushort)6);
            edit.Set(Width, 1000u);
            edit.Set(Height, 750u);
        });

        CrwMetadata crw = CrwMetadata.Read(output);
        Assert.Equal(6u, Number(crw, Orientation));
        Assert.Equal(1000u, Number(crw, Width));
        Assert.Equal(750u, Number(crw, Height));
        // The record: width, height, a field that is kept, the rotation in degrees, and three more that are kept.
        byte[] record = [.. Le32(1000), .. Le32(750), .. Le32(0), .. Le32(90), .. Le32(8), .. Le32(3), .. Le32(0)];
        Assert.True(IndexOf(output, record) > 0);
        Assert.Equal(Save(file, _ => { }).Length, output.Length);
        Assert.Equal(Others(CrwMetadata.Read(file), Orientation, Width, Height), Others(crw, Orientation, Width, Height));
    }

    [Fact]
    public void TheDateIsStoredAsSecondsAndTheRestOfItsRecordIsKept()
    {
        byte[] output = Save(Sample(), edit => edit.Set(Date, "2005:04:23 17:54:36"));

        Assert.Equal("2005:04:23 17:54:36\0", Ascii(CrwMetadata.Read(output), Date));
        Assert.True(IndexOf(output, [.. Le32(0x426A8BDC), .. Le32(7), .. Le32(9)]) > 0);

        byte[] removed = Save(Sample(), edit => edit.Remove(Date));
        Assert.False(CrwMetadata.Read(removed).Exif!.TryGetEntry(Date, out _));
    }

    [Fact]
    public void AValueThatLayInItsEntryMovesToTheHeapWhenItsLengthIsAnother()
    {
        byte[] output = Save(Sample(), edit => edit.Set(ColorSpace, (ushort)2));

        CrwMetadata crw = CrwMetadata.Read(output);
        Assert.True(crw.Exif!.TryGetEntry(ColorSpace, out ExifEntry? entry));
        Assert.Equal(1u, entry.Value.Count);
        Assert.Equal(2u, Number(crw, ColorSpace));

        byte[] removed = Save(Sample(), edit => edit.Remove(ColorSpace));
        Assert.False(CrwMetadata.Read(removed).Exif!.TryGetEntry(ColorSpace, out _));
    }

    [Fact]
    public void ATagOfTheCanonNoteIsWrittenToItsRecord()
    {
        byte[] file = Sample();

        byte[] output = Save(file, edit => edit.SetTextByKey("Exif.Canon.OwnerName", "A Longer Owner Name"));

        CrwMetadata crw = CrwMetadata.Read(output);
        Assert.True(crw.Exif!.MakerNote!.TryGetEntry(0x0009, out MakerNoteEntry? owner));
        Assert.Equal("A Longer Owner Name\0", Encoding.ASCII.GetString(owner.Value.RawBytes));
        Assert.Equal(Others(CrwMetadata.Read(file)), Others(crw));
        Assert.Equal(
            CrwMetadata.Read(file).Exif!.MakerNote!.Entries.Where(entry => entry.TagId != 0x0009).Select(entry => Convert.ToHexString(entry.Value.RawBytes)),
            crw.Exif.MakerNote.Entries.Where(entry => entry.TagId != 0x0009).Select(entry => Convert.ToHexString(entry.Value.RawBytes)));
    }

    [Fact]
    public void AFieldOfACanonArrayIsWrittenIntoTheArray()
    {
        byte[] output = Save(Sample(), edit => edit.SetTextByKey("Exif.CanonCs.Macro", "1"));

        Assert.True(IndexOf(output, Shorts16(8, 1, 0, 5)) > 0);
        Assert.True(IndexOf(output, Shorts16(8, 2, 0, 5)) < 0);
    }

    [Fact]
    public void TheFileNumberIsThePrefixOfItsRecord()
    {
        byte[] output = Save(Sample(), edit => edit.SetTextByKey("Exif.Canon.FileNumber", "1234567"));

        Assert.True(CrwMetadata.Read(output).Exif!.MakerNote!.TryGetEntry(0x0008, out MakerNoteEntry? number));
        Assert.Equal(1234567u, BinaryPrimitives.ReadUInt32LittleEndian(number.Value.RawBytes));
    }

    [Fact]
    public void TheThumbnailIsSetAndRemoved()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xDB, 0, 2, 0xFF, 0xD9];
        byte[] file = Sample();

        byte[] output = Save(file, edit => edit.SetThumbnail(jpeg));

        CrwMetadata crw = CrwMetadata.Read(output);
        Assert.Equal(jpeg.Length, crw.ThumbnailLength);
        Assert.Equal(jpeg, output.AsSpan((int)crw.ThumbnailOffset!.Value, jpeg.Length).ToArray());
        // The preview is another record and keeps its bytes.
        Assert.Equal(new byte[] { 0xFF, 0xD8, 1, 2, 0xFF, 0xD9 }, output.AsSpan((int)crw.PreviewOffset!.Value, 6).ToArray());

        CrwMetadata removed = CrwMetadata.Read(Save(file, edit => edit.RemoveThumbnail()));
        Assert.Null(removed.ThumbnailOffset);
        Assert.Equal(6, removed.PreviewLength);
    }

    [Fact]
    public void TheCommentKeepsTheLengthOfItsRecordOrGrowsIt()
    {
        byte[] file = Sample();

        byte[] shorter = SaveComment(file, "Short");
        Assert.Equal("Short", CrwMetadata.Read(shorter).Comment);
        Assert.Equal(Save(file, _ => { }).Length, shorter.Length);
        Assert.True(IndexOf(shorter, "Short\0\0\0\0\0\0\0"u8.ToArray()) > 0);

        byte[] longer = SaveComment(file, "A comment that is longer, with a letter of two bytes: é");
        Assert.Equal("A comment that is longer, with a letter of two bytes: é", CrwMetadata.Read(longer).Comment);
        Assert.Equal(Others(CrwMetadata.Read(file)), Others(CrwMetadata.Read(longer)));

        // Cleared, the record stays and holds nothing, as the reference leaves it.
        byte[] cleared = SaveComment(file, null);
        Assert.Null(CrwMetadata.Read(cleared).Comment);
        Assert.Equal(Save(file, _ => { }).Length, cleared.Length);
    }

    [Fact]
    public void ACommentIsAddedToAFileThatHasNoRecordForIt()
    {
        byte[] file = Crw((0x300A, Heap((0x0816, Unmapped))));

        byte[] output = SaveComment(file, "New");

        Assert.Equal("New", CrwMetadata.Read(output).Comment);
        Assert.True(IndexOf(output, Unmapped) > 0);
        Assert.Throws<ArgumentException>(() => SaveComment(file, "a\0b"));
    }

    [Fact]
    public void WhatHasNoRecordIsRefused()
    {
        // A tag the format has no record for.
        Assert.Equal(MetadataErrorCode.TagNotEditable, Refusal(edit => edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B), "An artist")));
        // A tag that is computed from the shot information.
        Assert.Equal(MetadataErrorCode.TagNotEditable, Refusal(edit => edit.Set(FNumber, new UnsignedRational(28, 10))));
        // The whole EXIF, a field of a record, an orientation that is no rotation, a date that is none.
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(edit => edit.RemoveAllExif()));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(edit => edit.Remove(Orientation)));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(edit => edit.Set(Orientation, (ushort)2)));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(edit => edit.Set(Date, "yesterday")));
    }

    [Fact]
    public void ADisposedDocumentAndAStreamThatIsNotEmptyAreRefused()
    {
        CrwMetadataDocument document = CrwMetadataDocument.Open(new MemoryStream(Sample()));
        Assert.Throws<ArgumentException>(() => document.SaveComment(new MemoryStream([1, 2, 3]), "x"));
        document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => document.SaveComment(new MemoryStream(), "x"));
    }

    [Fact]
    public void TheFileIsWrittenToAPath()
    {
        string directory = Directory.CreateTempSubdirectory("crw").FullName;
        try
        {
            string source = Path.Combine(directory, "a.crw"), target = Path.Combine(directory, "b.crw");
            System.IO.File.WriteAllBytes(source, Sample());
            using (CrwMetadataDocument document = CrwMetadataDocument.Open(source))
            {
                document.SaveCommentAs(target, "To a path");
                var edit = new ExifEdit();
                edit.Set(Orientation, (ushort)3);
                document.SaveExifAs(Path.Combine(directory, "c.crw"), edit);
            }
            Assert.Equal("To a path", CrwMetadata.Read(System.IO.File.ReadAllBytes(target)).Comment);
            Assert.Equal(3u, Number(CrwMetadata.Read(System.IO.File.ReadAllBytes(Path.Combine(directory, "c.crw"))), Orientation));
            Assert.Equal(Sample(), System.IO.File.ReadAllBytes(source));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
