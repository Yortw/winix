using Winix.TimeIt;
using Xunit;

namespace Winix.TimeIt.Tests;

public class TimeItResultTests
{
    [Fact]
    public void Properties_ReturnConstructorValues()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            UserCpuTime: TimeSpan.FromSeconds(6.0),
            SystemCpuTime: TimeSpan.FromSeconds(3.1),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        Assert.Equal(TimeSpan.FromSeconds(12.4), result.WallTime);
        Assert.Equal(TimeSpan.FromSeconds(6.0), result.UserCpuTime);
        Assert.Equal(TimeSpan.FromSeconds(3.1), result.SystemCpuTime);
        Assert.Equal(TimeSpan.FromSeconds(9.1), result.TotalCpuTime);
        Assert.Equal(505_413_632, result.PeakMemoryBytes);
        Assert.Equal(0, result.ExitCode);
    }
}
