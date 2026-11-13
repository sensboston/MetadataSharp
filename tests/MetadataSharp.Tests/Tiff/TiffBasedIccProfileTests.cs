using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Tests.Containers;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

/// <summary>
/// Writing the ICC profile of a plain TIFF: tag 0x8773 of the primary directory, through the
/// writer that moves no byte of the source. Every case runs on a baseline TIFF, a TIFF of several
/// pages with a reduced image and a TIFF as a camera writes it, in both byte orders. A DNG or raw
/// layout without an existing valid primary ICC field is refused: insertion would assign a profile
/// to a directory whose image role is not proven. A valid existing field is an explicit target for
/// replacement or removal.
/// </summary>
public sealed class TiffBasedIccProfileTests
{
    private const int IccTag = 0x8773;
    private static readonly ExifTagKey DateTimeOriginal = new(ExifDirectoryKind.Exif, 0x9003);

    public static TheoryData<string, bool> Layouts() => Both(TiffBasedFixture.Plain);

    /// <summary>The authored DNG and raw layouts: all the layouts of the writer that are not plain TIFF files.</summary>
    public static TheoryData<string, bool> RawLayouts() => Both(TiffBasedFixture.Names.Except(TiffBasedFixture.Plain));

    private static TheoryData<string, bool> Both(IEnumerable<string> names)
    {
        var data = new TheoryData<string, bool>();
        foreach (string name in names)
        {
            data.Add(name, false);
            data.Add(name, true);
        }
        return data;
    }

    private static byte[] Profile(int size, int seed = 1) => IccProfileTests.Profile(size, seed);

    private static byte[] Save(byte[] file, byte[]? profile, TiffReadOptions? options = null)
    {
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file), options);
        using var output = new MemoryStream();
        document.SaveIccProfile(output, profile);
        return output.ToArray();
    }

    /// <summary>The refusal of a save, and the proof that it left the destination empty.</summary>
    private static MetadataErrorCode Refusal(byte[] file, byte[]? profile, TiffReadOptions? options = null)
    {
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file), options);
        using var output = new MemoryStream();
        MetadataException refusal = Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, profile));
        Assert.Equal(0, output.Length);
        return refusal.Code;
    }

    /// <summary>The refusal of a file for what it is, with a profile and with none: nothing is written either time.</summary>
    private static string Unsuitable(byte[] file)
    {
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file));
        string? message = null;
        foreach (byte[]? profile in new[] { Profile(560), null })
        {
            using var output = new MemoryStream();
            MetadataException refusal = Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, profile));
            Assert.Equal(MetadataErrorCode.UnsupportedFeature, refusal.Code);
            Assert.Equal(0, output.Length);
            Assert.True(message is null || message == refusal.Message);
            message = refusal.Message;
        }
        return message!;
    }

    /// <summary>
    /// The file with one entry of a directory made another: the tag, the type and the one value it
    /// holds in the entry itself. The order of the tags is the business of the caller.
    /// </summary>
    private static byte[] WithEntry(byte[] file, bool big, long directory, int tag, int newTag, int type, uint value)
    {
        byte[] copy = (byte[])file.Clone();
        int entry = TiffBasedFixture.EntryAt(copy, big, directory, tag);
        Assert.True(entry >= 0);
        void U16(int at, int number)
        {
            if (big) BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(at), (ushort)number);
            else BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(at), (ushort)number);
        }
        U16(entry, newTag);
        U16(entry + 2, type);
        copy.AsSpan(entry + 4, 8).Clear();
        if (big) copy[entry + 7] = 1; else copy[entry + 4] = 1;
        if (type == 3) U16(entry + 8, (int)value);
        else if (big) BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(entry + 8), value);
        else BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(entry + 8), value);
        return copy;
    }

    /// <summary>The entry of the profile in the primary directory of the file: its type, its count and where its value is.</summary>
    private static (int Entry, int Type, int Count, int At) Stored(byte[] file, bool big)
    {
        int entry = TiffBasedWriteTests.PrimaryEntry(file, big, IccTag);
        return (entry, TiffBasedFixture.U16(file, big, entry + 2), (int)TiffBasedFixture.U32(file, big, entry + 4),
            (int)TiffBasedFixture.U32(file, big, entry + 8));
    }

    private static bool HasTag(byte[] file, bool big) =>
        TiffBasedFixture.EntryAt(file, big, TiffBasedWriteTests.Primary(file, big), IccTag) >= 0;

    private static string Date(byte[] file)
    {
        Assert.True(TiffExif.Read(file).TryGetEntry(DateTimeOriginal, out ExifEntry? entry));
        byte[] raw = new byte[entry.Value.ByteLength];
        entry.Value.CopyRawTo(raw);
        return Encoding.ASCII.GetString(raw).TrimEnd('\0');
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AFileWithoutAProfileIsGivenOneAsTag8773OfThePrimaryDirectory(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        byte[] source = fixture.Bytes, profile = Profile(560);
        Assert.Null(IccProfiles.Read(source));
        Assert.False(HasTag(source, big));

        byte[] output = Save(source, profile);

        // The directory gains an entry, so a copy of it follows the source, and the header leads to it.
        TiffBasedWriteTests.AssertSourceKeptExcept(source, output, (4, 4));
        (_, int type, int count, int at) = Stored(output, big);
        Assert.Equal(7, type);
        Assert.Equal(profile.Length, count);
        Assert.True(at >= source.Length && at + count <= output.Length);
        Assert.Equal(0, at & 1);
        Assert.Equal(profile, output[at..(at + count)]);
        Assert.Equal(profile, IccProfiles.Read(output));
        Assert.Equal("2020:01:02 03:04:05", Date(output));
        Assert.Equal(TiffExif.Read(source).Entries.Count + 1, TiffExif.Read(output).Entries.Count);
        TiffBasedWriteTests.AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AProfileNoLongerThanTheStoredOneIsWrittenWhereThatOneIs(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        byte[] held = Save(fixture.Bytes, Profile(560));
        (int entry, _, _, int at) = Stored(held, big);

        // The same length: the bytes of the profile, and nothing else.
        byte[] same = Save(held, Profile(560, 9));
        Assert.Equal(held.Length, same.Length);
        TiffBasedWriteTests.AssertSourceKeptExcept(held, same, (at, 560));
        Assert.Equal(Profile(560, 9), IccProfiles.Read(same));
        TiffBasedWriteTests.AssertImageAndMakerNoteUntouched(fixture, same);

        // A shorter one: its bytes, and the count of the entry. What the old one had after them stays.
        byte[] shorter = Save(held, Profile(400, 9));
        Assert.Equal(held.Length, shorter.Length);
        TiffBasedWriteTests.AssertSourceKeptExcept(held, shorter, (entry + 4, 4), (at, 400));
        Assert.Equal((entry, 7, 400, at), Stored(shorter, big));
        Assert.Equal(Profile(400, 9), IccProfiles.Read(shorter));
        TiffBasedWriteTests.AssertImageAndMakerNoteUntouched(fixture, shorter);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ALongerProfileIsAppendedAndTheOldOneStaysInTheFile(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        byte[] held = Save(fixture.Bytes, Profile(560));
        (int entry, _, _, int at) = Stored(held, big);

        byte[] output = Save(held, Profile(2000, 9));

        // The directory keeps its entry count, so it stays where it is and one entry changes.
        TiffBasedWriteTests.AssertSourceKeptExcept(held, output, (entry + 4, 8));
        int start = held.Length + (held.Length & 1);
        Assert.Equal((entry, 7, 2000, start), Stored(output, big));
        Assert.Equal(start + 2000, output.Length);
        Assert.Equal(Profile(560), output[at..(at + 560)]);
        Assert.Equal(Profile(2000, 9), IccProfiles.Read(output));
        TiffBasedWriteTests.AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void RemovingTheProfileRemovesItsTagAndLeavesItsBytes(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        byte[] held = Save(fixture.Bytes, Profile(560));
        (_, _, _, int at) = Stored(held, big);

        byte[] output = Save(held, null);

        // The directory is rewritten where it is; nothing is appended. An edit is not an erasure:
        // the profile is in the file still, and no tag leads to it.
        Assert.Equal(held.Length, output.Length);
        Assert.False(HasTag(output, big));
        Assert.Null(IccProfiles.Read(output));
        Assert.Equal(Profile(560), output[at..(at + 560)]);
        Assert.Equal(TiffExif.Read(fixture.Bytes).Entries.Count, TiffExif.Read(output).Entries.Count);
        Assert.Equal("2020:01:02 03:04:05", Date(output));
        TiffBasedWriteTests.AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AFileThatIsTheResultAlreadyIsCopiedAsItIs(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        // No profile to remove.
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, null));
        // The profile it has.
        byte[] held = Save(fixture.Bytes, Profile(560));
        Assert.Equal(held, Save(held, Profile(560)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTagKeepsTheTypeByteAndBecomesUndefinedOtherwise(bool big)
    {
        byte[] make = Encoding.ASCII.GetBytes("TestCompany\0");

        // BYTE, as some writers have it: the reader takes it, and the writer keeps it.
        byte[] bytes = TiffBasedFixture.Flat(big, (0x010F, 2, make), (IccTag, 1, Profile(560)));
        Assert.Equal(Profile(560), IccProfiles.Read(bytes));
        Assert.Equal(bytes, Save(bytes, Profile(560)));
        byte[] output = Save(bytes, Profile(560, 9));
        Assert.Equal(1, Stored(output, big).Type);
        Assert.Equal(bytes.Length, output.Length);
        Assert.Equal(Profile(560, 9), IccProfiles.Read(output));
        output = Save(bytes, Profile(2000, 9));
        Assert.Equal(1, Stored(output, big).Type);
        Assert.Equal(Profile(2000, 9), IccProfiles.Read(output));

        // A tag of a type that is no profile to the reader, with the very bytes of one: it is
        // written as a profile, where it is.
        byte[] text = TiffBasedFixture.Flat(big, (0x010F, 2, make), (IccTag, 2, Profile(560)));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, TiffBasedWriteTests.Code(() => IccProfiles.Read(text)));
        output = Save(text, Profile(560));
        int entry = TiffBasedWriteTests.PrimaryEntry(text, big, IccTag);
        TiffBasedWriteTests.AssertSourceKeptExcept(text, output, (entry + 2, 2));
        Assert.Equal(7, Stored(output, big).Type);
        Assert.Equal(Profile(560), IccProfiles.Read(output));
        // And removed like any other.
        Assert.False(HasTag(Save(text, null), big));
    }

    [Fact]
    public void AProfileThatIsNotOneIsRefusedWhateverTheFile()
    {
        byte[] source = TiffBasedFixture.Tiff(false).Bytes;
        byte[] padded = [.. Profile(560), 0, 0];
        byte[] unsigned = Profile(560);
        unsigned[36] = (byte)'x';

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(source, Profile(560)[..100]));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(source, padded));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(source, unsigned));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(source, []));
        // Even for a file the writer would refuse anyway, the profile is looked at first.
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(TiffBasedFixture.Rw2().Bytes, padded));
    }

    [Fact]
    public void TheBudgetsBoundTheProfileAndTheOutput()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Tiff(false);
        // A value of the default limits of the library is what a JPEG segment holds; the limits of
        // a TIFF-based document are those of a whole file unless others are given.
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Refusal(fixture.Bytes, Profile(70_000), new TiffReadOptions { Limits = new MetadataLimits() }));
        Assert.Equal(Profile(70_000), IccProfiles.Read(Save(fixture.Bytes, Profile(70_000))));

        // A profile is appended to a file that has none: the output may not pass the input budget.
        var options = new TiffReadOptions { Limits = new MetadataLimits { MaxInputBytes = fixture.Bytes.Length + 200 } };
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(fixture.Bytes, Profile(560), options));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, null, options));
    }

    [Fact]
    public void AFileThatCannotBeAppendedToTakesNoNewProfile()
    {
        // A strip without its byte count runs to the end of the file: what is appended would
        // become part of it. There is still no profile to remove.
        byte[] open = TiffBasedFixture.Flat(false, (0x010F, 2, Encoding.ASCII.GetBytes("TestCompany\0")), (0x0111, 4, new byte[4]));
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Refusal(open, Profile(560)));
        Assert.Equal(open, Save(open, null));

        // Image data addressed past the end of the file.
        TiffBasedFixture past = TiffBasedFixture.Tiff(false, stripLength: 1000);
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Refusal(past.Bytes, Profile(560)));

        // A field type of unknown width: the bytes it addresses are unknown, and so is whether
        // the file has a profile to remove.
        TiffBasedFixture unknown = TiffBasedFixture.Tiff(false, extraType: 99);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(unknown.Bytes, Profile(560)));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(unknown.Bytes, null));

        byte[] bigTiff = TiffBasedFixture.Tiff(false).Bytes;
        bigTiff[2] = 43;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(bigTiff, Profile(560)));
    }

    [Theory]
    [MemberData(nameof(RawLayouts))]
    public void ADngAndARawFileAreRefusedWithAProfileAndWithNone(string name, bool big)
    {
        byte[] source = TiffBasedFixture.Create(name, big).Bytes;

        string reason = name switch
        {
            "dng" => "DNG (DNGVersion",
            "cr2" => "signature of a CR2",
            "orf" => "ORF or RW2",
            _ => "reduced image (NewSubfileType)"
        };
        string message = Unsuitable(source);
        Assert.StartsWith("The ICC profile of the first directory is not written or removed: ", message);
        Assert.Contains(reason, message);

        // The other saves of the document are what they were.
        var edit = new ExifEdit();
        edit.Set(DateTimeOriginal, ExifValue.FromRaw(TiffFieldType.Ascii, 20, Endianness.LittleEndian,
            Encoding.ASCII.GetBytes("2021:06:07 08:09:10\0")));
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(source));
        using var edited = new MemoryStream();
        document.SaveExif(edited, edit);
        Assert.Equal("2021:06:07 08:09:10", Date(edited.ToArray()));
    }

    [Fact]
    public void AnRw2IsRefusedForItsHeaderBeforeAnythingElseIsAsked()
    {
        // The file could not be appended to either; what it is comes first.
        Assert.Contains("ORF or RW2", Unsuitable(TiffBasedFixture.Rw2().Bytes));
        Assert.Contains("ORF or RW2", Unsuitable(TiffBasedFixture.Vendor("orf", false).Bytes));
        Assert.Contains("ORF or RW2", Unsuitable(TiffBasedFixture.Vendor("orf", true).Bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheVersionOfADngInThePrimaryDirectoryIsEnough(bool big)
    {
        byte[] make = Encoding.ASCII.GetBytes("TestCompany\0");
        byte[] plain = TiffBasedFixture.Flat(big, (0x010F, 2, make), (0x0131, 2, Encoding.ASCII.GetBytes("Writer1\0")));
        Assert.Equal(Profile(560), IccProfiles.Read(Save(plain, Profile(560))));

        byte[] dng = TiffBasedFixture.Flat(big, (0x010F, 2, make), (0xC612, 1, new byte[] { 1, 4, 0, 0 }));
        Assert.Contains("DNG", Unsuitable(dng));

        // A profile already stored in the primary DNG directory is an explicit target; the
        // full-directory fixture below pins replacement and removal with image bytes held.
        byte[] held = TiffBasedFixture.Flat(big, (0x010F, 2, make), (IccTag, 7, Profile(560)), (0xC612, 1, new byte[] { 1, 4, 0, 0 }));
        Assert.Equal(Profile(560), IccProfiles.Read(held));
        byte[] replacement = Profile(560, 9);
        Assert.Equal(replacement, IccProfiles.Read(Save(held, replacement)));
        Assert.Null(IccProfiles.Read(Save(held, null)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExistingDngPrimaryIccFieldIsAProvenReplaceOrRemoveTarget(bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Dng(big, iccProfile: Profile(560));
        byte[] source = fixture.Bytes;
        Assert.Equal(Profile(560), IccProfiles.Read(source));

        byte[] replacement = Save(source, Profile(560, 9));
        Assert.Equal(Profile(560, 9), IccProfiles.Read(replacement));
        TiffBasedWriteTests.AssertImageAndMakerNoteUntouched(fixture, replacement);

        byte[] removed = Save(source, null);
        Assert.Null(IccProfiles.Read(removed));
        TiffBasedWriteTests.AssertImageAndMakerNoteUntouched(fixture, removed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APrimaryDirectoryOfAReducedImageIsEnough(bool big)
    {
        byte[] source = TiffBasedFixture.Tiff(big).Bytes;
        // ImageWidth, the first entry, becomes NewSubfileType: bit 0 says reduced image, as LONG or SHORT.
        foreach ((int type, uint flags) in new[] { (4, 1u), (4, 3u), (4, 5u), (3, 1u) })
            Assert.Contains("reduced image (NewSubfileType)", Unsuitable(WithEntry(source, big, 8, 0x0100, 0x00FE, type, flags)));

        // The other bits say a page or a mask, and a full image has none.
        foreach ((int type, uint flags) in new[] { (4, 0u), (4, 2u), (4, 4u), (3, 2u) })
        {
            byte[] file = WithEntry(source, big, 8, 0x0100, 0x00FE, type, flags);
            Assert.Equal(Profile(560), IccProfiles.Read(Save(file, Profile(560))));
            Assert.Equal(file, Save(file, null));
        }

        // A reduced image further on, in the chain or in a sub-directory, is what a TIFF may have.
        byte[] pages = TiffBasedFixture.Pages(big).Bytes, camera = TiffBasedFixture.Camera(big).Bytes;
        Assert.Equal(1L, TiffBasedFixture.U32(pages, big, TiffBasedFixture.EntryAt(pages, big, 310, 0x00FE) + 8));
        Assert.Equal(1L, TiffBasedFixture.U32(camera, big, TiffBasedFixture.EntryAt(camera, big, 230, 0x00FE) + 8));
        Assert.Equal(Profile(560), IccProfiles.Read(Save(pages, Profile(560))));
        Assert.Equal(Profile(560), IccProfiles.Read(Save(camera, Profile(560))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawSensorDataInAnyImageDirectoryIsEnough(bool big)
    {
        const string raw = "raw sensor data";
        byte[] source = TiffBasedFixture.Tiff(big).Bytes;

        // The compression of a PEF, a NEF and an ARW.
        foreach (uint compression in new uint[] { 65535, 34713, 32767 })
            Assert.Contains(raw, Unsuitable(WithEntry(source, big, 8, 0x0103, 0x0103, 3, compression)));
        // A colour filter array or linear raw data, as SHORT or LONG.
        foreach ((int type, uint photometric) in new[] { (3, 32803u), (3, 34892u), (4, 32803u) })
            Assert.Contains(raw, Unsuitable(WithEntry(source, big, 8, 0x0103, 0x0106, type, photometric)));

        // The compressions and the colour spaces of a picture are none of these.
        foreach (uint compression in new uint[] { 1, 5, 8, 32773 })
        {
            byte[] file = WithEntry(source, big, 8, 0x0103, 0x0103, 3, compression);
            Assert.Equal(Profile(560), IccProfiles.Read(Save(file, Profile(560))));
        }
        foreach (uint photometric in new uint[] { 0, 1, 2, 3, 5, 6, 8 })
        {
            byte[] file = WithEntry(source, big, 8, 0x0103, 0x0106, 3, photometric);
            Assert.Equal(Profile(560), IccProfiles.Read(Save(file, Profile(560))));
        }

        // In a sub-directory, as a NEF or an ARW has it under a primary directory that says nothing,
        // and in a chained one.
        byte[] camera = TiffBasedFixture.Camera(big).Bytes, pages = TiffBasedFixture.Pages(big).Bytes;
        Assert.Contains(raw, Unsuitable(WithEntry(camera, big, 230, 0x0103, 0x0103, 3, 34713)));
        Assert.Contains(raw, Unsuitable(WithEntry(camera, big, 230, 0x0103, 0x0106, 3, 32803)));
        Assert.Contains(raw, Unsuitable(WithEntry(pages, big, 230, 0x0103, 0x0106, 3, 32803)));
        // The same numbers under tags of the Exif directory mean something else.
        Assert.Equal(Profile(560), IccProfiles.Read(Save(WithEntry(camera, big, 300, 0x829A, 0x0106, 3, 32803), Profile(560))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSignatureOfACr2IsEnoughAndTheNameOfAMakerIsNot(bool big)
    {
        // Nothing else in the layout says raw: without the two letters it is a TIFF with a gap after its header.
        byte[] cr2 = TiffBasedFixture.Cr2(big).Bytes;
        Assert.Contains("CR2", Unsuitable(cr2));
        byte[] plain = (byte[])cr2.Clone();
        plain[8] = plain[9] = 0;
        Assert.Equal(Profile(560), IccProfiles.Read(Save(plain, Profile(560))));

        // The letters where a value is are that value: a TIFF whose directory, at 24, follows the
        // one text it addresses, at 8.
        byte[] value = new byte[42];
        value[0] = value[1] = big ? (byte)0x4D : (byte)0x49;
        Encoding.ASCII.GetBytes("CR writes TIFF.\0").CopyTo(value, 8);
        int[] shorts = [2, 42, 24, 1, 26, 0x0131, 28, 2];
        uint[] longs = [4, 24, 30, 16, 34, 8];
        for (int i = 0; i < shorts.Length; i += 2)
        {
            if (big) BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(shorts[i]), (ushort)shorts[i + 1]);
            else BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(shorts[i]), (ushort)shorts[i + 1]);
        }
        for (int i = 0; i < longs.Length; i += 2)
        {
            if (big) BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan((int)longs[i]), longs[i + 1]);
            else BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan((int)longs[i]), longs[i + 1]);
        }
        Assert.Equal("CR", Encoding.ASCII.GetString(value, 8, 2));
        Assert.Equal(Profile(560), IccProfiles.Read(Save(value, Profile(560))));

        // A camera writes TIFF files too: the maker and the MakerNote of a NEF, and a primary
        // directory that is the image.
        byte[] camera = TiffBasedFixture.Camera(big).Bytes;
        Assert.StartsWith("NIKON", Encoding.ASCII.GetString(camera, 120, 5));
        Assert.Equal(Profile(560), IccProfiles.Read(Save(camera, Profile(560))));
    }

    [Fact]
    public void ARefusedFileLeavesNoFileAtThePath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.dng"), target = Path.Combine(directory, "target.dng");
            File.WriteAllBytes(source, TiffBasedFixture.Dng(false).Bytes);
            using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(source);
            Assert.Equal(MetadataErrorCode.UnsupportedFeature, TiffBasedWriteTests.Code(() => document.SaveIccProfileAs(target, Profile(560))));
            Assert.Equal(MetadataErrorCode.UnsupportedFeature, TiffBasedWriteTests.Code(() => document.SaveIccProfileAs(target, null)));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void TheOutputOfAProfileSaveTakesTheOtherEdits()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Camera(false);
        byte[] held = Save(fixture.Bytes, Profile(560));

        var edit = new ExifEdit();
        edit.Set(DateTimeOriginal, ExifValue.FromRaw(TiffFieldType.Ascii, 20, Endianness.LittleEndian,
            Encoding.ASCII.GetBytes("2021:06:07 08:09:10\0")));
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(held));
        using var edited = new MemoryStream();
        document.SaveExif(edited, edit);

        byte[] bytes = edited.ToArray();
        Assert.Equal("2021:06:07 08:09:10", Date(bytes));
        Assert.Equal(Profile(560), IccProfiles.Read(bytes));
        TiffBasedWriteTests.AssertImageAndMakerNoteUntouched(fixture, bytes);
    }

    [Fact]
    public async Task TheAsynchronousAndPathSavesWriteTheSameBytes()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Pages(false);
        byte[] profile = Profile(560), expected = Save(fixture.Bytes, profile);
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.tif"), target = Path.Combine(directory, "target.tif");
            await File.WriteAllBytesAsync(source, fixture.Bytes);
            await using (TiffBasedMetadataDocument document = await TiffBasedMetadataDocument.OpenAsync(source))
            {
                using var stream = new MemoryStream();
                await document.SaveIccProfileAsync(stream, profile);
                Assert.Equal(expected, stream.ToArray());

                await document.SaveIccProfileAsAsync(target, profile);
                Assert.Equal(expected, await File.ReadAllBytesAsync(target));
                // Neither the source nor an existing file is written over.
                await Assert.ThrowsAsync<IOException>(() => document.SaveIccProfileAsAsync(source, profile));
                await Assert.ThrowsAsync<IOException>(() => document.SaveIccProfileAsAsync(target, null));
                Assert.Throws<IOException>(() => document.SaveIccProfileAs(target, profile));
                // A refused save leaves no file.
                string refused = Path.Combine(directory, "refused.tif");
                Assert.Throws<MetadataException>(() => document.SaveIccProfileAs(refused, new byte[200]));
                Assert.False(File.Exists(refused));
            }
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(source));
            Assert.Equal(2, Directory.GetFiles(directory).Length);

            using TiffBasedMetadataDocument reopened = TiffBasedMetadataDocument.Open(target);
            string other = Path.Combine(directory, "other.tif");
            reopened.SaveIccProfileAs(other, null);
            Assert.Null(IccProfiles.Read(File.ReadAllBytes(other)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void TheProfileIsTakenWhenTheSaveIsCalledAndTheDestinationRulesHold()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Tiff(false);
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(fixture.Bytes));

        Assert.Throws<ArgumentNullException>(() => document.SaveIccProfile(null!, Profile(560)));
        using var used = new MemoryStream(new byte[] { 1, 2, 3 });
        Assert.Throws<ArgumentException>(() => document.SaveIccProfile(used, Profile(560)));
        Assert.Equal(new byte[] { 1, 2, 3 }, used.ToArray());

        // The document is as it was opened: a save does not change what the next one starts from.
        using var first = new MemoryStream();
        document.SaveIccProfile(first, Profile(560));
        using var second = new MemoryStream();
        document.SaveIccProfile(second, null);
        Assert.Equal(fixture.Bytes, second.ToArray());

        document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => document.SaveIccProfile(new MemoryStream(), Profile(560)));
    }
}
