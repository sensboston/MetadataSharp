using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.WebP;
using Xunit;

namespace MetadataSharp.Tests.Containers;

/// <summary>
/// Synthetic files, one per place an ICC profile can be kept in. The profiles are a header that
/// passes the two checks the reader makes and a pattern after it; nothing here is a real profile.
/// </summary>
public sealed class IccProfileTests
{
    // ---- builders ----

    /// <summary>Bytes that state their own length where a profile does and carry the signature.</summary>
    internal static byte[] Profile(int size, int seed = 1)
    {
        byte[] bytes = new byte[size];
        for (int i = 0; i < size; i++) bytes[i] = (byte)(i * 7 + seed);
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)size);
        "acsp"u8.CopyTo(bytes.AsSpan(36));
        return bytes;
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    /// <summary>One APP2 segment of a profile: the identifier, the number from one, the count, the data.</summary>
    private static byte[] Chunk(int number, int count, byte[] data) =>
        JpegFixture.Segment(0xE2, [.. "ICC_PROFILE\0"u8, (byte)number, (byte)count, .. data]);

    private static byte[] Jpeg(params byte[][] segments) => JpegFixture.Build(Join(segments));

    /// <summary>An iCCP chunk: the name, a zero, the compression method, the profile deflated.</summary>
    private static byte[] Iccp(byte[] profile, string name = "icc", byte method = 0) =>
        PngFixture.Chunk("iCCP", [.. System.Text.Encoding.Latin1.GetBytes(name), 0, method, .. PngFixture.Compress(profile)]);

    private static void Put16(byte[] to, int at, int value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(to.AsSpan(at), (ushort)value);
        else BinaryPrimitives.WriteUInt16BigEndian(to.AsSpan(at), (ushort)value);
    }

    private static void Put32(byte[] to, int at, long value, bool little)
    {
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(to.AsSpan(at), (uint)value);
        else BinaryPrimitives.WriteUInt32BigEndian(to.AsSpan(at), (uint)value);
    }

    /// <summary>
    /// A TIFF of one directory at byte 8 with two entries, the image width and the profile tag; the
    /// link to the next directory is at byte 34 and the bytes of the profile start at byte 38.
    /// </summary>
    private static byte[] Tiff(bool little, byte[] profile, int tag = 0x8773, int type = 7, long? count = null, long? offset = null)
    {
        byte[] file = new byte[38 + profile.Length];
        (little ? "II*\0"u8 : "MM\0*"u8).CopyTo(file);
        Put32(file, 4, 8, little);
        Put16(file, 8, 2, little);
        Put16(file, 10, 0x0100, little);
        Put16(file, 12, 4, little);
        Put32(file, 14, 1, little);
        Put32(file, 18, 1, little);
        Put16(file, 22, tag, little);
        Put16(file, 24, type, little);
        Put32(file, 26, count ?? profile.Length, little);
        Put32(file, 30, offset ?? 38, little);
        profile.CopyTo(file, 38);
        return file;
    }

    private static MetadataErrorCode Refusal(byte[] file, MetadataLimits? limits = null) =>
        Assert.Throws<MetadataException>(() => IccProfiles.Read(file, limits)).Code;

    // ---- the header ----

    [Fact]
    public void AProfileIsReturnedAsTheBytesItsHeaderDeclares()
    {
        byte[] profile = Profile(560);

        Assert.Equal(profile, IccProfiles.Read(Jpeg(Chunk(1, 1, profile))));
    }

    [Fact]
    public void BytesAfterTheDeclaredLengthArePaddingAndAreNotReturned()
    {
        byte[] profile = Profile(200);

        Assert.Equal(profile, IccProfiles.Read(Jpeg(Chunk(1, 1, Join(profile, new byte[3])))));
    }

    [Fact]
    public void AHeaderShorterThanItsOwnLengthIsRefused()
    {
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Jpeg(Chunk(1, 1, Profile(200)[..127]))));
    }

    [Fact]
    public void ASizeFieldThatDoesNotFitTheBytesOfTheFileIsRefused()
    {
        byte[] longer = Profile(200), shorter = Profile(200);
        BinaryPrimitives.WriteUInt32BigEndian(longer, 201);
        BinaryPrimitives.WriteUInt32BigEndian(shorter, 127);

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Jpeg(Chunk(1, 1, longer))));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Jpeg(Chunk(1, 1, shorter))));
    }

    [Fact]
    public void AHeaderWithoutTheSignatureIsRefused()
    {
        byte[] profile = Profile(200);
        profile[39] = (byte)'q';

        MetadataException refusal = Assert.Throws<MetadataException>(() => IccProfiles.Read(Jpeg(Chunk(1, 1, profile))));

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, refusal.Code);
    }

    // ---- JPEG ----

    [Fact]
    public void TheSegmentsOfAJpegAreJoinedByNumberInWhateverOrderTheFileHasThem()
    {
        // More than one segment holds: 65,519 bytes of data is the most beside the 14 of the identifier.
        byte[] profile = Profile(150_000);
        byte[] first = profile[..65_519], second = profile[65_519..131_038], third = profile[131_038..];

        Assert.Equal(profile, IccProfiles.Read(Jpeg(Chunk(1, 3, first), Chunk(2, 3, second), Chunk(3, 3, third))));
        Assert.Equal(profile, IccProfiles.Read(Jpeg(Chunk(2, 3, second), Chunk(3, 3, third), Chunk(1, 3, first))));
    }

    [Fact]
    public void AMissingSegmentIsRefused()
    {
        byte[] profile = Profile(400);

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Jpeg(Chunk(1, 2, profile[..200]))));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Jpeg(Chunk(1, 3, profile[..200]), Chunk(3, 3, profile[200..]))));
    }

    [Fact]
    public void ARepeatedSegmentIsRefused()
    {
        byte[] profile = Profile(400);

        Assert.Equal(MetadataErrorCode.InvalidIccProfile,
            Refusal(Jpeg(Chunk(1, 2, profile[..200]), Chunk(1, 2, profile[..200]), Chunk(2, 2, profile[200..]))));
        // The same with nothing in the segment: what is repeated is the number, not the bytes.
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Jpeg(Chunk(1, 2, []), Chunk(1, 2, []), Chunk(2, 2, profile))));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    public void ASegmentNumberedOutsideItsCountIsRefused(int number, int count)
    {
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Jpeg(Chunk(number, count, Profile(200)))));
    }

    [Fact]
    public void SegmentsThatDisagreeAboutTheCountAreRefused()
    {
        byte[] profile = Profile(400);

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Jpeg(Chunk(1, 2, profile[..200]), Chunk(2, 3, profile[200..]))));
    }

    [Fact]
    public void AnEmptySegmentTakesItsPlaceAmongTheOthers()
    {
        byte[] profile = Profile(300);

        Assert.Equal(profile, IccProfiles.Read(Jpeg(Chunk(1, 3, profile[..100]), Chunk(2, 3, []), Chunk(3, 3, profile[100..]))));
    }

    [Fact]
    public void AnotherUseOfTheSameMarkerIsNotAProfile()
    {
        byte[] other = JpegFixture.Segment(0xE2, [.. "MPF\0"u8, 1, 1, 0, 0]);

        Assert.Null(IccProfiles.Read(Jpeg(other)));
        Assert.Equal(Profile(200), IccProfiles.Read(Jpeg(other, Chunk(1, 1, Profile(200)))));
    }

    [Fact]
    public void AnExvFileIsFramedAsAJpegIs()
    {
        byte[] profile = Profile(256);
        byte[] file = Join([0xFF, 0x01, .. "Exiv2"u8], Chunk(1, 1, profile), [0xFF, 0xD9]);

        Assert.Equal(profile, IccProfiles.Read(file));
    }

    // ---- PNG ----

    [Fact]
    public void TheProfileOfAPngIsInflatedFromItsChunk()
    {
        byte[] profile = Profile(3_000);

        Assert.Equal(profile, IccProfiles.Read(PngFixture.Build(Iccp(profile))));
        Assert.Equal(profile, IccProfiles.Read(PngFixture.Build(Iccp(profile, new string('n', 79)))));
        Assert.Null(IccProfiles.Read(PngFixture.Build()));
    }

    [Fact]
    public void AProfileChunkAfterTheImageDataIsNotRead()
    {
        Assert.Null(IccProfiles.Read(PngFixture.Build(after: Iccp(Profile(200)))));
    }

    [Fact]
    public void TwoProfileChunksInAPngAreRefusedAsAmbiguous()
    {
        byte[] file = PngFixture.Build(Join(Iccp(Profile(200)), Iccp(Profile(200, 2))));

        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Refusal(file));
    }

    [Fact]
    public void AProfileChunkThatFailsItsChecksumIsRefused()
    {
        byte[] chunk = Iccp(Profile(200));
        chunk[^1] ^= 1;

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(PngFixture.Build(chunk)));
    }

    [Fact]
    public void AProfileChunkWithoutANameOrWithAnotherCompressionMethodIsRefused()
    {
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(PngFixture.Build(Iccp(Profile(200), ""))));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(PngFixture.Build(Iccp(Profile(200), new string('n', 80)))));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(PngFixture.Build(Iccp(Profile(200), "icc", 1))));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(PngFixture.Build(PngFixture.Chunk("iCCP", [.. "icc"u8, 0]))));
    }

    [Fact]
    public void ACompressedProfileThatIsNotAWholeStreamIsRefused()
    {
        byte[] stream = PngFixture.Compress(Profile(2_000));
        byte[] cut = PngFixture.Chunk("iCCP", [.. "icc"u8, 0, 0, .. stream[..(stream.Length / 2)]]);
        byte[] plain = PngFixture.Chunk("iCCP", [.. "icc"u8, 0, 0, .. Profile(200)]);

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(PngFixture.Build(cut)));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(PngFixture.Build(plain)));
    }

    [Fact]
    public void AProfileThatInflatesBeyondTheBudgetIsRefusedBeforeItIsInflated()
    {
        // 200,000 bytes of one value deflate to a few hundred: the chunk is small and the profile is not.
        byte[] profile = new byte[200_000];
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
        "acsp"u8.CopyTo(profile.AsSpan(36));
        byte[] file = PngFixture.Build(Iccp(profile));

        Assert.True(file.Length < 4_000);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxValueBytes = 199_999 }));
        Assert.Equal(profile, IccProfiles.Read(file));
    }

    [Fact]
    public void WhatAPngInflatesToMustBeAProfile()
    {
        byte[] other = Profile(200);
        other[36] = (byte)'x';

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(PngFixture.Build(Iccp(other))));
    }

    // ---- TIFF ----

    [Theory]
    [InlineData(true, 7)]
    [InlineData(false, 7)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    public void TheProfileOfATiffIsTheValueOfItsTagInTheFirstDirectory(bool little, int type)
    {
        byte[] profile = Profile(1_352);

        Assert.Equal(profile, IccProfiles.Read(Tiff(little, profile, type: type)));
    }

    [Fact]
    public void ATiffWithoutTheTagHasNoProfile()
    {
        Assert.Null(IccProfiles.Read(Tiff(true, Profile(200), tag: 0x8774)));
    }

    [Fact]
    public void ATiffStructureUnderTheIdentifierOfARawFormatIsReadTheSameWay()
    {
        byte[] profile = Profile(200);
        byte[] file = Tiff(true, profile);
        "IIRO"u8.CopyTo(file);

        Assert.Equal(profile, IccProfiles.Read(file));
    }

    [Fact]
    public void AProfileTagOfAnotherTypeIsRefused()
    {
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Tiff(true, Profile(200), type: 2)));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Tiff(false, Profile(200), type: 4)));
    }

    [Fact]
    public void AProfileTagThatPointsOutsideTheFileIsRefused()
    {
        byte[] profile = Profile(200);

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Tiff(true, profile, offset: 39)));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Tiff(true, profile, count: 201)));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Tiff(false, profile, offset: 0xFFFFFFF0)));
    }

    [Fact]
    public void AProfileTagWhoseValueIsInTheEntryIsRefused()
    {
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(Tiff(true, Profile(200), count: 4)));
    }

    [Fact]
    public void ATiffProfileAboveTheBudgetAndADirectoryAboveItsOwnAreRefused()
    {
        byte[] file = Tiff(true, Profile(2_000));

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxValueBytes = 1_999 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxEntriesPerDirectory = 1 }));
        Assert.NotNull(IccProfiles.Read(file, new MetadataLimits { MaxEntriesPerDirectory = 2 }));
    }

    [Fact]
    public void ATiffWhoseFirstDirectoryLeavesTheFileHasNoProfile()
    {
        byte[] file = Tiff(true, Profile(200));
        Put32(file, 4, file.Length - 1, true);
        Assert.Null(IccProfiles.Read(file));

        file = Tiff(true, Profile(200));
        Put16(file, 8, 400, true);
        Assert.Null(IccProfiles.Read(file));
    }

    // ---- WebP ----

    [Fact]
    public void TheProfileOfAWebPIsItsChunk()
    {
        byte[] even = Profile(560), odd = Profile(561);
        byte[] image = WebPFixture.Chunk("VP8L", [0x2F, 0, 0, 0, 0]);

        Assert.Equal(even, IccProfiles.Read(WebPFixture.Extended(0x20, WebPFixture.Chunk("ICCP", even), image)));
        // A chunk of odd length is followed by a padding byte, which is not part of the profile.
        Assert.Equal(odd, IccProfiles.Read(WebPFixture.Extended(0x20, WebPFixture.Chunk("ICCP", odd), image)));
        Assert.Null(IccProfiles.Read(WebPFixture.Extended(0, image)));
        Assert.Null(IccProfiles.Read(WebPFixture.Image(0)));
    }

    [Fact]
    public void TwoProfileChunksInAWebPAreRefusedAsAmbiguous()
    {
        byte[] file = WebPFixture.Extended(0x20, WebPFixture.Chunk("ICCP", Profile(200)), WebPFixture.Chunk("ICCP", Profile(200)));

        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Refusal(file));
    }

    [Fact]
    public void AWebPChunkThatIsNotAProfileAndOneAboveTheBudgetAreRefused()
    {
        byte[] file = WebPFixture.Extended(0x20, WebPFixture.Chunk("ICCP", Profile(2_000)));

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(WebPFixture.Extended(0x20, WebPFixture.Chunk("ICCP", new byte[200]))));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxValueBytes = 1_999 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxSegments = 1 }));
    }

    [Fact]
    public void AWebPChunkThatLeavesTheFileEndsTheWalk()
    {
        byte[] file = WebPFixture.Extended(0x20, WebPFixture.Chunk("ICCP", Profile(200)));

        Assert.Null(IccProfiles.Read(file[..^1]));
    }

    // ---- no profile, and the budgets ----

    [Fact]
    public void AFileWithoutAProfileAndAFileOfAnUnknownKindHaveNone()
    {
        Assert.Null(IccProfiles.Read(JpegFixture.Build()));
        Assert.Null(IccProfiles.Read(Profile(200)));
        Assert.Null(IccProfiles.Read([]));
    }

    [Fact]
    public void AProfileAboveTheBudgetOfAValueIsRefused()
    {
        byte[] file = Jpeg(Chunk(1, 1, Profile(2_000)));

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxValueBytes = 1_999 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxRetainedMetadataBytes = 1_999 }));
        Assert.Equal(2_000, IccProfiles.Read(file, new MetadataLimits { MaxValueBytes = 2_000 })!.Length);
    }

    [Fact]
    public void AFileAboveTheInputBudgetAndASegmentCountAboveItsOwnAreRefused()
    {
        byte[] file = Jpeg(Chunk(1, 1, Profile(200)));

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxInputBytes = file.Length - 1 }));
        // The fixture has the profile, two tables and a frame header before the scan.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxSegments = 3 }));
        Assert.NotNull(IccProfiles.Read(file, new MetadataLimits { MaxSegments = 4 }));
    }
}
