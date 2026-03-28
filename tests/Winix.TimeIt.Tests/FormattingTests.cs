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

public class FormatDefaultTests
{
    [Fact]
    public void FormatDefault_WithSuccessExitCode_FormatsCorrectly()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            UserCpuTime: TimeSpan.FromSeconds(9.1),
            SystemCpuTime: TimeSpan.FromSeconds(0.3),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: false);

        Assert.Contains("real", output);
        Assert.Contains("12.4s", output);
        Assert.Contains("user", output);
        Assert.Contains("9.1s", output);
        Assert.Contains("sys", output);
        Assert.Contains("0.300s", output);
        Assert.Contains("peak", output);
        Assert.Contains("482 MB", output);
        Assert.Contains("exit", output);
        Assert.Contains("0", output);
    }

    [Fact]
    public void FormatDefault_WithColor_ContainsAnsiSequences()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: TimeSpan.FromSeconds(0.4),
            SystemCpuTime: TimeSpan.FromSeconds(0.1),
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: true);

        Assert.Contains("\x1b[", output);
    }

    [Fact]
    public void FormatDefault_FailedExitCode_WithColor_ContainsRedAnsi()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: TimeSpan.FromSeconds(0.4),
            SystemCpuTime: TimeSpan.FromSeconds(0.1),
            PeakMemoryBytes: 1_048_576,
            ExitCode: 1
        );

        string output = Formatting.FormatDefault(result, useColor: true);

        Assert.Contains("\x1b[31m", output);
    }

    [Fact]
    public void FormatDefault_NullPeakMemory_ShowsNotAvailable()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: TimeSpan.FromSeconds(0.4),
            SystemCpuTime: TimeSpan.FromSeconds(0.1),
            PeakMemoryBytes: null,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: false);

        Assert.Contains("N/A", output);
    }

    [Fact]
    public void FormatDefault_NullCpuTimes_ShowsNotAvailable()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: null,
            SystemCpuTime: null,
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: false);

        string[] lines = output.Split('\n');
        string userLine = lines.First(l => l.Contains("user"));
        string sysLine = lines.First(l => l.Contains("sys"));
        Assert.Contains("N/A", userLine);
        Assert.Contains("N/A", sysLine);
    }

    [Fact]
    public void FormatDefault_ZeroCpuTimes_ShowsZero()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: TimeSpan.Zero,
            SystemCpuTime: TimeSpan.Zero,
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: false);

        Assert.DoesNotContain("N/A", output);
        Assert.Contains("0.000s", output);
    }
}

public class FormatOneLineTests
{
    [Fact]
    public void FormatOneLine_ProducesExpectedFormat()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            UserCpuTime: TimeSpan.FromSeconds(9.1),
            SystemCpuTime: TimeSpan.FromSeconds(0.3),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatOneLine(result, useColor: false);

        Assert.Equal("[timeit] 12.4s wall | 9.1s user | 0.300s sys | 482 MB peak | exit 0", output);
    }

    [Fact]
    public void FormatOneLine_NullPeakMemory_ShowsNotAvailable()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: TimeSpan.FromSeconds(0.4),
            SystemCpuTime: TimeSpan.FromSeconds(0.1),
            PeakMemoryBytes: null,
            ExitCode: 0
        );

        string output = Formatting.FormatOneLine(result, useColor: false);

        Assert.Contains("N/A peak", output);
    }

    [Fact]
    public void FormatOneLine_NullCpuTimes_ShowsNotAvailable()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: null,
            SystemCpuTime: null,
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        string output = Formatting.FormatOneLine(result, useColor: false);

        Assert.Contains("N/A user", output);
        Assert.Contains("N/A sys", output);
    }
}

public class FormatJsonTests
{
    [Fact]
    public void FormatJson_IncludesStandardFields()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            UserCpuTime: TimeSpan.FromSeconds(9.1),
            SystemCpuTime: TimeSpan.FromSeconds(0.3),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatJson(result, "timeit", "0.1.0");

        Assert.Contains("\"tool\":\"timeit\"", output);
        Assert.Contains("\"version\":\"0.1.0\"", output);
        Assert.Contains("\"exit_code\":0", output);
        Assert.Contains("\"exit_reason\":\"success\"", output);
        Assert.Contains("\"child_exit_code\":0", output);
    }

    [Fact]
    public void FormatJson_IncludesMetrics()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            UserCpuTime: TimeSpan.FromSeconds(9.1),
            SystemCpuTime: TimeSpan.FromSeconds(0.3),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatJson(result, "timeit", "0.1.0");

        Assert.Contains("\"wall_seconds\":", output);
        Assert.Contains("\"user_cpu_seconds\":9.100", output);
        Assert.Contains("\"sys_cpu_seconds\":0.300", output);
        Assert.Contains("\"cpu_seconds\":9.400", output);
        Assert.Contains("\"peak_memory_bytes\":505413632", output);
    }

    [Fact]
    public void FormatJson_ChildExitCodePassesThrough()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: TimeSpan.FromSeconds(0.5),
            SystemCpuTime: TimeSpan.FromSeconds(0.1),
            PeakMemoryBytes: 1_048_576,
            ExitCode: 42
        );

        string output = Formatting.FormatJson(result, "timeit", "0.1.0");

        // Tool's exit_code is always 0 (success) when formatter is reached
        Assert.Contains("\"exit_code\":0", output);
        // Child's exit code is separate
        Assert.Contains("\"child_exit_code\":42", output);
    }

    [Fact]
    public void FormatJson_NullPeakMemory_OutputsNull()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: TimeSpan.FromSeconds(0.4),
            SystemCpuTime: TimeSpan.FromSeconds(0.1),
            PeakMemoryBytes: null,
            ExitCode: 0
        );

        string output = Formatting.FormatJson(result, "timeit", "0.1.0");

        Assert.Contains("\"peak_memory_bytes\":null", output);
    }

    [Fact]
    public void FormatJson_NullCpuTimes_OutputsNull()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: null,
            SystemCpuTime: null,
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        string output = Formatting.FormatJson(result, "timeit", "0.1.0");

        Assert.Contains("\"user_cpu_seconds\":null", output);
        Assert.Contains("\"sys_cpu_seconds\":null", output);
        Assert.Contains("\"cpu_seconds\":null", output);
    }

    [Fact]
    public void FormatJson_ZeroPeakMemory_OutputsZero()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            UserCpuTime: TimeSpan.FromSeconds(0.4),
            SystemCpuTime: TimeSpan.FromSeconds(0.1),
            PeakMemoryBytes: 0,
            ExitCode: 0
        );

        string output = Formatting.FormatJson(result, "timeit", "0.1.0");

        Assert.Contains("\"peak_memory_bytes\":0", output);
    }
}

public class FormatJsonErrorTests
{
    [Fact]
    public void FormatJsonError_CommandNotFound()
    {
        string output = Formatting.FormatJsonError(127, "command_not_found", "timeit", "0.1.0");

        Assert.Contains("\"tool\":\"timeit\"", output);
        Assert.Contains("\"version\":\"0.1.0\"", output);
        Assert.Contains("\"exit_code\":127", output);
        Assert.Contains("\"exit_reason\":\"command_not_found\"", output);
        Assert.Contains("\"child_exit_code\":null", output);
    }

    [Fact]
    public void FormatJsonError_CommandNotExecutable()
    {
        string output = Formatting.FormatJsonError(126, "command_not_executable", "timeit", "0.1.0");

        Assert.Contains("\"exit_code\":126", output);
        Assert.Contains("\"exit_reason\":\"command_not_executable\"", output);
    }

    [Fact]
    public void FormatJsonError_UsageError()
    {
        string output = Formatting.FormatJsonError(125, "usage_error", "timeit", "0.1.0");

        Assert.Contains("\"exit_code\":125", output);
        Assert.Contains("\"exit_reason\":\"usage_error\"", output);
    }
}

public class TotalCpuTimeTests
{
    [Fact]
    public void TotalCpuTime_BothPresent_ReturnSum()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(10.0),
            UserCpuTime: TimeSpan.FromSeconds(7.0),
            SystemCpuTime: TimeSpan.FromSeconds(2.0),
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        Assert.Equal(TimeSpan.FromSeconds(9.0), result.TotalCpuTime);
    }

    [Fact]
    public void TotalCpuTime_UserNull_ReturnsNull()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(10.0),
            UserCpuTime: null,
            SystemCpuTime: TimeSpan.FromSeconds(2.0),
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        Assert.Null(result.TotalCpuTime);
    }

    [Fact]
    public void TotalCpuTime_SystemNull_ReturnsNull()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(10.0),
            UserCpuTime: TimeSpan.FromSeconds(7.0),
            SystemCpuTime: null,
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        Assert.Null(result.TotalCpuTime);
    }

    [Fact]
    public void TotalCpuTime_BothNull_ReturnsNull()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(10.0),
            UserCpuTime: null,
            SystemCpuTime: null,
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        Assert.Null(result.TotalCpuTime);
    }
}
