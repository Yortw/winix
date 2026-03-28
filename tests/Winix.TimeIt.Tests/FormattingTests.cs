using Xunit;
using Winix.TimeIt;

namespace Winix.TimeIt.Tests;

public class FormatDurationTests
{
    [Theory]
    [InlineData(0.0, "0.000s")]
    [InlineData(0.842, "0.842s")]
    [InlineData(0.9999, "1.000s")]
    [InlineData(1.0, "1.0s")]
    [InlineData(12.4, "12.4s")]
    [InlineData(59.99, "60.0s")]
    [InlineData(60.0, "1m 00.0s")]
    [InlineData(207.1, "3m 27.1s")]
    [InlineData(3599.9, "59m 59.9s")]
    [InlineData(3600.0, "1h 00m 00s")]
    [InlineData(4323.0, "1h 12m 03s")]
    public void FormatDuration_ProducesExpectedOutput(double seconds, string expected)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        Assert.Equal(expected, Formatting.FormatDuration(timeSpan));
    }
}

public class FormatBytesTests
{
    [Theory]
    [InlineData(0L, "0 KB")]
    [InlineData(393_216L, "384 KB")]
    [InlineData(999_424L, "976 KB")]
    [InlineData(1_048_576L, "1 MB")]
    [InlineData(505_413_632L, "482 MB")]
    [InlineData(1_073_741_824L, "1.0 GB")]
    [InlineData(2_469_606_195L, "2.3 GB")]
    public void FormatBytes_ProducesExpectedOutput(long bytes, string expected)
    {
        Assert.Equal(expected, Formatting.FormatBytes(bytes));
    }
}
