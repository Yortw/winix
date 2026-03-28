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
            CpuTime: TimeSpan.FromSeconds(9.1),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: false);

        Assert.Contains("real", output);
        Assert.Contains("12.4s", output);
        Assert.Contains("cpu", output);
        Assert.Contains("9.1s", output);
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
            CpuTime: TimeSpan.FromSeconds(0.5),
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: true);

        // Should contain ANSI escape sequences
        Assert.Contains("\x1b[", output);
    }

    [Fact]
    public void FormatDefault_FailedExitCode_WithColor_ContainsRedAnsi()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            CpuTime: TimeSpan.FromSeconds(0.5),
            PeakMemoryBytes: 1_048_576,
            ExitCode: 1
        );

        string output = Formatting.FormatDefault(result, useColor: true);

        // Red ANSI code for non-zero exit
        Assert.Contains("\x1b[31m", output);
    }

    [Fact]
    public void FormatDefault_ZeroPeakMemory_ShowsNotAvailable()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            CpuTime: TimeSpan.FromSeconds(0.5),
            PeakMemoryBytes: 0,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: false);

        Assert.Contains("N/A", output);
        Assert.DoesNotContain("0 KB", output);
    }
}

public class FormatOneLineTests
{
    [Fact]
    public void FormatOneLine_ProducesExpectedFormat()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            CpuTime: TimeSpan.FromSeconds(9.1),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatOneLine(result, useColor: false);

        Assert.Equal("[timeit] 12.4s wall | 9.1s cpu | 482 MB peak | exit 0", output);
    }

    [Fact]
    public void FormatOneLine_ZeroPeakMemory_ShowsNotAvailable()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            CpuTime: TimeSpan.FromSeconds(0.5),
            PeakMemoryBytes: 0,
            ExitCode: 0
        );

        string output = Formatting.FormatOneLine(result, useColor: false);

        Assert.Contains("N/A", output);
    }
}

public class FormatJsonTests
{
    [Fact]
    public void FormatJson_ProducesValidJson()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            CpuTime: TimeSpan.FromSeconds(9.1),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatJson(result);

        Assert.Contains("\"wall_seconds\":", output);
        Assert.Contains("\"cpu_seconds\":", output);
        Assert.Contains("\"peak_memory_bytes\":505413632", output);
        Assert.Contains("\"exit_code\":0", output);
    }

    [Fact]
    public void FormatJson_ZeroPeakMemory_OutputsNull()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            CpuTime: TimeSpan.FromSeconds(0.5),
            PeakMemoryBytes: 0,
            ExitCode: 0
        );

        string output = Formatting.FormatJson(result);

        Assert.Contains("\"peak_memory_bytes\":null", output);
    }
}
