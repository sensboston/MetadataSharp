using MetadataSharp.Core.Binary;
using Xunit;

namespace MetadataSharp.Tests.Binary;

public sealed class CheckedRangeTests
{
    [Theory]
    [InlineData(0L, 0L, 0, true)]
    [InlineData(8L, 0L, 8, true)]
    [InlineData(2L, 6L, 8, true)]
    [InlineData(8L, 1L, 8, false)]
    [InlineData(-1L, 1L, 8, false)]
    [InlineData(0L, -1L, 8, false)]
    [InlineData(0L, 0L, -1, false)]
    [InlineData(long.MaxValue, long.MaxValue, 8, false)]
    [InlineData(0L, 2147483648L, int.MaxValue, false)]
    [InlineData(2147483647L, 0L, int.MaxValue, true)]
    public void SpanRangeChecksBeforeNarrowing(long offset, long length, int total, bool valid)
    {
        Assert.Equal(valid, CheckedRange.TryGetSpanRange(offset, length, total, out int start, out int count));
        Assert.Equal(valid ? (int)offset : 0, start);
        Assert.Equal(valid ? (int)length : 0, count);
    }

    [Theory]
    [InlineData(0U, 8, true, 0)]
    [InlineData(3U, 8, true, 24)]
    [InlineData(2147483647U, 1, true, int.MaxValue)]
    [InlineData(2147483648U, 1, false, 0)]
    [InlineData(uint.MaxValue, 8, false, 0)]
    [InlineData(1U, 0, false, 0)]
    [InlineData(1U, -1, false, 0)]
    public void ProductsAreBounded(uint count, int width, bool valid, int expected)
    {
        Assert.Equal(valid, CheckedRange.TryMultiply(count, width, out int length));
        Assert.Equal(expected, length);
    }

    [Theory]
    [InlineData(0L, 0L, true, 0L)]
    [InlineData(long.MaxValue, 0L, true, long.MaxValue)]
    [InlineData(long.MaxValue, 1L, false, 0L)]
    [InlineData(-1L, 0L, false, 0L)]
    [InlineData(0L, -1L, false, 0L)]
    public void AdditionCannotWrap(long offset, long length, bool valid, long expected)
    {
        Assert.Equal(valid, CheckedRange.TryAdd(offset, length, out long end));
        Assert.Equal(expected, end);
    }
}
