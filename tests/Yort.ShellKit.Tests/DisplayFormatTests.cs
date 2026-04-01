using Xunit;
using Yort.ShellKit;

namespace Yort.ShellKit.Tests;

public class DisplayFormatBytesTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(500L, "500 B")]
    [InlineData(1_023L, "1023 B")]
    [InlineData(1_024L, "1.0 KB")]
    [InlineData(1_536L, "1.5 KB")]
    [InlineData(393_216L, "384.0 KB")]
    [InlineData(524_288L, "512.0 KB")]
    [InlineData(999_424L, "976.0 KB")]
    [InlineData(1_048_576L, "1.0 MB")]
    [InlineData(1_572_864L, "1.5 MB")]
    [InlineData(505_413_632L, "482.0 MB")]
    [InlineData(1_073_741_824L, "1.0 GB")]
    [InlineData(2_469_606_195L, "2.3 GB")]
    public void FormatBytes_ProducesExpectedOutput(long bytes, string expected)
    {
        Assert.Equal(expected, DisplayFormat.FormatBytes(bytes));
    }

    [Fact]
    public void FormatBytes_NegativeValue_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayFormat.FormatBytes(-1));
    }
}

public class DisplayFormatDurationTests
{
    [Theory]
    [InlineData(0.0, "0.000s")]
    [InlineData(0.12, "0.120s")]
    [InlineData(0.842, "0.842s")]
    [InlineData(0.9999, "1.000s")]
    [InlineData(1.0, "1.0s")]
    [InlineData(12.4, "12.4s")]
    [InlineData(59.99, "60.0s")]
    [InlineData(60.0, "1m 00.0s")]
    [InlineData(87.1, "1m 27.1s")]
    [InlineData(207.1, "3m 27.1s")]
    [InlineData(3599.9, "59m 59.9s")]
    [InlineData(3600.0, "1h 00m 00s")]
    [InlineData(4323.0, "1h 12m 03s")]
    public void FormatDuration_ProducesExpectedOutput(double seconds, string expected)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        Assert.Equal(expected, DisplayFormat.FormatDuration(timeSpan));
    }

    [Fact]
    public void FormatDuration_NegativeValue_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayFormat.FormatDuration(TimeSpan.FromSeconds(-1)));
    }
}
