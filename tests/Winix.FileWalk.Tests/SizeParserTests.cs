#nullable enable

using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class SizeParserTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("1k", 1024)]
    [InlineData("1K", 1024)]
    [InlineData("10k", 10240)]
    [InlineData("1m", 1048576)]
    [InlineData("1M", 1048576)]
    [InlineData("1g", 1073741824)]
    [InlineData("1G", 1073741824)]
    [InlineData("512k", 524288)]
    public void Parse_ValidInput_ReturnsBytes(string input, long expected)
    {
        long result = SizeParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("k")]
    [InlineData("-1k")]
    [InlineData("1.5k")]
    [InlineData("1x")]
    public void Parse_InvalidInput_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => SizeParser.Parse(input));
    }

    [Fact]
    public void TryParse_ValidInput_ReturnsTrueAndValue()
    {
        bool ok = SizeParser.TryParse("10M", out long bytes);
        Assert.True(ok);
        Assert.Equal(10485760, bytes);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        bool ok = SizeParser.TryParse("bad", out long bytes);
        Assert.False(ok);
        Assert.Equal(0, bytes);
    }
}
