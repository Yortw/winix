using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

public class HumanSizeTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(999, "999")]
    [InlineData(1023, "1,023")]
    public void Format_ByteRange_ShowsPlainBytes(long bytes, string expected)
    {
        Assert.Equal(expected, HumanSize.Format(bytes));
    }

    [Theory]
    [InlineData(1024, "1.0K")]
    [InlineData(1536, "1.5K")]
    [InlineData(10240, "10.0K")]
    [InlineData(1048575, "1024.0K")]
    public void Format_KilobyteRange_ShowsK(long bytes, string expected)
    {
        Assert.Equal(expected, HumanSize.Format(bytes));
    }

    [Theory]
    [InlineData(1048576, "1.0M")]
    [InlineData(5242880, "5.0M")]
    [InlineData(1073741823, "1024.0M")]
    public void Format_MegabyteRange_ShowsM(long bytes, string expected)
    {
        Assert.Equal(expected, HumanSize.Format(bytes));
    }

    [Theory]
    [InlineData(1073741824, "1.0G")]
    [InlineData(5368709120, "5.0G")]
    public void Format_GigabyteRange_ShowsG(long bytes, string expected)
    {
        Assert.Equal(expected, HumanSize.Format(bytes));
    }

    [Fact]
    public void Format_NegativeOne_ReturnsDash()
    {
        Assert.Equal("-", HumanSize.Format(-1));
    }

    [Theory]
    [InlineData(0, 5, "    0")]
    [InlineData(1024, 6, "  1.0K")]
    public void FormatPadded_RightAligns(long bytes, int width, string expected)
    {
        Assert.Equal(expected, HumanSize.FormatPadded(bytes, width));
    }
}
