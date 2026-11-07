using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Values;

public sealed class RationalTests
{
    [Theory]
    [InlineData(0U, 1U, 0.0)]
    [InlineData(1U, 2U, 0.5)]
    [InlineData(uint.MaxValue, 1U, 4294967295.0)]
    [InlineData(uint.MaxValue, uint.MaxValue, 1.0)]
    public void UnsignedConversionUsesFloatingPointDivision(uint n, uint d, double expected)
    {
        var value = new UnsignedRational(n, d);
        Assert.True(value.TryGetDouble(out double actual));
        Assert.Equal(expected, actual);
        Assert.Equal(n, value.Numerator);
        Assert.Equal(d, value.Denominator);
    }

    [Theory]
    [InlineData(1, -2, -0.5)]
    [InlineData(-1, -2, 0.5)]
    [InlineData(int.MinValue, -1, 2147483648.0)]
    [InlineData(int.MinValue, int.MinValue, 1.0)]
    [InlineData(int.MaxValue, 1, 2147483647.0)]
    public void SignedConversionNeverPerformsOverflowingIntegerDivision(int n, int d, double expected)
    {
        var value = new SignedRational(n, d);
        Assert.True(value.TryGetDouble(out double actual));
        Assert.Equal(expected, actual);
        Assert.Equal(n, value.Numerator);
        Assert.Equal(d, value.Denominator);
    }

    [Fact]
    public void ZeroDenominatorsRemainReadableAndConversionFailsWithZero()
    {
        foreach (uint n in new uint[] { 0, 1, uint.MaxValue })
        {
            var value = new UnsignedRational(n, 0);
            Assert.False(value.TryGetDouble(out double actual));
            Assert.Equal(0.0, actual);
            Assert.Equal(n, value.Numerator);
        }
        foreach (int n in new[] { int.MinValue, 0, int.MaxValue })
        {
            var value = new SignedRational(n, 0);
            Assert.False(value.TryGetDouble(out double actual));
            Assert.Equal(0.0, actual);
            Assert.Equal(n, value.Numerator);
        }
        Assert.False(default(SignedRational).TryGetDouble(out _));
        Assert.False(default(UnsignedRational).TryGetDouble(out _));
    }

    [Fact]
    public void EqualityIsExactPairEqualityNotMathematicalEquivalence()
    {
        Assert.Equal(new UnsignedRational(2, 4), new UnsignedRational(2, 4));
        Assert.NotEqual(new UnsignedRational(1, 2), new UnsignedRational(2, 4));
        Assert.Equal(new SignedRational(-2, -4), new SignedRational(-2, -4));
        Assert.NotEqual(new SignedRational(1, 2), new SignedRational(-1, -2));
        Assert.Equal(new SignedRational(1, 0), new SignedRational(1, 0));
        Assert.Equal(new UnsignedRational(2, 4).GetHashCode(), new UnsignedRational(2, 4).GetHashCode());
    }
}
