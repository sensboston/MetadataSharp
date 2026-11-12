using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Tests.Containers;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

/// <summary>
/// Writing the ICC profile of TIFF-based files: tag 0x8773 of the primary directory, through the
/// writer that moves no byte of the source. Every case runs on a baseline TIFF, a DNG and a NEF-,
/// CR2-, ARW-, PEF- and ORF-like layout, in both byte orders.
/// </summary>
public sealed class TiffBasedIccProfileTests
{
    private const int IccTag = 0x8773;
    private static readonly ExifTagKey DateTimeOriginal = new(ExifDirectoryKind.Exif, 0x9003);

    public static TheoryData<string, bool> Layouts() => TiffBasedWriteTests.Layouts();

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
        // Raw data that runs to the end of the file: what is appended would become part of it.
        TiffBasedFixture raw = TiffBasedFixture.Rw2();
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Refusal(raw.Bytes, Profile(560)));
        Assert.Equal(raw.Bytes, Save(raw.Bytes, null));

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

    [Fact]
    public void TheOutputOfAProfileSaveTakesTheOtherEdits()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Nef(false);
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
        TiffBasedFixture fixture = TiffBasedFixture.Cr2(false);
        byte[] profile = Profile(560), expected = Save(fixture.Bytes, profile);
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.cr2"), target = Path.Combine(directory, "target.cr2");
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
                string refused = Path.Combine(directory, "refused.cr2");
                Assert.Throws<MetadataException>(() => document.SaveIccProfileAs(refused, new byte[200]));
                Assert.False(File.Exists(refused));
            }
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(source));
            Assert.Equal(2, Directory.GetFiles(directory).Length);

            using TiffBasedMetadataDocument reopened = TiffBasedMetadataDocument.Open(target);
            string other = Path.Combine(directory, "other.cr2");
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
