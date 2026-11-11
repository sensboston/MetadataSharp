using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Tests.Jpeg;
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
